using UnityEngine;

namespace Quack
{
    /// <summary>A feather eyelid over a baked eye: grows down from the eye's top edge as the animator lowers the lid pivot.</summary>
    public sealed class LidCover : MonoBehaviour
    {
        public Transform Pivot;
        public float Size = 0.1f;
        public Vector3 Open;   // the pivot's fully-open position (set by the loader before the animator runs)
        void LateUpdate()
        {
            if (Pivot == null) return;
            // baked eyes are drawn wide open: the stand-in's resting lid level must not cover them, so only the
            // last part of the lid travel (blinks, very heavy-lidded expressions) shows the feather lid
            float travel = Mathf.Clamp01((Open - Pivot.localPosition).magnitude / QuacksRig.LidTravel);
            float closed = Mathf.InverseLerp(0.55f, 1f, travel);
            transform.localScale = new Vector3(Size, Mathf.Max(0.0001f, Size * closed), Size);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = closed > 0.02f;
        }
    }

    /// <summary>
    /// Loads a processed generated body (tools/blender/prepare_quacks.py → FBX in Resources/Characters/) and maps
    /// its bones (root, body, head, jaw, wing_L, wing_R) onto QuacksRig so the existing animator drives it.
    /// If the head was stripped (--strip-head), the procedural Design A head + controllable face is attached to
    /// the head bone. Returns null when no model is available, so the caller keeps the stand-in.
    /// </summary>
    public static class CharacterLoader
    {
        [System.Serializable] sealed class Face
        {
            public float[] eyeL, eyeR, browL, browR, jaw_hinge, jaw_tip, tie;
            public float eyeL_r, eyeR_r, browL_half, browR_half, height = 1.2f;
        }

