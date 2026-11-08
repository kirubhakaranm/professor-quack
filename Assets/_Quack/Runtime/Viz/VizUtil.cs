using UnityEngine;

namespace Quack
{
    /// <summary>Small helpers for procedural geometry and labels.</summary>
    public static class VizUtil
    {
        static Font font;
        // UI type (SIL Open Font License, Resources/Fonts): Nunito for body text, Cinzel for headings and buttons,
        // Atkinson Hyperlegible for captions (designed for low-vision readers), VT323 for the LCD scoreboard
        // learning content uses the Braille Institute's Atkinson Hyperlegible; game chrome (title, pause, settings,
        // roadmap) uses the royal Cinzel Decorative; the whiteboard uses Patrick Hand handwriting
        public static Font Font => font ? font : (font = Load("Fonts/AtkinsonHyperlegible-Regular"));
        static Font heading, caption, lcd, hand;
        public static Font HeadingFont => heading ? heading : (heading = Load("Fonts/CinzelDecorative-Bold"));
        public static Font HandFont => hand ? hand : (hand = Load("Fonts/PatrickHand"));
        public static Font CaptionFont => caption ? caption : (caption = Load("Fonts/AtkinsonHyperlegible-Regular"));
        public static Font LcdFont => lcd ? lcd : (lcd = Load("Fonts/VT323"));
        static Font Load(string path) => Resources.Load<Font>(path) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // TextMeshPro (SDF) fonts for crisp 3D text at any distance (created by the editor TmpSetup in Resources/Fonts)
        static TMPro.TMP_FontAsset tmpBody, tmpRoyal, tmpLcd, tmpHand;
        static TMPro.TMP_FontAsset Tmp(string n) => Resources.Load<TMPro.TMP_FontAsset>("Fonts/" + n + " SDF");
        public static TMPro.TMP_FontAsset TmpBody => tmpBody ? tmpBody : (tmpBody = Tmp("AtkinsonHyperlegible-Regular"));
        public static TMPro.TMP_FontAsset TmpRoyal => tmpRoyal ? tmpRoyal : (tmpRoyal = Tmp("CinzelDecorative-Bold"));
        public static TMPro.TMP_FontAsset TmpLcd => tmpLcd ? tmpLcd : (tmpLcd = Tmp("VT323"));
        public static TMPro.TMP_FontAsset TmpHand => tmpHand ? tmpHand : (tmpHand = Tmp("PatrickHand"));

        /// <summary>A 3D text label (TextMeshPro: sharp at every distance, no shimmer). size matches the old TextMesh scale.</summary>
        public static TMPro.TextMeshPro Label(string text, Transform parent, Vector3 localPos, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool billboard = true)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TMPro.TextMeshPro>();
            if (TmpBody != null) tm.font = TmpBody;
            tm.text = text;
            tm.fontSize = size * 6.4f;                       // same visual height as the old TextMesh (characterSize size/10 at font size 64)
            tm.color = color;
            tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tm.overflowMode = TMPro.TextOverflowModes.Overflow;
            tm.richText = true;
            var rt = tm.rectTransform; rt.sizeDelta = Vector2.zero;
            switch (anchor)
            {
                case TextAnchor.MiddleLeft: tm.alignment = TMPro.TextAlignmentOptions.Left; rt.pivot = new Vector2(0, 0.5f); break;
                case TextAnchor.MiddleRight: tm.alignment = TMPro.TextAlignmentOptions.Right; rt.pivot = new Vector2(1, 0.5f); break;
                case TextAnchor.UpperLeft: tm.alignment = TMPro.TextAlignmentOptions.TopLeft; rt.pivot = new Vector2(0, 1); break;
                case TextAnchor.UpperCenter: tm.alignment = TMPro.TextAlignmentOptions.Top; rt.pivot = new Vector2(0.5f, 1); break;
                case TextAnchor.LowerCenter: tm.alignment = TMPro.TextAlignmentOptions.Bottom; rt.pivot = new Vector2(0.5f, 0); break;
                default: tm.alignment = TMPro.TextAlignmentOptions.Center; rt.pivot = new Vector2(0.5f, 0.5f); break;
            }
            tm.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (billboard) go.AddComponent<Billboard>();
            return tm;
        }

        public static LineRenderer Line(string name, Transform parent, Material mat, Color color, float width, params Vector3[] localPoints)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = localPoints.Length;
            lr.SetPositions(localPoints);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.numCapVertices = 2;
            return lr;
        }

        /// <summary>Cone along +Y with base at y=0 and tip at y=1, radius 1.</summary>
        public static Mesh Cone(int segments = 24)
        {
            var m = new Mesh { name = "Cone" };
            var v = new Vector3[segments + 2];
            var t = new int[segments * 6];
            v[0] = Vector3.up; v[1] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                v[i + 2] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i + 2, b = (i + 1) % segments + 2;
                t[i * 6 + 0] = 0; t[i * 6 + 1] = b; t[i * 6 + 2] = a;
                t[i * 6 + 3] = 1; t[i * 6 + 4] = a; t[i * 6 + 5] = b;
            }
            m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>Torus in the XY plane (ring axis = +Z), for procedural glasses frames.</summary>
        public static Mesh Torus(float radius, float tube, int seg = 32, int sides = 8)
        {
            var m = new Mesh { name = "Torus" };
            var v = new Vector3[seg * sides];
            var t = new int[seg * sides * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                var c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                for (int j = 0; j < sides; j++)
                {
                    float b = j * Mathf.PI * 2 / sides;
                    v[i * sides + j] = c * (radius + tube * Mathf.Cos(b)) + Vector3.forward * (tube * Mathf.Sin(b));
                }
            }
            int n = 0;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * sides + j, a1 = i * sides + (j + 1) % sides, b0 = ((i + 1) % seg) * sides + j, b1 = ((i + 1) % seg) * sides + (j + 1) % sides;
                    t[n++] = a0; t[n++] = b0; t[n++] = a1; t[n++] = a1; t[n++] = b0; t[n++] = b1;
                }
            m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static Vector3 ToV3(double[] x) => new Vector3((float)x[0], (float)x[1], (float)x[2]);
        public static double[] ToD(Vector3 v) => new double[] { v.x, v.y, v.z };
    }

    /// <summary>Keeps a label facing the active camera.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        public Camera Cam;   // null = main camera
        void LateUpdate()
        {
            var cam = Cam ? Cam : Camera.main;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }

    /// <summary>Marks a draggable handle.</summary>
    public sealed class Handle : MonoBehaviour
    {
        public enum Kind { Translate, Rotate, Landscape, TurnRing, TiltRing }
        public Kind Type;
    }
}
