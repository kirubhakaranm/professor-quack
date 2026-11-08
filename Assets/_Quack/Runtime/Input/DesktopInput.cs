using Quack.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Quack
{
    /// <summary>
    /// Mouse/keyboard path: orbit camera, zoom, preset views, egg selection, handle dragging.
    /// All edits go through GameState (BeginEdit / SetParams / EndEdit), the same commands a VR
    /// input layer will call, so both paths drive identical simulation state.
    /// </summary>
    public sealed class DesktopInput : MonoBehaviour
    {
        public Camera Cam;
        public FeaturePlot Plot;
        public DecisionPlaneView PlaneView;
        public GameState State;
        public AccessSettings Access;
        public System.Action ActivityNoted;
        public System.Action TalkPressed, TalkReleased;   // push-to-talk (hold V)
        public System.Action PauseToggled;                // P (and Esc)
        public bool PauseOnEscape = true;

        // orbit camera around the plot centre
        float yaw = 0, pitch = 10, distance = 2.7f;
        float tYaw, tPitch = 10, tDistance = 2.7f;
        float moveRate = 5f;   // camera glide speed (slower for cinematic pans)
        public const float LabDistance = 2.7f, PlotDistance = 1.6f;   // pulled back: the whole station stays in frame

        enum Drag { None, Orbit, Rotate, Translate }
        Drag drag;
        Vector2 pressPos;
        bool pressOnEmpty;

        /// <summary>Orbit centre: fixed at the table (set by the bootstrap) so moving holograms don't drag the view.</summary>
        public Vector3? Focus;
        /// <summary>Wall objects with their own preset views: 6 = whiteboard, 7 = TV.</summary>
        public Vector3? BoardFocus, TVFocus;
        Vector3 focusOffset, tFocusOffset;   // the orbit centre glides from the table to a wall object and back
        Vector3 Center => (Focus ?? Plot.transform.position) + Vector3.down * 0.06f + focusOffset;   // aim slightly low so the plot clears the captions

        // snap to whatever target view was requested before the first frame (e.g. the 3D preset)
        void Start() { yaw = tYaw; pitch = tPitch; distance = tDistance; focusOffset = tFocusOffset; ApplyCamera(true); }

        /// <summary>True once the learner moves the camera themselves; the follow camera then leaves it alone
        /// until the next stage (StationDirector resets it).</summary>
        public bool UserSteered { get; set; }

        /// <summary>Orbit around any point in the room (stations).</summary>
        /// <summary>Make the next camera move a slow, cinematic glide (scene tour, whiteboard visits).</summary>
        public bool SlowNext { get; set; }

        public void LookAt(Vector3 focus, float yaw, float pitch, float dist, int presetId)
        {
            Preset = presetId;
            moveRate = SlowNext ? 1.6f : 5f; SlowNext = false;
            tFocusOffset = focus - (Focus ?? Plot.transform.position) + Vector3.up * 0.06f;
            tYaw = yaw; tPitch = pitch; tDistance = dist;
            if (Access.ReducedMotion) { this.yaw = tYaw; this.pitch = tPitch; focusOffset = tFocusOffset; }
        }

        /// <summary>Last preset view requested (so a temporary look at a wall object can return).</summary>
        public int Preset { get; private set; }

        /// <summary>The last plot-centred preset (0-5); stations return to it.</summary>
        public int LastPlotPreset { get; private set; }

        public void SetView(int preset)
        {
            Preset = preset;
            if (preset <= 5 || preset == 8) LastPlotPreset = preset;
            moveRate = SlowNext ? 1.6f : 5f; SlowNext = false;
            tDistance = preset == 0 ? LabDistance : PlotDistance;
            tFocusOffset = Vector3.zero;
            var table = Focus ?? Plot.transform.position;
            switch (preset)
            {
                case 6 when BoardFocus.HasValue:              // turn to the whiteboard on the right wall
                    tFocusOffset = BoardFocus.Value - table + Vector3.up * 0.06f; tYaw = 90; tPitch = 0; tDistance = 1.9f; break;
                case 8:                                       // controller view: Professor Quack, plot, sorting line and control desk together
                    tFocusOffset = new Vector3(0.05f, 1.12f, 0.1f) - table + Vector3.up * 0.06f; tYaw = 0; tPitch = 13; tDistance = 3.0f; break;
                case 7 when TVFocus.HasValue:                 // turn to the TV on the left wall
                    tFocusOffset = TVFocus.Value - table + Vector3.up * 0.06f + new Vector3(0, 0, -0.45f); tYaw = -90; tPitch = 2; tDistance = 2.9f; break;   // wide enough for Professor Quack beside the TV
                case 0: tYaw = 0; tPitch = 10; break;       // lab overview: table, consoles, Quack, conveyor
                case 1: tYaw = 0; tPitch = 0; break;        // look along +z: mass (x) vs length (y)
                case 2: tYaw = 0; tPitch = 89; break;       // top-down: mass (x) vs width (z)
                case 3: tYaw = 90; tPitch = 0; break;       // from +x side... shows width (z) vs length (y)
                case 4: tYaw = -42; tPitch = 20; break;     // 3D overview
                case 5:                                      // edge-on: look along the plane (perpendicular to w)
                    if (State.HasBoundary)
                    {
                        var n = PlaneView.NormalLocal;
                        var along = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                        var worldDir = Plot.transform.TransformDirection(along);
                        tYaw = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
                        tPitch = -Mathf.Asin(Mathf.Clamp(worldDir.y, -1, 1)) * Mathf.Rad2Deg;
                    }
                    break;
            }
            State.Log("view", preset.ToString());
            if (Access.ReducedMotion) { yaw = tYaw; pitch = tPitch; focusOffset = tFocusOffset; }
        }

        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector2 mp = mouse.position.ReadValue();

            // keyboard shortcuts
            if (kb != null && !TypingInUi())
            {
                if (kb.pKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame && PauseOnEscape) PauseToggled?.Invoke();
                if (kb.f11Key.wasPressedThisFrame) { Access.Fullscreen = !Access.Fullscreen; Access.Apply(); }
                if (kb.vKey.wasPressedThisFrame) TalkPressed?.Invoke();
                if (kb.vKey.wasReleasedThisFrame) TalkReleased?.Invoke();
                if (kb.digit0Key.wasPressedThisFrame) { UserSteered = true; SetView(0); }
                if (kb.digit1Key.wasPressedThisFrame) { UserSteered = true; SetView(1); }
                if (kb.digit2Key.wasPressedThisFrame) { UserSteered = true; SetView(2); }
                if (kb.digit3Key.wasPressedThisFrame) { UserSteered = true; SetView(3); }
                if (kb.digit4Key.wasPressedThisFrame) { UserSteered = true; SetView(4); }
                if (kb.digit5Key.wasPressedThisFrame) { UserSteered = true; SetView(5); }
                if (kb.digit6Key.wasPressedThisFrame) { UserSteered = true; SetView(6); }
                if (kb.digit7Key.wasPressedThisFrame) { UserSteered = true; SetView(7); }
                if (kb.digit8Key.wasPressedThisFrame) { UserSteered = true; SetView(8); }
                if (kb.escapeKey.wasPressedThisFrame) State.Select(null);
                if (kb.iKey.wasPressedThisFrame) Plot.SetIsolate(!Plot.Isolate);
                if (kb.tabKey.wasPressedThisFrame) CycleSelection(kb.shiftKey.isPressed ? -1 : 1);
                // keyboard orbit for players who can't drag
                float k = 60f * Time.deltaTime;
                // W A S D: walk the view around the room (relative to where the camera looks), Shift = faster.
                // The orbit centre is clamped inside the walls; ApplyCamera keeps the camera itself in the room too.
                Vector2 walk = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
                if (walk != Vector2.zero)
                {
                    UserSteered = true;
                    var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                    float speed = (kb.leftShiftKey.isPressed ? 3.2f : 1.6f) * Time.unscaledDeltaTime;
                    var basePos = (Focus ?? Plot.transform.position) + Vector3.down * 0.06f;
                    var target = basePos + tFocusOffset + (fwd * walk.y + right * walk.x).normalized * speed;
                    target.x = Mathf.Clamp(target.x, -3.3f, 3.3f); target.z = Mathf.Clamp(target.z, -3.7f, 3.4f);
                    tFocusOffset = target - basePos;
                    focusOffset = tFocusOffset;   // move immediately (no glide lag while walking)
                }
                if (kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed || kb.upArrowKey.isPressed || kb.downArrowKey.isPressed) UserSteered = true;
                if (kb.leftArrowKey.isPressed) tYaw += k;
                if (kb.rightArrowKey.isPressed) tYaw -= k;
                if (kb.upArrowKey.isPressed) tPitch = Mathf.Clamp(tPitch + k, -89, 89);
                if (kb.downArrowKey.isPressed) tPitch = Mathf.Clamp(tPitch - k, -89, 89);
            }

            // any key, scroll or camera drag counts as the learner being active (resets Professor Quack's idle nudge)
            if ((kb != null && kb.anyKey.wasPressedThisFrame) || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f ||
                mouse.rightButton.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame) ActivityNoted?.Invoke();

            // zoom
            float scroll = mouse.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f) UserSteered = true;
            if (!overUi && Mathf.Abs(scroll) > 0.01f) tDistance = Mathf.Clamp(tDistance * (scroll > 0 ? 0.9f : 1.1f), 0.7f, 3.2f);

            // right-drag orbit
            if (mouse.rightButton.isPressed && !overUi)
            {
                var d = mouse.delta.ReadValue();
                if (d.sqrMagnitude > 4f) UserSteered = true;
                tYaw += d.x * 0.25f; tPitch = Mathf.Clamp(tPitch - d.y * 0.25f, -89, 89);
            }

            // left button: select / handle drag / orbit on empty space
            if (mouse.leftButton.wasPressedThisFrame && !overUi) OnPress(mp);
            if (mouse.leftButton.isPressed && drag != Drag.None) OnDrag(mp, mouse.delta.ReadValue());
            if (mouse.leftButton.wasReleasedThisFrame) OnRelease(mp);

            ApplyCamera(Access.ReducedMotion);
        }

        void ApplyCamera(bool snap)
        {
            float k = snap ? 1 : 1 - Mathf.Exp(-Time.unscaledDeltaTime * moveRate);
            focusOffset = Vector3.Lerp(focusOffset, tFocusOffset, k);
            yaw = Mathf.LerpAngle(yaw, tYaw, k); pitch = Mathf.Lerp(pitch, tPitch, k); distance = Mathf.Lerp(distance, tDistance, k);
            var rot = Quaternion.Euler(pitch, yaw, 0);
            // the camera never leaves the room: if the orbit would pass through a wall, floor or ceiling, pull in closer
            float d = distance;
            for (int i = 0; i < 12 && !InRoom(Center - rot * Vector3.forward * d); i++) d *= 0.85f;
            Cam.transform.position = Center - rot * Vector3.forward * d;
            Cam.transform.rotation = rot;
        }

        // room interior (walls x +-3.8, z -4.2..3.9, ceiling 4.0) minus a small margin
        static bool InRoom(Vector3 p) => p.x > -3.6f && p.x < 3.6f && p.z > -4.0f && p.z < 3.7f && p.y > 0.25f && p.y < 3.85f;

        static bool TypingInUi()
        {
            var go = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            return go != null && go.GetComponent<UnityEngine.UI.InputField>() != null;
        }

        PointerInteractor pointer;
        public PointerInteractor Pointer => pointer ??= new PointerInteractor(Plot, PlaneView, State);

        void OnPress(Vector2 mp)
        {
            ActivityNoted?.Invoke();
            pressPos = mp;
            var r = Pointer.Press(Cam.ScreenPointToRay(mp));
            pressOnEmpty = r == PointerInteractor.Result.Empty;
            drag = r == PointerInteractor.Result.Empty ? Drag.Orbit : Drag.Rotate;   // Rotate = "pointer owns the drag"
        }

        void OnDrag(Vector2 mp, Vector2 delta)
        {
            if (drag == Drag.Orbit && delta.sqrMagnitude > 4f) UserSteered = true;
            if (drag == Drag.Orbit) { tYaw += delta.x * 0.25f; tPitch = Mathf.Clamp(tPitch - delta.y * 0.25f, -89, 89); }
            else Pointer.Drag(Cam.ScreenPointToRay(mp));
        }

        void OnRelease(Vector2 mp)
        {
            if (drag == Drag.Orbit && pressOnEmpty && (mp - pressPos).magnitude < 4f) State.Select(null);   // click on empty space
            else if (drag != Drag.None) Pointer.Release();
            drag = Drag.None;
        }

        void CycleSelection(int dir)
        {
            var eggs = Plot.Eggs;
            if (eggs.Count == 0) return;
            int i = State.Selected == null ? -1 : eggs.IndexOf(Plot.ViewOf(State.Selected));
            i = (i + dir + eggs.Count) % eggs.Count;
            State.Select(eggs[i].Egg);
        }
    }

    static class ListExt
    {
        public static int IndexOf<T>(this System.Collections.Generic.IReadOnlyList<T> list, T item)
        {
            for (int i = 0; i < list.Count; i++) if (Equals(list[i], item)) return i;
            return -1;
        }
    }
}
