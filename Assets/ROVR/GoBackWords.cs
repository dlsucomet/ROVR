// Assets/ROVR/GoBackWords.cs
// "Go back", "take me back", "undo that": a fixed phrase, like "stop", handled by the engine and never the
// LLM. It undoes the last command (NavigationController.PlanGoBack): turn round, walk back, and face the
// way you were facing, as if the move never happened.
//
// Only the bare phrase counts. "Go back 2 meters", "step back" and "back up a bit" are ordinary backward
// moves, and go to the LLM as before.
using System;
using System.Text;

namespace ROVR
{
    public static class GoBackWords
    {
        static readonly string[] Phrases =
        {
            "go back", "take me back", "put me back", "move back to where i was",
            "go back to where i was", "back to where i was", "take me back to where i was", "return to where i was",
            "go back to where i started", "go back where i was",
            "undo", "undo that", "undo it", "undo the last move", "undo the last command", "undo last move",
        };

        // Words around the phrase that don't change it ("okay, go back please").
        static readonly string[] Fillers = { "okay", "ok", "please", "um", "uh", "now", "just", "can you", "could you", "and" };

        public static bool IsGoBack(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return false;
            string text = " " + Letters(utterance) + " ";
            bool trimmed = true;
            while (trimmed)
            {
                trimmed = false;
                foreach (var f in Fillers)
                {
                    if (text.StartsWith(" " + f + " ")) { text = text.Substring(f.Length + 1); trimmed = true; }
                    if (text.EndsWith(" " + f + " ")) { text = text.Substring(0, text.Length - f.Length - 1); trimmed = true; }
                }
            }
            return Array.IndexOf(Phrases, text.Trim()) >= 0;
        }

        // Lower case, letters and single spaces only ("Go back." -> "go back").
        static string Letters(string s)
        {
            var sb = new StringBuilder();
            bool space = true;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetter(c) || c == '\'') { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }
    }
}
