using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Quack
{
    /// <summary>
    /// Starts OpenXR only when asked (-vr on the command line). If no runtime/headset is available the
    /// game logs why and stays in desktop mode, so one Windows build serves both.
    /// </summary>
    public static class XRBoot
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-vr") >= 0;
        public static bool Active { get; private set; }

        public static IEnumerator Start(Action<bool> done)
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                Debug.LogWarning("QUACK_XR no XR settings in build; staying in desktop mode");
                done(false); yield break;
            }
            yield return settings.Manager.InitializeLoader();
            if (settings.Manager.activeLoader == null)
            {
                Debug.LogWarning("QUACK_XR OpenXR failed to start (is Quest Link running and set as the OpenXR runtime?); staying in desktop mode");
                done(false); yield break;
            }
            settings.Manager.StartSubsystems();
            // floor-relative tracking so the camera height is the player's real eye height
            var inputs = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs) s.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Floor);
            Active = true;
            Debug.Log($"QUACK_XR started with {settings.Manager.activeLoader.name}");
            done(true);
        }

        public static void Stop()
        {
            if (!Active) return;
            var m = XRGeneralSettings.Instance.Manager;
            m.StopSubsystems(); m.DeinitializeLoader();
            Active = false;
        }
    }
}