        /// <summary>
        /// Moving eyes, brows and lids on a generated head, placed from the face metrics the Blender step measured
        /// (Characters/&lt;name&gt;_face.json, in the processed model's metres: x = his left, y = back, z = up).
        /// Everything sits on a "face frame" under the head bone aligned with the character, so the animator's
        /// gaze/brow/blink code works exactly as for the stand-in.
        /// </summary>
        static void BuildGeneratedFace(QuacksRig rig, Transform model, Transform head, Bounds bounds, QuackPalette pal, string resourceName)
        {
            var json = Resources.Load<TextAsset>("Characters/" + resourceName + "_face");
            if (json == null) { Debug.LogWarning("QUACK_CHARACTER no face metrics; eyes/brows stay baked"); return; }
            var f = JsonUtility.FromJson<Face>(json.text);
            float k = bounds.size.y / Mathf.Max(0.01f, f.height);
            // Blender (x left, y back, z up; front = -y) -> world at load (model facing +Z, his left = -X)
            Vector3 W(float[] b) => new Vector3(bounds.center.x - b[0] * k, bounds.min.y + b[2] * k, model.position.z - b[1] * k);

            var frame = new GameObject("face frame (code-controlled)").transform;
            frame.SetParent(head, false);
            frame.position = W(new[] { 0f, 0f, (f.eyeL[2] + f.eyeR[2]) / 2 });
            frame.rotation = model.rotation;
            var ls = head.lossyScale; frame.localScale = new Vector3(1 / ls.x, 1 / ls.y, 1 / ls.z);   // frame units = metres at load

            foreach (var left in new[] { true, false })
            {
                // "left" = the Unity +X side (the animator's convention), which is Blender's -x side (eyeR/browR data)
                var c = W(left ? f.eyeR : f.eyeL);
                float r = Mathf.Min(left ? f.eyeR_r : f.eyeL_r, 0.06f) * k * 1.08f;   // a touch larger: fully covers the painted iris   // sclera radius (capped: the 95th-percentile radius can pick up lens glints)
                // eyeball: centred behind the baked eye so only its front cap shows, covering the painted eye
                var eye = new GameObject(left ? "eye L" : "eye R").transform;
                eye.SetParent(frame, false);
                eye.position = c + model.forward * (-r * 0.55f);
                eye.rotation = model.rotation;
                float d = r * 2f;
                Ball(eye, "sclera", pal.White, Vector3.zero, Vector3.one * d);
                Ball(eye, "iris", pal.Brown, new Vector3(0, 0, d * 0.32f), new Vector3(d * 0.56f, d * 0.56f, d * 0.4f));
                Ball(eye, "pupil", pal.Black, new Vector3(0, 0, d * 0.44f), new Vector3(d * 0.33f, d * 0.33f, d * 0.24f));
                Ball(eye, "catchlight", pal.White, new Vector3(d * 0.11f, d * 0.13f, d * 0.55f), Vector3.one * d * 0.14f);

                // blink lid: grows down over the eyeball from its top edge
                var pivot = new GameObject(left ? "lid L" : "lid R").transform;
                pivot.SetParent(frame, false); pivot.position = c;
                var cover = new GameObject("lid cover").transform;
                cover.SetParent(frame, false);
                cover.position = c + Vector3.up * (r * 1.02f) + model.forward * (r * 0.5f);
                cover.rotation = model.rotation;
                Ball(cover, "lid", pal.Duck, new Vector3(0, -0.5f, 0), new Vector3(1, 1, 0.6f));
                var lid = cover.gameObject.AddComponent<LidCover>();
                lid.Pivot = pivot; lid.Size = d * 1.08f; lid.Open = pivot.localPosition;
                if (left) { rig.EyeL = eye; rig.LidL = pivot; } else { rig.EyeR = eye; rig.LidR = pivot; }

                // brow: a soft brown arc where the baked brow was painted out
                if ((left ? f.browR : f.browL) != null)
                {
                    var bc = W(left ? f.browR : f.browL);
                    float half = Mathf.Max(left ? f.browR_half : f.browL_half, r * 0.75f);
                    var brow = new GameObject(left ? "brow L" : "brow R").transform;
                    brow.SetParent(frame, false);
                    brow.position = bc + model.forward * (r * 0.25f);
                    brow.rotation = model.rotation;
                    var arc = Ball(brow, "brow", pal.Brown, Vector3.zero, new Vector3(half * 2.2f, r * 0.32f, r * 0.45f));
                    arc.localRotation = Quaternion.Euler(0, 0, left ? -8f : 8f);
                    if (left) rig.BrowL = brow; else rig.BrowR = brow;
                }
            }
            rig.LidOpenL = rig.LidL.localPosition; rig.LidOpenR = rig.LidR.localPosition;
            if (rig.BrowL != null && rig.BrowR != null) { rig.BrowBaseL = rig.BrowL.localPosition; rig.BrowBaseR = rig.BrowR.localPosition; }
            else { rig.BrowL = rig.BrowR = null; }
        }

