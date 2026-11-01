// Assets/GUI/StudySession.cs
// Runs a study session from the PC. The operator's control page (served by the bridge, Bridge/rovr_bridge.py,
// at http://127.0.0.1:8765/control) sets the participant code and condition, moves the participant between
// worlds, and starts, skips and restarts tasks. Twice a second this sends the bridge what's happening (shown
// live on the page) and takes any new commands back. Each finished task goes to the bridge too, which saves
// it in a CSV next to its log.
//
// Tasks (StudyTasks): in each world they come in order; the operator reads the instruction, presses Start
// (F5 on the page), and the task ends by itself when the participant reaches its goal. No time limits.
//
// On the headset the session waits, with a card saying so, until the control page sets it up. In the Editor
// it doesn't wait (unless Wait In Editor is on), so the scene still just plays; F5 / F6 / F7 there start,
// skip and restart the current task.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ROVR;

public class StudySession : MonoBehaviour
{
    [Tooltip("In the Editor, also wait for the control page before starting. On the headset it always waits.")]
    public bool waitInEditor;
    [Tooltip("Seconds between updates to and from the bridge.")]
    public float syncSeconds = 0.5f;

    public string Code { get; private set; } = "";
    public bool IsSetUp => Code.Length > 0;
    public bool WaitsForSetup => !Application.isEditor || waitInEditor;
    public string World { get; private set; } = "";
    public StudyTask CurrentTask => _index >= 0 && _index < _worldTasks.Count ? _worldTasks[_index] : null;
    public string Status => _state == State.None ? (IsSetUp ? "no task" : "not set up") : _state.ToString().ToLowerInvariant();
    public float Elapsed => _state == State.Running ? Time.realtimeSinceStartup - _startedAt : 0f;

    private enum State { None, Ready, Running }

    private VRControlPanel _panel;
    private StudyTasks _tasks;
    private BridgeLink _bridge;
    private List<StudyTask> _worldTasks = new List<StudyTask>();
    private int _index = -1;
    private State _state;
    private float _startedAt;
    private DateTime _startedClock;
    private Vector3 _readyAt;
    private float _readyYaw;

    private UnityWebRequest _sync;
    private float _nextSync;
    private int _lastCommand = -1; // -1 until the first sync, which tells the bridge to skip older commands

    [Serializable] private class SyncState { public int after; public string code, condition, world, instruction, status; public int task, tasks; public float seconds; }
    [Serializable] public class Command { public int id; public string kind, code, condition, world; }
    [Serializable] private class SyncReply { public Command[] commands; public int latest; }
    [Serializable] private class TaskResult { public string code, condition, world, instruction, outcome, started, ended; public int task; public float seconds; }

    // Called by the panel's Start.
    public void Begin(VRControlPanel panel)
    {
        _panel = panel;
        _tasks = GetComponent<StudyTasks>();
        if (_tasks == null) _tasks = gameObject.AddComponent<StudyTasks>();
        _tasks.UsePlaceholdersIfEmpty(panel);
        _bridge = GetComponent<BridgeLink>();
        if (WaitsForSetup) ShowWaiting();
    }

    private void ShowWaiting()
    {
        if (_panel.tutorialCard != null)
            _panel.tutorialCard.ShowMessage("Waiting to start", "The researcher will set up your session on the PC.\nPlease wait a moment.", false);
    }

    private void Update()
    {
        if (_panel == null) return;
        Sync();
        EditorKeys();
        if (_state == State.Running && Reached()) Finish("done");
    }

    // ---------------------------------------------------------------- the operator's commands

    public void Run(Command c)
    {
        switch ((c.kind ?? "").ToLowerInvariant())
        {
            case "setup":
                SetUp(c.code, string.Equals(c.condition, "manual", StringComparison.OrdinalIgnoreCase) ? StudyCondition.Manual : StudyCondition.ROVR);
                break;
            case "world": GoTo(c.world); break;
            case "start": StartTask(); break;
            case "skip": SkipTask(); break;
            case "restart": RestartTask(); break;
        }
    }

