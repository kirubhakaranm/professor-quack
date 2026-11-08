using System;
using System.Collections;
using System.Globalization;
using System.Text;
using Quack.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Quack
{
    /// <summary>Backend reply as JSON (field names match backend/schema.mjs).</summary>
    [Serializable]
    public sealed class TutorReply
    {
        public string speech, emotion, gesture, after_speech, gaze_target, duck_behavior, highlight, source, model, heard;
        public float intensity = 0.5f;
        public Whiteboard whiteboard;
    }

    /// <summary>
    /// Sends structured game context to the tutor backend (never to Gemini directly; the key stays server-side)
    /// and turns the validated reply into a TutorLine. On any failure the caller falls back to local hints.
    /// Uses UnityWebRequest so the same code works in standalone and WebGL builds.
    /// </summary>
    public sealed class TutorClient : MonoBehaviour
    {
        public string BaseUrl = "http://127.0.0.1:8787";
        public float TimeoutSeconds = 20f;
        public GameState State;
        public SpeechQueue Speech;                 // for recent dialogue (memory across requests)
        public System.Func<string[]> Introduced;   // what the learner has been shown so far (from the lesson)
        public System.Func<string> StageGoal;
        public System.Func<string[]> RemainingSteps;
        public bool Busy { get; private set; }
        public bool MockRequests;

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-tutorUrl");
            if (i >= 0 && i + 1 < args.Length) BaseUrl = args[i + 1].TrimEnd('/');
            MockRequests = Array.IndexOf(args, "-tutorMock") >= 0;
#if UNITY_WEBGL && !UNITY_EDITOR
            // in the browser the backend serves the game, so the tutor lives at the page's own origin
            var url = Application.absoluteURL;
            if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
            {
                var u = new Uri(url);
                BaseUrl = u.GetLeftPart(UriPartial.Authority);
            }
            if (url != null && url.Contains("tutorMock")) MockRequests = true;
            // static hosting (GitHub Pages): the tutor service lives elsewhere, e.g. ?tutor=https://tutor.example.com
            var m = url != null ? System.Text.RegularExpressions.Regex.Match(url, @"[?&]tutor=([^&#]+)") : null;
            if (m != null && m.Success) BaseUrl = Uri.UnescapeDataString(m.Groups[1].Value).TrimEnd('/');
#endif
        }

        System.Collections.IEnumerator Start()
        {
            // reachability check, visible in the backend log and Player log
            using var ping = UnityWebRequest.Get(BaseUrl + "/health?from=" + Application.platform);
            ping.timeout = 5;
            yield return ping.SendWebRequest();
            Debug.Log($"QUACK_TUTOR health {BaseUrl}: {(ping.result == UnityWebRequest.Result.Success ? ping.downloadHandler.text : ping.error)}");
        }

        /// <summary>trigger: question | hint | recap | reaction.</summary>
        public void Ask(string trigger, string question, Action<TutorLine> onLine, Action<string> onFail)
        {
            if (Busy) { onFail?.Invoke("busy"); return; }
            StartCoroutine(Send(trigger, question ?? "", null, onLine, onFail));
        }

        /// <summary>Spoken question: WAV audio goes to Gemini with the game context; reply carries the transcript.</summary>
        public void AskVoice(byte[] wav, Action<TutorLine> onLine, Action<string> onFail)
        {
            if (Busy) { onFail?.Invoke("busy"); return; }
            StartCoroutine(Send("voice", "", Convert.ToBase64String(wav), onLine, onFail));
        }

        public string LastHeard { get; private set; }

        [Serializable] sealed class TranscribeReq { public string audio; }
        [Serializable] sealed class TranscribeReply { public string text; }

        /// <summary>Fast transcript of audio so far (live captions while the learner is still speaking).</summary>
        public IEnumerator Transcribe(byte[] wav, Action<string> onText)
        {
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TranscribeReq { audio = Convert.ToBase64String(wav) }));
            using var req = new UnityWebRequest(BaseUrl + "/transcribe", "POST")
            {
                uploadHandler = new UploadHandlerRaw(body), downloadHandler = new DownloadHandlerBuffer(), timeout = 15,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            if (MockRequests) req.SetRequestHeader("X-Quack-Mock", "1");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;
            TranscribeReply r = null;
            try { r = JsonUtility.FromJson<TranscribeReply>(req.downloadHandler.text); } catch { }
            if (r != null && !string.IsNullOrWhiteSpace(r.text)) onText?.Invoke(r.text);
        }

        IEnumerator Send(string trigger, string question, string audioB64, Action<TutorLine> onLine, Action<string> onFail)
        {
            Busy = true;
            string body = BuildRequestJson(trigger, question, audioB64);
            using var req = new UnityWebRequest(BaseUrl + "/tutor", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.CeilToInt(TimeoutSeconds),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            if (MockRequests) req.SetRequestHeader("X-Quack-Mock", "1");   // automated tests don't spend Gemini quota
            float t0 = Time.realtimeSinceStartup;
            yield return req.SendWebRequest();
            Busy = false;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"QUACK_TUTOR fail {req.responseCode} {req.error}");
                onFail?.Invoke(req.error);
                yield break;
            }
            TutorReply r;
            try { r = JsonUtility.FromJson<TutorReply>(req.downloadHandler.text); }
            catch (Exception e) { onFail?.Invoke("bad json: " + e.Message); yield break; }
            if (r == null || string.IsNullOrWhiteSpace(r.speech)) { onFail?.Invoke("empty reply"); yield break; }

            Debug.Log($"QUACK_TUTOR ok {trigger} {(Time.realtimeSinceStartup - t0):0.0}s source={r.source}");
            LastHeard = r.heard;
            onLine?.Invoke(ToLine(r, trigger == "question" || trigger == "voice" ? TutorPriority.PlayerQuestion : TutorPriority.Teaching));
        }

        public static TutorLine ToLine(TutorReply r, TutorPriority priority)
        {
            var line = new TutorLine(r.speech, TutorLine.ParseEmotion(r.emotion), TutorLine.ParseGesture(r.gesture), priority)
            {
                AfterSpeech = TutorLine.ParseGesture(r.after_speech),
                Gaze = TutorLine.ParseGaze(r.gaze_target),
                Intensity = Mathf.Clamp01(r.intensity),
                Duck = TutorLine.ParseDuck(r.duck_behavior),
                Highlight = r.highlight == "none" ? null : r.highlight,
                Source = string.IsNullOrEmpty(r.source) ? "gemini" : r.source,
            };
            if (r.whiteboard != null && (!string.IsNullOrEmpty(r.whiteboard.title) || (r.whiteboard.lines?.Length ?? 0) > 0))
                line.Board = r.whiteboard;
            return line;
        }

        // ---------- context snapshot ----------

        /// <summary>Hand-written JSON so absent things are omitted rather than defaulted (JsonUtility can't do null objects).</summary>
        public string BuildRequestJson(string trigger, string question, string audioB64 = null)
        {
            var s = State;
            var j = new Json();
            j.Open();
            j.Str("trigger", trigger);
            if (!string.IsNullOrEmpty(question)) j.Str("question", question);
            if (!string.IsNullOrEmpty(audioB64)) j.Str("audio", audioB64);
            j.Key("context"); j.Open();
            j.Str("stage", s.Stage);
            j.Str("features", s.WidthLocked
                ? "mass_g and length_mm only: width has NOT been measured yet (do not mention any egg's width)"
                : "mass_g, length_mm, width_mm; model uses standardised values");

            var p = s.Params;
            j.Key("params"); j.Open();
            for (int k = 0; k < p.Dim; k++) j.Num("w_" + EggDataset.FeatureNames[s.Space.Active[k]].ToLowerInvariant(), p.W[k]);
            j.Num("b", p.B); j.Num("steepness", p.WeightNorm()); j.Close();

            var c = s.TrainResult;
            j.Key("training"); j.Open();
            j.Int("correct", c.Correct); j.Int("total", c.Total); j.Num("accuracy", c.Accuracy); j.Int("misrouted", c.Misrouted);
            j.Str("jumbo_correct", $"{c.TruePos}/{c.TruePos + c.FalseNeg}");
            j.Str("standard_correct", $"{c.TrueNeg}/{c.TrueNeg + c.FalsePos}");
            j.Num("objective_bce_l2", s.TrainObjective);
            j.Close();

            if (s.Selected != null) { j.Key("selected_egg"); EggJson(j, s.Selected); }

            // the most confidently wrong training eggs: concrete material for hints
            var worst = new System.Collections.Generic.List<(Egg e, double conf)>();
            foreach (var e in s.Dataset.Train)
                if (s.IsMisrouted(e)) worst.Add((e, Math.Abs(s.Probability(e) - 0.5)));
            worst.Sort((a, b) => b.conf.CompareTo(a.conf));
            j.Key("most_confident_mistakes"); j.OpenArr();
            for (int i = 0; i < Math.Min(3, worst.Count); i++) EggJson(j, worst[i].e);
            j.CloseArr();

            if (StageGoal != null) j.Str("stage_goal", StageGoal());
            if (RemainingSteps != null)
            {
                j.Key("remaining_steps"); j.OpenArr();
                foreach (var r in RemainingSteps()) j.Val(r);
                j.CloseArr();
            }
            if (Introduced != null)
            {
                j.Key("introduced"); j.OpenArr();
                foreach (var item in Introduced()) j.Val(item);
                j.CloseArr();
            }
            if (Speech != null)
            {
                // last few exchanges so Quack remembers what he asked and how the learner answered
                j.Key("recent_dialogue"); j.OpenArr();
                int start = Math.Max(0, Speech.History.Count - 6);
                for (int i = start; i < Speech.History.Count; i++)
                {
                    var l = Speech.History[i];
                    j.Val((l.Source == "you" ? "Learner: " : "Quack: ") + l.Speech);
                }
                j.CloseArr();
            }
            j.Key("recent_actions"); j.OpenArr();
            foreach (var a in s.RecentActions) j.Val($"{a.Kind}: {a.Detail}");
            j.CloseArr();
            j.Close();
            j.Close();
            return j.ToString();
        }

        void EggJson(Json j, Egg e)
        {
            var s = State;
            j.Open();
            j.Int("id", e.Id);
            j.Num("mass_g", e.Raw[0]); j.Num("length_mm", e.Raw[1]);
            if (!s.WidthLocked) j.Num("width_mm", e.Raw[2]);   // width is "not measured yet" in the 2-feature intro
            j.Str("label", EggDataset.ClassNames[e.Label]);
            j.Num("p_jumbo", s.Probability(e));
            j.Str("prediction", EggDataset.ClassNames[s.Prediction(e)]);
            j.Bool("misrouted", s.IsMisrouted(e));
            j.Close();
        }

        /// <summary>Tiny JSON writer (invariant culture, escaped strings).</summary>
        sealed class Json
        {
            readonly StringBuilder sb = new StringBuilder();
            bool first = true;
            void Comma() { if (!first) sb.Append(','); first = false; }
            public void Open() { Comma(); sb.Append('{'); first = true; }
            public void Close() { sb.Append('}'); first = false; }
            public void OpenArr() { Comma(); sb.Append('['); first = true; }
            public void CloseArr() { sb.Append(']'); first = false; }
            public void Key(string k) { Comma(); sb.Append('"').Append(k).Append("\":"); first = true; }
            public void Str(string k, string v) { Key(k); first = true; Val(v); }
            public void Num(string k, double v) { Key(k); first = true; Comma(); sb.Append(double.IsFinite(v) ? Math.Round(v, 4).ToString(CultureInfo.InvariantCulture) : "null"); }
            public void Int(string k, int v) { Key(k); first = true; Comma(); sb.Append(v); }
            public void Bool(string k, bool v) { Key(k); first = true; Comma(); sb.Append(v ? "true" : "false"); }
            public void Val(string v)
            {
                Comma(); sb.Append('"');
                foreach (char ch in v ?? "")
                {
                    if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                    else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                }
                sb.Append('"');
            }
            public override string ToString() => sb.ToString();
        }
    }
}
