using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Highlights whatever Professor Quack is talking about: registered objects flash in a strongly contrasting
    /// colour (lime) for a couple of seconds, synced to when his line starts. Triggered by the tutor reply's
    /// `highlight` field and by keywords in any line. Reduced motion: a steady tint instead of a pulse.
    /// </summary>
    public sealed class HighlightManager : MonoBehaviour
    {
        public static readonly Color HighlightColor = new Color(0.25f, 1f, 0.15f);
        public AccessSettings Access;

        readonly Dictionary<string, Func<IEnumerable<GameObject>>> targets = new Dictionary<string, Func<IEnumerable<GameObject>>>();
        readonly Dictionary<string, Coroutine> running = new Dictionary<string, Coroutine>();

        // keyword → target (checked in order; first matches win, several targets may fire)
        static readonly (string[] words, string target)[] Keywords =
        {
            (new[] { "sheet", "plane", "boundary", "decision", "the line", "one rule", "sorting rule", "the rule", "your rule", "this rule" }, "plane"),
            (new[] { "correct", "misrouted", "accuracy", "percent", "score", "scoreboard" }, "score"),
            (new[] { "ring", "turn it", "tilt it" }, "gimbal"),
            (new[] { "equation", "score =", "slope", "intercept" }, "equation"),
            (new[] { "arrow", "weight vector", "weights" }, "arrow"),
            (new[] { "axis", "axes" }, "axes"),
            (new[] { "jumbo carton", "jumbo tray", "big carton" }, "carton_jumbo"),
            (new[] { "standard carton", "standard tray", "small carton" }, "carton_standard"),
            (new[] { "misrouted", "marked with a cross", "mistakes", "wrong carton" }, "misrouted"),
            (new[] { "band", "p = 0.25", "0.75" }, "bands"),
            (new[] { "landscape", "valley", "slice" }, "landscape"),
            (new[] { "learning rate", "training desk", "step button", "press play" }, "training"),
            (new[] { "console", "slider", "control desk", "dial", "dials", "lever", "knob" }, "console"),
            (new[] { "whiteboard" }, "whiteboard"),
            (new[] { "conveyor", "belt", "sorting line" }, "conveyor"),
            (new[] { "tv", "television", "factory floor cam" }, "tv"),
            (new[] { "mission screen", "mission board", "checklist" }, "mission"),
            // buttons he asks the learner to press: the exact button pulses
            (new[] { "measure width", "evaluate on held-out", "run the next shipment" }, "btn_action"),
            (new[] { "next lesson" }, "btn_next"),
            (new[] { "next step" }, "btn_step"),
            (new[] { "skip lesson" }, "btn_skip"),
            (new[] { "end lesson" }, "btn_end"),
            (new[] { "hint" }, "btn_hint"),
            (new[] { "hold to talk", "holding v", "hold v" }, "btn_talk"),
        };

        /// <summary>Raised with the main thing each line talks about (StationDirector walks Quack there).</summary>
        public event Action<string> Highlighted;

        /// <summary>Keyword match on word boundaries ("tv" must not fire inside other words, "board" not inside "mission board"...).</summary>
        static bool WholeWord(string text, string word)
        {
            for (int i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.Ordinal))
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int j = i + word.Length;
                bool after = j >= text.Length || !char.IsLetterOrDigit(text[j]);
                if (before && after) return true;
            }
            return false;
        }

        public void Register(string key, Func<IEnumerable<GameObject>> objects) => targets[key] = objects;
        public void Register(string key, params GameObject[] objects) => targets[key] = () => objects;

        /// <summary>Highlight explicit target (from the tutor) plus anything named in the text.</summary>
        public void FromLine(TutorLine line)
        {
            var hit = new HashSet<string>();
            if (!string.IsNullOrEmpty(line.Highlight) && targets.ContainsKey(line.Highlight)) hit.Add(line.Highlight);
            string s = (line.Speech ?? "").ToLowerInvariant();
            foreach (var (words, t) in Keywords)
                foreach (var w in words)
                    if (WholeWord(s, w)) { hit.Add(t); break; }
            int n = 0;
            foreach (var t in hit) { if (n++ >= 3) break; Pulse(t, t.StartsWith("btn_") ? 4f : 2.6f); }   // at most three things at once (avoid a light show)
            // the first thing he talks about decides where he walks (explicit tutor highlight wins)
            string lead = !string.IsNullOrEmpty(line.Highlight) && targets.ContainsKey(line.Highlight) ? line.Highlight : null;
            if (lead == null) foreach (var t in hit) { lead = t; break; }
            if (lead != null) Highlighted?.Invoke(lead);
        }

        public void Pulse(string key, float seconds = 2.6f)
        {
            if (!targets.TryGetValue(key, out var get)) return;
            if (running.TryGetValue(key, out var c) && c != null) StopCoroutine(c);
            running[key] = StartCoroutine(Run(key, get(), seconds));
        }

        IEnumerator Run(string key, IEnumerable<GameObject> objs, float seconds)
        {
            var rends = new List<Renderer>(); var lines = new List<(LineRenderer lr, Color a, Color b)>();
            var texts = new List<(TMPro.TextMeshPro tm, Color c)>(); var images = new List<(Graphic g, Color c)>();
            var buttons = new List<(Transform t, Vector3 s)>();
            foreach (var go in objs)
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r is LineRenderer lr) lines.Add((lr, lr.startColor, lr.endColor));
                    else if (r.GetComponent<TMPro.TextMeshPro>() is TMPro.TextMeshPro tm) texts.Add((tm, tm.color));
                    else rends.Add(r);
                }
                foreach (var g in go.GetComponentsInChildren<Image>()) images.Add((g, g.color));
                if (go.GetComponent<Button>() != null) buttons.Add((go.transform, go.transform.localScale));
            }
            var mpb = new MaterialPropertyBlock();
            bool still = Access != null && Access.ReducedMotion;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = still ? 0.7f : 0.5f + 0.5f * Mathf.Sin(t * 9f);
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    var baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
                    var c = Color.Lerp(baseCol, HighlightColor, k); c.a = Mathf.Max(baseCol.a, 0.55f);
                    mpb.SetColor("_BaseColor", c); r.SetPropertyBlock(mpb);
                }
                foreach (var (lr, a, b) in lines) if (lr) { lr.startColor = Color.Lerp(a, HighlightColor, k); lr.endColor = Color.Lerp(b, HighlightColor, k); }
                foreach (var (tm, c0) in texts) if (tm) tm.color = Color.Lerp(c0, HighlightColor * 0.8f, k);
                foreach (var (g, c0) in images) if (g) g.color = Color.Lerp(c0, buttons.Count > 0 ? new Color(1f, 0.78f, 0.2f, c0.a) : new Color(0.2f, 0.6f, 0.1f, c0.a), k * 0.75f);
                foreach (var (bt, s0) in buttons) if (bt) bt.localScale = s0 * (1f + (still ? 0.06f : 0.1f * k));
                yield return null;
            }
            foreach (var r in rends) if (r) r.SetPropertyBlock(null);
            foreach (var (lr, a, b) in lines) if (lr) { lr.startColor = a; lr.endColor = b; }
            foreach (var (tm, c0) in texts) if (tm) tm.color = c0;
            foreach (var (g, c0) in images) if (g) g.color = c0;
            foreach (var (bt, s0) in buttons) if (bt) bt.localScale = s0;
            running.Remove(key);
        }
    }
}
