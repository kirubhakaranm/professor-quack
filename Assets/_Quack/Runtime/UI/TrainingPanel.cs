using System;
using System.Collections;
using Quack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Gradient-descent control desk on the table edge. Every step is a real full-batch step on the training
    /// objective (Trainer); slow / converging / overshooting / diverging behaviour comes only from the chosen
    /// learning rate. The data never moves; only the rule's parameters do.
    /// </summary>
    public sealed class TrainingPanel : MonoBehaviour
    {
        GameState state;
        LandscapeView landscape;
        readonly UiKit ui = new UiKit();
        Trainer trainer;
        Slider lrSlider;
        TMPro.TMP_Text lrText, readout, status, playLabel;
        Coroutine playing;
        int steps;
        public RectTransform Root { get; private set; }
        public double LearningRate { get; private set; } = 1.0;
        public event Action<TrainStep> StepTaken;

        public void Build(GameState s, LandscapeView l, AccessSettings a, Transform anchor, Camera cam)
        {
            state = s; landscape = l; ui.Scale = a.TextScale; ui.Tmp = true;
            Root = WorldPanel.Create("Training control", anchor, cam, 0.66f, 880, 470);
            var p = WorldPanel.Body(ui, Root, "training");
            ui.T(p, "TRAINING CONTROL  (gradient descent)", 28, UiKit.Accent, FontStyle.Bold);
            ui.T(p, "each step:  parameters <- parameters - learning_rate x gradient of loss", 20, UiKit.MutedText);
            lrSlider = ui.Slider(p, "learning rate", 0, 1, OnLr, () => { }, () => { }, out lrText);
            var row = ui.Row(p, 10);
            ui.Button(row, "Step", () => { Pause(); DoStep(); }, 22);
            playLabel = ui.Button(row, "Play", TogglePlay, 22).GetComponentInChildren<TMPro.TMP_Text>();
            ui.Button(row, "Reset to broken sorter", ResetRule, 20);
            readout = ui.T(p, "", 22);
            status = ui.T(p, "", 22, new Color(1f, 0.85f, 0.5f), FontStyle.Bold);
            WorldPanel.EnableRayInput(Root);
            lrSlider.SetValueWithoutNotify(ToSlider(1.0)); OnLr(ToSlider(1.0));
            a.Changed += () => ui.ApplyScale(a.TextScale);
            UpdateReadout(null);
        }

        // learning rate on a log scale: 0.01 .. ~200
        static double FromSlider(float t) => Math.Pow(10, -2 + t * 4.3);
        static float ToSlider(double lr) => (float)((Math.Log10(lr) + 2) / 4.3);

        void OnLr(float t)
        {
            LearningRate = FromSlider(t);
            lrText.text = LearningRate < 1 ? LearningRate.ToString("0.000") : LearningRate.ToString("0.0");
        }

        public void SetLearningRate(double lr) { lrSlider.SetValueWithoutNotify(ToSlider(lr)); OnLr(ToSlider(lr)); }

        public void ResetRule()
        {
            Pause();
            state.SetParams(QuackGame.OldSorterParams(), ParamSource.Reset);
            landscape.ClearTrail();
            steps = 0;
            UpdateReadout(null);
            status.text = "Reset to the weight-only sorter. Pick a learning rate and press Step or Play.";
        }

        public TrainStep DoStep()
        {
            if (trainer == null) trainer = new Trainer(state.Space.Train, state.Params);
            else trainer.Reset(state.Params);   // continue from wherever the learner left the rule
            if (steps == 0) landscape.AddTrailPoint();
            var st = trainer.Step(LearningRate);
            steps++;
            state.SetParams(st.After, ParamSource.Training);
            landscape.AddTrailPoint();
            UpdateReadout(st);
            StepTaken?.Invoke(st);
            if (st.Diverged) Pause();
            return st;
        }

        void TogglePlay()
        {
            if (playing != null) { Pause(); return; }
            playLabel.text = "Pause";
            playing = StartCoroutine(Play());
        }

        public void Pause()
        {
            if (playing != null) StopCoroutine(playing);
            playing = null;
            if (playLabel) playLabel.text = "Play";
        }

        IEnumerator Play()
        {
            for (int i = 0; i < 300; i++)
            {
                var st = DoStep();
                if (st.Diverged || Math.Abs(st.ObjectiveBefore - st.ObjectiveAfter) < 1e-6) break;
                yield return new WaitForSeconds(0.18f);
            }
            Pause();
        }

        void UpdateReadout(TrainStep? st)
        {
            double j = LogisticModel.Objective(state.Params, state.Space.Train);
            var c = state.TrainResult;
            readout.text = $"steps: {steps}    loss: {j:0.0000}    accuracy: {c.Accuracy:P0}";
            if (st == null) return;
            var s = st.Value;
            double g = Math.Sqrt(s.Gradient.W[0] * s.Gradient.W[0] + s.Gradient.W[1] * s.Gradient.W[1] + s.Gradient.W[2] * s.Gradient.W[2] + s.Gradient.B * s.Gradient.B);
            double d = s.ObjectiveAfter - s.ObjectiveBefore;
            readout.text += $"\nlast step: loss {s.ObjectiveBefore:0.0000} -> {s.ObjectiveAfter:0.0000} ({(d <= 0 ? "down" : "UP")} {Math.Abs(d):0.0000}),  |gradient| = {g:0.000}";
            status.text = s.Diverged ? "Diverging! The steps are so big the loss explodes. Turn the learning rate down and reset."
                : d > 0 ? "The loss went UP: the step overshot the valley floor. Try a smaller learning rate."
                : Math.Abs(d) < 1e-5 ? "Barely moving: either at the bottom, or the learning rate is tiny."
                : "Going downhill.";
        }
    }
}
