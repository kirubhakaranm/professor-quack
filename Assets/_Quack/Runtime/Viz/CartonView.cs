using UnityEngine;

namespace Quack
{
    /// <summary>
    /// A procedural egg carton: cardboard tray with a grid of cups, an open lid with a coloured class band and a sign.
    /// JUMBO cartons are bigger with fewer, larger cups; STANDARD cartons have more, smaller cups.
    /// </summary>
    public static class CartonView
    {
        public static Transform Build(Transform parent, string name, bool jumbo, QuackPalette pal, out TMPro.TextMeshPro sign)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            int cols = jumbo ? 3 : 6, rows = 2;
            float cup = jumbo ? 0.17f : 0.11f, gap = jumbo ? 0.02f : 0.012f;
            float w = cols * (cup + gap) + gap, d = rows * (cup + gap) + gap, h = jumbo ? 0.12f : 0.09f;

            var tray = VizUtil.Primitive(PrimitiveType.Cube, "tray", root, pal.Cardboard);
            tray.transform.localPosition = new Vector3(0, h / 2, 0);
            tray.transform.localScale = new Vector3(w, h, d);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var cu = VizUtil.Primitive(PrimitiveType.Cylinder, "cup", root, pal.DuckDark);
                    cu.transform.localPosition = new Vector3(-w / 2 + gap + cup / 2 + c * (cup + gap), h + 0.002f, -d / 2 + gap + cup / 2 + r * (cup + gap));
                    cu.transform.localScale = new Vector3(cup * 0.85f, 0.004f, cup * 0.85f);
                }
            var lid = VizUtil.Primitive(PrimitiveType.Cube, "lid", root, pal.Cardboard);
            lid.transform.localPosition = new Vector3(0, h + d * 0.45f, d / 2 + 0.01f);
            lid.transform.localRotation = Quaternion.Euler(-75, 0, 0);
            lid.transform.localScale = new Vector3(w, 0.02f, d);
            var band = VizUtil.Primitive(PrimitiveType.Cube, "label band", lid.transform, jumbo ? pal.JumboEgg : pal.StandardEgg);
            band.transform.localPosition = new Vector3(0, -0.8f, 0);
            band.transform.localScale = new Vector3(1.01f, 0.9f, 0.35f);
            // the class name is PRINTED on the lid's front face, above the colour band, in the class colour (no floating sign).
            // Lid leans back 75 deg: its face normal points at the learner (-z, slightly up); "up the lid" is mostly +y.
            var normal = lid.transform.localRotation * Vector3.down * -1f;     // local +y of the lid...
            normal = new Vector3(0, normal.y, normal.z).normalized;
            if (normal.z > 0) normal = -normal;                                 // ...whichever side faces the learner
            var upLid = lid.transform.localRotation * Vector3.forward;
            sign = VizUtil.Label(jumbo ? "JUMBO" : "STANDARD", root,
                lid.transform.localPosition + normal * 0.013f + upLid * (d * 0.32f), jumbo ? 0.19f : 0.14f,
                new Color(0.05f, 0.05f, 0.06f), billboard: false);   // black print on both cartons
            sign.fontStyle = TMPro.FontStyles.Bold;
            sign.transform.localRotation = Quaternion.LookRotation(-normal, upLid);
            // a coloured stripe wraps the tray's front: yellow = JUMBO, blue = STANDARD (same cardboard otherwise)
            var stripe = VizUtil.Primitive(PrimitiveType.Cube, "class stripe", root, jumbo ? pal.JumboEgg : pal.StandardEgg);
            stripe.transform.localPosition = new Vector3(0, h * 0.5f, -d / 2 - 0.003f); stripe.transform.localScale = new Vector3(w + 0.004f, h * 0.38f, 0.006f);
            var stripeBack = VizUtil.Primitive(PrimitiveType.Cube, "class stripe", root, jumbo ? pal.JumboEgg : pal.StandardEgg);
            stripeBack.transform.localPosition = new Vector3(0, h * 0.5f, d / 2 + 0.003f); stripeBack.transform.localScale = new Vector3(w + 0.004f, h * 0.38f, 0.006f);
            return root;
        }
    }
}