    // A new participant: their code and condition. They start in the practice area, with the tutorial.
    public void SetUp(string code, StudyCondition condition)
    {
        if (_state == State.Running) Finish("left");
        Code = (code ?? "").Trim().ToUpperInvariant();
        _panel.Log("SESSION", "Participant " + Code + ", condition " + condition);
        _panel.ResetForParticipant();
        _panel.SetCondition(condition);
        GoTo("Practice");
        _panel.ShowTutorial();
    }

    // Moves the participant to a world's start. Once set up, that world's first task is made ready.
    public void GoTo(string world)
    {
        if (_state == State.Running) Finish("left");
        if (!_panel.GoToWorld(world, IsSetUp ? "control page" : "button")) return;
        World = world;
        _tasks.HidePad();
        _worldTasks = IsSetUp ? _tasks.For(world) : new List<StudyTask>();
        _index = -1;
        _state = State.None;
        if (IsSetUp) NextTask();
    }

    public void StartTask()
    {
        if (_state != State.Ready) return;
        _state = State.Running;
        _startedAt = Time.realtimeSinceStartup;
        _startedClock = DateTime.Now;
        _panel.Log("TASK", World + " task " + (_index + 1) + " started: " + CurrentTask.instruction);
    }

    public void SkipTask()
    {
        if (_state == State.None) return;
        Finish("skipped");
    }

    // The current task again from where it began, with the timer stopped until Start.
    public void RestartTask()
    {
        if (_state == State.None) return;
        if (_state == State.Running) Report("restarted", Elapsed);
        _panel.PlaceAt(_readyAt, _readyYaw, World, "task restarted");
        _state = State.Ready;
        _panel.Log("TASK", World + " task " + (_index + 1) + " restarted");
    }

    private void NextTask()
    {
        _index++;
        if (CurrentTask == null)
        {
            _state = State.None;
            _tasks.HidePad();
            _panel.Log("TASK", World + ": no more tasks");
            return;
        }
        _state = State.Ready;
        _readyAt = _panel.playerBody.position;
        Vector3 f = Camera.main != null ? Camera.main.transform.forward : _panel.playerBody.forward;
        _readyYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        _tasks.ShowPad(CurrentTask);
        _panel.Log("TASK", World + " task " + (_index + 1) + " of " + _worldTasks.Count + " ready: " + CurrentTask.instruction);
    }

    // done, skipped, or left (the operator moved them on mid-task).
    private void Finish(string outcome)
    {
        float seconds = Elapsed;
        _panel.Log("TASK", World + " task " + (_index + 1) + " " + outcome + (_state == State.Running ? " after " + seconds.ToString("F1") + " s" : ""));
        Report(outcome, seconds);
        NextTask();
    }

    private bool Reached()
    {
        var task = CurrentTask;
        Transform head = Camera.main != null ? Camera.main.transform : _panel.playerBody;
        Vector3 to = _tasks.GoalOf(task) - head.position;
        to.y = 0f;
        return to.magnitude <= task.radius;
    }

    // ---------------------------------------------------------------- the bridge

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

        var state = new SyncState
        {
            after = _lastCommand, code = Code, condition = _panel.condition.ToString(), world = World,
            instruction = CurrentTask != null ? CurrentTask.instruction : "", status = Status,
            task = _index + 1, tasks = _worldTasks.Count, seconds = Elapsed,
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

    private void Report(string outcome, float seconds)
    {
        if (_bridge == null || _bridge.Url == null || CurrentTask == null) return;
        var result = new TaskResult
        {
            code = Code, condition = _panel.condition.ToString(), world = World, task = _index + 1,
            instruction = CurrentTask.instruction, outcome = outcome, seconds = seconds,
            started = _state == State.Running ? _startedClock.ToString("yyyy-MM-dd HH:mm:ss.fff") : "",
            ended = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        };
        var request = Post(_bridge.Url + "/session/task", JsonUtility.ToJson(result));
        request.SendWebRequest().completed += _ => request.Dispose();
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
        if (keyboard.f5Key.wasPressedThisFrame) StartTask();
        if (keyboard.f6Key.wasPressedThisFrame) SkipTask();
        if (keyboard.f7Key.wasPressedThisFrame) RestartTask();
#endif
    }

    private void OnDestroy()
    {
        _sync?.Dispose();
        _tasks?.HidePad();
    }
}
