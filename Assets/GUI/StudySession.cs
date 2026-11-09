// Assets/GUI/StudySession.cs
// Runs a study session. The headset opens on an ID menu (StudyMenu): the participant types their ID, and
// the fixed table (StudyAssignments) gives their condition (ROVR or traditional) and their sequence of the
// three worlds. Pilot IDs (p901-p904) wait for the researcher to pick both on the control page.
//
//   ID menu -> practice area (tutorial card) -> Next -> world 1 -> Next -> world 2 -> Next -> world 3 -> done
//
// In each world the objectives (StudyTasks) come back to back, shown in a banner at the top of the view.
// The world's clock and its CSV (StudyWorldLog) start at the participant's first action there: their first
// stick push (traditional) or when they start speaking (ROVR). When the last objective is reached, the CSV is
// saved and sent to the PC, and the headset asks them to take it off for the questionnaires; the researcher
// presses Next on the control page when they're back.
//
// The operator's control page (served by the bridge, Bridge/rovr_bridge.py, at http://127.0.0.1:8765/control)
// shows all this live and has Next, Skip (the current objective), Restart (the world, as a new run), Reset
// (back to the ID menu for a new participant), and for pilot IDs the condition and sequence. Twice a second
// this sends the bridge its state and takes any new commands back.
//
// Re-entering an ID gives the same assignment; worlds this device already has a completed CSV for are
// skipped. In the Editor the session doesn't show the menu (unless Wait In Editor is on), so the scene still
// just plays; there, F5 / F6 / F7 are Next / Skip / Restart.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ROVR;

public class StudySession : MonoBehaviour
{
    [Tooltip("In the Editor, also show the ID menu and wait, as on the headset.")]
    public bool waitInEditor;
    [Tooltip("Seconds between updates to and from the bridge.")]
    public float syncSeconds = 0.5f;
    [Tooltip("Seconds between the world log's position rows.")]
    public float positionEverySeconds = 1f;

    public enum Phase { NoParticipant, AwaitingAssignment, Practice, WorldReady, WorldRunning, WorldDone, Finished }

    public Phase Now { get; private set; } = Phase.NoParticipant;
    public string Id { get; private set; } = "";
    public bool IsPilot { get; private set; }
    public StudyCondition Condition { get; private set; }
    public int Sequence { get; private set; } // 1-6; 0 = not assigned yet
    public bool InStudy => Now != Phase.NoParticipant;
    public bool WaitsForSetup => !Application.isEditor || waitInEditor;
    public string World => _worldIndex >= 0 && _worldIndex < _worlds.Length ? _worlds[_worldIndex] : Now == Phase.Practice ? "Practice" : "";
    public int WorldPosition => _worldIndex + 1;
    public StudyTask CurrentTask => _objective >= 0 && _objective < _objectives.Count ? _objectives[_objective] : null;
    public int Objective => _objective + 1;
    public int ObjectiveCount => _objectives.Count;
    public StudyWorldLog WorldLog => _log;
    public float Seconds => _log != null ? _log.Seconds : 0f;
    public StudyMenu Menu => _menu;
    public ObjectiveBanner Banner => _banner;

    private VRControlPanel _panel;
    private StudyTasks _tasks;
    private BridgeLink _bridge;
    private StudyMenu _menu;
    private ObjectiveBanner _banner;

    private string[] _worlds = new string[0];
    private int _worldIndex = -1;
    private List<StudyTask> _objectives = new List<StudyTask>();
    private int _objective = -1;
    private int _skipped;
    private StudyWorldLog _log;
    private float _nextPosition;
    private HashSet<string> _completed = new HashSet<string>();
    private string _lastNote = ""; // for the control page: the latest thing the session said

    private UnityWebRequest _sync;
    private float _nextSync;
    private int _lastCommand = -1; // -1 until the first sync, which tells the bridge to skip older commands

