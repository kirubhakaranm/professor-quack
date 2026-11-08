import { test } from "node:test";
import assert from "node:assert/strict";
import { validateLine, validateRequest, EMOTIONS, GESTURES } from "../schema.mjs";
import { createServer, mockReply } from "../server.mjs";
import { pcmToWav, envelope, cacheKey, synthesize } from "../tts.mjs";
import fs from "node:fs"; import os from "node:os"; import path from "node:path";

test("validateLine keeps valid fields and replaces invalid enums with safe defaults", () => {
  const { ok, line, problems } = validateLine({
    speech: "Look at **egg #4**.", emotion: "furious", gesture: "launch_rocket", after_speech: "nod",
    gaze_target: "selected_egg", intensity: 3, duck_behavior: "quack",
  });
  assert.equal(ok, true);
  assert.equal(line.speech, "Look at egg #4.");       // markdown stripped
  assert.equal(line.emotion, "neutral");               // unknown -> neutral
  assert.equal(line.gesture, "none");                  // unknown -> none (never executes arbitrary actions)
  assert.equal(line.intensity, 1);                     // clamped
  assert.equal(problems.length, 2);
});

test("validateLine rejects empty speech and caps long speech at a sentence boundary", () => {
  assert.equal(validateLine({ speech: "   " }).ok, false);
  const long = "This is a sentence. ".repeat(40);
  const { line } = validateLine({ speech: long });
  assert.ok(line.speech.length <= 420);
  assert.ok(line.speech.endsWith("."));
});

test("schema contains the original lesson emotions and the design-brief additions", () => {
  for (const e of ["neutral", "curious", "thinking", "encouraging", "delighted", "mock_serious", "happy", "surprised"])
    assert.ok(EMOTIONS.includes(e), e);
  for (const g of ["point_at_plot", "celebrate", "wing_flap"]) assert.ok(GESTURES.includes(g), g);
});

test("validateRequest enforces trigger and question", () => {
  assert.throws(() => validateRequest({ trigger: "dance" }));
  assert.throws(() => validateRequest({ trigger: "question", question: "" }));
  assert.deepEqual(validateRequest({ trigger: "hint" }).trigger, "hint");
});

async function withServer(opts, fn) {
  const srv = createServer(opts);
  await new Promise((r) => srv.listen(0, "127.0.0.1", r));
  const base = `http://127.0.0.1:${srv.address().port}`;
  try { await fn(base); } finally { srv.close(); }
}

test("POST /tutor in mock mode returns a validated line built from context", async () => {
  await withServer({ mock: true }, async (base) => {
    const res = await fetch(base + "/tutor", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ trigger: "question", question: "why?", context: { selected_egg: { id: 4, p_jumbo: 0.6 } } }),
    });
    assert.equal(res.status, 200);
    const line = await res.json();
    assert.match(line.speech, /egg #4/);
    assert.equal(line.source, "mock");
    assert.equal(line.gaze_target, "selected_egg");
  });
});

test("POST /tutor returns 502 when the model fails or replies with garbage", async () => {
  await withServer({ mock: false, ask: async () => ({ speech: "" }) }, async (base) => {
    const res = await fetch(base + "/tutor", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ trigger: "hint" }) });
    assert.equal(res.status, 502);
  });
  await withServer({ mock: false, ask: async () => { throw new Error("boom"); } }, async (base) => {
    const res = await fetch(base + "/tutor", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ trigger: "hint" }) });
    assert.equal(res.status, 502);
  });
});

test("bad requests get 400, unknown routes 404, health reports mode", async () => {
  await withServer({ mock: true }, async (base) => {
    assert.equal((await fetch(base + "/tutor", { method: "POST", headers: { "Content-Type": "application/json" }, body: "not json" })).status, 400);
    assert.equal((await fetch(base + "/nope")).status, 404);
    const h = await (await fetch(base + "/health")).json();
    assert.equal(h.ok, true);
  });
});

test("mockReply without an egg mentions accuracy", () => {
  assert.match(mockReply({ trigger: "hint", context: { training: { accuracy: 0.72 } } }).speech, /72%/);
});

test("X-Quack-Mock header returns a mock reply even when the server is in Gemini mode", async () => {
  let called = false;
  await withServer({ mock: false, ask: async () => { called = true; return {}; } }, async (base) => {
    const res = await fetch(base + "/tutor", { method: "POST", headers: { "X-Quack-Mock": "1", "Content-Type": "application/json" }, body: JSON.stringify({ trigger: "hint" }) });
    assert.equal(res.status, 200);
    assert.equal((await res.json()).source, "mock");
    assert.equal(called, false);
  });
});

