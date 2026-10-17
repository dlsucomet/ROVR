// Assets/GUI/SessionTranscript.cs
// A plain-text log of one Play session: buttons pressed, teleports, what the mic heard and how it
// was transcribed, and what the panel showed back. One file per session, written line by line
// (flushed each time) so nothing is lost if Unity stops unexpectedly.
//
// Where: <project>/Transcripts/ in the Editor (git-ignored: it can hold participants' speech),
// <persistentDataPath>/Transcripts/ in a build. The Console prints the full path at the start.
//
// Each line: wall-clock time, time since the session started, a kind, and the details:
//   2026-10-17 14:02:11.482  +00:03.120  BUTTON    Maze
using System;
using System.IO;
using UnityEngine;

public class SessionTranscript
{
    public string FilePath { get; private set; }

    private StreamWriter _writer;
    private float _startTime;
    private readonly object _lock = new object();

    // Opens a new transcript file. Returns null (with a warning) if the file can't be created,
    // so a full disk or a read-only folder never stops the session itself.
    public static SessionTranscript Begin()
    {
        try
        {
            string folder = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Transcripts")
                : Path.Combine(Application.persistentDataPath, "Transcripts");
            Directory.CreateDirectory(folder);

            var transcript = new SessionTranscript();
            transcript.FilePath = Path.Combine(folder, "session_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
            transcript._writer = new StreamWriter(transcript.FilePath, false) { AutoFlush = true };
            transcript._startTime = Time.realtimeSinceStartup;

            Debug.Log("<b>[Transcript]</b> Saving this session to " + transcript.FilePath);
            return transcript;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Transcript] Could not create the transcript file, so this session is not saved: " + e.Message);
            return null;
        }
    }

    public void Log(string kind, string details)
    {
        lock (_lock)
        {
            if (_writer == null) return;

            TimeSpan elapsed = TimeSpan.FromSeconds(Time.realtimeSinceStartup - _startTime);
            string since = (elapsed.TotalHours >= 1 ? ((int)elapsed.TotalHours) + ":" : "") + elapsed.ToString(@"mm\:ss\.fff");
            string oneLine = (details ?? "").Replace("\r", " ").Replace("\n", " ");

            try
            {
                _writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  +" + since + "  " + kind.PadRight(8) + "  " + oneLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Transcript] Stopped saving: " + e.Message);
                _writer = null;
            }
        }
    }

    public void End()
    {
        Log("SESSION", "Ended");
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
