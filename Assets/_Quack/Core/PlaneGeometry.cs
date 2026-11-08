using System;

namespace Quack.Core
{
    /// <summary>
    /// Geometry of the decision boundary w·x̃ + b = 0 in standardised feature space.
    /// The weight vector is the plane's normal. The plane fixes only w/‖w‖ and b/‖w‖;
    /// ‖w‖ ("steepness") changes probabilities without moving the plane.
    /// All operations return new parameters; inputs are never mutated.
    /// </summary>
    public static class PlaneGeometry
    {
        /// <summary>Below this ‖w‖ there is no meaningful boundary (p ≈ σ(b) everywhere).</summary>
        public const double MinWeightNorm = 1e-6;

        public static bool HasBoundary(ModelParams p) => p.WeightNorm() > MinWeightNorm;

        public static double[] UnitNormal(ModelParams p)
        {
            double n = RequireNorm(p);
            var u = new double[p.Dim];
            for (int j = 0; j < p.Dim; j++) u[j] = p.W[j] / n;
            return u;
        }

        /// <summary>Signed Euclidean distance from x to the plane (positive on the JUMBO side).</summary>
        public static double SignedDistance(ModelParams p, double[] x) => LogisticModel.Score(p, x) / RequireNorm(p);

        /// <summary>Orthogonal projection of x onto the plane.</summary>
        public static double[] Project(ModelParams p, double[] x)
        {
            double dist = SignedDistance(p, x);
            var u = UnitNormal(p);
            var q = new double[p.Dim];
            for (int j = 0; j < p.Dim; j++) q[j] = x[j] - dist * u[j];
            return q;
        }

        /// <summary>Move the plane a distance d along its unit normal: b_new = b − ‖w‖·d.</summary>
        public static ModelParams Translate(ModelParams p, double d)
        {
            var r = p.Clone();
            r.B = p.B - RequireNorm(p) * d;
            return r;
        }

        /// <summary>
        /// Point the normal along <paramref name="newDirection"/> keeping ‖w‖ fixed and keeping
        /// <paramref name="pivot"/> on the plane (b = −w·pivot). The pivot should already be on the plane
        /// (use <see cref="Project"/>), otherwise the plane jumps to pass through it.
        /// </summary>
        public static ModelParams Rotate(ModelParams p, double[] newDirection, double[] pivot)
        {
            double norm = RequireNorm(p);
            double dn = 0; foreach (var v in newDirection) dn += v * v;
            dn = Math.Sqrt(dn);
            if (dn < 1e-12) throw new ArgumentException("rotation direction must be nonzero");
            var r = p.Clone();
            double b = 0;
            for (int j = 0; j < p.Dim; j++) { r.W[j] = norm * newDirection[j] / dn; b -= r.W[j] * pivot[j]; }
            r.B = b;
            return r;
        }

        /// <summary>Set ‖w‖ to <paramref name="steepness"/> without moving the plane (scale w and b together).</summary>
        public static ModelParams SetSteepness(ModelParams p, double steepness)
        {
            if (!(steepness > MinWeightNorm)) throw new ArgumentException("steepness must be positive");
            double k = steepness / RequireNorm(p);
            var r = p.Clone();
            for (int j = 0; j < p.Dim; j++) r.W[j] *= k;
            r.B *= k;
            return r;
        }

        static double RequireNorm(ModelParams p)
        {
            double n = p.WeightNorm();
            if (n <= MinWeightNorm) throw new InvalidOperationException("no boundary: weights are ~0, the rule says 50% everywhere");
            return n;
        }
    }
}
