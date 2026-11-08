using System;
using System.Collections;
using UnityEngine;

namespace Quack
{


    /// <summary>
    /// Procedural animation for Quacks: emotion poses, gestures, approximate speech beak motion
    /// (amplitude-driven, NOT phoneme lip sync), blinks, gaze, idle bob and occasional preening.
    /// </summary>
    public sealed class QuacksAnimator : MonoBehaviour
    {
        public QuacksRig Rig;
        public AccessSettings Access;
        public Func<Vector3?> GazeTarget;     // world point to look at (e.g. selected egg)
        public Transform PlotTarget, PanelTarget;

        public Emotion Emotion { get; private set; }
        /// <summary>0..1 from the tutor line; scales pose and gesture amplitude.</summary>
        public float Intensity = 0.5f;
        /// <summary>What the current line wants Quack to look at; resolved to a point by GazeTarget.</summary>
        public Quack.GazeTarget Gaze = Quack.GazeTarget.Learner;
        public bool Busy => gestureRoutine != null;

        // speech: amplitude source returns 0..1 for the current playback time
        Func<float> amplitude;
        float jaw, jawVel;

        // aimed pointing: blend each wing toward a rotation that points it at a world target
        Quaternion aimL = Quaternion.identity, aimR = Quaternion.identity;
        float aimWL, aimWR;
        Vector3 restDirL = Vector3.down, restDirR = Vector3.down;   // wing direction at rest, in the wing's parent space
        Vector3 bodyExtra;                                           // hop / step offsets

        // pose state (targets blended every frame)
        Quaternion headBase, wingLBase, wingRBase, tieBase, jawBase;

        /// <summary>
        /// A rotation expressed in the CHARACTER's frame (x = his right->left pitch axis, y = up, z = forward),
        /// converted into t's local frame. Generated rigs have bones with arbitrary rest axes, so every gesture is
        /// applied this way: local' = Delta(t, d) * restLocal. For the stand-in (axes aligned) it equals d.
        /// </summary>
        Quaternion Delta(Transform t, Quaternion d)
        {
            var p = t.parent != null ? t.parent.rotation : Quaternion.identity;
            var r = Rig.transform.rotation;
            return Quaternion.Inverse(p) * (r * d * Quaternion.Inverse(r)) * p;
        }
        Vector3 bodyBase;
        float tilt, pitch, lidOpen = 1f, wingLift;
        Vector3 wingLExtra, wingRExtra, headExtra;
        float nextBlink, blinkT = -1;
        float browUp, browSlant, browAsym;
        float lastActivity;
        Coroutine gestureRoutine;

        void Start()
        {
            headBase = Rig.Head.localRotation; wingLBase = Rig.WingL.localRotation; wingRBase = Rig.WingR.localRotation;
            tieBase = Rig.Tie.localRotation; bodyBase = Rig.Body.localPosition;
            jawBase = Rig.Jaw != null ? Rig.Jaw.localRotation : Quaternion.identity;
            restDirL = RestDir(Rig.WingL); restDirR = RestDir(Rig.WingR);
            nextBlink = Time.time + UnityEngine.Random.Range(2f, 5f);
            lastActivity = Time.time;
        }

        public void NoteActivity() => lastActivity = Time.time;

        public void SetEmotion(Emotion e) => Emotion = e;

        public void StartSpeaking(Func<float> amplitudeSource) { amplitude = amplitudeSource; NoteActivity(); }
        public void StopSpeaking() => amplitude = null;

