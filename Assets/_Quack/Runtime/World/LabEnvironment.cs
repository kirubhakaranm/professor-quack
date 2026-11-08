using UnityEngine;

namespace Quack
{
    /// <summary>
    /// The factory lab the learner stands in — one world for desktop and VR (metres, y up, learner faces +z).
    /// The hologram plot floats above a lab table; consoles stand either side; Quack and the sorting line share
    /// the room. Anchors are the single source of layout truth for every view and for Quack's gaze.
    /// </summary>
    public sealed class LabEnvironment : MonoBehaviour
    {
        public Transform PlotAnchor, PlotAsideAnchor, LandscapeAnchor, TrainingAnchor, ConsoleAnchor, MissionAnchor, BoardAnchor, QuackSpot, ConveyorSpot, LearnerSpot, TVAnchor, ShowcaseAnchor, TrainingDeskAnchor, StartAnchor;

        public const float PlotSize = 0.7f;   // hologram cube edge, metres

        public static LabEnvironment Build(QuackPalette pal)
        {
            var root = new GameObject("Lab").AddComponent<LabEnvironment>();
            var t = root.transform;

            // a cosy, enclosed room: warm wood floor, warm walls close around the learner, soft warm light
            Box(t, "floor (wood)", pal.Wood, new Vector3(0, -0.05f, -0.15f), new Vector3(7.6f, 0.1f, 8.1f));   // roomier: nothing jam-packed
            Shell(t, "back wall", pal.LabWall, new Vector3(0, 2.0f, 3.9f), new Vector3(7.6f, 4.0f, 0.12f));
            Shell(t, "front wall", pal.LabWall, new Vector3(0, 2.0f, -4.2f), new Vector3(7.6f, 4.0f, 0.12f));
            Shell(t, "left wall", pal.LabWall, new Vector3(-3.8f, 2.0f, -0.15f), new Vector3(0.12f, 4.0f, 8.1f));
            Shell(t, "right wall", pal.LabWall, new Vector3(3.8f, 2.0f, -0.15f), new Vector3(0.12f, 4.0f, 8.1f));
            Shell(t, "ceiling", pal.LabWall, new Vector3(0, 4.05f, -0.15f), new Vector3(7.6f, 0.1f, 8.1f));   // taller room: the emporium sign fits above everything
            Box(t, "skirting", pal.Wood, new Vector3(0, 0.06f, 3.83f), new Vector3(7.6f, 0.12f, 0.03f));
            Box(t, "rug", pal.Rug, new Vector3(0, 0.005f, -0.4f), new Vector3(3.4f, 0.01f, 3.0f));
            BuildSign(t, pal);
            foreach (var lp in new[] { new Vector3(-2.2f, 3.6f, -1.8f), new Vector3(2.2f, 3.6f, 1.6f) })   // the bigger room gets two more warm lights
            {
                var l2 = new GameObject("warm ceiling light (side)").AddComponent<Light>();
                l2.transform.SetParent(t, false); l2.transform.localPosition = lp;
                l2.type = LightType.Point; l2.range = 6.5f; l2.intensity = 1.1f; l2.color = new Color(1f, 0.86f, 0.68f);
            }
            var lamp = new GameObject("warm ceiling light").AddComponent<Light>();
            lamp.transform.SetParent(t, false); lamp.transform.localPosition = new Vector3(0, 3.6f, 0.2f);
            lamp.type = LightType.Point; lamp.range = 8f; lamp.intensity = 2.0f; lamp.color = new Color(1f, 0.86f, 0.68f);

            // lab table with a glowing hologram emitter
            Box(t, "table top", pal.LabTable, new Vector3(0, 0.84f, 0), new Vector3(1.3f, 0.06f, 0.85f));
            Box(t, "table base", pal.DuckDark, new Vector3(0, 0.42f, 0), new Vector3(0.25f, 0.8f, 0.25f));
            var emitter = VizUtil.Primitive(PrimitiveType.Cylinder, "hologram emitter", t, pal.Handle);
            emitter.transform.localPosition = new Vector3(0, 0.88f, 0);
            emitter.transform.localScale = new Vector3(0.42f, 0.012f, 0.42f);

            root.PlotAnchor = Anchor(t, "plot anchor", new Vector3(0, 1.32f, 0), 0);
            root.LearnerSpot = Anchor(t, "learner spot", new Vector3(0, 0, -1.15f), 0);
            root.PlotAsideAnchor = Anchor(t, "plot aside anchor", new Vector3(-0.36f, 1.32f, 0.05f), 0);
            root.LandscapeAnchor = Anchor(t, "landscape anchor", new Vector3(0.38f, 1.0f, 0.05f), 0);
            root.TrainingAnchor = Anchor(t, "training desk anchor", new Vector3(0, 0.95f, -0.5f), 0);
            root.TrainingAnchor.localRotation = Quaternion.Euler(40, 0, 0);   // tilted up toward the learner
            // consoles stand out to the sides, level with the plot (not between the learner and it), angled inward
            root.ConsoleAnchor = Anchor(t, "console anchor", new Vector3(1.12f, 1.28f, -0.2f), 32);   // high enough that the trays show beneath it
            // the mission screen hangs above the hologram, at its back edge (never between the learner and the plot)
            root.MissionAnchor = Anchor(t, "mission anchor", new Vector3(0, 1.97f, 0.38f), 0);
            // whiteboard on the RIGHT wall (faces -x), TV on the LEFT wall (faces +x)
            root.BoardAnchor = Anchor(t, "whiteboard anchor", new Vector3(3.72f, 1.75f, 0.5f), 90);
            root.TVAnchor = Anchor(t, "tv anchor", new Vector3(-3.7f, 1.75f, 1.9f), -90);   // further back: Professor Quack never stands in front of it
            root.StartAnchor = Anchor(t, "start screen anchor", new Vector3(0, 1.4f, -0.3f), 0);   // in front of the table, ~0.85 m from the VR learner
            root.ShowcaseAnchor = Anchor(t, "showcase anchor", new Vector3(0, 0.87f, -0.22f), 0);   // on the table top: the measuring station
            root.QuackSpot = Anchor(t, "quack spot", new Vector3(-1.9f, 0, 0.7f), 155);      // left of the table, clear of the visuals
            root.ConveyorSpot = Anchor(t, "conveyor spot", new Vector3(2.1f, 0, 1.9f), -12);   // trays clear of the right wall // right side, in view: rule changes show on the belt at once
            root.TrainingDeskAnchor = Anchor(t, "training desk anchor", new Vector3(0.8f, 1.05f, -0.3f), 26);   // stage E: right beside the landscape

            // the TV: dark bezel on the left wall (the screen canvas is added by TVScreen)
            BuildTV(root.TVAnchor, pal);
            BuildWhiteboard(root.BoardAnchor, pal);

            // stands under the floating consoles so they read as physical objects
            Box(t, "training desk stand", pal.DuckDark, new Vector3(0.8f, 0.45f, -0.25f), new Vector3(0.05f, 0.9f, 0.05f));
            // the screen's bezel and the two cables it hangs from
            Box(t, "mission screen bezel", pal.DuckDark, new Vector3(0, 1.97f, 0.40f), new Vector3(1.36f, 0.43f, 0.03f));
            Box(t, "mission cable L", pal.DuckDark, new Vector3(-0.55f, 3.1f, 0.40f), new Vector3(0.01f, 1.9f, 0.01f));
            Box(t, "mission cable R", pal.DuckDark, new Vector3(0.55f, 3.1f, 0.40f), new Vector3(0.01f, 1.9f, 0.01f));
            return root;
        }