    [Serializable]
    private class SyncState
    {
        public int after;
        public string code, condition, phase, world, worlds, completed, instruction, note, lastSent, sequenceName;
        public int sequence, worldPos, objective, objectives, run, pending;
        public bool pilot;
        public float seconds;
    }
    [Serializable] public class Command { public int id; public string kind, code, condition; public int sequence; }
    [Serializable] private class SyncReply { public Command[] commands; public int latest; }

    // Called by the panel's Start.
    public void Begin(VRControlPanel panel)
    {
        _panel = panel;
        _tasks = GetComponent<StudyTasks>();
        if (_tasks == null) _tasks = gameObject.AddComponent<StudyTasks>();
        _tasks.UsePlaceholdersIfEmpty(panel);
        _bridge = GetComponent<BridgeLink>();
        _menu = StudyMenu.Build(panel);
        _menu.OnIdConfirmed += EnterId;
        _banner = ObjectiveBanner.Build(panel);
        StudyUploads.QueueUnsent();
        if (WaitsForSetup) ShowMenu();
    }

    private void Update()
    {
        if (_panel == null) return;
        Sync();
        StudyUploads.Tick(_bridge != null ? _bridge.Url : null);
        EditorKeys();
        if (Now != Phase.WorldRunning) return;
        if (Time.realtimeSinceStartup >= _nextPosition)
        {
            _nextPosition = Time.realtimeSinceStartup + positionEverySeconds;
            Row("position", "", "", "", -1f);
        }
        if (CurrentTask != null && Reached(CurrentTask)) ObjectiveEnded(false);
    }

    // ---------------------------------------------------------------- what the participant does

    // From the panel (ROVR: speech, commands, replies) and the sticks (traditional). An action starts the
    // world's clock if it hasn't started; before that nothing is logged.
    public void Note(string evt, string input, string system, string result, float push = -1f, bool isAction = false)
    {
        if (Now == Phase.WorldReady && isAction) StartClock();
        if (Now == Phase.WorldRunning) Row(evt, input, system, result, push);
    }

    // The traditional condition's sticks: ("move", "forward-right", 0.8), ("turn", "left", 0.5), (..., "stopped", 0).
    public void Stick(string kind, string direction, float push)
    {
        bool action = direction != "stopped";
        Note(kind, direction, "", "", push, action);
    }

    private void StartClock()
    {
        _log.StartClock();
        Now = Phase.WorldRunning;
        _nextPosition = Time.realtimeSinceStartup + positionEverySeconds;
        Say("First action: " + World + " clock started");
        Row("objective_start", "", CurrentTask != null ? CurrentTask.instruction : "", "", -1f);
    }

    private void Row(string evt, string input, string system, string result, float push)
    {
        if (_log == null) return;
        Here(out Vector3 local, out float heading);
        _log.Row(evt, CurrentTask != null ? Objective : 0, input, system, result, push, local, heading);
    }

