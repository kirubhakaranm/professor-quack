using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Quack
{
    /// <summary>Backend /tts reply.</summary>
    [Serializable]
    public sealed class TtsReply
    {
        public string id;
        public float duration;
        public float[] envelope;
        public float windowMs = 20;
    }

    /// <summary>
    /// Fetches Professor Quack's voice for a line (backend /tts → cached WAV + loudness envelope). Optional:
    /// if the backend can't produce audio (no billing, quota, offline) the line simply stays text-only.
    /// </summary>
    public sealed class VoiceClient : MonoBehaviour
    {
        public TutorClient Tutor;
        public AccessSettings Access;
        float pausedUntil;   // back off after a failure so we don't spam the backend

        // automated runs (smoke test, tours) never fetch voice: no quota use, no timing changes
        static readonly bool Automated = Array.IndexOf(Environment.GetCommandLineArgs(), "-quackSmoke") >= 0
                                      || Array.IndexOf(Environment.GetCommandLineArgs(), "-tutorMock") >= 0;
        public bool Enabled => !Automated && !(Tutor != null && Tutor.MockRequests) && Access != null && Access.VoiceOn && Time.time >= pausedUntil;

        /// <summary>True while the voice service is resting after a failure/limit (captions continue).</summary>
        public bool Resting => Access != null && Access.VoiceOn && !Automated && Time.time < pausedUntil;

        // at most two voice requests in flight: bursts trip Gemini's per-minute speech limit
        const int MaxInFlight = 2;
        int inFlight;
        readonly System.Collections.Generic.Queue<TutorLine> waiting = new System.Collections.Generic.Queue<TutorLine>();

        public void Prepare(TutorLine line)
        {
            if (!Enabled || line == null || string.IsNullOrWhiteSpace(line.Speech) || line.VoicePending || line.Clip != null) return;
            line.VoicePending = true;
            if (inFlight >= MaxInFlight) { waiting.Enqueue(line); return; }
            StartCoroutine(Run(line));
        }

        IEnumerator Run(TutorLine line)
        {
            inFlight++;
            yield return Fetch(line);
            inFlight--;
            while (waiting.Count > 0)
            {
                var next = waiting.Dequeue();
                if (!next.VoicePending || next.Clip != null) continue;
                if (!Enabled) { next.VoicePending = false; continue; }
                StartCoroutine(Run(next));
                break;
            }
        }

        IEnumerator Fetch(TutorLine line)
        {
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TtsRequest { text = line.Speech }));
            using (var req = new UnityWebRequest(Tutor.BaseUrl + "/tts", "POST")
            {
                uploadHandler = new UploadHandlerRaw(body), downloadHandler = new DownloadHandlerBuffer(), timeout = 45,   // the server may retry once (2 x 20 s)
            })
            {
                req.SetRequestHeader("Content-Type", "application/json");
                if (Tutor.MockRequests) req.SetRequestHeader("X-Quack-Mock", "1");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    line.VoicePending = false;
                    pausedUntil = Time.time + (req.responseCode == 503 ? 60f : 4f);   // brief back-off: a single bad reply must not silence a whole stage
                    Debug.Log($"QUACK_VOICE unavailable ({req.responseCode}); text-only");
                    yield break;
                }
                TtsReply r = null;
                try { r = JsonUtility.FromJson<TtsReply>(req.downloadHandler.text); } catch { }
                if (r == null || string.IsNullOrEmpty(r.id)) { line.VoicePending = false; yield break; }
                line.Envelope = r.envelope;
                line.EnvelopeWindow = Mathf.Max(0.005f, r.windowMs / 1000f);
                using var audio = UnityWebRequestMultimedia.GetAudioClip(Tutor.BaseUrl + "/tts-audio/" + r.id + ".wav", AudioType.WAV);
                yield return audio.SendWebRequest();
                if (audio.result == UnityWebRequest.Result.Success) line.Clip = DownloadHandlerAudioClip.GetContent(audio);
                line.VoicePending = false;
                Debug.Log($"QUACK_VOICE ready {r.duration:0.0}s");
            }
        }

        [Serializable] sealed class TtsRequest { public string text; }
    }
}
