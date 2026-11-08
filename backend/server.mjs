// Professor Quack tutor backend: Unity -> POST /tutor -> Gemini (structured JSON) -> validated reply.
// Zero dependencies (Node >= 20: built-in fetch). The Gemini key never leaves this process.
//
//   node server.mjs           real Gemini (needs GEMINI_API_KEY in backend/.env)
//   node server.mjs --mock    deterministic replies, no network (tests / offline demo)

import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { RESPONSE_SCHEMA, validateLine, validateRequest } from "./schema.mjs";
import { SYSTEM_INSTRUCTION, buildUserPrompt } from "./prompt.mjs";
import { synthesize, TTS_MODEL, TTS_VOICE } from "./tts.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
loadEnv(path.join(here, ".env"));

const MOCK = process.argv.includes("--mock") || process.env.TUTOR_MOCK === "1";
const KEY = process.env.GEMINI_API_KEY || "";
const MODEL = process.env.GEMINI_MODEL || "gemini-3.6-flash";
const PORT = Number(process.env.PORT || 8787);
const BIND = process.env.BIND || "127.0.0.1";
const ORIGINS = (process.env.ALLOWED_ORIGINS || "").split(",").map((s) => s.trim()).filter(Boolean);
const TIMEOUT_MS = 15000;
const TTS_CACHE = path.join(here, "cache", "tts");
const TTS = { blockedUntil: 0 };   // after a quota error, stop calling TTS for a while (game stays text-only)

// The browser (WebGL) build is served from here, so game and tutor share one origin (no CORS needed).
const STATIC_DIR = path.resolve(process.env.STATIC_DIR || path.join(here, "..", "Builds", "WebGL"));
const TYPES = { ".html": "text/html; charset=utf-8", ".js": "application/javascript", ".wasm": "application/wasm",
  ".data": "application/octet-stream", ".json": "application/json", ".css": "text/css", ".png": "image/png",
  ".ico": "image/x-icon", ".symbols.json": "application/json" };

