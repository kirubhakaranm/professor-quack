using System;
using UnityEngine;

namespace Quack
{
    /// <summary>Learner accessibility preferences, saved locally (PlayerPrefs).</summary>
    public sealed class AccessSettings
    {
        public float TextScale = 1f;          // 0.8 – 1.6
        public float CaptionOpacity = 0.85f;  // caption background
        public bool ReducedMotion;            // no bobbing, no tweened camera, no conveyor travel
        public bool LessChatter;              // suppress low-priority reactions and jokes
        public float ReadingPace = 1f;        // multiplies caption hold time
        public bool SmoothWalk;
        public bool Fullscreen = true;
        public float VoiceSpeed = 1f;          // 0.5x - 2x: voice playback and caption hold time (faster testing / slower listening)        // desktop: fullscreen (default) or windowed               // VR: stick walking (with comfort vignette); off = teleport pads only
        public bool VoiceOn = true;           // Quack speaks aloud when voice is available; captions always shown

        public event Action Changed;

        public static AccessSettings Load()
        {
            var s = new AccessSettings();
            try
            {
                s.TextScale = PlayerPrefs.GetFloat("quack.textScale", 1f);
                s.CaptionOpacity = PlayerPrefs.GetFloat("quack.captionOpacity", 0.85f);
                s.ReducedMotion = PlayerPrefs.GetInt("quack.reducedMotion", 0) == 1;
                s.LessChatter = PlayerPrefs.GetInt("quack.lessChatter", 0) == 1;
                s.ReadingPace = PlayerPrefs.GetFloat("quack.readingPace", 1f);
                s.VoiceOn = PlayerPrefs.GetInt("quack.voiceOn", 1) == 1;
                s.SmoothWalk = PlayerPrefs.GetInt("quack.smoothWalk", 0) == 1;
                s.Fullscreen = PlayerPrefs.GetInt("quack.fullscreen", 1) == 1;
                s.VoiceSpeed = Mathf.Clamp(PlayerPrefs.GetFloat("quack.voiceSpeed", 1f), 0.5f, 2f);
            }
            catch (Exception) { /* storage unavailable: defaults */ }
            return s;
        }

        public void Apply()
        {
            TextScale = Mathf.Clamp(TextScale, 0.8f, 1.6f);
            ReadingPace = Mathf.Clamp(ReadingPace, 0.5f, 3f);
            try
            {
                PlayerPrefs.SetFloat("quack.textScale", TextScale);
                PlayerPrefs.SetFloat("quack.captionOpacity", CaptionOpacity);
                PlayerPrefs.SetInt("quack.reducedMotion", ReducedMotion ? 1 : 0);
                PlayerPrefs.SetInt("quack.lessChatter", LessChatter ? 1 : 0);
                PlayerPrefs.SetFloat("quack.readingPace", ReadingPace);
                PlayerPrefs.SetInt("quack.voiceOn", VoiceOn ? 1 : 0);
                PlayerPrefs.SetInt("quack.smoothWalk", SmoothWalk ? 1 : 0);
                PlayerPrefs.SetInt("quack.fullscreen", Fullscreen ? 1 : 0);
                PlayerPrefs.SetFloat("quack.voiceSpeed", VoiceSpeed);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
            Changed?.Invoke();
        }
    }
}
