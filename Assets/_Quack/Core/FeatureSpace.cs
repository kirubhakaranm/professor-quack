using System.Collections.Generic;

namespace Quack.Core
{
    /// <summary>
    /// Which features are active (e.g. {Mass, Length} in the 2-feature introduction, all three later)
    /// and the standardised design matrices built from the dataset.
    /// </summary>
    public sealed class FeatureSpace
    {
        public readonly EggDataset Dataset;
        public readonly FeatureScaler Scaler;   // fitted on training eggs, all features
        public readonly int[] Active;           // indices into Egg.Raw
        public readonly DesignMatrix Train, Test;

        public FeatureSpace(EggDataset ds, int[] active)
        {
            Dataset = ds; Active = (int[])active.Clone();
            Scaler = FeatureScaler.Fit(ds.Train);
            Train = Build(ds.Train);
            Test = Build(ds.Test);
        }

        public int Dim => Active.Length;

        /// <summary>Standardised feature vector of an egg over the active features.</summary>
        public double[] Vector(Egg e)
        {
            var x = new double[Active.Length];
            for (int k = 0; k < Active.Length; k++) x[k] = Scaler.ToStd(Active[k], e.Raw[Active[k]]);
            return x;
        }

        DesignMatrix Build(List<Egg> eggs)
        {
            var x = new double[eggs.Count][];
            var y = new int[eggs.Count];
            for (int i = 0; i < eggs.Count; i++) { x[i] = Vector(eggs[i]); y[i] = eggs[i].Label; }
            return new DesignMatrix(x, y);
        }
    }
}
