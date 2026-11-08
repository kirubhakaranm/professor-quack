# Professor Quack's Egg & AI Emporium

**A 3D/VR learning game where a duck CEO teaches you how machine learning works, by fixing his egg factory.**

Built in 24 hours at **HACK.COMS '26** (RIT).

Professor Quack's egg-sorting machine breaks the night before a big shipment. You build a new sorter that **learns from labelled examples**, and along the way you meet the core ideas of machine learning hands-on, in 3D:

| # | Lesson | Idea |
|---|---|---|
| 1 | Describe the eggs | measurements → **feature vectors** (mass, length, width) |
| 2 | Build the sorting rule | a **decision plane** `z = w·x + b` you drag in 3D |
| 3 | Measure the mistakes | **probability** `p = σ(z)` and **cross-entropy loss** vs accuracy |
| 4 | Explore the landscape | **parameter space**: a labelled 2D slice of the loss surface |
| 5 | Teach the machine | **gradient descent**: small, good and overshooting learning rates |
| 6 | Test and scale | **held-out eggs**, then **sequential vs batch** processing of 10,000 eggs |

**Professor Quack** is an AI tutor powered by **Google Gemini**. He receives the actual game state (your parameters, the selected egg, your mistakes, loss, recent actions), asks a leading question before giving an answer, highlights what he's talking about, and walks to that part of the lab.

## Play

| Where | How |
|---|---|
| **Browser** | **[professorquack.courses](https://professorquack.courses)**: no install (desktop Chrome, Edge or Firefox). Uses the built-in offline tutor unless a tutor service is linked with `?tutor=<url>`. |
| **Windows** | Download `ProfessorQuack-Windows.zip` from [Releases](../../releases), unzip, run **Play.bat** (with the live Gemini tutor if `backend/` is running). |
| **VR (Meta Quest 3)** | Same Windows download: connect the headset with **Quest Link**, set Link as the OpenXR runtime, run **Play VR.bat**. |

Trailer: [ProfessorQuack_trailer.mp4](../../releases) (76 s, recorded from the game).

## Accessibility first

Every essential task works **with the sound off and without voice input**:

- persistent, resizable captions, including meaningful sounds (`[surprised quack]`, `[applause]`)
- shapes and symbols alongside colour (JUMBO eggs carry a band; misrouted eggs are marked ✕)
- pause, replay, explanation history, reading pace and voice speed controls
- less-chatter and reduced-motion options
- [Atkinson Hyperlegible](https://brailleinstitute.org/freefont) for all learning text
- an exact numeric alternative (−/+ buttons) to every drag interaction

## Run it

**Requirements:** Unity **6000.6.8f1** (URP), Node.js **20+**, a Gemini API key (optional: the game runs with a local, offline tutor without one).

```bash
# 1. tutor service (keeps the API key server-side)
cd backend
cp .env.example .env        # put your GEMINI_API_KEY in .env
node server.mjs             # or: node server.mjs --mock   (no key, offline replies)

# 2. game
#    open the project folder in Unity 6000.6.8f1
#    open Assets/_Quack/Scenes/Main.unity and press Play
#    (if the scene is missing: menu Quack > Rebuild Main Scene)
```

- **Windows build:** `File > Build Profiles > Windows`, then run `ProfQuack.exe -tutorUrl http://127.0.0.1:8787`.
- **VR (Meta Quest 3 via Quest Link):** `ProfQuack.exe -vr` with Quest Link as the active OpenXR runtime.
- **Tests:** Unity Test Runner (EditMode), and `cd backend && node --test`.

## How it's built

See **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** for the full picture.

```
Unity 6 game (URP, OpenXR) ──game-state JSON──► Node tutor service ──► Google Gemini
  math core (pure C#)                            schema validation       tutor (structured JSON)
  3D lab, lessons, captions ◄──speech + emotion + gesture + audio────    TTS voice · speech-to-text
```

```
Assets/_Quack/
  Core/        pure C# maths (no Unity): dataset, scaler, logistic model, loss, trainer, plane geometry
  Runtime/
    State/     GameState (single source of truth), palette, accessibility settings
    Viz/       feature plot, decision plane + weight arrow, loss landscape, conveyor, cartons
    World/     the lab: stations, TV, whiteboard, control desk, trophies, highlights
    Lesson/    guided intro, six lessons, finale (board meeting)
    Tutor/     tutor schema, speech queue, local reactions, Gemini client
    Quacks/    character loader, rig and procedural animator
    UI/        title screen, road map, HUD, settings, world-space panels
    Input/     desktop (mouse/keyboard) and VR (XR rays) input
  Editor/      scene generation, TextMeshPro setup, build scripts
  Tests/       EditMode tests (maths cross-checked against tools/ref_model.py)
backend/       Node tutor service (no dependencies): server, prompt, schema, TTS, tests
tools/         dataset generator, reference model, Blender character pipeline
docs/          architecture, character pipeline, testing
```

## Honest notes

- The maths is real: every plane, arrow, surface and number is computed from the same parameters the model uses. The loss is convex, so there are no fake local minima.
- Throughput figures in lesson 6 are labelled: one is **measured** in-game, the others are **illustrative**.
- The egg dataset is synthetic and fictional, generated by `tools/gen_eggs.py`.
- Professor Quack's 3D model, the intro and finale clips, and some concept art were AI-generated (Higgsfield), then cleaned up and rigged by our Blender pipeline (`tools/blender/prepare_quacks.py`).

## Credits and licences

- Code: [MIT](LICENSE).
- Fonts: Atkinson Hyperlegible, Cinzel / Cinzel Decorative, Patrick Hand, VT323, Nunito and STIX Two (maths), all under the SIL Open Font License (licence files next to the fonts in `Assets/_Quack/Resources/`).
- Built with Unity 6 (URP, OpenXR, TextMeshPro), Node.js, Blender and Google Gemini.
