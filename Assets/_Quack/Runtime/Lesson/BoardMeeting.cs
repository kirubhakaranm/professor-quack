using System;
using System.Collections;
using System.Collections.Generic;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// The ending: Professor Quack presents the learner's results to the Board of Ducks on the factory TV
    /// (numbers taken from the real model), the board quacks its approval, feather-confetti falls, and the
    /// "Mission complete" card lists the skills learned. All of it is captioned; confetti is skipped with reduced motion.
    /// </summary>
    public sealed class BoardMeeting : MonoBehaviour
    {
        public GameState State;
        public SpeechQueue Speech;
        public DesktopInput Desktop;
        public TVScreen TV;
        public Hud Hud;
        public AccessSettings Access;
        public QuackPalette Palette;
        public Transform TVAnchor;
        public Action PlayAgain;
        public StationDirector Stations;

        readonly List<Transform> board = new List<Transform>();

        public IEnumerator Run(int shipmentSize, int flagged)
        {
            var before = LogisticModel.Evaluate(QuackGame.OldSorterParams(), State.Space.Train);
            var now = State.TrainResult;
            var test = LogisticModel.Evaluate(State.Params, State.Space.Test);
            if (Stations != null) { Stations.Suspended = true; Stations.Go("tv", fromLearner: false); }
            else Desktop?.SetView(7);
            Desktop?.SetView(7);
            BuildBoard();
            TV.PrepareFinale();
            TV.ShowReport("QUARTERLY BOARD MEETING",
                $"Misrouted eggs:  {before.Misrouted} (old sorter)  ->  {now.Misrouted}\n" +
                $"Brand-new eggs sorted correctly:  {test.Correct} / {test.Total}\n" +
                (shipmentSize > 0 ? $"Shipment:  {shipmentSize:N0} eggs sorted,  {flagged:N0} sent to an inspector" : "Shipment:  ready to go"),
                new Color(0.1f, 0.18f, 0.3f));

            Say("Ahem. Members of the board. Thanks to our new trainee, the sorting crisis is over.", Emotion.MockSerious, Gesture.TieAdjust);
            Say($"Misrouted eggs went from {before.Misrouted} down to {now.Misrouted}, and on eggs it had never seen, the sorter got {test.Correct} of {test.Total} right.",
                Emotion.Proud, Gesture.PointAtPanel);
            yield return Until(() => Speech.IsIdle);

            // the board meeting on the TV (generated clip, captioned); its own soundtrack carries the cheering
            yield return TV.PlayFinale();
            bool clip = TV.PlayedFinale;

            // the board approves
            Speech.Say(new TutorLine("The board is delighted. Motion to buy everyone more bread, approved!", Emotion.Laughing, Gesture.Celebrate, TutorPriority.Teaching)
                { NonVerbal = "[applause] [the board quacks approvingly]", Duck = DuckBehavior.Quack });
            StartCoroutine(BoardCheers(3f));
            if (Access.VoiceOn && !clip) Applause();   // sound is optional: the caption says [applause]
            if (!Access.ReducedMotion) Confetti();
            yield return Until(() => Speech.IsIdle);

            var t = test;
            Speech.Say(new TutorLine("Congratulations! You have learned the basics of machine learning: you built a classification algorithm that learns from examples, and proved it works on eggs it had never seen.",
                Emotion.Delighted, Gesture.FlapSmall, TutorPriority.Teaching) { Gaze = GazeTarget.Learner });
            Hud.ShowFinale("CONGRATULATIONS!",
                $"You learned the basics of machine learning: a classifier that sorted {t.Correct}/{t.Total} brand-new eggs correctly.",
                LessonDirector.Outcomes, PlayAgain, () => Hud.HideFinale());
        }

        void Say(string text, Emotion e, Gesture g) =>
            Speech.Say(new TutorLine(text, e, g, TutorPriority.Teaching) { Gaze = GazeTarget.Learner });

        IEnumerator Until(Func<bool> c) { float t0 = Time.time; while (!c() && Time.time - t0 < 60f) yield return null; }

        /// <summary>Three small duck board members facing the TV (simple, separate from Professor Quack).</summary>
        void BuildBoard()
        {
            if (board.Count > 0) return;
            var root = new GameObject("Board of Ducks").transform;
            var tv = TVAnchor.position;
            for (int i = 0; i < 3; i++)
            {
                var d = new GameObject($"board member {i + 1}").transform;
                d.SetParent(root, false);
                d.position = new Vector3(tv.x + 1.1f, 0, tv.z - 0.6f + i * 0.6f);
                d.rotation = Quaternion.LookRotation(new Vector3(tv.x, 0, tv.z) - new Vector3(d.position.x, 0, d.position.z) + new Vector3(0, 0, -0.6f));
                Part(d, PrimitiveType.Sphere, Palette.Duck, new Vector3(0, 0.28f, 0), new Vector3(0.34f, 0.3f, 0.4f));
                Part(d, PrimitiveType.Sphere, Palette.Duck, new Vector3(0, 0.55f, 0.1f), Vector3.one * 0.22f);
                Part(d, PrimitiveType.Cube, Palette.DuckBeak, new Vector3(0, 0.53f, 0.23f), new Vector3(0.1f, 0.04f, 0.1f));
                Part(d, PrimitiveType.Cube, i == 1 ? Palette.Tie : Palette.Navy, new Vector3(0, 0.36f, 0.19f), new Vector3(0.06f, 0.1f, 0.02f));
                Part(d, PrimitiveType.Sphere, Palette.DuckDark, new Vector3(0.06f, 0.6f, 0.19f), Vector3.one * 0.03f);
                Part(d, PrimitiveType.Sphere, Palette.DuckDark, new Vector3(-0.06f, 0.6f, 0.19f), Vector3.one * 0.03f);
                board.Add(d);
            }
        }

        static void Part(Transform parent, PrimitiveType t, Material m, Vector3 pos, Vector3 scale)
        {
            var g = VizUtil.Primitive(t, "part", parent, m);
            g.transform.localPosition = pos; g.transform.localScale = scale;
        }

        IEnumerator BoardCheers(float seconds)
        {
            var baseY = new float[board.Count];
            for (int i = 0; i < board.Count; i++) baseY[i] = board[i].position.y;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                for (int i = 0; i < board.Count; i++)
                {
                    float hop = Access.ReducedMotion ? 0 : Mathf.Abs(Mathf.Sin(t * 7f + i * 1.3f)) * 0.08f;
                    var p = board[i].position; p.y = baseY[i] + hop; board[i].position = p;
                }
                yield return null;
            }
            for (int i = 0; i < board.Count; i++) { var p = board[i].position; p.y = baseY[i]; board[i].position = p; }
        }

        /// <summary>Rich confetti: paper pieces that flutter and tumble from both sides and overhead, plus metallic streamers,
        /// in Professor Quack's colours (gold, navy, burgundy, white).</summary>
        void Confetti()
        {
            var cam = Camera.main; if (cam == null) return;
            var shader = Shader.Find("Sprites/Default");   // always included in builds
            var paper = new Material(shader);
            var gradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(1f, 0.8f, 0.25f), 0f), new GradientColorKey(new Color(0.1f, 0.16f, 0.45f), 0.3f),
                    new GradientColorKey(new Color(0.6f, 0.08f, 0.16f), 0.6f), new GradientColorKey(Color.white, 0.8f), new GradientColorKey(new Color(1f, 0.9f, 0.5f), 1f),
                },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) },
            };
            var right = cam.transform.right; var fwd = cam.transform.forward;
            // three cannons: left, right (angled inward and up) and a shower from overhead
            Burst(cam.transform.position + fwd * 1.6f - right * 1.4f + Vector3.down * 0.4f, (Vector3.up * 1.6f + right * 0.9f + fwd * 0.3f).normalized, paper, gradient, 260, 4.5f, 0.0f, 0.035f);
            Burst(cam.transform.position + fwd * 1.6f + right * 1.4f + Vector3.down * 0.4f, (Vector3.up * 1.6f - right * 0.9f + fwd * 0.3f).normalized, paper, gradient, 260, 4.5f, 0.15f, 0.035f);
            Burst(cam.transform.position + fwd * 1.8f + Vector3.up * 1.4f, Vector3.down, paper, gradient, 320, 1.2f, 0.5f, 0.03f, shower: true);
            Burst(cam.transform.position + fwd * 1.8f + Vector3.up * 1.4f, Vector3.down, paper, gradient, 40, 1.0f, 0.6f, 0.02f, shower: true, streamers: true);
        }

        void Burst(Vector3 pos, Vector3 dir, Material mat, Gradient colors, int count, float speed, float delay, float size, bool shower = false, bool streamers = false)
        {
            var go = new GameObject(streamers ? "confetti streamers" : "confetti");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = false; main.playOnAwake = false; main.startDelay = delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            main.startSpeed = shower ? new ParticleSystem.MinMaxCurve(0.2f, 0.8f) : new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize3D = true;
            main.startSizeX = streamers ? new ParticleSystem.MinMaxCurve(0.012f, 0.018f) : new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
            main.startSizeY = streamers ? new ParticleSystem.MinMaxCurve(0.25f, 0.45f) : new ParticleSystem.MinMaxCurve(size * 0.45f, size * 0.7f);   // paper rectangles / long streamers
            main.startSizeZ = 1f;
            main.gravityModifier = shower ? 0.08f : 0.22f;
            main.maxParticles = 1000;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            if (shower) { shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(3.2f, 0.2f, 1.6f); go.transform.rotation = Quaternion.identity; }
            else { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 22; shape.radius = 0.08f; }
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;   // tumbling paper
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var noise = ps.noise; noise.enabled = true; noise.strength = shower ? 0.35f : 0.5f; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f;   // flutter
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.limit = 1.2f; drag.dampen = 0.08f;   // air resistance: they float down
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.Local;
            r.sharedMaterial = mat;
            ps.Play();
            Destroy(go, 10f);
        }

        /// <summary>Applause, synthesised in-game (no audio file): hundreds of short filtered claps that swell, peak and taper.</summary>
        void Applause(float seconds = 5f)
        {
            const int rate = 44100;
            int n = Mathf.RoundToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(7);
            int claps = 900;
            for (int c = 0; c < claps; c++)
            {
                // clap times follow a swell: dense in the middle, sparse at the ends
                double u = rng.NextDouble(); double t = seconds * (0.5 + 0.5 * System.Math.Sin((u - 0.5) * System.Math.PI)) * 0.95;
                int start = (int)(t * rate);
                float gain = 0.25f + 0.35f * (float)rng.NextDouble();
                float decay = 0.004f + 0.006f * (float)rng.NextDouble();           // 4-10 ms
                float lp = 0f, hpPrev = 0f, hpOut = 0f;
                for (int k = 0; k < (int)(decay * 6 * rate) && start + k < n; k++)
                {
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    lp += 0.45f * (noise - lp);                                      // soften the top
                    hpOut = 0.9f * (hpOut + lp - hpPrev); hpPrev = lp;               // remove rumble
                    data[start + k] += hpOut * gain * Mathf.Exp(-k / (decay * rate));
                }
            }
            float peak = 0.001f; foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Clamp01(i / (0.6f * rate)) * Mathf.Clamp01((n - i) / (1.2f * rate));   // fade in / out
                data[i] = data[i] / peak * 0.55f * env;
            }
            var clip = AudioClip.Create("applause", n, 1, rate, false);
            clip.SetData(data, 0);
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0; src.volume = 0.7f; src.clip = clip; src.Play();
            Destroy(src, seconds + 0.5f);
        }
    }
}
