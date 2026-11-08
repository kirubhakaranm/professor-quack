using System;
using System.IO;
using NUnit.Framework;
using Quack.Core;
using UnityEngine;

namespace Quack.Core.Tests
{
    /// <summary>
    /// Checks the claims the lessons make to players. Reference numbers come from the independent
    /// Python implementation in tools/ref_model.py run on the same eggs.csv.
    /// </summary>
    public class CoreMathTests
    {
        static EggDataset ds;
        static FeatureSpace fs3;

        [OneTimeSetUp]
        public void Load()
        {
            ds = EggDataset.ParseCsv(File.ReadAllText(Path.Combine(Application.dataPath, "_Quack/Resources/eggs.csv")));
            fs3 = new FeatureSpace(ds, new[] { 0, 1, 2 });
        }

        // ---------- dataset & scaling ----------

        [Test]
        public void Dataset_HasTrainTestSplitAndBothClasses()
        {
            Assert.AreEqual(200, ds.All.Count);
            Assert.AreEqual(150, ds.Train.Count);
            Assert.AreEqual(50, ds.Test.Count);
            Assert.That(ds.Train.FindAll(e => e.Label == 1).Count, Is.InRange(40, 110));
            Assert.That(ds.Test.FindAll(e => e.Label == 1).Count, Is.InRange(10, 40));
        }

        [Test]
        public void Scaler_MatchesReferenceAndStandardisesTrainingSet()
        {
            double[] mu = { 58.048, 57.887333, 43.614 }, sd = { 6.680167, 2.850083, 1.937388 };
            for (int j = 0; j < 3; j++)
            {
                Assert.AreEqual(mu[j], fs3.Scaler.Mean[j], 1e-5);
                Assert.AreEqual(sd[j], fs3.Scaler.Std[j], 1e-5);
                double m = 0, v = 0;
                foreach (var x in fs3.Train.X) m += x[j];
                m /= fs3.Train.Count;
                foreach (var x in fs3.Train.X) v += (x[j] - m) * (x[j] - m);
                Assert.AreEqual(0, m, 1e-9);
                Assert.AreEqual(1, v / fs3.Train.Count, 1e-9);
                Assert.AreEqual(57.3, fs3.Scaler.ToRaw(j, fs3.Scaler.ToStd(j, 57.3)), 1e-9);
            }
        }

        // ---------- probability & loss ----------

        [Test]
        public void Sigmoid_IsHalfAtZero_Symmetric_AndStableAtExtremes()
        {
            Assert.AreEqual(0.5, LogisticModel.Sigmoid(0), 1e-15);
            foreach (var z in new[] { 0.1, 1.7, 6, 30 })
                Assert.AreEqual(1 - LogisticModel.Sigmoid(z), LogisticModel.Sigmoid(-z), 1e-12);
            Assert.AreEqual(1, LogisticModel.Sigmoid(800), 1e-15);
            Assert.AreEqual(0, LogisticModel.Sigmoid(-800), 1e-15);
            Assert.False(double.IsNaN(LogisticModel.Sigmoid(-1e6)));
        }

        [Test]
        public void Bce_MatchesNaiveFormula_AndStaysFiniteForConfidentMistakes()
        {
            foreach (var z in new[] { -4.0, -0.3, 0, 0.8, 5 })
            {
                double p = 1 / (1 + Math.Exp(-z));
                Assert.AreEqual(-Math.Log(p), LogisticModel.Bce(z, 1), 1e-10);
                Assert.AreEqual(-Math.Log(1 - p), LogisticModel.Bce(z, 0), 1e-10);
            }
            Assert.AreEqual(1000, LogisticModel.Bce(-1000, 1), 1e-9);   // confidently wrong: big but finite
            Assert.AreEqual(0, LogisticModel.Bce(1000, 1), 1e-12);
            // a confident wrong answer costs more than a cautious wrong answer
            Assert.Greater(LogisticModel.Bce(-3, 1), LogisticModel.Bce(-0.2, 1));
        }

        [Test]
        public void ProbabilityThreshold_AgreesWithPlaneSide_ForRandomModels()
        {
            var rng = new System.Random(7);
            for (int t = 0; t < 200; t++)
            {
                var p = new ModelParams(new[] { rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3 }, rng.NextDouble() * 4 - 2);
                foreach (var x in fs3.Train.X)
                {
                    bool byProb = LogisticModel.Probability(p, x) >= LogisticModel.DecisionThreshold;
                    bool bySide = LogisticModel.Score(p, x) >= 0;
                    Assert.AreEqual(bySide, byProb);
                    Assert.AreEqual(bySide ? 1 : 0, LogisticModel.Predict(p, x));
                }
            }
        }

        [Test]
        public void Objective_MatchesPythonReference()
        {
            var p = new ModelParams(new[] { 0.5, -0.25, 0.75 }, 0.1);
            Assert.AreEqual(0.430231123030, LogisticModel.Objective(p, fs3.Train), 1e-9);
        }

