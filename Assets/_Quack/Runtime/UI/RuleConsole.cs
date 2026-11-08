using Quack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The control desk's screen, set into the desk face: the live equation, the score, and large -/+ step buttons for
    /// every number in the rule. The buttons are the exact alternative to dragging the dials, lever or sheet
    /// (accessibility: no freehand precision required; mouse click or VR trigger).
    /// </summary>
    public sealed class RuleConsole : MonoBehaviour
    {
        public const float ScreenMetresWide = 0.46f;
        public const float PixelW = 640, PixelH = 460;

        GameState state;
        readonly UiKit ui = new UiKit();
        readonly TMPro.TMP_Text[] wVal = new TMPro.TMP_Text[3];
        readonly Button[] wMinus = new Button[3], wPlus = new Button[3];
        TMPro.TMP_Text bVal, steepVal, accuracy, widthNote, loss;
        Button steepMinus, steepPlus;
        bool showLoss;
        public bool ShowLoss { get => showLoss; set { showLoss = value; Sync(); } }
        public RectTransform Root { get; private set; }
        public TMPro.TMP_Text Equation { get; private set; }
        GameObject steepRow;
        bool showSteep;
        /// <summary>Steepness is introduced with probabilities (stage C); hidden before that.</summary>
        public bool ShowSteepness { get => showSteep; set { showSteep = value; if (steepRow) steepRow.SetActive(value); } }

        static readonly Color Screen = new Color(0.02f, 0.05f, 0.06f, 1f);
        static readonly Color Lcd = new Color(0.55f, 1f, 0.7f);

        public void Build(GameState s, AccessSettings a, Transform anchor, Camera cam)
        {
            state = s; ui.Scale = a.TextScale; ui.Tmp = true;
            Root = WorldPanel.Create("Sorting rule console", anchor, cam, ScreenMetresWide, PixelW, PixelH);

            // fixed-size screen (never grows past the desk's recess)
            var bg = new GameObject("screen", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            bg.transform.SetParent(Root, false);
            var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = Screen;
            var v = bg.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 10, 10); v.spacing = 4;
            v.childControlHeight = v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            var p = brt;

            ui.T(p, "SORTING RULE", 24, UiKit.Accent, FontStyle.Bold);
            Equation = ui.T(p, "", 22, UiKit.TextColor);   // filled live by EquationView

            string[] names = { "mass", "length", "width" };
            for (int j = 0; j < 3; j++)
            {
                int jj = j;
                StepRow(p, names[j], Color.Lerp(QuackPalette.FeatureColor[j], Color.white, 0.35f), d => Nudge(jj, d), 0.1f, out wVal[j], out wMinus[j], out wPlus[j]);
            }
            widthNote = ui.T(p, "width: not measured yet", 18, new Color(1f, 0.75f, 0.4f));
            StepRow(p, "offset b", Color.Lerp(QuackPalette.BiasColor, Color.white, 0.35f), d => Nudge(3, d), 0.1f, out bVal, out _, out _);
            steepRow = StepRow(p, "steepness", new Color(0.9f, 0.9f, 0.95f), Steep, 0.2f, out steepVal, out steepMinus, out steepPlus);

            var bottom = ui.Row(p, 12);
            accuracy = ui.T(bottom, "", 24, Lcd, FontStyle.Bold);
            accuracy.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var reset = ui.Button(bottom, "Reset", () => { s.SetParams(QuackGame.OldSorterParams(), ParamSource.Reset); s.Log("reset", "old sorter"); }, 20, 110);
            reset.GetComponent<LayoutElement>().minHeight = 38;
            loss = ui.T(p, "", 20, new Color(0.75f, 0.85f, 1f));
            WorldPanel.EnableRayInput(Root);

            s.ParamsChanged += _ => Sync();
            s.WidthLockChanged += Sync;
            a.Changed += () => ui.ApplyScale(a.TextScale);
            Sync();
        }

        /// <summary>One parameter: name, big LCD value, and -/+ buttons (each press = one exact step).</summary>
        GameObject StepRow(Transform parent, string label, Color col, System.Action<float> step, float size, out TMPro.TMP_Text value, out Button minus, out Button plus)
        {
            var row = ui.Row(parent, 10);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var name = ui.T(row, label, 24, col, FontStyle.Bold);
            name.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
            minus = ui.Button(row, "−", () => step(-size), 30, 64);
            minus.GetComponent<LayoutElement>().minHeight = 44;
            value = ui.T(row, "", 34, Lcd, FontStyle.Normal, TextAnchor.MiddleCenter);
            value.font = VizUtil.TmpLcd;
            value.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
            plus = ui.Button(row, "+", () => step(size), 30, 64);
            plus.GetComponent<LayoutElement>().minHeight = 44;
            return row.gameObject;
        }

        void Nudge(int k, float d)
        {
            var p = state.Params.Clone();
            if (k < 3) { if (k == 2 && state.WidthLocked) return; p.W[k] = Mathf.Clamp((float)p.W[k] + d, -ControlDesk.Range, ControlDesk.Range); }
            else p.B = Mathf.Clamp((float)p.B + d, -ControlDesk.Range, ControlDesk.Range);
            state.BeginEdit(); state.SetParams(p, ParamSource.Slider); state.EndEdit(ParamSource.Slider);
        }

        void Steep(float d)
        {
            if (!state.HasBoundary) return;
            float n = Mathf.Clamp((float)state.Params.WeightNorm() + d, 0.2f, 8f);
            state.BeginEdit(); state.SetParams(PlaneGeometry.SetSteepness(state.Params, n), ParamSource.Steepness); state.EndEdit(ParamSource.Steepness);
        }

        void Sync()
        {
            if (state == null) return;
            var p = state.Params;
            for (int j = 0; j < 3; j++) wVal[j].text = p.W[j].ToString("+0.00;-0.00");
            wMinus[2].interactable = wPlus[2].interactable = !state.WidthLocked;
            if (state.WidthLocked) wVal[2].text = "--";
            widthNote.gameObject.SetActive(state.WidthLocked);
            bVal.text = p.B.ToString("+0.00;-0.00");
            steepVal.text = p.WeightNorm().ToString("0.00");
            steepMinus.interactable = steepPlus.interactable = state.HasBoundary;
            var c = state.TrainResult;
            accuracy.text = $"{c.Correct}/{c.Total} correct  ({c.Misrouted} wrong)";
            loss.gameObject.SetActive(showLoss);
            steepRow.SetActive(ShowSteepness);
            if (showLoss)
            {
                double data = LogisticModel.DataLoss(p, state.Space.Train);
                int bar = Mathf.Clamp(Mathf.RoundToInt((float)data * 20), 0, 30);
                loss.text = $"loss {data:0.000}  {new string('|', bar)}";
            }
        }
    }
}
