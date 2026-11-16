// Assets/ROVR/InterruptModule.cs
// The Hardware Interrupt Module from Sections 5.5.2 / 6.3.3: halt or self-correction phrases
// bypass the primary NLU/LLM pipeline so they take effect immediately instead of waiting on a
// model round-trip.
//
// A halt phrase only counts when it LEADS the utterance ("stop", "wait, I mean left"), never when
// it appears mid-sentence ("move forward until you stop at the wall" is a normal command).
// After a halt, what follows is a new command and is kept: a correction ("wait, I mean left"), or a
// follow-up said in the same breath ("stop, turn left"). Only words that just describe the halt itself
// ("stop moving", "stop it", "wait a bit", "hold on a second") are dropped, so they can never restart
// movement. (On the headset the halt also ends the sentence there, so what's said next is its own sentence.)
//
// Caveat: the real design assumes a continuous streaming ASR buffer so a halt can land
// mid-utterance. This text stub only sees a complete string, so it's the closest testable analog.
using System;
using System.Text;

namespace ROVR
{
    public struct InterruptResult
    {
        public bool isHalt;
        public string remainder; // a self-correction to still send to the LLM; empty when there is none
    }

    public static class InterruptModule
    {
        // Halts; whatever follows is only kept when it starts with a correction cue.
        static readonly string[] HaltLeads =
        {
            "hold on", "hang on", "stop", "halt", "cancel", "freeze", "emergency", "wait", "whoa", "oops", "ops",
            // Whisper often hears a short "ops" as one of these (seen in testing: "Op stop", "Off stop").
            "op", "oop", "off"
        };

        // "no wait" is itself a correction, so what follows it is always kept.
        static readonly string[] CorrectingLeads = { "no wait" };

        static readonly string[] CorrectionCues =
        {
            "i meant", "i mean", "make that", "actually", "sorry", "rather", "instead", "no"
        };

        public static InterruptResult Evaluate(string utterance)
        {
            var none = new InterruptResult { isHalt = false, remainder = string.Empty };
            string t = Normalize(utterance);
            if (t.Length == 0) return none;

            foreach (var lead in CorrectingLeads)
                if (StartsWithWord(t, lead))
                    return new InterruptResult { isHalt = true, remainder = StripCues(t.Substring(lead.Length).Trim()) };

            foreach (var lead in HaltLeads)
            {
                if (!StartsWithWord(t, lead)) continue;

                string rest = t.Substring(lead.Length).Trim();
                // "Wait, go back" is a correction too: stop, then undo the move (GoBackWords).
                bool correction = StartsWithAny(rest, CorrectionCues) || GoBackWords.IsGoBack(rest);
                bool followUp = !correction && IsCommand(rest);
                return new InterruptResult { isHalt = true, remainder = correction || followUp ? StripCues(rest) : string.Empty };
            }

            return none;
        }

        // Words that start a movement command ("turn left", "go to the door", "take two steps").
        static readonly string[] CommandStarts =
        {
            "turn", "move", "go", "walk", "step", "take", "face", "head", "back", "look", "rotate", "come", "forward",
            "left", "right", "keep", "bring", "get", "slowly", "quickly", "sidestep", "strafe"
        };

        // Whether what follows a halt word is a command of its own, not more of the halt ("moving", "it",
        // "a bit", "for a second", "right there", "right now").
        static bool IsCommand(string rest)
        {
            if (rest.Length == 0) return false;
            if (StartsWithAny(rest, new[] { "moving", "going", "walking", "turning", "it", "that", "there", "now", "please",
                                            "a bit", "a second", "a sec", "a moment", "for a", "right there", "right now", "right here", "here" }))
                return false;
            return StartsWithAny(rest, CommandStarts) || QuickCommands.TryParse(rest) != null;
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            var sb = new StringBuilder(s.Length);
            bool lastSpace = true;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '\'')
                {
                    sb.Append(c);
                    lastSpace = false;
                }
                else if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            return sb.ToString().Trim();
        }

        static bool StartsWithWord(string t, string phrase)
        {
            return t == phrase || t.StartsWith(phrase + " ", StringComparison.Ordinal);
        }

        static bool StartsWithAny(string t, string[] phrases)
        {
            foreach (var p in phrases)
                if (StartsWithWord(t, p)) return true;
            return false;
        }

        static string StripCues(string s)
        {
            bool stripped = true;
            while (stripped && s.Length > 0)
            {
                stripped = false;
                foreach (var cue in CorrectionCues)
                {
                    if (!StartsWithWord(s, cue)) continue;
                    s = s.Substring(cue.Length).Trim();
                    stripped = true;
                    break;
                }
            }
            return s;
        }
    }
}
