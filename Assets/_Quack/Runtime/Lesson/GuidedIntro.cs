using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// The opening, revealed one thing at a time: the TV tells the story → one real egg is measured →
    /// a few eggs of different sizes → the mass and length axes appear → the eggs fly into the plot as points →
    /// the rest of the cloud fills in → the inspectors' labels recolour every egg and the cartons light up.
    /// Every step is captioned; "Skip intro" jumps straight to the finished stage-A scene.
    /// </summary>
    public sealed class GuidedIntro : MonoBehaviour
    {
        public GameState State;
        public FeaturePlot Plot;
        public DecisionPlaneView PlaneView;
        public RuleConsole Console;
        public SpeechQueue Speech;
        public DesktopInput Desktop;
        public TVScreen TV;
        public HighlightManager Highlight;
        public Hud Hud;
        public AccessSettings Access;
        public QuackPalette Palette;
        public Transform Showcase;
        public VoiceClient Voice;
        public Transform Quack;              // Professor Quack's root (front shot)
        public StationDirector Stations;
        public GameObject[] HideDuringIntro; // panels/holograms not introduced yet (training desk, landscape...)
        public RectTransform MissionScreen;   // optional: the whole fixed script is voiced in advance so lines start spoken

        const string Hello = "Ah, there you are! Welcome to Professor Quack's Egg and AI Emporium. I am Professor Quack: CEO, professor, and, between us, a duck.";
        const string TourIntro = "Before we start, a quick tour of the factory floor.";
        const string TourMission = "Up here is your mission screen: the lesson, your checklist and your score. Next step tells you what to do if you're ever unsure.";
        const string TourTable = "This is the hologram table. Every egg the inspectors measured lives here as a point.";
        const string TourLine = "Over here is the sorting line. Whatever rule you build, the belt sorts eggs into these two trays, live.";
        const string TourBoard = "On this wall, the whiteboard. After each lesson I write down what you discovered.";
        const string TourCaptions = "And my words always appear at the bottom of the screen. Ask me anything with Hint, the question box, or by holding V. Right, let's begin!";
        const string Welcome = "Now, we have a little crisis. Watch the screen.";
        const string Crisis = "So: eggs are going into the wrong cartons, and you and I are going to build a sorter that learns from the inspectors' examples.";
        const string Several = "Every egg gets its own pair of numbers. Small eggs, small numbers; big eggs, bigger ones. But a pile of numbers is hard to compare at a glance.";
        const string AxisMass = "So let's give each number a direction. Mass runs from left to right...";
        const string AxisLength = "...and length runs upward.";
        const string FlyLine = "Now watch. Each egg flies to the spot that matches its mass and its length. That pair of numbers is the egg's feature vector.";
        const string AllEggs = "Here are all of the inspectors' eggs. Points close together are eggs with similar measurements, not eggs sitting next to each other on the floor.";
        const string Labels = "Finally, the inspectors' labels. The big golden eggs with a dark band went in the JUMBO carton; the smaller blue eggs went in the STANDARD carton.";

        bool skip;
        readonly List<GameObject> props = new List<GameObject>();

        public IEnumerator Run(Action done)
        {
            skip = false;
            var all = State.Dataset.Train.OrderBy(e => e.Raw[0]).ToList();
            var picks = new[] { all[all.Count / 2], all[all.Count / 20], all[all.Count * 3 / 10], all[all.Count * 7 / 10], all[all.Count * 19 / 20] };
            var first = picks[0];
            Prefetch(new[]
            {
                Welcome, Crisis, $"Before anything is sorted, our machine measures every egg. This one weighs {first.Raw[0]:0.0} grams...",
                $"...and it is {first.Raw[1]:0.0} millimetres long. Those two numbers are all the sorter gets to see.",
                Several, AxisMass, AxisLength, FlyLine, AllEggs, Labels,
                Hello, TourIntro, TourMission, TourTable, TourLine, TourBoard, TourCaptions,
            });
            Hud.ShowSkip("Skip intro  >>", () => { skip = true; Speech.ClearAll(); });

            // nothing but the room, the TV, the sorting line and Professor Quack
            State.SetWidthLocked(true);
            Plot.SetDepthScale(0);
            if (PlaneView) PlaneView.Visible = false;
            if (Console) Console.Root.gameObject.SetActive(false);
            Plot.SetFrameVisible(false); Plot.SetAxisVisible(0, false); Plot.SetAxisVisible(1, false);
            Plot.ShowOnly(new int[0]);
            Plot.SetNeutral(true);
            if (HideDuringIntro != null) foreach (var g in HideDuringIntro) if (g) g.SetActive(false);
            if (MissionScreen) MissionScreen.gameObject.SetActive(false);

            // 0) Professor Quack greets the learner: a clean front shot of him
            if (Quack != null && Desktop != null)
            {
                Desktop.LookAt(Quack.position + Vector3.up * 1.15f, Quack.eulerAngles.y + 180f, 4f, 2.3f, 200);
            }
            Say(Hello, Emotion.Happy, Gesture.TieAdjust);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 1) the story on the factory TV: Professor Quack walks over and stands beside it (never in front of it)
            if (Desktop != null) Desktop.SlowNext = true;
            if (Stations != null) Stations.Go("tv", fromLearner: false, force: true); else Desktop?.SetView(7);
            Say(Welcome,
                Emotion.MockSerious, Gesture.TieAdjust);
            bool storyDone = false;
            StartCoroutine(Track(TV.PlayStory(Access.ReducedMotion ? 7f : 5.5f, () => skip), () => storyDone = true));
            yield return Until(() => storyDone && Speech.IsIdle);
            Say(Crisis,
                Emotion.Encouraging, Gesture.Nod);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 2) one egg, measured (face-on view: the plot will be flat, mass across and length up); he walks back
            Stations?.Go("table", fromLearner: false, force: true);
            Desktop?.SetView(1);
            TV.Idle();
            // the measuring station on the table: a digital scale and a height gauge (caliper); the camera comes close
            Desktop?.LookAt(Showcase.position + new Vector3(0.05f, 0.1f, 0), 0, 16, 0.95f, 210);
            var (scaleLcd, gaugeLcd, gaugeArm, gaugeTop) = BuildMeasuringStation();
            var hero = MakeShowEgg(first, new Vector3(0, ScaleTop, 0), 1.0f);
            yield return Wait(0.6f);
            Highlight.Register("intro_mass", scaleLcd.gameObject);
            Highlight.Pulse("intro_mass");
            StartCoroutine(CountUp(scaleLcd, (float)first.Raw[0], "0.0", " g", 1.2f));
            Say($"Before anything is sorted, our machine measures every egg. On the scale, this one weighs {first.Raw[0]:0.0} grams...", Emotion.Curious, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            float eggTop = ScaleTop + hero.transform.localScale.y;
            StartCoroutine(LowerArm(gaugeArm, gaugeTop, eggTop, 1.0f));
            Highlight.Register("intro_len", gaugeLcd.gameObject);
            Highlight.Pulse("intro_len");
            StartCoroutine(CountUp(gaugeLcd, (float)first.Raw[1], "0.0", " mm", 1.2f));
            Say($"...and the gauge says it is {first.Raw[1]:0.0} millimetres long. Those two numbers are all the sorter gets to see.", Emotion.Thinking, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 3) a few eggs of different sizes, each with its own pair of numbers
            float[] xs = { 0f, -0.46f, -0.28f, 0.3f, 0.48f };
            Desktop?.LookAt(Showcase.position + new Vector3(0, 0.08f, 0), 0, 14, 1.45f, 211);
            var showEggs = new List<(Egg egg, GameObject go)> { (first, hero) };
            for (int i = 1; i < picks.Length; i++)
            {
                var go = MakeShowEgg(picks[i], new Vector3(xs[i], 0, 0.02f), 1.0f);
                Tag(go, $"{picks[i].Raw[0]:0} g\n{picks[i].Raw[1]:0} mm", new Vector3(0, go.transform.localScale.y / 2 + 0.05f, 0));
                showEggs.Add((picks[i], go));
                yield return Wait(0.35f);
            }
            Say(Several,
                Emotion.Encouraging, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 4) give each number a direction (back to the face-on plot view)
            Desktop?.SetView(1);
            Plot.SetFrameVisible(true);
            Plot.SetAxisVisible(0, true);
            Highlight.Register("axis_mass", Plot.AxisObjects(0));
            Highlight.Pulse("axis_mass", 3f);
            Say(AxisMass, Emotion.Thinking, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            Plot.SetAxisVisible(1, true);
            Highlight.Register("axis_length", Plot.AxisObjects(1));
            Highlight.Pulse("axis_length", 3f);
            Say(AxisLength, Emotion.Thinking, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 5) the eggs fly to their spots: each one becomes a point
            Say(FlyLine,
                Emotion.Excited, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Fly(showEggs);
            Plot.ShowOnly(picks.Select(e => e.Id));
            ClearProps();
            yield return Until(() => Speech.IsIdle);
            // the rest of the inspectors' eggs fill in
            var shown = new HashSet<int>(picks.Select(e => e.Id));
            var rest = State.Dataset.Train.Where(e => !shown.Contains(e.Id)).ToList();
            int batches = Access.ReducedMotion ? 1 : 10;
            for (int b = 0; b < batches && !skip; b++)
            {
                foreach (var e in rest.Skip(b * rest.Count / batches).Take(rest.Count / batches + 1)) shown.Add(e.Id);
                Plot.ShowOnly(shown);
                yield return Wait(0.12f);
            }
            Plot.ShowOnly(null);
            Say(AllEggs,
                Emotion.Curious, Gesture.PointAtPlot, GazeTarget.Plot);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 6) the inspectors' labels: colour AND shape, and the cartons they belong in (rule view: plot + trays in frame)
            if (Desktop != null) { Desktop.SlowNext = true; Desktop.SetView(8); }
            Say(Labels,
                Emotion.Encouraging, Gesture.PointAtConveyor, GazeTarget.Conveyor);
            var views = Plot.Eggs.ToList();
            int perFrame = Access.ReducedMotion ? views.Count : Mathf.Max(1, views.Count / 30);
            for (int i = 0; i < views.Count; i++)
            {
                Plot.RevealLabel(views[i]);
                if (i % perFrame == 0) yield return null;
            }
            Highlight.Pulse("carton_jumbo", 3f);
            Highlight.Pulse("carton_standard", 3f);
            yield return Until(() => Speech.IsIdle);
            if (skip) { Finish(done); yield break; }

            // 7) scene tour: show where everything is before the first exercise
            Say(TourIntro, Emotion.Encouraging, Gesture.Nod);
            yield return Until(() => Speech.IsIdle);
            if (MissionScreen) MissionScreen.gameObject.SetActive(true);
            yield return TourStop(null, 0, TourMission, "mission", GazeTarget.Plot);
            yield return TourStop("table", 1, TourTable, "axes", GazeTarget.Plot);
            yield return TourStop("conveyor", -1, TourLine, "conveyor", GazeTarget.Conveyor);
            yield return TourStop("board", -1, TourBoard, "whiteboard", GazeTarget.Whiteboard);
            yield return TourStop("table", 1, TourCaptions, null, GazeTarget.Learner);
            Finish(done);
        }

        /// <summary>One stop of the scene tour: glide there (Quack walks via the station director), flash it, explain it.</summary>
        IEnumerator TourStop(string station, int preset, string line, string highlight, GazeTarget gaze)
        {
            if (skip) yield break;
            if (Desktop != null) Desktop.SlowNext = true;
            if (station != null && Stations != null && station != "table") Stations.Go(station, fromLearner: false, force: true);
            else
            {
                if (station == "table" && Stations != null) Stations.Go("table", fromLearner: false, force: true);
                if (Desktop != null) { Desktop.SlowNext = true; Desktop.SetView(preset >= 0 ? preset : 0); }
            }
            yield return Wait(1.0f);
            if (highlight != null) Highlight.Pulse(highlight, 3f);
            Say(line, Emotion.Encouraging, Gesture.PointAtPanel, gaze);
            yield return Until(() => Speech.IsIdle);
        }

        void Finish(Action done)
        {
            StopAllCoroutines();
            ClearProps();
            Speech.ClearAll();
            Plot.SetFrameVisible(true); Plot.SetAxisVisible(0, true); Plot.SetAxisVisible(1, true);
            Plot.ShowOnly(null);
            Plot.SetNeutral(false);
            if (MissionScreen) MissionScreen.gameObject.SetActive(true);
            TV.Idle();
            Hud.ShowSkip(null, null);
            Desktop?.SetView(1);
            done?.Invoke();
        }

        // ---------- helpers ----------

        readonly Dictionary<string, TutorLine> script = new Dictionary<string, TutorLine>();

        void Say(string text, Emotion emo, Gesture g, GazeTarget gaze = GazeTarget.Learner)
        {
            if (skip) return;
            if (!script.TryGetValue(text, out var line)) line = new TutorLine(text, emo, g, TutorPriority.Teaching) { Gaze = gaze };
            line.Emotion = emo; line.Gesture = g; line.Gaze = gaze;
            Speech.Say(line);
        }

        /// <summary>Ask for every intro line's voice now (cached by the backend after the first run).</summary>
        void Prefetch(IEnumerable<string> texts)
        {
            foreach (var t in texts)
            {
                var line = new TutorLine(t, Emotion.Neutral, Gesture.None, TutorPriority.Teaching);
                script[t] = line;
                Voice?.Prepare(line);
            }
        }

        IEnumerator Until(Func<bool> cond) { while (!skip && !cond()) yield return null; }
        IEnumerator Wait(float s) { for (float t = 0; t < s && !skip; t += Time.deltaTime) yield return null; }
        IEnumerator Track(IEnumerator inner, Action after) { yield return inner; after(); }

        /// <summary>A real-looking egg (cream ellipsoid) scaled from its actual length; mass shown by its label.</summary>
        GameObject MakeShowEgg(Egg e, Vector3 local, float k)
        {
            var go = VizUtil.Primitive(PrimitiveType.Sphere, $"showcase egg #{e.Id}", Showcase, Palette.Cream);
            float len = (float)e.Raw[1] / 1000f * 1.9f * k;          // ~1.9x life size so it reads from the learner's spot
            go.transform.localScale = new Vector3(len * 0.76f, len, len * 0.76f);
            go.transform.localPosition = local + Vector3.up * (len / 2);   // standing on the table / scale
            props.Add(go);
            return go;
        }

        const float ScaleTop = 0.05f;   // height of the scale platform above the table

        /// <summary>Digital scale (green LCD) under the showcase egg and a height gauge beside it.</summary>
        (TMPro.TextMeshPro scaleLcd, TMPro.TextMeshPro gaugeLcd, Transform gaugeArm, float gaugeTop) BuildMeasuringStation()
        {
            Material M(Color c) { var m = new Material(Palette.LabTable); m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); return m; }
            var body = M(new Color(0.92f, 0.92f, 0.9f)); var dark = M(new Color(0.08f, 0.1f, 0.09f)); var brass = M(new Color(0.85f, 0.65f, 0.28f));
            GameObject Box(string n, Material m, Vector3 p, Vector3 sz, PrimitiveType t = PrimitiveType.Cube)
            { var g = VizUtil.Primitive(t, n, Showcase, m); g.transform.localPosition = p; g.transform.localScale = sz; props.Add(g); return g; }
            // scale: body, platform, LCD on the front edge
            Box("scale body", body, new Vector3(0, 0.02f, 0), new Vector3(0.2f, 0.04f, 0.17f));
            Box("scale platform", M(new Color(0.75f, 0.77f, 0.8f)), new Vector3(0, 0.045f, 0.005f), new Vector3(0.14f, 0.005f, 0.14f), PrimitiveType.Cylinder);
            Box("scale display", dark, new Vector3(0, 0.022f, -0.087f), new Vector3(0.12f, 0.028f, 0.004f));
            var sl = VizUtil.Label("0.0 g", Showcase, new Vector3(0, 0.022f, -0.09f), 0.032f, new Color(0.45f, 1f, 0.6f), billboard: false);
            if (VizUtil.TmpLcd != null) sl.font = VizUtil.TmpLcd; sl.transform.localRotation = Quaternion.identity;
            props.Add(sl.gameObject);
            // height gauge: base, column, sliding arm, display
            var gx = 0.16f;
            Box("gauge base", dark, new Vector3(gx, 0.01f, 0.02f), new Vector3(0.08f, 0.02f, 0.08f));
            Box("gauge column", brass, new Vector3(gx, 0.13f, 0.03f), new Vector3(0.012f, 0.24f, 0.012f));
            var arm = Box("gauge arm", brass, new Vector3(gx - 0.06f, 0.24f, 0.03f), new Vector3(0.12f, 0.008f, 0.012f)).transform;
            Box("gauge display", dark, new Vector3(gx + 0.01f, 0.07f, 0.0f), new Vector3(0.05f, 0.03f, 0.004f));
            var gl = VizUtil.Label("0.0 mm", Showcase, new Vector3(gx + 0.01f, 0.07f, -0.003f), 0.022f, new Color(0.45f, 1f, 0.6f), billboard: false);
            if (VizUtil.TmpLcd != null) gl.font = VizUtil.TmpLcd; gl.transform.localRotation = Quaternion.identity;
            props.Add(gl.gameObject);
            return (sl, gl, arm, 0.24f);
        }

        IEnumerator CountUp(TMPro.TextMeshPro lcd, float target, string fmt, string unit, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (lcd == null) yield break;
                float k = Mathf.SmoothStep(0, 1, t / seconds);
                lcd.text = (target * k + UnityEngine.Random.Range(-0.4f, 0.4f) * (1 - k)).ToString(fmt) + unit;   // a little digital flicker while settling
                yield return null;
            }
            if (lcd) lcd.text = target.ToString(fmt) + unit;
        }

        IEnumerator LowerArm(Transform arm, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds && arm != null; t += Time.deltaTime)
            {
                var p = arm.localPosition; p.y = Mathf.Lerp(from, to + 0.004f, Mathf.SmoothStep(0, 1, t / seconds)); arm.localPosition = p;
                yield return null;
            }
        }

        TMPro.TextMeshPro Tag(GameObject egg, string text, Vector3 offset)
        {
            var tm = VizUtil.Label(text, Showcase, egg.transform.localPosition + offset, 0.075f, new Color(0.15f, 0.12f, 0.1f));
            props.Add(tm.gameObject);
            return tm;
        }

        IEnumerator Fly(List<(Egg egg, GameObject go)> eggs)
        {
            // hide the number tags; the eggs carry their numbers to the plot
            foreach (var p in props) if (p && p.GetComponent<TMPro.TextMeshPro>()) p.SetActive(false);
            var from = eggs.Select(x => (x.go.transform.position, x.go.transform.localScale)).ToList();
            float worldEgg = FeaturePlot.EggSize * Plot.transform.lossyScale.x;
            float dur = Access.ReducedMotion ? 0f : 1.4f;
            for (float t = 0; t < dur && !skip; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, t / dur);
                for (int i = 0; i < eggs.Count; i++)
                {
                    var target = Plot.transform.TransformPoint(Plot.LocalOf(eggs[i].egg));
                    var arc = Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.12f;
                    eggs[i].go.transform.position = Vector3.Lerp(from[i].position, target, k) + arc;
                    eggs[i].go.transform.localScale = Vector3.Lerp(from[i].localScale, Vector3.one * worldEgg / Showcase.lossyScale.x, k);
                }
                yield return null;
            }
        }

        void ClearProps()
        {
            foreach (var p in props) if (p) Destroy(p);
            props.Clear();
        }
    }
}
