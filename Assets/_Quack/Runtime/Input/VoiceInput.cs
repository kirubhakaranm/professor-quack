using System;
using System.IO;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Push-to-talk microphone capture (Windows / VR builds). Begin() starts recording from the default input
    /// device (in VR via Link: the headset mic if it is the Windows default recording device); End() returns a
    /// 16 kHz mono 16-bit WAV, or null if nothing usable was captured. Not available in browser builds.
    /// </summary>
    public sealed class VoiceInput : MonoBehaviour
    {
        public const int Rate = 16000, MaxSeconds = 120;
        // record from every microphone at once and keep the one that actually heard the learner:
        // laptops often expose silent defaults (VR headset mic not worn, vendor "AI" virtual inputs, empty USB jacks)
        string[] devices = new string[0];
        AudioClip[] clips = new AudioClip[0];
        public bool Recording { get; private set; }
        public string LastDevice { get; private set; }

        public static bool Supported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return Microphone.devices.Length > 0;
#endif
            }
        }

        public bool Begin()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (Recording) return true;
            devices = Microphone.devices;
            if (devices.Length == 0) { Debug.LogWarning("QUACK_MIC no microphone (check Windows privacy: let desktop apps use the microphone)"); return false; }
            clips = new AudioClip[devices.Length];
            int started = 0;
            for (int i = 0; i < devices.Length; i++)
            {
                try { clips[i] = Microphone.Start(devices[i], false, MaxSeconds, Rate); if (clips[i] != null) started++; }
                catch (Exception e) { Debug.LogWarning($"QUACK_MIC could not open '{devices[i]}': {e.Message}"); }
            }
            Recording = started > 0;
            Debug.Log($"QUACK_MIC recording from {started}/{devices.Length} device(s): {string.Join(" | ", devices)}");
            return Recording;
#endif
        }

        /// <summary>Stop all devices; return WAV from the one with the strongest signal, or null if all were silent.</summary>
        public byte[] End()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;
#else
            if (!Recording) return null;
            Recording = false;
            float bestPeak = 0; float[] best = null; int bestCh = 1; string bestName = null;
            var report = new System.Text.StringBuilder();
            for (int i = 0; i < devices.Length; i++)
            {
                if (clips[i] == null) continue;
                int frames = Frames(i);
                Microphone.End(devices[i]);
                if (frames < Rate / 3) { report.Append($"{devices[i]}: too short; "); continue; }
                var samples = new float[frames * clips[i].channels];
                clips[i].GetData(samples, 0);
                float peak = 0; foreach (var v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
                report.Append($"{devices[i]}: {frames / (float)Rate:0.0}s peak {peak:0.000}; ");
                if (peak > bestPeak) { bestPeak = peak; best = samples; bestCh = clips[i].channels; bestName = devices[i]; }
            }
            Debug.Log($"QUACK_MIC {report}");
            if (best == null || bestPeak < 0.01f) { Debug.Log("QUACK_MIC all devices silent"); return null; }
            LastDevice = bestName;
            Debug.Log($"QUACK_MIC using '{bestName}' (peak {bestPeak:0.000})");
            return ToWav(best, bestCh, Rate);
#endif
        }

        /// <summary>Samples recorded so far on device i (a full buffer reports position 0, so use the clip length).</summary>
        int Frames(int i)
        {
            int pos = Microphone.GetPosition(devices[i]);
            if (pos == 0 && clips[i] != null && !Microphone.IsRecording(devices[i])) pos = clips[i].samples;
            return pos;
        }

        /// <summary>WAV of everything recorded so far on the loudest device, without stopping (live transcript).</summary>
        public byte[] Snapshot()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;
#else
            if (!Recording) return null;
            float bestPeak = 0; float[] best = null; int bestCh = 1;
            for (int i = 0; i < devices.Length; i++)
            {
                if (clips[i] == null) continue;
                int frames = Frames(i);
                if (frames < Rate) continue;
                var samples = new float[frames * clips[i].channels];
                clips[i].GetData(samples, 0);
                float peak = 0; foreach (var v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
                if (peak > bestPeak) { bestPeak = peak; best = samples; bestCh = clips[i].channels; }
            }
            return best == null || bestPeak < 0.01f ? null : ToWav(best, bestCh, Rate);
#endif
        }

        /// <summary>Loudest current input level across devices (0..1), for the on-screen meter.</summary>
        public float Level()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return 0;
#else
            if (!Recording) return 0;
            float best = 0;
            var buf = new float[256];
            for (int i = 0; i < devices.Length; i++)
            {
                if (clips[i] == null) continue;
                int pos = Microphone.GetPosition(devices[i]) - 256;
                if (pos < 0) continue;
                clips[i].GetData(buf, pos);
                float sum = 0; foreach (var v in buf) sum += v * v;
                best = Mathf.Max(best, Mathf.Sqrt(sum / buf.Length));
            }
            return best;
#endif
        }

        static byte[] ToWav(float[] samples, int channels, int rate)
        {
            // mix to mono 16-bit PCM
            int frames = samples.Length / channels;
            using var ms = new MemoryStream(44 + frames * 2);
            using var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + frames * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(frames * 2);
            for (int f = 0; f < frames; f++)
            {
                float v = 0; for (int c = 0; c < channels; c++) v += samples[f * channels + c];
                v /= channels;
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), short.MinValue, short.MaxValue));
            }
            w.Flush();
            return ms.ToArray();
        }

        void OnDisable()
        {
            if (!Recording) return;
            foreach (var d in devices) Microphone.End(d);
            Recording = false;
        }
    }
}
