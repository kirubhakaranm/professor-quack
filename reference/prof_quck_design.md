# Professor Quack — 3D Character, Unity Integration & AI-Driven Animation

You are the lead Unity developer and technical architect for **Professor Quack**, an AI-powered, gamified educational experience being developed for a hackathon.

Your task is to help design and implement the complete 3D character integration and animation architecture for Professor Quack.

This document describes our agreed character design, asset-generation workflow, animation requirements, and AI interaction architecture.

**Important:** Do not immediately implement everything. First inspect the existing Unity project, identify what is already implemented, propose an incremental implementation plan, and ask for approval before making major architectural changes.

---

## 1. Project Overview

Professor Quack is an intelligent, expressive, humorous anthropomorphic duck who teaches concepts through interactive gamified lessons.

Our first educational experience focuses on **classification in machine learning**.

The application is built using **Unity** and must support:

- Traditional desktop/2D-screen gameplay.
- VR experiences through Unity XR/OpenXR.
- Interactive teaching environments.
- AI-generated explanations and responses.
- Voice-driven teaching and conversational interaction.
- Expressive 3D character animation.
- Gamified learning activities.

Professor Quack is not merely a static NPC. He is the central character and teaching companion.

We want him to feel like a living animated professor, combining educational intelligence with cartoon personality.

## 2. Character Design

Professor Quack is a stylized yellow duck professor.

His approved visual identity includes:

- Golden-yellow fluffy feathers.
- Large, rounded, expressive head.
- Plump duck-shaped body.
- Exactly two large expressive eyes.
- One pair of round black glasses.
- Expressive eyebrows.
- Orange beak with distinct upper and lower sections.
- Small feather tuft on his head.
- Navy-blue academic blazer.
- Cream vest and white shirt.
- Burgundy bow tie.
- Orange legs and webbed feet.
- Small feathered tail.
- Short wing-like arms capable of teaching gestures.

His personality is intelligent, slightly theatrical, friendly, curious, playful, and occasionally mischievous.

He should look polished and appealing in both desktop and VR environments.

**Visual identity must remain consistent across all assets and animations.**

## 3. Our 3D Asset Generation Pipeline

We are creating Professor Quack's character model using **Higgsfield AI**, specifically its Tripo 3D generation capabilities.

### Step A — Character Concept

We generated and approved a reference image of Professor Quack.

This is our primary character identity reference.

### Step B — Multiview Reference Images

Using Higgsfield's image generation model, we generated a single turnaround reference sheet containing:

1. Front view — 0 degrees.
2. Left-side profile — 90 degrees.
3. Back view — 180 degrees.
4. Three-quarter front view — approximately 45 degrees.

We then cropped this turnaround sheet into four separate PNG files.

These are intended as reference images for 3D reconstruction.

### Step C — 3D Reconstruction

We are evaluating **Tripo H3.1 Multiview-to-3D**.

Target output: a textured `.glb` character model.

Our preferred generation settings are:

- Geometry quality: Standard initially.
- Texture generation: Enabled.
- PBR materials: Enabled.
- Texture quality: Standard initially.

We previously generated an initial single-image 3D model and an unsuccessful multiview version.

The unsuccessful multiview reconstruction duplicated facial geometry, including eyes and glasses.

Therefore, every generated model must be inspected before acceptance.

Check:

- Exactly two eyes.
- One pair of glasses.
- Correct beak geometry.
- No duplicated facial features.
- Consistent wing geometry.
- Two legs and two feet.
- Correct tail placement.
- Correct outfit.
- No detached or floating geometry.
- Acceptable topology for rigging.

**Do not assume that AI-generated GLB models are already rigged, animated, or suitable for facial blendshapes.**

### Step D — Blender Cleanup

After selecting the best 3D model, use Blender for any required cleanup.

Potential operations:

- Correct mesh geometry.
- Remove duplicated objects or artifacts.
- Fix normals.
- Check materials and textures.
- Set the correct model orientation and scale.
- Establish a consistent origin/pivot.
- Separate facial components if necessary.
- Prepare the model for skeletal rigging.
- Add facial shape keys where practical.
- Reduce polygon complexity if needed for VR performance.

If the generated mesh is too difficult to rig, evaluate whether replacing individual facial components with simpler controllable geometry is more efficient than repairing the original mesh.

