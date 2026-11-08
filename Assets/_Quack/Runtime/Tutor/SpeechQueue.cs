using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Plays tutor lines one at a time. Captions, beak motion and gestures all start together when a line
    /// actually begins (not when it was requested). Without audio, hold time comes from reading speed;
    /// when TTS audio is added, the AudioSource clock and its amplitude envelope will drive the same hooks.
    /// Higher-priority lines (player questions) may cut in; reactions never interrupt speech.
    /// </summary>
    public sealed class SpeechQueue : MonoBehaviour
    {
        public QuacksAnimator Animator;
        public AccessSettings Access;
        public VoiceClient Voice;          // optional
        public AudioSource Audio;          // optional; on Quack's head
        const float VoiceWait = 3.5f;          // reactions: max wait for audio before starting caption-only
        const float AnswerVoiceWait = 12f;     // answers to the learner's questions: wait longer so they start spoken
        bool currentVoiced, waitingVoice;

        public event Action<TutorLine> LineStarted;
        public event Action LineEnded;

        public readonly List<TutorLine> History = new List<TutorLine>();
        public TutorLine Current { get; private set; }
        public bool Paused { get; set; }
        public float Remaining => Current == null ? 0 : Mathf.Max(0, endTime - Time.time);

        readonly List<TutorLine> pending = new List<TutorLine>();
        float startTime, endTime, pausedAt;

        float lastDuck = -999f;
        const float DuckCooldown = 30f;

        public void Say(TutorLine line)
        {
            // duck habits: rate-limited, suppressed by "less chatter", sound cues always captioned
            if (line != null && line.Duck != DuckBehavior.None)
            {
                if (Access.LessChatter || Time.time - lastDuck < DuckCooldown) line.Duck = DuckBehavior.None;
                else
                {
                    lastDuck = Time.time;
                    if (string.IsNullOrEmpty(line.NonVerbal))
                        line.NonVerbal = line.Duck == DuckBehavior.Quack ? "[quack]" : line.Duck == DuckBehavior.EmbarrassedQuack ? "[accidental quack]" : null;
                }
            }
            if (line == null || string.IsNullOrWhiteSpace(line.Speech) && string.IsNullOrEmpty(line.NonVerbal)) return;
            if (Access.LessChatter && (line.IsJoke || line.Priority >= TutorPriority.Idle)) return;
            if (Current != null && line.Priority == TutorPriority.PlayerQuestion && Current.Priority > TutorPriority.PlayerQuestion)
            {
                pending.Insert(0, line);
                line.QueuedAt = Time.time;
                Voice?.Prepare(line);
                Finish();
                return;
            }
            // drop stale reactions so feedback stays about the latest attempt
            if (line.Priority == TutorPriority.Reaction) pending.RemoveAll(l => l.Priority >= TutorPriority.Reaction);
            int i = pending.FindIndex(l => l.Priority > line.Priority);
            if (i < 0) pending.Add(line); else pending.Insert(i, line);
            line.QueuedAt = Time.time;
            Voice?.Prepare(line);
        }

        public bool IsIdle => Current == null && pending.Count == 0;

        public void Replay(TutorLine line)
        {
            if (line == null) return;
            var copy = (TutorLine)line.MemberwiseCloneLine();
            copy.Priority = TutorPriority.PlayerQuestion;
            Say(copy);
        }

        public void Skip() { if (Current != null) Finish(); }

        /// <summary>The learner started talking: stop speaking now and drop queued reactions (keep teaching lines).</summary>
        public void Interrupt()
        {
            pending.RemoveAll(l => l.Priority >= TutorPriority.Reaction);
            if (Current != null) Finish();
        }

        /// <summary>Drop everything queued and stop the current line (e.g. the learner skipped the intro).</summary>
        public void ClearAll()
        {
            pending.Clear();
            if (Current != null) Finish();
        }

        public event Action<string> LearnerSaid;

        /// <summary>Record what the learner said (shown in captions/history, never spoken).</summary>
        public void LogLearner(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            History.Add(new TutorLine(text) { Source = "you" });
            LearnerSaid?.Invoke(text);
        }

        void Update()
        {
            if (Current != null)
            {
                if (Paused)
                {
                    endTime += Time.deltaTime;
                    if (Audio != null && Audio.isPlaying) Audio.Pause();
                    return;
                }
                if (Audio != null && Current.Clip != null && !Audio.isPlaying && Audio.time > 0 && Audio.time < Current.Clip.length) Audio.UnPause();
                // late voice: the audio arrived after the caption started -> speak now and hold the caption until done
                if (!currentVoiced && Current.Clip != null && Audio != null && Access.VoiceOn && endTime - Time.time > 0.8f)
                    StartVoice(Current);
                else if (waitingVoice && !Current.VoicePending && Current.Clip == null)
                {
                    // the voice failed: fall back to text-timed beak motion for the rest of the caption
                    waitingVoice = false;
                    string t = Current.Speech ?? ""; float s0 = Time.time, s1 = endTime;
                    Animator.StartSpeaking(() => Envelope(t, (Time.time - s0) / Mathf.Max(0.01f, s1 - s0 - 0.4f)));
                }
                if (Time.time >= endTime) Finish();
                return;
            }
            if (pending.Count > 0 && !Paused)
            {
                // give the voice a moment so captions, beak and audio start together; then go text-only
                var next = pending[0];
                float wait = next.Priority == TutorPriority.PlayerQuestion ? AnswerVoiceWait : VoiceWait;
                if (next.VoicePending && Time.time - next.QueuedAt < wait) return;
                Begin(next);
            }
        }

        void Begin(TutorLine line)
        {
            pending.RemoveAt(0);
            Current = line;
            History.Add(line);
            if (History.Count > 60) History.RemoveAt(0);
            startTime = Time.time;
            endTime = startTime + HoldSeconds(line);
            bool voiced = line.Clip != null && Audio != null && Access.VoiceOn;
            currentVoiced = false;
            Animator.SetEmotion(line.Emotion);
            Animator.Intensity = line.Intensity;
            Animator.Gaze = line.Gaze;
            Animator.Play(line.Gesture);
            float speechStart = startTime, speechEnd = endTime;
            string text = line.Speech ?? "";
            waitingVoice = false;
            if (voiced) StartVoice(line);
            else if (line.VoicePending && Access.VoiceOn) { waitingVoice = true; Animator.StartSpeaking(() => 0f); }   // beak shut until the audio arrives
            else Animator.StartSpeaking(() => Envelope(text, (Time.time - speechStart) / Mathf.Max(0.01f, speechEnd - speechStart - 0.4f)));
            LineStarted?.Invoke(line);
        }

        /// <summary>Play the line's audio now; the caption lasts as long as the audio; beak follows its loudness.</summary>
        void StartVoice(TutorLine line)
        {
            currentVoiced = true; waitingVoice = false;
            float speed = Access != null ? Mathf.Clamp(Access.VoiceSpeed, 0.5f, 2f) : 1f;
            Audio.pitch = speed;   // faster/slower playback (the beak follows Audio.time, so it stays in sync)
            Audio.clip = line.Clip; Audio.time = 0; Audio.Play();
            endTime = Time.time + Mathf.Max(line.Clip.length / speed + 0.5f, 1.5f);
            // beak follows the real audio loudness at the playback clock (approximate, not phoneme lip sync)
            var env = line.Envelope; float win = line.EnvelopeWindow;
            Animator.StartSpeaking(() =>
            {
                if (!Audio.isPlaying || env == null || env.Length == 0) return 0f;
                int k = Mathf.Clamp((int)(Audio.time / win), 0, env.Length - 1);
                return env[k];
            });
        }

        void Finish()
        {
            var line = Current;
            Current = null;
            Animator.StopSpeaking();
            if (Audio != null && Audio.isPlaying) Audio.Stop();
            if (line != null)
            {
                // duck habits happen after the sentence, never interrupting an explanation
                if (line.Duck != DuckBehavior.None) Animator.PlayDuck(line.Duck);
                else Animator.Play(line.AfterSpeech);
            }
            LineEnded?.Invoke();
        }

        float HoldSeconds(TutorLine line)
        {
            int words = string.IsNullOrEmpty(line.Speech) ? 0 : line.Speech.Split(' ').Length;
            return Mathf.Max(1.5f, (1.2f + words / 2.6f) * Access.ReadingPace / Mathf.Clamp(Access.VoiceSpeed, 0.5f, 2f));   // ~155 wpm + settle time, scaled by the speed setting
        }

        /// <summary>
        /// Approximate text-driven amplitude for the beak when there is no audio: vowels open, consonants
        /// half, spaces/punctuation close. Phase 0..1 over the line. Clearly not lip sync.
        /// </summary>
        static float Envelope(string text, float phase)
        {
            if (phase < 0 || phase >= 1 || text.Length == 0) return 0;
            float pos = phase * text.Length;
            char c = char.ToLowerInvariant(text[Mathf.Clamp((int)pos, 0, text.Length - 1)]);
            float frac = pos - Mathf.Floor(pos);
            float shape = Mathf.Sin(frac * Mathf.PI);
            if ("aeiou".IndexOf(c) >= 0) return 0.9f * shape + 0.1f;
            if (char.IsLetter(c)) return 0.45f * shape;
            return 0f;
        }
    }

    static class TutorLineExt
    {
        public static TutorLine MemberwiseCloneLine(this TutorLine l) => new TutorLine(l.Speech, l.Emotion, l.Gesture, l.Priority)
        {
            AfterSpeech = l.AfterSpeech, NonVerbal = l.NonVerbal, IsJoke = l.IsJoke, Source = l.Source,
            Gaze = l.Gaze, Intensity = l.Intensity, Duck = DuckBehavior.None, Board = l.Board,
            Clip = l.Clip, Envelope = l.Envelope, EnvelopeWindow = l.EnvelopeWindow,
        };
    }
}