        public void Play(Gesture g)
        {
            if (g == Gesture.None) return;
            if (gestureRoutine != null) StopCoroutine(gestureRoutine);
            wingLExtra = wingRExtra = headExtra = Vector3.zero;
            aimWL = aimWR = 0; bodyExtra = Vector3.zero;
            gestureRoutine = StartCoroutine(RunGesture(g));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool still = Access != null && Access.ReducedMotion;

            // emotion pose targets
            float tTilt = 0, tPitch = 0, tLid = 1, tWing = 0;
            switch (Emotion)
            {
                case Emotion.Curious: tTilt = 14; tLid = 1.1f; break;
                case Emotion.Thinking: tTilt = -8; tPitch = -10; tLid = 0.7f; break;
                case Emotion.Encouraging: tPitch = 4; tWing = 12; break;
                case Emotion.Delighted: tPitch = -6; tWing = 28; tLid = 0.8f; break;
                case Emotion.MockSerious: tPitch = 6; tLid = 0.55f; break;
                case Emotion.Happy: tPitch = -3; tWing = 10; tLid = 0.9f; break;
                case Emotion.Excited: tPitch = -8; tWing = 30; tLid = 1.2f; break;
                case Emotion.Confused: tTilt = 18; tPitch = 3; tLid = 0.85f; break;
                case Emotion.Surprised: tPitch = -10; tLid = 1.3f; tWing = 8; break;
                case Emotion.Concerned: tTilt = -6; tPitch = 6; tLid = 0.8f; break;
                case Emotion.Proud: tPitch = -12; tLid = 0.7f; tWing = 6; break;
                case Emotion.Laughing: tPitch = -10; tLid = 0.3f; tWing = 15; break;
            }
            float amp = Mathf.Lerp(0.6f, 1.3f, Mathf.Clamp01(Intensity));
            tTilt *= amp; tPitch *= amp; tWing *= amp;
            float k = 1 - Mathf.Exp(-dt * 6);
            tilt = Mathf.Lerp(tilt, tTilt, k); pitch = Mathf.Lerp(pitch, tPitch, k);
            lidOpen = Mathf.Lerp(lidOpen, tLid, k); wingLift = Mathf.Lerp(wingLift, tWing, k);

            // gaze (yaw/pitch toward target, clamped)
            float yaw = 0, gazePitch = 0;
            var target = GazeTarget?.Invoke();
            if (target.HasValue)
            {
                var local = Rig.Body.InverseTransformPoint(target.Value) - Rig.Head.localPosition;
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -40, 40);
                gazePitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20, 20);
            }
            // small nods while speaking (follows the speech amplitude, not phonemes)
            float talkNod = amplitude != null && !still ? Mathf.Sin(Time.time * 6.3f) * 2.2f * jaw : 0f;
            Rig.Head.localRotation = Delta(Rig.Head, Quaternion.Euler(pitch + gazePitch * 0.6f + headExtra.x + talkNod, yaw * 0.7f + headExtra.y, tilt + headExtra.z)) * headBase;

            // wings
            float zs = Rig.WingZSign;   // generated rigs are mirrored relative to the stand-in's wing convention
            var poseL = Delta(Rig.WingL, Quaternion.Euler(wingLExtra.x, wingLExtra.y, zs * (-wingLift + wingLExtra.z))) * wingLBase;
            var poseR = Delta(Rig.WingR, Quaternion.Euler(wingRExtra.x, wingRExtra.y, zs * (wingLift + wingRExtra.z))) * wingRBase;
            Rig.WingL.localRotation = Quaternion.Slerp(poseL, aimL, aimWL);
            Rig.WingR.localRotation = Quaternion.Slerp(poseR, aimR, aimWR);

            // blinking
            if (Time.time > nextBlink && blinkT < 0) blinkT = 0;
            float lid = lidOpen;
            if (blinkT >= 0)
            {
                blinkT += dt;
                lid = Mathf.Min(lid, Mathf.Abs(blinkT - 0.07f) / 0.07f);
                if (blinkT > 0.14f) { blinkT = -1; nextBlink = Time.time + UnityEngine.Random.Range(2f, 6f); }
            }
            // lids rest above the eye; lowering them by LidTravel covers it (lid > 1 = wide-eyed, lifts a little more)
            float drop = (1f - Mathf.Clamp01(lid)) * QuacksRig.LidTravel;   // never above rest: wide eyes come from the brows
            Rig.LidL.localPosition = Rig.LidOpenL + Vector3.down * drop;
            Rig.LidR.localPosition = Rig.LidOpenR + Vector3.down * drop;

            // pupils follow the gaze target within a comfortable range (head does the rest)
            if (target.HasValue && Rig.EyeL != null)
            {
                foreach (var eye in new[] { Rig.EyeL, Rig.EyeR })
                {
                    var dir = eye.parent.InverseTransformDirection(target.Value - eye.position).normalized;
                    var look = Quaternion.LookRotation(dir, Vector3.up);
                    var e = look.eulerAngles;
                    float ex = Mathf.Clamp(Mathf.DeltaAngle(0, e.x), -22, 22), ey = Mathf.Clamp(Mathf.DeltaAngle(0, e.y), -28, 28);
                    eye.localRotation = Quaternion.Slerp(eye.localRotation, Quaternion.Euler(ex, ey, 0), k);
                }
            }

