using System;

namespace Quack.Core
{
    /// <summary>
    /// New, unlabelled eggs for the "next shipment" (inference). Same generative recipe as tools/gen_eggs.py
    /// (length, correlated width, density -> mass), different seed. Fictional and synthetic like the dataset.
    /// </summary>
    public static class ShipmentGenerator
    {
        public static double[][] Generate(int count, int seed = 777)
        {
            var rng = new Random(seed);
            double Gauss(double mu, double sd)
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                return mu + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }
            var eggs = new double[count][];
            for (int i = 0; i < count; i++)
            {
                double length = Gauss(58.0, 3.2);
                double width = Gauss(43.5, 1.9) + 0.22 * (length - 58.0);
                double density = Gauss(1.0, 0.035);
                double mass = 0.000525 * length * width * width * density;
                eggs[i] = new[] { mass, length, width };
            }
            return eggs;
        }

        public struct BatchResult { public int Jumbo, Standard, FlaggedForReview; }

        /// <summary>
        /// Batch inference: standardise each row with the TRAINING scaler, score = row . w + b, predict.
        /// Eggs with |p - 0.5| below reviewBand are flagged for a human inspector (a separate threshold
        /// from the 0.5 decision threshold).
        /// </summary>
        public static BatchResult Classify(double[][] raw, FeatureScaler scaler, int[] active, ModelParams p, double reviewBand)
        {
            var r = new BatchResult();
            var x = new double[active.Length];
            foreach (var row in raw)
            {
                for (int k = 0; k < active.Length; k++) x[k] = scaler.ToStd(active[k], row[active[k]]);
                double prob = LogisticModel.Probability(p, x);
                if (prob >= LogisticModel.DecisionThreshold) r.Jumbo++; else r.Standard++;
                if (Math.Abs(prob - 0.5) < reviewBand) r.FlaggedForReview++;
            }
            return r;
        }
    }
}
