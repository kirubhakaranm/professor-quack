using System.Linq;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Scene bootstrap. Builds the lab (one world for desktop and VR), the shared GameState, every view derived
    /// from it, Professor Quack, the tutor, the lesson director and the input path (mouse or OpenXR with -vr).
    /// </summary>
    public sealed class QuackGame : MonoBehaviour
    {
        public QuackPalette Palette;
        public Camera Cam;

        public GameState State { get; private set; }
        public AccessSettings Access { get; private set; }
        public Hud Hud { get; private set; }
        public LessonDirector Lesson { get; private set; }
        public MissionBoard Mission { get; private set; }
        public TutorClient Tutor { get; private set; }

        SpeechQueue speech;
        StationDirector stations;
        StartScreen titleScreen;
        XRRig xrRig;
        QuacksAnimator anim;
        public QuacksAnimator Animator => anim;

        /// <summary>The broken factory sorter only weighed eggs: a mass-only rule.</summary>
        public static ModelParams OldSorterParams() => new ModelParams(new[] { 1.5, 0.0, 0.0 }, -0.35);

        void Start()
        {
            var csv = Resources.Load<TextAsset>("eggs");
            if (csv == null) { Debug.LogError("Quack: Resources/eggs.csv missing"); return; }
            var ds = EggDataset.ParseCsv(csv.text);
            State = new GameState(ds, new[] { 0, 1, 2 }, OldSorterParams()) { Clock = () => Time.time };
            Access = AccessSettings.Load();
            // display mode follows the setting (fullscreen by default; F11 or Settings switch to a window)
            void ApplyDisplay()
            {
                if (XRBoot.Requested || Application.platform == RuntimePlatform.WebGLPlayer) return;
                var want = Access.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Screen.fullScreenMode != want)
                {
                    if (Access.Fullscreen) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, want);
                    else Screen.SetResolution(Mathf.RoundToInt(Display.main.systemWidth * 0.75f), Mathf.RoundToInt(Display.main.systemHeight * 0.75f), want);
                }
            }
            // automated runs pass -screen-fullscreen 0 and a fixed size: keep it
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-screen-fullscreen") < 0) ApplyDisplay();
            Access.Changed += ApplyDisplay;
            if (Cam == null) Cam = Camera.main;
            Cam.fieldOfView = 60; Cam.nearClipPlane = 0.03f;

            // character checks run in a plain studio: only Professor Quack, so every check is unambiguous
            var studioDir = CharacterPortrait.RequestedDir();
            if (studioDir != null || CharacterViewer.Requested) { RunStudio(studioDir); return; }

            // ---- the lab ----
            var lab = LabEnvironment.Build(Palette);

            var plotGo = new GameObject("Feature plot (hologram)");
            plotGo.transform.SetPositionAndRotation(lab.PlotAnchor.position, lab.PlotAnchor.rotation);
            plotGo.transform.localScale = Vector3.one * (LabEnvironment.PlotSize / 2f / FeaturePlot.HalfExtent);   // uniform scale only
            var plot = plotGo.AddComponent<FeaturePlot>();
            plot.Build(State, Palette);
            var plane = plotGo.AddComponent<DecisionPlaneView>();
            plane.Build(State, Palette);
            plot.DepthChanged += plane.SetDepth;
            var equation = plotGo.AddComponent<EquationView>();
            equation.Build(State, plot, plane, Palette);

            var landGo = new GameObject("Loss landscape (parameter space)");
            landGo.transform.SetPositionAndRotation(lab.LandscapeAnchor.position, lab.LandscapeAnchor.rotation);
            var landscape = landGo.AddComponent<LandscapeView>();
            landscape.Build(State, Palette);

            var conveyorGo = new GameObject("Sorting line");
            conveyorGo.transform.SetPositionAndRotation(lab.ConveyorSpot.position, lab.ConveyorSpot.rotation);
            conveyorGo.transform.localScale = Vector3.one * 0.75f;
            var conveyor = conveyorGo.AddComponent<ConveyorView>();
            conveyor.Build(State, Palette, Access);

            var tvGo = new GameObject("Factory TV");
            var tv = tvGo.AddComponent<TVScreen>();

            // ---- Professor Quack stands beside the table and looks at real things ----
            // generated body only when explicitly requested (until a clean Design A model is approved)
            QuacksRig rig = null;
            // the generated Design A model is the default when it is in the build; -quackStandin forces the primitive stand-in
            var charName = ArgValue("-quackCharacter") ?? (HasArg("-quackStandin") ? null : "ProfessorQuack");
            if (charName != null)
            {
                rig = CharacterLoader.TryLoad(charName, Palette, out var report);
                Debug.Log("QUACK_CHARACTER " + report);
            }
            if (rig == null) rig = QuacksRig.BuildPlaceholder(null, Palette);
            rig.transform.SetPositionAndRotation(lab.QuackSpot.position, lab.QuackSpot.rotation);
            rig.transform.localScale = Vector3.one * 1.7f;   // ~1.8 m tall: stands well above the lab table
            anim = rig.gameObject.AddComponent<QuacksAnimator>();
            anim.Rig = rig; anim.Access = Access;
            anim.GazeTarget = () =>
            {
                var g = anim.Gaze;
                if (g == GazeTarget.Learner && State.Selected != null && speech != null && speech.Current == null) g = GazeTarget.SelectedEgg;
                switch (g)
                {
                    case GazeTarget.SelectedEgg when State.Selected != null: return plot.transform.TransformPoint(plot.LocalOf(State.Selected));
                    case GazeTarget.SelectedEgg:
                    case GazeTarget.Plot: return plotGo.transform.position;
                    case GazeTarget.SortingRule: return lab.ConsoleAnchor.position;
                    case GazeTarget.Conveyor: return lab.ConveyorSpot.position + Vector3.up * 0.6f;
                    case GazeTarget.Whiteboard: return lab.BoardAnchor.position;
                    default: return Cam.transform.position;
                }
            };

            // ---- tutor ----
            speech = gameObject.AddComponent<SpeechQueue>();
            speech.Animator = anim; speech.Access = Access;
            Tutor = gameObject.AddComponent<TutorClient>();
            Tutor.State = State;
            var voice = gameObject.AddComponent<VoiceClient>();
            voice.Tutor = Tutor; voice.Access = Access;
            var mouthAudio = rig.Head.gameObject.AddComponent<AudioSource>();
            mouthAudio.playOnAwake = false; mouthAudio.spatialBlend = 0.6f; mouthAudio.minDistance = 2f; mouthAudio.maxDistance = 15f;
            speech.Voice = voice; speech.Audio = mouthAudio;
            var tutor = gameObject.AddComponent<LocalTutor>();
            tutor.Init(State, speech);

            // ---- world panels + slim screen HUD ----
            // the sorting machine's control desk (dials, lever, steepness knob); the rule console is its screen
            var desk = new GameObject("Sorter control desk").AddComponent<ControlDesk>();
            desk.State = State; desk.Build(lab.transform, Palette);
            desk.ScreenAnchor.localScale = Vector3.one;   // the console canvas is sized to the desk's screen recess
            var console = gameObject.AddComponent<RuleConsole>();
            console.Build(State, Access, desk.ScreenAnchor, Cam);
            equation.Target = console.Equation; equation.Visible = equation.Visible;   // refresh into the console
            var tag = gameObject.AddComponent<EggTag>();
            tag.Build(State, plot, Access, Cam);
            var board = gameObject.AddComponent<WhiteboardView>();
            board.Build(speech, Access, lab.BoardAnchor, Cam);
            var training = gameObject.AddComponent<TrainingPanel>();
            training.Build(State, landscape, Access, lab.TrainingDeskAnchor, Cam);   // its own desk beside the landscape (stage E)
            Mission = gameObject.AddComponent<MissionBoard>();
            Mission.Build(Access, lab.MissionAnchor, Cam);
            Hud = gameObject.AddComponent<Hud>();
            Hud.Build(speech, Access);
            // panels that drift between the learner and the plot fade to a ghost (solid again under the mouse)
            var fader = gameObject.AddComponent<PanelFader>();
            fader.Cam = Cam; fader.Focus = plotGo.transform; fader.FocusSize = LabEnvironment.PlotSize; fader.Access = Access;
            fader.AlsoProtect(() => rig.transform.position + Vector3.up * 0.95f, new Vector3(0.7f, 1.9f, 0.7f));   // never hide Professor Quack
            fader.Add(training.Root);   // the mission screen has a fixed place above the plot
            tv.Build(Access, lab.TVAnchor, Cam);
            Hud.VoiceResting = () => voice.Resting;

            // ---- highlight whatever Professor Quack talks about ----
            var hl = gameObject.AddComponent<HighlightManager>();
            hl.Access = Access;
            hl.Register("plane", () => plane.SheetObjects);
            hl.Register("gimbal", () => plane.GimbalObjects);
            hl.Register("arrow", () => plane.ArrowObjects);
            hl.Register("equation", () => equation.Objects);
            hl.Register("bands", () => plane.BandObjects);
            hl.Register("axes", () => plot.AxisObjects(0).Concat(plot.AxisObjects(1)).Concat(plot.AxisObjects(2)));
            hl.Register("carton_jumbo", conveyor.JumboCarton.gameObject);
            hl.Register("carton_standard", conveyor.StandardCarton.gameObject);
            hl.Register("conveyor", conveyorGo);
            hl.Register("misrouted", () => plot.Eggs.Where(v => v.gameObject.activeInHierarchy && v.MisroutedMarker.activeSelf).Select(v => v.MisroutedMarker));
            hl.Register("selected_egg", () => State.Selected == null ? new GameObject[0] : new[] { plot.ViewOf(State.Selected)?.gameObject });
            hl.Register("landscape", landGo);
            hl.Register("training", () => new[] { training.Root.gameObject });
            hl.Register("console", () => new[] { console.Root.gameObject, desk.gameObject });
            hl.Register("whiteboard", () => new[] { board.Root.gameObject });
            hl.Register("mission", () => new[] { Mission.Root.gameObject });
            hl.Register("tv", () => new[] { tv.Root.gameObject });
            hl.Register("btn_action", () => new[] { Mission.ActionButton });
            hl.Register("score", () => new[] { Mission.Scoreboard });
            hl.Register("btn_next", () => new[] { Mission.NextButton });
            hl.Register("btn_step", () => new[] { Mission.StepButton });
            hl.Register("btn_skip", () => new[] { Mission.SkipButton });
            hl.Register("btn_end", () => new[] { Mission.EndButton });
            hl.Register("btn_hint", () => new[] { Hud.HintButton });
            hl.Register("btn_talk", () => new[] { Hud.TalkButton });
            speech.LineStarted += hl.FromLine;

            // ---- stations: Professor Quack walks to what he talks about; the desktop camera follows; VR gets pads ----
            stations = gameObject.AddComponent<StationDirector>();
            stations.Rig = rig; stations.Anim = anim; stations.Access = Access; stations.Palette = Palette;
            stations.Xr = () => xrRig;
            stations.Build(lab, plotGo.transform, landGo.transform);
            hl.Highlighted += stations.OnHighlight;

            // ---- input ----
            var input = gameObject.AddComponent<DesktopInput>();
            input.Cam = Cam; input.Plot = plot; input.PlaneView = plane; input.State = State; input.Access = Access;
            input.ActivityNoted = anim.NoteActivity;
            input.Pointer.Landscape = landscape;
            input.Focus = lab.PlotAnchor.position;
            input.BoardFocus = lab.BoardAnchor.position; input.TVFocus = lab.TVAnchor.position;
            Mission.ViewRequested = input.SetView;
            Hud.QuestionAsked = q => { State.Log("question", q); AskTutor("question", q, () => OfflineAnswer(q)); };
            Hud.HintRequested = () => { State.Log("hint", "requested"); AskTutor("hint", null, OfflineHint); };
            // push-to-talk: HUD button, V key, or the left grip in VR
            mic = gameObject.AddComponent<VoiceInput>();
            Hud.TalkPressed = BeginTalk; Hud.TalkReleased = EndTalk;
            input.TalkPressed = BeginTalk; input.TalkReleased = EndTalk;
            Hud.PauseToggled = TogglePause; input.PauseToggled = TogglePause;
            // pause menu
            Hud.LessonsRequested = () => { if (titleScreen != null) titleScreen.ShowRoadmapFromPause(); };
            Hud.RestartLessonRequested = () => { if (GamePaused) TogglePause(); Lesson.RestartCurrent(); };
            Hud.QuitToTitleRequested = () => Lesson.RestartRequested?.Invoke();
            State.SelectionChanged += _ => anim.NoteActivity();
            State.ParamsChanged += _ => anim.NoteActivity();

            // ---- lesson ----
            Lesson = gameObject.AddComponent<LessonDirector>();
            Lesson.Init(State, plot, speech, Mission, board, input, Access, (trigger, fallback) => AskTutor(trigger, null, fallback));
            Lesson.PlaneView = plane; Lesson.Console = console; Lesson.Tag = tag; Lesson.Desk = desk;
            Tutor.Speech = speech; Tutor.Introduced = Lesson.Introduced;
            Tutor.StageGoal = () => Lesson.CurrentGoal; Tutor.RemainingSteps = Lesson.RemainingSteps;
            Lesson.PlotRoot = plotGo.transform; Lesson.PlotHome = lab.PlotAnchor.position; Lesson.PlotAside = lab.PlotAsideAnchor.position;
            Lesson.Landscape = landscape; Lesson.Training = training; Lesson.HookTraining();
            Lesson.Hud = Hud;
            var shelf = new GameObject("Golden egg shelf").AddComponent<TrophyShelf>();
            shelf.Access = Access; shelf.Build(lab.transform, Palette);
            Lesson.StageCompleted += st => shelf.Add(Lesson.StageIndex);
            Lesson.Stations = stations; stations.Desktop = input;
            Lesson.RestartRequested = () => { Time.timeScale = 1; AudioListener.pause = false;
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex); };
            var meeting = gameObject.AddComponent<BoardMeeting>();
            meeting.State = State; meeting.Speech = speech; meeting.Desktop = input; meeting.TV = tv; meeting.Hud = Hud;
            meeting.Access = Access; meeting.Palette = Palette; meeting.TVAnchor = lab.TVAnchor; meeting.PlayAgain = Lesson.RestartRequested;
            Lesson.Meeting = meeting; meeting.Stations = stations;
            Hud.NextRequested = () => { Lesson.NoteActivity(); Lesson.NextStepRequested(); };
            input.ActivityNoted = () => { anim.NoteActivity(); Lesson.NoteActivity(); };
            State.SelectionChanged += _ => Lesson.NoteActivity();
            State.ParamsChanged += _ => Lesson.NoteActivity();
            // the guided intro plays on a normal start; automated runs, -quackStage and -quackSkipIntro go straight in
            string startStage = ArgValue("-quackStage");
            bool automated = Automated();
            stations.Suspended = automated;   // tours/smoke drive the camera themselves
            if (startStage == null && !automated)
            {
                void RunIntro()
                {
                    plotGo.SetActive(true); Mission.Root.gameObject.SetActive(true);
                    if (HasArg("-quackSkipIntro")) { stations.Suspended = false; tutor.Intro(); Lesson.Begin(null); return; }
                    var intro = gameObject.AddComponent<GuidedIntro>();
                    intro.State = State; intro.Plot = plot; intro.PlaneView = plane; intro.Console = console; intro.Speech = speech;
                    intro.Desktop = input; intro.TV = tv; intro.Highlight = hl; intro.Hud = Hud; intro.Access = Access; intro.Palette = Palette;
                    intro.Showcase = lab.ShowcaseAnchor; intro.Voice = voice;
                    intro.Quack = rig.transform; intro.Stations = stations; intro.MissionScreen = Mission.Root;
                    intro.HideDuringIntro = new[] { training.Root.gameObject, landGo, console.Root.gameObject, desk.gameObject };
                    stations.Suspended = true;
                    StartCoroutine(intro.Run(() => { stations.Suspended = false; Lesson.Begin(null); }));
                }

                // title scene first: a clean room (no holograms or panels yet), Professor Quack waving, Start / Lessons / Settings
                stations.Suspended = true;
                plotGo.SetActive(false); Mission.Root.gameObject.SetActive(false);
                training.Root.gameObject.SetActive(false); landGo.SetActive(false); console.Root.gameObject.SetActive(false); desk.gameObject.SetActive(false);
                var start = gameObject.AddComponent<StartScreen>();
                start.Desktop = input; start.Quack = anim; start.Access = Access; start.Hud = Hud;
                start.Palette = Palette;
                start.Build(Lesson.LessonList);
                titleScreen = start;
                start.OnStart = RunIntro;
                start.OnJump = id =>
                {
                    if (GamePaused) TogglePause();
                    plotGo.SetActive(true); Mission.Root.gameObject.SetActive(true);
                    stations.Suspended = false;
                    Lesson.Begin(id);
                };
                start.Show();
                var uiShots = ArgValue("-quackUiShots");
                if (uiShots != null) StartCoroutine(UiShots(uiShots, start));
                var introShots = ArgValue("-quackIntroShots");
                if (introShots != null) StartCoroutine(IntroShots(introShots, start));
                var roomShots = ArgValue("-quackRoomShots");
                if (roomShots != null) StartCoroutine(RoomShots(roomShots, start));
            }
            else
            {
                tutor.Intro();
                Lesson.Begin(startStage);
            }

            if (XRBoot.Requested)
                StartCoroutine(XRBoot.Start(ok =>
                {
                    if (!ok) return;
                    input.enabled = false;
                    var xr = XRRig.Build(Cam, lab.LearnerSpot.position, plotGo.transform.position, Palette.Line, Palette.Highlight);
                    xrRig = xr; stations.EnableVr();
                    if (titleScreen != null)   // the title becomes a big floating screen in front of the learner
                        titleScreen.MakeWorldSpace(Cam, lab.LearnerSpot.position + new Vector3(0, 1.55f, 1.6f), Quaternion.identity, 2.2f);
                    xr.Pointer = input.Pointer; xr.PlotRoot = plotGo.transform; xr.Access = Access; xr.ActivityNoted = () => { anim.NoteActivity(); Lesson.NoteActivity(); };
                    xr.TalkPressed = BeginTalk; xr.TalkReleased = EndTalk;
                    xr.PauseToggled = TogglePause;
                    // captions become a low panel in front of the learner, angled up for comfortable reading
                    Hud.MakeWorldSpace(Cam, lab.LearnerSpot.position + new Vector3(0, 0.95f, 0.45f), Quaternion.Euler(35, 0, 0), 0.95f);
                    foreach (var tm in FindObjectsByType<Billboard>(FindObjectsSortMode.None)) tm.Cam = null;
                    Debug.Log("QUACK_XR_READY");
                }));
            Debug.Log($"QUACK_READY eggs={plot.Eggs.Count} misrouted={State.TrainResult.Misrouted} stage={State.Stage} params={State.Params}");

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-quackSmoke") >= 0)
                StartCoroutine(SmokeTest());

            var tourDir = VisualTour.RequestedDir();
            if (tourDir != null)
            {
                var tour = gameObject.AddComponent<VisualTour>();
                tour.Game = this; tour.Plot = plot; tour.PlaneView = plane; tour.Cam = Cam;
                tour.Run(tourDir);
            }
        }

        /// <summary>
        /// Character studio (-quackPortrait &lt;dir&gt;): a neutral floor and backdrop, three-point light, and Professor
        /// Quack alone at the origin facing the camera. CharacterPortrait then shoots views, expressions, gaze,
        /// beak, wings and bow tie. Nothing else from the game is built.
        /// </summary>
        void RunStudio(string dir)
        {
            var floor = VizUtil.Primitive(PrimitiveType.Cube, "studio floor", null, Palette.LabWall);
            floor.transform.position = new Vector3(0, -0.05f, 0); floor.transform.localScale = new Vector3(12, 0.1f, 12);
            var back = VizUtil.Primitive(PrimitiveType.Cube, "studio backdrop", null, Palette.LabWall);
            back.transform.position = new Vector3(0, 3, -4); back.transform.localScale = new Vector3(12, 6, 0.1f);
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = new Color(0.82f, 0.8f, 0.78f);
            foreach (var (pos, intensity) in new[] { (new Vector3(2.5f, 3f, 3f), 1.4f), (new Vector3(-3f, 2f, 2f), 0.7f), (new Vector3(0, 3f, -2.5f), 0.8f) })
            {
                var l = new GameObject("studio light").AddComponent<Light>();
                l.type = LightType.Point; l.transform.position = pos; l.range = 10; l.intensity = intensity; l.color = new Color(1f, 0.96f, 0.9f);
            }
            QuacksRig rig = null;
            var charName = ArgValue("-quackCharacter") ?? (HasArg("-quackStandin") ? null : "ProfessorQuack");
            if (charName != null) { rig = CharacterLoader.TryLoad(charName, Palette, out var report); Debug.Log("QUACK_CHARACTER " + report); }
            if (rig == null) rig = QuacksRig.BuildPlaceholder(null, Palette);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one * 1.7f;
            anim = rig.gameObject.AddComponent<QuacksAnimator>();
            anim.Rig = rig; anim.Access = Access ?? AccessSettings.Load();
            if (dir == null)
            {
                var viewer = gameObject.AddComponent<CharacterViewer>();   // interactive review (-quackViewer)
                viewer.Anim = anim; viewer.Cam = Cam;
                viewer.Build();
                return;
            }
            var portrait = gameObject.AddComponent<CharacterPortrait>();
            portrait.Anim = anim; portrait.Cam = Cam;
            portrait.Run(dir);
        }

        /// <summary>Screenshots of the menus (-quackUiShots &lt;dir&gt;): title, roadmap, settings, then in-game pause.</summary>
        System.Collections.IEnumerator UiShots(string dir, StartScreen start)
        {
            System.IO.Directory.CreateDirectory(dir);
            int n = 0;
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{++n:00}_{name}.png"));
                yield return new WaitForSecondsRealtime(0.4f);
            }
            yield return new WaitForSecondsRealtime(3f); yield return Shot("title");
            start.ShowMap(); yield return new WaitForSecondsRealtime(1f); yield return Shot("roadmap");
            start.HideMap(); Hud.OpenSettings(); yield return new WaitForSecondsRealtime(0.6f); yield return Shot("settings");
            Hud.ToggleSettings();
            start.Dismiss();
            var tvs = FindAnyObjectByType<TVScreen>(); var dk = FindAnyObjectByType<DesktopInput>();
            if (tvs && dk) { dk.UserSteered = true; dk.SetView(7); StartCoroutine(tvs.PlayStory(3f, () => false)); yield return new WaitForSecondsRealtime(4.5f); yield return Shot("intro_video_on_tv"); }
            start.OnJump?.Invoke("B_build_rule");
            yield return new WaitForSecondsRealtime(4f); yield return Shot("lesson2_rule_view");
            { var cd = FindAnyObjectByType<ControlDesk>(); var di = FindAnyObjectByType<DesktopInput>();
              if (cd && di) { di.UserSteered = true; di.LookAt(cd.ScreenAnchor.parent.position, 0, 38, 0.95f, 150); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("desk_closeup"); } }
            TogglePause(); yield return new WaitForSecondsRealtime(0.6f); yield return Shot("pause");
            TogglePause();
            var desk = FindAnyObjectByType<DesktopInput>();
            if (desk) { desk.UserSteered = true; desk.SetView(7); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("tv"); desk.SetView(6); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("whiteboard");
                Lesson.ShowRecapForShots(5); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("whiteboard_lesson6");
                var conv = FindAnyObjectByType<ConveyorView>();
                if (conv) { Egg big = null; foreach (var e in State.Dataset.Train) if (e.Label == 1) { big = e; break; }
                    if (big != null) State.Select(big);
                    desk.LookAt(conv.JumboCarton.position + Vector3.up * 0.1f, 20, 35, 0.9f, 151); yield return new WaitForSecondsRealtime(3f); yield return Shot("egg_in_carton"); } desk.SetView(0); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("room_overview"); }
            Access.ReadingPace = 1f; Access.VoiceSpeed = 2f;   // screenshot run only (not saved): get to the confetti quickly
            speech.ClearAll();
            Lesson.Finale();
            yield return new WaitForSecondsRealtime(7f); yield return Shot("finale_board_meeting");
            var tvFin = tvs;
            for (float w = 0; w < 20f && tvFin && !tvFin.PlayedFinale; w += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(4f); yield return Shot("finale_clip_on_tv");
            yield return new WaitForSecondsRealtime(8f); yield return Shot("finale_confetti");
            yield return new WaitForSecondsRealtime(6f); yield return Shot("finale_card");
            Debug.Log($"QUACK_UISHOTS DONE shots={n}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>Screenshots through the real guided intro (-quackIntroShots &lt;dir&gt;), with fast captions for the run.</summary>
        System.Collections.IEnumerator IntroShots(string dir, StartScreen start)
        {
            System.IO.Directory.CreateDirectory(dir);
            Access.ReadingPace = 1f; Access.VoiceSpeed = 2f;   // this run only (not saved)
            yield return new WaitForSecondsRealtime(2f);
            start.Dismiss(); start.OnStart?.Invoke();
            int n = 0;
            for (float t = 0; t < 75f; t += 5f)
            {
                yield return new WaitForSecondsRealtime(5f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{++n:00}_intro_{(int)t + 5:00}s.png"));
            }
            Debug.Log($"QUACK_INTROSHOTS DONE shots={n}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>Clean reference shots of the room (-quackRoomShots &lt;dir&gt;): no HUD, captions or menus.</summary>
        System.Collections.IEnumerator RoomShots(string dir, StartScreen start)
        {
            System.IO.Directory.CreateDirectory(dir);
            int n = 0;
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{++n:00}_{name}.png"), 2);   // 2x resolution
                yield return new WaitForSecondsRealtime(0.6f);
            }
            yield return new WaitForSecondsRealtime(2f);
            start.Dismiss(); start.OnJump?.Invoke("B_build_rule");
            yield return new WaitForSecondsRealtime(3f);
            Hud.Canvas.enabled = false; Hud.Overlay.enabled = false; speech.ClearAll();
            var desk = FindAnyObjectByType<DesktopInput>(); desk.UserSteered = true;
            stations.Suspended = true;
            void View(Vector3 focus, float yaw, float pitch, float dist) { desk.SlowNext = false; desk.LookAt(focus, yaw, pitch, dist, 400); }
            View(new Vector3(0, 1.4f, 0.3f), 0, 8, 4.6f); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("room_wide_front");
            View(new Vector3(0, 1.3f, 0.5f), -28, 12, 4.4f); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("room_three_quarter_left");
            View(new Vector3(0, 1.3f, 0.5f), 28, 12, 4.4f); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("room_three_quarter_right");
            View(new Vector3(2.1f, 0.7f, 1.6f), 25, 16, 2.2f); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("sorting_line_and_trays");
            View(new Vector3(-1.9f, 1.0f, 0.7f), -20, 4, 2.3f); yield return new WaitForSecondsRealtime(2.5f); yield return Shot("professor_quack_in_room");
            Debug.Log($"QUACK_ROOMSHOTS DONE shots={n}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;
        static bool Automated() => HasArg("-quackSmoke") || HasArg("-batchmode") || VisualTour.RequestedDir() != null || CharacterPortrait.RequestedDir() != null;

        void UpdateProgress()
        {
            if (Lesson == null || Hud == null) return;
            var st = Lesson.Current;
            Hud.SetProgress(Lesson.StageIndex, Lesson.StageCount, st?.Title ?? "", Lesson.StageProgress(), Lesson.JourneyProgress(), Lesson.CurrentCompleted);
        }

        static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>
        /// Headless check of the core loop (launch with -quackSmoke): select a misrouted egg, slide the plane
        /// through it using the same command the drag handle uses, and verify prediction, plane and tutor respond.
        /// </summary>
        System.Collections.IEnumerator SmokeTest()
        {
            yield return null;
            var egg = State.Dataset.Train.Find(e => State.IsMisrouted(e));
            State.Select(egg);
            int before = State.Prediction(egg);
            double dist = PlaneGeometry.SignedDistance(State.Params, State.Vector(egg));
            State.BeginEdit();
            State.SetParams(PlaneGeometry.Translate(State.Params, dist + (before == 1 ? 0.05 : -0.05)), ParamSource.PlaneTranslate);
            State.EndEdit(ParamSource.PlaneTranslate);
            int after = State.Prediction(egg);
            yield return new WaitForSeconds(2f);
            bool ok = egg != null && before != after && after == egg.Label && speech.History.Count > 0;
            Debug.Log($"QUACK_SMOKE {(ok ? "PASS" : "FAIL")} egg=#{egg?.Id} label={egg?.Label} pred {before}->{after} " +
                      $"misrouted={State.TrainResult.Misrouted} tutorLines={speech.History.Count} stage={State.Stage} " +
                      $"lastAction={State.RecentActions[State.RecentActions.Count - 1].Kind}");
            Application.Quit(ok ? 0 : 1);
        }

        VoiceInput mic;
        bool talking;

        string liveText;
        float nextSnapshot;
        bool transcribing;

        void Update()
        {
            UpdateProgress();
            if (!talking || mic == null) return;
            Hud.SetMicLevel(mic.Level(), liveText);
            // every ~2.5 s send the audio so far for a quick transcript, so the learner sees their words arriving
            if (!transcribing && Time.unscaledTime >= nextSnapshot)
            {
                nextSnapshot = Time.unscaledTime + 2.5f;
                var wav = mic.Snapshot();
                if (wav != null)
                {
                    transcribing = true;
                    StartCoroutine(RunTranscribe(wav));
                }
            }
        }

        System.Collections.IEnumerator RunTranscribe(byte[] wav)
        {
            yield return Tutor.Transcribe(wav, t => { if (talking) liveText = t; });
            transcribing = false;
        }
        public bool GamePaused { get; private set; }

        /// <summary>Freeze the whole game (time, animation, training, Quack's voice); UI stays usable to resume.</summary>
        void TogglePause()
        {
            GamePaused = !GamePaused;
            Time.timeScale = GamePaused ? 0f : 1f;
            AudioListener.pause = GamePaused;
            speech.Paused = GamePaused;
            Hud.SetGamePaused(GamePaused);
            State.Log("pause", GamePaused ? "on" : "off");
        }

        /// <summary>Learner starts speaking: Quack stops talking, looks at them and listens.</summary>
        void BeginTalk()
        {
            if (talking || Tutor.Busy) return;
            if (!mic.Begin())
            {
                speech.Say(new TutorLine("I can't hear you: no microphone is available. Check that Windows lets desktop apps use the microphone, or type your question.",
                    Emotion.Concerned, Gesture.Shrug, TutorPriority.PlayerQuestion));
                return;
            }
            talking = true;
            liveText = null; nextSnapshot = Time.unscaledTime + 2.0f;
            speech.Interrupt();
            anim.SetEmotion(Emotion.Curious);
            anim.Gaze = GazeTarget.Learner;
            anim.NoteActivity();
            Hud.SetListening(true);
            State.Log("voice", "talking");
        }

        /// <summary>Learner released: send the recording with the game state; Quack answers aloud.</summary>
        void EndTalk()
        {
            if (!talking) return;
            talking = false;
            Hud.SetListening(false);
            var wav = mic.End();
            if (wav == null)
            {
                speech.Say(new TutorLine("Sorry, I couldn't hear anything. Check that a microphone is connected and not muted, then hold the button while you speak.",
                    Emotion.Curious, Gesture.None, TutorPriority.PlayerQuestion));
                return;
            }
            Hud.SetThinking(true);
            anim.SetEmotion(Emotion.Thinking);
            Tutor.AskVoice(wav,
                line =>
                {
                    Hud.SetThinking(false);
                    if (!string.IsNullOrEmpty(Tutor.LastHeard)) { speech.LogLearner(Tutor.LastHeard); State.Log("question", Tutor.LastHeard); }
                    speech.Say(line);
                },
                err =>
                {
                    Hud.SetThinking(false);
                    speech.Say(new TutorLine("My AI consultant is unreachable right now, so I couldn't hear that properly. Try typing, or press Hint.",
                        Emotion.Concerned, Gesture.Shrug, TutorPriority.PlayerQuestion) { Source = "offline" });
                });
        }

        /// <summary>Ask the Gemini tutor; show a visible thinking state; fall back to local text on any failure.</summary>
        void AskTutor(string trigger, string question, System.Func<TutorLine> fallback)
        {
            if (Tutor.Busy) return;
            Hud.SetThinking(true);
            anim.SetEmotion(Emotion.Thinking);
            Tutor.Ask(trigger, question,
                line => { Hud.SetThinking(false); speech.Say(line); },
                err =>
                {
                    Hud.SetThinking(false);
                    var line = fallback();
                    line.Source = "offline";
                    speech.Say(line);
                });
        }

        TutorLine OfflineHint()
        {
            var sel = State.Selected;
            string a = sel != null && State.IsMisrouted(sel)
                ? $"Egg #{sel.Id} is on the wrong side. Look at its width compared with its neighbours: should the sheet lean that way?"
                : "Pick an egg marked X. Ask yourself which way the sheet must tilt to fix it without pushing its neighbours across.";
            return new TutorLine(a, Emotion.Thinking, Gesture.PointAtPlot, TutorPriority.PlayerQuestion);
        }

        /// <summary>Offline fallback when the tutor backend is unreachable: honest, contextual, short.</summary>
        TutorLine OfflineAnswer(string q)
        {
            string s = q.ToLowerInvariant();
            string a;
            if (s.Contains("weight") || s.Contains(" w") || s.Contains("arrow"))
                a = "The weights are the arrow. It points to the JUMBO side and is perpendicular to the sheet. A bigger weight on a feature means that measurement pushes the score harder.";
            else if (s.Contains("plane") || s.Contains("sheet") || s.Contains("boundary"))
                a = "The sheet is every point where the score is exactly zero, which is p = 0.5. Eggs on the arrow side get JUMBO. One sheet, one rule, every egg.";
            else if (s.Contains("prob") || s.Contains("confiden") || s.Contains("sure"))
                a = "Probability comes from the score through the sigmoid. Far from the sheet means close to 0 or 1, and on the sheet it's 0.5. It's the model's estimate, not a guarantee.";
            else if (s.Contains("standard") || s.Contains("normal") || s.Contains("scale"))
                a = "Each axis is standardised: subtract the average, divide by the spread. That way grams and millimetres are comparable, and the angles you see are the real ones.";
            else
                a = "Good question, but my AI consultant is unreachable right now. For now: pick a misrouted egg (marked X) and ask yourself which way the sheet must move to fix it without breaking its neighbours.";
            return new TutorLine(a, Emotion.Encouraging, Gesture.Nod, TutorPriority.PlayerQuestion);
        }
    }
}
