using UnityEngine;
using UnityEngine.UI;

namespace Quack
{
    /// <summary>
    /// The lab whiteboard behind the table: recaps connecting what the learner did to notation.
    /// Text is rendered through controlled UI (never generated textures). Keeps the last recap visible.
    /// </summary>
    public sealed class WhiteboardView : MonoBehaviour
    {
        readonly UiKit ui = new UiKit();
        TMPro.TMP_Text title, body;

        public RectTransform Root { get; private set; }

        public void Build(SpeechQueue speech, AccessSettings a, Transform anchor, Camera cam)
        {
            ui.Scale = a.TextScale; ui.Tmp = true;
            var root = WorldPanel.Create("Whiteboard", anchor, cam, 1.76f, 1200, 800, interactive: false);   // writing surface of the 1.8 x 1.2 m board
            root.localPosition = new Vector3(0, 0, -0.008f);   // just in front of the white backing
            Root = root;
            var frame = WorldPanel.Body(ui, root, "board", new Color(0, 0, 0, 0));   // the 3D board provides the white surface
            title = ui.T(frame, "WHITEBOARD", 66, new Color(0.08f, 0.2f, 0.62f), FontStyle.Normal);   // blue marker headings
            title.font = VizUtil.TmpHand;
            body = ui.T(frame, "Recaps of each discovery will appear here.", 48, new Color(0.1f, 0.1f, 0.12f));   // black marker
            body.font = VizUtil.TmpHand;   // handwritten (Patrick Hand), like real marker writing
            speech.LineStarted += line =>
            {
                if (line.Board == null) return;
                title.text = string.IsNullOrEmpty(line.Board.title) ? "WHITEBOARD" : line.Board.title;
                body.text = line.Board.lines == null ? "" : "- " + string.Join("\n- ", line.Board.lines);
            };
            a.Changed += () => ui.ApplyScale(a.TextScale);
        }

        public void Show(string heading, params string[] lines)
        {
            title.text = heading;
            body.text = "- " + string.Join("\n- ", lines);
            // long recaps get smaller marker writing so everything stays on the board
            int len = 0; foreach (var l in lines) len += System.Text.RegularExpressions.Regex.Replace(l ?? "", "<.*?>", "").Length + 40;
            body.fontSize = (len > 700 ? 32 : len > 480 ? 40 : 48) * ui.Scale;
        }
    }
}
