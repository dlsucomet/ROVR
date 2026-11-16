// Assets/ROVR/QuickCommands.cs
// The primary movements, read straight from the words without the LLM, like the halt words: moving
// forward / back, sidestepping left / right, turning, and counted steps. Each sentence is read on its own,
// so nothing (a distance, an angle, a step count, a target) carries over from the previous command, and it
// takes over from a running command at once instead of after a model round trip.
//
//   "move forward", "keep going forward", "go back", "back up", "left"         continuous
//   "move forward 3 meters", "go back a bit"                                    a distance / "a bit"
//   "take two steps forward", "step left", "two steps to the right"             counted steps (sidesteps for left / right)
//   "sidestep left", "strafe right"                                             one sidestep / continuous
//   "turn right", "turn left 45 degrees", "turn 90 degrees to the right"         a turn (90 without an angle)
//   "turn around", "turn left a little"                                          180 / a small turn
//
// Only a whole sentence that is exactly one of these forms is read here (fillers like "please" and "now"
// allowed); anything else, such as a target ("move toward the tree"), a chain or a follow-up ("a bit
// more"), goes to the LLM as before. Plain C#, so it can be unit-tested.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ROVR
{
    public static class QuickCommands
    {
        static readonly string[] LeadFillers =
        {
            "please", "ok", "okay", "alright", "all right", "now", "can you", "could you", "would you", "will you",
            "just", "and", "so", "um", "uh", "hmm", "then", "i want to", "i'd like to", "let's", "lets"
        };
        static readonly string[] TailFillers = { "please", "now", "for me", "thanks", "thank you", "again" };

        const string Num = @"(?<n>\d+(?:\.\d+)?|a couple of|a couple|a few|half a|a half|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty five|forty|sixty|ninety|one hundred and eighty|one hundred eighty|a hundred and eighty|hundred and eighty|one eighty|half)";
        const string Dir = @"(?<d>forwards?|ahead|straight ahead|straight|backwards?|back|left|right|to the left|to the right|to your left|to your right)";
        const string Side = @"(?<d>left|right|to the left|to the right|to your left|to your right)";
        const string Walk = @"(?:keep |continue )?(?:move|moving|go|going|walk|walking|head|heading|run|running|travel)";
        const string Small = @"(?<small>a bit|a little|a little bit|slightly|a touch|a tad)";
        const string Meters = @"(?:meters?|metres?|m)";
        const string StepWord = @"(?:steps?|paces?)";

        static readonly Regex[] Moves =
        {
            new Regex($@"^{Walk}(?: straight)? {Dir}(?: {Num} {Meters})?$"),
            new Regex($@"^{Walk} {Num} {Meters} {Dir}$"),
            new Regex($@"^{Walk}(?: straight)? {Dir} {Small}$"),
            new Regex($@"^{Walk} {Small} {Dir}$"),
            new Regex($@"^(?<d>back) up(?: {Small})?$"),
            new Regex($@"^(?:keep going|keep moving|continue)$"),               // forward
            new Regex($@"^{Dir}$"),
            new Regex($@"^(?:strafe|slide|shift) {Side}(?: {Num} {Meters})?$"),
        };

        static readonly Regex[] Steps =
        {
            new Regex($@"^(?:take |walk |go )?{Num} (?:more )?(?:small |big )?{StepWord} {Dir}$"),
            new Regex($@"^(?:take |walk )?{Num} (?:more )?(?:side ?steps?) {Side}$"),
            new Regex($@"^(?:take a |take one )?(?:step|pace) {Dir}$"),
            new Regex($@"^(?:take a |take one )?side ?step {Side}$"),
            new Regex($@"^(?:step|pace) {Dir} {Num} (?:times|{StepWord})$"),
        };

        static readonly Regex[] Turns =
        {
            new Regex($@"^(?:turn|rotate|spin|face|look)(?: to)?(?: the| your)? (?<d>left|right)(?: by)?(?: {Num} degrees?)?$"),
            new Regex($@"^(?:turn|rotate|spin)(?: by)? {Num} degrees?(?: to)?(?: the| your)? (?<d>left|right)$"),
            new Regex($@"^(?:turn|rotate|spin)(?: to)?(?: the| your)? (?<d>left|right) {Small}$"),
            new Regex($@"^(?:turn|rotate|spin) {Small}(?: to)?(?: the| your)? (?<d>left|right)$"),
            new Regex(@"^(?:turn|spin) (?<around>around|round|back)$"),
            new Regex(@"^(?<around>about face|about turn)$"),
        };

        // A command for the sentence, or null when it isn't one of the quick forms.
        public static NavigationCommand TryParse(string utterance)
        {
            string t = Normalize(utterance);
            if (t.Length == 0) return null;

            foreach (var r in Turns)
            {
                var m = r.Match(t);
                if (!m.Success) continue;
                var step = new NavigationStep { action = ActionType.Turn, condition = string.Empty, until = UntilType.Reach };
                if (m.Groups["around"].Success) { step.direction = DirectionType.Right; step.amount = AmountType.Exact; step.magnitude = 180f; }
                else
                {
                    step.direction = m.Groups["d"].Value == "left" ? DirectionType.Left : DirectionType.Right;
                    if (m.Groups["small"].Success) step.amount = AmountType.Small;
                    else if (m.Groups["n"].Success)
                    {
                        float deg = NumberOf(m.Groups["n"].Value);
                        if (deg <= 0f || deg > 360f) return null;
                        step.amount = AmountType.Exact;
                        step.magnitude = deg;
                    }
                }
                return One(step);
            }

            foreach (var r in Steps)
            {
                var m = r.Match(t);
                if (!m.Success) continue;
                var dir = DirectionOf(m.Groups["d"].Value);
                if (dir == DirectionType.None) return null;
                float count = m.Groups["n"].Success ? NumberOf(m.Groups["n"].Value) : 1f;
                if (count <= 0f || count > 20f) return null;
                return One(new NavigationStep { action = ActionType.Move, direction = dir, amount = AmountType.Steps, magnitude = count, condition = string.Empty, until = UntilType.Reach });
            }

            for (int i = 0; i < Moves.Length; i++)
            {
                var m = Moves[i].Match(t);
                if (!m.Success) continue;
                var dir = m.Groups["d"].Success ? DirectionOf(m.Groups["d"].Value) : DirectionType.Forward;
                if (dir == DirectionType.None) return null;
                var step = new NavigationStep { action = ActionType.Move, direction = dir, amount = AmountType.None, condition = string.Empty, until = UntilType.Reach };
                if (m.Groups["small"].Success) step.amount = AmountType.Small;
                else if (m.Groups["n"].Success)
                {
                    float metres = NumberOf(m.Groups["n"].Value);
                    if (metres <= 0f || metres > 100f) return null;
                    step.amount = AmountType.Exact;
                    step.magnitude = metres;
                }
                return One(step);
            }
            return null;
        }

        static NavigationCommand One(NavigationStep step)
        {
            return new NavigationCommand { steps = new[] { step } };
        }

        static DirectionType DirectionOf(string word)
        {
            switch (word)
            {
                case "forward": case "forwards": case "ahead": case "straight": case "straight ahead": return DirectionType.Forward;
                case "back": case "backward": case "backwards": return DirectionType.Back;
                case "left": case "to the left": case "to your left": return DirectionType.Left;
                case "right": case "to the right": case "to your right": return DirectionType.Right;
                default: return DirectionType.None;
            }
        }

        static readonly Dictionary<string, float> Words = new Dictionary<string, float>
        {
            { "a", 1 }, { "an", 1 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 },
            { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 }, { "fifteen", 15 },
            { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 }, { "forty five", 45 }, { "sixty", 60 }, { "ninety", 90 },
            { "one hundred and eighty", 180 }, { "one hundred eighty", 180 }, { "a hundred and eighty", 180 },
            { "hundred and eighty", 180 }, { "one eighty", 180 }, { "half", 0.5f }, { "half a", 0.5f }, { "a half", 0.5f },
            { "a couple", 2 }, { "a couple of", 2 }, { "a few", 3 },
        };

        public static float NumberOf(string word)
        {
            if (Words.TryGetValue(word, out float v)) return v;
            return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
        }

        // Lower case, words only ("90°" -> "90 degrees", "3m" -> "3 m"), fillers off both ends.
        public static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = s.ToLowerInvariant().Replace("°", " degrees ").Replace("-", " ");
            s = Regex.Replace(s, @"(\d)(m|meters?|metres?)\b", "$1 $2");
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '.')
                {
                    if (c == '.' && (sb.Length == 0 || !char.IsDigit(sb[sb.Length - 1]))) { if (!space) { sb.Append(' '); space = true; } continue; }
                    sb.Append(c);
                    space = false;
                }
                else if (!space) { sb.Append(' '); space = true; }
            }
            string t = sb.ToString().Trim().TrimEnd('.');

            bool changed = true;
            while (changed && t.Length > 0)
            {
                changed = false;
                foreach (var f in LeadFillers)
                    if (t == f) return string.Empty;
                    else if (t.StartsWith(f + " ", StringComparison.Ordinal)) { t = t.Substring(f.Length + 1); changed = true; break; }
                foreach (var f in TailFillers)
                    if (t.EndsWith(" " + f, StringComparison.Ordinal)) { t = t.Substring(0, t.Length - f.Length - 1); changed = true; break; }
            }
            return t;
        }
    }
}