For example, separately controllable eyelids, eyes, eyebrows, and upper/lower beak may provide more reliable animation than attempting to deform a complex AI-generated mesh.

### Step E — Unity Import

Import the approved model into Unity.

We can use GLB import tooling such as glTFast, or export an FBX from Blender when that is more appropriate for the chosen rigging and animation workflow.

Validate:

- Character scale.
- Coordinate orientation.
- Material compatibility with the selected Unity render pipeline.
- Correct textures.
- Bone hierarchy.
- Animation clips.
- Blendshape availability.
- VR rendering performance.

The model should be instantiated as a reusable character prefab.

Suggested project organization:

Assets/
  ProfessorQuack/
    Models/
    Materials/
    Textures/
    Animations/
    Prefabs/
    Scripts/
    Audio/
    Config/

Adapt this organization to the existing Unity project's conventions.

---

## 4. Character Animation Architecture

We want Professor Quack to have multiple independent animation systems that can work together.

Avoid implementing every behavior as a single large animation clip.

Use layered animation wherever possible.

### A. Body Animation

Use skeletal rigging for:

- Head rotation and tilting.
- Neck movement.
- Wing gestures.
- Shoulder movement.
- Body rotation.
- Tail movement.
- Foot movement.
- Walking.
- Jumping.
- Idle breathing and body sway.

Suggested initial animations:

- Idle.
- Talking.
- Explaining.
- Pointing.
- Thinking.
- Celebrating.
- Confused.
- Surprised.
- Laughing.
- Wing flap.
- Walking.
- Returning to idle.

These can initially be simple procedural animations or manually authored clips.

### B. Facial Expressions

Professor Quack must be able to express emotions while speaking.

Target expressions:

| Emotion | Facial behavior |
|---|---|
| Neutral | Relaxed eyebrows, open eyes, closed beak |
| Happy | Raised cheeks, smiling beak, bright eyes |
| Excited | Wide eyes, raised eyebrows, animated beak |
| Confused | One raised eyebrow, tilted head, asymmetric expression |
| Thinking | Narrowed eyes, subtle eyebrow movement |
| Surprised | Wide eyes, raised eyebrows, open beak |
| Concerned | Slightly lowered eyebrows, cautious expression |
| Proud | Confident smile, raised head |
| Laughing | Squinted eyes, open beak, rhythmic body movement |

Preferred implementation:

- Blendshapes / shape keys for facial deformation where available.
- Skeletal controls for head and beak.
- Procedural eye movement.
- Procedural blinking.
- Eyebrow controls.
- Upper and lower beak articulation.
- Optional independently animated eyelids.

**The facial system must not depend on Tripo generating usable facial blendshapes automatically.**

If blendshapes are unavailable, create a fallback using independent facial meshes and transform-based animation.

### C. Eye Tracking and Blinking

Professor Quack should look toward relevant objects and occasionally toward the learner.

Examples:

- Look at the student while explaining.
- Look toward an object being discussed.
- Look toward a classification example.
- Look back at the learner after a demonstration.
- Occasionally glance around naturally.

Implement:

- Controlled eye rotation.
- Natural blinking intervals.
- Occasional double blinks.
- Head movement coordinated with gaze.
- Smooth gaze transitions.

Avoid constant random eye movement.

### D. Speech and Beak Synchronization

Professor Quack will speak using generated speech audio.

The beak must move naturally during speech.

We need a lip-sync strategy appropriate for a cartoon duck.

Possible implementation levels:

**Level 1 — Hackathon MVP**

Use audio-amplitude-driven beak opening.

Map speech loudness to the beak opening amount.

Apply smoothing to prevent jitter.

**Level 2 — Improved Speech Animation**

Use phoneme or viseme timing information when supported by the speech provider.

Map speech units to a small set of duck-specific beak shapes.

Example viseme categories:

- Closed.
- Slightly open.
- Wide open.
- Rounded.
- Narrow.
- Smile-like articulation.

**Level 3 — Expressive Speech**

Combine:

- Viseme animation.
- Emotion-driven facial expressions.
- Eyebrow movement.
- Head gestures.
- Eye tracking.
- Wing gestures.

Speech animation should coexist with emotional expression rather than replacing it.

The animation architecture must allow future upgrades without rewriting the dialogue system.

