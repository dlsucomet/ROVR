// Assets/ROVR/SpeedWords.cs
// "Faster", "slow down", "normal speed": fixed keywords that change the walking speed at once, without
// the LLM, like halt words. Only a whole short sentence counts ("go faster", "slower please"), so a
// command that mentions speed ("slowly go to the door") still goes to the LLM, which sets the speed
// for that one command. Plain C#, no Unity types.
using System;
using System.Text;

namespace ROVR
{
    public enum SpeedChange { None, Faster, Slower, Normal }

    public static class SpeedWords
    {
        static readonly string[] Faster = { "faster", "go faster", "move faster", "walk faster", "speed up", "quicker", "go quicker", "a bit faster", "a little faster" };
        static readonly string[] Slower = { "slower", "go slower", "move slower", "walk slower", "slow down", "a bit slower", "a little slower" };
        static readonly string[] Normal = { "normal speed", "regular speed", "normal pace", "default speed", "back to normal speed", "go at normal speed" };

        public static SpeedChange Evaluate(string utterance)
        {
            string t = Normalize(utterance);
            if (t.EndsWith(" please")) t = t.Substring(0, t.Length - 7);
            if (t.StartsWith("please ")) t = t.Substring(7);
            if (Array.IndexOf(Faster, t) >= 0) return SpeedChange.Faster;
            if (Array.IndexOf(Slower, t) >= 0) return SpeedChange.Slower;
            if (Array.IndexOf(Normal, t) >= 0) return SpeedChange.Normal;
            return SpeedChange.None;
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder();
            bool space = true;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetter(c)) { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }
    }
}
