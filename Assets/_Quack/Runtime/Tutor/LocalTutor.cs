using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Deterministic, immediate reactions to what the learner actually did (selection, plane edits).
    /// Prefers questions and hints over answers. Gemini (later) handles open questions and recaps;
    /// this layer keeps the game responsive and works offline.
    /// </summary>
    public sealed class LocalTutor : MonoBehaviour
    {
        GameState state;
        SpeechQueue speech;
        float lastReaction = -99, lastJoke = -99;
        int selections;
        bool praisedStandards, explainedSteepness, explainedNearPlane, explainedMisroute;
        const float ReactionCooldown = 5f, JokeCooldown = 45f;

        public void Init(GameState s, SpeechQueue q)
        {
            state = s; speech = q;
            s.SelectionChanged += OnSelect;
            s.EditCommitted += OnEdit;
        }

        public void Intro()
        {
            speech.Say(new TutorLine("Welcome! I am Professor Quack, CEO of Professor Quack's Egg and AI Emporium, the finest egg factory on the pond. Our sorting machine has failed right before the big shipment, and eggs are going into the wrong cartons.",
                Emotion.MockSerious, Gesture.TieAdjust, TutorPriority.Teaching));
            speech.Say(new TutorLine("Our inspectors labelled these eggs: the big golden ones with a band went in JUMBO cartons, the smaller blue ones in STANDARD. ",
                Emotion.Encouraging, Gesture.PointAtPlot, TutorPriority.Teaching));
        }

        /// <summary>How to move the rule: one ring while the plot is flat, two rings in 3D.</summary>
        public static string SheetHowTo(bool flat) => flat
            ? "The line is our sorting rule: eggs on the arrow's side go to JUMBO. Drag along the teal ring to turn it, or drag the teal disc to slide it."
            : "The sheet is our sorting rule: eggs on the arrow's side go to JUMBO. Drag along the teal ring to turn it, the purple ring to tilt it into width, or the teal disc to slide it.";

        bool CanReact() => Time.time - lastReaction > ReactionCooldown;

        void React(TutorLine line)
        {
            lastReaction = Time.time;
            speech.Say(line);
        }

        void OnSelect(Egg e)
        {
            if (e == null) return;
            selections++;
            double p = state.Probability(e);
            int pred = state.Prediction(e);
            string label = EggDataset.ClassNames[e.Label];
            var x = state.Vector(e);

            if (selections == 1)
            {
                string desc = state.WidthLocked
                    ? $"Egg #{e.Id}: {e.Raw[0]:0.0} grams and {e.Raw[1]:0.0} mm long. Those two numbers are the egg's feature vector, and the plot puts it at that mass and length. We haven't measured width yet."
                    : $"Egg #{e.Id}: {e.Raw[0]:0.0} grams, {e.Raw[1]:0.0} mm long, {e.Raw[2]:0.0} mm wide. Those three numbers are the egg's feature vector, and the plot puts it at that mass, length and width.";
                speech.Say(new TutorLine(desc + " Nearby points are similar eggs, not eggs sitting near each other on the factory floor.",
                    Emotion.Curious, Gesture.PointAtPlot, TutorPriority.Teaching));
                if (state.Stage != "A_describe_eggs")
                    speech.Say(new TutorLine(SheetHowTo(state.WidthLocked), Emotion.Encouraging, Gesture.PointAtPlot, TutorPriority.Teaching));
                return;
            }
            if (!CanReact()) return;
            if (state.Stage == "A_describe_eggs")
            {
                // no sorting rule yet: talk about the measurements, not about routing
                if (selections == 2)
                    React(new TutorLine($"Egg #{e.Id}: {e.Raw[0]:0.0} grams, {e.Raw[1]:0.0} mm long. Compare it with the last one: is it further right, higher up, or both?",
                        Emotion.Curious, Gesture.PointAtPlot));
                return;
            }

            if (pred != e.Label && !explainedMisroute)
            {
                explainedMisroute = true;
                React(new TutorLine(
                    $"The inspectors said {label}, but our rule says {EggDataset.ClassNames[pred]}, with p(JUMBO) = {p:0.00}. Which side of the sheet is this egg on, and which way would you move the sheet?",
                    Emotion.Thinking, Gesture.PointAtPlot));
            }
            else if (pred != e.Label)
            {
                React(new TutorLine($"Another misroute: a {label} egg on the {EggDataset.ClassNames[pred]} side. Is it an odd one out, or are there neighbours like it?",
                    Emotion.Curious));
            }
            else if (System.Math.Abs(p - 0.5) < 0.12 && !explainedNearPlane)
            {
                explainedNearPlane = true;
                React(new TutorLine($"Correct, but only just: p(JUMBO) = {p:0.00}. Eggs close to the sheet are the ones our rule is least sure about.",
                    Emotion.Thinking));
            }
            else if (Mathf.Abs((float)x[2]) > 1.5f && System.Math.Abs(p - 0.5) < 0.3)
            {
                React(new TutorLine($"Unusually {(x[2] > 0 ? "wide" : "narrow")} egg. Does width seem to matter to the inspectors? Try a top-down view (key 2) to compare mass and width.",
                    Emotion.Curious, Gesture.PointAtPanel));
            }
        }

        // ---- coalesced reactions: let Quack finish, wait until the learner settles, react once to the net change ----
        ModelParams editBaseline;          // the rule before the current burst of edits
        int editsInBurst;
        bool steepInBurst;
        float lastEditAt;
        int bestCorrect = -1;              // best training result seen so far (coach-style "best so far")
        const float SettleSeconds = 1.5f;

        void OnEdit(ParamSource source, ModelParams before)
        {
            if (editBaseline == null) { editBaseline = before.Clone(); editsInBurst = 0; steepInBurst = false; }
            editsInBurst++;
            if (source == ParamSource.Steepness) steepInBurst = true;
            lastEditAt = Time.time;
            int c = state.TrainResult.Correct;
            if (c > bestCorrect) bestCorrect = c;
        }

        // silent body language while the learner turns dials / drags the sheet: better -> nod and brighten (a happy flap for
        // a big jump); worse -> concerned and a small shrug (a surprised quack for a big drop). Spoken comment comes on settle.
        int moodRef = -1; float lastMood = -99;
        void BodyLanguage()
        {
            var anim = speech != null ? speech.Animator : null;
            if (anim == null || speech.Current != null) return;
            int c = state.TrainResult.Correct;
            if (moodRef < 0) { moodRef = c; return; }
            if (Time.time - lastMood < 1.2f) return;
            int d = c - moodRef;
            if (d >= 10) { anim.SetEmotion(Emotion.Excited); anim.Play(Gesture.FlapSmall); }
            else if (d >= 3) { anim.SetEmotion(Emotion.Happy); anim.Play(Gesture.Nod); }
            else if (d <= -15) { anim.SetEmotion(Emotion.Surprised); anim.Play(Gesture.SurprisedQuack); }
            else if (d <= -3) { anim.SetEmotion(Emotion.Concerned); anim.Play(Gesture.Shrug); }
            else return;
            moodRef = c; lastMood = Time.time;
        }

        void Update()
        {
            if (state.Interacting) BodyLanguage(); else moodRef = -1;   // compare within each drag
            if (editBaseline == null) return;
            // never talk over Quack's current line; react only after the learner has stopped adjusting for a moment
            if (Time.time - lastEditAt < SettleSeconds || speech.Current != null || state.Interacting) return;
            var before = editBaseline; int n = editsInBurst; bool steep = steepInBurst;
            editBaseline = null;
            var line = ComposeReaction(before, n, steep);
            if (line != null) React(line);
        }

        /// <summary>One reaction to the NET effect of a burst of edits (built from the current state at speaking time).</summary>
        TutorLine ComposeReaction(ModelParams before, int edits, bool steep)
        {
            var nowC = state.TrainResult;
            var thenC = LogisticModel.Evaluate(before, state.Space.Train);
            int delta = thenC.Misrouted - nowC.Misrouted;   // positive = improvement
            string tries = edits > 1 ? $"After {edits} adjustments, " : "";

            if (steep && !explainedSteepness)
            {
                explainedSteepness = true;
                return new TutorLine("Notice the sheet did not move, so no egg changed carton. But every probability became bolder or more cautious. Hold that thought, because it matters when we measure mistakes.",
                    Emotion.Thinking, Gesture.PointAtPanel, TutorPriority.Teaching);
            }
            if (!state.HasBoundary)
                return new TutorLine("All the weights are zero, so there is no sheet at all. The rule shrugs and says fifty-fifty for every egg. Even I can't manage a factory like that.",
                    Emotion.MockSerious, Gesture.Shrug);
            if (nowC.Accuracy >= 0.92 && !praisedStandards)
            {
                praisedStandards = true;
                return new TutorLine($"{tries}{nowC.Correct} of {nowC.Total} eggs are routed correctly with one reusable rule. Finally, someone here with standards.",
                    Emotion.Delighted, Gesture.FlapSmall, TutorPriority.Teaching) { NonVerbal = "[delighted quack]" };
            }
            if (!CanReact()) return null;

            bool belowBest = bestCorrect > nowC.Correct + 2;
            if (delta >= 3)
                return new TutorLine($"{tries}misrouted eggs went from {thenC.Misrouted} to {nowC.Misrouted}." +
                    (belowBest ? $" Your best so far was {bestCorrect} correct." : " One rule, applied to every egg at once. Which change helped most?"),
                    delta >= 10 ? Emotion.Delighted : Emotion.Encouraging, delta >= 10 ? Gesture.FlapSmall : Gesture.Nod);
            if (delta <= -3)
            {
                if (delta <= -25 && Time.time - lastJoke > JokeCooldown)
                {
                    lastJoke = Time.time;
                    return new TutorLine($"{nowC.Misrouted} misrouted. The cartons are filing a complaint. Try nudging it back a little.",
                        Emotion.MockSerious, Gesture.SurprisedQuack) { NonVerbal = "[surprised quack]", IsJoke = true };
                }
                return new TutorLine($"{tries}that sent {-delta} more eggs to the wrong carton ({nowC.Misrouted} misrouted now)." +
                    (bestCorrect > nowC.Correct ? $" You had {bestCorrect} correct earlier - can you find your way back?" : " Which eggs crossed the sheet?"),
                    Emotion.Concerned, Gesture.Shrug);
            }
            var sel = state.Selected;
            if (sel != null && LogisticModel.Predict(before, state.Vector(sel)) != state.Prediction(sel))
                return new TutorLine($"Egg #{sel.Id} switched sides, so it now goes to {EggDataset.ClassNames[state.Prediction(sel)]}. Its measurements didn't change, only the rule did.",
                    Emotion.Thinking, Gesture.PointAtPlot);
            return null;   // small net change: say nothing rather than narrate every nudge
        }
    }
}