    // The head's spot relative to the world's start (metres, world axes) and which way it faces (0 = north).
    private void Here(out Vector3 local, out float heading)
    {
        Transform head = Camera.main != null ? Camera.main.transform : _panel.playerBody;
        Transform start = _panel.StartOf(World);
        local = head.position - (start != null ? start.position : Vector3.zero);
        local.y = 0f;
        Vector3 f = head.forward;
        heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    private bool Reached(StudyTask task)
    {
        Transform head = Camera.main != null ? Camera.main.transform : _panel.playerBody;
        Vector3 to = _tasks.GoalOf(task) - head.position;
        to.y = 0f;
        return to.magnitude <= task.radius;
    }

    // ---------------------------------------------------------------- the session's steps

    private void ShowMenu()
    {
        EndWorld("incomplete: new participant", false);
        Now = Phase.NoParticipant;
        Id = "";
        IsPilot = false;
        Sequence = 0;
        _worlds = new string[0];
        _worldIndex = -1;
        _banner.Hide();
        _tasks.HidePad();
        if (_panel.tutorialCard != null) _panel.tutorialCard.Dismiss();
        _panel.PauseForBreak();
        _menu.Show();
        Say("ID menu shown");
    }

    // An ID typed in the headset's menu (or on the control page, as a fallback).
    public void EnterId(string typed)
    {
        string id = StudyAssignments.NormalizeId(typed);
        if (id == null)
        {
            _menu.ShowMessage("Please try again", "Type the digits of your participant ID, for example 0 0 7 for p007.", true);
            return;
        }
        switch (StudyAssignments.Find(id, out StudyCondition condition, out int sequence))
        {
            case StudyAssignments.Lookup.Unknown:
                Say(id + " typed, but it isn't on the study list");
                _menu.ShowMessage("ID not found", id + " isn't on the study list.\nPlease check your ID with the researcher.", true);
                break;
            case StudyAssignments.Lookup.Pilot:
                EndWorld("incomplete: new participant", false);
                Id = id;
                IsPilot = true;
                Sequence = 0;
                Now = Phase.AwaitingAssignment;
                Say("Pilot " + id + ": waiting for the researcher to pick the condition and sequence");
                _menu.ShowMessage("Thank you", "The researcher will finish setting up your session.\nPlease wait a moment.", false);
                break;
            default:
                IsPilot = false;
                StartParticipant(id, condition, sequence);
                break;
        }
    }

    // A pilot participant's condition and sequence, from the control page.
    public void Assign(StudyCondition condition, int sequence)
    {
        if (!IsPilot || !StudyAssignments.IsValidSequence(sequence)) return;
        if (Now != Phase.AwaitingAssignment && Now != Phase.Practice) return; // once a world has started, the assignment stays
        StartParticipant(Id, condition, sequence);
    }

    private void StartParticipant(string id, StudyCondition condition, int sequence)
    {
        EndWorld("incomplete: new participant", false);
        Id = id;
        Condition = condition;
        Sequence = sequence;
        _worlds = StudyAssignments.Worlds(sequence);
        _worldIndex = -1;
        _completed = StudyWorldLog.CompletedWorlds(id, condition);
        Say($"Participant {id}{(IsPilot ? " (pilot)" : "")}: {StudyAssignments.ConditionLabel(condition)}, seq{sequence} {StudyAssignments.SequenceName(sequence)}"
            + (_completed.Count > 0 ? ". Already completed here: " + string.Join(", ", _completed) : ""));

        _menu.Hide();
        _banner.Hide();
        _tasks.HidePad();
        _panel.ResetForParticipant();
        _panel.SetCondition(condition);
        _panel.GoToWorld("Practice", "study: practice");
        Now = Phase.Practice;
        _panel.ShowTutorial();
    }

    // The control page's Next: from practice to the first world, from a finished world to the next one.
    // During a world, it ends that run as incomplete first.
    public void Next()
    {
        switch (Now)
        {
            case Phase.Practice:
            case Phase.WorldDone:
                NextWorld();
                break;
            case Phase.WorldReady:
            case Phase.WorldRunning:
                EndWorld("incomplete: moved on by the researcher", true);
                NextWorld();
                break;
        }
    }

    private void NextWorld()
    {
        int next = _worldIndex + 1;
        while (next < _worlds.Length && _completed.Contains(StudyAssignments.WorldLabel(_worlds[next])))
        {
            Say(_worlds[next] + " skipped: already completed on this device");
            next++;
        }
        if (next >= _worlds.Length) { Finish(); return; }
        LoadWorld(next);
    }

    private void LoadWorld(int index)
    {
        _worldIndex = index;
        if (_panel.tutorialCard != null) _panel.tutorialCard.Dismiss();
        _panel.SetCondition(Condition); // sticks back on after the break
        _panel.GoToWorld(World, "study: world " + WorldPosition + " of " + _worlds.Length);
        _objectives = _tasks.For(World);
        _objective = 0;
        _skipped = 0;
        _log = StudyWorldLog.For(Id, Condition, Sequence, World, WorldPosition);
        Now = Phase.WorldReady;
        ShowObjective(false);
        Say($"World {WorldPosition} of {_worlds.Length}: {World}, run {_log.Run}. The clock starts at the first action");
    }

    private void ShowObjective(bool flash)
    {
        var task = CurrentTask;
        if (task == null) { _banner.Hide(); _tasks.HidePad(); return; }
        _banner.Show(Objective, ObjectiveCount, task.instruction, flash);
        _tasks.ShowPad(task);
    }

    // The current objective reached (or skipped by the researcher); the next one starts at once.
    private void ObjectiveEnded(bool skipped)
    {
        Row(skipped ? "objective_skipped" : "objective_done", "", CurrentTask.instruction, skipped ? "skipped" : "done", -1f);
        Say($"{World} objective {Objective} of {ObjectiveCount} {(skipped ? "skipped" : "done")} at {_log.Seconds:F1} s");
        if (skipped) _skipped++;
        _objective++;
        if (CurrentTask == null) { CompleteWorld(); return; }
        Row("objective_start", "", CurrentTask.instruction, "", -1f);
        ShowObjective(true);
    }

    private void CompleteWorld()
    {
        string label = StudyAssignments.WorldLabel(World);
        EndWorld(_skipped > 0 ? $"completed ({_skipped} skipped)" : "completed", true);
        _completed.Add(label);
        _panel.PauseForBreak();
        bool more = false;
        for (int i = _worldIndex + 1; i < _worlds.Length; i++) more |= !_completed.Contains(StudyAssignments.WorldLabel(_worlds[i]));
        if (!more) { Finish(); return; }
        Now = Phase.WorldDone;
        if (_panel.tutorialCard != null)
            _panel.tutorialCard.ShowMessage("World complete", "Well done! Please take off the headset to answer a few questions.\nThe researcher will start the next world.", false);
    }

    // Closes the world's CSV (it is sent to the PC). keepIfEmpty: write the file even if the participant
    // never acted, so the run is on record.
    private void EndWorld(string result, bool keepIfEmpty)
    {
        if (_log == null) return;
        Here(out Vector3 local, out float heading);
        bool wrote = _log.ClockStarted || keepIfEmpty;
        _log.End(result, CurrentTask != null ? Objective : 0, local, heading, keepIfEmpty);
        Say($"{World}: {result}" + (wrote ? $" (saved {_log.FileName}, {_log.Rows} rows)" : " (nothing to save: no action)"));
        _log = null;
        _banner.Hide();
        _tasks.HidePad();
        if (Now == Phase.WorldReady || Now == Phase.WorldRunning) Now = Phase.WorldDone;
    }

    private void Finish()
    {
        Now = Phase.Finished;
        _banner.Hide();
        _tasks.HidePad();
        _panel.PauseForBreak();
        if (_panel.tutorialCard != null)
            _panel.tutorialCard.ShowMessage("All done", "Thank you for taking part!\nPlease take off the headset.", false);
        Say("All worlds done for " + Id);
    }

    public void Skip()
    {
        if (Now == Phase.WorldRunning && CurrentTask != null) ObjectiveEnded(true);
    }

    // The current world again, from its start, as a new run (its own CSV, ...-run2). A run that had started
    // is saved as incomplete first.
    public void Restart()
    {
        if (_worldIndex < 0 || (Now != Phase.WorldReady && Now != Phase.WorldRunning && Now != Phase.WorldDone)) return;
        EndWorld("incomplete: restarted by the researcher", false);
        _completed.Remove(StudyAssignments.WorldLabel(World));
        LoadWorld(_worldIndex);
    }

    // Back to the ID menu for the next participant. (Not "Reset": Unity calls a MonoBehaviour's Reset in the Editor.)
    public void ResetSession()
    {
        Say("Reset by the researcher");
        EndWorld("incomplete: session reset by the researcher", false);
        _panel.ResetForParticipant();
        if (WaitsForSetup) { ShowMenu(); return; }
        Now = Phase.NoParticipant;
        Id = "";
        IsPilot = false;
        _banner.Hide();
        _tasks.HidePad();
    }

    private void Say(string message)
    {
        _lastNote = message;
        _panel.Log("STUDY", message);
    }

    // ---------------------------------------------------------------- the control page

    public void Run(Command c)
    {
        switch ((c.kind ?? "").ToLowerInvariant())
        {
            case "id": EnterId(c.code); break;
            case "assign":
                Assign(string.Equals(c.condition, "trad", StringComparison.OrdinalIgnoreCase) ? StudyCondition.Manual : StudyCondition.ROVR, c.sequence);
                break;
            case "next": Next(); break;
            case "skip": Skip(); break;
            case "restart": Restart(); break;
            case "reset": ResetSession(); break;
        }
    }

    public static string PhaseName(Phase phase)
    {
        switch (phase)
        {
            case Phase.AwaitingAssignment: return "waiting for researcher";
            case Phase.Practice: return "practice";
            case Phase.WorldReady: return "waiting for first action";
            case Phase.WorldRunning: return "running";
            case Phase.WorldDone: return "world done";
            case Phase.Finished: return "finished";
            default: return "no participant";
        }
    }

    private void Sync()
    {
        if (_sync != null)
        {
            if (!_sync.isDone) return;
            if (_sync.result == UnityWebRequest.Result.Success) Apply(_sync.downloadHandler.text);
            _sync.Dispose();
            _sync = null;
        }
        if (_bridge == null || _bridge.Url == null || Time.realtimeSinceStartup < _nextSync) return;
        _nextSync = Time.realtimeSinceStartup + syncSeconds;

        bool assigned = Sequence > 0;
        var state = new SyncState
        {
            after = _lastCommand, code = Id, pilot = IsPilot, phase = PhaseName(Now),
            condition = assigned ? StudyAssignments.ConditionLabel(Condition) : "",
            sequence = Sequence, sequenceName = assigned ? StudyAssignments.SequenceName(Sequence) : "",
            worlds = string.Join(",", Array.ConvertAll(_worlds, StudyAssignments.WorldLabel)),
            completed = string.Join(",", _completed), world = StudyAssignments.WorldLabel(World), worldPos = WorldPosition,
            instruction = CurrentTask != null ? CurrentTask.instruction : "", objective = CurrentTask != null ? Objective : 0,
            objectives = ObjectiveCount, run = _log != null ? _log.Run : 0, seconds = Seconds, note = _lastNote,
            pending = StudyUploads.Pending, lastSent = StudyUploads.LastSent ?? "",
        };
        _sync = Post(_bridge.Url + "/session/sync", JsonUtility.ToJson(state));
        _sync.SendWebRequest();
    }

    private void Apply(string json)
    {
        SyncReply reply;
        try { reply = JsonUtility.FromJson<SyncReply>(json); }
        catch (ArgumentException) { return; }
        if (reply == null) return;
        if (_lastCommand < 0) { _lastCommand = reply.latest; return; } // first contact: older commands aren't for this run
        if (reply.commands == null) return;
        foreach (var c in reply.commands)
        {
            if (c.id <= _lastCommand) continue;
            _lastCommand = c.id;
            Run(c);
        }
    }

    private static UnityWebRequest Post(string url, string json)
    {
        var request = new UnityWebRequest(url, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 3,
        };
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private void EditorKeys()
    {
#if UNITY_EDITOR
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f5Key.wasPressedThisFrame) Next();
        if (keyboard.f6Key.wasPressedThisFrame) Skip();
        if (keyboard.f7Key.wasPressedThisFrame) Restart();
#endif
    }

    private void OnApplicationQuit()
    {
        EndWorld("incomplete: app closed", false);
    }

    private void OnDestroy()
    {
        _sync?.Dispose();
        _tasks?.HidePad();
    }
}
