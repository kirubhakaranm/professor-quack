using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Device-independent pointer logic: given a world ray and press/drag/release events, select eggs,
    /// tilt the plane (rotate handle) and slide it (translate handle). Desktop feeds mouse rays,
    /// VR feeds controller rays — both drive identical GameState commands.
    /// </summary>
    public sealed class PointerInteractor
    {
        public enum Result { None, Handle, Egg, WorldUI, Empty }

        readonly FeaturePlot plot;
        readonly DecisionPlaneView planeView;
        readonly GameState state;

        enum DragMode { None, Rotate, Translate, Landscape, Ring, Desk }
        IDraggable deskControl;
        Vector3 ringAxis, ringStartVec, ringStartNormal;
        public LandscapeView Landscape;   // set when the parameter-space hologram exists
        DragMode drag;
        ModelParams dragStart;
        double[] rotatePivot;
        float translateStartT;
        float sphereRadiusLocal;

        public bool Dragging => drag != DragMode.None;

        public PointerInteractor(FeaturePlot plot, DecisionPlaneView planeView, GameState state)
        {
            this.plot = plot; this.planeView = planeView; this.state = state;
        }

        /// <summary>Handles win over eggs (they sit in the dense centre); world UI wins over both.</summary>
        public Result Press(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 100f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Handle handle = null; EggView egg = null; WorldUIHit ui = null; RaycastHit uiHit = default;
            // a station floor pad (nearest hit only, so pads never steal clicks from things in front of them)
            if (hits.Length > 0 && hits[0].collider.GetComponent<TeleportPad>() is TeleportPad pad) { pad.Activate(); return Result.WorldUI; }
            foreach (var h in hits)
            {
                if (ui == null && (ui = h.collider.GetComponent<WorldUIHit>()) != null) uiHit = h;
                if (handle == null) handle = h.collider.GetComponent<Handle>();
                if (egg == null) egg = h.collider.GetComponent<EggView>();
            }
            if (ui != null) { ui.Press(uiHit.point); return Result.WorldUI; }
            // control desk dials / lever (nearest hit wins)
            foreach (var h in hits)
            {
                var dc = h.collider.GetComponent<IDraggable>();
                if (dc == null) continue;
                deskControl = dc; dc.Begin(ray); drag = DragMode.Desk;
                return Result.Handle;
            }
            if (handle != null && handle.Type == Handle.Kind.Landscape && Landscape != null)
            {
                state.BeginEdit();
                drag = DragMode.Landscape;
                return Result.Handle;
            }
            if (handle != null && state.HasBoundary)
            {
                dragStart = state.Params.Clone();
                state.BeginEdit();
                if (handle.Type == Handle.Kind.TurnRing || handle.Type == Handle.Kind.TiltRing)
                {
                    // constrained rotation: the arrow tip slides along the grabbed ring only
                    drag = DragMode.Ring;
                    rotatePivot = PlaneGeometry.Project(dragStart, new double[3]);
                    ringAxis = handle.Type == Handle.Kind.TurnRing ? planeView.TurnAxisLocal : planeView.TiltAxisLocal;
                    ringStartNormal = planeView.NormalLocal;
                    ringStartVec = RingVector(ray);
                }
                else if (handle.Type == Handle.Kind.Rotate)
                {
                    drag = DragMode.Rotate;
                    rotatePivot = PlaneGeometry.Project(dragStart, new double[3]);
                    sphereRadiusLocal = planeView.ArrowLength;
                }
                else
                {
                    drag = DragMode.Translate;
                    translateStartT = ParamAlongNormal(ray);
                }
                return Result.Handle;
            }
            if (egg != null) { state.Select(egg.Egg); return Result.Egg; }
            return Result.Empty;
        }

        public void Drag(Ray ray)
        {
            if (WorldUIHit.Active != null) { WorldUIHit.Active.Drag(ray); return; }
            switch (drag)
            {
                case DragMode.Landscape:
                    if (Landscape.ParamsFromRay(ray, out var lp)) state.SetParams(lp, ParamSource.Landscape);
                    break;
                case DragMode.Translate:
                {
                    float t = ParamAlongNormal(ray);
                    state.SetParams(PlaneGeometry.Translate(dragStart, t - translateStartT), ParamSource.PlaneTranslate);
                    break;
                }
                case DragMode.Desk:
                    deskControl?.Drag(ray);
                    break;
                case DragMode.Ring:
                {
                    var cur = RingVector(ray);
                    if (cur.sqrMagnitude < 0.5f || ringStartVec.sqrMagnitude < 0.5f) break;
                    float angle = Vector3.SignedAngle(ringStartVec, cur, ringAxis);
                    var n = Quaternion.AngleAxis(angle, ringAxis) * ringStartNormal;
                    state.SetParams(PlaneGeometry.Rotate(dragStart, VizUtil.ToD(n.normalized), rotatePivot), ParamSource.PlaneRotate);
                    break;
                }
                case DragMode.Rotate:
                {
                    // point the arrow tip along the ray: intersect it with a sphere around the pivot
                    var t = plot.transform;
                    var o = t.InverseTransformPoint(ray.origin);
                    var dir = t.InverseTransformDirection(ray.direction).normalized;
                    var c = VizUtil.ToV3(rotatePivot);
                    float tc = Vector3.Dot(c - o, dir);
                    var closest = o + dir * tc;
                    float dist2 = (closest - c).sqrMagnitude, r2 = sphereRadiusLocal * sphereRadiusLocal;
                    Vector3 onSphere = dist2 <= r2 ? o + dir * (tc - Mathf.Sqrt(r2 - dist2))
                                                   : c + (closest - c).normalized * sphereRadiusLocal;
                    var n = (onSphere - c).normalized;
                    if (n.sqrMagnitude > 0.5f)
                        state.SetParams(PlaneGeometry.Rotate(dragStart, VizUtil.ToD(n), rotatePivot), ParamSource.PlaneRotate);
                    break;
                }
            }
        }

        public void Release()
        {
            if (WorldUIHit.Active != null) WorldUIHit.Active.Release();
            if (drag == DragMode.Desk) { deskControl?.End(); deskControl = null; }
            if (drag == DragMode.Rotate || drag == DragMode.Ring) state.EndEdit(ParamSource.PlaneRotate);
            else if (drag == DragMode.Translate) state.EndEdit(ParamSource.PlaneTranslate);
            else if (drag == DragMode.Landscape) state.EndEdit(ParamSource.Landscape);
            drag = DragMode.None;
        }

        /// <summary>Unit direction (in the ring's plane, from the pivot) of where the ray meets the grabbed ring's plane.</summary>
        Vector3 RingVector(Ray ray)
        {
            var t = plot.transform;
            var o = t.InverseTransformPoint(ray.origin);
            var d = t.InverseTransformDirection(ray.direction).normalized;
            var c = VizUtil.ToV3(rotatePivot);
            float dn = Vector3.Dot(d, ringAxis);
            Vector3 p;
            if (Mathf.Abs(dn) > 0.08f) p = o + d * (Vector3.Dot(c - o, ringAxis) / dn);
            else p = o + d * Vector3.Dot(c - o, d);              // ring seen edge-on: use the closest point on the ray
            var v = p - c; v -= ringAxis * Vector3.Dot(v, ringAxis);
            return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
        }

        /// <summary>Signed position along the plane normal (plot-local units) closest to the ray.</summary>
        float ParamAlongNormal(Ray ray)
        {
            var t = plot.transform;
            var o = t.InverseTransformPoint(ray.origin);
            var d = t.InverseTransformDirection(ray.direction).normalized;
            var p0 = planeView.PivotLocal;
            var n = planeView.NormalLocal;
            var w0 = p0 - o;
            float b = Vector3.Dot(n, d), dd = Vector3.Dot(n, w0), e = Vector3.Dot(d, w0);
            float denom = 1 - b * b;
            if (Mathf.Abs(denom) < 1e-4f) return Vector3.Dot(p0, n);   // ray parallel to normal
            float s = (b * e - dd) / denom;
            return Vector3.Dot(p0, n) + s;
        }
    }
}