        static Transform Ball(Transform parent, string name, Material m, Vector3 pos, Vector3 scale)
        {
            var g = VizUtil.Primitive(PrimitiveType.Sphere, name, parent, m);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        static void CopyRig(QuacksRig a, QuacksRig b)
        {
            b.Body = a.Body; b.Head = a.Head; b.Jaw = a.Jaw; b.LidL = a.LidL; b.LidR = a.LidR; b.WingL = a.WingL; b.WingR = a.WingR; b.Tie = a.Tie;
            b.EyeL = a.EyeL; b.EyeR = a.EyeR; b.BrowL = a.BrowL; b.BrowR = a.BrowR; b.Glasses = a.Glasses;
            b.LidOpenL = a.LidOpenL; b.LidOpenR = a.LidOpenR; b.BrowBaseL = a.BrowBaseL; b.BrowBaseR = a.BrowBaseR;
            b.WingRestL = a.WingRestL; b.WingRestR = a.WingRestR; b.JawOpenDegrees = a.JawOpenDegrees; b.BrowTravel = a.BrowTravel; b.WingZSign = a.WingZSign;
        }

        public static QuacksRig TryLoad(string resourceName, QuackPalette pal, out string report)
        {
            report = null;
            var prefab = Resources.Load<GameObject>("Characters/" + resourceName);
            if (prefab == null) { report = $"no model at Resources/Characters/{resourceName}"; return null; }
            var go = Object.Instantiate(prefab);
            go.name = "Professor Quack (generated body)";
            // explicit URP material from the exported base-colour texture (FBX material import is unreliable)
            var tex = Resources.Load<Texture2D>("Characters/" + resourceName + "_basecolor");
            if (tex != null)
            {
                var mat = new Material(pal.Duck) { name = resourceName + " (generated)" };
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
            }
            Transform Find(string n)
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            }
            var head = Find("head");
            if (head == null) { report = "model has no 'head' bone"; Object.Destroy(go); return null; }
            var rig = go.AddComponent<QuacksRig>();
            rig.Body = Find("body") ?? go.transform;
            rig.Head = head;
            // the animator's convention (from the stand-in) is "+X = his left / WingL"; Blender's +X (his anatomical left)
            // lands on Unity's -X after the FBX axis flip, so map by Unity side: the bone on +X drives WingL
            rig.WingL = Find("wing_R") ?? new GameObject("wing_L (missing)").transform;
            rig.WingR = Find("wing_L") ?? new GameObject("wing_R (missing)").transform;
            rig.Tie = new GameObject("tie (baked into mesh)").transform;
            rig.Tie.SetParent(rig.Body, false);
            rig.WingRestL = (go.transform.rotation * new Vector3(0.18f, -1f, 0)).normalized;    // WingL is the +X wing (see above)
            rig.WingRestR = (go.transform.rotation * new Vector3(-0.18f, -1f, 0)).normalized;

            // Two kinds of generated model:
            //  - full head (baked eyes, glasses and beak): keep it; the jaw bone opens the lower beak, small
            //    feather lids grow down over the baked eyes to blink; head/body/wings bones do the acting.
            //  - stripped head (--strip-head): attach the procedural Design A head + controllable face.
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
            float h = bounds.size.y;
            bool fullHead = bounds.max.y > head.position.y + 0.15f * h;
            if (fullHead)
            {
                rig.Jaw = Find("jaw") ?? new GameObject("jaw (missing)").transform;
                rig.JawOpenDegrees = 18f; rig.BrowTravel = 0.5f; rig.WingZSign = -1f;   // a real lower beak: a smaller swing reads as talking without gaping
                var tieBone = Find("tie");
                if (tieBone != null) { rig.Tie = tieBone; }
                BuildGeneratedFace(rig, go.transform, head, bounds, pal, resourceName);
            }
            else
            {
                // The stripped body ends at the neck; the procedural head sits on the head bone.
                var headPivot = new GameObject("procedural head").transform;
                headPivot.SetParent(head, false);
                headPivot.rotation = go.transform.rotation;       // upright, facing the character's forward
                headPivot.localPosition = Vector3.zero;
                QuacksRig.BuildHead(rig, headPivot, pal);
            }

            // match the stand-in's height so the rest of the scene (gaze, stations, scale 1.7) stays the same
            const float StandInHeight = 1.06f;
            var wrapper = new GameObject("Professor Quack").transform;
            wrapper.position = go.transform.position;
            go.transform.SetParent(wrapper, true);
            go.transform.localScale *= StandInHeight / Mathf.Max(0.01f, h);
            var wrapped = wrapper.gameObject.AddComponent<QuacksRig>();
            CopyRig(rig, wrapped);
            Object.Destroy(rig);
            rig = wrapped;
            report = $"loaded {resourceName}: texture={(tex != null)} fullHead={fullHead} height={h:0.00} bones body={rig.Body.name} head={head.name} jaw={(rig.Jaw != null)} wings={(Find("wing_L") != null)}/{(Find("wing_R") != null)}";
            return rig;
        }
    }
}
