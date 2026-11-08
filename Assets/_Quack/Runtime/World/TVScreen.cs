using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Quack
{
    /// <summary>
    /// The factory TV on the left wall. Plays the intro video if StreamingAssets/intro.mp4 exists (e.g. a
    /// Higgsfield clip); otherwise shows captioned story cards. Captions are always on screen (no audio needed).
    /// </summary>
    public sealed class TVScreen : MonoBehaviour
    {
        readonly UiKit ui = new UiKit();
        TMPro.TMP_Text title, body, footer;
        Image bg;
        RawImage videoImage;
        VideoPlayer player;
        public bool HasVideo { get; private set; }
        public Transform Root { get; private set; }

        static readonly (string title, string body, Color bg)[] Story =
        {
            ("FACTORY FLOOR CAM", "Professor Quack's Egg & AI Emporium.\nThe big shipment leaves tomorrow morning.", new Color(0.12f, 0.2f, 0.3f)),
            ("THE SORTING LINE", "Every egg rolls down the belt.\nThe sorter decides: JUMBO carton or STANDARD carton.", new Color(0.15f, 0.25f, 0.2f)),
            ("THE INSPECTORS", "Expert duck inspectors measured hundreds of eggs\nand wrote down where each one belongs.", new Color(0.22f, 0.18f, 0.3f)),
            ("!! MALFUNCTION !!", "The sorting machine broke.\nSmall eggs are landing in big cartons, big eggs in small ones.", new Color(0.45f, 0.12f, 0.1f)),
            ("YOUR MISSION", "Teach a new sorter to learn from the inspectors' examples,\nthen prove it works on eggs it has never seen.", new Color(0.35f, 0.25f, 0.08f)),
            ("WHAT YOU'LL LEARN", "Turn eggs into numbers  -  sort them with a line, then a plane\nMeasure mistakes  -  let the machine learn by itself\nTest it on eggs it has never seen", new Color(0.1f, 0.28f, 0.32f)),
        };

        AccessSettings access;
        VideoPlayer finale;

        /// <summary>The generated board-meeting clip (StreamingAssets/finale.mp4), prepared ahead so it starts instantly.</summary>
        public bool PlayedFinale { get; private set; }
        public bool HasFinale => finale != null && finale.isPrepared;

        public void PrepareFinale()
        {
            if (finale != null) return;
            string path = Path.Combine(Application.streamingAssetsPath, "finale.mp4");
            if (!File.Exists(path) && !path.StartsWith("http")) return;
            var holder = new GameObject("finale clip"); holder.transform.SetParent(transform, false);
            finale = holder.AddComponent<VideoPlayer>();
            finale.source = VideoSource.Url; finale.url = path; finale.isLooping = false; finale.playOnAwake = false;
            finale.renderMode = VideoRenderMode.APIOnly; finale.skipOnDrop = true;
            finale.audioOutputMode = VideoAudioOutputMode.Direct;   // the clip's own cheering; muted when voice is off
            finale.errorReceived += (vp, msg) => Debug.LogWarning("QUACK_FINALE error: " + msg);
            finale.Prepare();
        }

        static readonly (float at, string text)[] FinaleCaptions =
        {
            (0.0f, "[the Board of Ducks listens around the table]"),
            (3.5f, "[confetti bursts; the board cheers and applauds]"),
            (6.5f, "Professor Quack takes a proud bow."),
        };

        /// <summary>Play the finale clip once on the TV with captions, then return to the results report.</summary>
        public IEnumerator PlayFinale()
        {
            PrepareFinale();
            for (float w = 0; w < 6f && finale != null && !finale.isPrepared; w += Time.deltaTime) yield return null;
            Debug.Log($"QUACK_FINALE clip={(finale != null)} prepared={(finale != null && finale.isPrepared)} image={(videoImage != null)}");
            if (!HasFinale || videoImage == null) yield break;
            string t0 = title.text, b0 = body.text; var c0 = bg.color;
            title.text = body.text = footer.text = "";
            videoImage.texture = finale.texture; videoImage.enabled = true;
            finale.SetDirectAudioMute(0, access != null && !access.VoiceOn);
            finale.time = 0; finale.Play(); PlayedFinale = true;
            float len = (float)(finale.length > 0 ? finale.length : 9.0);
            int next = 0;
            for (float t = 0; t < len + 0.2f; t += Time.deltaTime)
            {
                if (next < FinaleCaptions.Length && t >= FinaleCaptions[next].at) Subtitle(FinaleCaptions[next++].text);
                yield return null;
            }
            finale.Pause(); Subtitle(null);
            videoImage.enabled = false;
            if (player != null && player.isPrepared) videoImage.texture = player.texture;
            title.text = t0; body.text = b0; bg.color = c0;
        }

        public void Build(AccessSettings a, Transform anchor, Camera cam)
        {
            access = a; ui.Scale = a.TextScale; ui.Tmp = true;
            var root = WorldPanel.Create("TV screen", anchor, cam, 1.55f, 1280, 720, interactive: false);
            Root = root;
            root.localPosition = new Vector3(0, 0, -0.03f);   // recessed just inside the bezel frame
            var screen = new GameObject("screen", typeof(RectTransform), typeof(Image));
            screen.transform.SetParent(root, false);
            var srt = (RectTransform)screen.transform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero;
            bg = screen.GetComponent<Image>(); bg.color = Story[0].bg;

            var col = ui.Panel(root, "story", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 1200, new Color(0, 0, 0, 0));
            title = ui.T(col, "", 80, UiKit.Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            body = ui.T(col, "", 52, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            footer = ui.T(col, "", 30, UiKit.MutedText, FontStyle.Italic, TextAnchor.MiddleCenter);

            // optional real video (generated intro clip), shown full-screen with captions overlaid
            string path = Path.Combine(Application.streamingAssetsPath, "intro.mp4");
            if (File.Exists(path) || path.StartsWith("http"))
            {
                var vgo = new GameObject("video", typeof(RectTransform), typeof(RawImage));
                vgo.transform.SetParent(root, false); vgo.transform.SetSiblingIndex(1);
                var vrt = (RectTransform)vgo.transform; vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = vrt.offsetMax = Vector2.zero;
                videoImage = vgo.GetComponent<RawImage>();
                player = gameObject.AddComponent<VideoPlayer>();
                player.source = VideoSource.Url; player.url = path; player.isLooping = true; player.audioOutputMode = VideoAudioOutputMode.None;
                player.renderMode = VideoRenderMode.APIOnly;
                player.prepareCompleted += p => { videoImage.texture = p.texture; HasVideo = true; videoImage.enabled = false; };
                player.skipOnDrop = true; player.playOnAwake = false;
                player.Prepare();
            }
            a.Changed += () => ui.ApplyScale(a.TextScale);
            ShowCard(0);
        }

        // the generated intro clip (10 s): short captions timed to the action, then the "what you'll learn" card
        static readonly (float at, string text)[] VideoCaptions =
        {
            (0.0f, "The night before the big shipment, the sorting machine starts to sputter..."),
            (3.0f, "Big eggs are landing in small trays, small eggs in big ones!"),
            (6.5f, "Professor Quack has an idea, and he needs your help."),
        };
        TMPro.TMP_Text subtitle;

        void Subtitle(string t)
        {
            if (subtitle == null)
            {
                var band = new GameObject("subtitle band", typeof(RectTransform), typeof(Image));
                band.transform.SetParent(Root, false);
                var brt = (RectTransform)band.transform; brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
                brt.sizeDelta = new Vector2(0, 120); brt.anchoredPosition = Vector2.zero;
                band.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
                subtitle = ui.T(band.transform, "", 40, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
                subtitle.font = VizUtil.TmpBody;
                var srt2 = (RectTransform)subtitle.transform; srt2.anchorMin = Vector2.zero; srt2.anchorMax = Vector2.one; srt2.offsetMin = new Vector2(30, 6); srt2.offsetMax = new Vector2(-30, -6);
            }
            subtitle.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(t));
            subtitle.text = t ?? "";
        }

        public void ShowCard(int i)
        {
            i = Mathf.Clamp(i, 0, Story.Length - 1);
            var (t, b, c) = Story[i];
            title.text = t; body.text = b; bg.color = c;
            footer.text = HasVideo ? "" : $"({i + 1}/{Story.Length})";
        }

        public int CardCount => Story.Length;

        /// <summary>Play the story: video (if present) or cards, each held for seconds.</summary>
        public IEnumerator PlayStory(float secondsPerCard, System.Func<bool> skip)
        {
            if (player != null)
            {
                for (float w = 0; w < 2f && !player.isPrepared; w += Time.deltaTime) yield return null;   // give the decoder a moment
            }
            if (player != null && player.isPrepared)
            {
                // video intro: play once with timed captions (no story text over the picture), then the outcomes card
                title.text = body.text = footer.text = "";
                videoImage.enabled = true;
                player.isLooping = false; player.time = 0; player.Play();
                float len = (float)(player.length > 0 ? player.length : 10.0);
                int next = 0;
                for (float t = 0; t < len + 0.3f; t += Time.deltaTime)
                {
                    if (skip()) { player.Stop(); Subtitle(null); yield break; }
                    if (next < VideoCaptions.Length && t >= VideoCaptions[next].at) Subtitle(VideoCaptions[next++].text);
                    yield return null;
                }
                player.Pause(); Subtitle(null);
                videoImage.enabled = false;
                ShowCard(Story.Length - 1);   // "WHAT YOU'LL LEARN"
                for (float t = 0; t < secondsPerCard + 1.5f; t += Time.deltaTime) { if (skip()) yield break; yield return null; }
                yield break;
            }
            for (int i = 0; i < Story.Length; i++)
            {
                ShowCard(i);
                for (float t = 0; t < secondsPerCard; t += Time.deltaTime) { if (skip()) yield break; yield return null; }
            }
        }

        /// <summary>Show a custom card (e.g. the board-meeting report built from the learner's real results).</summary>
        public void ShowReport(string t, string b, Color c)
        {
            if (player != null && player.isPlaying) player.Pause();
            if (videoImage) videoImage.enabled = false;
            title.text = t; body.text = b; footer.text = ""; bg.color = c;
        }

        public void Idle()
        {
            if (videoImage) videoImage.enabled = false;
            title.text = "FACTORY FLOOR CAM"; body.text = "Live: the sorting line is waiting for a working rule."; footer.text = "";
            bg.color = new Color(0.1f, 0.14f, 0.2f);
        }
    }
}
