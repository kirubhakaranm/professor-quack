using System;
using System.Collections.Generic;
using Quack.Core;

namespace Quack
{
    /// <summary>Why the parameters changed — lets the tutor tell a drag from a slider from training.</summary>
    public enum ParamSource { Reset, PlaneTranslate, PlaneRotate, Steepness, Slider, Training, Landscape }

    /// <summary>A recent learner action, kept for tutor context.</summary>
    public struct PlayerAction
    {
        public float Time;
        public string Kind;      // "select", "translate", "rotate", "steepness", "slider", "view", "question"
        public string Detail;
    }

    /// <summary>
    /// Single source of truth for the simulation. Views read from it; input writes through its methods.
    /// Every visual (plot, plane, arrow, conveyor, captions) is derived from these values.
    /// </summary>
    public sealed class GameState
    {
        public readonly EggDataset Dataset;
        public FeatureSpace Space { get; private set; }

        public ModelParams Params { get; private set; }
        public Egg Selected { get; private set; }
        public bool Interacting { get; private set; }   // a handle drag is in progress

        public Confusion TrainResult { get; private set; }
        public double TrainObjective { get; private set; }

        public readonly List<PlayerAction> RecentActions = new List<PlayerAction>();
        public Func<float> Clock = () => 0f;
        /// <summary>Current lesson stage id sent to the tutor (replaced by the lesson director).</summary>
        public string Stage = "A_describe_eggs";

        public event Action<ParamSource> ParamsChanged;
        /// <summary>Raised once when a drag or slider gesture ends (for debounced feedback).</summary>
        public event Action<ParamSource, ModelParams> EditCommitted;
        public event Action<Egg> SelectionChanged;
        public event Action WidthLockChanged;

        /// <summary>
        /// Two-feature introduction: width "not measured yet". Its weight is held at exactly 0, so the model is
        /// the 2-feature model (mass, length) evaluated on the same eggs. Unlocking adds the third feature.
        /// </summary>
        public bool WidthLocked { get; private set; }

        public void SetWidthLocked(bool locked)
        {
            if (WidthLocked == locked) return;
            WidthLocked = locked;
            Log("width", locked ? "locked" : "unlocked");
            SetParams(Params, ParamSource.Reset);
            WidthLockChanged?.Invoke();
        }

        ModelParams editStart;

        public GameState(EggDataset dataset, int[] activeFeatures, ModelParams start)
        {
            Dataset = dataset;
            Space = new FeatureSpace(dataset, activeFeatures);
            SetParams(start, ParamSource.Reset);
        }

        // ---------- queries ----------

        public double[] Vector(Egg e) => Space.Vector(e);
        public double Probability(Egg e) => LogisticModel.Probability(Params, Vector(e));
        public int Prediction(Egg e) => LogisticModel.Predict(Params, Vector(e));
        public bool IsMisrouted(Egg e) => Prediction(e) != e.Label;
        public bool HasBoundary => PlaneGeometry.HasBoundary(Params);

        // ---------- commands ----------

        public void SetParams(ModelParams p, ParamSource source)
        {
            Params = p.Clone();
            if (WidthLocked && Params.Dim > 2) Params.W[2] = 0;
            TrainResult = LogisticModel.Evaluate(Params, Space.Train);
            TrainObjective = LogisticModel.Objective(Params, Space.Train);
            ParamsChanged?.Invoke(source);
        }

        public void BeginEdit() { Interacting = true; editStart = Params.Clone(); }

        public void EndEdit(ParamSource source)
        {
            Interacting = false;
            var before = editStart ?? Params;
            editStart = null;
            Log(source.ToString().ToLowerInvariant(), Params.ToString());
            EditCommitted?.Invoke(source, before);
        }

        public void Select(Egg e)
        {
            if (e == Selected) return;
            Selected = e;
            if (e != null) Log("select", $"egg #{e.Id}");
            SelectionChanged?.Invoke(e);
        }

        public void Log(string kind, string detail)
        {
            RecentActions.Add(new PlayerAction { Time = Clock(), Kind = kind, Detail = detail });
            if (RecentActions.Count > 12) RecentActions.RemoveAt(0);
        }
    }
}