        [Test]
        public void Gradient_MatchesPythonReference_AndFiniteDifferences()
        {
            var p = new ModelParams(new[] { 0.5, -0.25, 0.75 }, 0.1);
            var g = LogisticModel.Gradient(p, fs3.Train);
            double[] refW = { -0.063729753340, 0.080141842187, -0.144342045500 };
            for (int j = 0; j < 3; j++) Assert.AreEqual(refW[j], g.W[j], 1e-9);
            Assert.AreEqual(0.091704015312, g.B, 1e-9);

            const double h = 1e-5;
            for (int j = 0; j <= 3; j++)
            {
                var up = p.Clone(); var dn = p.Clone();
                if (j < 3) { up.W[j] += h; dn.W[j] -= h; } else { up.B += h; dn.B -= h; }
                double numeric = (LogisticModel.Objective(up, fs3.Train) - LogisticModel.Objective(dn, fs3.Train)) / (2 * h);
                Assert.AreEqual(numeric, j < 3 ? g.W[j] : g.B, 1e-7, $"param {j}");
            }
        }

        // ---------- gradient descent ----------

        [Test]
        public void Training_MatchesPythonReference()
        {
            var tr = new Trainer(fs3.Train, ModelParams.Zero(3));
            for (int i = 0; i < 100; i++) tr.Step(0.5);
            double[] refW = { 1.092435623028, -1.424262307971, 2.645329951857 };
            for (int j = 0; j < 3; j++) Assert.AreEqual(refW[j], tr.Params.W[j], 1e-8);
            Assert.AreEqual(-0.537795886268, tr.Params.B, 1e-8);
            Assert.AreEqual(0.233667518352, LogisticModel.Objective(tr.Params, fs3.Train), 1e-9);
        }

        [Test]
        public void SuitableRate_DecreasesLossEveryStep_AndLearnsAUsefulRule()
        {
            var tr = new Trainer(fs3.Train, ModelParams.Zero(3));
            for (int i = 0; i < 60; i++)
            {
                var s = tr.Step(1.0);
                Assert.LessOrEqual(s.ObjectiveAfter, s.ObjectiveBefore + 1e-12, $"step {i}");
            }
            Assert.Greater(LogisticModel.Evaluate(tr.Params, fs3.Train).Accuracy, 0.9);
            Assert.Greater(LogisticModel.Evaluate(tr.Params, fs3.Test).Accuracy, 0.85);
        }

        [Test]
        public void LearningRateRegimes_EmergeFromTheRealObjective()
        {
            double Final(double lr, out double max)
            {
                var tr = new Trainer(fs3.Train, ModelParams.Zero(3)); max = 0;
                for (int i = 0; i < 60; i++) max = Math.Max(max, tr.Step(lr).ObjectiveAfter);
                return LogisticModel.Objective(tr.Params, fs3.Train);
            }
            double slow = Final(0.01, out _), good = Final(1.0, out _), osc = Final(20, out double oscMax), blow = Final(200, out _);
            Assert.Greater(slow, 0.5);              // tiny steps: still far from the valley floor
            Assert.Less(good, 0.25);                // suitable steps: converged
            Assert.Greater(oscMax, Math.Log(2));    // big steps overshoot above the starting loss
            Assert.Greater(blow, 1000);             // excessive steps diverge
        }

        // ---------- plane geometry ----------

        [Test]
        public void Translate_FollowsFormula_AndShiftsEveryPointsDistance()
        {
            var p = new ModelParams(new[] { 1.2, -0.7, 2.0 }, 0.3);
            double d = 0.8, norm = p.WeightNorm();
            var q = PlaneGeometry.Translate(p, d);
            Assert.AreEqual(p.B - norm * d, q.B, 1e-12);
            foreach (var x in fs3.Train.X)
                Assert.AreEqual(PlaneGeometry.SignedDistance(p, x) - d, PlaneGeometry.SignedDistance(q, x), 1e-12);
            // a point on the old plane moved by d along the normal lies on the new plane
            var onOld = PlaneGeometry.Project(p, fs3.Train.X[0]);
            var n = PlaneGeometry.UnitNormal(p);
            var moved = new double[3];
            for (int j = 0; j < 3; j++) moved[j] = onOld[j] + d * n[j];
            Assert.AreEqual(0, LogisticModel.Score(q, moved), 1e-12);
        }

        [Test]
        public void Rotate_KeepsSteepnessAndPivot_AndSetsNormal()
        {
            var p = new ModelParams(new[] { 1.2, -0.7, 2.0 }, 0.3);
            var pivot = PlaneGeometry.Project(p, new[] { 0.0, 0.0, 0.0 });
            var q = PlaneGeometry.Rotate(p, new[] { 0.0, 1.0, 1.0 }, pivot);
            Assert.AreEqual(p.WeightNorm(), q.WeightNorm(), 1e-12);
            Assert.AreEqual(0, LogisticModel.Score(q, pivot), 1e-12);
            var u = PlaneGeometry.UnitNormal(q);
            Assert.AreEqual(0, u[0], 1e-12);
            Assert.AreEqual(Math.Sqrt(0.5), u[1], 1e-12);
        }

