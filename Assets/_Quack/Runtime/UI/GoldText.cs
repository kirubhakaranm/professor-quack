using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// Metallic gold for UI text: a vertical gradient (pale highlight -> rich gold -> deep amber) plus a slow glint
    /// sweeping across the letters. Add it BEFORE Outline/Shadow so those keep their own colours.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class GoldText : BaseMeshEffect
    {
        public Color Top = new Color(1f, 0.97f, 0.72f), Mid = new Color(1f, 0.78f, 0.18f), Bottom = new Color(0.66f, 0.38f, 0.03f);
        public bool Glint = true;
        public float GlintSpeed = 0.35f, GlintWidth = 0.12f;
        float glint = -1f;

        void Update()
        {
            if (!Glint) return;
            float t = Time.unscaledTime * GlintSpeed % 1.6f;      // sweep, then a pause before the next one
            glint = t - 0.3f;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float yMin = float.MaxValue, yMax = float.MinValue, xMin = float.MaxValue, xMax = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                yMin = Mathf.Min(yMin, v.position.y); yMax = Mathf.Max(yMax, v.position.y);
                xMin = Mathf.Min(xMin, v.position.x); xMax = Mathf.Max(xMax, v.position.x);
            }
            float h = Mathf.Max(1e-3f, yMax - yMin), w = Mathf.Max(1e-3f, xMax - xMin);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float ty = (v.position.y - yMin) / h;                               // 0 bottom .. 1 top
                Color c = ty > 0.55f ? Color.Lerp(Mid, Top, (ty - 0.55f) / 0.45f) : Color.Lerp(Bottom, Mid, ty / 0.55f);
                if (Glint)
                {
                    float tx = (v.position.x - xMin) / w + (ty - 0.5f) * 0.15f;      // slanted band
                    float g = Mathf.Clamp01(1f - Mathf.Abs(tx - glint) / GlintWidth);
                    c = Color.Lerp(c, Color.white, g * g * 0.85f);
                }
                c.a = v.color.a / 255f;
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
