// Shared tutor behaviour vocabulary. MUST stay in sync with Assets/_Quack/Runtime/Tutor/TutorLine.cs.
// Unity maps each approved value to an animation; anything else is dropped here and again in Unity.

export const EMOTIONS = [
  // original lesson set (CLAUDE.md)
  "neutral", "curious", "thinking", "encouraging", "delighted", "mock_serious",
  // added from the character design brief
  "happy", "excited", "confused", "surprised", "concerned", "proud", "laughing",
];

export const GESTURES = [
  "none", "point_at_plot", "point_at_panel", "point_at_conveyor", "nod", "shrug",
  "adjust_glasses", "tie_adjust", "wing_flap", "celebrate", "think_pose",
];

export const GAZE_TARGETS = ["learner", "plot", "selected_egg", "sorting_rule", "conveyor", "whiteboard"];

// scene objects Quack can make flash while he talks about them (MUST match HighlightManager keys in Unity)
export const HIGHLIGHTS = [
  "none", "plane", "gimbal", "equation", "arrow", "axes", "carton_jumbo", "carton_standard", "misrouted", "bands",
  "landscape", "training", "console", "whiteboard", "conveyor", "tv", "mission", "selected_egg",
];

export const DUCK_BEHAVIORS = ["none", "quack", "embarrassed_quack", "wing_flap", "preen", "tail_wiggle"];

export const LIMITS = { speechChars: 420, whiteboardLines: 4, whiteboardLineChars: 90, titleChars: 60, audioB64Chars: 6_000_000, heardChars: 300 };

/** JSON schema handed to Gemini (structured output). Enums constrain generation; we still validate. */
export const RESPONSE_SCHEMA = {
  type: "object",
  properties: {
    heard: { type: "string", description: "Only for spoken questions: an exact transcript of what the learner said." },
    speech: { type: "string", description: "What Professor Quack says aloud; 1-3 short sentences, plain English, no markdown or LaTeX." },
    emotion: { type: "string", enum: EMOTIONS },
    gesture: { type: "string", enum: GESTURES },
    after_speech: { type: "string", enum: GESTURES },
    gaze_target: { type: "string", enum: GAZE_TARGETS },
    intensity: { type: "number", description: "Emotional intensity 0.0-1.0; keep below 0.7 unless celebrating." },
    highlight: { type: "string", enum: HIGHLIGHTS, description: "The one scene object your speech refers to, so it flashes for the learner; none if nothing specific." },
    duck_behavior: { type: "string", enum: DUCK_BEHAVIORS, description: "Usually none. Occasional duck habit; never during an explanation of a mistake." },
    whiteboard: {
      type: "object",
      description: "Optional short recap; omit unless summarising a discovery.",
      properties: {
        title: { type: "string" },
        lines: { type: "array", items: { type: "string" } },
      },
    },
  },
  required: ["speech", "emotion", "gesture", "after_speech", "gaze_target", "intensity", "duck_behavior"],
};

function pick(value, allowed, fallback) {
  return typeof value === "string" && allowed.includes(value) ? value : fallback;
}

function clean(text, max) {
  if (typeof text !== "string") return "";
  // strip markdown/LaTeX-ish noise the UI can't render; collapse whitespace
  let t = text
    .replace(/^\s{0,3}#{1,6}\s+/gm, "")    // markdown headings (keep "egg #4")
    .replace(/^\s*>\s?/gm, "")             // block quotes
    .replace(/[*`]|\$+/g, "")               // bold/italic/code markers, LaTeX dollars (keep w_mass)
    .replace(/\s+/g, " ").trim();
  if (t.length > max) {
    const cut = t.slice(0, max);
    const end = Math.max(cut.lastIndexOf(". "), cut.lastIndexOf("? "), cut.lastIndexOf("! "));
    t = end > max * 0.5 ? cut.slice(0, end + 1) : cut.trimEnd() + "...";
  }
  return t;
}

/**
 * Validate and normalise a model reply into the application schema.
 * Returns { ok, line, problems[] }. Unknown enum values fall back to safe defaults.
 */
export function validateLine(raw) {
  const problems = [];
  if (!raw || typeof raw !== "object") return { ok: false, line: null, problems: ["not an object"] };
  const speech = clean(raw.speech, LIMITS.speechChars);
  if (!speech) problems.push("empty speech");
  const line = {
    speech,
    emotion: pick(raw.emotion, EMOTIONS, "neutral"),
    gesture: pick(raw.gesture, GESTURES, "none"),
    after_speech: pick(raw.after_speech, GESTURES, "none"),
    gaze_target: pick(raw.gaze_target, GAZE_TARGETS, "learner"),
    intensity: Number.isFinite(raw.intensity) ? Math.min(1, Math.max(0, raw.intensity)) : 0.5,
    duck_behavior: pick(raw.duck_behavior, DUCK_BEHAVIORS, "none"),
    highlight: pick(raw.highlight, HIGHLIGHTS, "none"),
  };
  for (const k of ["emotion", "gesture", "after_speech", "gaze_target", "duck_behavior", "highlight"])
    if (raw[k] !== undefined && raw[k] !== line[k]) problems.push(`replaced invalid ${k}: ${String(raw[k]).slice(0, 30)}`);
  if (typeof raw.heard === "string" && raw.heard.trim()) line.heard = clean(raw.heard, LIMITS.heardChars);
  if (raw.whiteboard && typeof raw.whiteboard === "object") {
    const lines = Array.isArray(raw.whiteboard.lines) ? raw.whiteboard.lines : [];
    const wb = {
      title: clean(raw.whiteboard.title, LIMITS.titleChars),
      lines: lines.slice(0, LIMITS.whiteboardLines).map((l) => clean(l, LIMITS.whiteboardLineChars)).filter(Boolean),
    };
    if (wb.title || wb.lines.length) line.whiteboard = wb;
  }
  return { ok: problems.every((p) => !p.startsWith("empty")), line, problems };
}

/** Validate the request coming from Unity; returns a trimmed copy or throws. */
export function validateRequest(body) {
  if (!body || typeof body !== "object") throw new Error("body must be a JSON object");
  const trigger = pick(body.trigger, ["question", "hint", "recap", "reaction", "voice"], null);
  if (!trigger) throw new Error("trigger must be question|hint|recap|reaction|voice");
  let audio = null;
  if (trigger === "voice") {
    if (typeof body.audio !== "string" || body.audio.length < 100) throw new Error("audio (base64 WAV) required for trigger=voice");
    if (body.audio.length > LIMITS.audioB64Chars) throw new Error("audio too long (max ~30 s)");
    audio = body.audio;
  }
  const question = typeof body.question === "string" ? body.question.slice(0, 500).trim() : "";
  if (trigger === "question" && !question) throw new Error("question required for trigger=question");
  const context = body.context && typeof body.context === "object" ? body.context : {};
  const json = JSON.stringify(context);
  if (json.length > 12000) throw new Error("context too large");
  return { trigger, question, context, audio };
}
