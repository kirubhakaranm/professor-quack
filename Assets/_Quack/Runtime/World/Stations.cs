using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Quack
{
    /// <summary>A floor pad the learner can point at (VR trigger / mouse click) to go to a station.</summary>
    public sealed class TeleportPad : MonoBehaviour
    {
        public string Station;
        public Action<string> Activated;
        public void Activate() => Activated?.Invoke(Station);
    }

    /// <summary>
    /// The room is organised into stations (TV, hologram table, sorting line, learning corner, whiteboard).
    /// When Professor Quack talks about something, he waddles to its station and waits there facing the learner.
    /// Desktop: the camera follows to that station (until the learner steers the camera themselves; reset each stage).
    /// VR: nothing moves the learner; the station's floor pad pulses and an arrow points to it; pointing at a pad and
    /// pulling the trigger teleports there (with a quick fade).
    /// </summary>
    public sealed class StationDirector : MonoBehaviour
    {
        public sealed class Station
        {
            public string Id, Title;
            public Vector3 Focus;                  // what the camera looks at
            public float Yaw, Pitch, Distance;     // desktop camera (orbit around Focus)
            public Vector3 QuackSpot;              // where Professor Quack stands (floor)
            public Vector3 Pad;                    // where the learner stands in VR (floor)
            public bool UseLessonView;             // table: keep the stage's own preset (2D face-on, 3D overview...)
        }

        public QuacksRig Rig;
        public QuacksAnimator Anim;
        public DesktopInput Desktop;
        public AccessSettings Access;
        public QuackPalette Palette;
        public Func<XRRig> Xr;                     // non-null once VR is running
        public string Current { get; private set; } = "table";
        /// <summary>True while something else directs the scene (guided intro, finale, automated tours).</summary>
        public bool Suspended;
        public int LessonPreset = 0;

        readonly Dictionary<string, Station> stations = new Dictionary<string, Station>();
        readonly Dictionary<string, Renderer> pads = new Dictionary<string, Renderer>();
        Coroutine walk, pulse;
        float lastMove = -99;
        Transform arrow;

        // which station each highlightable thing lives at
        static readonly Dictionary<string, string> KeyStation = new Dictionary<string, string>
        {
            { "plane", "table" }, { "gimbal", "table" }, { "arrow", "table" }, { "axes", "table" }, { "equation", "table" },
            { "bands", "table" }, { "misrouted", "table" }, { "selected_egg", "table" }, { "console", "table" }, { "mission", "table" },
            { "carton_jumbo", "conveyor" }, { "carton_standard", "conveyor" }, { "conveyor", "conveyor" },
            { "landscape", "learning" }, { "training", "learning" },
            { "whiteboard", "board" }, { "tv", "tv" },
        };

        public void Build(LabEnvironment lab, Transform plotRoot, Transform landscapeRoot)
        {
            var table = plotRoot.position;
            Add(new Station { Id = "table", Title = "Hologram table", Focus = table, UseLessonView = true,
                QuackSpot = lab.QuackSpot.position, Pad = lab.LearnerSpot.position });
            Add(new Station { Id = "conveyor", Title = "Sorting line", Focus = lab.ConveyorSpot.position + Vector3.up * 0.55f, Yaw = 32, Pitch = 18, Distance = 1.7f,
                QuackSpot = lab.ConveyorSpot.position + new Vector3(-0.85f, 0, -0.15f), Pad = lab.ConveyorSpot.position + new Vector3(-0.7f, 0, -1.5f) });
            var learn = (landscapeRoot.position + lab.TrainingDeskAnchor.position) / 2f;
            Add(new Station { Id = "learning", Title = "Learning corner", Focus = learn, Yaw = 14, Pitch = 14, Distance = 1.35f,
                QuackSpot = new Vector3(learn.x + 0.95f, 0, learn.z + 0.65f), Pad = new Vector3(learn.x - 0.1f, 0, learn.z - 1.25f) });
            Add(new Station { Id = "board", Title = "Whiteboard", Focus = lab.BoardAnchor.position, Yaw = 90, Pitch = 0, Distance = 1.9f,
                QuackSpot = lab.BoardAnchor.position + new Vector3(-0.6f, -lab.BoardAnchor.position.y, -1.05f), Pad = lab.BoardAnchor.position + new Vector3(-1.9f, -lab.BoardAnchor.position.y, 0) });
            Add(new Station { Id = "tv", Title = "Factory TV", Focus = lab.TVAnchor.position + new Vector3(0, 0, -0.45f), Yaw = -90, Pitch = 2, Distance = 2.3f,
                QuackSpot = lab.TVAnchor.position + new Vector3(0.55f, -lab.TVAnchor.position.y, -1.25f),   // beside the TV, not in front of it
                Pad = lab.TVAnchor.position + new Vector3(2.0f, -lab.TVAnchor.position.y, 0) });
            foreach (var st in stations.Values) BuildPad(st);
        }

        void Add(Station s) => stations[s.Id] = s;

        void BuildPad(Station st)
        {
            var go = VizUtil.Primitive(PrimitiveType.Cylinder, $"teleport pad: {st.Title}", transform, Palette.Handle, keepCollider: true);
            go.transform.position = st.Pad + Vector3.up * 0.01f;
            go.transform.localScale = new Vector3(0.6f, 0.01f, 0.6f);
            var pad = go.AddComponent<TeleportPad>(); pad.Station = st.Id; pad.Activated = id => Go(id, fromLearner: true);
            var label = VizUtil.Label(st.Title, transform, st.Pad + Vector3.up * 0.3f, 0.12f, new Color(0f, 0.35f, 0.33f));
            label.gameObject.SetActive(false);
            padLabels.Add(label.gameObject);
            pads[st.Id] = go.GetComponent<Renderer>();
            go.SetActive(false);   // pads only matter in VR (shown by EnableVr)
        }

        readonly List<GameObject> padLabels = new List<GameObject>();
        public void EnableVr()
        {
            foreach (var p in pads.Values) p.gameObject.SetActive(true);
            foreach (var l in padLabels) l.SetActive(true);
        }

        /// <summary>Called for every tutor line that highlighted something: go where the talk is.</summary>
        public void OnHighlight(string key)
        {
            if (Suspended || !KeyStation.TryGetValue(key, out var id) || id == Current) return;
            if (Time.time - lastMove < 6f) return;   // don't ping-pong between stations mid-explanation
            Go(id, fromLearner: false);
        }

        /// <summary>Move the lesson to a station: Quack walks, the desktop camera follows, VR gets a pad pulse + arrow.</summary>
        public void Go(string id, bool fromLearner, bool force = false)
        {
            if (!stations.TryGetValue(id, out var st)) return;
            Current = id; lastMove = Time.time;
            if (walk != null) StopCoroutine(walk);
            walk = StartCoroutine(Walk(st.QuackSpot));

            var xr = Xr?.Invoke();
            if (xr != null)
            {
                if (fromLearner) xr.TeleportTo(st.Pad, st.Focus);
                else { if (pulse != null) StopCoroutine(pulse); pulse = StartCoroutine(PulsePad(id)); StartCoroutine(PointArrow(xr, st.Pad)); }
            }
            else if (Desktop != null && (fromLearner || force || !Desktop.UserSteered))
            {
                if (st.UseLessonView) Desktop.SetView(Desktop.LastPlotPreset);
                else Desktop.LookAt(st.Focus, st.Yaw, st.Pitch, st.Distance, 100 + Array.IndexOf(new[] { "table", "conveyor", "learning", "board", "tv" }, id));
            }
        }

        /// <summary>Stage start: back to its home station; the desktop camera is allowed to follow again.</summary>
        public void StageHome(string id, int lessonPreset)
        {
            LessonPreset = lessonPreset;
            if (Desktop != null) Desktop.UserSteered = false;
            lastMove = -99;
            if (id != Current || id == "table") Go(id, fromLearner: false);
        }

        // ---------- Professor Quack walks (waddles) ----------

        IEnumerator Walk(Vector3 target)
        {
            var t = Rig.transform;
            var path = new List<Vector3>();
            var from = t.position; target.y = from.y;
            // the lab table sits in the middle: walk around behind it rather than through it (and never in front of the learner)
            bool crosses = Mathf.Sign(from.x) != Mathf.Sign(target.x) && Mathf.Min(from.z, target.z) < 0.9f;
            if (crosses) { path.Add(new Vector3(from.x, from.y, 1.0f)); path.Add(new Vector3(target.x, from.y, 1.0f)); }
            path.Add(target);

            if (Access.ReducedMotion) { t.position = target; FaceLearner(t, true); walk = null; yield break; }
            foreach (var wp in path)
            {
                while ((t.position - wp).sqrMagnitude > 0.0004f)
                {
                    var dir = wp - t.position; dir.y = 0;
                    var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    t.rotation = Quaternion.RotateTowards(t.rotation, want, 360f * Time.deltaTime);
                    t.position = Vector3.MoveTowards(t.position, wp, 0.9f * Time.deltaTime);
                    // waddle: side-to-side roll on each step
                    float roll = Mathf.Sin(Time.time * 9f) * 7f;
                    t.rotation = Quaternion.Euler(0, t.eulerAngles.y, roll);
                    Anim?.NoteActivity();
                    yield return null;
                }
            }
            for (float k = 0; k < 0.5f; k += Time.deltaTime) { FaceLearner(t, false); yield return null; }
            FaceLearner(t, true);
            walk = null;
        }

        void FaceLearner(Transform t, bool snap)
        {
            var cam = Camera.main; if (cam == null) return;
            var d = cam.transform.position - t.position; d.y = 0;
            if (d.sqrMagnitude < 0.01f) return;
            var want = Quaternion.LookRotation(d.normalized, Vector3.up);
            t.rotation = snap ? want : Quaternion.RotateTowards(t.rotation, want, 240f * Time.deltaTime);
        }

        // ---------- VR cues ----------

        IEnumerator PulsePad(string id)
        {
            if (!pads.TryGetValue(id, out var r) || !r.gameObject.activeInHierarchy) yield break;
            var mpb = new MaterialPropertyBlock();
            for (float t = 0; t < 4f; t += Time.deltaTime)
            {
                float k = Access.ReducedMotion ? 0.7f : 0.5f + 0.5f * Mathf.Sin(t * 8f);
                mpb.SetColor("_BaseColor", Color.Lerp(new Color(0f, 0.6f, 0.55f), HighlightManager.HighlightColor, k));
                r.SetPropertyBlock(mpb);
                yield return null;
            }
            r.SetPropertyBlock(null);
        }

        IEnumerator PointArrow(XRRig xr, Vector3 pad)
        {
            var cam = Camera.main; if (cam == null) yield break;
            if (arrow == null)
            {
                arrow = new GameObject("station arrow").transform;
                var cone = new GameObject("cone"); cone.transform.SetParent(arrow, false);
                cone.AddComponent<MeshFilter>().sharedMesh = VizUtil.Cone();
                cone.AddComponent<MeshRenderer>().sharedMaterial = Palette.Handle;
                cone.transform.localRotation = Quaternion.Euler(90, 0, 0);   // cone +Y -> arrow +Z
                cone.transform.localScale = new Vector3(0.04f, 0.1f, 0.04f);
            }
            arrow.gameObject.SetActive(true);
            for (float t = 0; t < 4f; t += Time.deltaTime)
            {
                arrow.position = cam.transform.position + cam.transform.forward * 0.8f - cam.transform.up * 0.18f;
                var d = pad - arrow.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f) arrow.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                yield return null;
            }
            arrow.gameObject.SetActive(false);
        }
    }
}
