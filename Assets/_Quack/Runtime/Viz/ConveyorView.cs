using System.Collections;
using Quack.Core;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// The factory sorter: the selected egg rides the belt to the diverter, which points at the
    /// carton the CURRENT rule predicts. Bin signs show how all training eggs are routed right now.
    /// </summary>
    public sealed class ConveyorView : MonoBehaviour
    {
        GameState state;
        AccessSettings access;
        QuackPalette pal;
        Transform gate, rider;
        TMPro.TextMeshPro jumboSign, standardSign, status;
        Coroutine travel;
        int lastPrediction = -1;
        Egg lastEgg;

        static readonly Vector3 BeltStart = new Vector3(0, 0.85f, 1.6f), GatePos = new Vector3(0, 0.85f, 0);
        // trays sit clear of the belt and of the diverter's swing (arm 0.5 long, +-40 degrees -> tip within x +-0.33, z -0.38)
        static readonly Vector3 JumboBin = new Vector3(-0.98f, 0.35f, -0.82f), StandardBin = new Vector3(0.98f, 0.35f, -0.82f);

        public void Build(GameState s, QuackPalette p, AccessSettings a)
        {
            state = s; pal = p; access = a;
            var belt = VizUtil.Primitive(PrimitiveType.Cube, "belt", transform, pal.Factory);
            belt.transform.localPosition = new Vector3(0, 0.75f, 0.8f);
            belt.transform.localScale = new Vector3(0.45f, 0.12f, 1.8f);
            foreach (var (x, z) in new[] { (-0.2f, -0.05f), (0.2f, -0.05f), (-0.2f, 1.65f), (0.2f, 1.65f), (-0.2f, 0.8f), (0.2f, 0.8f) })   // legs: the belt stands on the floor
            {
                var leg = VizUtil.Primitive(PrimitiveType.Cube, "belt leg", transform, pal.DuckDark);
                leg.transform.localPosition = new Vector3(x, 0.345f, z); leg.transform.localScale = new Vector3(0.05f, 0.69f, 0.05f);
            }
            var rail = VizUtil.Primitive(PrimitiveType.Cube, "belt side rail", transform, pal.FactoryAccent);
            rail.transform.localPosition = new Vector3(-0.235f, 0.82f, 0.8f); rail.transform.localScale = new Vector3(0.02f, 0.06f, 1.8f);
            var rail2 = VizUtil.Primitive(PrimitiveType.Cube, "belt side rail", transform, pal.FactoryAccent);
            rail2.transform.localPosition = new Vector3(0.235f, 0.82f, 0.8f); rail2.transform.localScale = new Vector3(0.02f, 0.06f, 1.8f);

            var g = VizUtil.Primitive(PrimitiveType.Cube, "diverter", transform, pal.FactoryAccent);
            g.transform.localScale = new Vector3(0.06f, 0.18f, 0.5f);
            var pivot = new GameObject("diverter pivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = GatePos;
            g.transform.SetParent(pivot, false);
            g.transform.localPosition = new Vector3(0, 0, -0.25f);
            gate = pivot;

            JumboCarton = BuildCarton("JUMBO carton", JumboBin, true, out jumboSign);
            StandardCarton = BuildCarton("STANDARD carton", StandardBin, false, out standardSign);
            status = VizUtil.Label("", transform, new Vector3(0, 1.75f, 0.6f), 0.13f, Color.black);
            status.gameObject.SetActive(false);   // clean scene: the egg card says where the selected egg goes

            s.ParamsChanged += _ => Refresh(false);
            s.SelectionChanged += _ => Refresh(true);
            Refresh(true);
        }

        public Transform JumboCarton { get; private set; }
        public Transform StandardCarton { get; private set; }

        Transform BuildCarton(string title, Vector3 pos, bool jumbo, out TMPro.TextMeshPro sign)
        {
            var c = CartonView.Build(transform, title, jumbo, pal, out var top);
            c.localPosition = new Vector3(pos.x, 0.62f, pos.z);
            // lid stays at the far side (+z), so the open tray and its printed label face the learner
            // pedestal so the carton sits at belt height
            var ped = VizUtil.Primitive(PrimitiveType.Cube, "pedestal", c, pal.Factory);
            ped.transform.localPosition = new Vector3(0, -0.31f, 0); ped.transform.localScale = new Vector3(0.5f, 0.62f, 0.35f);
            sign = top;   // printed on the lid; tallies live on the mission screen
            return c;
        }

        void Refresh(bool selectionChanged)
        {
            // bin tallies across all training eggs under the current rule
            int jumboIn = 0, jumboWrong = 0, stdIn = 0, stdWrong = 0;
            foreach (var e in state.Dataset.Train)
            {
                int pred = state.Prediction(e);
                if (pred == 1) { jumboIn++; if (e.Label != 1) jumboWrong++; }
                else { stdIn++; if (e.Label != 0) stdWrong++; }
            }
            // (tray tallies are shown on the mission screen's scoreboard; the trays just carry their printed names)

            var sel = state.Selected;
            int prediction = sel == null ? -1 : state.Prediction(sel);
            gate.localRotation = Quaternion.Euler(0, prediction == 1 ? 40 : -40, 0);   // arm swings toward the chosen bin

            if (sel == null)
            {
                status.text = "Select an egg to send it down the belt";
                if (rider) rider.gameObject.SetActive(false);
                lastEgg = null; lastPrediction = -1;
                return;
            }
            double prob = state.Probability(sel);
            string verdict = prediction == sel.Label ? "correct" : "MISROUTED";
            status.text = $"Egg #{sel.Id} -> {EggDataset.ClassNames[prediction]}\np(JUMBO) = {prob:0.00}, {verdict}";

            if (selectionChanged || sel != lastEgg || prediction != lastPrediction)
            {
                EnsureRider(sel);
                if (travel != null) StopCoroutine(travel);
                travel = StartCoroutine(Ride(prediction == 1 ? JumboBin : StandardBin, selectionChanged));
            }
            lastEgg = sel; lastPrediction = prediction;
        }

        void EnsureRider(Egg e)
        {
            if (rider) Destroy(rider.gameObject);
            bool jumbo = e.Label == 1;
            var go = VizUtil.Primitive(PrimitiveType.Sphere, "rider", transform, jumbo ? pal.JumboEgg : pal.StandardEgg);
            if (jumbo) FeaturePlot.AddBand(go.transform);
            // egg-shaped and cup-sized, so it can sit in a carton cup (JUMBO cups 0.17 m, STANDARD 0.11 m)
            go.transform.localScale = jumbo ? new Vector3(0.13f, 0.17f, 0.13f) : new Vector3(0.085f, 0.11f, 0.085f);
            riderHeight = go.transform.localScale.y;
            go.layer = gameObject.layer;
            rider = go.transform;
        }

        float riderHeight = 0.17f;

        /// <summary>The front-centre cup of a carton, in conveyor-local space (where the routed egg comes to rest).</summary>
        Vector3 CupSpot(Transform carton)
        {
            Transform best = null; float bestScore = float.MaxValue;
            foreach (Transform ch in carton)
            {
                if (ch.name != "cup") continue;
                var lp = ch.localPosition;
                float score = lp.z * 10f + Mathf.Abs(lp.x);   // front row first, then nearest the middle
                if (score < bestScore) { bestScore = score; best = ch; }
            }
            return best ? transform.InverseTransformPoint(best.position) : carton.localPosition + Vector3.up * 0.12f;
        }

        IEnumerator Ride(Vector3 bin, bool fromStart)
        {
            var carton = bin.x < 0 ? JumboCarton : StandardCarton;
            var cup = CupSpot(carton);
            var seated = cup + Vector3.up * (riderHeight * 0.3f);   // sunk into the cup, not floating above the tray
            var above = cup + Vector3.up * 0.32f;
            if (access.ReducedMotion) { rider.localPosition = seated; yield break; }
            if (fromStart) rider.localPosition = BeltStart;
            yield return Move(rider.localPosition, GatePos, 0.6f);
            yield return Move(GatePos, above, 0.4f);
            // drop (accelerating), then a small settle bounce
            for (float t = 0; t < 0.22f; t += Time.deltaTime) { float k = t / 0.22f; rider.localPosition = Vector3.Lerp(above, seated, k * k); yield return null; }
            yield return Move(seated, seated + Vector3.up * 0.025f, 0.08f);
            yield return Move(seated + Vector3.up * 0.025f, seated, 0.1f);
        }

        IEnumerator Move(Vector3 a, Vector3 b, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                rider.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            rider.localPosition = b;
        }
    }
}