/** Serve a file from the WebGL build; ".gz" files get Content-Encoding so the browser inflates them. */
function serveStatic(urlPath, res) {
  if (!fs.existsSync(STATIC_DIR)) return false;
  let rel = decodeURIComponent(urlPath.split("?")[0]);
  if (rel === "/" || rel === "") rel = "/index.html";
  const file = path.resolve(STATIC_DIR, "." + rel);
  if (!file.startsWith(STATIC_DIR + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) return false;
  const gz = file.endsWith(".gz");
  const base = gz ? file.slice(0, -3) : file;
  const ext = path.extname(base);
  const headers = { "Content-Type": TYPES[ext] || "application/octet-stream", "Cache-Control": "no-cache" };
  if (gz) headers["Content-Encoding"] = "gzip";
  res.writeHead(200, headers);
  fs.createReadStream(file).pipe(res);
  return true;
}
const RATE = { perMinute: 30, hits: [] };
// Test hooks for the browser build (off unless TEST_SHOTS=1): the game uploads CANVAS screenshots and tour
// progress lines, because a browser game can't write files or expose its log.
const TEST_SHOTS = process.env.TEST_SHOTS === "1";
const SHOT_DIR = path.join(here, "..", "Logs", "browser");

function loadEnv(file) {
  if (!fs.existsSync(file)) return;
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/);
    if (m && !(m[1] in process.env)) process.env[m[1]] = m[2].replace(/^["']|["']$/g, "");
  }
}

function rateLimited() {
  const now = Date.now();
  RATE.hits = RATE.hits.filter((t) => now - t < 60000);
  if (RATE.hits.length >= RATE.perMinute) return true;
  RATE.hits.push(now);
  return false;
}

/** Call Gemini generateContent (REST) with structured JSON output. One retry on 429/5xx. */
export async function askGemini(req, { fetchImpl = fetch, key = KEY, model = MODEL } = {}) {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`;
  const body = {
    systemInstruction: { parts: [{ text: SYSTEM_INSTRUCTION }] },
    contents: [{ role: "user", parts: [{ text: buildUserPrompt(req) }, ...(req.audio ? [{ inlineData: { mimeType: "audio/wav", data: req.audio } }] : [])] }],
    generationConfig: {
      responseMimeType: "application/json",
      responseJsonSchema: RESPONSE_SCHEMA,
      temperature: 0.7,
      maxOutputTokens: 4096,   // newer models may spend part of the budget on internal reasoning
    },
  };
  let lastErr;
  for (let attempt = 0; attempt < 2; attempt++) {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS);
    try {
      const res = await fetchImpl(url, {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-goog-api-key": key },
        body: JSON.stringify(body),
        signal: ctrl.signal,
      });
      const text = await res.text();
      if (!res.ok) {
        lastErr = new Error(`gemini http ${res.status}: ${text.slice(0, 300)}`);
        if (res.status === 429 || res.status >= 500) { await new Promise((r) => setTimeout(r, 1000)); continue; }
        throw lastErr;
      }
      const data = JSON.parse(text);
      const cand = data?.candidates?.[0];
      const part = cand?.content?.parts?.find((p) => typeof p.text === "string");
      if (!part) throw new Error(`no text in reply (finishReason=${cand?.finishReason})`);
      try { return JSON.parse(part.text); }
      catch (e) { throw new Error(`unparseable JSON (finishReason=${cand?.finishReason}, ${part.text.length} chars): ${e.message}`); }
    } catch (e) {
      lastErr = e.name === "AbortError" ? new Error("gemini timeout") : e;
      if (attempt === 0 && /timeout|ECONN|fetch failed/i.test(lastErr.message)) continue;
      throw lastErr;
    } finally {
      clearTimeout(timer);
    }
  }
  throw lastErr;
}

const TRANSCRIBE_MODEL = process.env.TRANSCRIBE_MODEL || "gemini-3.5-flash-lite";

/** Fast verbatim transcript (no tutoring) for live captions while the learner is still speaking. */
export async function transcribe(audioB64, { fetchImpl = fetch, key = KEY, model = TRANSCRIBE_MODEL } = {}) {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`;
  const body = {
    contents: [{ role: "user", parts: [
      { text: "Transcribe this speech verbatim. Output only the words spoken, no commentary. If there is no speech, output nothing." },
      { inlineData: { mimeType: "audio/wav", data: audioB64 } }] }],
    generationConfig: { temperature: 0, maxOutputTokens: 400 },
  };
  const ctrl = new AbortController(); const timer = setTimeout(() => ctrl.abort(), 12000);
  try {
    const res = await fetchImpl(url, { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": key }, body: JSON.stringify(body), signal: ctrl.signal });
    const raw = await res.text();
    if (!res.ok) throw new Error(`transcribe http ${res.status}: ${raw.slice(0, 160)}`);
    const data = JSON.parse(raw);
    return (data?.candidates?.[0]?.content?.parts || []).map((p) => p.text || "").join("").trim().slice(0, 600);
  } finally { clearTimeout(timer); }
}

/** Deterministic reply built from context — used for --mock and tests. */
export function mockReply({ trigger, question, context, audio }) {
  if (trigger === "voice") return { heard: "(mock) what does the arrow mean?", speech: "Mock tutor: I heard you. The arrow points to the JUMBO side.", emotion: "curious", gesture: "point_at_plot", after_speech: "nod", gaze_target: "learner", intensity: 0.4, duck_behavior: "none" };
  const egg = context?.selected_egg;
  const acc = context?.training?.accuracy;
  let speech;
  if (egg) speech = `Mock tutor: egg #${egg.id} has p(JUMBO) = ${Number(egg.p_jumbo).toFixed(2)}. Which side of the sheet is it on?`;
  else if (Number.isFinite(acc)) speech = `Mock tutor: your rule routes ${Math.round(acc * 100)}% of training eggs correctly. Pick a misrouted egg to investigate.`;
  else speech = `Mock tutor received a ${trigger}${question ? `: "${question.slice(0, 60)}"` : ""}.`;
  return { speech, emotion: "curious", gesture: "point_at_plot", after_speech: "nod", gaze_target: egg ? "selected_egg" : "plot", intensity: 0.4, duck_behavior: "none" };
}

function send(res, status, obj, origin) {
  const headers = { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" };
  if (origin && ORIGINS.includes(origin)) {
    headers["Access-Control-Allow-Origin"] = origin;
    headers["Access-Control-Allow-Headers"] = "Content-Type";
    headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";
  }
  res.writeHead(status, headers);
  res.end(JSON.stringify(obj));
}

export function createServer({ mock = MOCK, ask = askGemini } = {}) {
  return http.createServer(async (req, res) => {
    const origin = req.headers.origin;
    if (req.method === "OPTIONS") return send(res, 204, {}, origin);
    const urlPath = (req.url || "/").split("?")[0];
    if (req.method === "GET" && urlPath === "/health") {
      const from = new URL(req.url, "http://x").searchParams.get("from");
      if (from) console.log(`[health] ping from ${from}`);
      return send(res, 200, { ok: true, mode: mock ? "mock" : "gemini", model: mock ? null : MODEL }, origin);
    }
    if (req.method === "GET" && serveStatic(urlPath, res)) return;
    // ---- live transcript while the learner holds to talk: POST /transcribe {audio} -> {text} ----
    if (req.method === "POST" && urlPath === "/transcribe") {
      let raw = "";
      for await (const chunk of req) { raw += chunk; if (raw.length > 6_200_000) return send(res, 413, { error: "too large" }, origin); }
      let audio;
      try { audio = JSON.parse(raw).audio; } catch { return send(res, 400, { error: "bad json" }, origin); }
      if (typeof audio !== "string" || audio.length < 100) return send(res, 400, { error: "audio required" }, origin);
      if (mock || req.headers["x-quack-mock"] === "1") return send(res, 200, { text: "(mock) live transcript" }, origin);
      try {
        const text = await transcribe(audio);
        return send(res, 200, { text }, origin);
      } catch (e) {
        console.warn(`[transcribe] FAILED: ${e.message.slice(0, 160)}`);
        return send(res, 503, { error: "transcribe unavailable" }, origin);
      }
    }
    // ---- voice: POST /tts {text} -> {id, duration, envelope}; GET /tts-audio/<id>.wav ----
    if (req.method === "GET" && urlPath.startsWith("/tts-audio/")) {
      const id = urlPath.slice("/tts-audio/".length).replace(/\.wav$/, "");
      if (!/^[0-9a-f]{24}$/.test(id)) return send(res, 404, { error: "not found" }, origin);
      const f = path.join(TTS_CACHE, id + ".wav");
      if (!fs.existsSync(f)) return send(res, 404, { error: "not found" }, origin);
      res.writeHead(200, { "Content-Type": "audio/wav", "Cache-Control": "public, max-age=86400" });
      return fs.createReadStream(f).pipe(res);
    }
    if (req.method === "POST" && urlPath === "/tts") {
      let raw = "";
      for await (const chunk of req) { raw += chunk; if (raw.length > 8000) return send(res, 413, { error: "too large" }, origin); }
      let text;
      try { text = String(JSON.parse(raw).text || "").replace(/\[[^\]]*\]/g, "").trim().slice(0, 600); } catch { return send(res, 400, { error: "bad json" }, origin); }
      if (!text) return send(res, 400, { error: "empty text" }, origin);
      if (mock || req.headers["x-quack-mock"] === "1") return send(res, 503, { error: "no voice in mock mode" }, origin);
      if (Date.now() < TTS.blockedUntil) return send(res, 503, { error: "voice paused (quota)" }, origin);
      const t0 = Date.now();
      try {
        const r = await synthesize(text, { key: KEY, cacheDir: TTS_CACHE });
        console.log(`[tts] ${r.cached ? "cache" : "new"} ${r.duration}s ${Date.now() - t0}ms`);
        return send(res, 200, { id: r.id, duration: r.duration, envelope: r.envelope, windowMs: r.windowMs }, origin);
      } catch (e) {
        // per-minute limits refill quickly: rest 60 s (the game shows "voice resting" and keeps captions)
        if (/http 429/.test(e.message)) { TTS.blockedUntil = Date.now() + 60 * 1000; console.warn("[tts] QUOTA EXCEEDED - voice resting 60 s (text-only)"); }
        else console.warn(`[tts] FAILED: ${e.message.slice(0, 200)}`);
        return send(res, 503, { error: "voice unavailable" }, origin);
      }
    }
    if (req.method === "POST" && (urlPath === "/test-shot" || urlPath === "/test-log")) {
      if (!TEST_SHOTS) return send(res, 404, { error: "not found" }, origin);
      const chunks = []; let size = 0;
      for await (const c of req) { size += c.length; if (size > 8e6) return send(res, 413, { error: "too large" }, origin); chunks.push(c); }
      const body = Buffer.concat(chunks);
      if (urlPath === "/test-log") { console.log(`[browser-tour] ${body.toString("utf8").slice(0, 300)}`); return send(res, 200, { ok: true }, origin); }
      const name = (new URL(req.url, "http://x").searchParams.get("name") || "shot").replace(/[^A-Za-z0-9_-]/g, "").slice(0, 60) || "shot";
      fs.mkdirSync(SHOT_DIR, { recursive: true });
      fs.writeFileSync(path.join(SHOT_DIR, name + ".png"), body);
      return send(res, 200, { ok: true }, origin);
    }
    if (req.method !== "POST" || urlPath !== "/tutor") return send(res, 404, { error: "not found" }, origin);
    if (rateLimited()) return send(res, 429, { error: "slow down" }, origin);

    let raw = "";
    for await (const chunk of req) { raw += chunk; if (raw.length > 6_200_000) return send(res, 413, { error: "too large" }, origin); }
    let parsed;
    try { parsed = validateRequest(JSON.parse(raw)); }
    catch (e) { return send(res, 400, { error: e.message }, origin); }

    const t0 = Date.now();
    try {
      // automated tests send X-Quack-Mock: 1 so they never consume the Gemini quota
      const useMock = mock || req.headers["x-quack-mock"] === "1";
      const reply = useMock ? mockReply(parsed) : await ask(parsed);
      const { ok, line, problems } = validateLine(reply);
      if (!ok) throw new Error("invalid reply: " + problems.join("; "));
      line.source = useMock ? "mock" : "gemini";
      line.model = useMock ? null : MODEL;
      console.log(`[tutor] ${parsed.trigger} ${Date.now() - t0}ms ${line.source}${problems.length ? " fixes=" + problems.length : ""}`);
      return send(res, 200, line, origin);
    } catch (e) {
      if (/http 429/.test(e.message)) {
        console.warn(`[tutor] ${parsed.trigger} QUOTA EXCEEDED (429) - game falls back to local hints`);
        return send(res, 503, { error: "tutor quota exceeded" }, origin);
      }
      console.warn(`[tutor] ${parsed.trigger} FAILED after ${Date.now() - t0}ms: ${e.message.slice(0, 200)}`);
      return send(res, 502, { error: "tutor unavailable" }, origin);   // Unity falls back to local hints
    }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (!MOCK && !KEY) {
    console.error("GEMINI_API_KEY missing (backend/.env). Start with --mock for offline replies.");
    process.exit(1);
  }
  // BIND may list several addresses (e.g. "127.0.0.1,<tailscale-ip>"): localhost for this laptop, the
  // Tailscale IP for other tailnet devices. Never 0.0.0.0 by default (would expose the quota on the LAN).
  const srv = createServer();
  for (const addr of BIND.split(",").map((a) => a.trim()).filter(Boolean)) {
    const s = srv.listen ? http.createServer(srv.listeners("request")[0]) : srv;
    s.listen(PORT, addr, () => console.log(`Professor Quack tutor on http://${addr}:${PORT}  mode=${MOCK ? "mock" : "gemini"} model=${MOCK ? "-" : MODEL} voice=${TTS_MODEL}/${TTS_VOICE}`));
  }
}
