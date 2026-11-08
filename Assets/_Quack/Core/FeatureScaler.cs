using System;
using System.Collections.Generic;

namespace Quack.Core
{
    /// <summary>
    /// Z-score standardisation fitted on the TRAINING set only: x̃ = (x − μ) / σ.
    /// The model and the 3D plot both live in standardised units; tick labels show raw units.
    /// </summary>
    public sealed class FeatureScaler
    {
        public readonly double[] Mean;
        public readonly double[] Std;

        public FeatureScaler(double[] mean, double[] std)
        {
            Mean = mean; Std = std;
            for (int j = 0; j < std.Length; j++)
                if (!(std[j] > 1e-12)) throw new ArgumentException($"feature {j} has zero variance");
        }

        public static FeatureScaler Fit(IReadOnlyList<Egg> eggs)
        {
            int d = eggs[0].Raw.Length, n = eggs.Count;
            var mu = new double[d];
            var sd = new double[d];
            foreach (var e in eggs) for (int j = 0; j < d; j++) mu[j] += e.Raw[j];
            for (int j = 0; j < d; j++) mu[j] /= n;
            foreach (var e in eggs) for (int j = 0; j < d; j++) sd[j] += (e.Raw[j] - mu[j]) * (e.Raw[j] - mu[j]);
            for (int j = 0; j < d; j++) sd[j] = Math.Sqrt(sd[j] / n);   // population σ (matches tools/ref_model.py)
            return new FeatureScaler(mu, sd);
        }

        public double ToStd(int j, double raw) => (raw - Mean[j]) / Std[j];
        public double ToRaw(int j, double z) => z * Std[j] + Mean[j];

        public double[] Transform(double[] raw)
        {
            var z = new double[raw.Length];
            for (int j = 0; j < raw.Length; j++) z[j] = ToStd(j, raw[j]);
            return z;
        }
    }
}
