using System.Collections;
using System.IO;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Test tool (launch with -quackPortrait &lt;dir&gt;): frames Professor Quack and screenshots each emotion,
    /// a blink, gaze left/right and an open beak, so the face system can be reviewed without a human driver.
    /// </summary>
    public sealed class CharacterPortrait : MonoBehaviour
    {
        public QuacksAnimator Anim;
        public Camera Cam;

        public static string RequestedDir()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-quackPortrait");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public void Run(string dir) { Directory.CreateDirectory(dir); StartCoroutine(Shoot(dir)); }

        IEnumerator Shoot(string dir)
        {
            Application.runInBackground = true;
            var input = FindAnyObjectByType<DesktopInput>(); if (input) input.enabled = false;
            var head = Anim.Rig.Head;
            int n = 0;
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}_{name}.png"));
                yield return null; yield return null;
            }
            // the studio puts him alone at the origin (QuackGame.RunStudio)
            Vector3 FaceFocus() => Anim.Rig.LidL != null && Anim.Rig.LidR != null
                ? (Anim.Rig.LidL.position + Anim.Rig.LidR.position) / 2 : head.position + Vector3.up * 0.15f;
            void FrameBody(float yawDeg, float dist)
            {
                var focus = Anim.Rig.transform.position + Vector3.up * 0.95f;
                var dirToCam = Quaternion.AngleAxis(yawDeg, Vector3.up) * Anim.Rig.transform.forward;
                Cam.transform.position = focus + dirToCam * dist + Vector3.up * 0.1f;
                Cam.transform.LookAt(focus);
            }
            void Frame(float yawDeg, float dist)
            {
                var focus = FaceFocus();
                var fwd = Anim.Rig.transform.forward;
                var dirToCam = Quaternion.AngleAxis(yawDeg, Vector3.up) * fwd;
                Cam.transform.position = focus + dirToCam * dist + Vector3.up * 0.05f;
                Cam.transform.LookAt(focus);
            }
            Anim.GazeTarget = () => Cam.transform.position;
            yield return new WaitForSeconds(1.5f);

            FrameBody(0, 3.0f); yield return new WaitForSeconds(0.6f); yield return Shot("full_front");
            FrameBody(35, 3.0f); yield return new WaitForSeconds(0.6f); yield return Shot("full_three_quarter");
            FrameBody(90, 3.0f); yield return new WaitForSeconds(0.6f); yield return Shot("full_side");
            FrameBody(180, 3.0f); yield return new WaitForSeconds(0.6f); yield return Shot("full_back");
            Frame(0, 0.9f);
            foreach (Emotion e in System.Enum.GetValues(typeof(Emotion)))
            {
                Anim.Intensity = 0.8f; Anim.SetEmotion(e);
                yield return new WaitForSeconds(0.9f);
                yield return Shot("face_" + e.ToString().ToLowerInvariant());
            }
            Anim.SetEmotion(Emotion.Neutral);
            var left = head.position + Anim.Rig.transform.right * -1.5f + Vector3.up * 0.2f;
            Anim.GazeTarget = () => left; yield return new WaitForSeconds(0.8f); yield return Shot("gaze_his_right");
            var right = head.position + Anim.Rig.transform.right * 1.5f + Vector3.up * 0.2f;
            Anim.GazeTarget = () => right; yield return new WaitForSeconds(0.8f); yield return Shot("gaze_his_left");
            Anim.GazeTarget = () => Cam.transform.position;
            Anim.StartSpeaking(() => 1f); yield return new WaitForSeconds(0.4f); yield return Shot("beak_open");
            Anim.StopSpeaking();
            Anim.Play(Gesture.AdjustGlasses); yield return new WaitForSeconds(0.5f); yield return Shot("gesture_adjust_glasses");
            yield return new WaitForSeconds(1.2f);
            Anim.Play(Gesture.PointAtPlot); yield return new WaitForSeconds(0.5f); yield return Shot("gesture_point");
            // full-body gestures: are the wings and bow tie actually moving?
            yield return new WaitForSeconds(1.5f);
            var target = Anim.Rig.transform.position + Anim.Rig.transform.right * 1.5f + Anim.Rig.transform.forward * 1.0f + Vector3.up * 1.2f;
            Anim.GazeTarget = () => target;
            FrameBody(15, 3.0f); Anim.Play(Gesture.PointAtPlot); yield return new WaitForSeconds(0.55f); yield return Shot("body_point_his_left");
            yield return new WaitForSeconds(1.8f);
            Anim.GazeTarget = () => Cam.transform.position;
            Anim.Play(Gesture.FlapSmall); yield return new WaitForSeconds(0.25f); yield return Shot("body_flap");
            yield return new WaitForSeconds(1.2f);
            Anim.Play(Gesture.Shrug); yield return new WaitForSeconds(0.35f); yield return Shot("body_shrug");
            yield return new WaitForSeconds(1.2f);
            Frame(0, 1.1f); Anim.Play(Gesture.TieAdjust); yield return new WaitForSeconds(0.2f); yield return Shot("tie_adjust");
            Debug.Log($"QUACK_PORTRAIT DONE shots={n}");
            yield return new WaitForSeconds(1.5f);
            Application.Quit(0);
        }
    }
}
