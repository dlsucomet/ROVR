// Assets/GUI/StudyAssignments.cs
// Who gets which condition and which order of worlds. A participant types their ID (p001-p030) in the
// headset's opening menu (StudyMenu), and this fixed table gives their condition (ROVR or traditional)
// and their sequence of the three worlds. The same ID always gets the same assignment, so re-entering it
// after a crash changes nothing. A copy of the table for the researchers is Study/assignment-table.csv.
//
// The table is balanced: 15 participants per condition, each sequence 5 times. The first 12 IDs, and
// the next 12, each hold every condition x sequence pair once, so the groups stay balanced even if
// recruiting stops early. The last 6 hold each sequence once (3 ROVR, 3 traditional). Order within each
// block was shuffled once, with a fixed seed (Python's random, seed 2026).
//
// Pilot IDs (p901-p904) aren't in the table: the researcher picks their condition and sequence on the
// control page, so one pilot participant can try both conditions on two days.
using System;

public static class StudyAssignments
{
    public enum Lookup { Assigned, Pilot, Unknown }

    // The six orders of the three worlds. The thesis adviser's names call the Plain "world".
    // seq1 plains-maze-house, seq2 plains-house-maze, seq3 maze-plains-house,
    // seq4 maze-house-plains, seq5 house-maze-plains, seq6 house-plains-maze.
    private static readonly string[][] Sequences =
    {
        new[] { "Plain", "Maze", "House" },
        new[] { "Plain", "House", "Maze" },
        new[] { "Maze", "Plain", "House" },
        new[] { "Maze", "House", "Plain" },
        new[] { "House", "Maze", "Plain" },
        new[] { "House", "Plain", "Maze" },
    };

    // p001 is the first entry, p030 the last. R = ROVR (voice), T = traditional (controller); the digit is the sequence.
    private static readonly string[] Table =
    {
        // p001-p012: every pair once
        "R1", "T1", "R3", "R4", "T5", "R5", "T2", "T6", "T4", "T3", "R6", "R2",
        // p013-p024: every pair once
        "R6", "T5", "R5", "T6", "T1", "R3", "T3", "R2", "R1", "R4", "T2", "T4",
        // p025-p030: each sequence once
        "R1", "T5", "T4", "R2", "R3", "T6",
    };

    public const int FirstPilot = 901, LastPilot = 904;
    public const int SequenceCount = 6;

    // "7", "007", "p7", "P007" -> "p007". Null if it isn't 1-3 digits (after an optional p).
    public static string NormalizeId(string typed)
    {
        string s = (typed ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("p")) s = s.Substring(1);
        if (s.Length == 0 || s.Length > 3) return null;
        foreach (char c in s) if (c < '0' || c > '9') return null;
        int n = int.Parse(s);
        return n == 0 ? null : "p" + n.ToString("000");
    }

    public static Lookup Find(string id, out StudyCondition condition, out int sequence)
    {
        condition = StudyCondition.ROVR;
        sequence = 0;
        string normal = NormalizeId(id);
        if (normal == null) return Lookup.Unknown;
        int n = int.Parse(normal.Substring(1));
        if (n >= FirstPilot && n <= LastPilot) return Lookup.Pilot;
        if (n < 1 || n > Table.Length) return Lookup.Unknown;
        string entry = Table[n - 1];
        condition = entry[0] == 'T' ? StudyCondition.Manual : StudyCondition.ROVR;
        sequence = entry[1] - '0';
        return Lookup.Assigned;
    }

    public static int TableSize => Table.Length;

    public static bool IsValidSequence(int sequence) => sequence >= 1 && sequence <= Sequences.Length;

    // The worlds in order, as the panel names them ("Plain", "Maze", "House").
    public static string[] Worlds(int sequence)
    {
        if (!IsValidSequence(sequence)) throw new ArgumentOutOfRangeException(nameof(sequence));
        return (string[])Sequences[sequence - 1].Clone();
    }

    // "plains-maze-house"
    public static string SequenceName(int sequence)
    {
        var worlds = Worlds(sequence);
        return WorldLabel(worlds[0]) + "-" + WorldLabel(worlds[1]) + "-" + WorldLabel(worlds[2]);
    }

    // The world's name in file names and logs: "plains", "maze", "house".
    public static string WorldLabel(string world)
    {
        switch ((world ?? "").ToLowerInvariant())
        {
            case "plain": case "plains": return "plains";
            case "maze": return "maze";
            case "house": return "house";
            default: return (world ?? "").ToLowerInvariant();
        }
    }

    // The condition's name in file names and logs: "rovr" or "trad".
    public static string ConditionLabel(StudyCondition condition) => condition == StudyCondition.Manual ? "trad" : "rovr";
}
