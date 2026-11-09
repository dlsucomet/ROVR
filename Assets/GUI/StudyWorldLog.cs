// Assets/GUI/StudyWorldLog.cs
// One CSV per world run, for analysis: a temporal log of what the participant did in that world.
// Named p007-rovr-house.csv / p007-trad-plains.csv; a second run of the same world is ...-run2.csv, so
// nothing is ever overwritten. The clock (t_s) starts at the participant's first action in the world:
// for ROVR when they start speaking, for traditional their first stick push. Nothing before that is logged.
//
// Every row has the same columns:
//   participant_id, condition (rovr / trad), sequence_id (seq1-seq6), sequence (plains-maze-house),
//   world (plains / maze / house), world_position (1-3), run, t_s, clock_time, event, objective,
//   input, system, result, push, x_m, z_m, heading_deg
//
// Events:
//   objective_start / objective_done / objective_skipped   objective number; system = its instruction
//   position             once a second
//   world_end            result = completed, or incomplete and why
//   ROVR:   speech_start, heard (input = the words, or result "ignored as noise"), command (system = what
//           it will do), question, notice, error, halt, speed, move_end (result = completed / halted /
//           blocked / not found), mic (input = on / off)
//   Trad:   move (input = forward, forward-right, right, back-right, back, back-left, left, forward-left or
//           stopped; push = how far the left stick is pushed, 0-1), turn (input = left / right / stopped)
// x_m / z_m: metres from the world's start point, in the world's own axes (z = north); heading_deg: where
// the participant faces, 0 = north, 90 = east.
//
// Each row is written to the file at once (flushed), so a crash loses nothing. When the world ends the
// whole file goes to the ROVR bridge on the PC (StudyUploads), which saves it in Bridge/logs/study/. Files
// the bridge hasn't confirmed are sent again, also after the app restarts.
//
// Where: <project>/StudyData/ in the Editor (git-ignored: participant data), and
// <persistentDataPath>/StudyData/ on the headset (Android/data/com.hxil.ROVR/files/StudyData/).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class StudyWorldLog
{
    public const string Header = "participant_id,condition,sequence_id,sequence,world,world_position,run,t_s,clock_time," +
                                 "event,objective,input,system,result,push,x_m,z_m,heading_deg";

    public static string Folder
    {
        get
        {
#if UNITY_EDITOR
            // The Editor's flow check (Assets/Editor/StudyFlowCheck.cs) writes somewhere other than the real data
            // folder. SessionState, because entering Play mode resets static fields.
            string test = UnityEditor.SessionState.GetString(TestFolderKey, "");
            if (test.Length > 0) return test;
#endif
            return Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "StudyData")
                : Path.Combine(Application.persistentDataPath, "StudyData");
        }
    }

    public const string TestFolderKey = "ROVR.StudyFlowCheck.Folder";
    public const string TestNoUploadKey = "ROVR.StudyFlowCheck.NoUpload";

    public string FileName { get; }
    public string FilePath => Path.Combine(Folder, FileName);
    public int Run { get; }
    public int Rows { get; private set; }
    public bool ClockStarted { get; private set; }
    public float Seconds => ClockStarted ? Time.realtimeSinceStartup - _startedAt : 0f;

    private readonly string _prefix; // the first seven columns, the same on every row
    private StreamWriter _writer;
    private float _startedAt;

    private StudyWorldLog(string id, StudyCondition condition, int sequence, string world, int position)
    {
        string cond = StudyAssignments.ConditionLabel(condition), label = StudyAssignments.WorldLabel(world);
        string stem = id + "-" + cond + "-" + label;
        Run = NextRun(stem);
        FileName = stem + (Run > 1 ? "-run" + Run : "") + ".csv";
        _prefix = string.Join(",", id, cond, "seq" + sequence, StudyAssignments.SequenceName(sequence), label,
            position.ToString(CultureInfo.InvariantCulture), Run.ToString(CultureInfo.InvariantCulture));
    }

    // A new run of a world. The file is created when the clock starts (StartClock), not before.
    public static StudyWorldLog For(string id, StudyCondition condition, int sequence, string world, int position)
    {
        return new StudyWorldLog(id, condition, sequence, world, position);
    }

    // The participant's first action: t = 0 from now on, and the file is created.
    public void StartClock()
    {
        if (ClockStarted) return;
        ClockStarted = true;
        _startedAt = Time.realtimeSinceStartup;
        Open();
    }

    public void Row(string evt, int objective, string input, string system, string result, float push, Vector3 local, float heading)
    {
        if (_writer == null) return;
        var line = new StringBuilder(_prefix, 160);
        line.Append(',').Append(Seconds.ToString("F3", CultureInfo.InvariantCulture));
        line.Append(',').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        line.Append(',').Append(Csv(evt));
        line.Append(',').Append(objective > 0 ? objective.ToString(CultureInfo.InvariantCulture) : "");
        line.Append(',').Append(Csv(input));
        line.Append(',').Append(Csv(system));
        line.Append(',').Append(Csv(result));
        line.Append(',').Append(push >= 0f ? push.ToString("F2", CultureInfo.InvariantCulture) : "");
        line.Append(',').Append(local.x.ToString("F2", CultureInfo.InvariantCulture));
        line.Append(',').Append(local.z.ToString("F2", CultureInfo.InvariantCulture));
        line.Append(',').Append(Mathf.Repeat(heading, 360f).ToString("F0", CultureInfo.InvariantCulture));
        try
        {
            _writer.WriteLine(line.ToString());
            Rows++;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Study] Stopped writing " + FileName + ": " + e.Message);
            _writer = null;
        }
    }

    // The last row, then the file is closed and sent to the PC. A run whose clock never started has no
    // file unless keepIfEmpty, in which case it gets just the header and this row.
    public void End(string result, int objective, Vector3 local, float heading, bool keepIfEmpty)
    {
        if (!ClockStarted && !keepIfEmpty) return;
        if (_writer == null && !ClockStarted) Open();
        Row("world_end", objective, "", "", result, -1f, local, heading);
        try { _writer?.Dispose(); } catch (IOException) { }
        _writer = null;
        StudyUploads.Queue(FilePath);
    }

    private void Open()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(false)) { AutoFlush = true };
            _writer.WriteLine(Header);
            Debug.Log("<b>[Study]</b> Logging this world to " + FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Study] Could not create " + FilePath + ": " + e.Message);
            _writer = null;
        }
    }

    // 1 for the first run of this participant, condition and world; 2 if there is already one, and so on.
    private static int NextRun(string stem)
    {
        int highest = 0;
        if (Directory.Exists(Folder))
        {
            var pattern = new Regex("^" + Regex.Escape(stem) + @"(?:-run(\d+))?\.csv$", RegexOptions.IgnoreCase);
            foreach (string file in Directory.GetFiles(Folder, "*.csv"))
            {
                var m = pattern.Match(Path.GetFileName(file));
                if (m.Success) highest = Math.Max(highest, m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1);
            }
        }
        return highest + 1;
    }

    // The worlds (plains / maze / house) this participant has already completed in this condition, from
    // the files on this device: a run counts when its last row is a completed world_end.
    public static HashSet<string> CompletedWorlds(string id, StudyCondition condition)
    {
        var done = new HashSet<string>();
        if (!Directory.Exists(Folder)) return done;
        string cond = StudyAssignments.ConditionLabel(condition);
        var pattern = new Regex("^" + Regex.Escape(id + "-" + cond) + @"-(plains|maze|house)(?:-run\d+)?\.csv$", RegexOptions.IgnoreCase);
        foreach (string file in Directory.GetFiles(Folder, "*.csv"))
        {
            var m = pattern.Match(Path.GetFileName(file));
            if (!m.Success) continue;
            try
            {
                string last = null;
                foreach (string line in File.ReadLines(file)) if (line.Trim().Length > 0) last = line;
                if (last != null && last.Contains(",world_end,") && last.Contains(",completed")) done.Add(m.Groups[1].Value.ToLowerInvariant());
            }
            catch (IOException) { }
        }
        return done;
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string s = value.Replace("\r", " ").Replace("\n", " ");
        return s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

// Sends finished world CSVs to the bridge (POST /session/worldlog), one at a time, until it confirms each.
// A confirmed file gets a "<name>.sent" marker next to it; at start, any file without one is sent again.
public static class StudyUploads
{
    public static int Pending => _queue.Count;
    public static string LastSent { get; private set; }

    // The Editor's flow check keeps its test files off the PC's real study folder unless told otherwise.
    private static bool Paused
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.SessionState.GetBool(StudyWorldLog.TestNoUploadKey, false);
#else
            return false;
#endif
        }
    }

    private static readonly List<string> _queue = new List<string>();
    private static UnityWebRequest _request;
    private static string _sending;
    private static float _retryAt;
    private const float RetrySeconds = 5f;

    [Serializable] private class Upload { public string name; public string content; }

    public static void Queue(string path)
    {
        if (!string.IsNullOrEmpty(path) && !_queue.Contains(path) && File.Exists(path)) _queue.Add(path);
        _retryAt = 0f;
    }

    // Files from earlier runs of the app that the bridge never confirmed.
    public static void QueueUnsent()
    {
        if (!Directory.Exists(StudyWorldLog.Folder)) return;
        foreach (string file in Directory.GetFiles(StudyWorldLog.Folder, "*.csv"))
            if (!File.Exists(file + ".sent")) Queue(file);
    }

    // Called every frame by the session with the bridge's address (null when there is none).
    public static void Tick(string bridgeUrl)
    {
        if (_request != null)
        {
            if (!_request.isDone) return;
            bool ok = _request.result == UnityWebRequest.Result.Success;
            _request.Dispose();
            _request = null;
            if (ok)
            {
                try { File.WriteAllText(_sending + ".sent", DateTime.Now.ToString("o")); } catch (IOException) { }
                _queue.Remove(_sending);
                LastSent = Path.GetFileName(_sending);
                Debug.Log("<b>[Study]</b> Sent to the PC: " + LastSent);
            }
            else _retryAt = Time.realtimeSinceStartup + RetrySeconds;
            _sending = null;
        }
        if (Paused || _queue.Count == 0 || string.IsNullOrEmpty(bridgeUrl) || Time.realtimeSinceStartup < _retryAt) return;

        _sending = _queue[0];
        string content;
        try { content = File.ReadAllText(_sending); }
        catch (IOException) { _queue.RemoveAt(0); _sending = null; return; }
        string json = JsonUtility.ToJson(new Upload { name = Path.GetFileName(_sending), content = content });
        _request = new UnityWebRequest(bridgeUrl.TrimEnd('/') + "/session/worldlog", "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 10,
        };
        _request.SetRequestHeader("Content-Type", "application/json");
        _request.SendWebRequest();
    }
}
