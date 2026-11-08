using System.Collections;
using System.IO;
using Quack.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Automated visual tour (launch with -quackTour &lt;outputDir&gt;). Drives the REAL desktop input code
    /// through a virtual mouse/keyboard, saves a screenshot after each step, logs state, then quits.
    /// Lets the team (and remote tooling) check layout, rendering and interactions without a human driver.
    /// </summary>
    public sealed class VisualTour : MonoBehaviour
    {
        public QuackGame Game;
        public FeaturePlot Plot;
        public DecisionPlaneView PlaneView;
        public Camera Cam;

        string outDir;
        Mouse mouse;
        Keyboard kb;
        int shot;
        int failures;

        public static string RequestedDir()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // browsers have no command line: ?quackTour in the page URL; shots are uploaded, not written to disk
            var url = Application.absoluteURL ?? "";
            if (url.Contains("quackTour")) return "web";
#endif
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-quackTour");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public void Run(string dir)
        {
            outDir = dir;
            if (dir != "web") Directory.CreateDirectory(outDir);
            StartCoroutine(Tour());
        }

        IEnumerator Tour()
        {
            // keep receiving (virtual) input even if someone focuses another window on the test machine
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("QuackTourMouse");
            kb = InputSystem.AddDevice<Keyboard>("QuackTourKeyboard");
            mouse.MakeCurrent(); kb.MakeCurrent();
            var s = Game.State;
            ResetAccess();
            yield return new WaitForSeconds(2.5f);
            yield return Shot("start_3d_view");

            // 1) click a visible misrouted egg
            EggView target = null;
            foreach (var v in Plot.Eggs)
            {
                if (!s.IsMisrouted(v.Egg)) continue;
                var sp = Cam.WorldToScreenPoint(v.transform.position);
                if (sp.z > 0 && Physics.Raycast(Cam.ScreenPointToRay(sp), out var hit) && hit.collider.gameObject == v.gameObject) { target = v; break; }
            }
            Check(target != null, "found a clickable misrouted egg");
            if (target != null)
            {
                yield return Click(Cam.WorldToScreenPoint(target.transform.position));
                Check(s.Selected == target.Egg, $"click selected egg #{target.Egg.Id}");
            }
            yield return new WaitForSeconds(1.5f);
            yield return Shot("A_egg_selected_2D");

            // stage A: inspect two more eggs (Tab cycles), then add the width measurement from the mission board
            Check(s.Stage == "A_describe_eggs", $"lesson starts in stage A (stage={s.Stage})");
            yield return Press(Key.Tab); yield return Press(Key.Tab);
            yield return new WaitForSeconds(0.5f);
            Check(s.WidthLocked, "width is locked during the 2-feature introduction");
            yield return ClickButton("button Measure width too (3rd feature)");
            yield return new WaitForSeconds(2.2f);
            Check(!s.WidthLocked && Plot.DepthScale > 0.99f, "width unlocked and eggs spread into 3D");
            Check(Game.Lesson.CurrentCompleted, "stage A completed");
            yield return Shot("A_width_revealed_3D");
            yield return ClickButton("button Next stage");
            yield return new WaitForSeconds(1.0f);
            Check(s.Stage == "B_build_rule", $"advanced to stage B (stage={s.Stage})");
            yield return Shot("B_stage_start");

            // 2) preset views
            foreach (var (key, name) in new[] { (Key.Digit1, "view1_mass_length"), (Key.Digit2, "view2_mass_width"), (Key.Digit3, "view3_length_width"), (Key.Digit5, "view5_edge_on") })
            {
                yield return Press(key);
                yield return new WaitForSeconds(1.0f);
                yield return Shot(name);
            }
            yield return Press(Key.Digit4);
            yield return new WaitForSeconds(1.0f);
            yield return Press(Key.Digit0);
            yield return new WaitForSeconds(1.0f);
            yield return Shot("lab_overview");
            yield return Press(Key.Digit4);
            yield return new WaitForSeconds(1.0f);

            // 3) drag the rotate handle (tilt the plane)
            var before = s.Params.Clone();
            int misBefore = s.TrainResult.Misrouted;
            var tipWorld = Plot.transform.TransformPoint(PlaneView.PivotLocal + PlaneView.NormalLocal * PlaneView.ArrowLength);
            Vector2 tip = Cam.WorldToScreenPoint(tipWorld);
            yield return DragFrom(tip, tip + new Vector2(-60, 120), 20);
            Check(!SameDir(before, s.Params), "rotate drag changed the plane's direction");
            Check(System.Math.Abs(before.WeightNorm() - s.Params.WeightNorm()) < 1e-6, "rotate drag kept steepness");
            Debug.Log($"QUACK_TOUR rotate misrouted {misBefore}->{s.TrainResult.Misrouted} params={s.Params}");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("after_tilt");

            // 4) drag the translate disc (slide the plane)
            before = s.Params.Clone();
            Vector2 disc = Cam.WorldToScreenPoint(Plot.transform.TransformPoint(PlaneView.PivotLocal));
            var nScreen = (Vector2)Cam.WorldToScreenPoint(Plot.transform.TransformPoint(PlaneView.PivotLocal + PlaneView.NormalLocal)) - disc;
            yield return DragFrom(disc, disc + nScreen.normalized * 80, 15);
            Check(System.Math.Abs(before.B - s.Params.B) > 1e-4 && SameDir(before, s.Params), "translate drag moved the plane along its normal");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("after_slide");

            // 5) UI: open settings, bump text size, open history
            yield return ClickButton("button Accessibility settings");
            yield return ClickButton("button Text +");
            yield return ClickButton("button Text +");
            yield return ClickButton("button History");
            yield return new WaitForSeconds(1.0f);
            yield return Shot("settings_bigger_text_history");
            yield return ClickButton("button History");                  // close it again
            yield return ClickButton("button Accessibility settings");

            // 6) ask a question through the text box path; expect a Gemini (or mock) reply, not the offline fallback
            var speechQ = Game.GetComponent<SpeechQueue>();
            int before6 = speechQ.History.Count;
            Game.Hud.QuestionAsked?.Invoke("why is the arrow perpendicular to the plane?");
            float waited = 0;
            TutorLine reply = null;
            while (waited < 25f && reply == null)
            {
                yield return new WaitForSeconds(0.5f); waited += 0.5f;
                for (int i = before6; i < speechQ.History.Count; i++)
                    if (speechQ.History[i].Priority == TutorPriority.PlayerQuestion) reply = speechQ.History[i];
            }
            Check(reply != null, $"tutor answered the typed question ({waited:0.0}s)");
            if (reply != null)
            {
                Check(reply.Source == "gemini" || reply.Source == "mock", $"answer came from the backend (source={reply.Source})");
                Debug.Log($"QUACK_TOUR answer [{reply.Source}] emotion={reply.Emotion} gesture={reply.Gesture} gaze={reply.Gaze}: {reply.Speech}");
            }
            yield return new WaitForSeconds(1.5f);
            yield return Shot("question_answer");

            // 7) finish stage B through the console path (a learner could set these numbers with the sliders)
            ResetAccess();
            var good = new ModelParams(new[] { 1.1, -1.4, 2.6 }, -0.5);
            s.BeginEdit(); s.SetParams(PlaneGeometry.SetSteepness(good, 1.2), ParamSource.Slider); s.EndEdit(ParamSource.Slider);
            yield return new WaitForSeconds(0.5f);
            Check(Game.Lesson.CurrentCompleted, $"stage B completed at {s.TrainResult.Accuracy:P0}");
            yield return ClickButton("button Next stage");
            yield return new WaitForSeconds(1.0f);
            Check(s.Stage == "C_measure_mistakes", $"advanced to stage C (stage={s.Stage})");

            // stage C: select a mistake, drag the real steepness slider on the world console, then loss below 0.30
            EggView mistake = null;
            foreach (var v in Plot.Eggs) if (s.IsMisrouted(v.Egg)) { mistake = v; break; }
            Check(mistake != null, "a misrouted egg exists to inspect");
            if (mistake != null) s.Select(mistake.Egg);
            var steepSlider = FindSlider("steepness |w|");
            Check(steepSlider != null, "found the steepness slider on the world console");
            if (steepSlider != null)
            {
                var srt = (RectTransform)steepSlider.transform;
                Vector2 a = Cam.WorldToScreenPoint(srt.TransformPoint(new Vector3(srt.rect.xMin + srt.rect.width * 0.2f, srt.rect.center.y, 0)));
                Vector2 b = Cam.WorldToScreenPoint(srt.TransformPoint(new Vector3(srt.rect.xMin + srt.rect.width * 0.45f, srt.rect.center.y, 0)));
                yield return DragFrom(a, b, 12);
            }
            yield return new WaitForSeconds(0.6f);
            yield return Shot("C_bands_and_loss");
            // settle near the optimum (what gradient descent will find in stage E) to satisfy the loss goal
            var tr = new Trainer(s.Space.Train, s.Params);
            for (int i = 0; i < 300; i++) tr.Step(1.0);
            s.BeginEdit(); s.SetParams(tr.Params, ParamSource.Slider); s.EndEdit(ParamSource.Slider);
            yield return new WaitForSeconds(0.6f);
            Check(Game.Lesson.CurrentCompleted, $"stage C completed (loss {LogisticModel.DataLoss(s.Params, s.Space.Train):0.000})");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("C_complete_recap");

            // 8) stage D: change a held parameter (b), then drag the real ball to the slice minimum
            yield return ClickButton("button Next stage");
            yield return new WaitForSeconds(2.0f);
            Check(s.Stage == "D_landscape", $"advanced to stage D (stage={s.Stage})");
            var land = Game.Lesson.Landscape;
            Check(land != null && land.gameObject.activeInHierarchy, "parameter-space hologram is visible");
            yield return Shot("D_feature_vs_parameter_space");
            var pb = s.Params.Clone(); pb.B += 0.4;
            s.BeginEdit(); s.SetParams(pb, ParamSource.Slider); s.EndEdit(ParamSource.Slider);
            yield return new WaitForSeconds(0.5f);
            if (land != null)
            {
                Vector2 from = Cam.WorldToScreenPoint(land.Ball.position);
                Vector2 to = Cam.WorldToScreenPoint(land.MinFlag.position - Vector3.up * 0.03f);
                yield return DragFrom(from, to, 25);
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"QUACK_TOUR landscape loss {land.CurrentObjective:0.0000} slice min {land.SliceMin:0.0000}");
            }
            Check(Game.Lesson.CurrentCompleted, "stage D completed (ball dragged to the slice minimum)");
            yield return Shot("D_ball_at_minimum");

            // 9) stage E: a too-big learning rate overshoots; a moderate one converges
            yield return ClickButton("button Next stage");
            yield return new WaitForSeconds(2.0f);
            Check(s.Stage == "E_gradient_descent", $"advanced to stage E (stage={s.Stage})");
            var panel = Game.Lesson.Training;
            panel.SetLearningRate(50);
            for (int i = 0; i < 4; i++) { yield return ClickButton("button Step"); yield return new WaitForSeconds(0.25f); }
            yield return Shot("E_big_learning_rate");
            yield return ClickButton("button Reset to broken sorter");
            panel.SetLearningRate(1.0);
            yield return ClickButton("button Play");
            yield return new WaitForSeconds(14f);
            Check(Game.Lesson.CurrentCompleted, $"stage E completed (loss {LogisticModel.Objective(s.Params, s.Space.Train):0.0000})");
            yield return Shot("E_converged_trail");

            // 10) stage F: held-out evaluation and the shipment
            yield return ClickButton("button Next stage");
            yield return new WaitForSeconds(1.5f);
            Check(s.Stage == "F_test_and_scale", $"advanced to stage F (stage={s.Stage})");
            Check(Plot.TestEggsVisible, "held-out eggs are visible");
            yield return Shot("F_held_out_eggs");
            yield return ClickButton("button Evaluate on held-out eggs");
            yield return new WaitForSeconds(1.0f);
            yield return ClickButton("button Run the next shipment (10,000 eggs)");
            yield return new WaitForSeconds(2.0f);
            Check(Game.Lesson.CurrentCompleted, "stage F completed (evaluated + shipment)");
            yield return Shot("F_mission_complete");

            ResetAccess();   // the tour must not leave changed preferences on the machine
            Debug.Log($"QUACK_TOUR DONE shots={shot} failures={failures}");
            if (outDir == "web") yield return Report($"DONE shots={shot} failures={failures}");
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(kb);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        void ResetAccess()
        {
            var a = Game.Access;
            a.TextScale = 1f; a.ReadingPace = 1f; a.CaptionOpacity = 0.85f; a.ReducedMotion = false; a.LessChatter = false;
            a.Apply();
        }

        IEnumerator Report(string line)
        {
            using var r = UnityEngine.Networking.UnityWebRequest.PostWwwForm(Game.Tutor.BaseUrl + "/test-log", "");
            r.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(line));
            yield return r.SendWebRequest();
        }

        static bool SameDir(ModelParams a, ModelParams b)
        {
            double na = a.WeightNorm(), nb = b.WeightNorm(), dot = 0;
            for (int j = 0; j < a.Dim; j++) dot += a.W[j] * b.W[j];
            return dot / (na * nb) > 1 - 1e-9;
        }

        void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"QUACK_TOUR {(ok ? "OK  " : "FAIL")} {what}");
            if (outDir == "web") StartCoroutine(Report($"{(ok ? "OK  " : "FAIL")} {what}"));
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = $"{++shot:00}_{name}";
            if (outDir == "web")
            {
                // canvas only (never the desktop): encode the game frame and upload it to the local backend
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                Destroy(tex);
                var tutor = Game.Tutor;
                using var up = new UnityEngine.Networking.UnityWebRequest(tutor.BaseUrl + "/test-shot?name=" + file, "POST")
                {
                    uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(png),
                    downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer(),
                };
                up.SetRequestHeader("Content-Type", "image/png");
                yield return up.SendWebRequest();
                Debug.Log($"QUACK_TOUR shot {file} upload={up.result}");
                yield break;
            }
            string path = Path.Combine(outDir, file + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null; yield return null;
            Debug.Log($"QUACK_TOUR shot {path}");
        }

        IEnumerator Move(Vector2 p, bool down)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p, buttons = (ushort)(down ? 1 : 0) });
            yield return null;
        }

        IEnumerator Click(Vector2 p)
        {
            yield return Move(p, false);
            yield return Move(p, true);
            yield return Move(p, false);
            yield return null;
        }

        IEnumerator DragFrom(Vector2 a, Vector2 b, int steps)
        {
            yield return Move(a, false);
            yield return Move(a, true);
            for (int i = 1; i <= steps; i++) yield return Move(Vector2.Lerp(a, b, i / (float)steps), true);
            yield return Move(b, false);
            yield return null;
        }

        IEnumerator Press(Key k)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        static UnityEngine.UI.Slider FindSlider(string label)
        {
            foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                if (t.text == label)
                {
                    var row = t.transform.parent;
                    var sl = row.GetComponentInChildren<UnityEngine.UI.Slider>();
                    if (sl != null) return sl;
                }
            return null;
        }

        IEnumerator ClickButton(string goName)
        {
            var go = GameObject.Find(goName);
            Check(go != null, "found UI " + goName);
            if (go == null) yield break;
            var rt = (RectTransform)go.transform;
            var canvas = go.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode == RenderMode.WorldSpace ? Cam : null;   // world panels project through the camera
            Vector2 center = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            if (cam != null && (center.x < 0 || center.y < 0 || center.x > Screen.width || center.y > Screen.height ||
                                cam.WorldToScreenPoint(rt.position).z < 0))
            {
                // the camera is looking at a wall object (e.g. the whiteboard recap): back to the lab overview first
                yield return Press(Key.Digit0);
                yield return new WaitForSeconds(1.2f);
                center = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            }
            yield return Click(center);
            yield return new WaitForSeconds(0.3f);
        }
    }
}
