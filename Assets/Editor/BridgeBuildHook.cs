// Assets/Editor/BridgeBuildHook.cs
// After every Android (Meta Quest) build: links the headset to this PC over the USB cable
// (adb reverse of the bridge port) and opens the ROVR bridge (Bridge/rovr_bridge.py) in its own
// terminal window, unless it is already running. So Build And Run leaves everything ready.
//
// Menu: Tools > ROVR > Start Bridge does the same by hand, e.g. when you only relaunch the app.
// Nothing here can fail a build: problems are only reported as warnings in the Console.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

public class BridgeBuildHook : IPostprocessBuildWithReport
{
    const int BridgePort = 8765;

    public int callbackOrder => 1000; // after other post-build steps

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        if (report.summary.result == BuildResult.Failed || report.summary.result == BuildResult.Cancelled) return;
        StartBridge();
    }

    [MenuItem("Tools/ROVR/Start Bridge")]
    public static void StartBridge()
    {
        try
        {
            LinkHeadset();
            OpenBridgeWindow();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ROVR Bridge] Couldn't start the bridge: " + e.Message + ". Run Bridge/run_bridge.bat instead.");
        }
    }

    // adb reverse: the headset's port 8765 leads to this PC's port 8765, through the cable.
    static void LinkHeadset()
    {
        string adb = AdbPath();
        if (adb == null)
        {
            Debug.LogWarning("[ROVR Bridge] adb not found, so the headset isn't linked over the cable. Is Android support installed in Unity Hub?");
            return;
        }

        var (exitCode, output) = Run(adb, $"reverse tcp:{BridgePort} tcp:{BridgePort}");
        if (exitCode == 0)
            Debug.Log($"[ROVR Bridge] Headset linked over the cable: its port {BridgePort} leads to this PC.");
        else
            Debug.LogWarning("[ROVR Bridge] Couldn't link the headset over the cable (" + output.Trim() + "). " +
                             "Plug it in, accept \"Allow USB debugging\" in the headset, then use Tools > ROVR > Start Bridge.");
    }

    static void OpenBridgeWindow()
    {
        if (PortInUse(BridgePort))
        {
            Debug.Log($"[ROVR Bridge] Already running on port {BridgePort}.");
            return;
        }

        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        string script = Path.Combine(projectRoot, "Bridge", "rovr_bridge.py");
        if (!File.Exists(script))
        {
            Debug.LogWarning("[ROVR Bridge] " + script + " not found.");
            return;
        }

        // A new console window, kept open (/k) so a startup error stays readable.
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k title ROVR bridge && python \"{script}\" --port {BridgePort}",
            WorkingDirectory = projectRoot,
            UseShellExecute = true,
        });
        Debug.Log("[ROVR Bridge] Opened the bridge in a new terminal window.");
    }

    static bool PortInUse(int port)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var connect = client.BeginConnect("127.0.0.1", port, null, null);
                bool ok = connect.AsyncWaitHandle.WaitOne(300) && client.Connected;
                if (ok) client.EndConnect(connect);
                return ok;
            }
        }
        catch (SocketException)
        {
            return false;
        }
    }

    // Unity's own Android SDK first, then a custom SDK set in Preferences > External Tools.
    static string AdbPath()
    {
        string[] candidates =
        {
            Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe"),
            Path.Combine(EditorPrefs.GetString("AndroidSdkRoot", ""), "platform-tools", "adb.exe"),
        };
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return null;
    }

    static (int, string) Run(string exe, string args)
    {
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10000)) { process.Kill(); return (-1, "adb timed out"); }
            return (process.ExitCode, output);
        }
    }
}
