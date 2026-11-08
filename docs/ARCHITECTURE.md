# Architecture

## System overview

```
┌──────────────────────────── Unity 6 game (URP · OpenXR) ────────────────────────────┐
│                                                                                     │
│  Input (desktop mouse/keys · VR rays)                                               │
│        │ commands                                                                   │
│        ▼                                                                            │
│  GameState  ◄── single source of truth: dataset, parameters (w, b), selection,      │
│        │        stage, recent actions, train/test results                           │
│        │ events                                                                     │
│        ├──► Viz: feature plot · decision plane + weight arrow · loss landscape ·    │
│        │         conveyor + cartons · equation  (all computed from GameState)       │
│        ├──► Lesson director: 6 lessons, checklists, nudges, recaps, finale          │
│        ├──► Local tutor: instant, deterministic reactions (no network)              │
│        └──► Tutor client ──────────────┐                                            │
│                                        │ POST /tutor  {game state, question}        │
│  Speech queue ◄── TutorLine ◄──────────┤                                            │
│   captions · voice · beak · gestures   │ POST /tts · /transcribe                    │
└────────────────────────────────────────┼────────────────────────────────────────────┘
                                         ▼
                 ┌────────── Node tutor service (backend/, no dependencies) ──────────┐
                 │ API key server-side · prompt builder · JSON-schema validation ·    │
                 │ enum allow-lists · TTS cache · rate-limit back-off                 │
                 └───────────────┬───────────────────────────────┬────────────────────┘
                                 ▼                               ▼
                  Gemini Flash (tutor, structured JSON)   Gemini TTS (voice)
                                                          Gemini Flash-Lite (speech→text)
```

## Maths core (`Assets/_Quack/Core`)

Pure C#, with no UnityEngine references, so it can be unit-tested and cross-checked against the Python reference model in `tools/ref_model.py`.

- **Features:** mass (g), length (mm), width (mm), standardised with a fitted scaler. Axes are always labelled in raw units, and the model works in standardised units.
- **Model:** `z = w·x + b`, `p(JUMBO) = σ(z)`, decision at `p = 0.5` ⇔ `z = 0` (the plane).
- **Loss:** numerically stable binary cross-entropy plus a small L2 penalty on `w`. This keeps the objective convex with a finite optimum even when the data is separable.
- **Training:** full-batch gradient descent. The learning rate alone decides whether steps are slow, converge, oscillate or diverge; no outcome is hard-coded.
- **Plane geometry:** rotating the plane changes the normal (the direction of `w`). Translating it by distance `d` gives `b ← b − ‖w‖·d`, and near-zero `w` is handled explicitly.
- **Loss landscape:** a labelled 2D slice through the 4-parameter space, with the other parameters shown as held fixed.
- **Tests:** probability/threshold consistency, plane placement, analytic vs numerical gradients, and loss reduction under a suitable step size.

## Feature space vs parameter space

| | Feature space (hologram table) | Parameter space (learning corner) |
|---|---|---|
| A point is | one **egg** | one **model** (a choice of w, b) |
| Axes | measurements (g, mm) | parameters |
| Shown as | egg cloud + decision plane | loss surface slice + training trail |

## The tutor contract

The game sends structured context, not just text: current task, selected egg and its prediction, parameters, loss, recent actions, misrouted eggs, and what has been introduced so far. Gemini must answer in a JSON schema:

```json
{ "speech": "...", "emotion": "encouraging", "gesture": "point_at_learning_rate",
  "after_speech": "small_nod", "gaze_target": "plot", "highlight": "plane",
  "intensity": 0.6, "duck_behavior": "none" }
```

- The service validates every reply: unknown enum values fall back to safe defaults, so the model can only trigger **approved** animations and highlights.
- Unity maps the reply to animation states, highlights and station moves. Captions and gestures are timed to **audio playback**, not to text arrival.
- Priorities: player questions > teaching > reactions > idle comedy, with cooldowns so jokes never interrupt speech.
- Immediate reactions (drags, selections) come from the local tutor, so the game never waits on the network. Without a backend, the game still runs with the local tutor.

## Character

`tools/blender/prepare_quacks.py` takes an AI-generated mesh, segments the beak, tie, eyes and brows by colour, adds jaw, tie, head and wing bones, and exports FBX plus face metadata. At runtime, `CharacterLoader` builds eyes, lids and brows from that metadata. `QuacksAnimator` drives six emotion states, audio-amplitude beak motion, blinks, gaze, gestures and duck habits (preening, a tie adjustment, a surprised quack).

## Accessibility architecture

- `AccessSettings` (persisted): text size, caption opacity, reading pace, voice on/off and speed, less chatter, reduced motion.
- Every spoken line is a `TutorLine` with caption text and an optional non-verbal caption (`[applause]`); captions are always rendered, with or without voice.
- Class cues never rely on colour alone (band and size for JUMBO, ✕ for misrouted).
- Every drag has an exact alternative (−/+ step buttons on the desk screen); VR never moves the player without consent (teleport pads).

## Scene organisation

The scene is generated by code (`Editor/SceneSetup.cs`, menu **Quack > Rebuild Main Scene**), and `QuackGame.cs` assembles the runtime: lab environment, stations (TV, hologram table, sorting line, learning corner, whiteboard), world-space panels, and the desktop or XR rig. Mathematical objects are procedural; only the character and video clips are imported assets.
