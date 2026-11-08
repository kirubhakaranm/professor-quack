using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Quack.Editor
{
    /// <summary>
    /// Batchmode builds: -executeMethod Quack.Editor.BuildScripts.BuildWindows | BuildWebGL
    /// Output under Builds/ (gitignored). Prints BUILD_RESULT lines for tools/win.sh.
    /// </summary>
    public static class BuildScripts
    {
        // -quackOut <dir> builds elsewhere (e.g. while the current build is running and its exe is locked)
        public static void BuildWindows()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-quackOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/Windows";
            Build(BuildTarget.StandaloneWindows64, dir.TrimEnd('/') + "/ProfQuack.exe");
            EditorApplication.Exit(exitCode);
        }
        public static void BuildWebGL()
        {
            // gzip + decompression fallback: runs from any static host without special server headers
            // (our backend still serves .gz with Content-Encoding for speed)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.runInBackground = true;
            // WebGL 2 only: with WebGPU listed first, browsers that fall back to WebGL 2 drew no lit (URP) meshes at all
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            Build(BuildTarget.WebGL, "Builds/WebGL");
            EditorApplication.Exit(exitCode);
        }

        static void Build(BuildTarget target, string path)
        {
            // version stamp: -quackVersion 0.2.0 -quackCommit abc1234  ->  Application.version "0.2.0+abc1234"
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name) { int i = System.Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string ver = Arg("-quackVersion") ?? "0.0.0-dev", commit = Arg("-quackCommit");
            // the tutor runs on a laptop over plain http on a private Tailscale network; Unity 6 blocks non-localhost http by default
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            // start fullscreen at the monitor's native resolution (F11 / Alt+Enter / Settings switch to a window)
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            string oldVer = PlayerSettings.bundleVersion, oldName = PlayerSettings.productName;
            PlayerSettings.bundleVersion = commit == null ? ver : $"{ver}+{commit}";
            PlayerSettings.productName = "Professor Quack's Egg & AI Emporium";
            try { BuildInner(target, path); }
            finally
            {
                // restore so builds never leave ProjectSettings modified in the working tree
                PlayerSettings.bundleVersion = oldVer; PlayerSettings.productName = oldName;
                AssetDatabase.SaveAssets();
            }
        }

        static void BuildInner(BuildTarget target, string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Debug.LogError("BUILD_RESULT failed: no scenes in Build Settings (run Quack/Rebuild Main Scene)"); exitCode = 2; return; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = path, target = target, options = BuildOptions.None });
            var s = report.summary;
            Debug.Log($"BUILD_RESULT {s.result} version={PlayerSettings.bundleVersion} target={target} size={s.totalSize / (1024 * 1024)}MB errors={s.totalErrors} time={s.totalTime}");
            exitCode = s.result == BuildResult.Succeeded ? 0 : 1;
        }

        static int exitCode = 1;
    }
}
