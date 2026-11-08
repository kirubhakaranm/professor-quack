using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quack
{
    /// <summary>
    /// Keeps world panels from hiding the visualisation: any registered panel that sits between the viewer and
    /// the plot (overlapping it on screen and nearer the camera) fades to a faint ghost, and comes back to full
    /// strength while the mouse is over it. Works for whichever camera is active (desktop orbit or VR head).
    /// </summary>
    public sealed class PanelFader : MonoBehaviour
    {
        public Camera Cam;
        public Transform Focus;           // the plot root
        public float FocusSize = 0.7f;    // metres (plot cube edge)
        public AccessSettings Access;
        public const float GhostAlpha = 0.14f;

        readonly List<(RectTransform rt, CanvasGroup cg)> panels = new List<(RectTransform, CanvasGroup)>();
        readonly Vector3[] corners = new Vector3[4];

        readonly List<(System.Func<Vector3> centre, Vector3 size)> extra = new List<(System.Func<Vector3>, Vector3)>();
        /// <summary>Another thing panels must not cover (e.g. Professor Quack).</summary>
        public void AlsoProtect(System.Func<Vector3> centre, Vector3 size) => extra.Add((centre, size));

        public void Add(RectTransform root)
        {
            if (root == null) return;
            var cg = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            panels.Add((root, cg));
        }

        void LateUpdate()
        {
            var cam = Cam != null && Cam.isActiveAndEnabled ? Cam : Camera.main;
            if (cam == null || Focus == null) return;
            var foci = new List<(Rect r, float depth)>();
            if (ScreenRect(cam, Box(Focus.position, Vector3.one * FocusSize), out var fr, out float fd)) foci.Add((fr, fd));
            foreach (var (c, size) in extra) if (ScreenRect(cam, Box(c(), size), out var er, out float ed)) foci.Add((er, ed));
            if (foci.Count == 0) return;
            Vector2? mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2?)null;

            foreach (var (rt, cg) in panels)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                rt.GetWorldCorners(corners);
                float target = 1f;
                if (ScreenRect(cam, corners, out var r, out float depth))
                {
                    bool hovered = mouse.HasValue && r.Contains(mouse.Value);
                    foreach (var (focus, focusDepth) in foci)
                    {
                        if (depth >= focusDepth) continue;
                        float overlap = Overlap(r, focus) / Mathf.Max(1f, focus.width * focus.height);
                        if (overlap > 0.04f && !hovered) { target = GhostAlpha; break; }
                    }
                }
                cg.alpha = Access != null && Access.ReducedMotion ? target : Mathf.MoveTowards(cg.alpha, target, Time.unscaledDeltaTime * 3.5f);
            }
        }

        static Vector3[] Box(Vector3 c, Vector3 size)
        {
            var h = size / 2f; var list = new Vector3[8]; int i = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                list[i++] = c + new Vector3(x * h.x, y * h.y, z * h.z);
            return list;
        }

        /// <summary>Screen rect of world points (false if any point is behind the camera) and their nearest depth.</summary>
        static bool ScreenRect(Camera cam, Vector3[] pts, out Rect rect, out float nearest)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; nearest = float.MaxValue;
            foreach (var p in pts)
            {
                var s = cam.WorldToScreenPoint(p);
                if (s.z <= 0.01f) { rect = default; return false; }
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
                nearest = Mathf.Min(nearest, s.z);
            }
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0 && h > 0 ? w * h : 0;
        }
    }
}
