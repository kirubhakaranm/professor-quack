using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The only screen-space layer: captions (with history, replay, pause, skip), the text question box,
    /// Hint, accessibility settings and the version label. Everything else lives in the lab as world panels.
    /// In VR this canvas becomes a small world panel below the learner's view.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        SpeechQueue speech;
        AccessSettings access;
        readonly UiKit ui = new UiKit();

        Text captionSpeaker, captionText, historyText, pauseLabel, settingsText;
        Image captionBg;
        GameObject historyPanel, settingsBody;

        public Action<string> QuestionAsked;
        public Action HintRequested;
        public Action NextRequested;         // "what should I do now?" / move on when the stage is done
        public Action TalkPressed, TalkReleased;
        public Action PauseToggled;          // whole-game pause (button, P key, VR menu button)
        Text talkLabel;
        GameObject pausePanel;
        public Canvas Canvas { get; private set; }
        public Canvas Overlay { get; private set; }
        public GameObject HintButton { get; private set; }
        public GameObject TalkButton { get; private set; }

        public void Build(SpeechQueue q, AccessSettings a)
        {
            speech = q; access = a;
            ui.Scale = a.TextScale;

            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            BuildCaptions(root);
            // settings live on their own top-most canvas so they also open over the title screen
            var ov = new GameObject("Settings overlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ov.transform.SetParent(transform, false);
            Overlay = ov.GetComponent<Canvas>(); Overlay.renderMode = RenderMode.ScreenSpaceOverlay; Overlay.sortingOrder = 60;
            var osc = ov.GetComponent<CanvasScaler>(); osc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; osc.referenceResolution = new Vector2(1920, 1080); osc.matchWidthOrHeight = 0.5f;
            BuildSettings(root);
            BuildProgress(root);
            var ver = ui.Text(root, $"v{Application.version}", 13, new Color(0.3f, 0.3f, 0.35f), align: TextAnchor.LowerLeft);
            var vrt = (RectTransform)ver.transform;
            vrt.anchorMin = vrt.anchorMax = vrt.pivot = Vector2.zero; vrt.anchoredPosition = new Vector2(8, 2); vrt.sizeDelta = new Vector2(400, 20);

            q.LineStarted += OnLine;
            q.LearnerSaid += t => { captionSpeaker.text = "You said"; captionText.text = t; RefreshHistory(); };
            q.LineEnded += () => { if (q.IsIdle) captionSpeaker.text = "Professor Quack"; };
            a.Changed += ApplyAccess;
            ApplyAccess();
        }

        /// <summary>VR: the caption bar becomes a world panel low in front of the learner.</summary>
        public void MakeWorldSpace(Camera cam, Vector3 position, Quaternion rotation, float metresWide)
        {
            var c = Canvas;
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = cam;
            var scaler = c.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 6;
            var rt = (RectTransform)c.transform;
            rt.SetParent(null, true);
            rt.sizeDelta = new Vector2(1920, 1080);
            rt.localScale = Vector3.one * (metresWide / 1920f);
            rt.SetPositionAndRotation(position, rotation);
            WorldUIHit.AttachAll(c);
            // the settings window floats just in front of the caption panel
            var o = Overlay; o.renderMode = RenderMode.WorldSpace; o.worldCamera = cam;
            var osc = o.GetComponent<CanvasScaler>(); osc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; osc.dynamicPixelsPerUnit = 3;
            var ort = (RectTransform)o.transform; ort.SetParent(null, true); ort.sizeDelta = new Vector2(1920, 1080); ort.localScale = Vector3.one * (metresWide / 1920f);
            ort.SetPositionAndRotation(position + rotation * new Vector3(0, 0.05f, -0.03f), rotation);
            WorldUIHit.AttachAll(o);
        }

        // ---------- captions ----------

        void BuildCaptions(Transform root)
        {
            var p = ui.Panel(root, "Captions", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), 1150);
            captionBg = p.GetComponent<Image>();
            captionSpeaker = ui.Text(p, "Professor Quack", 18, UiKit.Accent, FontStyle.Bold);
            captionText = ui.Text(p, "", 24);
            captionText.font = VizUtil.CaptionFont;   // Atkinson Hyperlegible: captions are the most readable text on screen
            var row = ui.Row(p);
            pauseLabel = ui.Button(row, "Pause game (P)", () => PauseToggled?.Invoke(), 16).GetComponentInChildren<Text>();
            ui.Button(row, "Replay", () => speech.Replay(LastLine()), 16);
            ui.Button(row, "Skip", speech.Skip, 16);
            ui.Button(row, "History", () => historyPanel.SetActive(!historyPanel.activeSelf), 16);
            HintButton = ui.Button(row, "Hint", () => HintRequested?.Invoke(), 16).gameObject;
            if (VoiceInput.Supported)
            {
                var talk = ui.Button(row, "Hold to talk (V)", () => { }, 16);
                TalkButton = talk.gameObject;
                talkLabel = talk.GetComponentInChildren<Text>();
                var trig = talk.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                void Add(UnityEngine.EventSystems.EventTriggerType t, Action a)
                {
                    var e = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = t };
                    e.callback.AddListener(_ => a?.Invoke());
                    trig.triggers.Add(e);
                }
                Add(UnityEngine.EventSystems.EventTriggerType.PointerDown, () => TalkPressed?.Invoke());
                Add(UnityEngine.EventSystems.EventTriggerType.PointerUp, () => TalkReleased?.Invoke());
            }
            ui.Input(row, "Type a question for Professor Quack and press Enter...", q => QuestionAsked?.Invoke(q));

            var hp = ui.Panel(root, "Caption history", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 230), 1150);
            ui.Text(hp, "EXPLANATION HISTORY (most recent last)", 18, UiKit.Accent, FontStyle.Bold);
            historyText = ui.Text(hp, "", 17);
            historyText.font = VizUtil.CaptionFont;
            historyPanel = hp.gameObject;
            historyPanel.SetActive(false);
        }

        TutorLine LastLine() => speech.History.Count == 0 ? null : speech.History[speech.History.Count - 1];

        /// <summary>Show or hide the "Paused" panel (inside this canvas, so it also works as a world panel in VR).</summary>
        public Action LessonsRequested, RestartLessonRequested, QuitToTitleRequested;   // pause menu

        public void SetGamePaused(bool paused)
        {
            if (pausePanel == null)
            {
                // a dim grey veil over the middle three quarters of the screen, with the pause menu on it
                var veil = new GameObject("Paused", typeof(RectTransform), typeof(Image));
                veil.transform.SetParent(Canvas.transform, false);
                var vrt = (RectTransform)veil.transform; vrt.anchorMin = new Vector2(0.125f, 0.125f); vrt.anchorMax = new Vector2(0.875f, 0.875f);
                vrt.offsetMin = vrt.offsetMax = Vector2.zero;
                veil.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 0.72f);
                ui.Royal = true;   // the pause menu is game chrome: royal type, matching the title screen
                var p = ui.Panel(veil.transform, "pause menu", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 520, new Color(0, 0, 0, 0));
                ui.Text(p, "PAUSED", 64, UiKit.Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
                ui.Text(p, "Professor Quack, his voice and the factory are frozen.", 20, UiKit.MutedText, align: TextAnchor.MiddleCenter);
                void Big(string label, Action a)
                {
                    var b = ui.Button(p, label, () => a?.Invoke(), 26);
                    var le = b.GetComponent<LayoutElement>(); le.preferredHeight = 62; le.minHeight = 62;
                }
                Big("Resume", () => PauseToggled?.Invoke());
                Big("Settings", OpenSettings);
                Big("Lessons", () => LessonsRequested?.Invoke());
                Big("Restart this lesson", () => RestartLessonRequested?.Invoke());
                Big("Quit to title", () => QuitToTitleRequested?.Invoke());
                ui.Royal = false;
                if (Canvas.renderMode == RenderMode.WorldSpace) WorldUIHit.AttachAll(Canvas);
                pausePanel = veil;
            }
            pausePanel.SetActive(paused);
            if (paused) pausePanel.transform.SetAsLastSibling();
            pauseLabel.text = paused ? "Resume (P)" : "Pause game (P)";
        }

        GameObject finalePanel;

        /// <summary>End-of-mission card: congratulations, the learner's result, the skills they now have, play again.</summary>
        public void ShowFinale(string title, string result, string[] skills, Action playAgain, Action close)
        {
            if (finalePanel) Destroy(finalePanel);
            var p = ui.Panel(Canvas.transform, "Finale", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), 820, RoyalNavy);
            var ft = p.gameObject.AddComponent<Outline>(); ft.effectColor = RoyalGold; ft.effectDistance = new Vector2(4, -4);
            ui.Royal = true;
            var tt = ui.Text(p, title, 54, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            tt.gameObject.AddComponent<GoldText>();
            var ttO = tt.gameObject.AddComponent<Outline>(); ttO.effectColor = new Color(0.06f, 0.09f, 0.26f); ttO.effectDistance = new Vector2(2, -2);
            ui.Royal = false;
            ui.Text(p, result, 22, UiKit.TextColor, FontStyle.Bold, TextAnchor.MiddleCenter);
            ui.Royal = true; ui.Text(p, "SKILLS YOU NOW HAVE", 20, RoyalGold); ui.Royal = false;
            var sb = new StringBuilder();
            foreach (var sk in skills) sb.AppendLine("[x]  " + sk);
            ui.Text(p, sb.ToString().TrimEnd(), 20);
            var row = ui.Row(p);
            ui.Royal = true;
            ui.Button(row, "Back to title screen", () => playAgain?.Invoke(), 22).GetComponent<Image>().color = new Color(0.93f, 0.66f, 0.12f);
            ui.Button(row, "Keep exploring", () => close?.Invoke(), 22);
            ui.Royal = false;
            if (Canvas.renderMode == RenderMode.WorldSpace) WorldUIHit.AttachAll(Canvas);
            finalePanel = p.gameObject;
        }

        public void HideFinale() { if (finalePanel) finalePanel.SetActive(false); }

        /// <summary>Visible listening state while push-to-talk is held.</summary>
        public void SetListening(bool on)
        {
            if (talkLabel) talkLabel.text = on ? "Listening... release to send" : "Hold to talk (V)";
            if (on) { captionSpeaker.text = "Professor Quack  (listening... release to send)"; captionText.text = "(waiting for your voice...)"; }
        }

        /// <summary>Live microphone level while holding to talk (so the learner sees they are being heard).</summary>
        public void SetMicLevel(float level, string liveTranscript = null)
        {
            int bars = Mathf.Clamp(Mathf.RoundToInt(level * 60f), 0, 20);
            string meter = bars == 0 ? "(waiting for your voice...)" : "Hearing you  " + new string('|', bars);
            captionText.text = string.IsNullOrEmpty(liveTranscript) ? meter : $"You (live): {liveTranscript}\n<size={Mathf.RoundToInt(16 * access.TextScale)}>{meter}</size>";
        }

        /// <summary>Visible "thinking" state while the tutor backend works (no sound needed).</summary>
        public void SetThinking(bool on)
        {
            if (on) captionSpeaker.text = "Professor Quack  (thinking...)";
            else if (speech.Current == null) captionSpeaker.text = "Professor Quack";
        }

        public Func<bool> VoiceResting;   // set by the bootstrap

        void OnLine(TutorLine line)
        {
            captionSpeaker.text = "Professor Quack" + (line.Source == "gemini" ? "  (AI tutor)" : line.Source == "offline" ? "  (offline hint)" : "")
                + (VoiceResting != null && VoiceResting() && line.Clip == null ? "   [voice resting - captions only]" : "");
            captionText.text = EmphasiseNumbers(line.CaptionText);
            RefreshHistory();
        }

        /// <summary>Numbers in captions (counts, percentages, grams, losses) in bold gold, so it's clear what is being quoted.</summary>
        static readonly System.Text.RegularExpressions.Regex Num = new System.Text.RegularExpressions.Regex(@"(?<![\w.#])([+-]?\d+(?:[.,]\d+)*\s?%?)(?![\w])");
        static string EmphasiseNumbers(string s) => string.IsNullOrEmpty(s) ? s : Num.Replace(s, "<b><color=#FFD54A>$1</color></b>");

        void RefreshHistory()
        {
            var sb = new StringBuilder();
            int start = Mathf.Max(0, speech.History.Count - 8);
            for (int i = start; i < speech.History.Count; i++)
            {
                var l = speech.History[i];
                sb.AppendLine((l.Source == "you" ? "You: " : "Quack: ") + l.CaptionText);
            }
            historyText.text = sb.ToString();
        }

        // ---------- progress (top right, game-style) ----------

        Text progStage, progTitle, progPct;
        RectTransform progFill;
        readonly System.Collections.Generic.List<Image> pips = new System.Collections.Generic.List<Image>();
        GameObject skipPanel;
        Text skipLabel;
        Action skipAction;
        static readonly Color PipDone = new Color(0.35f, 0.8f, 0.4f), PipNow = UiKit.Accent, PipTodo = new Color(0.3f, 0.32f, 0.4f);

        // royal, game-style progress crest: navy plate with gold trim, gold lettering, a gold XP-style bar and six medallions
        static readonly Color RoyalNavy = new Color(0.06f, 0.09f, 0.26f, 0.96f), RoyalGold = new Color(1f, 0.79f, 0.26f);
        Text progPctInBar;
        readonly System.Collections.Generic.List<Text> pipLabels = new System.Collections.Generic.List<Text>();
        readonly System.Collections.Generic.List<GameObject> pipTicks = new System.Collections.Generic.List<GameObject>();
        int pulseStage = -1;

        void BuildProgress(Transform root)
        {
            var p = ui.Panel(root, "Progress", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), 430, RoyalNavy);
            var trim = p.gameObject.AddComponent<Outline>(); trim.effectColor = RoyalGold; trim.effectDistance = new Vector2(3, -3);
            ui.Royal = true;
            var top = ui.Row(p);
            progStage = ui.Text(top, "LESSON 1 / 6", 22, RoyalGold);
            progStage.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            progPct = ui.Text(top, "", 18, new Color(0.95f, 0.92f, 0.85f), align: TextAnchor.UpperRight);
            progTitle = ui.Text(p, "", 24, Color.white);
            ui.Royal = false;

            // bar: dark track, gold fill with a lighter highlight strip, percentage inside
            var bar = new GameObject("stage bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(p, false);
            bar.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.1f, 1f);
            var le = bar.GetComponent<LayoutElement>(); le.preferredHeight = 22; le.minHeight = 22;
            var barTrim = bar.AddComponent<Outline>(); barTrim.effectColor = new Color(RoyalGold.r, RoyalGold.g, RoyalGold.b, 0.6f); barTrim.effectDistance = new Vector2(1.5f, -1.5f);
            var fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            fill.GetComponent<Image>().color = new Color(0.93f, 0.66f, 0.12f);
            progFill = (RectTransform)fill.transform;
            progFill.anchorMin = Vector2.zero; progFill.anchorMax = new Vector2(0, 1); progFill.offsetMin = progFill.offsetMax = Vector2.zero;
            var shine = new GameObject("shine", typeof(RectTransform), typeof(Image));
            shine.transform.SetParent(fill.transform, false);
            var srt = (RectTransform)shine.transform; srt.anchorMin = new Vector2(0, 0.55f); srt.anchorMax = new Vector2(1, 0.9f); srt.offsetMin = srt.offsetMax = Vector2.zero;
            shine.GetComponent<Image>().color = new Color(1f, 0.95f, 0.7f, 0.55f);
            progPctInBar = ui.Text(bar.transform, "", 15, new Color(0.08f, 0.06f, 0.02f), FontStyle.Bold, TextAnchor.MiddleCenter);
            var prt = (RectTransform)progPctInBar.transform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;

            // six lesson medallions
            var row = ui.Row(p, 12);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;   // medallions centred under the bar
            for (int i = 0; i < 6; i++)
            {
                var pip = new GameObject("medallion", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                pip.transform.SetParent(row, false);
                var ple = pip.GetComponent<LayoutElement>(); ple.preferredWidth = 44; ple.preferredHeight = 44; ple.minWidth = 44; ple.minHeight = 44; ple.flexibleWidth = 0;
                var img = pip.GetComponent<Image>(); img.sprite = UiIcons.Circle; img.color = PipTodo; pips.Add(img);
                var ring = new GameObject("ring", typeof(RectTransform), typeof(Image));
                ring.transform.SetParent(pip.transform, false);
                var rrt = (RectTransform)ring.transform; rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = new Vector2(-3, -3); rrt.offsetMax = new Vector2(3, 3);
                var ri = ring.GetComponent<Image>(); ri.sprite = UiIcons.Ring; ri.color = RoyalGold; ri.raycastTarget = false;
                ui.Royal = true;
                var t = ui.Text(pip.transform, (i + 1).ToString(), 20, Color.black, align: TextAnchor.MiddleCenter);
                ui.Royal = false;
                var trt = (RectTransform)t.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
                pipLabels.Add(t);
                var tick = new GameObject("tick", typeof(RectTransform), typeof(Image));
                tick.transform.SetParent(pip.transform, false);
                var krt = (RectTransform)tick.transform; krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one; krt.offsetMin = new Vector2(7, 7); krt.offsetMax = new Vector2(-7, -7);
                var ki = tick.GetComponent<Image>(); ki.sprite = UiIcons.Check; ki.color = Color.white; ki.raycastTarget = false;
                tick.SetActive(false);
                pipTicks.Add(tick);
            }
            p.gameObject.SetActive(false);   // shown once the lesson starts (after the intro)
            progressPanel = p.gameObject;
        }
        GameObject progressPanel;

        void LateUpdate()
        {
            // the current medallion breathes gently (game-style "you are here")
            for (int i = 0; i < pips.Count; i++)
            {
                float s = i == pulseStage && !access.ReducedMotion ? 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
                pips[i].transform.localScale = Vector3.one * s;
            }
        }

        /// <summary>Lesson, stage percentage and journey percentage (called every frame by the bootstrap; cheap).</summary>
        public void SetProgress(int stage, int count, string title, float stageFrac, float journeyFrac, bool complete)
        {
            if (stage < 0) { progressPanel.SetActive(false); return; }
            progressPanel.SetActive(true);
            string st = $"LESSON {stage + 1} / {count}";
            if (progStage.text != st) progStage.text = st;
            string pct = $"overall {journeyFrac:P0}";
            if (progPct.text != pct) progPct.text = pct;
            string tt = complete ? $"{title}  - complete!" : title;
            if (progTitle.text != tt) progTitle.text = tt;
            progFill.anchorMax = new Vector2(Mathf.Clamp01(stageFrac), 1);
            string inBar = $"{stageFrac:P0}";
            if (progPctInBar.text != inBar) progPctInBar.text = inBar;
            pulseStage = complete ? -1 : stage;
            for (int i = 0; i < pips.Count; i++)
            {
                pips[i].gameObject.SetActive(i < count);
                bool done = i < stage || (i == stage && complete);
                // finished = green medallion with a white tick; current = bright gold (breathing); later = dim
                pips[i].color = done ? new Color(0.18f, 0.62f, 0.28f) : i == stage ? new Color(1f, 0.85f, 0.35f) : new Color(0.2f, 0.23f, 0.35f);
                pipLabels[i].gameObject.SetActive(!done);
                if (pipTicks[i].activeSelf != done) pipTicks[i].SetActive(done);
                pipLabels[i].color = i == stage ? new Color(0.1f, 0.07f, 0.02f) : new Color(0.65f, 0.65f, 0.7f);
            }
        }

        /// <summary>A single prominent button at the top centre (e.g. "Skip intro"); null hides it.</summary>
        public void ShowSkip(string label, Action onClick)
        {
            if (skipPanel == null)
            {
                var p = ui.Panel(Canvas.transform, "Skip", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), 260, new Color(0, 0, 0, 0));
                skipLabel = ui.Button(p, "Skip", () => skipAction?.Invoke(), 18).GetComponentInChildren<Text>();
                if (Canvas.renderMode == RenderMode.WorldSpace) WorldUIHit.AttachAll(Canvas);
                skipPanel = p.gameObject;
            }
            skipAction = onClick;
            skipPanel.SetActive(onClick != null);
            if (label != null) skipLabel.text = label;
        }

        // ---------- settings: a gear button opens a tabbed settings window ----------

        GameObject settingsWindow;
        Text speedText;
        readonly System.Collections.Generic.List<(Button b, float speed)> speedButtons = new System.Collections.Generic.List<(Button, float)>();
        readonly System.Collections.Generic.List<(Button tab, GameObject page)> tabs = new System.Collections.Generic.List<(Button, GameObject)>();

        void BuildSettings(Transform root)
        {
            // gear button, top-left corner
            var gear = new GameObject("button Settings (gear)", typeof(RectTransform), typeof(Image), typeof(Button));
            gear.transform.SetParent(root, false);
            var grt = (RectTransform)gear.transform; grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0, 1);
            grt.anchoredPosition = new Vector2(18, -18); grt.sizeDelta = new Vector2(58, 58);
            var gimg = gear.GetComponent<Image>(); gimg.sprite = UiIcons.Gear; gimg.color = new Color(0.95f, 0.95f, 0.98f, 0.92f);
            var gb = gear.GetComponent<Button>(); gb.onClick.AddListener(() => ToggleSettings());
            var gc = gb.colors; gc.highlightedColor = UiKit.Accent; gb.colors = gc;

            // modal window
            ui.Royal = true;   // window title + tabs in the royal face; the options themselves stay readable
            var win = ui.Panel(Overlay.transform, "Settings window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 900, new Color(0.06f, 0.07f, 0.1f, 0.98f));
            settingsWindow = win.gameObject;
            var head = ui.Row(win, 10);
            var title = ui.Text(head, "SETTINGS", 34, UiKit.Accent, FontStyle.Bold);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            ui.Button(head, "Close", () => settingsWindow.SetActive(false), 20);
            var tabRow = ui.Row(win, 6);
            ui.Royal = false;
            var pages = new GameObject("pages", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            pages.transform.SetParent(win, false);
            var pl = pages.GetComponent<VerticalLayoutGroup>(); pl.childControlHeight = false; pl.childControlWidth = true; pl.childForceExpandHeight = false;
            pl.childAlignment = TextAnchor.UpperLeft;
            pages.GetComponent<LayoutElement>().minHeight = 260;

            Transform Page(string name)
            {
                var pg = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
                pg.transform.SetParent(pages.transform, false);
                var v = pg.GetComponent<VerticalLayoutGroup>(); v.spacing = 10; v.childControlHeight = v.childControlWidth = true; v.childForceExpandHeight = false;
                v.childAlignment = TextAnchor.UpperLeft;
                pg.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                int idx = tabs.Count;
                ui.Royal = true; var tab = ui.Button(tabRow, name, () => ShowTab(idx), 20); ui.Royal = false;
                tabs.Add((tab, pg));
                return pg.transform;
            }

            var display = Page("Display");
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                ui.Toggle(display, "Fullscreen (F11 toggles)", access.Fullscreen, v => { access.Fullscreen = v; access.Apply(); });
            settingsText = ui.Text(display, "", 20, UiKit.MutedText);
            var r1 = ui.Row(display); ui.Button(r1, "Text smaller", () => { access.TextScale -= 0.1f; access.Apply(); }, 20); ui.Button(r1, "Text larger", () => { access.TextScale += 0.1f; access.Apply(); }, 20);

            var audio = Page("Voice");
            ui.Toggle(audio, "Professor Quack speaks aloud (captions are always on)", access.VoiceOn, v => { access.VoiceOn = v; access.Apply(); });
            speedText = ui.Text(audio, "", 20, UiKit.MutedText);
            var sr = ui.Row(audio, 6);
            foreach (var sp in new[] { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f })
            {
                var spd = sp;
                var b = ui.Button(sr, $"{sp:0.##}x", () => { access.VoiceSpeed = spd; access.Apply(); }, 20);
                speedButtons.Add((b, spd));
            }
            ui.Text(audio, "Sound is never required: every cue is shown as text or shape.", 18, UiKit.MutedText);

            var caps = Page("Captions");
            var r2 = ui.Row(caps); ui.Button(r2, "Slower captions", () => { access.ReadingPace += 0.25f; access.Apply(); }, 20); ui.Button(r2, "Faster captions", () => { access.ReadingPace -= 0.25f; access.Apply(); }, 20);
            ui.Toggle(caps, "Solid caption background", access.CaptionOpacity > 0.95f, v => { access.CaptionOpacity = v ? 1f : 0.85f; access.Apply(); });

            var acc = Page("Accessibility");
            ui.Toggle(acc, "Reduced motion (no camera glides, bobbing or confetti)", access.ReducedMotion, v => { access.ReducedMotion = v; access.Apply(); });
            ui.Toggle(acc, "Less chatter (fewer jokes and reactions)", access.LessChatter, v => { access.LessChatter = v; access.Apply(); });

            var ctl = Page("Controls");
            ui.Text(ctl, "Mouse: click an egg to inspect it; drag the teal ring to turn the rule, the purple ring to tilt it, the teal disc to slide it.\n" +
                         "W A S D: walk around the room (Shift = faster).  Right-drag or arrow keys: look around.  Scroll: zoom.  Keys 0-8: views (6 whiteboard, 7 TV, 8 rule view).\n" +
                         "Hold V: talk to Professor Quack.  P or Esc: pause.  F11: fullscreen.\n" +
                         "VR: trigger points and drags; point at a floor pad + trigger to teleport; right stick snap-turns; hold left grip to talk; left menu button pauses.", 19);

            var vr = Page("VR");
            ui.Toggle(vr, "Smooth stick walking with a comfort vignette (off = teleport pads only)", access.SmoothWalk, v => { access.SmoothWalk = v; access.Apply(); });

            ShowTab(0);
            settingsWindow.SetActive(false);
            settingsBody = settingsWindow;
        }

        void ShowTab(int i)
        {
            for (int k = 0; k < tabs.Count; k++)
            {
                tabs[k].page.SetActive(k == i);
                tabs[k].tab.GetComponent<Image>().color = k == i ? new Color(0.35f, 0.4f, 0.55f) : new Color(0.2f, 0.22f, 0.3f);
            }
        }

        public void ToggleSettings() { settingsWindow.SetActive(!settingsWindow.activeSelf); settingsWindow.transform.SetAsLastSibling(); }

        /// <summary>Open the settings window (title screen / pause menu).</summary>
        public void OpenSettings() { settingsWindow.SetActive(true); settingsWindow.transform.SetAsLastSibling(); }

        void ApplyAccess()
        {
            ui.ApplyScale(access.TextScale);
            var c = captionBg.color; c.a = access.CaptionOpacity; captionBg.color = c;
            settingsText.text = $"Text size {access.TextScale:0.0}x   Caption pace {access.ReadingPace:0.00}x";
            if (speedText) speedText.text = $"Voice and caption speed: {access.VoiceSpeed:0.##}x";
            foreach (var (b, sp) in speedButtons)
                if (b) b.GetComponent<Image>().color = Mathf.Abs(sp - access.VoiceSpeed) < 0.01f ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.25f, 0.28f, 0.36f);
        }
    }
}
