using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// VR: draws a world-space canvas over the 3D scene (ZTest Always), so the captions / voice / settings panel is
    /// never hidden behind the control desk or other props. Re-applies to graphics created later (settings, cards).
    /// </summary>
    public sealed class OnTopUI : MonoBehaviour
    {
        static Material onTop;
        float next;

        static Material Mat
        {
            get
            {
                if (onTop == null)
                {
                    onTop = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "UI on top (VR)" };
                    onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                    onTop.renderQueue = 4000;
                }
                return onTop;
            }
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            foreach (var g in GetComponentsInChildren<Graphic>(true))
                if (g.material == null || g.material == g.defaultMaterial || g.material == Canvas.GetDefaultCanvasMaterial()) g.material = Mat;
        }
    }
}