test("health accepts a query string and unknown static paths 404", async () => {
  await withServer({ mock: true }, async (base) => {
    assert.equal((await fetch(base + "/health?from=WebGLPlayer")).status, 200);
    assert.equal((await fetch(base + "/../../etc/passwd")).status, 404);
  });
});

test("tts helpers: WAV header, envelope range, stable cache key", () => {
  const pcm = Buffer.alloc(2 * 4800);   // 0.2 s at 24 kHz
  for (let i = 0; i < 4800; i++) pcm.writeInt16LE(Math.round(Math.sin(i / 10) * 12000 * (i > 2400 ? 1 : 0.2)), i * 2);
  const wav = pcmToWav(pcm);
  assert.equal(wav.toString("ascii", 0, 4), "RIFF");
  assert.equal(wav.readUInt32LE(24), 24000);
  const env = envelope(pcm);
  assert.equal(env.length, 10);
  assert.ok(env.every((v) => v >= 0 && v <= 1));
  assert.ok(env[9] > env[0], "louder second half opens the beak wider");
  assert.equal(cacheKey("hello"), cacheKey("hello"));
  assert.notEqual(cacheKey("hello"), cacheKey("hello!"));
});

test("synthesize caches: second call does not hit the API", async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "quack-tts-"));
  let calls = 0;
  const pcm = Buffer.alloc(4800 * 2, 1);
  const fake = async () => { calls++; return { ok: true, text: async () => JSON.stringify({ candidates: [{ content: { parts: [{ inlineData: { data: pcm.toString("base64") } }] } }] }) }; };
  const a = await synthesize("Hello learner", { key: "x", cacheDir: dir, fetchImpl: fake });
  const b = await synthesize("Hello learner", { key: "x", cacheDir: dir, fetchImpl: fake });
  assert.equal(calls, 1);
  assert.equal(a.cached, false); assert.equal(b.cached, true);
  assert.ok(Math.abs(a.duration - 0.2) < 0.01);
});

test("voice trigger requires audio and returns the transcript field", async () => {
  assert.throws(() => validateRequest({ trigger: "voice" }));
  const fakeAudio = "A".repeat(200);
  assert.equal(validateRequest({ trigger: "voice", audio: fakeAudio }).audio, fakeAudio);
  await withServer({ mock: true }, async (base) => {
    const res = await fetch(base + "/tutor", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ trigger: "voice", audio: fakeAudio }) });
    assert.equal(res.status, 200);
    const line = await res.json();
    assert.match(line.heard, /arrow/);
  });
});

test("transcribe endpoint returns text (mock) and rejects missing audio", async () => {
  await withServer({ mock: true }, async (base) => {
    assert.equal((await fetch(base + "/transcribe", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({}) })).status, 400);
    const r = await fetch(base + "/transcribe", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ audio: "A".repeat(200) }) });
    assert.equal(r.status, 200);
    assert.match((await r.json()).text, /live transcript/);
  });
});

// ---- security regressions ----
import { test as secTest } from "node:test";
import assertSec from "node:assert/strict";
import { createServer as createSecServer } from "../server.mjs";

async function withSecServer(fn) {
  const srv = createSecServer({ mock: true });
  await new Promise((r) => srv.listen(0, "127.0.0.1", r));
  const base = `http://127.0.0.1:${srv.address().port}`;
  try { await fn(base); } finally { srv.close(); }
}

secTest("malformed URL escapes return 404 instead of crashing the server", async () => {
  await withSecServer(async (base) => {
    const r = await fetch(base + "/%E0%A4%A");
    assertSec.equal(r.status, 404);
    const h = await fetch(base + "/health");   // still alive
    assertSec.equal(h.status, 200);
  });
});

secTest("cross-site simple POSTs (text/plain) are refused", async () => {
  await withSecServer(async (base) => {
    const r = await fetch(base + "/tutor", { method: "POST", headers: { "Content-Type": "text/plain" }, body: JSON.stringify({ trigger: "hint", context: {} }) });
    assertSec.equal(r.status, 415);
  });
});

secTest("per-client rate limit on /tts", async () => {
  await withSecServer(async (base) => {
    let last = 0;
    for (let i = 0; i < 45; i++) {
      const r = await fetch(base + "/tts", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ text: "hi " + i }) });
      last = r.status;
    }
    assertSec.equal(last, 429);
  });
});
