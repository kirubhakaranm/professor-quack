using System.Collections.Generic;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Parameter space hologram: the training objective over (w_mass, w_width) with w_length and b HELD at their
    /// current values — explicitly a slice, never the full 4-parameter space. Height = loss computed from the
    /// training data (LossSlice). The ball is the current rule; drag it to change w_mass and w_width.
    /// The slice is recomputed whenever a held parameter changes (e.g. during gradient descent).
    /// </summary>
    public sealed class LandscapeView : MonoBehaviour
    {
        public const int AxisA = 0, AxisB = 2;                 // w_mass (x), w_width (z)
        public const double MinA = -1.5, MaxA = 4.5, MinB = -1.5, MaxB = 5.5;
        public const int Res = 44;
        public const float Size = 0.55f, Height = 0.3f;       // metres
        const double LossCap = 1.4;                            // objective values above this are flattened at the top

        GameState state;
        Transform surfT;
        Mesh mesh;
        MeshCollider surfaceCollider;
        Transform ball, minFlag;
        LineRenderer trail;
        readonly List<Vector3> trailPts = new List<Vector3>();
        TMPro.TextMeshPro slicelabel, title;
        LossSlice slice;
        double heldLength = double.NaN, heldB = double.NaN;

        public double SliceMin => slice?.MinValue ?? 0;
        public Transform Ball => ball;
        public Transform MinFlag => minFlag;
        public double CurrentObjective => LogisticModel.Objective(state.Params, state.Space.Train);

        public void Build(GameState s, QuackPalette pal)
        {
            state = s;
            var surf = new GameObject("loss surface (slice)");
            surfT = surf.transform;
            surf.transform.SetParent(transform, false);
            mesh = new Mesh { name = "LossSlice" };
            mesh.MarkDynamic();
            surf.AddComponent<MeshFilter>().sharedMesh = mesh;
            surf.AddComponent<MeshRenderer>().sharedMaterial = pal.Surface;
            surfaceCollider = surf.AddComponent<MeshCollider>();
            surf.AddComponent<LandscapeSurface>().View = this;

            var b = VizUtil.Primitive(PrimitiveType.Sphere, "current rule (drag)", transform, pal.Handle, keepCollider: true);
            b.transform.localScale = Vector3.one * 0.035f;
            b.AddComponent<Handle>().Type = Handle.Kind.Landscape;
            ball = b.transform;
            var f = VizUtil.Primitive(PrimitiveType.Cylinder, "slice minimum", transform, pal.Marker);
            f.transform.localScale = new Vector3(0.008f, 0.03f, 0.008f);
            minFlag = f.transform;

            trail = VizUtil.Line("descent trail", transform, pal.Line, new Color(0.0f, 0.55f, 0.5f), 0.006f);
            trail.positionCount = 0;

            // frame and labels (raw parameter values are what the console shows)
            var c = new Color(0.25f, 0.25f, 0.3f);
            float h = Size / 2;
            VizUtil.Line("frame", transform, pal.Line, c, 0.003f,
                new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h), new Vector3(-h, 0, -h));
            VizUtil.Line("height axis", transform, pal.Line, c, 0.004f, new Vector3(-h, 0, -h), new Vector3(-h, Height, -h));
            VizUtil.Label("w_mass", transform, new Vector3(0, -0.02f, -h - 0.05f), 0.035f, Color.black);
            VizUtil.Label("w_width", transform, new Vector3(h + 0.06f, -0.02f, 0), 0.035f, Color.black);
            VizUtil.Label("loss\n(height)", transform, new Vector3(-h - 0.05f, Height * 0.8f, -h), 0.03f, Color.black);
            for (double v = -1; v <= 4; v += 1)
                VizUtil.Label(v.ToString("0"), transform, new Vector3(MapA(v), -0.01f, -h - 0.02f), 0.022f, c);
            for (double v = -1; v <= 5; v += 1)
                VizUtil.Label(v.ToString("0"), transform, new Vector3(h + 0.025f, -0.01f, MapB(v)), 0.022f, c);
            title = VizUtil.Label("PARAMETER SPACE: one point = one whole rule", transform, new Vector3(0, Height + 0.12f, 0), 0.04f, new Color(0.1f, 0.15f, 0.35f));
            slicelabel = VizUtil.Label("", transform, new Vector3(0, Height + 0.07f, 0), 0.028f, new Color(0.35f, 0.2f, 0.1f));

            s.ParamsChanged += _ => Refresh(false);
            Refresh(true);
        }

        /// <summary>
        /// Lesson 4 intro: parameter space is built from rules. Sample rules pop in as dots at (w_mass, w_width), each at the
        /// height of its real loss on the training eggs; then the surface rises through them and the dots fade.
        /// </summary>
        public System.Collections.IEnumerator BuildUp()
        {
            if (slice == null || surfT == null) yield break;
            var surfR = surfT.GetComponent<MeshRenderer>();
            surfR.enabled = false; ball.gameObject.SetActive(false); minFlag.gameObject.SetActive(false);
            var dots = new List<(Transform t, Renderer r)>();
            var order = new List<(int i, int k)>();
            for (int i = 1; i < Res; i += 4) for (int k = 1; k < Res; k += 4) order.Add((i, k));
            var rng = new System.Random(7);
            for (int n = order.Count - 1; n > 0; n--) { int m = rng.Next(n + 1); (order[n], order[m]) = (order[m], order[n]); }
            var dotMat = new Material(surfR.sharedMaterial);
            float per = 3.2f / order.Count;
            for (int n = 0; n < order.Count; n++)
            {
                var (i, k) = order[n];
                double v = slice.Values[i, k];
                float t = Mathf.Clamp01((float)((v - slice.MinValue) / LossCap));
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(d.GetComponent<Collider>());
                d.name = "sample rule"; d.transform.SetParent(transform, false);
                d.transform.localPosition = new Vector3(MapA(slice.ParamAt(0, i)), t * Height, MapB(slice.ParamAt(1, k)));
                d.transform.localScale = Vector3.one * 0.014f;
                var r = d.GetComponent<Renderer>(); r.sharedMaterial = dotMat;
                var mpb = new MaterialPropertyBlock(); var col = Color.Lerp(new Color(0.1f, 0.65f, 0.6f), new Color(0.95f, 0.55f, 0.25f), Mathf.Sqrt(t));
                mpb.SetColor("_BaseColor", col); mpb.SetColor("_Color", col); r.SetPropertyBlock(mpb);
                dots.Add((d.transform, r));
                // the first few arrive slowly so the learner can follow "one dot = one rule"
                yield return new WaitForSeconds(n < 5 ? 0.45f : per);
            }
            yield return new WaitForSeconds(0.6f);
            // the surface rises through the dots
            surfR.enabled = true;
            for (float a = 0; a < 1.4f; a += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0, 1, a / 1.4f);
                surfT.localScale = new Vector3(1, Mathf.Max(0.001f, k), 1);
                foreach (var (dt, _) in dots) if (dt) dt.localScale = Vector3.one * 0.014f * (1 - k * 0.8f);
                yield return null;
            }
            surfT.localScale = Vector3.one;
            foreach (var (dt, _) in dots) if (dt) Destroy(dt.gameObject);
            minFlag.gameObject.SetActive(true);
            Refresh(false);
        }

        /// <summary>Skip the intro (reduced motion / leaving the lesson): everything visible at once.</summary>
        public void FinishBuildUp()
        {
            if (surfT == null) return;
            foreach (Transform ch in transform) if (ch.name == "sample rule") Destroy(ch.gameObject);
            surfT.localScale = Vector3.one; surfT.GetComponent<MeshRenderer>().enabled = true;
            minFlag.gameObject.SetActive(true); Refresh(false);
        }

        static float MapA(double a) => (float)((a - MinA) / (MaxA - MinA) - 0.5) * Size;
        static float MapB(double b) => (float)((b - MinB) / (MaxB - MinB) - 0.5) * Size;
        static double UnmapA(float x) => MinA + (x / Size + 0.5) * (MaxA - MinA);
        static double UnmapB(float z) => MinB + (z / Size + 0.5) * (MaxB - MinB);
        float MapY(double j) => (float)(System.Math.Min(LossCap, j - slice.MinValue) / (LossCap) * Height);

        void Refresh(bool force)
        {
            var p = state.Params;
            if (force || System.Math.Abs(p.W[1] - heldLength) > 1e-9 || System.Math.Abs(p.B - heldB) > 1e-9)
            {
                heldLength = p.W[1]; heldB = p.B;
                Recompute(p);
            }
            double j = CurrentObjective;
            ball.localPosition = new Vector3(MapA(p.W[AxisA]), MapY(j) + 0.012f, MapB(p.W[AxisB]));
            bool inside = p.W[AxisA] >= MinA && p.W[AxisA] <= MaxA && p.W[AxisB] >= MinB && p.W[AxisB] <= MaxB;
            ball.gameObject.SetActive(inside);
        }

        void Recompute(ModelParams held)
        {
            slice = new LossSlice(AxisA, AxisB, MinA, MaxA, MinB, MaxB, Res, held);
            slice.Compute(state.Space.Train);
            var verts = new Vector3[Res * Res];
            var cols = new Color[Res * Res];
            int mi = 0, mj = 0;
            for (int i = 0; i < Res; i++)
                for (int k = 0; k < Res; k++)
                {
                    double v = slice.Values[i, k];
                    float t = Mathf.Clamp01((float)((v - slice.MinValue) / LossCap));
                    verts[i * Res + k] = new Vector3(MapA(slice.ParamAt(0, i)), t * Height, MapB(slice.ParamAt(1, k)));
                    cols[i * Res + k] = Color.Lerp(new Color(0.1f, 0.65f, 0.6f, 0.9f), new Color(0.95f, 0.55f, 0.25f, 0.9f), Mathf.Sqrt(t));
                    if (v <= slice.Values[mi, mj]) { mi = i; mj = k; }
                }
            var tris = new int[(Res - 1) * (Res - 1) * 6];
            int n = 0;
            for (int i = 0; i < Res - 1; i++)
                for (int k = 0; k < Res - 1; k++)
                {
                    int a = i * Res + k, b = a + 1, c = a + Res, d = c + 1;
                    tris[n++] = a; tris[n++] = b; tris[n++] = c;
                    tris[n++] = b; tris[n++] = d; tris[n++] = c;
                }
            mesh.Clear();
            mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            surfaceCollider.sharedMesh = null; surfaceCollider.sharedMesh = mesh;
            minFlag.localPosition = verts[mi * Res + mj] + Vector3.up * 0.03f;
            slicelabel.text = $"slice: w_length = {held.W[1]:+0.00;-0.00} and b = {held.B:+0.00;-0.00} held fixed";
        }

        /// <summary>Map a pointer ray onto the surface and return the rule with the hit's (w_mass, w_width).</summary>
        public bool ParamsFromRay(Ray ray, out ModelParams p)
        {
            p = null;
            if (!surfaceCollider.Raycast(ray, out var hit, 20f)) return false;
            var local = transform.InverseTransformPoint(hit.point);
            p = state.Params.Clone();
            p.W[AxisA] = System.Math.Max(MinA, System.Math.Min(MaxA, UnmapA(local.x)));
            p.W[AxisB] = System.Math.Max(MinB, System.Math.Min(MaxB, UnmapB(local.z)));
            return true;
        }

        public void ClearTrail() { trailPts.Clear(); trail.positionCount = 0; }

        public void AddTrailPoint()
        {
            trailPts.Add(ball.localPosition);
            if (trailPts.Count > 200) trailPts.RemoveAt(0);
            trail.positionCount = trailPts.Count;
            trail.SetPositions(trailPts.ToArray());
        }
    }

    /// <summary>Marks the landscape surface collider so pointers can drag across it.</summary>
    public sealed class LandscapeSurface : MonoBehaviour { public LandscapeView View; }
}