        static Material Tinted(Material src, Color c, string name)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.75f);
            m.color = c;
            return m;
        }

        static void Part(Transform parent, string name, Material m, Vector3 localPos, Vector3 size, PrimitiveType type = PrimitiveType.Cube)
        {
            var g = VizUtil.Primitive(type, name, parent, m);
            g.transform.localPosition = localPos; g.transform.localScale = size;
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>Wall plaque: walnut board, gold trim, raised gold lettering in the royal face, two small spotlights.</summary>
        static void BuildSign(Transform t, QuackPalette pal)
        {
            var plaque = new GameObject("emporium sign").transform;
            plaque.SetParent(t, false); plaque.localPosition = new Vector3(0, 3.3f, 3.82f);   // high on the back wall, above the hanging mission screen
            var walnut = Tinted(pal.Wood, new Color(0.32f, 0.18f, 0.09f), "walnut");
            var gold = Tinted(pal.Gold, new Color(0.95f, 0.72f, 0.25f), "sign gold");
            Part(plaque, "board", walnut, new Vector3(0, 0, 0.01f), new Vector3(4.5f, 0.62f, 0.05f));   // wide enough for the whole title
            foreach (var (pos, size) in new[] { (new Vector3(0, 0.29f, -0.02f), new Vector3(4.44f, 0.02f, 0.02f)), (new Vector3(0, -0.29f, -0.02f), new Vector3(4.44f, 0.02f, 0.02f)),
                                                (new Vector3(-2.21f, 0, -0.02f), new Vector3(0.02f, 0.6f, 0.02f)), (new Vector3(2.21f, 0, -0.02f), new Vector3(0.02f, 0.6f, 0.02f)) })
                Part(plaque, "gold trim", gold, pos, size);
            // raised letters: a dark shadow layer offset behind, then the gold face
            foreach (var (off, col, nm) in new[] { (new Vector3(0.006f, -0.007f, -0.022f), new Color(0.12f, 0.06f, 0.02f), "lettering shadow"),
                                                   (new Vector3(0, 0, -0.03f), new Color(1f, 0.8f, 0.32f), "lettering") })
            {
                var tm = VizUtil.Label("PROFESSOR QUACK'S EGG & AI EMPORIUM", plaque, off, 0.24f, col, billboard: false);
                tm.name = nm; if (VizUtil.TmpRoyal != null) tm.font = VizUtil.TmpRoyal;
                tm.transform.localRotation = Quaternion.identity;
            }
            foreach (float x in new[] { -1.4f, 1.4f })
            {
                var spot = new GameObject("sign spotlight").AddComponent<Light>();
                spot.transform.SetParent(plaque, false); spot.transform.localPosition = new Vector3(x, 0.55f, -0.45f);
                spot.transform.LookAt(plaque.position + new Vector3(x * 0.6f, 0, 0));
                spot.type = LightType.Spot; spot.spotAngle = 70; spot.range = 2.5f; spot.intensity = 1.6f; spot.color = new Color(1f, 0.88f, 0.7f);
            }
        }

        /// <summary>A real-looking TV: rear housing, glossy bezel frame with depth, wall bracket, power LED, badge, screen glow.
        /// Built in the TV anchor's frame (x across the screen, y up, -z toward the viewer); TVScreen puts the picture inside.</summary>
        static void BuildTV(Transform a, QuackPalette pal)
        {
            const float W = 1.55f, H = 0.872f, B = 0.055f;   // picture size (1280x720 at 1.55 m) and bezel width
            var tv = new GameObject("TV").transform; tv.SetParent(a, false);
            var black = Tinted(pal.DuckDark, new Color(0.04f, 0.04f, 0.05f), "tv gloss black");
            var housing = Tinted(pal.DuckDark, new Color(0.08f, 0.08f, 0.09f), "tv housing");
            Part(tv, "rear housing", housing, new Vector3(0, 0, 0.04f), new Vector3(W + 2 * B - 0.04f, H + 2 * B - 0.04f, 0.07f));
            Part(tv, "bezel top", black, new Vector3(0, H / 2 + B / 2, -0.01f), new Vector3(W + 2 * B, B, 0.05f));
            Part(tv, "bezel bottom", black, new Vector3(0, -H / 2 - B / 2 - 0.01f, -0.01f), new Vector3(W + 2 * B, B + 0.02f, 0.05f));
            Part(tv, "bezel left", black, new Vector3(-W / 2 - B / 2, 0, -0.01f), new Vector3(B, H, 0.05f));
            Part(tv, "bezel right", black, new Vector3(W / 2 + B / 2, 0, -0.01f), new Vector3(B, H, 0.05f));
            Part(tv, "wall bracket", housing, new Vector3(0, 0, 0.09f), new Vector3(0.35f, 0.25f, 0.03f));
            var led = Tinted(pal.Gold, new Color(0.2f, 1f, 0.35f), "power led");
            if (led.HasProperty("_EmissionColor")) { led.EnableKeyword("_EMISSION"); led.SetColor("_EmissionColor", new Color(0.1f, 0.9f, 0.3f) * 2f); }
            Part(tv, "power LED", led, new Vector3(W / 2 + B / 2 - 0.05f, -H / 2 - B / 2 - 0.01f, -0.037f), Vector3.one * 0.012f, PrimitiveType.Sphere);
            var badge = VizUtil.Label("QuackVision", tv, new Vector3(0, -H / 2 - B / 2 - 0.01f, -0.037f), 0.05f, new Color(0.75f, 0.75f, 0.78f), billboard: false);
            badge.transform.localRotation = Quaternion.identity;
            var glow = new GameObject("screen glow").AddComponent<Light>();
            glow.transform.SetParent(tv, false); glow.transform.localPosition = new Vector3(0, 0, -0.5f);
            glow.type = LightType.Point; glow.range = 2.2f; glow.intensity = 0.6f; glow.color = new Color(0.55f, 0.7f, 1f);
        }

        /// <summary>A real whiteboard, 1.8 x 1.2 m: aluminium frame with corner caps, marker tray with markers and an eraser.
        /// The writing surface is the WhiteboardView canvas at the same anchor.</summary>
        static void BuildWhiteboard(Transform a, QuackPalette pal)
        {
            const float W = 1.8f, H = 1.2f, F = 0.035f;
            var wb = new GameObject("Whiteboard frame").transform; wb.SetParent(a, false);
            var alu = Tinted(pal.LabTable, new Color(0.78f, 0.8f, 0.83f), "aluminium");
            var cap = Tinted(pal.DuckDark, new Color(0.25f, 0.26f, 0.28f), "corner cap");
            Part(wb, "backing", Tinted(pal.White, new Color(0.96f, 0.96f, 0.95f), "board white"), new Vector3(0, 0, 0.015f), new Vector3(W, H, 0.02f));
            Part(wb, "frame top", alu, new Vector3(0, H / 2 + F / 2, -0.005f), new Vector3(W + 2 * F, F, 0.035f));
            Part(wb, "frame bottom", alu, new Vector3(0, -H / 2 - F / 2, -0.005f), new Vector3(W + 2 * F, F, 0.035f));
            Part(wb, "frame left", alu, new Vector3(-W / 2 - F / 2, 0, -0.005f), new Vector3(F, H, 0.035f));
            Part(wb, "frame right", alu, new Vector3(W / 2 + F / 2, 0, -0.005f), new Vector3(F, H, 0.035f));
            foreach (var x in new[] { -1f, 1f }) foreach (var y in new[] { -1f, 1f })
                Part(wb, "corner cap", cap, new Vector3(x * (W / 2 + F / 2), y * (H / 2 + F / 2), -0.01f), new Vector3(F * 1.5f, F * 1.5f, 0.045f));
            Part(wb, "marker tray", alu, new Vector3(0, -H / 2 - F - 0.015f, -0.05f), new Vector3(W * 0.8f, 0.012f, 0.09f));
            Part(wb, "tray lip", alu, new Vector3(0, -H / 2 - F - 0.0f, -0.093f), new Vector3(W * 0.8f, 0.03f, 0.006f));
            var colors = new[] { new Color(0.1f, 0.25f, 0.7f), new Color(0.75f, 0.12f, 0.1f), new Color(0.08f, 0.08f, 0.08f) };
            for (int i = 0; i < 3; i++)
            {
                var m = VizUtil.Primitive(PrimitiveType.Cylinder, "marker", wb, Tinted(pal.DuckDark, colors[i], "marker"));
                m.transform.localPosition = new Vector3(-0.45f + i * 0.17f, -H / 2 - F - 0.0f, -0.05f);
                m.transform.localRotation = Quaternion.Euler(0, 0, 90); m.transform.localScale = new Vector3(0.022f, 0.065f, 0.022f);
            }
            Part(wb, "eraser", Tinted(pal.DuckDark, new Color(0.15f, 0.15f, 0.2f), "eraser"), new Vector3(0.45f, -H / 2 - F + 0.005f, -0.05f), new Vector3(0.15f, 0.04f, 0.06f));
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos, float yaw)
        {
            var a = new GameObject(name).transform;
            a.SetParent(parent, false);
            a.localPosition = pos;
            a.localRotation = Quaternion.Euler(0, yaw, 0);
            return a;
        }

        /// <summary>Room shell piece: receives light/shadows but never blocks the key light (enclosed room stays bright).</summary>
        static void Shell(Transform parent, string name, Material mat, Vector3 pos, Vector3 size)
        {
            Box(parent, name, mat, pos, size);
            parent.Find(name).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 size)
        {
            var go = VizUtil.Primitive(PrimitiveType.Cube, name, parent, mat);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.GetComponent<Renderer>().receiveShadows = true;
        }
    }
}
