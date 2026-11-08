using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Quack.Editor
{
    /// <summary>
    /// Batchmode helpers for the VR path (PC VR through Quest Link / any OpenXR runtime).
    ///   -executeMethod Quack.Editor.XRSetup.AddPackages     (step 1; Unity recompiles afterwards)
    ///   -executeMethod Quack.Editor.XRSetup.ConfigureOpenXR (step 2; separate run, after packages compiled)
    /// XR is NOT started automatically: the desktop build stays flat unless launched with -vr.
    /// </summary>
    public static class XRSetup
    {
        static readonly string[] Packages = { "com.unity.xr.management", "com.unity.xr.openxr", "com.unity.xr.interaction.toolkit" };

        public static void AddPackages()
        {
            foreach (var id in Packages)
            {
                var req = Client.Add(id);
                while (!req.IsCompleted) System.Threading.Thread.Sleep(200);
                if (req.Status == StatusCode.Success) Debug.Log($"QUACK_XR added {req.Result.name}@{req.Result.version}");
                else Debug.LogError($"QUACK_XR failed {id}: {req.Error?.message}");
            }
        }

        public static void ConfigureOpenXR()
        {
            const BuildTargetGroup group = BuildTargetGroup.Standalone;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                System.IO.Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var gs = perTarget.SettingsForBuildTarget(group);
            gs.InitManagerOnStart = false;   // desktop stays flat; XR starts only with -vr (see XRBoot)
            bool ok = XRPackageMetadataStore.AssignLoader(gs.Manager, typeof(OpenXRLoader).FullName, group);
            Debug.Log($"QUACK_XR loader assigned={ok} loaders={string.Join(",", gs.Manager.activeLoaders.Select(l => l.name))}");

            FeatureHelpers.RefreshFeatures(group);
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) { Debug.LogError("QUACK_XR no OpenXR settings for Standalone"); return; }
            foreach (var f in oxr.GetFeatures<OpenXRFeature>())
            {
                string n = f.GetType().Name;
                if (n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || n == "MetaQuestTouchProControllerProfile")
                {
                    f.enabled = true;
                    EditorUtility.SetDirty(f);
                    Debug.Log($"QUACK_XR enabled interaction profile {n}");
                }
            }
            EditorUtility.SetDirty(oxr); EditorUtility.SetDirty(gs); EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log("QUACK_XR_CONFIG_OK");
        }
    }
}
