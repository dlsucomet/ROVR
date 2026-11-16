// Assets/Editor/QuestBuild.cs
// Menu: Tools > ROVR > Build For Quest
// Builds main.unity for the headset (Android) into main.apk at the project root, with the project's
// build settings. From the command line, with the Editor closed:
//   Unity -batchmode -quit -projectPath . -executeMethod QuestBuild.Build -logFile build.log
// then install it: adb install -r main.apk
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class QuestBuild
{
    const string Output = "main.apk";

    [MenuItem("Tools/ROVR/Build For Quest")]
    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/main.unity" },
            locationPathName = Output,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        Debug.Log($"[QuestBuild] {summary.result}: {Output}, {summary.totalSize / (1024f * 1024f):F0} MB, " +
                  $"{summary.totalTime.TotalMinutes:F1} min, {summary.totalErrors} error(s)");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
