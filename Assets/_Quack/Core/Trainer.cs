using System;
using System.Collections.Generic;

namespace Quack.Core
{
    public struct TrainStep
    {
        public int Index;
        public double LearningRate;
        public double ObjectiveBefore, ObjectiveAfter;
        public ModelParams Before, After, Gradient;
        public bool Diverged;   // non-finite or exploding objective
    }

    /// <summary>
    /// Full-batch gradient descent on the regularised objective. Behaviour (slow / converge /
    /// oscillate / diverge) comes only from the real objective and learning rate — nothing is scripted.
    /// The data never changes; only parameters do.
    /// </summary>
    public sealed class Trainer
    {
        public const double DivergenceObjective = 1e6;

        public readonly DesignMatrix Data;
        public readonly double Lambda;
        public ModelParams Params { get; private set; }
        public readonly List<TrainStep> History = new List<TrainStep>();

        public Trainer(DesignMatrix data, ModelParams start, double lambda = LogisticModel.DefaultLambda)
        {
            Data = data; Lambda = lambda; Params = start.Clone();
        }

        public void Reset(ModelParams start) { Params = start.Clone(); History.Clear(); }

        public TrainStep Step(double learningRate)
        {
            if (!(learningRate > 0)) throw new ArgumentException("learning rate must be positive");
            var before = Params;
            double jBefore = LogisticModel.Objective(before, Data, Lambda);
            var g = LogisticModel.Gradient(before, Data, Lambda);
            var after = before.Clone();
            for (int j = 0; j < after.Dim; j++) after.W[j] -= learningRate * g.W[j];
            after.B -= learningRate * g.B;
            double jAfter = LogisticModel.Objective(after, Data, Lambda);
            var step = new TrainStep
            {
                Index = History.Count, LearningRate = learningRate,
                ObjectiveBefore = jBefore, ObjectiveAfter = jAfter,
                Before = before, After = after, Gradient = g,
                Diverged = double.IsNaN(jAfter) || double.IsInfinity(jAfter) || jAfter > DivergenceObjective,
            };
            History.Add(step);
            Params = after;
            return step;
        }
    }
}
