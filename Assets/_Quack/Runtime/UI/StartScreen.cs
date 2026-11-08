using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Title screen: a full-screen game menu over a slow, drifting shot of the factory (Professor Quack waving, eggs
    /// floating). Play / Lessons / Settings / Quit. "Lessons" opens a roadmap: a winding yellow path with six lesson
    /// milestones (done = tick, next = glowing); hovering one shows what it teaches with a Start button.
    /// The roadmap is also used from the pause menu. In VR the canvas becomes a large floating screen.
    /// </summary>
    public sealed class StartScreen : MonoBehaviour
    {
        public DesktopInput Desktop;
        public QuacksAnimator Quack;
        public AccessSettings Access;
        public Hud Hud;
        public QuackPalette Palette;
        public Action OnStart;                 // full intro, then lesson 1
        public Action<string> OnJump;          // straight to a lesson id
        public bool IsOpen => open;

        readonly UiKit ui = new UiKit();
        Canvas canvas;
        /// <summary>The title canvas (VR: made world-space and kept in front of the learner).</summary>
        public Transform CanvasRoot => canvas.transform;
        RectTransform menu, map, card, logo;
        Text cardTitle, cardBody;
        Button cardStart;
        string cardLessonId;
        bool open, fromPause;
        Coroutine drift;
        readonly List<Transform> eggs = new List<Transform>();
        IReadOnlyList<(string id, string title, string goal)> lessons;

        public static readonly string[] Teaches =
        {
            "Turn eggs into numbers: every egg becomes a feature vector, a point in space.",
            "Draw one rule that sorts every egg: a line, then a plane (score = w.x + b).",
            "Probabilities, confidence, and why a smooth loss beats counting mistakes.",
            "Every rule is a point on a loss landscape: a 2D slice of parameter space.",
            "Gradient descent: the machine walks downhill. Learning rates that crawl, fit or overshoot.",
            "Test on eggs it has never seen, then sort a 10,000-egg shipment.",
        };

        public static bool Done(int i) => PlayerPrefs.GetInt("quack.done." + i, 0) == 1;

        public void Build(IReadOnlyList<(string id, string title, string goal)> lessonList)
        {
            lessons = lessonList;
            ui.Scale = Access.TextScale;
            var go = new GameObject("Title screen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)go.transform;
            var vig = Fill(root, "vignette"); vig.GetComponent<Image>().sprite = UiIcons.Vignette; vig.GetComponent<Image>().color = new Color(1, 1, 1, 0.85f);

            // ---- main menu ----
            menu = Fill(root, "menu", clear: true);
            logo = new GameObject("logo", typeof(RectTransform)).GetComponent<RectTransform>();
            logo.SetParent(menu, false); logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.76f); logo.sizeDelta = new Vector2(1500, 300);
            // royal crest in Professor Quack's colours: navy ribbon with gold trim, gold lettering, burgundy shadow
            var ribbon = new GameObject("ribbon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            ribbon.SetParent(logo, false); ribbon.anchoredPosition = new Vector2(0, 20); ribbon.sizeDelta = new Vector2(1540, 270);
            ribbon.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.26f, 1f);
            var trim = ribbon.gameObject.AddComponent<Outline>(); trim.effectColor = Gold; trim.effectDistance = new Vector2(5, -5);
            foreach (float y in new[] { 118f, -78f })   // thin gold rules above and below the name
            {
                var rule = new GameObject("gold rule", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rule.SetParent(ribbon, false); rule.anchoredPosition = new Vector2(0, y - 20); rule.sizeDelta = new Vector2(1380, 5);
                rule.GetComponent<Image>().color = Gold;
            }
            var l1 = Title(logo, "PROFESSOR QUACK'S", 50, Gold, new Vector2(0, 90), royal: true);
            var l2 = Title(logo, "EGG & AI EMPORIUM", 108, Gold, new Vector2(0, -8), royal: true);
            var tag = Title(logo, "Learn how machines learn, one egg at a time.", 30, new Color(0.97f, 0.94f, 0.86f), new Vector2(0, -150));
            tag.fontStyle = FontStyle.Italic;

            var col = ui.Panel(menu, "buttons", new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.5f), Vector2.zero, 460, new Color(0, 0, 0, 0));
            Big(col, "Play", () => Close(() => OnStart?.Invoke()), true);
            Big(col, "Lessons", ShowMap);
            Big(col, "Settings", () => Hud?.OpenSettings());   // the settings window has its own top-most canvas
            if (Application.platform != RuntimePlatform.WebGLPlayer) Big(col, "Quit", Application.Quit);
            var foot = ui.Text(menu, "Captions are always on. Sound is optional.      v" + Application.version, 18, new Color(0.85f, 0.85f, 0.85f, 0.8f), align: TextAnchor.LowerCenter);
            var frt = (RectTransform)foot.transform; frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 0); frt.pivot = new Vector2(0.5f, 0); frt.anchoredPosition = new Vector2(0, 16); frt.sizeDelta = new Vector2(0, 30);

            // ---- lesson roadmap: an antique map on parchment ----
            map = Fill(root, "roadmap", clear: false);
            map.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 0.7f);
            var sheet = new GameObject("parchment", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            sheet.SetParent(map, false); sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.55f); sheet.sizeDelta = new Vector2(1640, 780);
            sheet.GetComponent<Image>().sprite = UiIcons.Parchment;
            var mt = Title(sheet, "ROAD TO SUCCESS", 70, Color.white, new Vector2(0, 318), royal: true);
            var compass = new GameObject("compass rose", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            compass.SetParent(sheet, false); compass.anchoredPosition = new Vector2(690, -270); compass.sizeDelta = new Vector2(150, 150);   // bottom-right, clear of the lessons
            var ci = compass.GetComponent<Image>(); ci.sprite = UiIcons.Compass; ci.raycastTarget = false;
            var cn = ui.Text(compass, "N", 26, new Color(0.35f, 0.22f, 0.1f), FontStyle.Bold, TextAnchor.MiddleCenter);
            var cnr = (RectTransform)cn.transform; cnr.anchoredPosition = new Vector2(0, 92); cnr.sizeDelta = new Vector2(40, 40);
            var area = new GameObject("path area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(sheet, false); area.anchoredPosition = new Vector2(0, -20); area.sizeDelta = new Vector2(1500, 560);
            // a gently winding road through the six lessons (smooth curve through the milestones only)
            var nodes = new[] { new Vector2(-620, -170), new Vector2(-380, 40), new Vector2(-120, -90), new Vector2(130, 110), new Vector2(390, -40), new Vector2(620, 160) };
            // smooth S-curves: cubic Bezier between neighbouring milestones with horizontal tangents (a winding road)
            var pts = new List<Vector2>();
            var stops = new List<Vector2> { nodes[0] + new Vector2(-130, -30) }; stops.AddRange(nodes); stops.Add(nodes[nodes.Length - 1] + new Vector2(120, 20));
            for (int k = 0; k < stops.Count - 1; k++)
            {
                Vector2 a = stops[k], d = stops[k + 1]; float hx = (d.x - a.x) * 0.5f;
                Vector2 b = a + new Vector2(hx, 0), c = d - new Vector2(hx, 0);
                for (int sIdx = 0; sIdx < 20; sIdx++)
                {
                    float t = sIdx / 20f, u = 1 - t;
                    pts.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
                }
            }
            pts.Add(stops[stops.Count - 1]);
            var trail = new GameObject("dotted trail", typeof(RectTransform), typeof(UiPath)).GetComponent<UiPath>();
            trail.transform.SetParent(area, false); trail.Thickness = 9; trail.Dash = 18; trail.Gap = 14; trail.Samples = 2; trail.raycastTarget = false;
            trail.color = new Color(0.42f, 0.24f, 0.1f, 0.9f);   // sepia ink
            trail.SetPoints(pts);
            int next = 0; while (next < lessons.Count && Done(next)) next++;
            for (int i = 0; i < nodes.Length && i < lessons.Count; i++) Milestone(area, nodes[i], i, next);

            card = ui.Panel(map, "lesson card", new Vector2(0.5f, 0.11f), new Vector2(0.5f, 0.5f), Vector2.zero, 900, new Color(0.06f, 0.09f, 0.26f, 0.97f));
            var cardTrim = card.gameObject.AddComponent<Outline>(); cardTrim.effectColor = Gold; cardTrim.effectDistance = new Vector2(3, -3);
            cardTitle = ui.Text(card, "", 34, MissionBoard.Led, FontStyle.Bold, TextAnchor.MiddleCenter);
            cardBody = ui.Text(card, "Point at a lesson on the map to see what it teaches.", 24, new Color(0.95f, 0.93f, 0.88f), align: TextAnchor.MiddleCenter);
            var cr = ui.Row(card, 12);
            cardStart = ui.Button(cr, "Start this lesson", () => { var id = cardLessonId; if (id != null) Close(() => OnJump?.Invoke(id)); }, 24);
            ui.Button(cr, "Back", HideMap, 24);
            cardStart.gameObject.SetActive(false);
            map.gameObject.SetActive(false);
            canvas.gameObject.SetActive(false);
        }

        RectTransform Fill(Transform parent, string name, bool clear = false)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            g.GetComponent<Image>().color = clear ? new Color(0, 0, 0, 0) : Color.white;
            if (clear) g.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        // Professor Quack's palette: gold piping, navy blazer, burgundy bow tie
        static readonly Color Gold = new Color(1f, 0.79f, 0.26f), Navy = new Color(0.06f, 0.09f, 0.26f), Burgundy = new Color(0.45f, 0.05f, 0.12f);
        static Font royalFont;
        /// <summary>Cinzel Decorative (SIL Open Font License, Resources/Fonts) for the royal title; built-in font if missing.</summary>
        static Font RoyalFont => royalFont ??= (Resources.Load<Font>("Fonts/CinzelDecorative-Bold") ?? VizUtil.Font);

        Text Title(Transform parent, string s, int size, Color c, Vector2 pos, bool royal = false)
        {
            var t = ui.Text(parent, s, size, c, royal ? FontStyle.Normal : FontStyle.Bold, TextAnchor.MiddleCenter);
            if (royal)
            {
                t.font = RoyalFont;
                t.color = Color.white;
                var gold = t.gameObject.AddComponent<GoldText>();          // shiny metallic gold (before outline/shadow)
                gold.Glint = !Access.ReducedMotion;
            }
            var rt = (RectTransform)t.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(1600, size * 1.6f); rt.anchoredPosition = pos;
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = royal ? Navy : new Color(0.25f, 0.12f, 0.02f, 0.9f); o.effectDistance = new Vector2(3, -3);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = royal ? new Color(Burgundy.r, Burgundy.g, Burgundy.b, 0.9f) : new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(5, -7);
            return t;
        }

        void Big(Transform parent, string label, Action click, bool primary = false)
        {
            var b = ui.Button(parent, label, click, primary ? 40 : 30);
            b.gameObject.name = "button " + label;
            var le = b.GetComponent<LayoutElement>(); le.preferredHeight = primary ? 88 : 68; le.minHeight = le.preferredHeight;
            b.GetComponent<Image>().color = primary ? Gold : Navy;
            var trim = b.gameObject.AddComponent<Outline>(); trim.effectColor = primary ? Navy : Gold; trim.effectDistance = new Vector2(3, -3);
            var txt = b.GetComponentInChildren<Text>(); txt.font = RoyalFont; txt.fontStyle = FontStyle.Normal;
            txt.color = primary ? Navy : Gold;
            if (primary) StartCoroutine(Pulse(b.transform));
        }

        IEnumerator Pulse(Transform t)
        {
            while (t != null)
            {
                float s = Access.ReducedMotion ? 1f : 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 3f);
                t.localScale = Vector3.one * s;
                yield return null;
            }
        }

        void Milestone(Transform area, Vector2 pos, int i, int next)
        {
            bool done = Done(i), current = i == next, locked = i > next;
            var g = new GameObject($"milestone {i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            g.transform.SetParent(area, false);
            var rt = (RectTransform)g.transform; rt.anchoredPosition = pos; rt.sizeDelta = current ? new Vector2(116, 116) : new Vector2(92, 92);
            var img = g.GetComponent<Image>(); img.sprite = UiIcons.Circle;
            img.color = current ? new Color(1f, 0.8f, 0.25f) : done ? new Color(0.72f, 0.48f, 0.2f) : new Color(0.62f, 0.55f, 0.45f, 0.75f);
            var ringGo = new GameObject("ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(g.transform, false);
            var rrt = (RectTransform)ringGo.transform; rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = new Vector2(-8, -8); rrt.offsetMax = new Vector2(8, 8);
            var ri = ringGo.GetComponent<Image>(); ri.sprite = UiIcons.Ring; ri.raycastTarget = false;
            ri.color = locked ? new Color(0.45f, 0.36f, 0.25f, 0.6f) : new Color(0.35f, 0.2f, 0.06f);
            var num = Title(g.transform, (i + 1).ToString(), current ? 52 : 42, locked ? new Color(0.5f, 0.42f, 0.32f) : Color.white, Vector2.zero, royal: true);
            var nrt0 = (RectTransform)num.transform; nrt0.sizeDelta = rt.sizeDelta;
            if (locked) { var gt = num.GetComponent<GoldText>(); if (gt) gt.enabled = false; num.color = new Color(0.45f, 0.38f, 0.3f); }
            var name = ui.Text(area, lessons[i].title, 24, locked ? new Color(0.45f, 0.36f, 0.26f) : new Color(0.28f, 0.16f, 0.06f), FontStyle.Bold, TextAnchor.MiddleCenter);
            var nrt = (RectTransform)name.transform; nrt.anchoredPosition = pos + new Vector2(0, pos.y > 0 ? 88 : -88); nrt.sizeDelta = new Vector2(320, 40);
            if (current) StartCoroutine(Pulse(g.transform));
            void Show()
            {
                cardLessonId = locked ? null : lessons[i].id;
                cardTitle.text = $"Lesson {i + 1}: {lessons[i].title}" + (locked ? "   (Locked)" : "");
                cardBody.text = locked ? $"Locked: finish Lesson {next + 1} first." : (i < Teaches.Length ? Teaches[i] : lessons[i].goal);
                cardStart.gameObject.SetActive(!locked);
                cardStart.GetComponentInChildren<Text>().text = done ? "Replay this lesson" : "Start this lesson";
            }
            g.GetComponent<Button>().onClick.AddListener(Show);
            var trig = g.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; enter.callback.AddListener(_ => Show()); trig.triggers.Add(enter);
        }

        public void ShowMap() { menu.gameObject.SetActive(false); map.gameObject.SetActive(true); }
        public void HideMap()
        {
            map.gameObject.SetActive(false);
            if (fromPause) { canvas.gameObject.SetActive(false); open = false; fromPause = false; return; }
            menu.gameObject.SetActive(true);
        }

        /// <summary>Pause menu → Lessons: the roadmap alone, over the paused game.</summary>
        public void ShowRoadmapFromPause()
        {
            fromPause = true; open = true;
            canvas.gameObject.SetActive(true); menu.gameObject.SetActive(false); map.gameObject.SetActive(true);
        }

        public void Show()
        {
            open = true; fromPause = false;
            canvas.gameObject.SetActive(true);
            menu.gameObject.SetActive(true); map.gameObject.SetActive(false);
            if (Hud) Hud.Canvas.enabled = false;
            if (Desktop) Desktop.PauseOnEscape = false;
            SpawnEggs();
            drift = StartCoroutine(Establishing());
        }

        public void Dismiss() => Close(null);

        void Close(Action then)
        {
            if (!open) return;
            open = false;
            if (drift != null) StopCoroutine(drift);
            canvas.gameObject.SetActive(false);
            foreach (var e in eggs) if (e) Destroy(e.gameObject);
            eggs.Clear();
            if (Hud) Hud.Canvas.enabled = true;
            if (Desktop) Desktop.PauseOnEscape = true;
            bool wasPause = fromPause; fromPause = false;
            then?.Invoke();
        }

        /// <summary>VR: show the title as a big floating screen in front of the learner.</summary>
        public void MakeWorldSpace(Camera cam, Vector3 position, Quaternion rotation, float metresWide)
        {
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam;
            var sc = canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; sc.dynamicPixelsPerUnit = 3;
            var rt = (RectTransform)canvas.transform; rt.sizeDelta = new Vector2(1920, 1080); rt.localScale = Vector3.one * (metresWide / 1920f);
            rt.SetPositionAndRotation(position, rotation);
            WorldUIHit.AttachAll(canvas);
        }

        void SpawnEggs()
        {
            if (Palette == null) return;
            var mats = new[] { Palette.Cream, Palette.JumboEgg, Palette.StandardEgg };
            for (int i = 0; i < 7; i++)
            {
                var e = VizUtil.Primitive(PrimitiveType.Sphere, "title egg", null, mats[i % 3]).transform;
                e.localScale = new Vector3(0.11f, 0.14f, 0.11f);
                e.position = new Vector3(-1.4f + i * 0.47f, 1.2f + (i % 3) * 0.25f, 0.6f + (i % 2) * 0.5f);
                eggs.Add(e);
            }
        }

        /// <summary>Wide shot of the room that drifts gently; eggs bob and spin; Professor Quack waves now and then.</summary>
        IEnumerator Establishing()
        {
            float t = 0, nextWave = 1.5f;
            while (open)
            {
                if (Desktop != null && Desktop.isActiveAndEnabled)
                {
                    float sway = Access.ReducedMotion ? 0 : Mathf.Sin(t * 0.12f) * 9f;
                    Desktop.LookAt(new Vector3(0, 1.45f, 0.4f), sway, 6f, 3.4f, 300);
                }
                if (!Access.ReducedMotion)
                {
                    for (int i = 0; i < eggs.Count; i++)
                        if (eggs[i]) { eggs[i].position += Vector3.up * Mathf.Sin(t * 1.3f + i) * 0.0015f; eggs[i].Rotate(0, 25 * Time.deltaTime, 12 * Time.deltaTime); }
                    if (logo) logo.localScale = Vector3.one * (1f + 0.012f * Mathf.Sin(t * 1.6f));
                }
                if (Quack != null && t >= nextWave) { Quack.Play(Gesture.FlapSmall); nextWave = t + 9f; }
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
