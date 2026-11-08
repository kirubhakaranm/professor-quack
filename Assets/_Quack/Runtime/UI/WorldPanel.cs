using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// A world-space uGUI canvas placed at a lab anchor. Mouse works through the standard EventSystem
    /// (GraphicRaycaster + event camera); VR controllers work through WorldUIHit colliders. Same panel, both devices.
    /// </summary>
    public static class WorldPanel
    {
        /// <summary>Create a canvas whose 1 UI unit = metresWide / pixelWidth metres.</summary>
        public static RectTransform Create(string name, Transform anchor, Camera cam, float metresWide, float pixelWidth = 800, float pixelHeight = 600, bool interactive = true)
        {
            // display-only panels get no raycaster, so they never swallow clicks meant for eggs behind them
            var go = interactive ? new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
                                 : new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;   // higher text resolution on 3D panels: crisp instead of blurry/glowing
            var rt = (RectTransform)go.transform;
            rt.SetParent(anchor, false);
            rt.sizeDelta = new Vector2(pixelWidth, pixelHeight);
            rt.localScale = Vector3.one * (metresWide / pixelWidth);
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            return rt;
        }

        /// <summary>Call after a panel's controls exist so VR rays can use them.</summary>
        public static void EnableRayInput(RectTransform canvasRoot) => WorldUIHit.AttachAll(canvasRoot.GetComponent<Canvas>());

        /// <summary>A vertical panel filling the canvas width, anchored at the top.</summary>
        public static RectTransform Body(UiKit ui, RectTransform canvasRoot, string name, Color? color = null)
        {
            var p = ui.Panel(canvasRoot, name, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, canvasRoot.sizeDelta.x, color);
            return p;
        }
    }
}