        [Test]
        public void Steepness_ChangesConfidenceButNotTheBoundary()
        {
            var p = new ModelParams(new[] { 1.2, -0.7, 2.0 }, 0.3);
            var q = PlaneGeometry.SetSteepness(p, 3 * p.WeightNorm());
            Assert.AreEqual(3 * p.WeightNorm(), q.WeightNorm(), 1e-12);
            foreach (var x in fs3.Train.X)
            {
                Assert.AreEqual(PlaneGeometry.SignedDistance(p, x), PlaneGeometry.SignedDistance(q, x), 1e-12);
                Assert.AreEqual(LogisticModel.Predict(p, x), LogisticModel.Predict(q, x));
                double pp = LogisticModel.Probability(p, x), qq = LogisticModel.Probability(q, x);
                if (Math.Abs(pp - 0.5) > 1e-9) Assert.Greater(Math.Abs(qq - 0.5), Math.Abs(pp - 0.5));
            }
        }

        [Test]
        public void ZeroWeights_HaveNoBoundary_AndGeometryRefuses()
        {
            var p = new ModelParams(new[] { 0.0, 1e-9, 0.0 }, 0.4);
            Assert.False(PlaneGeometry.HasBoundary(p));
            Assert.Throws<InvalidOperationException>(() => PlaneGeometry.Translate(p, 1));
            Assert.Throws<InvalidOperationException>(() => PlaneGeometry.UnitNormal(p));
        }

        [Test]
        public void PlaneClip_VerticesLieOnPlaneAndInsideCube()
        {
            var rng = new System.Random(3);
            for (int t = 0; t < 100; t++)
            {
                var p = new ModelParams(new[] { rng.NextDouble() * 4 - 2, rng.NextDouble() * 4 - 2, rng.NextDouble() * 4 - 2 }, rng.NextDouble() * 2 - 1);
                var poly = PlaneClip.ClipToCube(p, 3);
                Assert.That(poly.Count == 0 || (poly.Count >= 3 && poly.Count <= 6), $"count {poly.Count}");
                foreach (var v in poly)
                {
                    Assert.AreEqual(0, LogisticModel.Score(p, v) / p.WeightNorm(), 1e-9);
                    foreach (var c in v) Assert.That(Math.Abs(c), Is.LessThanOrEqualTo(3 + 1e-9));
                }
            }
            // axis-aligned plane x = 0 cuts the cube in a square
            Assert.AreEqual(4, PlaneClip.ClipToCube(new ModelParams(new[] { 1.0, 0, 0 }, 0), 3).Count);
            // plane outside the cube
            Assert.AreEqual(0, PlaneClip.ClipToCube(new ModelParams(new[] { 1.0, 0, 0 }, -10), 3).Count);
        }

        [Test]
        public void Shipment_MatchesDatasetDistribution_AndBatchCountsAddUp()
        {
            var ship = ShipmentGenerator.Generate(10000);
            double m = 0; foreach (var e in ship) m += e[0]; m /= ship.Length;
            Assert.AreEqual(fs3.Scaler.Mean[0], m, 1.0, "shipment mean mass should match the training data");
            var good = new ModelParams(new[] { 1.1, -1.4, 2.6 }, -0.5);
            var r = ShipmentGenerator.Classify(ship, fs3.Scaler, fs3.Active, good, 0.15);
            Assert.AreEqual(10000, r.Jumbo + r.Standard);
            Assert.That(r.FlaggedForReview, Is.InRange(1, 3000));
            Assert.That(r.Jumbo, Is.InRange(2000, 7000));   // both classes occur in a realistic shipment
        }

        // ---------- loss landscape ----------

        [Test]
        public void LossSlice_AgreesWithObjective_AndKnowsWhatItHolds()
        {
            var held = new ModelParams(new[] { 1.0, -1.4, 2.6 }, -0.5);
            var s = new LossSlice(0, 2, -2, 4, -1, 5, 31, held);
            s.Compute(fs3.Train);
            var probe = held.Clone(); probe.W[0] = s.ParamAt(0, 10); probe.W[2] = s.ParamAt(1, 20);
            Assert.AreEqual(LogisticModel.Objective(probe, fs3.Train), s.Values[10, 20], 1e-12);
            Assert.True(s.Contains(probe));
            var moved = probe.Clone(); moved.B += 0.1;
            Assert.False(s.Contains(moved));   // changing a held parameter leaves the slice
            Assert.LessOrEqual(s.MinValue, s.Values[10, 20]);
        }

        [Test]
        public void TwoFeatureIntro_IsWorseThanThreeFeatures()
        {
            var fs2 = new FeatureSpace(ds, new[] { 0, 1 });
            var t2 = new Trainer(fs2.Train, ModelParams.Zero(2));
            var t3 = new Trainer(fs3.Train, ModelParams.Zero(3));
            for (int i = 0; i < 300; i++) { t2.Step(1.0); t3.Step(1.0); }
            Assert.Greater(LogisticModel.Evaluate(t3.Params, fs3.Train).Accuracy,
                           LogisticModel.Evaluate(t2.Params, fs2.Train).Accuracy + 0.03);
        }
    }
}