---

## 5. Professor Quack's Personality and Duck Behaviors

Professor Quack is a professor first, but occasionally behaves like a duck.

This contrast is part of the humor.

Examples:

**During a correct answer:**
- Smiles.
- Flaps his wings.
- Says something encouraging.
- Occasionally gives a playful quack.

**During a wrong answer:**
- Tilts his head.
- Raises an eyebrow.
- Gives a helpful hint.
- Avoids embarrassing the student.

**During a difficult explanation:**
- Adjusts his glasses.
- Looks thoughtful.
- Pauses briefly.
- Uses a wing to point toward an example.

**During a surprising result:**
- Widens his eyes.
- Opens his beak.
- Takes a small step backward.

**During idle moments:**
- Blinks.
- Looks around.
- Adjusts posture.
- Occasionally preens his feathers.
- Makes subtle tail movements.

**Occasional humorous behaviors:**
- Accidental quack during a serious explanation.
- Embarrassed reaction after quacking.
- Proud professor pose.
- Playful wing flap.
- Dramatic thinking pose.

Important constraints:

- Humor should be occasional, not constant.
- Duck behaviors must not interrupt important explanations.
- Avoid excessive random animations.
- Character actions should support the educational experience.
- VR animations should remain comfortable and avoid unnecessary rapid movements.

---

## 6. AI-Driven Behavior System

We want an AI agent to control Professor Quack's teaching behavior.

However, the AI agent must NOT directly manipulate Unity transforms or animation bones.

Instead, the AI should output structured, high-level behavioral instructions.

Unity interprets those instructions and executes validated animations.

### Proposed Response Schema

Example:

```json
{
  "dialogue": "Excellent! You classified that correctly!",
  "emotion": "excited",
  "gesture": "celebrate",
  "gaze_target": "student",
  "intensity": 0.8,
  "duck_behavior": "wing_flap",
  "speech_style": "enthusiastic"
}
```

Another example:

```json
{
  "dialogue": "Hmm, interesting choice. Let's look at the features again.",
  "emotion": "thinking",
  "gesture": "point",
  "gaze_target": "classification_board",
  "intensity": 0.4,
  "duck_behavior": "none",
  "speech_style": "curious"
}
```

These examples are illustrative, not the final production schema.

The actual system should use typed and validated data structures.

### Recommended Architecture

AI Teaching Agent
↓
Structured Behavior Response
↓
Behavior Validator / Interpreter
↓
Dialogue + TTS Orchestrator
↓
Unity Character Controller
↓
Animation Layers:
- Body Animator
- Facial Expression Controller
- Beak / Lip-Sync Controller
- Gaze Controller
- Gesture Controller
- Duck Personality Controller

The architecture should separate:

1. What the professor says.
2. How the professor feels.
3. Which gesture the professor performs.
4. Where the professor looks.
5. How the beak moves during speech.
6. Which occasional duck-like behavior occurs.

Unity should have final control over timing, blending, and animation safety.

### Timing and Coordination

Avoid triggering all animations simultaneously without coordination.

For example:

- Start looking toward the student.
- Transition into a happy expression.
- Begin speaking.
- Trigger a celebratory wing flap during an appropriate phrase.
- Continue natural blinking.
- Return to a relaxed idle pose after speaking.

Consider a timeline or event-driven orchestration system for coordinating speech and animations.

---

## 7. Unity Character Controller Design

Propose a modular implementation.

Potential components:

**ProfessorQuackController**
- Main entry point.
- Receives validated high-level behavior commands.

**QuackAnimationController**
- Manages body animation states and transitions.

**QuackFacialController**
- Controls facial expressions and blending.

**QuackSpeechController**
- Plays generated audio.
- Coordinates beak movement.

**QuackGazeController**
- Handles eye tracking and head orientation.

**QuackGestureController**
- Handles wing and body gestures.

**QuackPersonalityController**
- Controls occasional duck-specific behaviors.
- Enforces cooldowns and contextual restrictions.

**QuackBehaviorOrchestrator**
- Coordinates timing and priorities across all animation systems.

These are suggested responsibilities, not mandatory class names.

Inspect the existing project and avoid unnecessary complexity or redundant controllers.

---

## 8. Desktop and VR Compatibility

The same Professor Quack character must work in both desktop and VR modes.

