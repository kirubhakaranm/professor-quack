using Quack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The rule written out, live, on the console: score = w_mass·mass + w_length·length (+ w_width·width) + b,
    /// each term in its own colour (matching the console sliders). The term being changed pulses. In the flat
    /// 2-feature view the boundary is also written as a line (length = slope · mass + intercept).
    /// In the plot, a marker on each axis shows where the sheet cuts that axis, labelled in grams / millimetres.
    /// </summary>
    public sealed class EquationView : MonoBehaviour
    {
        GameState state;
        FeaturePlot plot;
        DecisionPlaneView plane;
        public TMPro.TMP_Text Target;                                  // the console's equation line
        readonly GameObject[] marks = new GameObject[3];     // axis intercept markers: mass, length, width
        readonly TMPro.TextMeshPro[] markLabels = new TMPro.TextMeshPro[3];
        readonly float[] changedAt = { -9, -9, -9, -9 };     // w_mass, w_length, w_width, b
        ModelParams last;
        bool visible = true;
        public bool Visible { get => visible; set { visible = value; Refresh(); } }
        public GameObject[] Objects => new GameObject[] { Target ? Target.gameObject : null, marks[0], marks[1], marks[2] };

        public void Build(GameState s, FeaturePlot p, DecisionPlaneView pv, QuackPalette pal)
        {
            state = s; plot = p; plane = pv;
            for (int k = 0; k < 3; k++)
            {
                var m = new Material(pal.Handle);
                var c = QuackPalette.FeatureColor[k];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c;
                marks[k] = VizUtil.Primitive(PrimitiveType.Sphere, $"intercept on {EggDataset.FeatureNames[k]} axis", transform, m);
                marks[k].transform.localScale = Vector3.one * 0.3f;
                markLabels[k] = VizUtil.Label("", transform, Vector3.zero, 0.32f, c * 0.75f);
            }
            last = s.Params.Clone();
            s.ParamsChanged += _ => { NoteChanges(); Refresh(); };
            s.WidthLockChanged += Refresh;
            p.DepthChanged += _ => Refresh();
            Refresh();
        }

        void NoteChanges()
        {
            var p = state.Params;
            for (int j = 0; j < 3; j++) if (System.Math.Abs(p.W[j] - last.W[j]) > 1e-6) changedAt[j] = Time.time;
            if (System.Math.Abs(p.B - last.B) > 1e-6) changedAt[3] = Time.time;
            last = p.Clone();
        }

        bool lastShown;
        void Update()
        {
            bool show = visible && plane.Visible;
            if (show != lastShown) { Refresh(); return; }
            for (int k = 0; k < 4; k++) if (Time.time - changedAt[k] < 1.2f) { Refresh(); break; }
        }

        string Term(int k, double v, string fmt = "0.00")
        {
            var col = k < 3 ? QuackPalette.FeatureColor[k] : QuackPalette.BiasColor;
            col = Color.Lerp(col, Color.white, 0.4f);   // readable on the dark console
            string num = v.ToString(fmt);
            bool hot = Time.time - changedAt[k] < 1.2f;
            return hot ? $"<b><color=#{QuackPalette.Hex(Color.white)}>{num}</color></b>" : $"<color=#{QuackPalette.Hex(col)}>{num}</color>";
        }

        static string Name(int k) => $"<color=#{QuackPalette.Hex(Color.Lerp(QuackPalette.FeatureColor[k], Color.white, 0.4f))}>{EggDataset.FeatureNames[k].ToLower()}</color>";

        void Refresh()
        {
            bool show = visible && plane.Visible;
            lastShown = show;
            var p = state.Params;
            bool flat = state.WidthLocked;
            if (Target != null)
            {
                Target.gameObject.SetActive(show);
                if (show)
                {
                    string bTerm = (p.B >= 0 ? " + " : " - ") + Term(3, System.Math.Abs(p.B));
                    string w3 = flat ? "" : $" + {Term(2, p.W[2])} {Name(2)}";
                    string line1 = $"score = {Term(0, p.W[0])} {Name(0)} + {Term(1, p.W[1])} {Name(1)}{w3}{bTerm}";
                    string line2;
                    if (flat && System.Math.Abs(p.W[1]) > 1e-3)
                    {
                        double slope = -p.W[0] / p.W[1], icpt = -p.B / p.W[1];
                        line2 = $"line:  {Name(1)} = {slope:+0.00;-0.00} {Name(0)} {(icpt >= 0 ? "+" : "-")} {System.Math.Abs(icpt):0.00}";
                    }
                    else line2 = "JUMBO where score > 0;  the sheet is score = 0";
                    Target.text = $"{line1}\n<size={Mathf.RoundToInt(Target.fontSize * 0.8f)}>{line2}</size>";
                }
            }

            // where the sheet cuts each drawn axis (axes run along the cube's back-bottom-left edges)
            float h = FeaturePlot.HalfExtent, d = Mathf.Max(0.001f, plot.DepthScale);
            var sc = state.Space.Scaler;
            string[] unit = { "g", "mm", "mm" };
            for (int k = 0; k < 3; k++)
            {
                bool on = show && state.HasBoundary && (k < 2 || !flat);
                double wk = p.W[k];
                float t = 0;
                if (on && System.Math.Abs(wk) > 1e-4)
                {
                    // the other two coordinates sit at the axis line (-h); solve w·x + b = 0 for coordinate k
                    double rest = p.B;
                    for (int j = 0; j < 3; j++) if (j != k) rest += p.W[j] * (j == 2 && flat ? 0 : -h);
                    t = (float)(-rest / wk);
                    on = t >= -h && t <= h;
                }
                else on = false;
                marks[k].SetActive(on); markLabels[k].gameObject.SetActive(on);
                if (!on) continue;
                var pos = new Vector3(-h, -h, -h * d); pos[k] = k == 2 ? t * d : t;
                marks[k].transform.localPosition = pos;
                var outward = k == 1 ? new Vector3(-0.9f, 0, 0) : new Vector3(0, -0.75f, 0);
                markLabels[k].transform.localPosition = pos + outward + (k == 0 ? new Vector3(0, -0.35f, 0) : Vector3.zero);
                markLabels[k].text = $"{sc.ToRaw(k, t):0} {unit[k]}";
            }
        }
    }
}
