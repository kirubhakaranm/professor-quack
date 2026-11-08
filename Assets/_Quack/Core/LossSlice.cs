namespace Quack.Core
{
    /// <summary>
    /// A 2D slice of the objective: vary weights <see cref="AxisA"/> and <see cref="AxisB"/> over a grid,
    /// hold every other parameter (other weights and the bias) at the values in <see cref="Held"/>.
    /// It is NOT the full parameter space; recompute whenever a held parameter changes.
    /// </summary>
    public sealed class LossSlice
    {
        public readonly int AxisA, AxisB, Resolution;
        public readonly double MinA, MaxA, MinB, MaxB;
        public readonly ModelParams Held;
        public readonly double[,] Values;   // [ia, ib]
        public double MinValue { get; private set; }
        public double MaxValue { get; private set; }

        public LossSlice(int axisA, int axisB, double minA, double maxA, double minB, double maxB, int resolution, ModelParams held)
        {
            AxisA = axisA; AxisB = axisB; MinA = minA; MaxA = maxA; MinB = minB; MaxB = maxB;
            Resolution = resolution; Held = held.Clone();
            Values = new double[resolution, resolution];
        }

        public double ParamAt(int axis, int index) =>
            axis == 0 ? MinA + (MaxA - MinA) * index / (Resolution - 1)
                      : MinB + (MaxB - MinB) * index / (Resolution - 1);

        public void Compute(DesignMatrix data, double lambda = LogisticModel.DefaultLambda)
        {
            var p = Held.Clone();
            MinValue = double.MaxValue; MaxValue = double.MinValue;
            for (int ia = 0; ia < Resolution; ia++)
            {
                p.W[AxisA] = ParamAt(0, ia);
                for (int ib = 0; ib < Resolution; ib++)
                {
                    p.W[AxisB] = ParamAt(1, ib);
                    double v = LogisticModel.Objective(p, data, lambda);
                    Values[ia, ib] = v;
                    if (v < MinValue) MinValue = v;
                    if (v > MaxValue) MaxValue = v;
                }
            }
        }

        /// <summary>True if the given parameters lie in this slice (held values equal).</summary>
        public bool Contains(ModelParams p, double tol = 1e-9)
        {
            if (System.Math.Abs(p.B - Held.B) > tol) return false;
            for (int j = 0; j < p.Dim; j++)
                if (j != AxisA && j != AxisB && System.Math.Abs(p.W[j] - Held.W[j]) > tol) return false;
            return true;
        }
    }
}
