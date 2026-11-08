using UnityEngine;

namespace Quack
{
    /// <summary>Small procedurally drawn UI sprites (no texture assets needed): gear, filled circle, ring, vignette.</summary>
    public static class UiIcons
    {
        static Sprite gear, circle, ring, vignette, check, parchment, compass;

        /// <summary>Aged parchment: warm paper with soft blotches, fibres and darkened, burnt-looking edges.</summary>
        public static Sprite Parchment
        {
            get
            {
                if (parchment != null) return parchment;
                const int w = 512, h = 256;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[w * h];
                var paper = new Color(0.93f, 0.85f, 0.67f); var stain = new Color(0.78f, 0.64f, 0.42f); var burnt = new Color(0.42f, 0.27f, 0.12f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = x / (float)w, v = y / (float)h;
                        float blot = Mathf.PerlinNoise(u * 5f + 3.1f, v * 3f + 7.7f) * 0.6f + Mathf.PerlinNoise(u * 18f, v * 9f) * 0.25f + Mathf.PerlinNoise(u * 60f, v * 30f) * 0.15f;
                        var c = Color.Lerp(paper, stain, Mathf.Clamp01((blot - 0.45f) * 1.6f));
                        float ex = Mathf.Min(u, 1 - u) * 2f, ey = Mathf.Min(v, 1 - v) * 2f;           // 0 at the edge, 1 in the middle
                        float edge = Mathf.Min(ex * 1.6f, ey * 3.2f) + (Mathf.PerlinNoise(u * 25f, v * 25f) - 0.5f) * 0.12f;
                        c = Color.Lerp(burnt, c, SStep(0.02f, 0.28f, edge));
                        c.a = SStep(0.0f, 0.04f, edge);                                               // ragged edge
                        px[y * w + x] = c;
                    }
                tex.SetPixels(px); tex.Apply();
                return parchment = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
            }
        }

        /// <summary>A simple compass rose (eight-point star with a ring).</summary>
        public static Sprite Compass => compass ??= Make(256, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
            float star4 = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2)), 6f), star8 = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2 + Mathf.PI / 4)), 6f);
            float star = Mathf.Max(Edge(r - (0.12f + 0.78f * star4), 0.02f), Edge(r - (0.1f + 0.45f * star8), 0.02f));
            float ringA = Edge(r - 0.62f, 0.02f) * (1 - Edge(r - 0.57f, 0.02f));
            return Mathf.Max(star, ringA);
        }, new Color(0.35f, 0.22f, 0.1f));

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>A bold tick mark.</summary>
        public static Sprite Check => check ??= Make(128, (u, v) =>
        {
            float d = Mathf.Min(SegDist(u, v, -0.55f, 0.02f, -0.15f, -0.4f), SegDist(u, v, -0.15f, -0.4f, 0.6f, 0.45f));
            return Edge(d - 0.13f, 0.03f);
        }, Color.white);

        static Sprite Make(int n, System.Func<float, float, float> alpha, Color tint)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                    var c = tint; c.a *= Mathf.Clamp01(alpha(u, v)); px[y * n + x] = c;
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>Proper smoothstep (Unity's Mathf.SmoothStep interpolates between two values instead).</summary>
        static float SStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        /// <summary>Anti-aliased edge: 1 inside, 0 outside, soft over ~1.5 px.</summary>
        static float Edge(float d, float px) => Mathf.Clamp01(0.5f - d / px);

        public static Sprite Gear => gear ??= Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
            float teeth = Mathf.Clamp(Mathf.Cos(a * 8) * 3f, -1, 1) * 0.5f + 0.5f;     // 8 square-ish teeth
            float outer = Mathf.Lerp(0.72f, 0.95f, teeth);
            return Edge(r - outer, 0.025f) * (1 - Edge(r - 0.32f, 0.025f));          // ring with a hole
        }, Color.white);

        public static Sprite Circle => circle ??= Make(128, (u, v) => Edge(Mathf.Sqrt(u * u + v * v) - 0.96f, 0.025f), Color.white);

        public static Sprite Ring => ring ??= Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return Edge(r - 0.96f, 0.025f) * (1 - Edge(r - 0.78f, 0.025f));
        }, Color.white);

        public static Sprite Vignette => vignette ??= Make(256, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u * 0.8f + v * v);
            return SStep(0.25f, 1.25f, r);
        }, Color.black);
    }
}
