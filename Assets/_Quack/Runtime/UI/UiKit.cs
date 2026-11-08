using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>Code-built uGUI helpers. Every Text registers its base size so the text-scale setting applies everywhere.</summary>
    public sealed class UiKit
    {
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.88f);
        public static readonly Color TextColor = new Color(0.96f, 0.96f, 0.96f);
        public static readonly Color MutedText = new Color(0.75f, 0.78f, 0.85f);
        public static readonly Color Accent = new Color(1f, 0.83f, 0.2f);

        readonly List<(Text text, int baseSize)> texts = new List<(Text, int)>();
        readonly List<(TMPro.TMP_Text text, int baseSize)> tmps = new List<(TMPro.TMP_Text, int)>();
        /// <summary>3D (world-space) panels: labels are TextMeshPro SDF text, crisp at any distance/angle.</summary>
        public bool Tmp;
        public float Scale = 1f;
        /// <summary>Game chrome (pause menu, settings window, title): royal Cinzel Decorative instead of the learning font.</summary>
        public bool Royal;

        public void ApplyScale(float s)
        {
            Scale = s;
            foreach (var (t, b) in texts) if (t) t.fontSize = Mathf.RoundToInt(b * s);
            foreach (var (t, b) in tmps) if (t) t.fontSize = b * s;
        }

        public RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, float width, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 100);
            go.GetComponent<Image>().color = color ?? PanelColor;
            var lg = go.GetComponent<VerticalLayoutGroup>();
            lg.padding = new RectOffset(16, 16, 12, 12); lg.spacing = 6;
            lg.childControlHeight = true; lg.childControlWidth = true; lg.childForceExpandHeight = false; lg.childForceExpandWidth = true;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        public RectTransform Row(Transform parent, float spacing = 8)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var lg = go.GetComponent<HorizontalLayoutGroup>();
            lg.spacing = spacing; lg.childControlHeight = true; lg.childControlWidth = true;
            lg.childForceExpandWidth = false; lg.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        public Text Text(Transform parent, string s, int size, Color? color = null, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            // learning content: Atkinson Hyperlegible everywhere; the royal face is applied explicitly to game chrome (UiKit.Royal)
            t.font = Royal ? VizUtil.HeadingFont : VizUtil.Font; t.text = s; t.color = color ?? TextColor; t.fontStyle = Royal ? FontStyle.Normal : style; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            texts.Add((t, size));
            t.fontSize = Mathf.RoundToInt(size * Scale);
            return t;
        }

        /// <summary>TextMeshPro version of Text() for world-space panels.</summary>
        public TMPro.TextMeshProUGUI T(Transform parent, string s, int size, Color? color = null, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            var f = Royal ? VizUtil.TmpRoyal : VizUtil.TmpBody;
            if (f != null) t.font = f;
            t.text = s; t.color = color ?? TextColor; t.richText = true;
            t.fontStyle = Royal ? TMPro.FontStyles.Normal : style == FontStyle.Bold ? TMPro.FontStyles.Bold
                : style == FontStyle.Italic ? TMPro.FontStyles.Italic : style == FontStyle.BoldAndItalic ? TMPro.FontStyles.Bold | TMPro.FontStyles.Italic : TMPro.FontStyles.Normal;
            t.alignment = Align(align);
            t.textWrappingMode = TMPro.TextWrappingModes.Normal;
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.raycastTarget = false;
            tmps.Add((t, size));
            t.fontSize = size * Scale;
            return t;
        }

        public static TMPro.TextAlignmentOptions Align(TextAnchor a) => a switch
        {
            TextAnchor.UpperLeft => TMPro.TextAlignmentOptions.TopLeft, TextAnchor.UpperCenter => TMPro.TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TMPro.TextAlignmentOptions.TopRight, TextAnchor.MiddleLeft => TMPro.TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TMPro.TextAlignmentOptions.Center, TextAnchor.MiddleRight => TMPro.TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TMPro.TextAlignmentOptions.BottomLeft, TextAnchor.LowerCenter => TMPro.TextAlignmentOptions.Bottom,
            _ => TMPro.TextAlignmentOptions.BottomRight,
        };

        Graphic Label(Transform parent, string s, int size, Color? color = null, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
            => Tmp ? (Graphic)T(parent, s, size, color, style, align) : Text(parent, s, size, color, style, align);

        public Button Button(Transform parent, string label, Action onClick, int size = 20, float minWidth = 0)
        {
            var go = new GameObject("button " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.36f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 40; le.minWidth = minWidth; le.preferredHeight = 40;
            var b = go.GetComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(0.45f, 0.5f, 0.65f); colors.pressedColor = Accent; b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Label(go.transform, label, size, align: TextAnchor.MiddleCenter);

            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 2); trt.offsetMax = new Vector2(-10, -2);
            if (minWidth <= 0) { var hl = go.AddComponent<HorizontalLayoutGroup>(); hl.padding = new RectOffset(12, 12, 6, 6); hl.childControlWidth = true; hl.childControlHeight = true; }
            return b;
        }

        /// <summary>Labelled slider. onBegin/onEnd bracket a drag so feedback fires once per gesture.</summary>
        public Slider Slider(Transform parent, string label, float min, float max, Action<float> onChange, Action onBegin, Action onEnd, out Text valueText)
        {
            var s = SliderCore(parent, label, min, max, onChange, onBegin, onEnd, out var g); valueText = (Text)g; return s;
        }

        public Slider Slider(Transform parent, string label, float min, float max, Action<float> onChange, Action onBegin, Action onEnd, out TMPro.TMP_Text valueText)
        {
            var s = SliderCore(parent, label, min, max, onChange, onBegin, onEnd, out var g); valueText = (TMPro.TMP_Text)g; return s;
        }

        Slider SliderCore(Transform parent, string label, float min, float max, Action<float> onChange, Action onBegin, Action onEnd, out Graphic valueText)
        {
            var row = Row(parent, 10);
            var name = Label(row, label, 18, MutedText);
            name.gameObject.AddComponent<LayoutElement>().minWidth = 150;

            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(row, false);
            go.AddComponent<LayoutElement>().minWidth = 200;
            go.GetComponent<LayoutElement>().minHeight = 26;
            var s = go.GetComponent<Slider>();
            s.minValue = min; s.maxValue = max;
            foreach (var img in go.GetComponentsInChildren<Image>())
                img.color = img.name == "Handle" ? Accent : img.name == "Fill" ? new Color(0.5f, 0.55f, 0.7f) : new Color(0.25f, 0.27f, 0.33f);
            s.onValueChanged.AddListener(v => onChange(v));
            var trig = go.AddComponent<EventTrigger>();
            AddTrigger(trig, EventTriggerType.PointerDown, onBegin);
            AddTrigger(trig, EventTriggerType.PointerUp, onEnd);

            valueText = Label(row, "", 18);
            valueText.gameObject.AddComponent<LayoutElement>().minWidth = 70;
            return s;
        }

        public Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var go = DefaultControls.CreateToggle(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = 30;
            var tg = go.GetComponent<Toggle>();
            tg.isOn = value;
            var t = go.GetComponentInChildren<Text>();
            t.text = label; t.font = VizUtil.Font; t.color = TextColor;
            texts.Add((t, 18)); t.fontSize = Mathf.RoundToInt(18 * Scale);
            foreach (var img in go.GetComponentsInChildren<Image>())
                img.color = img.name == "Checkmark" ? Accent : new Color(0.3f, 0.32f, 0.4f);
            tg.onValueChanged.AddListener(v => onChange(v));
            return tg;
        }

        public InputField Input(Transform parent, string placeholder, Action<string> onSubmit)
        {
            var go = DefaultControls.CreateInputField(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.minHeight = 40; le.flexibleWidth = 1; le.minWidth = 300;
            go.GetComponent<Image>().color = new Color(0.93f, 0.93f, 0.95f);
            var f = go.GetComponent<InputField>();
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.font = VizUtil.Font; texts.Add((t, 18)); t.fontSize = Mathf.RoundToInt(18 * Scale);
                if (t.name == "Placeholder") { t.text = placeholder; t.color = new Color(0.4f, 0.4f, 0.45f); }
                else t.color = new Color(0.05f, 0.05f, 0.08f);
            }
            f.onSubmit.AddListener(s => { if (!string.IsNullOrWhiteSpace(s)) { onSubmit(s); f.text = ""; } });
            return f;
        }

        static void AddTrigger(EventTrigger trig, EventTriggerType type, Action a)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => a());
            trig.triggers.Add(entry);
        }
    }
}
