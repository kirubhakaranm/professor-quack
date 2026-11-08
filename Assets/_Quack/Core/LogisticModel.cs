using System;

namespace Quack.Core
{
    /// <summary>Model parameters: score z = w·x̃ + b on standardised features.</summary>
    public sealed class ModelParams
    {
        public double[] W;
        public double B;

        public ModelParams(double[] w, double b) { W = (double[])w.Clone(); B = b; }
        public static ModelParams Zero(int d) => new ModelParams(new double[d], 0.0);
        public ModelParams Clone() => new ModelParams(W, B);
        public int Dim => W.Length;

        public double WeightNorm()
        {
            double s = 0; foreach (var v in W) s += v * v; return Math.Sqrt(s);
        }

        public override string ToString() => $"w=[{string.Join(", ", Array.ConvertAll(W, v => v.ToString("0.###")))}] b={B:0.###}";
    }

    /// <summary>Training data in standardised units, ready for the model.</summary>
    public sealed class DesignMatrix
    {
        public readonly double[][] X;   // rows = eggs, columns = active features
        public readonly int[] Y;
        public DesignMatrix(double[][] x, int[] y) { X = x; Y = y; }
        public int Count => Y.Length;
        public int Dim => X[0].Length;
    }

    /// <summary>
    /// Logistic regression with binary cross-entropy and an L2 penalty on the weights (not the bias).
    /// Objective J(w,b) = mean BCE + (λ/2)‖w‖². Convex; finite optimum exists for λ &gt; 0.
    /// </summary>
    public static class LogisticModel
    {
        public const double DefaultLambda = 0.01;
        public const double DecisionThreshold = 0.5;   // p ≥ 0.5  ⇔  z ≥ 0

        public static double Score(ModelParams p, double[] x)
        {
            double z = p.B;
            for (int j = 0; j < p.W.Length; j++) z += p.W[j] * x[j];
            return z;
        }

        /// <summary>Numerically stable logistic function.</summary>
        public static double Sigmoid(double z)
        {
            if (z >= 0) return 1.0 / (1.0 + Math.Exp(-z));
            double e = Math.Exp(z);
            return e / (1.0 + e);
        }

        public static double Probability(ModelParams p, double[] x) => Sigmoid(Score(p, x));

        /// <summary>Predicted class uses the score sign so it never disagrees with the plane.</summary>
        public static int Predict(ModelParams p, double[] x) => Score(p, x) >= 0 ? 1 : 0;

        /// <summary>Stable BCE from the score: max(z,0) − z·y + log(1 + e^−|z|).</summary>
        public static double Bce(double z, int y) => Math.Max(z, 0) - z * y + Log1p(Math.Exp(-Math.Abs(z)));

        public static double Objective(ModelParams p, DesignMatrix d, double lambda = DefaultLambda)
        {
            double sum = 0;
            for (int i = 0; i < d.Count; i++) sum += Bce(Score(p, d.X[i]), d.Y[i]);
            double reg = 0; foreach (var w in p.W) reg += w * w;
            return sum / d.Count + 0.5 * lambda * reg;
        }

        /// <summary>Mean BCE only (no penalty) — what "data loss" panels show.</summary>
        public static double DataLoss(ModelParams p, DesignMatrix d)
        {
            double sum = 0;
            for (int i = 0; i < d.Count; i++) sum += Bce(Score(p, d.X[i]), d.Y[i]);
            return sum / d.Count;
        }

        /// <summary>Analytic gradient of <see cref="Objective"/>.</summary>
        public static ModelParams Gradient(ModelParams p, DesignMatrix d, double lambda = DefaultLambda)
        {
            var g = ModelParams.Zero(p.Dim);
            for (int i = 0; i < d.Count; i++)
            {
                double e = Sigmoid(Score(p, d.X[i])) - d.Y[i];
                for (int j = 0; j < p.Dim; j++) g.W[j] += e * d.X[i][j];
                g.B += e;
            }
            for (int j = 0; j < p.Dim; j++) g.W[j] = g.W[j] / d.Count + lambda * p.W[j];
            g.B /= d.Count;
            return g;
        }

        public static Confusion Evaluate(ModelParams p, DesignMatrix d)
        {
            var c = new Confusion();
            for (int i = 0; i < d.Count; i++)
            {
                int pred = Predict(p, d.X[i]);
                if (d.Y[i] == 1) { if (pred == 1) c.TruePos++; else c.FalseNeg++; }
                else { if (pred == 0) c.TrueNeg++; else c.FalsePos++; }
            }
            return c;
        }

        // Math.Log1p is not available on every .NET profile Unity targets.
        static double Log1p(double x) => Math.Abs(x) < 1e-5 ? x - 0.5 * x * x + x * x * x / 3.0 : Math.Log(1.0 + x);
    }

    /// <summary>Per-class results, so evaluation never hides one class behind an aggregate.</summary>
    public struct Confusion
    {
        public int TruePos, FalseNeg, TrueNeg, FalsePos;
        public int Total => TruePos + FalseNeg + TrueNeg + FalsePos;
        public int Correct => TruePos + TrueNeg;
        public int Misrouted => FalseNeg + FalsePos;
        public double Accuracy => Total == 0 ? 0 : (double)Correct / Total;
        public double JumboRecall => TruePos + FalseNeg == 0 ? 0 : (double)TruePos / (TruePos + FalseNeg);
        public double StandardRecall => TrueNeg + FalsePos == 0 ? 0 : (double)TrueNeg / (TrueNeg + FalsePos);
    }
}