            // eyebrows: height and slant per emotion (positive slant = inner ends down, i.e. stern)
            if (Rig.BrowL != null)
            {
                float bUp = 0, bSlant = 0, bAsym = 0;
                switch (Emotion)
                {
                    case Emotion.Curious: bUp = 0.01f; bAsym = 0.012f; break;
                    case Emotion.Thinking: bUp = -0.004f; bSlant = 8; break;
                    case Emotion.Encouraging: case Emotion.Happy: case Emotion.Proud: bUp = 0.008f; bSlant = -4; break;
                    case Emotion.Delighted: case Emotion.Laughing: bUp = 0.014f; bSlant = -6; break;
                    case Emotion.Excited: bUp = 0.02f; bSlant = -8; break;
                    case Emotion.Surprised: bUp = 0.026f; break;
                    case Emotion.MockSerious: bUp = -0.008f; bSlant = 16; break;
                    case Emotion.Confused: bUp = 0.006f; bAsym = 0.02f; bSlant = 4; break;
                    case Emotion.Concerned: bUp = 0.006f; bSlant = -14; break;
                }
                bUp *= amp * Rig.BrowTravel; bSlant *= amp;
                browUp = Mathf.Lerp(browUp, bUp, k); browSlant = Mathf.Lerp(browSlant, bSlant, k); browAsym = Mathf.Lerp(browAsym, bAsym, k);
                Rig.BrowL.localPosition = Rig.BrowBaseL + Vector3.up * (browUp + browAsym);
                Rig.BrowR.localPosition = Rig.BrowBaseR + Vector3.up * (browUp - browAsym * 0.5f);
                // brow L sits at +X (his left): its inner end is toward -X, so "inner down" is a positive Z rotation for L, negative for R
                Rig.BrowL.localRotation = Quaternion.Euler(0, 0, browSlant);
                Rig.BrowR.localRotation = Quaternion.Euler(0, 0, -browSlant);
            }

            // speech: smooth attack/release follow of the amplitude envelope; closed in pauses
            float targetJaw = amplitude != null ? Mathf.Clamp01(amplitude()) : 0f;
            jaw = Mathf.SmoothDamp(jaw, targetJaw, ref jawVel, targetJaw > jaw ? 0.03f : 0.07f);
            Rig.Jaw.localRotation = Delta(Rig.Jaw, Quaternion.Euler(jaw * Rig.JawOpenDegrees, 0, 0)) * jawBase;

            // idle bob + breathing, disabled for reduced motion; hop/step offsets from gestures
            float breath = Mathf.Sin(Time.time * 2.1f);
            Rig.Body.localPosition = (still ? bodyBase : bodyBase + Vector3.up * (breath * 0.008f)) + bodyExtra;
            if (!still) Rig.Body.localScale = new Vector3(1 - breath * 0.004f, 1 + breath * 0.008f, 1 - breath * 0.004f);

