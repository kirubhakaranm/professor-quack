// Text-to-speech for Professor Quack: Gemini TTS -> WAV + loudness envelope, cached on disk so each line is
// paid for once. Unity plays the WAV and drives the beak from the envelope (approximate, not lip sync).

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

export const TTS_MODEL = process.env.TTS_MODEL || "gemini-2.5-flash-preview-tts";
export const TTS_VOICE = process.env.TTS_VOICE || "Puck";   // upbeat prebuilt voice; change via env
const SAMPLE_RATE = 24000;                                    // Gemini TTS: 16-bit mono PCM at 24 kHz
const ENVELOPE_MS = 20;
// Style prompt: performance direction only; the spoken words are exactly the caption text.
const STYLE = "Say this as Professor Quack, a warm, slightly theatrical duck professor who is trying to sound like a serious CEO. Speak clearly at a relaxed teaching pace:";

export function cacheKey(text, model = TTS_MODEL, voice = TTS_VOICE) {
  return crypto.createHash("sha1").update(`${model}|${voice}|${STYLE}|${text}`).digest("hex").slice(0, 24);
}

/** Wrap raw 16-bit mono PCM in a WAV header. */
export function pcmToWav(pcm, rate = SAMPLE_RATE) {
  const h = Buffer.alloc(44);
  h.write("RIFF", 0); h.writeUInt32LE(36 + pcm.length, 4); h.write("WAVE", 8);
  h.write("fmt ", 12); h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22);
  h.writeUInt32LE(rate, 24); h.writeUInt32LE(rate * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34);
  h.write("data", 36); h.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([h, pcm]);
}

/** RMS loudness per 20 ms window, normalised to 0..1 (beak opening). */
export function envelope(pcm, rate = SAMPLE_RATE, windowMs = ENVELOPE_MS) {
  const n = Math.floor(pcm.length / 2), win = Math.max(1, Math.floor(rate * windowMs / 1000));
  const out = [];
  let peak = 1e-6;
  for (let i = 0; i < n; i += win) {
    let sum = 0, c = 0;
    for (let j = i; j < Math.min(n, i + win); j++) { const s = pcm.readInt16LE(j * 2) / 32768; sum += s * s; c++; }
    const r = Math.sqrt(sum / Math.max(1, c));
    out.push(r); if (r > peak) peak = r;
  }
  return out.map((r) => Math.round(Math.min(1, r / peak) * 100) / 100);
}

/**
 * Return { id, duration, envelope } for the text, generating and caching it if needed.
 * Throws on API failure (caller falls back to text-only).
 */
export async function synthesize(text, { key, cacheDir, fetchImpl = fetch, model = TTS_MODEL, voice = TTS_VOICE } = {}) {
  const id = cacheKey(text, model, voice);
  const wavPath = path.join(cacheDir, id + ".wav"), metaPath = path.join(cacheDir, id + ".json");
  if (fs.existsSync(wavPath) && fs.existsSync(metaPath)) return { id, cached: true, ...JSON.parse(fs.readFileSync(metaPath, "utf8")) };

  const url = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`;
  const body = {
    contents: [{ parts: [{ text: `${STYLE} ${text}` }] }],
    generationConfig: { responseModalities: ["AUDIO"], speechConfig: { voiceConfig: { prebuiltVoiceConfig: { voiceName: voice } } } },
  };
  // the TTS model occasionally returns no audio (finishReason=OTHER); one immediate retry usually succeeds
  let part, lastReason;
  for (let attempt = 0; attempt < 2 && !part; attempt++) {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 20000);
    let res, raw;
    try {
      res = await fetchImpl(url, { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": key }, body: JSON.stringify(body), signal: ctrl.signal });
      raw = await res.text();
    } finally { clearTimeout(timer); }
    if (!res.ok) throw new Error(`tts http ${res.status}: ${raw.slice(0, 200)}`);
    const data = JSON.parse(raw);
    part = data?.candidates?.[0]?.content?.parts?.find((p) => p.inlineData?.data);
    lastReason = data?.candidates?.[0]?.finishReason;
    if (!part && attempt === 0) console.log(`[tts] no audio (finishReason=${lastReason}), retrying once`);
  }
  if (!part) throw new Error(`tts: no audio (finishReason=${lastReason})`);
  const pcm = Buffer.from(part.inlineData.data, "base64");
  const meta = { duration: Math.round((pcm.length / 2 / SAMPLE_RATE) * 1000) / 1000, envelope: envelope(pcm), windowMs: ENVELOPE_MS, model, voice };
  fs.mkdirSync(cacheDir, { recursive: true });
  fs.writeFileSync(wavPath, pcmToWav(pcm));
  fs.writeFileSync(metaPath, JSON.stringify(meta));
  return { id, cached: false, ...meta };
}
