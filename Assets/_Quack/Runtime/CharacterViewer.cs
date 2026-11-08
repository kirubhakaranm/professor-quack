using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Interactive character viewer (launch with -quackViewer): Professor Quack alone in the studio, with buttons for
    /// every expression, gesture and duck habit, talking (beak), gaze, blink and camera angles. Right-drag orbits,
    /// scroll zooms. For reviewing the character and giving feedback.
    /// </summary>
    public sealed class CharacterViewer : MonoBehaviour
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-quackViewer") >= 0;

        public QuacksAnimator Anim;
        public Camera Cam;
        readonly UiKit ui = new UiKit();
        Text status;
        float yaw, pitch = 6, dist = 3.0f, tYaw, tPitch = 6, tDist = 3.0f;
        bool face, talking;
        Vector3 gaze; bool gazeFixed;

        public void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var cgo = new GameObject("Viewer UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);

            // left column: expressions + duck habits
            var left = ui.Panel(cgo.transform, "Expressions", new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), 300);
            ui.Text(left, "EXPRESSIONS", 18, UiKit.Accent, FontStyle.Bold);
            foreach (Emotion e in Enum.GetValues(typeof(Emotion)))
            {
                var em = e;
                ui.Button(left, Nice(e.ToString()), () => { Anim.Intensity = 0.8f; Anim.SetEmotion(em); Say($"Expression: {Nice(em.ToString())}"); }, 17);
            }
            ui.Text(left, "DUCK HABITS", 18, UiKit.Accent, FontStyle.Bold);
            foreach (DuckBehavior d in Enum.GetValues(typeof(DuckBehavior)))
            {
                if (d == DuckBehavior.None) continue;
                var dd = d;
                ui.Button(left, Nice(d.ToString()), () => { Anim.PlayDuck(dd); Say($"Duck habit: {Nice(dd.ToString())}"); }, 17);
            }

            // right column: gestures, talking, gaze
            var right = ui.Panel(cgo.transform, "Gestures", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), 300);
            ui.Text(right, "GESTURES", 18, UiKit.Accent, FontStyle.Bold);
            foreach (Gesture g in Enum.GetValues(typeof(Gesture)))
            {
                if (g == Gesture.None) continue;
                var gg = g;
                ui.Button(right, Nice(g.ToString()), () => { Anim.Play(gg); Say($"Gesture: {Nice(gg.ToString())}"); }, 17);
            }
            ui.Text(right, "FACE", 18, UiKit.Accent, FontStyle.Bold);
            ui.Button(right, "Talk on / off (beak)", ToggleTalk, 17);
            var row = ui.Row(right, 6);
            ui.Button(row, "Look L", () => Look(-1, 0), 15);
            ui.Button(row, "Look R", () => Look(1, 0), 15);
            ui.Button(row, "Up", () => Look(0, 1), 15);
            ui.Button(row, "At me", () => { gazeFixed = false; Say("Looking at you"); }, 15);

            // bottom: camera
            var bottom = ui.Panel(cgo.transform, "Camera", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), 1000);
            status = ui.Text(bottom, "Pick an expression or gesture. Right-drag to orbit, scroll to zoom.", 20, UiKit.TextColor, align: TextAnchor.MiddleCenter);
            var cams = ui.Row(bottom, 8);
            ui.Button(cams, "Front", () => View(0), 17);
            ui.Button(cams, "3/4", () => View(35), 17);
            ui.Button(cams, "Side", () => View(90), 17);
            ui.Button(cams, "Back", () => View(180), 17);
            ui.Button(cams, "Face close-up", () => { face = true; tDist = 1.1f; Say("Face close-up"); }, 17);
            ui.Button(cams, "Full body", () => { face = false; tDist = 3.0f; Say("Full body"); }, 17);
            ui.Button(cams, "Reset", () => { Anim.SetEmotion(Emotion.Neutral); if (talking) ToggleTalk(); gazeFixed = false; View(0); Say("Reset"); }, 17);

            Anim.GazeTarget = () => gazeFixed ? gaze : Cam.transform.position;
        }

        static string Nice(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])([A-Z])", " $1");
        void Say(string s) { if (status) status.text = s; }

        void View(float y) { tYaw = y; tPitch = face ? 2 : 6; Say(y == 0 ? "Front" : y == 180 ? "Back" : y == 90 ? "Side" : "Three-quarter"); }

        void Look(int x, int y)
        {
            var t = Anim.Rig.transform;
            gaze = t.position + t.forward * 2f + t.right * (x * 1.6f) + Vector3.up * (1.5f + y * 1.2f);
            gazeFixed = true; Say(x < 0 ? "Looking to his right" : x > 0 ? "Looking to his left" : "Looking up");
        }

        void ToggleTalk()
        {
            talking = !talking;
            if (talking) Anim.StartSpeaking(() => 0.5f + 0.5f * Mathf.Sin(Time.time * 11f) * Mathf.Abs(Mathf.Sin(Time.time * 2.3f)));
            else Anim.StopSpeaking();
            Say(talking ? "Talking (beak moving)" : "Quiet");
        }

        void Update()
        {
            var m = Mouse.current;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (m != null && !overUi)
            {
                if (m.rightButton.isPressed) { var d = m.delta.ReadValue(); tYaw += d.x * 0.3f; tPitch = Mathf.Clamp(tPitch - d.y * 0.3f, -20, 60); }
                float s = m.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 0.01f) tDist = Mathf.Clamp(tDist * (s > 0 ? 0.9f : 1.1f), 0.6f, 6f);
            }
            float k = 1 - Mathf.Exp(-Time.deltaTime * 6);
            yaw = Mathf.LerpAngle(yaw, tYaw, k); pitch = Mathf.Lerp(pitch, tPitch, k); dist = Mathf.Lerp(dist, tDist, k);
            var rig = Anim.Rig;
            Vector3 focus = face && rig.LidL != null && rig.LidR != null ? (rig.LidL.position + rig.LidR.position) / 2 : rig.transform.position + Vector3.up * 0.95f;
            var dir = Quaternion.Euler(pitch, yaw, 0) * rig.transform.forward;
            Cam.transform.position = focus + dir * dist;
            Cam.transform.LookAt(focus);
        }
    }
}
