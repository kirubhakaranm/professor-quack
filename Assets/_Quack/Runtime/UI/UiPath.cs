using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>A thick, smooth UI line through control points (Catmull-Rom), e.g. the winding lesson roadmap path.</summary>
    public sealed class UiPath : MaskableGraphic
    {
        public List<Vector2> Points = new List<Vector2>();
        public float Thickness = 14f;
        public int Samples = 24;
        public float Dash = 0f, Gap = 0f;   // > 0: dotted/dashed trail (map style)

        public void SetPoints(List<Vector2> pts) { Points = pts; SetVerticesDirty(); }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Points == null || Points.Count < 2) return;
            var curve = new List<Vector2>();
            for (int i = 0; i < Points.Count - 1; i++)
            {
                var p0 = Points[Mathf.Max(0, i - 1)]; var p1 = Points[i]; var p2 = Points[i + 1]; var p3 = Points[Mathf.Min(Points.Count - 1, i + 2)];
                for (int s = 0; s < Samples; s++) curve.Add(CatmullRom(p0, p1, p2, p3, s / (float)Samples));
            }
            curve.Add(Points[Points.Count - 1]);
            float h = Thickness / 2;
            if (Dash > 0)
            {
                // walk the curve by arc length, emitting a short quad for each dash
                float period = Dash + Gap, s0 = 0; int quads = 0;
                for (int i = 1; i < curve.Count; i++)
                {
                    var a = curve[i - 1]; var b = curve[i]; float len = (b - a).magnitude; if (len < 1e-4f) continue;
                    var dir = (b - a) / len; var n = new Vector2(-dir.y, dir.x) * h;
                    for (float d = 0; d < len; d += 2f)
                    {
                        if ((s0 + d) % period >= Dash) continue;   // in a gap
                        var p0 = a + dir * d; var p1 = a + dir * Mathf.Min(len, d + 2f);
                        var v = UIVertex.simpleVert; v.color = color;
                        v.position = p0 + n; vh.AddVert(v); v.position = p0 - n; vh.AddVert(v);
                        v.position = p1 + n; vh.AddVert(v); v.position = p1 - n; vh.AddVert(v);
                        int k = quads * 4; vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k + 1, k + 3, k + 2); quads++;
                    }
                    s0 += len;
                }
                return;
            }
            for (int i = 0; i < curve.Count; i++)
            {
                var dir = (i < curve.Count - 1 ? curve[i + 1] - curve[i] : curve[i] - curve[i - 1]).normalized;
                var n = new Vector2(-dir.y, dir.x) * h;
                var v = UIVertex.simpleVert; v.color = color;
                v.position = curve[i] + n; vh.AddVert(v);
                v.position = curve[i] - n; vh.AddVert(v);
                if (i > 0) { int k = i * 2; vh.AddTriangle(k - 2, k - 1, k); vh.AddTriangle(k - 1, k + 1, k); }
            }
        }
    }
}
