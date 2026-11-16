// Assets/ROVR/TranscriptFilter.cs
// Cleans what Whisper returns before it reaches the navigation pipeline. Whisper annotates
// non-speech ("[BLANK_AUDIO]", "(silence)", "*music*") and, fed noise or silence, hallucinates
// stock phrases ("Thank you.", "Thanks for watching!"). None of that is a navigation command, and nor is a
// sentence of nothing but hesitation sounds ("Uh", "um, hmm"), which muting mid-sentence can send on, or of
// nothing but an acknowledgement or a reaction ("Okay.", "Alright", "Yeah, cool", "Got it", "Nice"): people say
// those to themselves, and the model used to turn them into a move. Said with a command ("okay, turn left")
// the command still goes through; only a sentence that is nothing else is dropped.
// Plain C#, no Unity types.
using System;
using System.Text;

namespace ROVR
{
    public static class TranscriptFilter
    {
        static readonly string[] Hallucinations =
        {
            "thank you", "thanks", "thank you for watching", "thanks for watching", "you", "bye", "bye bye", "the end"
        };

        static readonly string[] Fillers =
        {
            "uh", "uhh", "um", "umm", "uhm", "hmm", "hm", "mm", "mmm", "er", "erm", "ah", "eh", "oh",
            // acknowledgements and reactions
            "okay", "ok", "okey", "kay", "alright", "yes", "yeah", "yep", "yup", "sure", "cool", "nice", "good",
            "great", "perfect", "awesome", "wow", "whoa", "huh", "hey", "so", "gotit", "fine", "oki"
        };

        // Returns the cleaned text, or null when there is nothing worth acting on.
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string text = StripAnnotations(raw);
            text = string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length == 0) return null;

            string letters = LettersOnly(text);
            if (letters.Length == 0) return null;
            if (Array.IndexOf(Hallucinations, letters) >= 0) return null;
            string joined = (" " + letters + " ").Replace(" all right ", " alright ").Replace(" got it ", " gotit ").Trim();
            if (Array.TrueForAll(joined.Split(' '), w => Array.IndexOf(Fillers, w) >= 0)) return null;

            return text;
        }

        // Drops [..], (..) and *..* segments: sound and silence annotations.
        static string StripAnnotations(string s)
        {
            var sb = new StringBuilder(s.Length);
            char closer = '\0';
            foreach (char c in s)
            {
                if (closer != '\0') { if (c == closer) closer = '\0'; continue; }
                if (c == '[') { closer = ']'; continue; }
                if (c == '(') { closer = ')'; continue; }
                if (c == '*') { closer = '*'; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string LettersOnly(string s)
        {
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
