using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>Anything on the desk that a pointer ray can grab and drag (mouse and VR use the same path).</summary>
    public interface IDraggable { void Begin(Ray r); void Drag(Ray r); void End(); }

    /// <summary>
    /// The sorting machine's control desk: a slanted walnut-and-brass console in front of the table.
    /// Three brass dials set the weights (mass, length, width), a lever sets the offset b, a big knob sets
    /// steepness (from lesson 3), and the screen (the rule console canvas) shows the equation and score.
    /// The dials always DISPLAY the live rule, so when training runs you watch the machine turn its own dials.
    /// </summary>
    public sealed class ControlDesk : MonoBehaviour
    {
        public GameState State;
        public Transform ScreenAnchor { get; private set; }
        public bool ShowSteepness { get => steepRoot && steepRoot.gameObject.activeSelf; set { if (steepRoot) steepRoot.gameObject.SetActive(value); } }
        Transform plate, steepRoot;
        readonly Dial[] dials = new Dial[3];
        Dial steep;
        Lever lever;
        public const float Range = 6f, Sweep = 135f;   // weights -6..6 across +-135 degrees

        public void Build(Transform parent, QuackPalette pal)
        {
            transform.SetParent(parent, false);
            transform.localPosition = new Vector3(0, 0, -0.72f);                     // just in front of the lab table, below the plot
            var walnut = Tint(pal.Wood, new Color(0.3f, 0.17f, 0.08f));
            var brass = Tint(pal.Gold, new Color(0.85f, 0.65f, 0.28f), 0.8f, 0.7f);
            var face = Tint(pal.DuckDark, new Color(0.08f, 0.16f, 0.13f));          // dark green enamel
            Box("desk stand", walnut, new Vector3(0, 0.31f, 0.08f), new Vector3(1.2f, 0.62f, 0.3f));   // tucked under the tilted face (never pokes through it)
            plate = new GameObject("control plate").transform;
            plate.SetParent(transform, false);
            plate.localPosition = new Vector3(0, 0.76f, -0.05f); plate.localRotation = Quaternion.Euler(35, 0, 0);   // face tilted UP toward the learner (top edge leans away)
            PBox("face", face, new Vector3(0, 0, 0), new Vector3(1.34f, 0.4f, 0.03f));
            foreach (var (p, sz) in new[] { (new Vector3(0, 0.205f, -0.005f), new Vector3(1.37f, 0.015f, 0.04f)), (new Vector3(0, -0.205f, -0.005f), new Vector3(1.37f, 0.015f, 0.04f)),
                                            (new Vector3(-0.68f, 0, -0.005f), new Vector3(0.015f, 0.41f, 0.04f)), (new Vector3(0.68f, 0, -0.005f), new Vector3(0.015f, 0.41f, 0.04f)) })
                PBox("brass trim", brass, p, sz);
            // engraved brass title plate
            PBox("title plate", brass, new Vector3(-0.33f, 0.168f, -0.018f), new Vector3(0.34f, 0.042f, 0.006f));
            var title = VizUtil.Label("SORTER CONTROL", plate, new Vector3(-0.33f, 0.168f, -0.023f), 0.046f, new Color(0.12f, 0.07f, 0.02f), billboard: false);
            title.font = VizUtil.TmpRoyal; title.transform.localRotation = Quaternion.identity;

            string[] names = { "mass", "length", "width" };
            for (int i = 0; i < 3; i++)
                dials[i] = Dial.Create(plate, $"dial {names[i]}", new Vector3(-0.575f + i * 0.17f, -0.01f, -0.02f), 0.05f, brass, QuackPalette.FeatureColor[i], names[i], this, i);
            lever = Lever.Create(plate, new Vector3(-0.07f, -0.01f, -0.02f), 0.2f, brass, face, this);
            steepRoot = new GameObject("steepness").transform; steepRoot.SetParent(plate, false);
            steep = Dial.Create(steepRoot, "dial steepness", new Vector3(0.08f, -0.01f, -0.02f), 0.05f, brass, new Color(0.9f, 0.9f, 0.95f), "steepness", this, 3);
            steepRoot.gameObject.SetActive(false);

            // the screen: the rule console canvas sits here (equation, score, accessible sliders)
            ScreenAnchor = new GameObject("desk screen").transform;
            ScreenAnchor.SetParent(plate, false);
            // screen set INTO the desk face: dark recess flush with the enamel, framed by brass, glass just in front
            float sw = RuleConsole.ScreenMetresWide, sh = sw * RuleConsole.PixelH / RuleConsole.PixelW, sx = 0.415f, sy = -0.005f;
            ScreenAnchor.localPosition = new Vector3(sx, sy, -0.0175f);
            PBox("screen recess", Tint(pal.DuckDark, new Color(0.01f, 0.01f, 0.015f)), new Vector3(sx, sy, -0.0155f), new Vector3(sw + 0.01f, sh + 0.01f, 0.002f));
            foreach (var (p2, sz) in new[] { (new Vector3(sx, sy + sh / 2 + 0.011f, -0.019f), new Vector3(sw + 0.044f, 0.012f, 0.01f)), (new Vector3(sx, sy - sh / 2 - 0.011f, -0.019f), new Vector3(sw + 0.044f, 0.012f, 0.01f)),
                                             (new Vector3(sx - sw / 2 - 0.016f, sy, -0.019f), new Vector3(0.012f, sh + 0.034f, 0.01f)), (new Vector3(sx + sw / 2 + 0.016f, sy, -0.019f), new Vector3(0.012f, sh + 0.034f, 0.01f)) })
                PBox("screen frame", brass, p2, sz);
        }

        // ---- value mapping (shared by dials and lever) ----
        public float Get(int k)
        {
            var p = State.Params;
            return k < 3 ? (float)p.W[k] : k == 3 ? (float)p.WeightNorm() : (float)p.B;
        }

        public bool Enabled(int k) => !(k == 2 && State.WidthLocked) && (k != 3 || State.HasBoundary);

        public void Set(int k, float v)
        {
            if (!Enabled(k)) return;
            var p = State.Params.Clone();
            if (k < 3) { p.W[k] = Mathf.Clamp(v, -Range, Range); State.SetParams(p, ParamSource.Slider); }
            else if (k == 3) State.SetParams(PlaneGeometry.SetSteepness(State.Params, Mathf.Clamp(v, 0.2f, 8f)), ParamSource.Steepness);
            else { p.B = Mathf.Clamp(v, -Range, Range); State.SetParams(p, ParamSource.Slider); }
        }

        public void BeginEdit() => State.BeginEdit();
        public void EndEdit(int k) => State.EndEdit(k == 3 ? ParamSource.Steepness : ParamSource.Slider);

        void Update()
        {
            if (State == null) return;
            for (int i = 0; i < 3; i++) dials[i].Show(Get(i), Enabled(i));
            if (steepRoot.gameObject.activeSelf) steep.Show(Get(3), Enabled(3));
            lever.Show(Get(4));
        }

        // ---- helpers ----
        static Material Tint(Material src, Color c, float smooth = 0.5f, float metal = 0f)
        {
            var m = new Material(src); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }
        void Box(string n, Material m, Vector3 p, Vector3 s)
        {
            if (s == Vector3.zero) return;
            var g = VizUtil.Primitive(PrimitiveType.Cube, n, transform, m); g.transform.localPosition = p; g.transform.localScale = s;
        }
        void PBox(string n, Material m, Vector3 p, Vector3 s) { var g = VizUtil.Primitive(PrimitiveType.Cube, n, plate, m); g.transform.localPosition = p; g.transform.localScale = s; }

        /// <summary>Ray -> point on the slanted plate (plate-local), for dial/lever dragging.</summary>
        public bool PlatePoint(Ray r, out Vector2 local)
        {
            var pl = new Plane(-plate.forward, plate.position);
            local = default;
            if (!pl.Raycast(r, out float d)) return false;
            var w = plate.InverseTransformPoint(r.GetPoint(d));
            local = new Vector2(w.x, w.y);
            return true;
        }

        // ================= dial =================
        static Material lcdMat;
        /// <summary>Under a control: a brass nameplate with dark engraved lettering and a colour chip (colour is never the only cue).
        /// Above it: a small black LCD window with the live value in green digits.</summary>
        static (TMPro.TextMeshPro name, TMPro.TextMeshPro value) Plates(Transform root, string label, Color chip, Vector3 namePos, Vector3 valuePos, Material brass)
        {
            var np = VizUtil.Primitive(PrimitiveType.Cube, "nameplate", root, brass);
            np.transform.localPosition = namePos + new Vector3(0, 0, -0.004f); np.transform.localScale = new Vector3(0.135f, 0.034f, 0.005f);
            var cm = new Material(brass); cm.color = chip; if (cm.HasProperty("_BaseColor")) cm.SetColor("_BaseColor", chip);
            var ch = VizUtil.Primitive(PrimitiveType.Cube, "colour chip", root, cm);
            ch.transform.localPosition = namePos + new Vector3(-0.058f, 0, -0.0075f); ch.transform.localScale = new Vector3(0.012f, 0.022f, 0.002f);
            var name = VizUtil.Label(label, root, namePos + new Vector3(0.009f, 0, -0.0075f), 0.045f, new Color(0.1f, 0.06f, 0.02f), billboard: false);
            name.fontStyle = TMPro.FontStyles.Bold; name.transform.localRotation = Quaternion.identity;

            if (lcdMat == null) { lcdMat = new Material(brass); lcdMat.color = new Color(0.02f, 0.04f, 0.03f); if (lcdMat.HasProperty("_BaseColor")) lcdMat.SetColor("_BaseColor", lcdMat.color); if (lcdMat.HasProperty("_Smoothness")) lcdMat.SetFloat("_Smoothness", 0.9f); if (lcdMat.HasProperty("_Metallic")) lcdMat.SetFloat("_Metallic", 0f); }
            var win = VizUtil.Primitive(PrimitiveType.Cube, "lcd window", root, lcdMat);
            win.transform.localPosition = valuePos + new Vector3(0, 0, -0.004f); win.transform.localScale = new Vector3(0.11f, 0.04f, 0.005f);
            var value = VizUtil.Label("", root, valuePos + new Vector3(0, 0, -0.0075f), 0.075f, new Color(0.55f, 1f, 0.7f), billboard: false);
            if (VizUtil.TmpLcd != null) value.font = VizUtil.TmpLcd;
            value.transform.localRotation = Quaternion.identity;
            return (name, value);
        }

        public sealed class Dial : MonoBehaviour, IDraggable
        {
            ControlDesk desk; int k; Transform needle, knob; TMPro.TextMeshPro value; Vector2 centre; Renderer ring;
            float startAngle, startValue;

            public static Dial Create(Transform plate, string name, Vector3 pos, float r, Material brass, Color ringColor, string label, ControlDesk desk, int k)
            {
                var root = new GameObject(name).transform; root.SetParent(plate, false); root.localPosition = pos;
                var ringGo = VizUtil.Primitive(PrimitiveType.Cylinder, "colour ring", root, brass);
                ringGo.transform.localRotation = Quaternion.Euler(90, 0, 0); ringGo.transform.localScale = new Vector3(r * 2.6f, 0.004f, r * 2.6f);
                var rc = new Material(brass); rc.color = ringColor; if (rc.HasProperty("_BaseColor")) rc.SetColor("_BaseColor", ringColor);
                ringGo.GetComponent<Renderer>().sharedMaterial = rc;
                var knobGo = VizUtil.Primitive(PrimitiveType.Cylinder, "knob", root, brass, keepCollider: true);
                knobGo.transform.localRotation = Quaternion.Euler(90, 0, 0); knobGo.transform.localPosition = new Vector3(0, 0, -0.015f);
                knobGo.transform.localScale = new Vector3(r * 2f, 0.015f, r * 2f);
                var needleRoot = new GameObject("needle").transform; needleRoot.SetParent(root, false); needleRoot.localPosition = new Vector3(0, 0, -0.032f);
                var n = VizUtil.Primitive(PrimitiveType.Cube, "pointer", needleRoot, brass);
                var nm = new Material(brass); nm.color = Color.black; if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", Color.black);
                n.GetComponent<Renderer>().sharedMaterial = nm;
                n.transform.localPosition = new Vector3(0, r * 0.55f, 0); n.transform.localScale = new Vector3(r * 0.18f, r * 1.0f, 0.004f);
                var (lbl, val) = Plates(root, label, ringColor, new Vector3(0, -r * 1.85f, 0), new Vector3(0, r * 1.75f, 0), brass);
                var d = knobGo.AddComponent<Dial>(); d.desk = desk; d.k = k; d.needle = needleRoot; d.knob = knobGo.transform; d.value = val; d.centre = new Vector2(pos.x, pos.y); d.ring = ringGo.GetComponent<Renderer>();
                return d;
            }

            float Lo => k == 3 ? 0.2f : -Range;
            float Hi => k == 3 ? 8f : Range;

            public void Show(float v, bool enabled)
            {
                float t = Mathf.InverseLerp(Lo, Hi, v);
                needle.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(Sweep, -Sweep, t));
                value.text = enabled ? (k == 3 ? v.ToString("0.0") : v.ToString("+0.0;-0.0")) : "--";
            }

            float AngleOf(Vector2 p) => Mathf.Atan2(p.x - centre.x, p.y - centre.y) * Mathf.Rad2Deg;   // 0 = up, clockwise positive

            public void Begin(Ray r)
            {
                desk.BeginEdit();
                startValue = desk.Get(k);
                startAngle = desk.PlatePoint(r, out var p) ? AngleOf(p) : 0;
            }

            public void Drag(Ray r)
            {
                if (!desk.PlatePoint(r, out var p)) return;
                float delta = Mathf.DeltaAngle(startAngle, AngleOf(p));          // clockwise drag = higher value
                desk.Set(k, startValue + delta / (2 * Sweep) * (Hi - Lo));
            }

            public void End() => desk.EndEdit(k);
        }

        // ================= lever (offset b) =================
        public sealed class Lever : MonoBehaviour, IDraggable
        {
            ControlDesk desk; Transform grip; float half; Vector3 basePos; TMPro.TextMeshPro value;
            public static Lever Create(Transform plate, Vector3 pos, float length, Material brass, Material slotMat, ControlDesk desk)
            {
                var root = new GameObject("offset lever").transform; root.SetParent(plate, false); root.localPosition = pos;
                var slot = VizUtil.Primitive(PrimitiveType.Cube, "slot", root, slotMat);
                var sm = new Material(slotMat); sm.color = Color.black; if (sm.HasProperty("_BaseColor")) sm.SetColor("_BaseColor", Color.black);
                slot.GetComponent<Renderer>().sharedMaterial = sm;
                slot.transform.localScale = new Vector3(0.018f, length, 0.01f);
                var g = VizUtil.Primitive(PrimitiveType.Cube, "lever grip", root, brass, keepCollider: true);
                g.transform.localScale = new Vector3(0.07f, 0.035f, 0.04f); g.transform.localPosition = new Vector3(0, 0, -0.025f);
                var (lbl, val) = Plates(root, "offset b", QuackPalette.BiasColor, new Vector3(0, -length / 2 - 0.04f, 0), new Vector3(0, length / 2 + 0.035f, 0), brass);
                var l = g.AddComponent<Lever>(); l.desk = desk; l.grip = g.transform; l.half = length / 2; l.basePos = new Vector3(pos.x, pos.y, 0); l.value = val;
                return l;
            }

            public void Show(float b)
            {
                float t = Mathf.InverseLerp(-Range, Range, b);
                grip.localPosition = new Vector3(0, Mathf.Lerp(-half, half, t), -0.025f);
                value.text = b.ToString("+0.0;-0.0");
            }

            public void Begin(Ray r) => desk.BeginEdit();
            public void Drag(Ray r)
            {
                if (!desk.PlatePoint(r, out var p)) return;
                float t = Mathf.InverseLerp(-half, half, p.y - basePos.y);
                desk.Set(4, Mathf.Lerp(-Range, Range, t));
            }
            public void End() => desk.EndEdit(4);
        }
    }
}
