using UnityEngine;

namespace Quack
{
    /// <summary>
    /// The pivots Professor Quack's animation drives. Character = Design A (reference/prev_asset).
    /// Hybrid build (approved): the BODY can come from a generated mesh (bones: body, head, jaw, wing_L, wing_R),
    /// while the FACE is always separate code-controlled geometry attached to the head: eyes with pupils (gaze),
    /// eyelids (blink), eyebrows (expression), procedural black round glasses, and a two-part beak.
    /// Until a clean generated body exists, BuildPlaceholder assembles a Design A stand-in from primitives.
    /// Faces +Z, feet at the origin, ~1.05 m tall.
    /// </summary>
    public sealed class QuacksRig : MonoBehaviour
    {
        public Transform Body, Head, Jaw, LidL, LidR, WingL, WingR, Tie;
        /// <summary>Option A rigid parts (generated V2 rig); null on older rigs.</summary>
        public Transform FootL, FootR, Tail;
        public Transform EyeL, EyeR, BrowL, BrowR, Glasses;
        public Vector3 LidOpenL, LidOpenR, BrowBaseL, BrowBaseR;
        /// <summary>Bone rigs: world-space rest direction of each wing (shoulder -> tip), for pointing.</summary>
        public Vector3 WingRestL, WingRestR;
        /// <summary>How far the beak opens at full speech amplitude (degrees).</summary>
        public float JawOpenDegrees = 28f;
        /// <summary>Scales eyebrow travel (a generated head is smaller than the stand-in's face).</summary>
        public float BrowTravel = 1f;
        /// <summary>+1 for the stand-in; -1 for generated rigs (lifts/flaps/shrugs swing the wings outward, not into the body).</summary>
        public float WingZSign = 1f;
        public const float LidTravel = 0.118f;   // lid rests just above the eye (wide-open Design A look) and drops this far to close

        // ---------------- Design A stand-in body ----------------

        public static QuacksRig BuildPlaceholder(Transform parent, QuackPalette pal)
        {
            var root = new GameObject("Professor Quack (Design A stand-in)");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<QuacksRig>();

            rig.Body = Pivot("body", root.transform, Vector3.zero);
            var b = rig.Body;
            Part(PrimitiveType.Sphere, "torso (feathers)", b, pal.Duck, new Vector3(0, 0.42f, 0), new Vector3(0.6f, 0.6f, 0.52f));
            Part(PrimitiveType.Sphere, "blazer", b, pal.Navy, new Vector3(0, 0.48f, -0.015f), new Vector3(0.63f, 0.48f, 0.55f));
            Part(PrimitiveType.Sphere, "vest", b, pal.Cream, new Vector3(0, 0.47f, 0.235f), new Vector3(0.2f, 0.32f, 0.09f));
            Part(PrimitiveType.Sphere, "shirt collar", b, pal.White, new Vector3(0, 0.635f, 0.19f), new Vector3(0.2f, 0.07f, 0.1f));
            foreach (float s in new[] { -1f, 1f })
            {
                var lapel = Part(PrimitiveType.Cube, "gold lapel piping", b, pal.Gold, new Vector3(s * 0.088f, 0.5f, 0.262f), new Vector3(0.012f, 0.27f, 0.012f));
                lapel.localRotation = Quaternion.Euler(0, 0, s * -14f);
                Part(PrimitiveType.Sphere, "blazer button", b, pal.Gold, new Vector3(s * 0.13f, 0.4f, 0.245f), Vector3.one * 0.024f);
            }
            var hem = new GameObject("gold hem piping");
            hem.transform.SetParent(b, false);
            hem.transform.localPosition = new Vector3(0, 0.3f, -0.005f);
            hem.transform.localRotation = Quaternion.Euler(90, 0, 0);
            hem.transform.localScale = new Vector3(1.0f, 0.88f, 1f);
            hem.AddComponent<MeshFilter>().sharedMesh = VizUtil.Torus(0.293f, 0.007f, 48, 6);
            hem.AddComponent<MeshRenderer>().sharedMaterial = pal.Gold;
            Part(PrimitiveType.Sphere, "vest button", b, pal.Gold, new Vector3(0, 0.5f, 0.28f), Vector3.one * 0.02f);
            Part(PrimitiveType.Sphere, "vest button", b, pal.Gold, new Vector3(0, 0.43f, 0.278f), Vector3.one * 0.02f);
            // breast pocket with pens on his left chest (+X when facing +Z)
            Part(PrimitiveType.Cube, "pocket", b, pal.Navy, new Vector3(0.15f, 0.53f, 0.235f), new Vector3(0.08f, 0.05f, 0.02f));
            var penMats = new[] { pal.Tie, pal.White, pal.StandardEgg };
            for (int i = 0; i < 3; i++)
                Part(PrimitiveType.Cylinder, "pen", b, penMats[i], new Vector3(0.13f + i * 0.02f, 0.575f, 0.238f), new Vector3(0.01f, 0.03f, 0.01f));
            Part(PrimitiveType.Sphere, "tail", b, pal.Duck, new Vector3(0, 0.33f, -0.28f), new Vector3(0.2f, 0.16f, 0.18f)).localRotation = Quaternion.Euler(-30, 0, 0);
            foreach (float s in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cylinder, "leg", b, pal.DuckBeak, new Vector3(s * 0.1f, 0.08f, 0.02f), new Vector3(0.06f, 0.08f, 0.06f));
                Part(PrimitiveType.Sphere, "webbed foot", b, pal.DuckBeak, new Vector3(s * 0.11f, 0.015f, 0.07f), new Vector3(0.15f, 0.028f, 0.16f));
                for (int toe = -1; toe <= 1; toe++)
                {
                    var tt = Part(PrimitiveType.Sphere, "toe", b, pal.DuckBeak, new Vector3(s * 0.11f + toe * 0.045f, 0.016f, 0.15f), new Vector3(0.05f, 0.03f, 0.11f));
                    tt.localRotation = Quaternion.Euler(0, toe * 18f, 0);
                }
            }

