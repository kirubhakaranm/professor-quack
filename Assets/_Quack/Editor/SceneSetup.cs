using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Quack.Editor
{
    /// <summary>
    /// Generates materials, the palette asset and the Main scene, and registers the scene in Build Settings.
    /// Run from the menu (Quack/Rebuild Main Scene) or batchmode:
    ///   -executeMethod Quack.Editor.SceneSetup.RebuildAll
    /// Safe to re-run: it overwrites only generated assets under Assets/_Quack/{Materials,Scenes}.
    /// </summary>
    public static class SceneSetup
    {
        const string MatDir = "Assets/_Quack/Materials";
        const string ScenePath = "Assets/_Quack/Scenes/Main.unity";
        const string PalettePath = MatDir + "/QuackPalette.asset";

        [MenuItem("Quack/Rebuild Main Scene")]
        public static void RebuildAll()
        {
            TmpSetup.Ensure();
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var pal = BuildPalette();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Sun", typeof(Light));
            var l = light.GetComponent<Light>();
            l.type = LightType.Directional; l.intensity = 0.9f; l.shadows = LightShadows.Soft; l.color = new Color(1f, 0.93f, 0.82f);
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.93f, 0.91f, 0.86f);
            cam.fieldOfView = 55; cam.nearClipPlane = 0.05f;
            camGo.transform.position = new Vector3(0, 1.6f, -4);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.56f, 0.5f);   // warm fill

            var game = new GameObject("QuackGame").AddComponent<QuackGame>();
            game.Palette = pal; game.Cam = cam;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("QUACK_SETUP_OK scene=" + ScenePath);
        }

        static QuackPalette BuildPalette()
        {
            var pal = AssetDatabase.LoadAssetAtPath<QuackPalette>(PalettePath);
            if (pal == null) { pal = ScriptableObject.CreateInstance<QuackPalette>(); AssetDatabase.CreateAsset(pal, PalettePath); }

            pal.StandardEgg = Lit("StandardEgg", QuackPalette.StandardColor);
            pal.JumboEgg = Lit("JumboEgg", QuackPalette.JumboColor);
            pal.Duck = Lit("Duck", new Color(1.0f, 0.78f, 0.12f));        // Design A golden yellow
            pal.DuckBeak = Lit("DuckBeak", new Color(0.98f, 0.5f, 0.08f));
            pal.DuckDark = Lit("DuckDark", new Color(0.12f, 0.13f, 0.18f));
            pal.Tie = Lit("Tie", new Color(0.55f, 0.08f, 0.14f));
            pal.Navy = Lit("Navy", new Color(0.12f, 0.15f, 0.32f));
            pal.Gold = Lit("Gold", new Color(0.93f, 0.72f, 0.2f));
            pal.Cream = Lit("Cream", new Color(0.98f, 0.88f, 0.66f));
            pal.White = Lit("White", new Color(0.97f, 0.97f, 0.95f));
            pal.Black = Lit("Black", new Color(0.04f, 0.04f, 0.05f));
            pal.Brown = Lit("Brown", new Color(0.42f, 0.25f, 0.12f));
            pal.Factory = Lit("Factory", new Color(0.78f, 0.78f, 0.8f));
            pal.FactoryAccent = Lit("FactoryAccent", new Color(0.3f, 0.6f, 0.55f));
            pal.LabWall = Lit("LabWall", new Color(0.93f, 0.85f, 0.72f));     // warm cream
            pal.Wood = Lit("Wood", new Color(0.55f, 0.36f, 0.22f));
            pal.Rug = Lit("Rug", new Color(0.55f, 0.18f, 0.16f));
            pal.Cardboard = Lit("Cardboard", new Color(0.78f, 0.62f, 0.42f));
            pal.LabTable = Lit("LabTable", new Color(0.32f, 0.36f, 0.42f));
            pal.Line = LoadOrCreate("Line", "Sprites/Default");   // honours LineRenderer vertex colours
            pal.Arrow = Unlit("Arrow", new Color(0.62f, 0.07f, 0.36f), transparent: false);
            pal.Handle = Unlit("Handle", new Color(0.0f, 0.70f, 0.64f), transparent: false);
            pal.Marker = Unlit("Marker", new Color(0.05f, 0.05f, 0.05f), transparent: false);
            pal.Plane = Unlit("Plane", new Color(0.45f, 0.35f, 0.75f, 0.32f), transparent: true);
            pal.Highlight = Unlit("Highlight", new Color(1f, 0.83f, 0f, 0.45f), transparent: true);
            pal.Band = Unlit("Band", new Color(0.45f, 0.35f, 0.75f, 0.12f), transparent: true);
            pal.Surface = LoadOrCreate("Surface", "Sprites/Default");   // vertex-coloured, double-sided
            EditorUtility.SetDirty(pal);
            return pal;
        }

        static Material Lit(string name, Color c)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            // feathers and fabric are soft; glasses, eyes and buttons a bit glossier
            float smooth = name == "Duck" || name == "Navy" || name == "Cream" ? 0.12f
                         : name == "Black" || name == "White" || name == "Gold" ? 0.6f : 0.35f;
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        static Material Unlit(string name, Color c, bool transparent)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", c);
            if (transparent)
            {
                m.SetFloat("_Surface", 1);      // transparent
                m.SetFloat("_Blend", 0);        // alpha
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.SetFloat("_Cull", (float)CullMode.Off);   // double-sided plane
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }
    }
}