            // occasional preening when nothing has happened for a while
            if (!Busy && amplitude == null && Time.time - lastActivity > 40f)
            {
                lastActivity = Time.time;
                if (!still) Play(Gesture.Preen);
            }
        }

        /// <summary>Wing direction at rest (pivot → wing geometry), in the wing's parent space.</summary>
        Vector3 RestDir(Transform wing)
        {
            if (wing == Rig.WingL && Rig.WingRestL.sqrMagnitude > 0) return wing.parent.InverseTransformDirection(Rig.WingRestL).normalized;
            if (wing == Rig.WingR && Rig.WingRestR.sqrMagnitude > 0) return wing.parent.InverseTransformDirection(Rig.WingRestR).normalized;
            var rs = wing.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return Vector3.down;
            var c = Vector3.zero; foreach (var r in rs) c += r.bounds.center; c /= rs.Length;
            var d = wing.parent.InverseTransformDirection(c - wing.position);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
        }

        /// <summary>Point the wing on the target's side at the target (raised and slightly forward), hold, release.</summary>
        IEnumerator PointAtTarget(float scale)
        {
            var t = GazeTarget?.Invoke();
            if (!t.HasValue) yield break;
            var local = Rig.Body.InverseTransformPoint(t.Value);
            bool useLeft = local.x > 0;   // +X is his left
            var wing = useLeft ? Rig.WingL : Rig.WingR;
            var dir = wing.parent.InverseTransformDirection(t.Value - wing.position).normalized;
            dir.y = Mathf.Clamp(dir.y, -0.2f, 0.6f);                     // a comfortable pointing height
            var forward = wing.parent.InverseTransformDirection(Rig.Body.forward);
            if (Vector3.Dot(dir, forward) < 0.2f) dir = (dir + forward * 0.6f).normalized;   // never point back into the body
            var rest = useLeft ? restDirL : restDirR;
            var baseRot = useLeft ? wingLBase : wingRBase;
            var aim = Quaternion.FromToRotation(rest, dir) * baseRot;
            if (useLeft) aimL = aim; else aimR = aim;
            float w = Mathf.Clamp01(0.85f * scale + 0.15f);
            yield return Tween(v => { if (useLeft) aimWL = v * w; else aimWR = v * w; }, 0.35f);
            yield return new WaitForSeconds(1.3f);
            yield return Tween(v => { if (useLeft) aimWL = (1 - v) * w; else aimWR = (1 - v) * w; }, 0.35f);
            aimWL = aimWR = 0;
        }

        IEnumerator RunGesture(Gesture g)
        {
            bool still = Access != null && Access.ReducedMotion;
            float scale = (still ? 0.35f : 1f) * Mathf.Lerp(0.7f, 1.2f, Mathf.Clamp01(Intensity));
            switch (g)
            {
                case Gesture.PointAtPlot:
                case Gesture.PointAtPanel:
                case Gesture.PointAtConveyor:
                    yield return PointAtTarget(scale);
                    break;
                case Gesture.Nod:
                    yield return Tween(v => headExtra = new Vector3(Mathf.Sin(v * Mathf.PI * 2) * 12 * scale, 0, 0), 0.6f);
                    break;
                case Gesture.Shrug:
                    yield return Tween(v => { float s = Mathf.Sin(v * Mathf.PI) * 30 * scale; wingLExtra = new Vector3(0, 0, -s); wingRExtra = new Vector3(0, 0, s); headExtra = new Vector3(0, 0, s * 0.3f); }, 0.8f);
                    break;
                case Gesture.TieAdjust:
                    yield return Tween(v => wingRExtra = new Vector3(-50 * scale * v, -30 * scale * v, 20 * scale * v), 0.3f);
                    yield return Tween(v => Rig.Tie.localRotation = Delta(Rig.Tie, Quaternion.Euler(0, 0, Mathf.Sin(v * Mathf.PI * 3) * 8 * scale)) * tieBase, 0.5f);
                    yield return Tween(v => wingRExtra = new Vector3(-50 * scale * (1 - v), -30 * scale * (1 - v), 20 * scale * (1 - v)), 0.3f);
                    Rig.Tie.localRotation = tieBase;
                    break;
                case Gesture.AdjustGlasses:
                    // wing up to the face, small nudge, back down
                    yield return Tween(v => { wingRExtra = new Vector3(-95 * scale * v, -20 * scale * v, 35 * scale * v); headExtra = new Vector3(-4 * v, 0, 0); }, 0.35f);
                    var gBase = Rig.Glasses != null ? Rig.Glasses.localPosition : Vector3.zero;
                    yield return Tween(v => { headExtra = new Vector3(-4 + Mathf.Sin(v * Mathf.PI * 2) * 3, 0, 0); if (Rig.Glasses) Rig.Glasses.localPosition = gBase + Vector3.up * (Mathf.Sin(v * Mathf.PI) * 0.012f); }, 0.4f);
                    yield return Tween(v => { wingRExtra = new Vector3(-95 * scale * (1 - v), -20 * scale * (1 - v), 35 * scale * (1 - v)); headExtra = new Vector3(-4 * (1 - v), 0, 0); }, 0.35f);
                    break;
                case Gesture.ThinkPose:
                    // wing to chin, hold, release
                    yield return Tween(v => { wingRExtra = new Vector3(-80 * scale * v, -35 * scale * v, 20 * scale * v); headExtra = new Vector3(6 * v, 0, -8 * v); }, 0.4f);
                    yield return new WaitForSeconds(1.4f);
                    yield return Tween(v => { wingRExtra = new Vector3(-80 * scale * (1 - v), -35 * scale * (1 - v), 20 * scale * (1 - v)); headExtra = new Vector3(6 * (1 - v), 0, -8 * (1 - v)); }, 0.4f);
                    break;
                case Gesture.Celebrate:
                    // celebratory flap with a little hop (hop skipped for reduced motion)
                    yield return Tween(v =>
                    {
                        float s = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 6)) * 45 * scale;
                        wingLExtra = new Vector3(0, 0, -s); wingRExtra = new Vector3(0, 0, s);
                        headExtra = new Vector3(-8 * Mathf.Sin(v * Mathf.PI), 0, 0);
                        bodyExtra = still ? Vector3.zero : Vector3.up * (Mathf.Abs(Mathf.Sin(v * Mathf.PI * 2)) * 0.04f);
                    }, 1.1f);
                    bodyExtra = Vector3.zero;
                    break;
                case Gesture.FlapSmall:
                    yield return Tween(v => { float s = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 4)) * 35 * scale; wingLExtra = new Vector3(0, 0, -s); wingRExtra = new Vector3(0, 0, s); }, 0.8f);
                    break;
                case Gesture.SurprisedQuack:
                    // head jerks back and a small step backwards
                    yield return Tween(v =>
                    {
                        headExtra = new Vector3(-12 * scale * Mathf.Sin(v * Mathf.PI), 0, 0);
                        bodyExtra = still ? Vector3.zero : Vector3.back * (Mathf.Sin(v * Mathf.PI * 0.5f) * 0.04f);
                    }, 0.4f);
                    yield return Tween(v => bodyExtra = still ? Vector3.zero : Vector3.back * ((1 - v) * 0.04f), 0.5f);
                    bodyExtra = Vector3.zero;
                    break;
                case Gesture.Preen:
                    // preens a feather, catches itself, straightens tie, resumes professor posture
                    yield return Tween(v => { headExtra = new Vector3(25 * v, -35 * v, 0); wingLExtra = new Vector3(0, 0, -25 * v); }, 0.5f);
                    yield return Tween(v => headExtra = new Vector3(25, -35 + Mathf.Sin(v * Mathf.PI * 4) * 6, 0), 0.9f);
                    yield return Tween(v => { headExtra = new Vector3(25 * (1 - v), -35 * (1 - v), 0); wingLExtra = new Vector3(0, 0, -25 * (1 - v)); }, 0.3f);
                    gestureRoutine = null;
                    Play(Gesture.TieAdjust);
                    yield break;
            }
            wingLExtra = wingRExtra = headExtra = Vector3.zero;
            gestureRoutine = null;
        }

        /// <summary>Occasional duck habit; the speech queue decides when (never mid-explanation).</summary>
        public void PlayDuck(DuckBehavior d)
        {
            switch (d)
            {
                case DuckBehavior.Quack: Play(Gesture.SurprisedQuack); break;
                case DuckBehavior.EmbarrassedQuack: StartCoroutine(Sequence(Gesture.SurprisedQuack, Gesture.AdjustGlasses)); break;
                case DuckBehavior.WingFlap: Play(Gesture.FlapSmall); break;
                case DuckBehavior.Preen: if (Access == null || !Access.ReducedMotion) Play(Gesture.Preen); break;
                case DuckBehavior.TailWiggle: StartCoroutine(Wiggle()); break;
            }
        }

        IEnumerator Sequence(Gesture a, Gesture b)
        {
            Play(a);
            while (Busy) yield return null;
            Play(b);
        }

        IEnumerator Wiggle()
        {
            if (Access != null && Access.ReducedMotion) yield break;
            var baseRot = Rig.Body.localRotation;
            yield return Tween(v => Rig.Body.localRotation = Delta(Rig.Body, Quaternion.Euler(0, Mathf.Sin(v * Mathf.PI * 4) * 6, 0)) * baseRot, 0.7f);
            Rig.Body.localRotation = baseRot;
        }

        static IEnumerator Tween(Action<float> apply, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) { apply(t / seconds); yield return null; }
            apply(1);
        }
    }
}