            // burgundy bow tie (Tie pivot is animated by the tie_adjust gesture)
            rig.Tie = Pivot("bow tie", b, new Vector3(0, 0.64f, 0.245f));
            Part(PrimitiveType.Sphere, "knot", rig.Tie, pal.Tie, Vector3.zero, Vector3.one * 0.04f);
            Part(PrimitiveType.Sphere, "bow L", rig.Tie, pal.Tie, new Vector3(0.05f, 0, 0), new Vector3(0.075f, 0.05f, 0.035f));
            Part(PrimitiveType.Sphere, "bow R", rig.Tie, pal.Tie, new Vector3(-0.05f, 0, 0), new Vector3(0.075f, 0.05f, 0.035f));

            // wings with navy sleeves and white cuffs
            rig.WingL = Pivot("shoulder L", b, new Vector3(0.3f, 0.56f, 0));
            rig.WingR = Pivot("shoulder R", b, new Vector3(-0.3f, 0.56f, 0));
            foreach (var (w, s) in new[] { (rig.WingL, 1f), (rig.WingR, -1f) })
            {
                Part(PrimitiveType.Sphere, "sleeve", w, pal.Navy, new Vector3(s * 0.02f, -0.06f, 0), new Vector3(0.15f, 0.2f, 0.2f));
                Part(PrimitiveType.Sphere, "cuff", w, pal.White, new Vector3(s * 0.035f, -0.17f, 0.01f), new Vector3(0.13f, 0.045f, 0.16f));
                Part(PrimitiveType.Sphere, "cuff piping", w, pal.Gold, new Vector3(s * 0.033f, -0.145f, 0.01f), new Vector3(0.145f, 0.012f, 0.175f));
                Part(PrimitiveType.Sphere, "wing hand", w, pal.Duck, new Vector3(s * 0.04f, -0.25f, 0.02f), new Vector3(0.11f, 0.17f, 0.15f));
            }

            rig.Head = Pivot("neck pivot", b, new Vector3(0, 0.66f, 0.02f));
            BuildHead(rig, rig.Head, pal);
            return rig;
        }

        /// <summary>Procedural Design A head (feathered sphere + tuft) with the controllable face. Used by the
        /// stand-in and on top of a generated body whose head was stripped.</summary>
        public static void BuildHead(QuacksRig rig, Transform neck, QuackPalette pal)
        {
            Part(PrimitiveType.Sphere, "head", neck, pal.Duck, new Vector3(0, 0.25f, 0), new Vector3(0.52f, 0.5f, 0.5f));
            foreach (float s in new[] { -1f, 1f })   // fluffy cheeks
                Part(PrimitiveType.Sphere, "cheek fluff", neck, pal.Duck, new Vector3(s * 0.17f, 0.17f, 0.1f), new Vector3(0.2f, 0.17f, 0.2f));
            for (int i = 0; i < 5; i++)                // curved tuft
            {
                float k = i - 2f;
                var feather = Pivot("tuft feather", neck, new Vector3(k * 0.022f, 0.48f, -0.01f));
                feather.localRotation = Quaternion.Euler(-20 + Mathf.Abs(k) * 4, 0, k * -22f);
                Part(PrimitiveType.Sphere, "feather", feather, pal.Duck, new Vector3(0, 0.06f, 0), new Vector3(0.028f, 0.13f, 0.03f));
                Part(PrimitiveType.Sphere, "feather tip", feather, pal.Duck, new Vector3(k * 0.012f, 0.12f, 0.015f), new Vector3(0.022f, 0.05f, 0.022f));
            }
            BuildFace(rig, neck, pal, Vector3.zero, 1f);
        }

