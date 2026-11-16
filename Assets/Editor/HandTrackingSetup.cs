// Assets/Editor/HandTrackingSetup.cs
// Menu: Tools > ROVR > Set Up Hand Tracking
// Turns on what VirtualHands needs to draw tracked hands on the Quest: the OpenXR "Hand Tracking
// Subsystem" feature (from the XR Hands package, in Packages/manifest.json) and Meta's hand tracking aim
// feature, for Android (the headset) and the PC (Quest Link in the Editor). It also checks every build
// (BridgeBuildHook-style), so a fresh checkout can't build without them. From the command line:
//   Unity -batchmode -quit -projectPath . -executeMethod HandTrackingSetup.Run -logFile setup.log
// The Quest's own setting (Settings > Movement tracking > Hand and body tracking) must be on too.
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;

public class HandTrackingSetup : IPreprocessBuildWithReport
{
    // The features' class names: XR Hands' HandTracking and MetaHandTrackingAim.
    static readonly string[] Wanted = { "HandTracking", "MetaHandTrackingAim" };

    public int callbackOrder => 0;

    [MenuItem("Tools/ROVR/Set Up Hand Tracking")]
    public static void Run()
    {
        int turnedOn = 0;
        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            turnedOn += Enable(group);
        AssetDatabase.SaveAssets();
        Debug.Log("[HandTrackingSetup] " + (turnedOn > 0 ? "Turned on " + turnedOn + " feature(s)." : "Already set up."));
    }

    static int Enable(BuildTargetGroup group)
    {
        FeatureHelpers.RefreshFeatures(group);
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        if (settings == null) { Debug.LogWarning("[HandTrackingSetup] No OpenXR settings for " + group); return 0; }
        int n = 0;
        foreach (var name in Wanted)
        {
            bool found = false;
            foreach (var f in settings.GetFeatures())
            {
                if (f == null || f.GetType().Name != name) continue;
                found = true;
                if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); n++; Debug.Log("[HandTrackingSetup] " + group + ": turned on " + f.name); }
            }
            if (!found) Debug.LogWarning("[HandTrackingSetup] " + group + ": no " + name + " feature. Is com.unity.xr.hands in Packages/manifest.json?");
        }
        if (n > 0) EditorUtility.SetDirty(settings);
        return n;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platformGroup == BuildTargetGroup.Android) Enable(BuildTargetGroup.Android);
    }
}
