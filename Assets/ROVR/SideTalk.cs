// Assets/ROVR/SideTalk.cs
// Whether a sentence is meant for ROVR at all, before it goes to the LLM. People talk while they navigate:
// to themselves ("I guess I'm ready"), to the researcher ("see you later"), or ask about what just
// happened ("was that 15 degrees?"). The model used to turn those into a move, so a sentence is only sent on
// when some part of it is a command: a quick command, a halt, a speed or go-back phrase, a command word or
// direction, a number with its unit, or an object or place in the world.
//
// Each part of the sentence (split at . ? ! ;) is judged on its own, so "where am I? okay. turn right 30
// degrees." still goes through. A part that starts as a question to oneself ("is that...", "where's...",
// "what's...") counts as nothing, unless it's a request ("could you walk to the door?", "can I go a
// bit further?"); a question mark alone doesn't ("one more step?"). The resolver doesn't use this while it waits for an answer ("that one", "ahead").
// Plain C#, no Unity types, so it can be checked on its own (Tools > ROVR > Check Commands).
using System;
using System.Collections.Generic;
using System.Text;

namespace ROVR
{
    public static class SideTalk
    {
        static readonly HashSet<string> CommandWords = new HashSet<string>
        {
            "move", "moving", "go", "going", "walk", "walking", "run", "head", "heading", "turn", "turning", "rotate",
            "spin", "face", "step", "steps", "sidestep", "strafe", "take", "come", "bring", "get", "lead", "guide",
            "forward", "forwards", "ahead", "straight", "back", "backward", "backwards", "left", "right", "around",
            "up", "jump", "keep", "continue", "again", "more", "further", "stop", "halt", "wait", "freeze",
            "faster", "slower", "quicker", "slowly", "quickly", "speed", "through", "toward", "towards", "into",
            "degrees", "degree", "meters", "meter", "metres", "metre", "inside", "out", "exit", "leave", "enter"
        };

        // Places and parts of the worlds a user may name that aren't scan names.
        static readonly HashSet<string> PlaceWords = new HashSet<string>
        {
            "room", "kitchen", "bedroom", "bathroom", "hall", "hallway", "corridor", "living", "dining",
            "marker", "beacon", "trail", "checkpoint", "maze", "field"
        };

        // Phrases that only count whole ("the start", not "I can start now").
        static readonly string[] Pointing = { "that one", "this one", "the other one", "the one", "over there", "the start" };

        static readonly string[] SelfQuestions =
        {
            "is that", "is it", "is this", "is there", "was that", "was it", "am i", "did i", "do i", "where is",
            "where's", "wheres", "what is", "what's", "whats", "what", "how do", "how far", "how much", "why", "who"
        };

        static readonly string[] Requests =
        {
            "can you", "could you", "would you", "will you", "can we", "could we", "can i", "could i", "may i"
        };

        // True when some part of the sentence is a command for ROVR. knownNames: the scan's object names in
        // the scene (FOVMetadataGrounding.KnownNames); may be null.
        public static bool IsCommand(string utterance, ICollection<string> knownNames)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return false;
            foreach (var part in Parts(utterance))
                if (PartIsCommand(part.text, part.question, knownNames)) return true;
            return false;
        }

        static bool PartIsCommand(string part, bool question, ICollection<string> knownNames)
        {
            string t = Words(part);
            if (t.Length == 0) return false;

            if (QuickCommands.TryParse(part) != null) return true;
            if (InterruptModule.Evaluate(part).isHalt) return true;
            if (SpeedWords.Evaluate(part) != SpeedChange.None || GoBackWords.IsGoBack(part)) return true;

            // A question to oneself ("was that 15 degrees?", "where's the kitchen?") isn't a command; a question
            // mark alone doesn't make one ("one more step?" is a request).
            if (!StartsWithAny(t, Requests) && StartsWithAny(t, SelfQuestions)) return false;

            string padded = " " + t + " ";
            foreach (var p in Pointing)
                if (padded.Contains(" " + p + " ")) return true;
            foreach (var w in t.Split(' '))
                if (CommandWords.Contains(w) || PlaceWords.Contains(w)) return true;
            if (knownNames != null && ObjectVocabulary.WordsIn(part, knownNames).Count > 0) return true;
            return false;
        }

        struct Part { public string text; public bool question; }

        // The sentence cut at . ? ! ; ("where am I? okay. move left" -> three parts, the first a question).
        static List<Part> Parts(string s)
        {
            var parts = new List<Part>();
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '.' || c == '?' || c == '!' || c == ';')
                {
                    // A decimal point ("1.5 meters") doesn't end a part.
                    if (c == '.' && sb.Length > 0 && char.IsDigit(sb[sb.Length - 1])) { sb.Append(c); continue; }
                    if (sb.ToString().Trim().Length > 0) parts.Add(new Part { text = sb.ToString(), question = c == '?' });
                    sb.Clear();
                }
                else sb.Append(c);
            }
            if (sb.ToString().Trim().Length > 0) parts.Add(new Part { text = sb.ToString(), question = false });
            return parts;
        }

        // Lower case, letters, digits and apostrophes, single spaces; fillers off the front ("okay, so turn").
        static string Words(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '\'') { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            string t = sb.ToString().Trim();
            foreach (var lead in new[] { "okay ", "ok ", "alright ", "all right ", "so ", "um ", "uh ", "and ", "then ", "now ", "please " })
                while (t.StartsWith(lead, StringComparison.Ordinal)) t = t.Substring(lead.Length);
            return t;
        }

        static bool StartsWithAny(string t, string[] leads)
        {
            foreach (var l in leads)
                if (t == l || t.StartsWith(l + " ", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
