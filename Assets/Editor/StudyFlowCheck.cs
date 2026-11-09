// Assets/Editor/StudyFlowCheck.cs
// Menu: Tools > ROVR > Check Study Flow. Or headless:
//   Unity -batchmode -projectPath . -executeMethod StudyFlowCheck.RunBatch -logFile -
// Plays main.unity and walks a study session through the real StudySession, StudyMenu, ObjectiveBanner and
// StudyWorldLog, checking the CSVs it writes:
//   - p002 (traditional, seq1: plains, maze, house) types the ID on the menu; practice; Next.
//   - Plains: nothing is logged until the first stick push; then 8-direction rows; the four objectives are
//     reached one after the other; world_end completed; the "World complete" card.
//   - Maze: a run restarted by the researcher (run 1 incomplete, run 2 completed at the exit).
//   - House: every objective skipped (completed, 4 skipped). Then finished.
//   - p002 again: all three worlds already completed on this device, so Next goes straight to "All done".
//   - p050 isn't on the list; p901 (pilot) waits until the researcher assigns ROVR seq3 (maze first).
//   - ROVR in the Maze: speech starts the clock; the heard sentence is logged.
// Test files go to Temp/StudyFlowCheck/ (deleted first), never to StudyData/, and nothing is sent to a bridge
// (add -studyFlowUpload to send them, for testing the bridge with a copy of it). Results: the Console, and
// Temp/StudyFlowCheck/results.txt. RunBatch quits with exit code 0 if everything passed, 1 if not.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class StudyFlowCheck
{
    const string RunningKey = "ROVR.StudyFlowCheck.Running", BatchKey = "ROVR.StudyFlowCheck.Batch";

    static IEnumerator<float> _steps;
    static float _resumeAt;
    static readonly List<string> _results = new List<string>();
    static int _failures;

    static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "StudyFlowCheck");

    static StudyFlowCheck()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/ROVR/Check Study Flow")]
    public static void Run() => Start(false);

    public static void RunBatch() => Start(true);

    static void Start(bool batch)
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BatchKey, batch);
        SessionState.SetString(StudyWorldLog.TestFolderKey, Folder);
        SessionState.SetBool(StudyWorldLog.TestNoUploadKey, !Environment.GetCommandLineArgs().Contains("-studyFlowUpload"));
        EditorSceneManager.OpenScene("Assets/main.unity");
        EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            _results.Clear();
            _failures = 0;
            _steps = Steps();
            _resumeAt = 0f;
            EditorApplication.update += Tick;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseBool(RunningKey);
            SessionState.EraseString(StudyWorldLog.TestFolderKey);
            SessionState.EraseBool(StudyWorldLog.TestNoUploadKey);
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(File.ReadAllText(Path.Combine(Folder, "results.txt")).Contains("ALL PASSED") ? 0 : 1);
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || _steps == null) return;
        if (Time.realtimeSinceStartup < _resumeAt) return;
        bool more;
        try { more = _steps.MoveNext(); }
        catch (Exception e) { Check(false, "exception: " + e); more = false; }
        if (more) { _resumeAt = Time.realtimeSinceStartup + _steps.Current; return; }

        EditorApplication.update -= Tick;
        _steps = null;
        string summary = _failures == 0 ? "ALL PASSED (" + _results.Count + " checks)" : _failures + " FAILED of " + _results.Count;
        _results.Add(summary);
        File.WriteAllLines(Path.Combine(Folder, "results.txt"), _results);
        Debug.Log("[StudyFlowCheck] " + summary + "\n" + string.Join("\n", _results));
        EditorApplication.isPlaying = false;
    }

    static void Check(bool ok, string what)
    {
        _results.Add((ok ? "ok    " : "FAIL  ") + what);
        if (!ok) { _failures++; Debug.LogError("[StudyFlowCheck] FAIL " + what); }
    }

    // ------------------------------------------------------------------ the session, step by step

    static IEnumerator<float> Steps()
    {
        yield return 2f; // the scene starts: panel, pipeline, session, menu
        var panel = UnityEngine.Object.FindAnyObjectByType<VRControlPanel>();
        var session = panel != null ? panel.GetComponent<StudySession>() : null;
        Check(session != null && session.Menu != null && session.Banner != null, "the session, its ID menu and its banner exist");
        if (session == null) yield break;
        var manual = panel.playerBody.GetComponent<ROVR.ManualLocomotion>();

        // The ID menu: p002 = traditional, seq1 (plains, maze, house).
        session.Menu.Show();
        foreach (var key in new[] { "0", "0", "2", "OK" }) session.Menu.Press(key);
        session.Menu.Confirm();
        yield return 0.5f;
        Check(!session.Menu.Shown, "the menu closes after the ID is confirmed");
        Check(session.Id == "p002" && session.Condition == StudyCondition.Manual && session.Sequence == 1,
            $"p002 gets traditional seq1 (got {session.Id} {session.Condition} seq{session.Sequence})");
        Check(session.Now == StudySession.Phase.Practice && panel.condition == StudyCondition.Manual, "practice, in the traditional condition");

        // ---- Plains
        session.Next();
        yield return 0.5f;
        Check(session.Now == StudySession.Phase.WorldReady && session.World == "Plain" && session.WorldPosition == 1, "Next: plains, world 1, waiting for the first action");
        Check(session.Banner.Shown && session.Banner.Text.StartsWith("Objective 1 of"), "the banner shows objective 1: " + session.Banner.Text);
        yield return 1f;
        Check(session.Now == StudySession.Phase.WorldReady && !File.Exists(Path.Combine(Folder, "p002-trad-plains.csv")), "no clock and no file before the first action");

        manual.SimulatedMove = new Vector2(0f, 1f);
        yield return 0.4f;
        Check(session.Now == StudySession.Phase.WorldRunning, "the first stick push starts the clock");
        manual.SimulatedMove = new Vector2(0.7f, 0.7f);
        yield return 0.4f;
        manual.SimulatedMove = Vector2.zero;
        yield return 1.3f; // at least one position row
        manual.SimulatedMove = null;

        var tasks = panel.GetComponent<StudyTasks>();
        int count = session.ObjectiveCount;
        for (int i = 0; i < count; i++)
        {
            panel.PlaceAt(tasks.GoalOf(session.CurrentTask), 0f, "Plain", "flow check");
            yield return 0.3f;
        }
        Check(session.Now == StudySession.Phase.WorldDone && panel.CardShown, "all plains objectives reached: world done, the card asks for the headset off");
        var plains = ReadCsv("p002-trad-plains.csv");
        CheckWorldCsv(plains, "p002,trad,seq1,plains-maze-house,plains,1,1", count, "completed");
        Check(plains.Count > 1 && plains[1][9] == "objective_start" && plains[1][7] == "0.000", "the first row is objective 1 starting at t = 0");
        Check(plains.Count > 2 && plains[2][9] == "move" && plains[2][11] == "forward" && plains[2][14] != "", "then the first push: move forward, with how far");
        Check(plains.Any(r => r[9] == "move" && r[11] == "forward-right"), "the push to forward-right is logged");
        Check(plains.Any(r => r[9] == "move" && r[11] == "stopped"), "letting go is logged as stopped");
        Check(plains.Any(r => r[9] == "position"), "position rows");

        // ---- Maze: restarted once, then completed at the exit
        session.Next();
        yield return 0.5f;
        Check(session.Now == StudySession.Phase.WorldReady && session.World == "Maze" && session.WorldPosition == 2, "Next: maze, world 2");
        Check(manual.enabled, "the sticks work again after the break");
        manual.SimulatedMove = new Vector2(0f, 1f);
        yield return 0.4f;
        manual.SimulatedMove = null;
        session.Restart();
        yield return 0.5f;
        Check(session.Now == StudySession.Phase.WorldReady && session.WorldLog != null && session.WorldLog.Run == 2, "Restart: the maze again, as run 2, waiting for the first action");
        CheckWorldCsv(ReadCsv("p002-trad-maze.csv"), "p002,trad,seq1,plains-maze-house,maze,2,1", -1, "incomplete: restarted by the researcher");
        manual.SimulatedMove = new Vector2(0f, 1f);
        yield return 0.3f;
        manual.SimulatedMove = null;
        panel.PlaceAt(tasks.GoalOf(session.CurrentTask), 0f, "Maze", "flow check");
        yield return 0.5f;
        Check(session.Now == StudySession.Phase.WorldDone, "the maze exit completes the maze");
        CheckWorldCsv(ReadCsv("p002-trad-maze-run2.csv"), "p002,trad,seq1,plains-maze-house,maze,2,2", 1, "completed");

        // ---- House: every objective skipped
        session.Next();
        yield return 0.5f;
        Check(session.World == "House" && session.WorldPosition == 3, "Next: house, world 3");
        session.Skip();
        Check(session.Now == StudySession.Phase.WorldReady, "Skip does nothing before the first action");
        manual.SimulatedTurn = new Vector2(-1f, 0f);
        yield return 0.4f;
        manual.SimulatedTurn = null;
        yield return 0.2f;
        int houseObjectives = session.ObjectiveCount;
        for (int i = 0; i < houseObjectives; i++) session.Skip();
        yield return 0.3f;
        Check(session.Now == StudySession.Phase.Finished, "the last world done: finished, the All done card");
        var house = ReadCsv("p002-trad-house.csv");
        CheckWorldCsv(house, "p002,trad,seq1,plains-maze-house,house,3,1", -1, $"completed ({houseObjectives} skipped)");
        Check(house.Count > 2 && house[2][9] == "turn" && house[2][11] == "left", "a right-stick turn is the first action: turn left");
        Check(house.Count(r => r[9] == "objective_skipped") == houseObjectives, "each skipped objective is logged");

        // ---- p002 again: nothing left to do
        session.EnterId("p002");
        yield return 0.3f;
        session.Next();
        yield return 0.3f;
        Check(session.Now == StudySession.Phase.Finished, "p002 again: all worlds already completed here, so Next goes to All done");

        // ---- an ID that isn't on the list
        session.ResetSession();
        session.Menu.Show();
        session.EnterId("p050");
        yield return 0.2f;
        Check(session.Now == StudySession.Phase.NoParticipant && session.Menu.Shown, "p050 isn't on the list: the menu says so, nothing starts");

        // ---- a pilot: waits for the researcher, then ROVR seq3 (maze first)
        session.EnterId("p901");
        yield return 0.2f;
        Check(session.Now == StudySession.Phase.AwaitingAssignment && session.IsPilot, "p901 (pilot) waits for the researcher");
        session.Run(new StudySession.Command { kind = "assign", condition = "rovr", sequence = 3 });
        yield return 0.5f;
        Check(session.Now == StudySession.Phase.Practice && session.Condition == StudyCondition.ROVR && session.Sequence == 3, "assign rovr 3: practice in ROVR");
        session.Next();
        yield return 0.5f;
        Check(session.World == "Maze" && session.WorldPosition == 1, "seq3 starts with the maze");

        // ROVR: speaking starts the clock; the sentence is logged (the LLM's reply needs Ollama, so it isn't checked).
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(VRControlPanel).GetMethod("NoteSpeechStarted", flags).Invoke(panel, null);
        Check(session.Now == StudySession.Phase.WorldRunning, "ROVR: starting to speak starts the clock");
        typeof(VRControlPanel).GetMethod("OnSentenceHeard", flags).Invoke(panel, new object[] { "Move forward five meters.", 1.2f, 0.3f, "in a test" });
        yield return 0.5f;
        session.Next(); // ends the run as incomplete
        yield return 0.5f;
        var rovr = ReadCsv("p901-rovr-maze.csv");
        CheckWorldCsv(rovr, "p901,rovr,seq3,maze-plains-house,maze,1,1", -1, "incomplete: moved on by the researcher");
        Check(rovr.Count > 2 && rovr[2][9] == "speech_start" && rovr[2][7] == "0.000", "the first ROVR row is speech_start at t = 0");
        Check(rovr.Any(r => r[9] == "heard" && r[11].StartsWith("move forward five meters")), "the heard sentence is logged in input");
        Check(session.World == "Plain" && session.WorldPosition == 2, "Next mid-world moves on to world 2 (plains)");
        session.ResetSession();
    }

    // ------------------------------------------------------------------ the CSVs

    static List<string[]> ReadCsv(string name)
    {
        string path = Path.Combine(Folder, name);
        if (!File.Exists(path)) { Check(false, name + " exists"); return new List<string[]>(); }
        return File.ReadAllLines(path).Where(l => l.Length > 0).Select(SplitCsv).ToList();
    }

    // prefix: the first seven columns. objectivesDone: how many objective_done rows (-1 = don't check).
    static void CheckWorldCsv(List<string[]> rows, string prefix, int objectivesDone, string result)
    {
        if (rows.Count == 0) return;
        string name = prefix.Replace(",", " ");
        Check(string.Join(",", rows[0]) == StudyWorldLog.Header, name + ": header");
        var data = rows.Skip(1).ToList();
        Check(data.All(r => r.Length == 18 && string.Join(",", r.Take(7)) == prefix), name + ": every row has 18 columns and starts " + prefix);
        bool ordered = true;
        for (int i = 1; i < data.Count; i++) ordered &= float.Parse(data[i][7], System.Globalization.CultureInfo.InvariantCulture) >= float.Parse(data[i - 1][7], System.Globalization.CultureInfo.InvariantCulture);
        Check(ordered, name + ": t_s never goes backwards");
        if (objectivesDone >= 0) Check(data.Count(r => r[9] == "objective_done") == objectivesDone, $"{name}: {objectivesDone} objective_done rows");
        var last = data.LastOrDefault();
        Check(last != null && last[9] == "world_end" && last[13] == result, $"{name}: ends with world_end '{result}' (got '{(last != null ? last[13] : "")}')");
    }

    static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields.ToArray();
    }
}
