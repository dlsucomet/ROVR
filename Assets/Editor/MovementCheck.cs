// Assets/Editor/MovementCheck.cs
// Menu: Tools > ROVR > Check Movement. Or headless:
//   Unity -batchmode -projectPath . -executeMethod MovementCheck.RunBatch -logFile -
// Plays main.unity and drives the real voice pipeline and body with the commands from Miguel's test session,
// without the LLM (only the quick commands, which don't need it) or a headset:
//   - gravity: standing on the floor after a teleport and after a jump;
//   - "take two steps forward" = twice the step length, stepping the whole way (slower at each footfall,
//     never a stop, starting and ending at rest);
//   - "step left" is a sidestep: sideways, the view doesn't turn;
//   - "keep moving forward" interrupted by "turn right": the move reports Interrupted, eases out briefly, and
//     the turn runs (90 degrees);
//   - the edges of the view darken while walking forward or back, not for sidesteps;
//   - "stop" and "freeze" ease to a stop (never dead); the cancel button (B) stops at once;
//   - steps into a hedge stop at it (Blocked), never through it;
//   - the minimap: the Maze's and the House's, low on the left of the view;
//   - "get me out of this room" from the living room ends in the hall; a route to the bed goes through doorways.
// And, since the pilots:
//   - side talk ("I guess I'm ready", "was that 15 degrees?") isn't sent to the LLM; requests are;
//   - an open-ended move runs at a short pause (SpeechSegmenter.OnPause); turns and counted moves wait;
//   - a bare "left" turns (not a sidestep); "10 degrees to the left" turns 10 degrees, not 90;
//   - "turn around more" after a small turn is a small turn more, not 180;
//   - Whisper's prompt: the House's object names.
// Results: the Console, and Logs/MovementCheck/results.txt. RunBatch quits with 0 if everything passed, 1 if not.
using System;
using System.Collections.Generic;
using System.IO;
using ROVR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MovementCheck
{
    const string RunningKey = "ROVR.MovementCheck.Running", BatchKey = "ROVR.MovementCheck.Batch";

    static IEnumerator<float> _steps;
    static float _resumeAt;
    static readonly List<string> _results = new List<string>();
    static int _failures;

    static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "MovementCheck");

    static MovementCheck()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/ROVR/Check Movement")]
    public static void Run() => Start(false);

    public static void RunBatch() => Start(true);

    static void Start(bool batch)
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(BatchKey, batch);
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
        Debug.Log("[MovementCheck] " + summary + "\n" + string.Join("\n", _results));
        EditorApplication.isPlaying = false;
    }

    static void Check(bool ok, string what)
    {
        _results.Add((ok ? "ok    " : "FAIL  ") + what);
        if (!ok) { _failures++; Debug.LogError("[MovementCheck] FAIL " + what); }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // A quick command that is one turn that way, by that much.
    static void Turned(string said, DirectionType dir, AmountType amount, float degrees)
    {
        var c = QuickCommands.TryParse(said);
        var s = c != null && c.steps.Length == 1 ? c.steps[0] : null;
        Check(s != null && s.action == ActionType.Turn && s.direction == dir && s.amount == amount && Mathf.Approximately(s.magnitude, degrees),
              $"\"{said}\" is a turn {dir} {amount} {degrees}" + (s == null ? " (not a quick turn)" : $" (got {s.action} {s.direction} {s.amount} {s.magnitude})"));
    }

    // Seconds of a 200 Hz tone at this amplitude (0 = silence) into the segmenter, 0.1 s at a time.
    static void Feed(SpeechSegmenter seg, float seconds, float amplitude, ref float clock)
    {
        for (int k = 0; k < Mathf.RoundToInt(seconds * 10f); k++)
        {
            var chunk = new float[1600];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = amplitude * Mathf.Sin(2f * Mathf.PI * 200f * i / 16000f);
            clock += 0.1f;
            seg.AddSamples(chunk);
        }
    }

    static float Yaw(Transform head)
    {
        Vector3 f = Flat(head.forward);
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------------ the checks

    static IEnumerator<float> Steps()
    {
        yield return 2f;
        var panel = UnityEngine.Object.FindAnyObjectByType<VRControlPanel>();
        Check(panel != null, "the panel exists");
        if (panel == null) yield break;
        var body = panel.playerBody;
        var nav = body.GetComponent<NavigationController>();
        var resolver = body.GetComponent<SemanticIntentResolver>();
        var gravity = body.GetComponent<PlayerGravity>();
        var map = UnityEngine.Object.FindAnyObjectByType<WorldMinimap>();
        var vignette = body.GetComponent<ComfortVignette>();
        var head = Camera.main.transform;
        Check(nav != null && resolver != null && gravity != null && map != null, "navigation, resolver, gravity and the minimap are there");
        if (nav == null || resolver == null || gravity == null) yield break;
        if (panel.tutorialCard != null) panel.tutorialCard.Dismiss();

        // ---- Words: expressions aren't commands; after "stop", a follow-up is its own command
        foreach (var said in new[] { "Okay.", "OK", "Alright.", "All right", "Yeah, okay.", "Got it.", "Uh, okay" })
            Check(TranscriptFilter.Clean(said) == null, $"\"{said}\" is ignored as an expression");
        Check(TranscriptFilter.Clean("Okay, turn left.") != null, "\"Okay, turn left\" still goes through");
        var follow = InterruptModule.Evaluate("Stop, turn left.");
        Check(follow.isHalt && follow.remainder == "turn left", $"\"stop, turn left\" stops, then turns left (remainder \"{follow.remainder}\")");
        foreach (var said in new[] { "Stop moving.", "Stop it.", "Wait a bit.", "Hold on a second." })
        {
            var r = InterruptModule.Evaluate(said);
            Check(r.isHalt && r.remainder.Length == 0, $"\"{said}\" only stops (remainder \"{r.remainder}\")");
        }

        // ---- Side talk isn't a command; requests and commands are
        var known = UnityEngine.Object.FindAnyObjectByType<FOVMetadataGrounding>().KnownNames();
        foreach (var said in new[] { "I guess I'm ready.", "Was that 15 degrees?", "See you later.", "Where's the bathroom? Okay.",
                                     "Talk to you soon.", "Hello there.", "I wonder if" })
            Check(!SideTalk.IsCommand(said, known), $"\"{said}\" is side talk");
        foreach (var said in new[] { "Could you walk to the door?", "Can I go a bit further?", "Where am I? Okay. Turn right 30 degrees.",
                                     "10 degrees to the left.", "Go to the bookshelf.", "That one.", "Faster.", "Go back.", "the sofa", "more",
                                     "One more step?", "Turn around, what is that?" })
            Check(SideTalk.IsCommand(said, known), $"\"{said}\" is a command");

        // ---- Quick forms: a bare side turns, a bare angle turns by it
        Turned("left", DirectionType.Left, AmountType.None, 0f);
        Turned("Okay, left.", DirectionType.Left, AmountType.None, 0f);
        Turned("to the right", DirectionType.Right, AmountType.None, 0f);
        Turned("10 degrees to the left.", DirectionType.Left, AmountType.Exact, 10f);
        Turned("right 45 degrees", DirectionType.Right, AmountType.Exact, 45f);
        Turned("a little to the left", DirectionType.Left, AmountType.Small, 0f);
        var bareForward = QuickCommands.TryParse("forward");
        Check(bareForward != null && bareForward.steps[0].action == ActionType.Move && bareForward.steps[0].direction == DirectionType.Forward, "a bare \"forward\" still moves");
        var sidestep = QuickCommands.TryParse("step left");
        Check(sidestep != null && sidestep.steps[0].action == ActionType.Move && sidestep.steps[0].direction == DirectionType.Left, "\"step left\" is still a sidestep");

        // ---- What may run at a short pause: open-ended moves only
        foreach (var said in new[] { "move forward", "move back", "keep going", "move left", "walk forward." })
            Check(QuickCommands.IsOpenEndedMove(QuickCommands.TryParse(said)), $"\"{said}\" may run at a pause");
        Check(GoBackWords.IsGoBack("go back"), "\"go back\" is the undo (GoBackWords), so the panel never runs it at a pause");
        foreach (var said in new[] { "turn left", "move forward two steps", "take 5 steps forward", "move forward 3 meters", "left", "turn right 45 degrees" })
            Check(!QuickCommands.IsOpenEndedMove(QuickCommands.TryParse(said)), $"\"{said}\" waits for the whole sentence");

        // ---- The segmenter's pause: once, at EarlyEndSec of silence, before the sentence ends
        var seg = new SpeechSegmenter(16000) { DeadAirSec = 1.5f, EarlyEndSec = 0.6f };
        float pausedAt = -1f, endedAt = -1f, clock = 0f;
        int pauses = 0;
        seg.OnPause += a => { pauses++; if (pausedAt < 0f) pausedAt = clock; };
        seg.OnSentence += a => { if (endedAt < 0f) endedAt = clock; };
        Feed(seg, 1.2f, 0f, ref clock);    // the room
        Feed(seg, 0.8f, 0.2f, ref clock);  // "move forward"
        float speechEnd = clock;
        Feed(seg, 2f, 0f, ref clock);      // silence
        Check(pauses == 1 && Mathf.Abs(pausedAt - speechEnd - 0.6f) < 0.15f, $"a pause is handed out once, {pausedAt - speechEnd:F1} s after speech ({pauses} times)");
        Check(endedAt > 0f && Mathf.Abs(endedAt - speechEnd - 1.5f) < 0.15f, $"and the sentence still ends at {endedAt - speechEnd:F1} s unless it's ended early");
        seg = new SpeechSegmenter(16000) { DeadAirSec = 1.5f, EarlyEndSec = 0.6f };
        endedAt = -1f; clock = 0f;
        seg.OnPause += a => seg.EndNow();
        seg.OnSentence += a => endedAt = clock;
        Feed(seg, 1.2f, 0f, ref clock);
        Feed(seg, 0.8f, 0.2f, ref clock);
        Feed(seg, 2f, 0f, ref clock);
        Check(endedAt < 0f && !seg.InSentence, "ended at the pause (EndNow), the silence after it isn't a sentence");

        // ---- Whisper's prompt: the House's object names, no commands
        string prompt = VRControlPanel.WorldPrompt(panel.StartOf("House"));
        Check(prompt.Contains("bookshelf") && prompt.Contains("TV") && prompt.EndsWith(".") && !prompt.ToLowerInvariant().Contains("turn"),
              "the House's Whisper prompt lists its objects: \"" + prompt + "\"");

        CommandOutcome? last = null;
        var outcomes = new List<CommandOutcome>();
        nav.OnCommandFinished += (c, o) => { last = o; outcomes.Add(o); };

        // ---- Gravity
        panel.GoToWorld("Plain", "movement check");
        yield return 1.5f;
        float floor = GameObject.Find("Plain").transform.position.y;
        Check(gravity.IsGrounded && Mathf.Abs(body.position.y - floor) < 0.1f, $"on the Plain floor after the teleport (y {body.position.y:F2}, grounded {gravity.IsGrounded})");

        // ---- Two steps forward
        float step = resolver.StepMeters;
        Vector3 from = body.position;
        float yaw0 = Yaw(head);
        resolver.SubmitUtterance("take two steps forward");
        yield return 0.1f;
        float early = nav.SpeedNow;
        float top = 0f;
        var speeds = new List<float>();
        for (int i = 0; i < 400 && nav.IsBusy; i++) { top = Mathf.Max(top, nav.SpeedNow); speeds.Add(nav.SpeedNow); yield return 0.01f; }
        // Between the two strides the speed dips (the footfall) and rises again: two peaks, a dip, never zero.
        int peaks = 0;
        float dip = float.MaxValue;
        for (int i = 2; i + 2 < speeds.Count; i++)
        {
            if (speeds[i] > speeds[i - 2] && speeds[i] >= speeds[i + 2] && speeds[i] > 0.7f * top && (peaks == 0 || dip < 0.75f * top)) { peaks++; dip = float.MaxValue; }
            else if (peaks == 1) dip = Mathf.Min(dip, speeds[i]);
        }
        Check(peaks == 2, $"two strides: the speed peaks once per step ({peaks} peaks in {speeds.Count} samples, top {top:F2} m/s)");
        float moved = Flat(body.position - from).magnitude;
        Check(Mathf.Abs(moved - 2f * step) < 0.12f, $"\"take two steps forward\" = 2 x {step:F2} m (moved {moved:F2} m)");
        Check(early < top - 0.2f, $"it eases in (speed {early:F2} m/s early, top {top:F2} m/s)");
        Check(last == CommandOutcome.Completed, "and completes (" + last + ")");

        // ---- A sidestep (after the forward walk's vignette has faded)
        yield return 0.4f;
        from = body.position;
        Vector3 forward = Flat(head.forward).normalized;
        resolver.SubmitUtterance("step left");
        float sideDark = 0f;
        for (int i = 0; i < 60 && (nav.IsBusy || i == 0); i++) { if (vignette != null) sideDark = Mathf.Max(sideDark, vignette.Amount); yield return 0.05f; }
        Vector3 d = Flat(body.position - from);
        Check(Mathf.Abs(d.magnitude - step) < 0.1f && Mathf.Abs(Vector3.Dot(d.normalized, forward)) < 0.1f && Vector3.Cross(forward, d).y < 0f,
            $"\"step left\" moves {step:F2} m to the left, not forward (moved {d.magnitude:F2} m, along forward {Vector3.Dot(d, forward):F2})");
        Check(Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw(head))) < 1f, "and the view doesn't turn");
        Check(sideDark < 0.01f, $"a sidestep doesn't darken the edges of the view (vignette {sideDark:F2})");

        // ---- Interrupt a continuous move with a turn
        resolver.SubmitUtterance("keep moving forward");
        yield return 1.2f;
        Check(nav.IsBusy && nav.SpeedNow > 1.5f, $"\"keep moving forward\" is moving ({nav.SpeedNow:F2} m/s)");
        Check(vignette != null && vignette.Amount > 0.3f, $"walking forward darkens the edges of the view (vignette {(vignette != null ? vignette.Amount : 0f):F2})");
        outcomes.Clear();
        Vector3 atTurn = body.position;
        float yawBefore = Yaw(head);
        resolver.SubmitUtterance("turn right");
        yield return 0.05f;
        Check(outcomes.Count > 0 && outcomes[0] == CommandOutcome.Interrupted, "\"turn right\" interrupts it at once (" + (outcomes.Count > 0 ? outcomes[0].ToString() : "nothing") + ")");
        for (int i = 0; i < 80 && nav.IsBusy; i++) yield return 0.05f;
        float coast = Flat(body.position - atTurn).magnitude;
        Check(coast > 0.02f && coast < 0.7f, $"the move eases out over a short distance ({coast:F2} m), not a dead stop");
        Check(Mathf.Abs(Mathf.DeltaAngle(yawBefore, Yaw(head)) - 90f) < 3f, $"then turns right 90 degrees ({Mathf.DeltaAngle(yawBefore, Yaw(head)):F1})");
        Check(last == CommandOutcome.Completed && !nav.IsBusy, "and nothing resumes the old move");

        // ---- "Stop" eases; B stops at once
        resolver.SubmitUtterance("move forward");
        yield return 1.2f;
        float before = nav.SpeedNow;
        resolver.SubmitUtterance("stop");
        yield return 0.05f;
        float after = nav.SpeedNow;
        Check(after > 0.05f && after < before, $"\"stop\" eases down ({before:F2} -> {after:F2} m/s after 50 ms)");
        yield return 0.1f;
        Check(nav.SpeedNow > 0.05f, $"still easing 150 ms after \"stop\" ({nav.SpeedNow:F2} m/s), not stopped dead");
        yield return 0.8f;
        Check(nav.SpeedNow < 0.01f, "and is stopped within a second");
        resolver.SubmitUtterance("move forward");
        yield return 1.2f;
        resolver.SubmitUtterance("freeze");
        yield return 0.1f;
        Check(nav.SpeedNow > 0.3f, $"\"freeze\" eases down too ({nav.SpeedNow:F2} m/s after 100 ms)");
        yield return 1f;
        Check(nav.SpeedNow < 0.01f, "and stops");
        resolver.SubmitUtterance("move forward");
        yield return 1.2f;
        panel.CancelCommand();
        Check(nav.SpeedNow == 0f && !nav.IsBusy, "the cancel button stops at once");

        // ---- A bare "left" turns; "10 degrees to the left" turns 10; "turn around more" adds a little
        yield return 0.5f;
        Vector3 at = body.position;
        float y0 = Yaw(head);
        resolver.SubmitUtterance("left");
        for (int i = 0; i < 60 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        float turned = Mathf.DeltaAngle(y0, Yaw(head));
        Check(Mathf.Abs(turned + 90f) < 3f && Flat(body.position - at).magnitude < 0.05f,
              $"\"left\" turns left 90 degrees, without sliding ({turned:F1}, moved {Flat(body.position - at).magnitude:F2} m)");
        y0 = Yaw(head);
        resolver.SubmitUtterance("10 degrees to the left.");
        for (int i = 0; i < 60 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        turned = Mathf.DeltaAngle(y0, Yaw(head));
        Check(Mathf.Abs(turned + 10f) < 1.5f, $"\"10 degrees to the left\" turns 10 degrees ({turned:F1})");
        y0 = Yaw(head);
        resolver.SubmitUtterance("turn right a little");
        for (int i = 0; i < 60 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        float little = Mathf.DeltaAngle(y0, Yaw(head));
        y0 = Yaw(head);
        resolver.SubmitUtterance("turn around more");
        for (int i = 0; i < 80 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        turned = Mathf.DeltaAngle(y0, Yaw(head));
        Check(little > 5f && Mathf.Abs(turned - little) < 2f, $"\"turn around more\" after \"turn right a little\" turns a little more right ({turned:F1}, the little turn was {little:F1}), not 180");
        var build = typeof(SemanticIntentResolver).GetMethod("BuildUserPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        string memory = build != null ? (string)build.Invoke(resolver, new object[] { "do that again", "[]" }) : "";
        Check(memory.Contains("Recent exchanges") && memory.Contains("\"turn around more\"") && memory.Contains("\"turn right a little\"")
              && !memory.Contains("\"left\":"), "the LLM is told the last 3 exchanges:\n" + memory);

        // ---- Jump and land
        nav.Execute(new NavigationCommand { steps = new[] { new NavigationStep { action = ActionType.Move, direction = DirectionType.Up, condition = "" } } });
        yield return 0.2f;
        Check(body.position.y > floor + 0.1f, $"\"up\" jumps (y {body.position.y:F2})");
        yield return 1.5f;
        Check(gravity.IsGrounded && Mathf.Abs(body.position.y - floor) < 0.1f, $"and lands back on the floor (y {body.position.y:F2})");

        // ---- The Maze: minimap, and steps into a hedge stop at it
        panel.GoToWorld("Maze", "movement check");
        yield return 1.5f;
        Check(map != null && map.IsShowing && map.ShownWorld == "Maze", "the Maze's minimap shows in the Maze");
        if (map != null)
        {
            var m = GameObject.Find("World Minimap");
            Vector3 local = m != null ? Quaternion.Euler(0f, -Yaw(head), 0f) * (m.transform.position - head.position) : Vector3.zero;
            Check(m != null && local.x < -0.05f && local.y < -0.05f && local.z > 0.3f, $"low on the left, in front ({local.x:F2}, {local.y:F2}, {local.z:F2})");
        }
        var maze = GameObject.Find("Maze").transform;
        resolver.SubmitUtterance("move forward 4 meters"); // into the entrance corridor
        for (int i = 0; i < 80 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        resolver.SubmitUtterance("take 12 steps left");
        for (int i = 0; i < 200 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        Vector3 inMaze = maze.InverseTransformPoint(body.position);
        Check(last == CommandOutcome.Blocked, "12 steps into the hedge end Blocked (" + last + ")");
        Check(inMaze.z > 0.3f && inMaze.z < MazeTrail.CellSize, $"and the body stops at the hedge, inside the corridor (z {inMaze.z:F2} m)");

        // ---- The House: minimap, way out of a room, route through doorways
        panel.GoToWorld("House", "movement check");
        yield return 2f;
        Check(map != null && map.IsShowing && map.ShownWorld == "House", "the House's minimap shows in the House");
        var house = GameObject.Find("House").transform;
        panel.PlaceAt(house.TransformPoint(new Vector3(4.5f, 0f, 4f)), 90f, "House", "movement check");
        yield return 1f;
        Check(HouseRooms.RoomAt(head.position) == "living room", "placed in the living room (" + HouseRooms.RoomAt(head.position) + ")");
        resolver.SubmitUtterance("get me out of this room");
        for (int i = 0; i < 400 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        Check(HouseRooms.RoomAt(body.position) == "hall" && last == CommandOutcome.Completed,
            "\"get me out of this room\" ends in the hall (" + HouseRooms.RoomAt(body.position) + ", " + last + ")");
        var bed = house.Find("Props/Bed");
        var corners = new List<Vector3>();
        bool route = bed != null && RoutePlanner.TryRouteTo(bed, body.position, body.GetComponent<CharacterController>().radius, nav.stopGap, corners);
        Check(route && corners.Count > 2, $"a route from the hall to the bed goes round through the doorway ({corners.Count} corners)");

        // ---- Going to the TV with the coffee table in the way: round it, then facing the TV
        string detoured = null;
        nav.OnDetour += what => detoured = what;
        panel.PlaceAt(house.TransformPoint(new Vector3(4.5f, 0f, 6.3f)), house.eulerAngles.y + 180f, "House", "movement check");
        yield return 1f;
        var tv = house.Find("Props/Television");
        var scanNow = UnityEngine.Object.FindAnyObjectByType<FOVMetadataGrounding>().ScanFieldOfView();
        string seen = string.Join(", ", scanNow.ConvertAll(o => $"{o.name} {o.distanceMeters:F1} m {o.angleDegrees:F0}°").GetRange(0, Mathf.Min(6, scanNow.Count)));
        var tvRoute = new List<Vector3>();
        bool tvRouted = tv != null && RoutePlanner.TryRouteTo(tv, body.position, body.GetComponent<CharacterController>().radius, nav.stopGap, tvRoute);
        string routeText = tvRouted ? string.Join(" ", tvRoute.ConvertAll(p => { var l = house.InverseTransformPoint(p); return $"({l.x:F1},{l.z:F1})"; })) : "none";
        nav.Execute(new NavigationCommand { steps = new[] { new NavigationStep { action = ActionType.Move, direction = DirectionType.Forward, condition = "tv", until = UntilType.Reach } } });
        for (int i = 0; i < 600 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        Vector3 toTv = tv != null ? Flat(FOVMetadataGrounding.CenterOf(tv) - body.position) : Vector3.zero;
        Vector3 endLocal = house.InverseTransformPoint(body.position);
        Check(detoured != null, "the coffee table stops the straight way to the TV, so it goes round (" + (detoured ?? "no detour") +
              $"; ended at ({endLocal.x:F1}, {endLocal.z:F1}); in view: {seen}; the planner's route: {routeText})");
        Check(last == CommandOutcome.Completed && tv != null && toTv.magnitude < 2.2f,
              $"and arrives at the TV ({last}, {toTv.magnitude:F2} m from it)");
        Check(tv != null && Vector3.Angle(Flat(head.forward), toTv) < 15f, $"facing it ({Vector3.Angle(Flat(head.forward), toTv):F0} degrees off)");

        // ---- "Go through the door" from the living room: through the doorway into the hall, facing on
        panel.PlaceAt(house.TransformPoint(new Vector3(6f, 0f, 5f)), house.eulerAngles.y + 90f, "House", "movement check");
        yield return 1f;
        nav.Execute(new NavigationCommand { steps = new[] { new NavigationStep { action = ActionType.Move, direction = DirectionType.Forward, condition = "door", until = UntilType.Reach, through = true } } });
        for (int i = 0; i < 400 && (nav.IsBusy || i == 0); i++) yield return 0.05f;
        Vector3 inHouse = house.InverseTransformPoint(body.position);
        Check(last == CommandOutcome.Completed && HouseRooms.RoomAt(body.position) == "hall" && inHouse.x > 9.8f,
              $"\"go through the door\" ends in the hall, past the doorway ({last}, {HouseRooms.RoomAt(body.position)}, x {inHouse.x:F2})");
        Check(Mathf.Abs(Mathf.DeltaAngle(house.eulerAngles.y + 90f, Yaw(head))) < 15f, $"facing on ({Mathf.DeltaAngle(house.eulerAngles.y + 90f, Yaw(head)):F0} degrees off)");
        Check(gravity.IsGrounded, "still on the floor at the end");
    }
}