        // ---------------- the controllable face (shared by stand-in and generated body) ----------------

        /// <summary>
        /// Attach eyes, lids, brows, glasses and beak to a head transform. offset/scale place the face on a
        /// generated head (head centre at local (0, 0.25, 0) and radius 0.25 in the stand-in's units).
        /// </summary>
        public static void BuildFace(QuacksRig rig, Transform head, QuackPalette pal, Vector3 offset, float scale)
        {
            var face = Pivot("face (code-controlled)", head, offset);
            face.localScale = Vector3.one * scale;

            foreach (float s in new[] { 1f, -1f })   // +X = his left
            {
                var eye = Pivot(s > 0 ? "eye L" : "eye R", face, new Vector3(s * 0.088f, 0.29f, 0.198f));
                Part(PrimitiveType.Sphere, "sclera", eye, pal.White, Vector3.zero, Vector3.one * 0.12f);
                Part(PrimitiveType.Sphere, "iris", eye, pal.Brown, new Vector3(0, 0, 0.036f), new Vector3(0.068f, 0.068f, 0.05f));
                Part(PrimitiveType.Sphere, "pupil", eye, pal.Black, new Vector3(0, 0, 0.052f), new Vector3(0.04f, 0.04f, 0.03f));
                Part(PrimitiveType.Sphere, "catchlight", eye, pal.White, new Vector3(0.013f, 0.015f, 0.066f), Vector3.one * 0.017f);
                var lid = Pivot(s > 0 ? "lid L" : "lid R", face, new Vector3(s * 0.088f, 0.29f + LidTravel, 0.19f));
                Part(PrimitiveType.Sphere, "lid", lid, pal.Duck, Vector3.zero, Vector3.one * 0.132f);
                var brow = Pivot(s > 0 ? "brow L" : "brow R", face, new Vector3(s * 0.085f, 0.4f, 0.215f));
                var arc = Part(PrimitiveType.Sphere, "brow", brow, pal.Brown, Vector3.zero, new Vector3(0.095f, 0.026f, 0.032f));
                arc.localRotation = Quaternion.Euler(0, 0, s * -8f);   // gentle arch, outer ends lower
                if (s > 0) { rig.EyeL = eye; rig.LidL = lid; rig.BrowL = brow; } else { rig.EyeR = eye; rig.LidR = lid; rig.BrowR = brow; }
            }
            rig.LidOpenL = rig.LidL.localPosition; rig.LidOpenR = rig.LidR.localPosition;
            rig.BrowBaseL = rig.BrowL.localPosition; rig.BrowBaseR = rig.BrowR.localPosition;

            // procedural black round glasses: two rings, bridge, temple arms
            rig.Glasses = Pivot("glasses", face, new Vector3(0, 0.29f, 0.265f));
            var ring = VizUtil.Torus(0.066f, 0.0095f);
            foreach (float s in new[] { 1f, -1f })
            {
                var lens = new GameObject("rim");
                lens.transform.SetParent(rig.Glasses, false);
                lens.transform.localPosition = new Vector3(s * 0.088f, 0, 0);
                lens.AddComponent<MeshFilter>().sharedMesh = ring;
                lens.AddComponent<MeshRenderer>().sharedMaterial = pal.Black;
                var arm = Part(PrimitiveType.Cube, "temple arm", rig.Glasses, pal.Black, new Vector3(s * 0.15f, 0.005f, -0.1f), new Vector3(0.008f, 0.008f, 0.2f));
                arm.localRotation = Quaternion.Euler(0, s * -12f, 0);
            }
            Part(PrimitiveType.Cube, "bridge", rig.Glasses, pal.Black, new Vector3(0, 0.012f, 0), new Vector3(0.05f, 0.008f, 0.008f));

            // two-part beak: the lower half hinges (speech)
            var bill = Part(PrimitiveType.Sphere, "upper bill", face, pal.DuckBeak, new Vector3(0, 0.198f, 0.25f), new Vector3(0.25f, 0.062f, 0.19f));
            bill.localRotation = Quaternion.Euler(-6, 0, 0);
            rig.Jaw = Pivot("jaw hinge", face, new Vector3(0, 0.178f, 0.19f));
            var lower = Part(PrimitiveType.Sphere, "lower bill", rig.Jaw, pal.DuckBeak, new Vector3(0, -0.01f, 0.065f), new Vector3(0.21f, 0.045f, 0.15f));
            lower.localRotation = Quaternion.Euler(4, 0, 0);
        }

        static Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Part(PrimitiveType type, string name, Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = VizUtil.Primitive(type, name, parent, mat);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go.transform;
        }
    }
}
