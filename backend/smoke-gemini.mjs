// One real Gemini call with a realistic game context; prints the validated reply (never the key).
import { askGemini } from "./server.mjs";
import { validateLine } from "./schema.mjs";
const context = {
  stage: "B_build_rule",
  selected_egg: { id: 4, mass_g: 61.4, length_mm: 60.1, width_mm: 43.8, label: "STANDARD", p_jumbo: 0.6, prediction: "JUMBO", misrouted: true },
  params: { w_mass: 1.5, w_length: 0, w_width: 0, b: -0.35, steepness: 1.5 },
  training: { correct: 108, total: 150, accuracy: 0.72, misrouted: 42, jumbo_correct: "38/64", standard_correct: "70/86" },
  recent_actions: ["select egg #4"],
};
const t0 = Date.now();
const raw = await askGemini({ trigger: "question", question: "Why is this egg going to the wrong carton?", context });
const v = validateLine(raw);
console.log(JSON.stringify({ ms: Date.now() - t0, ok: v.ok, problems: v.problems, line: v.line }, null, 2));
