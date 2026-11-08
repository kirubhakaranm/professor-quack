using Quack.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The selected egg's card: measurements in raw and standardised units, the inspector label, the rule's
    /// probability and verdict. Docked just OUTSIDE the plot cube (never over the egg cloud) on the side nearest
    /// the egg, at the egg's height, with a thin leader line to the egg. Faces the viewer.
    /// </summary>
    public sealed class EggTag : MonoBehaviour
    {
        GameState state;
        FeaturePlot plot;
        readonly UiKit ui = new UiKit();
        RectTransform root;
        TMPro.TMP_Text title, body, verdict;
        Transform cam;
        bool showLoss;
        LineRenderer leader;
        // the card shows for a while after selecting, then fades; hovering the egg or the card brings it back
        CanvasGroup group;
        float shownAt = -99, alpha = 1;
        public const float VisibleSeconds = 7f, FadeSeconds = 1f;
        public bool ShowLoss { get => showLoss; set { showLoss = value; Sync(); } }

        public void Build(GameState s, FeaturePlot p, AccessSettings a, Camera camera)
        {
            state = s; plot = p; cam = camera.transform; ui.Scale = a.TextScale; ui.Tmp = true;
            var holder = new GameObject("egg tag holder").transform;
            holder.SetParent(transform, false);
            root = WorldPanel.Create("Egg tag", holder, camera, 0.46f, 520, 300, interactive: false);
            var panel = WorldPanel.Body(ui, root, "tag", new Color(0.08f, 0.09f, 0.12f, 0.9f));
            title = ui.T(panel, "", 26, UiKit.Accent, FontStyle.Bold);
            body = ui.T(panel, "", 21);
            verdict = ui.T(panel, "", 22, UiKit.TextColor, FontStyle.Bold);
            // always readable: draw the tag on top of the eggs instead of being hidden behind them
            var onTop = new Material(Canvas.GetDefaultCanvasMaterial());
            onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.material = onTop;
            root.GetComponent<Canvas>().sortingOrder = 10;
            leader = VizUtil.Line("egg tag leader", transform, null, QuackPalette.SelectColor, 0.004f, Vector3.zero, Vector3.zero);
            leader.useWorldSpace = true;
            leader.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            group = root.gameObject.AddComponent<CanvasGroup>();
            s.SelectionChanged += _ => { shownAt = Time.unscaledTime; Sync(); };
            s.ParamsChanged += _ => Sync();
            a.Changed += () => ui.ApplyScale(a.TextScale);
            Sync();
        }

        /// <summary>Mouse over the selected egg or over the card itself (desktop).</summary>
        bool Hovering(Quack.Core.Egg e)
        {
            var m = UnityEngine.InputSystem.Mouse.current; var cam = Camera.main;
            if (m == null || cam == null) return false;
            var mp = m.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(root, mp, cam) && alpha > 0.3f) return true;
            if (Physics.Raycast(cam.ScreenPointToRay(mp), out var hit, 50f))
            {
                var v = hit.collider.GetComponent<EggView>();
                if (v != null && v.Egg == e) return true;
            }
            return false;
        }

        void Sync()
        {
            var e = state.Selected;
            root.gameObject.SetActive(e != null);
            if (leader) leader.gameObject.SetActive(e != null);
            if (e == null) return;
            var x = state.Vector(e);
            title.text = $"Egg #{e.Id}   inspectors: {EggDataset.ClassNames[e.Label]} {(e.Label == 1 ? "(golden, banded)" : "(blue)")}";
            string w = state.WidthLocked ? "(not measured yet)" : $"{e.Raw[2]:0.0} mm   z {x[2]:+0.00;-0.00}";
            body.text = $"mass    {e.Raw[0]:0.0} g    z {x[0]:+0.00;-0.00}\nlength  {e.Raw[1]:0.0} mm   z {x[1]:+0.00;-0.00}\nwidth   {w}";
            double score = LogisticModel.Score(state.Params, x);
            int pred = state.Prediction(e);
            verdict.text = $"p(JUMBO) = {LogisticModel.Sigmoid(score):0.00}  ->  {EggDataset.ClassNames[pred]}  " + (pred == e.Label ? "(correct)" : "(MISROUTED, marked X)")
                + (showLoss ? $"\nloss for this egg = -log p(true label) = {LogisticModel.Bce(score, e.Label):0.00}" : "");
        }

        void LateUpdate()
        {
            var e = state?.Selected;
            if (e == null) return;
            if (Hovering(e)) shownAt = Time.unscaledTime;
            float age = Time.unscaledTime - shownAt;
            float target = age < VisibleSeconds ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / (target > alpha ? 0.15f : FadeSeconds));
            if (group) group.alpha = alpha;
            if (leader) { var c = QuackPalette.SelectColor; c.a = alpha; leader.startColor = leader.endColor = c; leader.enabled = alpha > 0.02f; }
            var eggWorld = plot.transform.TransformPoint(plot.LocalOf(e));
            var centre = plot.transform.position;
            var toCam = cam.position - centre; toCam.y = 0; toCam.Normalize();
            var right = Vector3.Cross(Vector3.up, toCam).normalized * -1f;     // viewer's right
            float half = FeaturePlot.HalfExtent * plot.transform.lossyScale.x;  // plot cube half-size in metres
            // dock on the side of the cube the egg is nearer to; if the plot has slid aside (landscape stages), use the open left side
            float side = Vector3.Dot(eggWorld - centre, right) >= 0 ? 1f : -1f;
            if (centre.x < -0.1f) side = -1f;
            float tagHalf = 0.23f;
            var dock = centre + right * side * (half * 1.25f + tagHalf + 0.04f);
            dock.y = Mathf.Clamp(eggWorld.y, centre.y - half * 0.6f, centre.y + half * 0.8f);
            var holder = root.parent;
            holder.position = dock;
            holder.rotation = Quaternion.LookRotation(holder.position - cam.position, Vector3.up);
            // leader line from the egg to the tag's inner edge
            leader.SetPosition(0, eggWorld);
            leader.SetPosition(1, dock - right * side * tagHalf);
        }
    }
}
