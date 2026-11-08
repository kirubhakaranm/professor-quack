using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Quack
{
    /// <summary>
    /// A walnut trophy shelf under the emporium sign: every finished lesson adds a golden egg that pops in with a
    /// sparkle. Lessons finished earlier (saved) are already there. A visible, game-like record of progress.
    /// </summary>
    public sealed class TrophyShelf : MonoBehaviour
    {
        public AccessSettings Access;
        QuackPalette pal;
        readonly Dictionary<int, Transform> eggs = new Dictionary<int, Transform>();
        Material gold;
        const int Count = 6;

        public void Build(Transform parent, QuackPalette p)
        {
            pal = p;
            transform.SetParent(parent, false);
            transform.localPosition = new Vector3(0, 2.62f, 3.76f);   // under the sign, above the hanging mission screen
            var walnut = new Material(pal.Wood); walnut.color = new Color(0.32f, 0.18f, 0.09f); if (walnut.HasProperty("_BaseColor")) walnut.SetColor("_BaseColor", walnut.color);
            gold = new Material(pal.Gold); var gc = new Color(1f, 0.78f, 0.22f); gold.color = gc;
            if (gold.HasProperty("_BaseColor")) gold.SetColor("_BaseColor", gc);
            if (gold.HasProperty("_Smoothness")) gold.SetFloat("_Smoothness", 0.85f);
            if (gold.HasProperty("_Metallic")) gold.SetFloat("_Metallic", 0.8f);
            var shelf = VizUtil.Primitive(PrimitiveType.Cube, "trophy shelf", transform, walnut);
            shelf.transform.localPosition = new Vector3(0, -0.02f, -0.06f); shelf.transform.localScale = new Vector3(2.0f, 0.04f, 0.16f);
            for (int i = 0; i < Count; i++)
            {
                var stand = VizUtil.Primitive(PrimitiveType.Cylinder, "egg cup", transform, walnut);
                stand.transform.localPosition = new Vector3(Slot(i), 0.015f, -0.07f); stand.transform.localScale = new Vector3(0.07f, 0.015f, 0.07f);
                if (StartScreen.Done(i)) Add(i, animate: false);
            }
        }

        static float Slot(int i) => -0.8f + i * 0.32f;

        /// <summary>Lesson i finished: a golden egg appears in its cup.</summary>
        public void Add(int i, bool animate = true)
        {
            if (i < 0 || i >= Count || eggs.ContainsKey(i)) return;
            var e = VizUtil.Primitive(PrimitiveType.Sphere, $"golden egg {i + 1}", transform, gold).transform;
            e.localPosition = new Vector3(Slot(i), 0.09f, -0.07f);
            eggs[i] = e;
            if (animate && (Access == null || !Access.ReducedMotion)) StartCoroutine(Pop(e));
            else e.localScale = new Vector3(0.09f, 0.12f, 0.09f);
        }

        IEnumerator Pop(Transform e)
        {
            var target = new Vector3(0.09f, 0.12f, 0.09f);
            // sparkle: a burst of tiny gold flecks
            var flecks = new List<(Transform t, Vector3 v)>();
            for (int k = 0; k < 14; k++)
            {
                var f = VizUtil.Primitive(PrimitiveType.Cube, "sparkle", transform, gold).transform;
                f.localPosition = e.localPosition; f.localScale = Vector3.one * 0.015f;
                flecks.Add((f, Random.onUnitSphere * 0.5f + Vector3.up * 0.3f));
            }
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                float k = t / 0.9f;
                float s = k < 0.6f ? Mathf.SmoothStep(0, 1.25f, k / 0.6f) : Mathf.Lerp(1.25f, 1f, (k - 0.6f) / 0.4f);   // overshoot, settle
                e.localScale = target * s;
                e.localRotation = Quaternion.Euler(0, 360 * k, 0);
                foreach (var (f, v) in flecks) if (f) { f.localPosition += v * Time.deltaTime; f.localScale = Vector3.one * 0.015f * (1 - k); }
                yield return null;
            }
            e.localScale = target; e.localRotation = Quaternion.identity;
            foreach (var (f, _) in flecks) if (f) Destroy(f.gameObject);
        }
    }
}
