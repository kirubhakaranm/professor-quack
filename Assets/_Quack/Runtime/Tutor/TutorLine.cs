using System;

namespace Quack
{
    /// <summary>Who asked for this line. Lower value = higher priority.</summary>
    public enum TutorPriority { PlayerQuestion = 0, Teaching = 1, Reaction = 2, Idle = 3 }

    /// <summary>
    /// Merged emotion vocabulary: the original lesson set (first six, values unchanged for compatibility)
    /// plus the character-brief additions. Must match backend/schema.mjs EMOTIONS.
    /// </summary>
    public enum Emotion
    {
        Neutral, Curious, Thinking, Encouraging, Delighted, MockSerious,
        Happy, Excited, Confused, Surprised, Concerned, Proud, Laughing,
    }

    /// <summary>Validated gesture vocabulary (backend GESTURES). Unknown values map to None.</summary>
    public enum Gesture
    {
        None, PointAtPlot, PointAtPanel, Nod, Shrug, TieAdjust, FlapSmall, SurprisedQuack, Preen,
        PointAtConveyor, AdjustGlasses, Celebrate, ThinkPose,
    }

    /// <summary>Where Quack looks while speaking (backend GAZE_TARGETS).</summary>
    public enum GazeTarget { Learner, Plot, SelectedEgg, SortingRule, Conveyor, Whiteboard }

    /// <summary>Occasional duck habits (backend DUCK_BEHAVIORS). Rate-limited by the speech queue.</summary>
    public enum DuckBehavior { None, Quack, EmbarrassedQuack, WingFlap, Preen, TailWiggle }

    [Serializable]
    public sealed class Whiteboard
    {
        public string title;
        public string[] lines;
    }

    /// <summary>
    /// One thing Quack says/does. Same shape as the backend reply:
    /// {speech, emotion, gesture, after_speech, gaze_target, intensity, duck_behavior, whiteboard?}.
    /// </summary>
    [Serializable]
    public sealed class TutorLine
    {
        public string Speech;
        public Emotion Emotion = Emotion.Neutral;
        public Gesture Gesture = Gesture.None;
        public Gesture AfterSpeech = Gesture.None;
        public GazeTarget Gaze = GazeTarget.Learner;
        public float Intensity = 0.5f;
        public DuckBehavior Duck = DuckBehavior.None;
        public Whiteboard Board;
        public string Highlight;          // scene object to flash while speaking (HighlightManager key), or null
        public string NonVerbal;          // captioned sound, e.g. "[surprised quack]"
        public TutorPriority Priority = TutorPriority.Reaction;
        public bool IsJoke;               // suppressed by "less chatter"
        public string Source = "local";   // "local" | "gemini" | "mock" | "offline"

        // voice (optional): filled by VoiceClient; never serialised
        [NonSerialized] public UnityEngine.AudioClip Clip;
        [NonSerialized] public float[] Envelope;
        [NonSerialized] public float EnvelopeWindow = 0.02f;
        [NonSerialized] public bool VoicePending;
        [NonSerialized] public float QueuedAt;

        public TutorLine(string speech, Emotion emotion = Emotion.Neutral, Gesture gesture = Gesture.None,
                         TutorPriority priority = TutorPriority.Reaction)
        {
            Speech = speech; Emotion = emotion; Gesture = gesture; Priority = priority;
        }

        /// <summary>Caption text: non-verbal sound cue (if any) followed by speech.</summary>
        public string CaptionText => string.IsNullOrEmpty(NonVerbal) ? Speech : $"{NonVerbal} {Speech}";

        public static Emotion ParseEmotion(string s) => s switch
        {
            "curious" => Emotion.Curious, "thinking" => Emotion.Thinking, "encouraging" => Emotion.Encouraging,
            "delighted" => Emotion.Delighted, "mock_serious" => Emotion.MockSerious, "happy" => Emotion.Happy,
            "excited" => Emotion.Excited, "confused" => Emotion.Confused, "surprised" => Emotion.Surprised,
            "concerned" => Emotion.Concerned, "proud" => Emotion.Proud, "laughing" => Emotion.Laughing,
            _ => Emotion.Neutral,
        };

        public static Gesture ParseGesture(string s) => s switch
        {
            "point_at_plot" => Gesture.PointAtPlot, "point_at_panel" => Gesture.PointAtPanel,
            "point_at_learning_rate" => Gesture.PointAtPanel, "point_at_conveyor" => Gesture.PointAtConveyor,
            "nod" => Gesture.Nod, "small_nod" => Gesture.Nod, "shrug" => Gesture.Shrug,
            "tie_adjust" => Gesture.TieAdjust, "adjust_glasses" => Gesture.AdjustGlasses,
            "wing_flap" => Gesture.FlapSmall, "small_flap" => Gesture.FlapSmall, "celebrate" => Gesture.Celebrate,
            "think_pose" => Gesture.ThinkPose, "surprised_quack" => Gesture.SurprisedQuack,
            _ => Gesture.None,
        };

        public static GazeTarget ParseGaze(string s) => s switch
        {
            "plot" => GazeTarget.Plot, "selected_egg" => GazeTarget.SelectedEgg, "sorting_rule" => GazeTarget.SortingRule,
            "conveyor" => GazeTarget.Conveyor, "whiteboard" => GazeTarget.Whiteboard, _ => GazeTarget.Learner,
        };

        public static DuckBehavior ParseDuck(string s) => s switch
        {
            "quack" => DuckBehavior.Quack, "embarrassed_quack" => DuckBehavior.EmbarrassedQuack,
            "wing_flap" => DuckBehavior.WingFlap, "preen" => DuckBehavior.Preen, "tail_wiggle" => DuckBehavior.TailWiggle,
            _ => DuckBehavior.None,
        };
    }
}
