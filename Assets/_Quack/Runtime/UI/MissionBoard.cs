using System;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The mission screen: a wide LED panel mounted above the hologram table (a fixed, physical place, never
    /// floating or fading). Left: lesson number, title, goal and a live checklist. Right: an LCD-style scoreboard
    /// and the lesson controls (contextual action, Next step, Skip lesson, Next lesson / Finish, End lesson).
    /// Driven by LessonDirector; usable by mouse and VR ray.
    /// </summary>
    public sealed class MissionBoard : MonoBehaviour
    {
        readonly UiKit ui = new UiKit();
        TMPro.TMP_Text lessonLabel, title, goal, checklist, score;
        Button action, next, skip, step, end;
        TMPro.TMP_Text actionLabel, nextLabel;
        Action onAction, onNext, onSkip;
        public Action NextStepRequested, EndRequested;
        public Action<int> ViewRequested;   // kept for callers; views are on keys 0-7
        public RectTransform Root { get; private set; }
        /// <summary>For highlighting the button Professor Quack asks the learner to press.</summary>
        public GameObject ActionButton => action ? action.gameObject : null;
        public GameObject NextButton => next ? next.gameObject : null;
        public GameObject StepButton => step ? step.gameObject : null;
        public GameObject SkipButton => skip ? skip.gameObject : null;
        public GameObject EndButton => end ? end.gameObject : null;
        public GameObject Scoreboard => score ? score.transform.parent.gameObject : null;

        public static readonly Color Led = new Color(1f, 0.72f, 0.25f);       // warm amber LED text
        public static readonly Color LedDim = new Color(0.75f, 0.55f, 0.25f);
        public static readonly Color Lcd = new Color(0.45f, 1f, 0.65f);       // green LCD digits
        static Font lcdFont;

        /// <summary>A monospaced system font for the LCD look where available (Windows); Arial elsewhere.</summary>
        public static Font LcdFont
        {
            get
            {
                if (lcdFont != null) return lcdFont;
#if !UNITY_WEBGL || UNITY_EDITOR
                lcdFont = VizUtil.LcdFont;   // VT323: a real LCD look on every platform
#endif
                return lcdFont ??= VizUtil.Font;
            }
        }

        public void Build(AccessSettings a, Transform anchor, Camera cam)
        {
            ui.Scale = a.TextScale; ui.Tmp = true;
            Root = WorldPanel.Create("Mission screen", anchor, cam, 1.3f, 1600, 470);   // big type: read from the rule view
            // dark bezel + screen
            var screen = new GameObject("screen", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            screen.transform.SetParent(Root, false);
            var srt = (RectTransform)screen.transform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero;
            screen.GetComponent<Image>().color = new Color(0.03f, 0.035f, 0.05f, 0.97f);
            var h = screen.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 28, 20, 20); h.spacing = 30;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            // left: the lesson
            var left = Column(screen.transform, 1000);
            lessonLabel = ui.T(left, "", 30, LedDim, FontStyle.Bold);
            title = ui.T(left, "", 54, Led, FontStyle.Bold);
            goal = ui.T(left, "", 30, new Color(0.92f, 0.9f, 0.86f));
            checklist = ui.T(left, "", 30, new Color(0.8f, 0.95f, 0.85f));

            // right: scoreboard + controls
            var right = Column(screen.transform, 500);
            var lcd = new GameObject("lcd", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            lcd.transform.SetParent(right, false);
            lcd.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.04f, 1f);
            var lv = lcd.GetComponent<VerticalLayoutGroup>(); lv.padding = new RectOffset(16, 16, 10, 10); lv.childControlHeight = lv.childControlWidth = true;
            score = ui.T(lcd.transform, "", 40, Lcd, FontStyle.Bold);
            score.font = VizUtil.TmpLcd;

            action = ui.Button(right, "action", () => onAction?.Invoke(), 30);
            actionLabel = action.GetComponentInChildren<TMPro.TMP_Text>();
            var row = ui.Row(right, 8);
            step = ui.Button(row, "Next step", () => NextStepRequested?.Invoke(), 28);
            next = ui.Button(row, "Next lesson", () => onNext?.Invoke(), 28);
            nextLabel = next.GetComponentInChildren<TMPro.TMP_Text>();
            var row2 = ui.Row(right, 8);
            skip = ui.Button(row2, "Skip lesson", () => onSkip?.Invoke(), 24);
            end = ui.Button(row2, "End lesson", () => EndRequested?.Invoke(), 24);
            WorldPanel.EnableRayInput(Root);
            a.Changed += () => ui.ApplyScale(a.TextScale);
        }

        static RectTransform Column(Transform parent, float width)
        {
            var go = new GameObject("column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var v = go.GetComponent<VerticalLayoutGroup>(); v.spacing = 8; v.childControlHeight = v.childControlWidth = true; v.childForceExpandHeight = false;
            var le = go.GetComponent<LayoutElement>(); le.preferredWidth = width; le.flexibleWidth = width > 600 ? 1 : 0;
            return (RectTransform)go.transform;
        }

        public void Show(string lesson, string heading, string goalText, Action onNextStage, Action onSkipStage)
        {
            lessonLabel.text = lesson.ToUpperInvariant(); title.text = heading; goal.text = goalText;
            onNext = onNextStage; onSkip = onSkipStage;
        }

        // only touch UI when something changed (text assignment forces a layout rebuild)
        public void SetChecklist(string text) { if (checklist.text != text) checklist.text = text; }

        /// <summary>LCD scoreboard text (e.g. "CORRECT 132/150").</summary>
        public void SetScore(string text) { if (score.text != text) score.text = text; }

        public void SetAction(string label, Action click)
        {
            onAction = click;
            bool show = label != null;
            if (action.gameObject.activeSelf != show) action.gameObject.SetActive(show);
            if (show && actionLabel.text != label) { actionLabel.text = label; action.gameObject.name = "button " + label; }
        }

        public void SetNext(bool enabled, string label = "Next lesson")
        {
            next.interactable = enabled;
            nextLabel.text = label;
            next.gameObject.name = "button Next stage";   // stable name for the automated tour
        }

        public void SetSkipVisible(bool v) => skip.gameObject.SetActive(v);
    }
}
