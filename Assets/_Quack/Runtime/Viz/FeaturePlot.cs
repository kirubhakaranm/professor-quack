using System.Collections.Generic;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>Links an egg's scene object to its data.</summary>
    public sealed class EggView : MonoBehaviour
    {
        public Egg Egg;
        public GameObject MisroutedMarker;
    }

    /// <summary>
    /// The egg cloud in feature space. Local coordinates ARE standardised feature units
    /// (x = mass, y = length, z = width); the root has a uniform scale, so angles in the scene
    /// match angles in model space (the weight arrow really is perpendicular to the plane).
    /// Tick labels are raw units.
    /// </summary>
    public sealed class FeaturePlot : MonoBehaviour
    {
        public const float HalfExtent = 3f;           // plot cube spans ±3σ
        public const float EggSize = 0.22f;

        GameState state;
        QuackPalette pal;
        readonly List<EggView> eggs = new List<EggView>();
        readonly List<EggView> testEggs = new List<EggView>();
        public bool TestEggsVisible { get; private set; }

        // evaluation colours: held-out eggs in DARK blue / DARK gold; training eggs fade to pale grey tints while
        // the test set is on show, so the two sets can't be confused (the ring marks held-out eggs too)
        Material testJumbo, testStandard, fadedJumbo, fadedStandard;

        static Material Tint(Material src, Color c)
        {
            var m = new Material(src) { name = src.name + " (" + c + ")" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        Material TrainMat(bool jumbo) => TestEggsVisible ? (jumbo ? fadedJumbo : fadedStandard) : (jumbo ? pal.JumboEgg : pal.StandardEgg);

        public void ShowTestEggs(bool on)
        {
            TestEggsVisible = on;
            if (!Neutral) foreach (var v in eggs) v.GetComponent<Renderer>().sharedMaterial = TrainMat(v.Egg.Label == 1);

            foreach (var v in testEggs) { v.gameObject.SetActive(on); v.transform.localPosition = LocalOf(v.Egg); }
            RefreshPredictions();
        }
        static Mesh heldOutRing;
        GameObject halo;
        TMPro.TextMeshPro testLegend;
        TMPro.TextMeshPro selTag;
        LineRenderer[] dropLines;
        bool isolate;

        public IReadOnlyList<EggView> Eggs => eggs;

        public void Build(GameState s, QuackPalette p)
        {
            state = s; pal = p;
            BuildFrame();
            foreach (var e in s.Dataset.Train) eggs.Add(BuildEgg(e));
            testJumbo = Tint(pal.JumboEgg, new Color(0.72f, 0.5f, 0.0f));       // dark gold
            testStandard = Tint(pal.StandardEgg, new Color(0.05f, 0.15f, 0.5f)); // dark navy blue
            fadedJumbo = Tint(pal.JumboEgg, new Color(0.86f, 0.8f, 0.72f));      // pale greyish orange
            fadedStandard = Tint(pal.StandardEgg, new Color(0.74f, 0.79f, 0.86f)); // pale greyish blue
            // held-out eggs: built now, hidden until the test stage; a translucent shell marks them as unseen
            foreach (var e in s.Dataset.Test)
            {
                var v = BuildEgg(e);
                v.transform.localScale *= 0.8f;
                v.GetComponent<Renderer>().sharedMaterial = e.Label == 1 ? testJumbo : testStandard;
                // held-out marker: a bold dark ring that always faces the viewer (a translucent shell was illegible)
                var ring = new GameObject("held-out ring");
                ring.transform.SetParent(v.transform, false);
                ring.AddComponent<MeshFilter>().sharedMesh = heldOutRing ??= VizUtil.Torus(0.95f, 0.16f, 24, 6);
                var rr = ring.AddComponent<MeshRenderer>(); rr.sharedMaterial = pal.DuckDark;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.AddComponent<Billboard>();
                v.gameObject.SetActive(false);
                testEggs.Add(v);
            }
            BuildSelection();
            s.ParamsChanged += _ => RefreshPredictions();
            s.SelectionChanged += _ => RefreshSelection();
            RefreshPredictions();
            RefreshSelection();
        }

        /// <summary>0 = width not shown (all eggs on the z = 0 sheet, 2-feature view); 1 = full 3D.</summary>
        public float DepthScale { get; private set; } = 1f;
        readonly List<GameObject>[] axisObjs = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
        readonly bool[] axisShown = { true, true, true };
        readonly List<GameObject> frameMisc = new List<GameObject>();   // cube edges + caption
        bool frameShown = true;

        // ---------- guided-intro controls (reveal the plot piece by piece) ----------

        /// <summary>Show/hide the cube outline and caption (the axes are controlled separately).</summary>
        public void SetFrameVisible(bool on) { frameShown = on; ApplyAxisVisibility(); }

        public void SetAxisVisible(int axis, bool on) { axisShown[axis] = on; ApplyAxisVisibility(); }

        public GameObject[] AxisObjects(int axis) => axisObjs[axis].ToArray();

        void ApplyAxisVisibility()
        {
            for (int a = 0; a < 3; a++)
                foreach (var g in axisObjs[a]) if (g) g.SetActive(axisShown[a] && (a < 2 || DepthScale > 0.02f));
            foreach (var g in frameMisc) if (g) g.SetActive(frameShown);
        }

        /// <summary>null = every training egg; otherwise only these ids are shown (intro builds the cloud gradually).</summary>
        HashSet<int> shownIds;
        public void ShowOnly(IEnumerable<int> ids) { shownIds = ids == null ? null : new HashSet<int>(ids); RefreshSelection(); }
        bool Shown(EggView v) => shownIds == null || shownIds.Contains(v.Egg.Id);

        /// <summary>Neutral: every egg the same plain shape and colour (labels not revealed yet).</summary>
        public bool Neutral { get; private set; }
        Mesh sphereMesh, cubeMesh;
        public void SetNeutral(bool on)
        {
            Neutral = on;
            foreach (var v in eggs)
            {
                bool jumbo = v.Egg.Label == 1;
                v.GetComponent<Renderer>().sharedMaterial = on ? pal.Cream : TrainMat(jumbo);
                v.transform.localScale = on ? EggScale(false) : EggScale(jumbo);
                var band = v.transform.Find("size band"); if (band) band.gameObject.SetActive(!on);
            }
            RefreshPredictions();
        }

        /// <summary>Recolour one egg to its inspector label (for a staggered reveal).</summary>
        public void RevealLabel(EggView v)
        {
            bool jumbo = v.Egg.Label == 1;
            v.GetComponent<Renderer>().sharedMaterial = TrainMat(jumbo);
            v.transform.localScale = EggScale(jumbo);
            var band = v.transform.Find("size band"); if (band) band.gameObject.SetActive(true);
        }

        public Vector3 LocalOf(Egg e)
        {
            var v = Clamp(VizUtil.ToV3(state.Vector(e)));
            v.z *= DepthScale;
            return v;
        }

        public void SetDepthScale(float s)
        {
            DepthScale = Mathf.Clamp01(s);
            foreach (var v in eggs) v.transform.localPosition = LocalOf(v.Egg);
            foreach (var v in testEggs) v.transform.localPosition = LocalOf(v.Egg);
            ApplyAxisVisibility();
            // 2-feature view is genuinely flat: the cube collapses to a square, axes and labels onto the egg sheet
            if (frameRoot) frameRoot.localScale = new Vector3(1, 1, Mathf.Max(0.001f, DepthScale));
            foreach (var (t, p) in depthLabels) if (t) t.localPosition = new Vector3(p.x, p.y, p.z * DepthScale);
            RefreshSelection();
            DepthChanged?.Invoke(DepthScale);
        }

        static Vector3 Clamp(Vector3 v) => new Vector3(
            Mathf.Clamp(v.x, -HalfExtent, HalfExtent), Mathf.Clamp(v.y, -HalfExtent, HalfExtent), Mathf.Clamp(v.z, -HalfExtent, HalfExtent));

        bool showMarkers = true;
        /// <summary>Misrouted X marks only make sense once there is a visible sorting rule (stage B on).</summary>
        public bool ShowMarkers { get => showMarkers; set { showMarkers = value; RefreshPredictions(); } }

        bool scaleByLoss;
        public bool ScaleMarkersByLoss { get => scaleByLoss; set { scaleByLoss = value; RefreshPredictions(); } }

        public void SetIsolate(bool on)
        {
            isolate = on;
            RefreshSelection();
        }
        public bool Isolate => isolate;

        // ---------- construction ----------

        // frame lines live under frameRoot (z-scaled with DepthScale); labels are repositioned (never squashed)
        Transform frameRoot;
        readonly List<(Transform t, Vector3 basePos)> depthLabels = new List<(Transform, Vector3)>();
        /// <summary>Raised whenever the plot flattens or extrudes (2-feature view = 0, full 3D = 1).</summary>
        public event System.Action<float> DepthChanged;
        public Transform Frame => frameRoot;

        TMPro.TextMeshPro FrameLabel(string text, Vector3 pos, float size, Color c)
        {
            var tm = VizUtil.Label(text, transform, pos, size, c);
            depthLabels.Add((tm.transform, pos));
            return tm;
        }

        void BuildFrame()
        {
            float h = HalfExtent;
            frameRoot = new GameObject("frame").transform;
            frameRoot.SetParent(transform, false);
            var tmpS = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphereMesh = tmpS.GetComponent<MeshFilter>().sharedMesh; Destroy(tmpS);
            var tmpC = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeMesh = tmpC.GetComponent<MeshFilter>().sharedMesh; Destroy(tmpC);
            var c = new Color(0.35f, 0.35f, 0.4f);
            var corners = new[]
            {
                new Vector3(-h,-h,-h), new Vector3(h,-h,-h), new Vector3(h,h,-h), new Vector3(-h,h,-h),
                new Vector3(-h,-h,h), new Vector3(h,-h,h), new Vector3(h,h,h), new Vector3(-h,h,h),
            };
            int[,] edges = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
            for (int i = 0; i < 12; i++)
                frameMisc.Add(VizUtil.Line("edge", frameRoot, pal.Line, new Color(c.r, c.g, c.b, 0.5f), 0.015f, corners[edges[i, 0]], corners[edges[i, 1]]).gameObject);

            // axes along the back-bottom-left edges, with raw-unit ticks
            string[] titles = { "Mass (g)", "Length (mm)", "Width (mm)" };
            double[] steps = { 5, 2, 2 };
            for (int axis = 0; axis < 3; axis++)
            {
                var dir = Vector3.zero; dir[axis] = 1;
                var origin = new Vector3(-h, -h, -h);
                var axisLine = VizUtil.Line("axis " + titles[axis], frameRoot, pal.Line, Color.black, 0.035f, origin, origin + dir * 2 * h);
                axisObjs[axis].Add(axisLine.gameObject);

                var sc = state.Space.Scaler;
                double rawMin = sc.ToRaw(axis, -h), rawMax = sc.ToRaw(axis, h);
                double step = steps[axis];
                for (double raw = System.Math.Ceiling(rawMin / step) * step; raw <= rawMax; raw += step)
                {
                    float z = (float)sc.ToStd(axis, raw);
                    var pos = origin + dir * (z + h);
                    var outward = axis == 1 ? new Vector3(-0.25f, 0, 0) : new Vector3(0, -0.25f, 0);
                    var tick = VizUtil.Line("tick", frameRoot, pal.Line, Color.black, 0.02f, pos, pos + outward * 0.4f);
                    var tickLabel = FrameLabel(raw.ToString("0"), pos + outward * 1.5f, 0.36f, Color.black);
                    axisObjs[axis].Add(tick.gameObject); axisObjs[axis].Add(tickLabel.gameObject);
                }
                var titleOffset = axis == 1 ? new Vector3(-0.9f, 0, 0) : new Vector3(0, -0.9f, 0);
                var titlePos = origin + dir * h * 1.1f + titleOffset * 1.3f;
                // mass: at the far end of its axis (below the axis it would hide behind the control desk)
                if (axis == 0) titlePos = origin + dir * (2 * h) + new Vector3(1.25f, 0.05f, 0);
                var axisTitle = FrameLabel(axis == 0 ? titles[axis] + " \u2192" : titles[axis], titlePos, 0.5f, new Color(0.08f, 0.08f, 0.1f));
                axisObjs[axis].Add(axisTitle.gameObject);
            }

        }

        /// <summary>Egg proportions; JUMBO eggs are visibly bigger (size is a cue that doesn't rely on colour).</summary>
        public static Vector3 EggScale(bool jumbo) { float k = jumbo ? 1.18f : 0.92f; return new Vector3(EggSize * 0.8f * k, EggSize * k, EggSize * 0.8f * k); }
        static Mesh bandMesh;

        /// <summary>A dark band around a JUMBO egg's middle: a shape cue for players who can't tell the colours apart.</summary>
        public static void AddBand(Transform egg)
        {
            var b = new GameObject("size band").transform; b.SetParent(egg, false);
            b.localRotation = Quaternion.Euler(90, 0, 0); b.localScale = Vector3.one;
            b.gameObject.AddComponent<MeshFilter>().sharedMesh = bandMesh ??= VizUtil.Torus(0.5f, 0.07f, 24, 6);
            var r = b.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = BandMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static Material bandMat;
        static Material BandMat { get { if (bandMat == null) { bandMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default")); bandMat.color = new Color(0.25f, 0.13f, 0.02f); if (bandMat.HasProperty("_BaseColor")) bandMat.SetColor("_BaseColor", bandMat.color); } return bandMat; } }

        EggView BuildEgg(Egg e)
        {
            bool jumbo = e.Label == 1;
            var go = VizUtil.Primitive(PrimitiveType.Sphere, $"Egg {e.Id}", transform,
                jumbo ? pal.JumboEgg : pal.StandardEgg, keepCollider: true);
            if (jumbo) AddBand(go.transform);
            go.transform.localPosition = LocalOf(e);
            go.transform.localScale = EggScale(jumbo);
            var view = go.AddComponent<EggView>();
            view.Egg = e;

            // misrouted marker: a dark "×" (shape cue, not just colour)
            var marker = new GameObject("misrouted ×");
            marker.transform.SetParent(go.transform, false);
            marker.AddComponent<Billboard>();
            for (int k = 0; k < 2; k++)
            {
                var bar = VizUtil.Primitive(PrimitiveType.Cube, "bar", marker.transform, pal.Marker);
                bar.transform.localScale = new Vector3(1.7f, 0.28f, 0.05f);
                bar.transform.localRotation = Quaternion.Euler(0, 0, k == 0 ? 45 : -45);
                bar.transform.localPosition = new Vector3(0, 0, -0.6f);
            }
            view.MisroutedMarker = marker;
            return view;
        }

        void BuildSelection()
        {
            halo = VizUtil.Primitive(PrimitiveType.Sphere, "selection halo", transform, pal.Highlight);
            halo.transform.localScale = Vector3.one * EggSize * 2.6f;
            selTag = VizUtil.Label("", transform, Vector3.zero, 0.5f, new Color(0.1f, 0.1f, 0.1f));
            dropLines = new LineRenderer[3];
            for (int k = 0; k < 3; k++)
                dropLines[k] = VizUtil.Line("drop " + k, transform, pal.Line, QuackPalette.SelectColor * 0.8f, 0.025f, Vector3.zero, Vector3.zero);
        }

        // ---------- refresh ----------

        void RefreshPredictions()
        {
            foreach (var v in TestEggsVisible ? (IEnumerable<EggView>)AllVisible() : eggs)
            {
                bool wrong = ShowMarkers && state.IsMisrouted(v.Egg);
                v.MisroutedMarker.SetActive(wrong);
                // stage C+: the X grows with the egg's loss, so confident mistakes look expensive
                float size = 1f;
                if (wrong && ScaleMarkersByLoss)
                {
                    double z = LogisticModel.Score(state.Params, state.Vector(v.Egg));
                    size = Mathf.Clamp(0.6f + 0.45f * (float)LogisticModel.Bce(z, v.Egg.Label), 0.6f, 2.4f);
                }
                v.MisroutedMarker.transform.localScale = Vector3.one * size;
            }
        }

        void RefreshSelection()
        {
            var sel = state.Selected;
            bool on = sel != null;
            halo.SetActive(on);
            selTag.gameObject.SetActive(false);   // the docked egg card carries the id; no floating tag
            foreach (var d in dropLines) d.gameObject.SetActive(on);
            foreach (var v in eggs) v.gameObject.SetActive(Shown(v) && (!isolate || !on || v.Egg == sel));
            if (!on) return;

            var p = LocalOf(sel);
            halo.transform.localPosition = p;
            selTag.text = $"#{sel.Id}";
            selTag.transform.localPosition = p + new Vector3(0, 0.45f, 0);
            float h = HalfExtent;
            // lines to the three back walls show the egg's coordinates (its feature vector)
            // mass/length axes sit on the back wall, which moves with the depth (flat in the 2-feature view)
            dropLines[0].SetPositions(new[] { p, new Vector3(-h, p.y, p.z) });
            dropLines[1].SetPositions(new[] { p, new Vector3(p.x, -h, p.z) });
            dropLines[2].SetPositions(new[] { p, new Vector3(p.x, p.y, -h * DepthScale) });
            dropLines[2].gameObject.SetActive(DepthScale > 0.02f);
        }

        public EggView ViewOf(Egg e) => eggs.Find(v => v.Egg == e) ?? testEggs.Find(v => v.Egg == e);

        IEnumerable<EggView> AllVisible() { foreach (var v in eggs) yield return v; foreach (var v in testEggs) yield return v; }
    }
}
