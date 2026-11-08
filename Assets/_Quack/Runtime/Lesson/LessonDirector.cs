using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Runs the classification lesson as one continuous journey through stages. Each stage sets up the lab,
    /// shows a goal with a live checklist, detects completion from what the learner actually did, then asks the
    /// tutor for a recap (whiteboard) before Next unlocks. Every stage can be skipped (learner-controlled pacing).
    /// </summary>
    public sealed class LessonDirector : MonoBehaviour
    {
        public sealed class Stage
        {
            public string Id, Title, Goal, IntroSpeech;
            public Action Enter;
            public Func<string> Checklist;
            public Func<bool> Done;
            public Func<(string label, Action click)?> Action;
            public string RecapTitle;
            public Func<string[]> RecapLines;
        }

        GameState state;
        FeaturePlot plot;
        SpeechQueue speech;
        MissionBoard board;
        WhiteboardView whiteboard;
        DesktopInput desktop;
        AccessSettings access;
        Action<string, Func<TutorLine>> askTutor;

        readonly List<Stage> stages = new List<Stage>();
        int index = -1;
        bool completed;
        readonly HashSet<int> inspected = new HashSet<int>();
        bool editedThisStage, steepEdited, foundConfidentMistake;

        // optional views the later stages switch on (set by the bootstrap)
        public DecisionPlaneView PlaneView;
        public RuleConsole Console;
        public ControlDesk Desk;
        public EggTag Tag;
        public Transform PlotRoot;
        public Vector3 PlotHome, PlotAside;
        public LandscapeView Landscape;
        public TrainingPanel Training;

        double heldLenStart, heldBStart;
        bool evaluated, shipped;
        string[] shipmentLines = Array.Empty<string>();
        bool heldChanged, sawOvershoot;
        int trainedSteps;

        public Stage Current => index >= 0 && index < stages.Count ? stages[index] : null;

        /// <summary>Current stage goal (for the tutor).</summary>
        public string CurrentGoal => Current?.Goal ?? "";

        /// <summary>Unticked checklist steps of the current stage (for the tutor's leading questions).</summary>
        public string[] RemainingSteps()
        {
            var st = Current;
            if (st?.Checklist == null) return new string[0];
            var l = new List<string>();
            foreach (var line in st.Checklist().Split('\n'))
                if (line.StartsWith("[  ]")) l.Add(line.Substring(4).Trim());
            return l.ToArray();
        }

        // idle nudge: if the learner has done nothing for a while (no clicks, drags, camera moves) and Quack is quiet,
        // he says what to try next, in his own words. Repeated nudges get more concrete. Progress resets the ladder.
        string lastChecklist = "";
        float lastActivity, lastNudge = -999f;
        int nudgeLevel;                       // 0 = leading question, 1+ = concrete instruction
        const float NudgeAfter = 20f, NudgeEvery = 30f;

        /// <summary>Any learner input (pointer, keys, camera, UI) counts as activity.</summary>
        public void NoteActivity() => lastActivity = Time.time;

        void NudgeIfIdle()
        {
            var st = Current; if (st == null || finaleShown) return;   // no lesson hints during the board meeting
            string now = st.Checklist?.Invoke() ?? "";
            if (now != lastChecklist) { lastChecklist = now; lastActivity = Time.time; nudgeLevel = 0; return; }
            if (!speech.IsIdle || state.Interacting) { if (!speech.IsIdle) lastActivity = Mathf.Max(lastActivity, Time.time - NudgeAfter + 4f); return; }
            if (Time.time - lastActivity < NudgeAfter || Time.time - lastNudge < NudgeEvery) return;
            Nudge(nudgeLevel++);
        }

        /// <summary>"Next step" button: say the concrete next step now, or move on if the stage is done.</summary>
        public void NextStepRequested()
        {
            state.Log("next_step", Current?.Id ?? "");
            if (Current == null) return;
            if (completed) { NextStage(); return; }
            speech.Interrupt();
            Nudge(Math.Max(1, nudgeLevel++));
        }

        void Nudge(int level)
        {
            lastNudge = Time.time; lastActivity = Time.time;
            var rem = RemainingSteps();
            string line = completed ? Pick(ReadyNudges) : rem.Length > 0 ? NaturalNudge(rem[0], level) : Pick(GeneralNudges);
            speech.Say(new TutorLine(line, level == 0 ? Emotion.Curious : Emotion.Encouraging, Gesture.PointAtPanel, TutorPriority.Idle) { Gaze = GazeTarget.Learner });
        }

        // ---- idle nudges as conversation, not a to-do readout ----
        readonly System.Collections.Generic.Queue<string> recentNudges = new System.Collections.Generic.Queue<string>();
        readonly System.Random rng = new System.Random();

        /// <summary>Random line that wasn't said recently, so Quack never sounds like a looping recording.</summary>
        string Pick(string[] options)
        {
            var fresh = Array.FindAll(options, o => !recentNudges.Contains(o));
            if (fresh.Length == 0) fresh = options;
            var choice = fresh[rng.Next(fresh.Length)];
            recentNudges.Enqueue(choice); while (recentNudges.Count > 8) recentNudges.Dequeue();
            return choice;
        }

        static readonly string[] ReadyNudges =
        {
            "Take your time with the whiteboard. When you're ready, press Next lesson on the mission screen above the table.",
            "Whenever you like, press Next lesson on the mission screen and we'll move on.",
            "Any questions before we move on? Just ask. Otherwise, Next lesson whenever you like.",
            "I could admire this rule all day, but the shipment won't wait. Shall we press on to the next lesson?",
        };
        static readonly string[] GeneralNudges =
        {
            "Still with me? Ask me anything, or press Hint if you'd like a push.",
            "Have a look around the plot. What do you notice about where the golden and blue eggs sit?",
        };

        /// <summary>Turn the next unticked checklist step into something Professor Quack would actually say.</summary>
        string NaturalNudge(string step, int level)
        {
            string s = step.ToLowerInvariant();
            if (level >= 1) { var tell = Tell(s); if (tell != null) return Pick(tell); }
            string[] o;
            if (s.Contains("inspect")) o = new[]
            {
                "Pick an egg, any egg. I'm curious what its numbers say about where it sits.",
                "Try clicking one of the eggs at the edge of the cloud. Is it unusually heavy, or unusually long?",
                "Each egg has its own measurements. Click a couple more and see if their spots make sense.",
            };
            else if (s.Contains("width")) o = new[]
            {
                "Some of these eggs look alike with just two numbers. I wonder what a third measurement would show us. The mission board has a button for it.",
                "Our inspectors also measured width. Shall we add it and see what changes?",
            };
            else if (s.Contains("misrouted egg")) o = new[]
            {
                "Find an egg with a cross and click it. I'd love to know how much that mistake costs us.",
                "Every misrouted egg has a price tag in loss. Pick one and let's read it.",
            };
            else if (s.Contains("steep")) o = new[]
            {
                "Have you tried the steepness slider? Watch the probabilities, not the sheet.",
                "Here's a puzzle: can you change how confident the rule is without moving the sheet at all?",
            };
            else if (s.Contains("88%") || s.Contains("change the rule")) o = new[]
            {
                "Look at the eggs with crosses. Which way would you nudge the sheet to rescue a few of them?",
                "What if you turned the sheet a little with the teal ring? Watch the misrouted count while you do.",
                "Most of the mistakes seem to be on one side. Could sliding the sheet help?",
            };
            else if (s.Contains("bring training loss")) o = new[]
            {
                "The loss is still a bit high. Which eggs are costing us the most, and how could the sheet help them?",
                "Confident mistakes are expensive. Can you find a rule that is bold where it's right and modest where it isn't?",
            };
            else if (s.Contains("w_length") || s.Contains("reshapes")) o = new[]
            {
                "Here's an experiment: pull the offset lever or turn the length dial on the control desk and watch the landscape. Why do you think it changes shape?",
                "The landscape only shows two settings. What happens to it if we change one of the others?",
            };
            else if (s.Contains("lowest") || s.Contains("slice")) o = new[]
            {
                "That landscape has a lowest point somewhere. Can you roll the ball down to it?",
                "Where on the landscape would the loss be smallest? Follow the downhill direction.",
            };
            else if (s.Contains("learning rate") || s.Contains("step") || s.Contains("train")) o = new[]
            {
                "The training desk is ready when you are. Try a few steps and watch the loss.",
                "What do you think happens if we make the learning rate much bigger? Only one way to find out.",
            };
            else if (s.Contains("evaluate") || s.Contains("held-out")) o = new[]
            {
                "The real test is eggs the rule has never seen. Shall we bring them out?",
                "Training scores are nice, but I want to know how it does on fresh eggs. The mission screen can show us.",
            };
            else if (s.Contains("shipment")) o = new[]
            {
                "The big shipment is waiting at the door. Ready to send ten thousand eggs through?",
            };
            else o = new[] { $"Here's what's left on our list: {step}. Want a hint?" };
            return Pick(o);
        }

        /// <summary>Concrete, do-this-now instructions (second nudge onward, and the Next step button).</summary>
        static string[] Tell(string s)
        {
            if (s.Contains("inspect")) return new[]
            {
                "Here's what to do: click any egg in the plot. A card pops up beside the plot with its mass and length.",
                "Just click an egg, any one. Then click two more and compare their numbers. Tab also steps through them.",
            };
            if (s.Contains("width")) return new[]
            {
                "On the mission screen above the table, press Measure width. Then watch the flat plot grow a third direction.",
                "Press the Measure width button on the mission screen. The eggs will spread out into 3D.",
            };
            if (s.Contains("misrouted egg")) return new[]
            {
                "Click one of the eggs marked with a cross. Its card shows how much that mistake costs: its loss.",
            };
            if (s.Contains("steep")) return new[]
            {
                "On the control desk, turn the steepness knob all the way up, then all the way down. Watch the faint bands and the crosses.",
            };
            if (s.Contains("88%") || s.Contains("change the rule")) return new[]
            {
                "Drag along the teal ring to turn the sheet, and keep an eye on the correct count on the console.",
                "Try the purple ring: tilt the sheet a little toward width. Width separates eggs that mass alone can't.",
                "Drag the teal disc to slide the sheet without turning it. Stop when the misrouted count is lowest.",
            };
            if (s.Contains("bring training loss")) return new[]
            {
                "Use the rings and disc to rescue the eggs with the biggest crosses first; they cost the most loss.",
            };
            if (s.Contains("w_length") || s.Contains("reshapes")) return new[]
            {
                "On the control desk, pull the offset lever. The landscape on the right changes shape as you do.",
            };
            if (s.Contains("lowest") || s.Contains("slice")) return new[]
            {
                "Drag the teal ball on the landscape downhill until the loss number stops dropping.",
            };
            if (s.Contains("big learning rate")) return new[]
            {
                "Slide the learning rate all the way up past 20, then press Step a couple of times. Watch the loss.",
            };
            if (s.Contains("train")) return new[]
            {
                "On the training desk, set the learning rate near 1 and press Play. Watch the sheet move and the loss fall.",
            };
            if (s.Contains("evaluate")) return new[] { "Press Evaluate on held-out eggs on the mission screen." };
            if (s.Contains("shipment")) return new[] { "Press Run the next shipment on the mission screen." };
            return null;
        }

        /// <summary>Concepts the learner has been shown so far (sent to the tutor so it never quizzes ahead).</summary>
        public string[] Introduced()
        {
            var l = new List<string>
            {
                "The factory measures each egg: mass (grams) and length (mm); inspectors labelled eggs JUMBO (big golden eggs with a dark band) or STANDARD (smaller blue eggs)",
                "Each egg is a point in the plot at its measurements (its feature vector); nearby points = similar eggs",
            };
            if (!state.WidthLocked) l.Add("Width (mm) is the third measurement; the plot is now 3D (mass, length, width)");
            else l.Add("Width has NOT been introduced yet (the plot is flat, 2 features)");
            if (index >= 1) l.Add("A flat sheet (plane) is the sorting rule; eggs on the arrow side go to JUMBO; the arrow (weights) is perpendicular to the sheet; tilt and slide it");
            if (index >= 2) l.Add("Each egg gets a probability p(JUMBO) = sigmoid(score); faint bands mark p = 0.25 and 0.75; loss = how wrong, confident mistakes cost most; steepness");
            if (index >= 3) l.Add("Parameter space: a loss landscape where each point is a whole rule (a 2D slice, other parameters held fixed)");
            if (index >= 4) l.Add("Gradient descent: steps downhill with a learning rate; too big overshoots");
            if (index >= 5) l.Add("Held-out test eggs never used in training; a 10,000-egg shipment");
            return l.ToArray();
        }
        public bool CurrentCompleted => completed;
        public int StageIndex => index;
        /// <summary>(id, title, goal) of every lesson, for the start screen's lesson map.</summary>
        public IReadOnlyList<(string id, string title, string goal)> LessonList
        {
            get { var l = new List<(string, string, string)>(); foreach (var st in stages) l.Add((st.Id, st.Title, st.Goal)); return l; }
        }
        public int StageCount => stages.Count;

        /// <summary>Fraction of the current stage's checklist that is ticked (1 once complete).</summary>
        public float StageProgress()
        {
            var st = Current; if (st == null) return 0;
            if (completed) return 1;
            int total = 0, done = 0;
            foreach (var line in (st.Checklist?.Invoke() ?? "").Split('\n'))
            {
                if (line.StartsWith("[x]")) { total++; done++; }
                else if (line.StartsWith("[  ]")) total++;
            }
            return total == 0 ? 0 : done / (float)total;
        }

        /// <summary>Whole journey: finished stages plus the current one's progress.</summary>
        public float JourneyProgress() => stages.Count == 0 ? 0 : (Math.Max(0, index) + StageProgress()) / stages.Count;
        public event Action<Stage> StageEntered, StageCompleted;

        public void Init(GameState s, FeaturePlot p, SpeechQueue q, MissionBoard mb, WhiteboardView wb, DesktopInput d,
                         AccessSettings a, Action<string, Func<TutorLine>> ask)
        {
            state = s; plot = p; speech = q; board = mb; whiteboard = wb; desktop = d; access = a; askTutor = ask;
            mb.NextStepRequested = () => { NoteActivity(); NextStepRequested(); };
            mb.EndRequested = EndSession;
            s.SelectionChanged += e => { if (e != null) inspected.Add(e.Id); };
            if (Training != null) HookTraining();
            s.EditCommitted += (src, __) => { editedThisStage = true; if (src == ParamSource.Steepness) steepEdited = true; };
            s.SelectionChanged += e =>
            {
                if (e != null && s.IsMisrouted(e)) foundConfidentMistake = true;   // any mistake: its tag shows its loss
            };
            s.ParamsChanged += src =>
            {
                if (Current != null && Current.Id == "D_landscape" &&
                    (Math.Abs(s.Params.W[1] - heldLenStart) > 0.05 || Math.Abs(s.Params.B - heldBStart) > 0.05)) heldChanged = true;
            };
            Define();
        }

        /// <summary>Stages D+ put parameter space beside feature space; E+ adds the training desk.</summary>
        bool trainingHooked;
        public void HookTraining()
        {
            if (trainingHooked || Training == null) return;
            trainingHooked = true;
            Training.StepTaken += st =>
            {
                trainedSteps++;
                if (st.LearningRate >= 19.9 && (st.ObjectiveAfter > st.ObjectiveBefore || st.Diverged)) sawOvershoot = true;
            };
        }

        void ApplyLayout(int i, bool animate)
        {
            if (i >= 2)
            {
                if (PlaneView) PlaneView.ShowBands = true;
                plot.ScaleMarkersByLoss = true;
                if (Console) { Console.ShowLoss = true; Console.ShowSteepness = true; }
                if (Desk) Desk.ShowSteepness = true;
                if (Tag) Tag.ShowLoss = true;
            }
            bool showLand = (i == 3 || i == 4) && Landscape != null;
            if (Landscape) Landscape.gameObject.SetActive(showLand);
            // stage E: the training desk takes the console's stand (it shows the live numbers); the console returns after
            bool trainingStage = i == 4;
            if (Training) Training.Root.gameObject.SetActive(trainingStage);
            // introduce things one at a time: stage A is only eggs and axes; the sorting rule and its console arrive in B
            if (PlaneView) PlaneView.Visible = i >= 1;
            plot.ShowMarkers = i >= 1;
            // the control desk (and its screen) arrives with the sorting rule in lesson 2; its dials stay during training
            if (Desk) Desk.gameObject.SetActive(i >= 1);
            if (Console) Console.Root.gameObject.SetActive(i >= 1);
            if (PlotRoot == null) return;
            var target = showLand ? PlotAside : PlotHome;
            if (!animate || access.ReducedMotion) PlotRoot.position = target;
            else StartCoroutine(SlidePlot(target));
        }

        IEnumerator SlidePlot(Vector3 target)
        {
            var from = PlotRoot.position;
            for (float t = 0; t < 1.2f; t += Time.deltaTime) { PlotRoot.position = Vector3.Lerp(from, target, Mathf.SmoothStep(0, 1, t / 1.2f)); yield return null; }
            PlotRoot.position = target;
        }

        public void Begin(string startId = null)
        {
            int i = startId == null ? 0 : Math.Max(0, stages.FindIndex(st => st.Id.StartsWith(startId, StringComparison.OrdinalIgnoreCase)));
            // stages after A assume width is measured
            if (i > 0) { state.SetWidthLocked(false); plot.SetDepthScale(1); }
            Enter(i);
        }

        void Define()
        {
            stages.Add(new Stage
            {
                Id = "A_describe_eggs",
                Title = "Describe the eggs",
                Goal = "Every egg becomes a list of measurements: its feature vector. Inspect a few eggs and see where each one sits.",
                IntroSpeech = "Your turn. Click any egg in the plot to read its measurements, and check that its spot matches its numbers.",
                Enter = () =>
                {
                    state.SetWidthLocked(true);
                    plot.SetDepthScale(0);
                    state.SetParams(QuackGame.OldSorterParams(), ParamSource.Reset);
                    desktop?.SetView(1);   // face-on: the flat 2-feature plot
                },
                Checklist = () => Check(inspected.Count >= 3, $"Inspect 3 different eggs ({Math.Min(3, inspected.Count)}/3)") +
                                  Check(!state.WidthLocked, "Add the third measurement: width"),
                Action = () => inspected.Count >= 3 && state.WidthLocked ? ("Measure width too (3rd feature)", UnlockWidth) : null,
                Done = () => inspected.Count >= 3 && !state.WidthLocked,
                RecapTitle = "Eggs are vectors",
                RecapLines = () => new[]
                {
                    "Each egg = (mass, length, width): a feature vector",
                    "Axes are standardised: z = (value - mean) / std",
                    "Close in the plot = similar measurements, not close on the floor",
                    "Width separated eggs that looked alike in 2D",
                },
            });
            stages.Add(new Stage
            {
                Id = "B_build_rule",
                Title = "Build the sorting rule",
                Goal = "The broken sorter only weighed eggs (72% correct). Turn the sheet (teal ring), tilt it into width (purple ring), slide it (teal disc) or use the dials on the control desk to route at least 88% of eggs correctly.",
                IntroSpeech = "Meet the sorting rule: one flat sheet, and everything on the arrow's side goes to JUMBO. The old machine only weighed eggs. Drag along the rings to turn and tilt the sheet, slide it with the disc, and beat 88 percent.",
                Enter = () => { editedThisStage = false; desktop?.SetView(8); },
                Checklist = () =>
                {
                    var c = state.TrainResult;
                    return Check(editedThisStage, "Change the rule (turn, tilt, slide or console)") +
                           Check(c.Accuracy >= 0.88, $"Route at least 88% correctly (now {c.Accuracy:P0}, {c.Correct}/{c.Total})");
                },
                Done = () => editedThisStage && state.TrainResult.Accuracy >= 0.88,
                RecapTitle = "One rule for every egg",
                RecapLines = () => new[]
                {
                    "score = w . x + b ;  JUMBO when score >= 0  (p >= 0.5)",
                    "The sheet is score = 0; the arrow w is perpendicular to it",
                    "Tilting changes w's direction; sliding changes b",
                    $"{state.TrainResult.Correct}/150 correct vs 108/150 for the weight-only sorter",
                },
            });
            stages.Add(new Stage
            {
                Id = "C_measure_mistakes",
                Title = "Measure the mistakes",
                Goal = "A decision is not the whole story: each egg also gets a probability. The faint sheets mark p = 0.25 and p = 0.75. Loss measures how wrong the rule is, not just how often.",
                IntroSpeech = "Counting mistakes is blunt. A rule that is confidently wrong deserves more blame than one that is unsure. See those faint sheets? Between them, the rule is hesitant. Let's measure the damage properly.",
                Enter = () =>
                {
                    steepEdited = false; foundConfidentMistake = false;
                    if (PlaneView) PlaneView.ShowBands = true;
                    plot.ScaleMarkersByLoss = true;
                    if (Console) { Console.ShowLoss = true; Console.ShowSteepness = true; }
                if (Desk) Desk.ShowSteepness = true;
                    if (Tag) Tag.ShowLoss = true;
                    desktop?.SetView(8);
                },
                Checklist = () =>
                {
                    double loss = LogisticModel.DataLoss(state.Params, state.Space.Train);
                    return Check(foundConfidentMistake, "Select a misrouted egg (X) and read its loss on its tag") +
                           Check(steepEdited, "Turn steepness up and down: bands squeeze, mistake X marks grow, loss changes") +
                           Check(loss < 0.30, $"Bring training loss below 0.30 (now {loss:0.000})");
                },
                Done = () => foundConfidentMistake && steepEdited && LogisticModel.DataLoss(state.Params, state.Space.Train) < 0.30,
                RecapTitle = "Probability, decision and loss",
                RecapLines = () => new[]
                {
                    "p(JUMBO) = sigmoid(score); on the sheet p = 0.5",
                    "Loss per egg = -log p(true label): confident mistakes cost the most",
                    "Accuracy counts mistakes; loss is smooth, so we can follow its slope",
                    "Steepness |w| squeezes the p = 0.25 / 0.75 bands without moving the sheet",
                },
            });
            stages.Add(new Stage
            {
                Id = "D_landscape",
                Title = "Explore the loss landscape",
                Goal = "Left: feature space, one point = one egg. Right: parameter space, one point = one whole rule; height = its loss on all training eggs. It is a SLICE: w_length and b are held fixed.",
                IntroSpeech = "Here is a different kind of space. Each spot on this landscape is an entire sorting rule, and its height is how badly that rule does. Feature space on the left, parameter space on the right: don't mix them up.",
                Enter = () =>
                {
                    heldLenStart = state.Params.W[1]; heldBStart = state.Params.B; heldChanged = false;
                    desktop?.SetView(0);
                },
                Checklist = () =>
                {
                    double cur = Landscape ? Landscape.CurrentObjective : 0, min = Landscape ? Landscape.SliceMin : 0;
                    return Check(heldChanged, "On the control desk, change the offset lever or the length dial: the whole slice reshapes") +
                           Check(cur - min < 0.02, $"Drag the teal ball to the lowest point of this slice (loss {cur:0.000}, slice min {min:0.000})");
                },
                Done = () => heldChanged && Landscape != null && Landscape.CurrentObjective - Landscape.SliceMin < 0.02,
                RecapTitle = "Feature space vs parameter space",
                RecapLines = () => new[]
                {
                    "Feature space: one point = one egg; axes = measurements",
                    "Parameter space: one point = one rule; height = its loss",
                    "We see a 2D slice: w_length and b are held fixed",
                    "This loss is a single bowl (convex): no fake traps to fall into",
                },
            });
            stages.Add(new Stage
            {
                Id = "E_gradient_descent",
                Title = "Teach the machine to improve",
                Goal = "Let the machine walk downhill by itself: each step moves the rule against the gradient. Pick a learning rate on the control desk and press Step or Play.",
                IntroSpeech = "Now I'm promoting the machine. From the broken sorter, it will follow the slope of the loss downhill, one step at a time. You choose the step size.",
                Enter = () =>
                {
                    sawOvershoot = false; trainedSteps = 0;
                    Training?.SetLearningRate(1.0);
                    Training?.ResetRule();
                    desktop?.SetView(0);
                },
                Checklist = () =>
                {
                    double j = LogisticModel.Objective(state.Params, state.Space.Train);
                    return Check(trainedSteps > 0 && j < 0.25, $"Train down to loss below 0.25 (now {j:0.000})") +
                           Check(sawOvershoot, "Try a big learning rate (20 or more): see a step make the loss go UP");
                },
                Done = () => sawOvershoot && trainedSteps > 0 && LogisticModel.Objective(state.Params, state.Space.Train) < 0.25,
                RecapTitle = "Gradient descent",
                RecapLines = () => new[]
                {
                    "gradient = direction the loss rises fastest; step the other way",
                    "w <- w - learning_rate x gradient (all four parameters at once)",
                    "tiny rate: slow; moderate: converges; too big: overshoots or diverges",
                    "the eggs never moved - only the rule did",
                },
            });
            stages.Add(new Stage
            {
                Id = "F_test_and_scale",
                Title = "Test and scale",
                Goal = "Training eggs taught the rule. The dark, ringed eggs were held back: the rule has never seen them. Evaluate on them, then run the next shipment through the learned sorter.",
                IntroSpeech = "Moment of truth. These outlined eggs were locked in my office the whole time, so the machine has never seen them. If it does well on them, it learned a rule, not a list of answers.",
                Enter = () =>
                {
                    evaluated = false; shipped = false;
                    plot.ShowTestEggs(true);
                    desktop?.SetView(8);
                },
                Checklist = () => Check(evaluated, "Evaluate the rule on the held-out eggs (both classes)") +
                                  Check(shipped, "Run the next shipment: 10,000 new eggs through the learned sorter"),
                Action = () => !evaluated ? ("Evaluate on held-out eggs", Evaluate)
                             : !shipped ? ("Run the next shipment (10,000 eggs)", RunShipment) : null,
                Done = () => evaluated && shipped,
                RecapTitle = "Sequential vs batch processing",
                RecapLines = () =>
                {
                    var t = LogisticModel.Evaluate(state.Params, state.Space.Test);
                    const string blue = "<color=#1A3A9E>", red = "<color=#B3261E>", end = "</color>";
                    var lines = new List<string>
                    {
                        $"Unseen eggs: {t.Correct}/{t.Total} correct  (JUMBO {t.TruePos}/{t.TruePos + t.FalseNeg}, STANDARD {t.TrueNeg}/{t.TrueNeg + t.FalsePos})",
                        $"{blue}SEQUENTIAL{end}: one egg at a time,  w\u00B7x + b,  10,000 times",
                        $"{blue}BATCH{end}: eggs = rows of a matrix X  (10,000 x 3)",
                        $"{blue}scores = X w + b{end}: every egg in ONE operation",
                        $"Same w and b, same answers: only {red}how the work is organised{end} changes",
                        "Parallel workers help, but they are finite and moving data costs time",
                    };
                    if (shipmentLines != null && shipmentLines.Length > 1) { lines.Add(shipmentLines[1]); if (shipmentLines.Length > 2) lines.Add(shipmentLines[2]); }
                    return lines.ToArray();
                },
            });
        }

        static string Check(bool done, string text) => (done ? "[x] " : "[  ] ") + text + "\n";

        void Enter(int i)
        {
            index = i; completed = false; finaleShown = false;
            var st = stages[i];
            state.Stage = st.Id;
            ApplyLayout(i, animate: true);
            st.Enter?.Invoke();
            board.Show($"Lesson {i + 1} of {stages.Count}", st.Title, st.Goal, NextStage, SkipStage);
            board.SetNext(false);
            board.SetSkipVisible(i < stages.Count - 1);
            speech.Say(new TutorLine(st.IntroSpeech, Emotion.Encouraging, Gesture.PointAtPlot, TutorPriority.Teaching) { Gaze = GazeTarget.Plot });
            state.Log("stage", st.Id);
            Stations?.StageHome(HomeStation(i), desktop != null ? desktop.Preset : 0);
            StageEntered?.Invoke(st);
            Refresh();
        }

        void Update() { if (Current != null) { Refresh(); NudgeIfIdle(); } }

        void Refresh()
        {
            var st = Current;
            board.SetChecklist(st.Checklist?.Invoke() ?? "");
            var c = state.TrainResult;
            string loss = index >= 2 ? $"\nLOSS      {LogisticModel.DataLoss(state.Params, state.Space.Train):0.000}" : "";
            board.SetScore(index == 0 ? $"EGGS      {state.Space.Train.Count}\nINSPECTED {Math.Min(inspected.Count, 99)}"
                                      : $"CORRECT   {c.Correct}/{c.Total}\nMISROUTED {c.Misrouted}{loss}");
            var act = st.Action?.Invoke();
            board.SetAction(act?.label, act?.click);
            if (!completed && st.Done != null && st.Done())
            {
                completed = true;
                PlayerPrefs.SetInt("quack.done." + index, 1); PlayerPrefs.Save();   // roadmap shows it as done
                board.SetNext(true, index < stages.Count - 1 ? "Next lesson  >" : "Finish  >");
                Recap(st);
                StageCompleted?.Invoke(st);
            }
        }

        /// <summary>Screenshot runs: write a lesson's recap on the whiteboard without completing it.</summary>
        public void ShowRecapForShots(int i) { var st = stages[i]; whiteboard.Show(st.RecapTitle ?? st.Title, st.RecapLines?.Invoke() ?? Array.Empty<string>()); }

        void Recap(Stage st)
        {
            var lines = st.RecapLines?.Invoke() ?? Array.Empty<string>();
            whiteboard.Show(st.RecapTitle ?? st.Title, lines);
            if (Stations != null || (desktop != null && desktop.isActiveAndEnabled)) StartCoroutine(LookAtBoard());
            // local recap line (the whiteboard already holds the detail): saves the Gemini quota for player questions
            speech.Say(new TutorLine($"Lesson complete: {st.Title}. Have a look at the whiteboard, then press Next lesson when you're ready.",
                Emotion.Delighted, Gesture.Celebrate, TutorPriority.Teaching) { Gaze = GazeTarget.Whiteboard });
        }

        /// <summary>Turn to the whiteboard on the right wall for the recap, then back to where the learner was
        /// (the Next button lives on the mission board by the table). Key 6 looks at the board again any time.</summary>
        public StationDirector Stations;

        /// <summary>Each stage's home station (where Professor Quack stands and the camera rests).</summary>
        static string HomeStation(int i) => i == 3 || i == 4 ? "learning" : "table";

        IEnumerator LookAtBoard()
        {
            if (Stations != null)
            {
                // a slow pan to the board, a good look (about 8 s after he finishes), then a slow glide back
                if (desktop != null) desktop.SlowNext = true;
                Stations.Go("board", fromLearner: false, force: !state.Interacting);
                yield return null;
                while (!speech.IsIdle) yield return null;
                for (float t = 0; t < 8f; t += Time.deltaTime) yield return null;
                if (desktop != null) desktop.SlowNext = true;
                if (Stations.Current == "board") Stations.Go(HomeStation(index), fromLearner: false, force: true);
                yield break;
            }
            int back = desktop.Preset == 6 ? 4 : desktop.Preset;
            desktop.SetView(6);
            yield return null;
            while (!speech.IsIdle) yield return null;
            for (float t = 0; t < 3f; t += Time.deltaTime) yield return null;
            if (desktop.Preset == 6) desktop.SetView(back);   // unless the learner already looked elsewhere
        }

        public Hud Hud;   // finale panel lives in the HUD (works as a world panel in VR too)
        public Action RestartRequested;
        public BoardMeeting Meeting;
        int lastFlagged;
        bool finaleShown;

        /// <summary>The end of the mission: celebrate, list the skills the learner now has, offer to play again.</summary>
        public void Finale()
        {
            if (finaleShown) return;
            finaleShown = true;
            state.Log("finale", "shown");
            if (Meeting != null) { StartCoroutine(Meeting.Run(shipped ? 10000 : 0, lastFlagged)); return; }
            var t = LogisticModel.Evaluate(state.Params, state.Space.Test);
            speech.Say(new TutorLine($"Congratulations! You have learned the basics of machine learning: your classifier sorted {t.Correct} of {t.Total} brand-new eggs correctly.",
                Emotion.Delighted, Gesture.Celebrate, TutorPriority.Teaching) { Duck = DuckBehavior.Quack, NonVerbal = "[triumphant quack]" });
            Hud?.ShowFinale("CONGRATULATIONS!", $"Your sorter: {t.Correct}/{t.Total} correct on eggs it had never seen.",
                Outcomes, () => RestartRequested?.Invoke(), () => Hud.HideFinale());
        }

        /// <summary>Skills unlocked by each lesson (for the End-lesson summary).</summary>
        static readonly int[][] SkillsByLesson = { new[] { 0, 1 }, new[] { 2 }, new[] { 3, 4 }, new int[0], new[] { 5 }, new[] { 6 } };

        /// <summary>End lesson: stop here and show what was covered so far, with Play again / Keep going.</summary>
        public void EndSession()
        {
            state.Log("end_session", Current?.Id ?? "");
            int done = Math.Max(0, index + (completed ? 1 : 0));
            var skills = new List<string>();
            for (int i = 0; i < done && i < SkillsByLesson.Length; i++) foreach (var k in SkillsByLesson[i]) skills.Add(Outcomes[k]);
            if (skills.Count == 0) skills.Add("(finish Lesson 1 to unlock your first skills)");
            speech.Interrupt();
            speech.Say(new TutorLine(done > 0 ? $"Good work today. You finished {done} of {stages.Count} lessons. Come back any time and we'll pick up where we left off."
                                              : "Leaving already? No problem. The eggs will wait for you.",
                Emotion.Encouraging, Gesture.Nod, TutorPriority.Teaching));
            Hud?.ShowFinale("SESSION SUMMARY", $"Lessons finished: {done} of {stages.Count}", skills.ToArray(),
                () => RestartRequested?.Invoke(), () => Hud.HideFinale());
        }

        /// <summary>Learning outcomes, phrased as skills (also previewed on the TV at the start).</summary>
        public static readonly string[] Outcomes =
        {
            "Describe an egg as a feature vector: (mass, length, width)",
            "Read feature space: close points = similar eggs",
            "Sort with a line, then a plane: score = w.x + b, boundary at 0",
            "Turn a score into a probability with the sigmoid",
            "Tell accuracy from loss: confident mistakes cost most",
            "Train with gradient descent and choose a sensible learning rate",
            "Judge a model on held-out data, then run it on a whole batch",
        };

        /// <summary>Pause menu: start the current lesson again.</summary>
        public void RestartCurrent() { if (index >= 0) { state.Log("restart", Current.Id); Enter(index); } }

        void NextStage()
        {
            if (completed && index == stages.Count - 1) { Finale(); return; }
            if (!completed || index >= stages.Count - 1) return;
            Enter(index + 1);
        }

        void SkipStage()
        {
            if (index >= stages.Count - 1) return;
            state.Log("skip", Current.Id);
            if (Current.Id == "A_describe_eggs" && state.WidthLocked) UnlockWidth();
            Enter(index + 1);
        }

        void Evaluate()
        {
            var tr = state.TrainResult;
            var t = LogisticModel.Evaluate(state.Params, state.Space.Test);
            evaluated = true;
            state.Log("evaluate", $"test {t.Correct}/{t.Total}");
            whiteboard.Show("Held-out test (never seen in training)",
                $"Test eggs: {t.Correct}/{t.Total} correct ({t.Accuracy:P0})",
                $"JUMBO eggs: {t.TruePos}/{t.TruePos + t.FalseNeg} right    STANDARD eggs: {t.TrueNeg}/{t.TrueNeg + t.FalsePos} right",
                $"Training eggs, for comparison: {tr.Correct}/{tr.Total} ({tr.Accuracy:P0})",
                "Training = learning from labelled eggs. Evaluation = checking on held-out eggs. Inference = sorting new ones.");
            speech.Say(new TutorLine($"On eggs it has never seen: {t.Correct} of {t.Total} correct, and I checked both cartons separately. That is the number I'd show the board of directors.",
                t.Accuracy >= 0.85 ? Emotion.Proud : Emotion.Concerned, Gesture.PointAtPlot, TutorPriority.Teaching) { Gaze = GazeTarget.Whiteboard });
        }

        void RunShipment()
        {
            const int N = 10000;
            const double reviewBand = 0.15;   // flag eggs with 0.35 < p < 0.65 for a human; decisions still use p >= 0.5
            var shipment = ShipmentGenerator.Generate(N);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = ShipmentGenerator.Classify(shipment, state.Space.Scaler, state.Space.Active, state.Params, reviewBand);
            sw.Stop();
            shipped = true;
            lastFlagged = r.FlaggedForReview;
            state.Log("shipment", $"{r.Jumbo} jumbo, {r.Standard} standard, {r.FlaggedForReview} flagged");
            // illustrative cost model (NOT a measurement): per-egg overhead vs per-batch overhead; finite workers + transfer cost
            double perEggOverheadUs = 40, perEggComputeUs = 0.05, batch = 256, perBatchOverheadUs = 60, workers = 4, transferUs = 2000;
            double oneAtATime = N * (perEggOverheadUs + perEggComputeUs) / 1000.0;
            double batched = (Math.Ceiling(N / batch) * perBatchOverheadUs + N * perEggComputeUs) / 1000.0;
            double parallel = transferUs / 1000.0 + batched / workers;
            shipmentLines = new[]
            {
                $"Shipment of {N:N0}: {r.Jumbo:N0} JUMBO, {r.Standard:N0} STANDARD; {r.FlaggedForReview:N0} unsure (0.35<p<0.65) sent to an inspector",
                $"Measured here: {sw.Elapsed.TotalMilliseconds:0.0} ms for {N:N0} eggs (one CPU thread, this C# code)",
                $"ILLUSTRATIVE, not measured: one-at-a-time ~{oneAtATime:0} ms; batches of {batch} ~{batched:0.0} ms; {workers} workers + transfer ~{parallel:0.0} ms",
            };
            whiteboard.Show($"Next shipment: {N:N0} new eggs", shipmentLines);
            speech.Say(new TutorLine($"Ten thousand eggs, sorted. {r.FlaggedForReview} were too close to call, so they go to a human inspector instead of guessing. Finally, a factory I can be proud of.",
                Emotion.Delighted, Gesture.Celebrate, TutorPriority.Teaching) { Gaze = GazeTarget.Whiteboard, Duck = DuckBehavior.Quack });
        }

        void UnlockWidth()
        {
            state.SetWidthLocked(false);
            StartCoroutine(RevealDepth());
            speech.Say(new TutorLine("Width! Watch eggs that looked identical spread apart. Same eggs, one more measurement, a richer description.",
                Emotion.Excited, Gesture.PointAtPlot, TutorPriority.Teaching) { Gaze = GazeTarget.Plot, Intensity = 0.7f });
        }

        IEnumerator RevealDepth()
        {
            desktop?.SetView(4);
            if (access.ReducedMotion) { plot.SetDepthScale(1); yield break; }
            for (float t = 0; t < 1.6f; t += Time.deltaTime) { plot.SetDepthScale(Mathf.SmoothStep(0, 1, t / 1.6f)); yield return null; }
            plot.SetDepthScale(1);
        }
    }
}