The core character logic should be shared.

Desktop mode:
- Camera-oriented teaching.
- Mouse/keyboard interaction.
- UI-based learning activities.

VR mode:
- Spatial teaching.
- Natural gaze targeting.
- Pointing toward 3D objects.
- Comfortable character movement.
- Appropriate physical scale.
- Spatial audio where useful.

Avoid maintaining two separate character implementations.

Prefer shared animation logic with mode-specific presentation and interaction adapters.

---

## 9. Performance Requirements

We are building a hackathon prototype, so prioritize reliability and visual polish over unnecessary complexity.

Consider:

- Reasonable polygon counts.
- Efficient materials.
- Limited draw calls.
- Texture compression.
- Animation blending costs.
- VR frame-rate requirements.
- Minimal runtime allocations.
- Avoiding heavy AI inference inside Unity.

AI inference and TTS may run through external services, while Unity handles rendering and animation.

Do not prematurely optimize without measuring performance.

---

## 10. Recommended Development Milestones

### Milestone 1 — Static Character Integration

- Import the selected GLB.
- Verify materials and scale.
- Create Professor Quack prefab.
- Place the character in a simple test scene.
- Confirm rendering on desktop and VR.

### Milestone 2 — Basic Character Rigging

- Prepare a usable skeletal rig.
- Add idle animation.
- Add head movement.
- Add basic wing gestures.
- Add beak opening and closing.

### Milestone 3 — Facial Expressions

- Add blinking.
- Add eyebrow movement.
- Add eye tracking.
- Add happy, confused, thinking, and surprised expressions.
- Validate smooth transitions.

### Milestone 4 — Speech Integration

- Connect TTS audio playback.
- Add audio-driven beak animation.
- Synchronize speaking and idle states.
- Ensure expressions continue during speech.

### Milestone 5 — AI Behavior Integration

- Define a structured behavior command schema.
- Parse and validate AI outputs.
- Connect behavior commands to Unity controllers.
- Implement a small set of teaching reactions.

### Milestone 6 — Personality and Polish

- Add occasional quacks.
- Add wing flapping.
- Add glasses-adjustment animation if the rig supports it.
- Add idle personality behaviors.
- Add smooth transitions and animation priorities.
- Validate desktop and VR performance.

---

## 11. Hackathon MVP Priorities

We do NOT need a fully cinematic character-animation system immediately.

The minimum viable Professor Quack should:

1. Appear correctly in Unity.
2. Stand naturally with an idle animation.
3. Speak using generated audio.
4. Open and close his beak while speaking.
5. Blink naturally.
6. Look toward the learner or teaching objects.
7. Express at least four emotions: neutral, happy, thinking, surprised.
8. Perform at least three gestures: point, celebrate, wing flap.
9. Respond to structured AI behavior commands.
10. Work in desktop mode, with an architecture compatible with VR.

Prioritize a complete end-to-end teaching interaction over implementing dozens of unfinished animations.

---

## 12. What I Need From You (Claude Code)

First, inspect the existing Unity project and report:

1. Unity version and render pipeline.
2. Current project structure.
3. Existing character assets and animation systems.
4. Current AI dialogue/TTS integration, if any.
5. Existing desktop and XR/OpenXR configuration.
6. Dependencies needed for importing GLB models.
7. Any technical constraints affecting rigging, facial animation, or VR.

Then propose:

- A practical asset-import workflow.
- Whether GLB import or Blender-to-FBX is preferable.
- A suitable rigging strategy for the selected duck model.
- A facial-animation strategy with fallbacks if blendshapes are absent.
- A modular Unity component architecture.
- A milestone-based implementation plan.
- Clear acceptance criteria for each milestone.
- Which tasks can be implemented immediately and which require the final 3D model.

**Do not assume the final Higgsfield model has already been approved or imported.**

Begin by preparing the project architecture and an isolated character test scene. Avoid modifying existing gameplay systems unnecessarily.

Before implementing complex rigging or AI integration, present the proposed plan for approval.

### Ultimate Goal

Professor Quack should feel like an expressive, intelligent animated teacher rather than a static 3D model playing audio.

He should explain concepts, react to student decisions, show emotion, gesture toward examples, occasionally behave like a duck, and seamlessly transition between desktop and VR learning experiences.