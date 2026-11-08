using System.Collections;
using System.IO;
using Quack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Records a ~80 s gameplay trailer from the real game (-quackTrailer &lt;dir&gt;): a scripted run through the title, the
    /// intro video, the egg cloud, a player turning the desk dials, probability bands, parameter space, gradient descent,
    /// the unseen-eggs test and the board-meeting finale. Frames are rendered at a fixed 30 fps (Time.captureFramerate),
    /// so the result is smooth however slowly the machine renders; frames are saved as JPEGs for ffmpeg.
    /// Everything shown is the live simulation: the dial turns change the real parameters.
    /// </summary>
    public sealed class TrailerDirector : MonoBehaviour
    {
        public QuackGame Game;
        public StartScreen Start;
        public string Dir;
        public static bool Active => System.Environment.CommandLine.Contains("-quackTrailer");
        const int Fps = 30;
        int frame;
        bool recording;
        RectTransform cursor;

        public void Run() { StartCoroutine(Record()); StartCoroutine(Script()); }

        IEnumerator Record()
        {
            Directory.CreateDirectory(Dir);
            Time.captureFramerate = Fps;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (!recording) continue;
                if (frame > Fps * 110) { recording = false; Debug.Log("QUACK_TRAILER cap reached"); Application.Quit(0); yield break; }   // never run away
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(Dir, $"f{frame++:00000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
            }
        }

        static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

        void BuildCursor()
        {
            var go = new GameObject("trailer cursor", typeof(Canvas), typeof(CanvasScaler));
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 200;
            var ring = new GameObject("ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(go.transform, false);
            cursor = (RectTransform)ring.transform; cursor.sizeDelta = new Vector2(46, 46);
            var img = ring.GetComponent<Image>(); img.sprite = UiIcons.Ring; img.color = new Color(1f, 0.85f, 0.3f, 0.95f); img.raycastTarget = false;
            var dot = new GameObject("dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(ring.transform, false);
            ((RectTransform)dot.transform).sizeDelta = new Vector2(14, 14);
            var di = dot.GetComponent<Image>(); di.sprite = UiIcons.Circle; di.color = Color.white; di.raycastTarget = false;
            go.SetActive(false);
        }

        void CursorAt(Vector3 world, float press = 0)
        {
            var p = Game.Cam.WorldToScreenPoint(world);
            cursor.position = new Vector3(p.x, p.y, 0);
            cursor.localScale = Vector3.one * (1f - 0.18f * press);
        }

        StationDirector stations; QuacksRig rig; LabEnvironment labEnv;
        /// <summary>Keep Professor Quack at a fixed spot (in view, never in front of the camera) after a lesson jump.</summary>
        IEnumerator Park(Vector3 pos)
        {
            yield return null;
            if (stations) { stations.Suspended = true; stations.StopAllCoroutines(); }
            if (rig)
            {
                rig.transform.position = new Vector3(pos.x, rig.transform.position.y, pos.z);
                var d = Game.Cam.transform.position - rig.transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f) rig.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
        }

        IEnumerator Script()
        {
            var s = Game.State; var lesson = Game.Lesson;
            var desk = FindAnyObjectByType<DesktopInput>();
            ControlDesk ctrl = null;
            var tv = FindAnyObjectByType<TVScreen>();
            var lab = FindAnyObjectByType<LabEnvironment>();
            Game.Access.VoiceOn = false; Game.Access.ReadingPace = 0.75f; Game.Access.VoiceSpeed = 1.6f;   // captions only, brisk
            BuildCursor();
            stations = FindAnyObjectByType<StationDirector>(); rig = FindAnyObjectByType<QuacksRig>(); labEnv = lab;
            Vector3 home = lab ? lab.QuackSpot.position : new Vector3(-1.9f, 0, 0.7f);
            Vector3 tvSpot = lab ? lab.TVAnchor.position + new Vector3(0.55f, 0, -1.25f) : home;

            // the learned rule (real gradient descent from the broken sorter), used as the dial targets
            var trainer = new Trainer(s.Space.Train, QuackGame.OldSorterParams());
            TrainStep st = default; for (int i = 0; i < 400; i++) st = trainer.Step(1.0);
            var good = st.After;

            // 1) title screen
            yield return Wait(1.5f);
            recording = true;
            yield return Wait(4.0f);

            Debug.Log("QUACK_TRAILER segment 2) the story frame=" + frame);
            // 2) the story on the factory TV
            Start.Dismiss();
            yield return Park(tvSpot);
            if (desk) { desk.UserSteered = true; desk.SetView(7); }
            if (tv) StartCoroutine(tv.PlayStory(2.5f, () => false));
            yield return Wait(9.5f);

            Debug.Log("QUACK_TRAILER segment 3) eggs become po frame=" + frame);
            // 3) eggs become points: the egg cloud, orbiting
            Start.OnJump?.Invoke("A_describe_eggs"); yield return Park(home);
            desk.UserSteered = true;
            var plotFocus = lesson.PlotRoot ? lesson.PlotRoot.position : Vector3.up * 1.3f;
            float yaw = -35;
            for (float t = 0; t < 6.5f; t += Time.deltaTime)
            {
                yaw += 14f * Time.deltaTime;
                desk.LookAt(plotFocus, yaw, 18, 2.0f, 401);
                if (t > 2.5f && s.Selected == null) { foreach (var e in s.Dataset.Train) if (e.Label == 1) { s.Select(e); break; } }
                yield return null;
            }

            // 4) the player builds the sorting rule with the desk dials
            Start.OnJump?.Invoke("B_build_rule"); yield return Park(home);
            s.SetParams(QuackGame.OldSorterParams(), ParamSource.Reset);
            desk.UserSteered = true; desk.SetView(8);
            yield return Wait(2.0f);
            ctrl = FindAnyObjectByType<ControlDesk>(FindObjectsInactive.Include);
            Debug.Log("QUACK_TRAILER segment dials frame=" + frame);
            cursor.transform.parent.gameObject.SetActive(true);
            var from = s.Params.Clone();
            int[] order = { 0, 1, 2, 3 };
            foreach (int k in order)
            {
                var knob = ctrl.ControlAt(k);
                var start = cursor.position;
                for (float t = 0; t < 0.5f; t += Time.deltaTime) { var p = Game.Cam.WorldToScreenPoint(knob.position); cursor.position = Vector3.Lerp(start, new Vector3(p.x, p.y, 0), Mathf.SmoothStep(0, 1, t / 0.5f)); yield return null; }
                s.BeginEdit();
                float dur = 1.6f;
                for (float t = 0; t < dur; t += Time.deltaTime)
                {
                    float u = Mathf.SmoothStep(0, 1, t / dur);
                    var p = s.Params.Clone();
                    if (k < 3) p.W[k] = Mathf.Lerp((float)from.W[k], (float)good.W[k], u); else p.B = Mathf.Lerp((float)from.B, (float)good.B, u);
                    s.SetParams(p, ParamSource.Slider);
                    var sp = Game.Cam.WorldToScreenPoint(knob.position) + (k < 3 ? new Vector3(Mathf.Sin(u * 3f) * 14f, Mathf.Cos(u * 3f) * 6f, 0) : Vector3.zero);
                    cursor.position = new Vector3(sp.x, sp.y, 0); cursor.localScale = Vector3.one * 0.82f;
                    yield return null;
                }
                cursor.localScale = Vector3.one;
                s.EndEdit(ParamSource.Slider);
                yield return Wait(0.25f);
            }
            cursor.transform.parent.gameObject.SetActive(false);
            yield return Wait(1.6f);
            // the selected egg rides the belt into its carton
            var cv = FindAnyObjectByType<ConveyorView>();
            if (cv) { foreach (var e in s.Dataset.Train) if (e.Label == 1 && s.Probability(e) > 0.5) { s.Select(e); break; }
                      desk.LookAt(cv.JumboCarton.position + Vector3.up * 0.25f, 25, 30, 1.5f, 402); }
            yield return Wait(3.2f);

            Debug.Log("QUACK_TRAILER segment 5) probability ba frame=" + frame);
            // 5) probability bands: steepness squeezes them
            Start.OnJump?.Invoke("C_measure_mistakes"); yield return Park(home);
            desk.UserSteered = true; desk.SetView(8);
            s.SetParams(good, ParamSource.Reset);
            yield return Wait(1.0f);
            double n0 = s.Params.WeightNorm();
            for (float t = 0; t < 3.0f; t += Time.deltaTime)
            {
                float n = Mathf.Lerp((float)n0, (float)n0 * 0.35f, Mathf.PingPong(t / 1.5f, 1f));
                s.SetParams(PlaneGeometry.SetSteepness(good, n), ParamSource.Steepness);
                yield return null;
            }
            s.SetParams(good, ParamSource.Reset);

            Debug.Log("QUACK_TRAILER segment 6) parameter spac frame=" + frame);
            // 6) parameter space: sample rules become a landscape
            Start.OnJump?.Invoke("D_landscape"); yield return Park(home);
            desk.UserSteered = true; desk.SetView(0);
            yield return Wait(1.2f);
            if (lesson.Landscape) desk.LookAt(lesson.Landscape.transform.position + Vector3.up * 0.15f, 10, 28, 1.25f, 403);
            yield return Wait(7.5f);

            Debug.Log("QUACK_TRAILER segment 7) gradient desce frame=" + frame);
            // 7) gradient descent: the machine walks downhill by itself
            Start.OnJump?.Invoke("E_gradient_descent"); yield return Park(home);
            desk.UserSteered = true;
            if (lesson.Landscape) desk.LookAt(lesson.Landscape.transform.position + Vector3.up * 0.12f, -15, 30, 1.35f, 404);
            yield return Wait(1.2f);
            if (lesson.Training) { lesson.Training.ResetRule(); lesson.Training.SetLearningRate(1.0); }
            for (int i = 0; i < 22 && lesson.Training; i++) { lesson.Training.DoStep(); yield return Wait(0.3f); }
            yield return Wait(0.8f);

            Debug.Log("QUACK_TRAILER segment 8) unseen eggs frame=" + frame);
            // 8) unseen eggs
            Start.OnJump?.Invoke("F_test_and_scale"); yield return Park(home);
            desk.UserSteered = true; desk.SetView(8);
            yield return Wait(2.0f);
            var act = Game.Mission.ActionButton ? Game.Mission.ActionButton.GetComponent<Button>() : null;
            if (act) act.onClick.Invoke();   // evaluate on held-out eggs
            yield return Wait(2.5f);
            act = Game.Mission.ActionButton ? Game.Mission.ActionButton.GetComponent<Button>() : null;
            if (act && act.gameObject.activeSelf) act.onClick.Invoke();   // run the 10,000-egg shipment
            yield return Wait(1.0f);
            lesson.ShowRecapForShots(5);
            desk.SetView(6);
            yield return Wait(4.5f);

            Debug.Log("QUACK_TRAILER segment 9) the board meet frame=" + frame);
            // 9) the board meeting
            var sq = FindAnyObjectByType<SpeechQueue>(); if (sq) sq.ClearAll();
            Game.Access.ReadingPace = 0.35f;
            if (stations) stations.Suspended = false;
            lesson.Finale();
            for (float t = 0; t < 45f && !(tv && tv.PlayedFinale); t += Time.deltaTime) yield return null;   // the clip starts after two short lines
            yield return Wait(17f);   // clip, cheers, confetti, congratulations
            recording = false;
            Debug.Log($"QUACK_TRAILER DONE frames={frame} fps={Fps}");
            yield return null;
            Application.Quit(0);
        }
    }
}
