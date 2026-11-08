using UnityEngine;

namespace Quack
{
    /// <summary>
    /// Materials and colours, created as assets by the editor setup script so builds include their shaders.
    /// Colour is never the only cue: classes also differ by shape (sphere vs cube) and text.
    /// </summary>
    [CreateAssetMenu(menuName = "Quack/Palette")]
    public sealed class QuackPalette : ScriptableObject
    {
        [Header("Opaque lit")]
        public Material StandardEgg;   // blue sphere  ● STANDARD label
        public Material JumboEgg;      // orange cube ■ JUMBO label
        public Material Duck;          // Quacks body
        public Material DuckBeak;
        public Material DuckDark;      // eyes, tie knot
        public Material Tie;           // burgundy bow tie
        public Material Navy, Gold, Cream, White, Black, Brown;   // Design A outfit + face
        public Material Factory;       // floor, table, belt
        public Material FactoryAccent;
        public Material LabWall;
        public Material Wood, Rug, Cardboard;
        public Material LabTable;

        [Header("Unlit")]
        public Material Line;          // axes, drop lines (vertex/tint via color)
        public Material Arrow;         // weight vector
        public Material Marker;        // misrouted × marker
        public Material Handle;        // teal drag handles (distinct from every data colour)

        [Header("Transparent")]
        public Material Plane;         // decision plane (double-sided)
        public Material Highlight;     // selection halo
        public Material Band;          // p = 0.25 / 0.75 confidence sheets
        public Material Surface;       // loss landscape (vertex colours)

        public static readonly Color StandardColor = new Color(0.23f, 0.49f, 0.85f);
        public static readonly Color JumboColor = new Color(1.00f, 0.76f, 0.10f);   // golden yellow (blue vs yellow: clear for most colour vision)
        // one colour per model term, shared by the equation, the axis titles and the console values
        // (distinct from the egg classes' orange/blue; always shown next to a name, never colour alone)
        public static readonly Color[] FeatureColor =
        {
            new Color(0.82f, 0.12f, 0.12f),   // mass   (red)
            new Color(0.08f, 0.52f, 0.18f),   // length (green)
            new Color(0.48f, 0.2f, 0.78f),    // width  (purple)
        };
        public static readonly Color BiasColor = new Color(0f, 0.45f, 0.42f);   // b (teal, like the slide disc)
        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public static readonly Color SelectColor = new Color(1.00f, 0.83f, 0.00f);
    }
}
