using System;
using System.Collections.Generic;

namespace Quack.Core
{
    /// <summary>
    /// Intersection polygon of the 3D decision plane w·x + b = 0 with the plot cube [−h, h]³,
    /// ordered around its centroid (ready for a triangle fan). Empty if the plane misses the cube.
    /// </summary>
    public static class PlaneClip
    {
        public static List<double[]> ClipToCube(ModelParams p, double h)
        {
            if (p.Dim != 3) throw new ArgumentException("plane clipping needs 3 features");
            var pts = new List<double[]>();
            if (!PlaneGeometry.HasBoundary(p)) return pts;

            // 12 cube edges: vary one axis, fix the other two at ±h
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                foreach (double s1 in new[] { -h, h })
                foreach (double s2 in new[] { -h, h })
                {
                    double wa = p.W[axis];
                    double rest = p.B + p.W[a1] * s1 + p.W[a2] * s2;
                    if (Math.Abs(wa) < 1e-12) continue;      // edge parallel to plane
                    double t = -rest / wa;
                    if (t < -h - 1e-9 || t > h + 1e-9) continue;
                    var v = new double[3];
                    v[axis] = Math.Max(-h, Math.Min(h, t)); v[a1] = s1; v[a2] = s2;
                    AddUnique(pts, v);
                }
            }
            if (pts.Count < 3) { pts.Clear(); return pts; }

            // order around centroid using a basis in the plane
            var c = new double[3];
            foreach (var v in pts) for (int j = 0; j < 3; j++) c[j] += v[j] / pts.Count;
            var n = PlaneGeometry.UnitNormal(p);
            var u = Normalize(Cross(n, Math.Abs(n[0]) < 0.9 ? new[] { 1.0, 0, 0 } : new[] { 0, 1.0, 0 }));
            var w = Cross(n, u);
            pts.Sort((A, B) => Angle(A, c, u, w).CompareTo(Angle(B, c, u, w)));
            return pts;
        }

        static double Angle(double[] v, double[] c, double[] u, double[] w)
        {
            double du = 0, dw = 0;
            for (int j = 0; j < 3; j++) { du += (v[j] - c[j]) * u[j]; dw += (v[j] - c[j]) * w[j]; }
            return Math.Atan2(dw, du);
        }

        static void AddUnique(List<double[]> pts, double[] v)
        {
            foreach (var q in pts)
                if (Math.Abs(q[0] - v[0]) < 1e-9 && Math.Abs(q[1] - v[1]) < 1e-9 && Math.Abs(q[2] - v[2]) < 1e-9) return;
            pts.Add(v);
        }

        static double[] Cross(double[] a, double[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        static double[] Normalize(double[] a)
        {
            double n = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
            return new[] { a[0] / n, a[1] / n, a[2] / n };
        }
    }
}
