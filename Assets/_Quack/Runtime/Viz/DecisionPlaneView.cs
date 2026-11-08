using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Translucent decision plane (w·x̃ + b = 0) clipped to the plot cube, its weight-vector arrow
    /// (perpendicular, pointing to the JUMBO side, length ∝ ‖w‖), and the two handles.
    /// Rebuilt from GameState.Params whenever they change — never animated independently.
    /// </summary>
    public sealed class DecisionPlaneView : MonoBehaviour
    {
        public const float ArrowPerWeight = 0.45f;   // local units per unit of ‖w‖
        public const float MinArrow = 0.6f, MaxArrow = 3.2f;

        GameState state;
        Mesh mesh;
        GameObject planeGo, arrowRoot, shaft, tip, rotateHandle, translateHandle;
        TMPro.TextMeshPro arrowLabel, slideLabel, noBoundaryLabel;

        public Vector3 PivotLocal { get; private set; }
        public Vector3 NormalLocal { get; private set; }
        public float ArrowLength { get; private set; }

        GameObject ringTurn, ringTilt;
        readonly GameObject[] chevrons = new GameObject[4];
        TMPro.TextMeshPro turnLabel, tiltLabel;
        float ringLen = -1;
        /// <summary>Rotation axes (plot-local) of the two rings, recomputed from the current normal.</summary>
        public Vector3 TurnAxisLocal { get; private set; } = Vector3.forward;
        public Vector3 TiltAxisLocal { get; private set; } = Vector3.right;
        bool Flat => depth < 0.3f;

        GameObject BuildRing(string name, Material mat, Handle.Kind kind)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>();
            go.AddComponent<Handle>().Type = kind;
            return go;
        }

        bool visible = true;
        /// <summary>Stage A hides the sorting rule entirely (it is introduced in stage B).</summary>
        public bool Visible { get => visible; set { visible = value; Refresh(); } }
        public GameObject[] GimbalObjects => new[] { rotateHandle };
        public GameObject[] SheetObjects => new[] { planeGo, edgeLine ? edgeLine.gameObject : null };
        public GameObject[] ArrowObjects => new[] { arrowRoot };
        public GameObject[] BandObjects => bandGo;

        float depth = 1f;
        /// <summary>Follow the plot's flattening: in the 2-feature view the sheet collapses to a boundary LINE.</summary>
        public void SetDepth(float d)
        {
            depth = Mathf.Max(0.001f, d);
            var sc = new Vector3(1, 1, depth);
            if (planeGo) planeGo.transform.localScale = sc;
            foreach (var b in bandGo) if (b) b.transform.localScale = sc;
            if (edgeLine) edgeLine.gameObject.SetActive(d < 0.3f);
            Refresh();
        }
        LineRenderer edgeLine;   // the 2D boundary line (visible when flat)
        /// <summary>2D view: where the boundary line meets the plot's edges (plot-local).</summary>
        public Vector3 SegA { get; private set; }
        public Vector3 SegB { get; private set; }
        public bool HasSegment { get; private set; }
        public bool IsFlat => Flat;

        public void Build(GameState s, QuackPalette pal)
        {
            state = s;
            planeGo = new GameObject("decision plane");
            planeGo.transform.SetParent(transform, false);
            mesh = new Mesh { name = "DecisionPlane" };
            mesh.MarkDynamic();
            planeGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = planeGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pal.Plane;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int k = 0; k < 2; k++)
            {
                bandGo[k] = new GameObject(k == 0 ? "p=0.75 band (JUMBO side)" : "p=0.25 band (STANDARD side)");
                bandGo[k].transform.SetParent(transform, false);
                bandMesh[k] = new Mesh { name = "Band" }; bandMesh[k].MarkDynamic();
                bandGo[k].AddComponent<MeshFilter>().sharedMesh = bandMesh[k];
                var br = bandGo[k].AddComponent<MeshRenderer>();
                // the two confidence sheets get their own colours (not the main sheet's): JUMBO side warm, STANDARD side cool
                var bm = new Material(pal.Band);
                var bc = k == 0 ? new Color(1f, 0.6f, 0.15f, 0.22f) : new Color(0.2f, 0.5f, 1f, 0.22f);
                if (bm.HasProperty("_BaseColor")) bm.SetColor("_BaseColor", bc);
                bm.color = bc;
                br.sharedMaterial = bm;
                bandLabel[k] = VizUtil.Label(k == 0 ? "p = 0.75 (surer JUMBO)" : "p = 0.25 (surer STANDARD)", transform, Vector3.zero, 0.24f,
                    k == 0 ? new Color(0.6f, 0.3f, 0f) : new Color(0.05f, 0.25f, 0.6f));
                bandLabel[k].gameObject.SetActive(false);
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bandGo[k].SetActive(false);
            }

            edgeLine = VizUtil.Line("boundary line (2D view)", transform, pal.Line, new Color(0.55f, 0.05f, 0.3f), 0.06f, Vector3.zero, Vector3.zero);
            edgeLine.gameObject.SetActive(false);

            arrowRoot = new GameObject("weight vector");
            arrowRoot.transform.SetParent(transform, false);
            shaft = VizUtil.Primitive(PrimitiveType.Cylinder, "shaft", arrowRoot.transform, pal.Arrow);
            tip = new GameObject("tip");
            tip.transform.SetParent(arrowRoot.transform, false);
            tip.AddComponent<MeshFilter>().sharedMesh = VizUtil.Cone();
            tip.AddComponent<MeshRenderer>().sharedMaterial = pal.Arrow;

            // rotation gimbal: two rings through the arrow tip. Each ring is the exact track the tip follows when you
            // drag it, so a drag can only turn the sheet one way (no free spherical spin).
            //   TURN ring (teal): spins the sheet about the width axis; the only ring in the flat 2-feature view.
            //   TILT ring (gold): leans the sheet into the width direction (3D only).
            ringTurn = BuildRing("TURN ring (drag along it)", pal.Handle, Handle.Kind.TurnRing);
            var purple = new Material(pal.Handle);
            var pc = QuackPalette.FeatureColor[2]; if (purple.HasProperty("_BaseColor")) purple.SetColor("_BaseColor", pc); purple.color = pc;
            ringTilt = BuildRing("TILT ring (drag along it)", purple, Handle.Kind.TiltRing);
            rotateHandle = new GameObject("rotation gimbal");   // container toggled with the boundary
            rotateHandle.transform.SetParent(transform, false);
            ringTurn.transform.SetParent(rotateHandle.transform, false); ringTilt.transform.SetParent(rotateHandle.transform, false);
            for (int k = 0; k < 4; k++)
            {
                chevrons[k] = new GameObject("ring direction cue");
                chevrons[k].transform.SetParent(rotateHandle.transform, false);
                chevrons[k].AddComponent<MeshFilter>().sharedMesh = VizUtil.Cone();
                chevrons[k].AddComponent<MeshRenderer>().sharedMaterial = k < 2 ? pal.Handle : purple;
            }
            turnLabel = VizUtil.Label("TURN: drag the teal ring", rotateHandle.transform, Vector3.zero, 0.26f, new Color(0f, 0.42f, 0.4f));
            tiltLabel = VizUtil.Label("TILT into width: drag the purple ring", rotateHandle.transform, Vector3.zero, 0.26f, new Color(0.5f, 0.36f, 0f));

            translateHandle = VizUtil.Primitive(PrimitiveType.Cylinder, "translate handle (drag to slide)", transform, pal.Handle);
            translateHandle.transform.localScale = new Vector3(1.1f, 0.05f, 1.1f);
            var col = translateHandle.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.5f; col.height = 6f;   // easier to grab than a thin disc
            translateHandle.AddComponent<Handle>().Type = Handle.Kind.Translate;

            arrowLabel = VizUtil.Label("w  -> JUMBO side", transform, Vector3.zero, 0.34f, new Color(0.55f, 0.05f, 0.3f));
            slideLabel = VizUtil.Label("drag teal disc: slide", transform, Vector3.zero, 0.28f, new Color(0f, 0.45f, 0.42f));
            noBoundaryLabel = VizUtil.Label("No boundary: all weights are ~0, so the rule says 50% for every egg",
                transform, Vector3.zero, 0.25f, new Color(0.6f, 0.1f, 0.1f));

            s.ParamsChanged += _ => Refresh();
            Refresh();
        }

        /// <summary>score where p = 0.75: ln(0.75 / 0.25) = ln 3.</summary>
        public static readonly double BandScore = System.Math.Log(3.0);
        bool showBands;
        public bool ShowBands { get => showBands; set { showBands = value; Refresh(); } }
        readonly GameObject[] bandGo = new GameObject[2];
        readonly Mesh[] bandMesh = new Mesh[2];
        readonly TMPro.TextMeshPro[] bandLabel = new TMPro.TextMeshPro[2];

        static void FillPolygon(Mesh m, ModelParams p)
        {
            var poly = PlaneClip.ClipToCube(p, FeaturePlot.HalfExtent);
            m.Clear();
            if (poly.Count < 3) return;
            var verts = new Vector3[poly.Count];
            for (int i = 0; i < poly.Count; i++) verts[i] = VizUtil.ToV3(poly[i]);
            var tris = new int[(poly.Count - 2) * 3];
            for (int i = 0; i < poly.Count - 2; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
            m.vertices = verts; m.triangles = tris;
            m.RecalculateNormals(); m.RecalculateBounds();
        }

        void PlaceGimbal()
        {
            var n = NormalLocal;
            var u = Vector3.Cross(n, Vector3.forward);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.right;
            u.Normalize();
            TiltAxisLocal = u;                                   // turning about u moves the arrow toward width (z)
            TurnAxisLocal = Vector3.Cross(n, u).normalized;      // equals the width axis while the sheet is upright
            if (Mathf.Abs(ringLen - ArrowLength) > 0.01f)
            {
                ringLen = ArrowLength;
                var m = VizUtil.Torus(ArrowLength, 0.07f, 64, 8);
                foreach (var r in new[] { ringTurn, ringTilt })
                {
                    r.GetComponent<MeshFilter>().sharedMesh = m;
                    var mc = r.GetComponent<MeshCollider>(); mc.sharedMesh = null; mc.sharedMesh = m;
                }
            }
            rotateHandle.transform.localPosition = Vector3.zero;
            Place(ringTurn, TurnAxisLocal); Place(ringTilt, TiltAxisLocal);
            ringTilt.SetActive(!Flat);

            // direction cues: little cones either side of the tip, pointing along each ring
            var tip = PivotLocal + n * ArrowLength;
            for (int k = 0; k < 4; k++)
            {
                var axis = k < 2 ? TurnAxisLocal : TiltAxisLocal;
                float ang = (k % 2 == 0 ? 1 : -1) * 14f;
                var pos = PivotLocal + Quaternion.AngleAxis(ang, axis) * (n * ArrowLength);
                var tangent = Vector3.Cross(axis, pos - PivotLocal).normalized * Mathf.Sign(ang);
                chevrons[k].transform.localPosition = pos;
                chevrons[k].transform.localRotation = Quaternion.FromToRotation(Vector3.up, tangent);
                chevrons[k].transform.localScale = new Vector3(0.16f, 0.3f, 0.16f);
                chevrons[k].SetActive(k < 2 || !Flat);
            }
            turnLabel.transform.localPosition = PivotLocal + Quaternion.AngleAxis(38f, TurnAxisLocal) * (n * (ArrowLength + 0.45f));
            tiltLabel.transform.localPosition = PivotLocal + Quaternion.AngleAxis(-38f, TiltAxisLocal) * (n * (ArrowLength + 0.45f));
            tiltLabel.gameObject.SetActive(false); turnLabel.gameObject.SetActive(false);   // clean scene: no floating captions
        }

        void Place(GameObject ring, Vector3 axis)
        {
            ring.transform.localPosition = PivotLocal;
            ring.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);
        }

        static Vector3 Farthest(System.Collections.Generic.List<Vector3> pts, Vector3 from)
        {
            Vector3 best = from; float d = -1;
            foreach (var v in pts) { float e = (v - from).sqrMagnitude; if (e > d) { d = e; best = v; } }
            return best;
        }

        void Refresh()
        {
            var p = state.Params;
            bool has = state.HasBoundary && Visible;
            planeGo.SetActive(has); arrowRoot.SetActive(has); rotateHandle.SetActive(has);
            if (!has) { bandGo[0].SetActive(false); bandGo[1].SetActive(false); bandLabel[0].gameObject.SetActive(false); bandLabel[1].gameObject.SetActive(false); }
            if (edgeLine) edgeLine.enabled = has;
            translateHandle.SetActive(has); arrowLabel.gameObject.SetActive(false); slideLabel.gameObject.SetActive(false);
            noBoundaryLabel.gameObject.SetActive(Visible && !state.HasBoundary);
            if (!has) return;

            // plane polygon (local = standardised units)
            FillPolygon(mesh, p);
            HasSegment = false;
            if (edgeLine && edgeLine.gameObject.activeSelf)
            {
                // where the sheet crosses the flat z = 0 egg layer (w3 = 0 while width is unmeasured)
                var seg = PlaneClip.ClipToCube(p, FeaturePlot.HalfExtent);
                var pts = new System.Collections.Generic.List<Vector3>();
                foreach (var q in seg) { var v = VizUtil.ToV3(q); v.z = 0; pts.Add(v); }
                Vector3 lo = Vector3.zero, hi = Vector3.zero;
                if (pts.Count > 0)
                {
                    lo = Farthest(pts, pts[0]); hi = Farthest(pts, lo);   // the segment's two ends
                }
                edgeLine.SetPosition(0, lo); edgeLine.SetPosition(1, hi);
                SegA = lo; SegB = hi; HasSegment = pts.Count > 0 && (hi - lo).sqrMagnitude > 1e-4f;
            }
            // confidence bands: the parallel sheets where p = 0.75 (score = +ln 3) and p = 0.25 (score = -ln 3)
            for (int k = 0; k < 2; k++)
            {
                bandGo[k].SetActive(ShowBands);
                bandLabel[k].gameObject.SetActive(false);
                if (!ShowBands) continue;
                var q = p.Clone(); q.B -= (k == 0 ? 1 : -1) * BandScore;
                FillPolygon(bandMesh[k], q);
                // label each band where it sits: offset from the main sheet along the normal by ln3 / |w|
                var n = VizUtil.ToV3(PlaneGeometry.UnitNormal(p));
                float off = (float)(BandScore / System.Math.Max(1e-6, p.WeightNorm()));
                var pivot = VizUtil.ToV3(PlaneGeometry.Project(p, new double[3]));
                var lp = pivot + n * off * (k == 0 ? 1 : -1) + Vector3.up * (FeaturePlot.HalfExtent * 0.8f);
                float hx = FeaturePlot.HalfExtent;
                bandLabel[k].transform.localPosition = new Vector3(Mathf.Clamp(lp.x, -hx, hx), Mathf.Clamp(lp.y, -hx, hx * 1.1f), Mathf.Clamp(lp.z, -hx, hx));
            }

            // pivot: point on the plane closest to the data centre (origin in standardised space)
            PivotLocal = VizUtil.ToV3(PlaneGeometry.Project(p, new double[3]));
            NormalLocal = VizUtil.ToV3(PlaneGeometry.UnitNormal(p));
            ArrowLength = Mathf.Clamp((float)p.WeightNorm() * ArrowPerWeight, MinArrow, MaxArrow);

            var rot = Quaternion.FromToRotation(Vector3.up, NormalLocal);
            arrowRoot.transform.localPosition = PivotLocal;
            arrowRoot.transform.localRotation = rot;
            float tipLen = Mathf.Min(0.35f, ArrowLength * 0.4f);
            float shaftLen = ArrowLength - tipLen;
            shaft.transform.localScale = new Vector3(0.13f, shaftLen / 2f, 0.13f);   // unit cylinder is 2 tall
            shaft.transform.localPosition = new Vector3(0, shaftLen / 2f, 0);
            tip.transform.localPosition = new Vector3(0, shaftLen, 0);
            tip.transform.localScale = new Vector3(0.3f, tipLen, 0.3f);

            PlaceGimbal();
            translateHandle.transform.localPosition = PivotLocal;
            translateHandle.transform.localRotation = rot;
            arrowLabel.transform.localPosition = PivotLocal + NormalLocal * (ArrowLength + 0.7f);
            var side = Vector3.Cross(NormalLocal, Mathf.Abs(NormalLocal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            slideLabel.transform.localPosition = PivotLocal + side * 0.9f - NormalLocal * 0.3f;
        }
    }
}
