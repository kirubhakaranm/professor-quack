using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Quack.Editor
{
    /// <summary>
    /// TextMeshPro setup for crisp text at any distance: imports TMP Essential Resources (once) and turns each of our
    /// fonts into a dynamic SDF font asset saved next to it in Resources/Fonts (so builds include font + shader).
    /// Called from SceneSetup.RebuildAll; the first run imports, the next creates the font assets.
    /// </summary>
    public static class TmpSetup
    {
        static readonly string[] Fonts = { "AtkinsonHyperlegible-Regular", "AtkinsonHyperlegible-Bold", "CinzelDecorative-Bold", "VT323", "PatrickHand", "Fonts & Materials/STIXTwoText" };

        public static void Ensure()
        {
            if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                var pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
                if (File.Exists(pkg)) { AssetDatabase.ImportPackage(pkg, false); AssetDatabase.Refresh(); Debug.Log("QUACK_TMP imported essential resources"); }
                else Debug.LogWarning("QUACK_TMP essential resources package not found: " + pkg);
            }
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null && Shader.Find("TextMeshPro/Distance Field") == null)
            {
                Debug.LogWarning("QUACK_TMP shaders not loaded yet; run RebuildAll again to create font assets");
                return;
            }
            foreach (var name in Fonts)
            {
                // names with a folder live elsewhere under Resources (STIX sits in "Fonts & Materials" so TMP's <font="..."> tag finds it)
                string dir = name.Contains("/") ? "Assets/_Quack/Resources/" : "Assets/_Quack/Resources/Fonts/";
                var path = $"{dir}{name} SDF.asset";
                if (File.Exists(path)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>($"{dir}{name}.ttf");
                if (font == null) { Debug.LogWarning("QUACK_TMP missing font " + name); continue; }
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) { Debug.LogWarning("QUACK_TMP could not create " + name); continue; }
                fa.name = System.IO.Path.GetFileName(name) + " SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
                foreach (var tex in fa.atlasTextures) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                AssetDatabase.SaveAssets();
                Debug.Log("QUACK_TMP created " + path);
            }
        }
    }
}
