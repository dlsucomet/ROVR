// Assets/ROVR/VoiceCommandRouter.cs
// Decides what to do with speech-to-text output. Plain C# with no Unity or Whisper types, so it
// can be unit-tested; VRControlPanel feeds it whisper.unity's stream events.
//
//   Partial text (the sentence so far, re-transcribed as you speak): only checked for a leading
//   halt word ("stop", "wait", "ops"), so movement stops before the sentence is finished.
//   Final text (the whole sentence): cleaned, de-duplicated, and sent to the full pipeline
//   (SemanticIntentResolver), which also handles corrections like "wait, I mean left".
using System;
using System.Text;

namespace ROVR
{
    public class VoiceCommandRouter
    {
        readonly Action halt;
        readonly Action<string> submit;
        readonly Func<float> clock;
        readonly float duplicateWindow;

        bool haltedThisSentence;
        string lastSubmittedKey;
        float lastSubmittedAt = float.NegativeInfinity;

        public string LastHeard { get; private set; }

        // duplicateWindow: whisper.unity can report one sentence twice, almost at once. A person repeating
        // it takes longer (saying it, then the pause that ends it), so a short window drops only duplicates
        // and keeps rapid-fire repeats like "a bit more... a bit more".
        public VoiceCommandRouter(Action halt, Action<string> submit, Func<float> clock, float duplicateWindow = 1.0f)
        {
            this.halt = halt;
            this.submit = submit;
            this.clock = clock;
            this.duplicateWindow = duplicateWindow;
        }

        public void OnPartial(string text)
        {
            if (haltedThisSentence) return;

            string clean = TranscriptFilter.Clean(text);
            if (clean == null) return;

            if (InterruptModule.Evaluate(clean).isHalt)
            {
                haltedThisSentence = true;
                halt();
            }
        }

        public void OnFinal(string text)
        {
            haltedThisSentence = false;

            string clean = TranscriptFilter.Clean(text);
            if (clean == null) return;

            string key = Key(clean);
            float now = clock();
            if (key == lastSubmittedKey && now - lastSubmittedAt < duplicateWindow) return;

            lastSubmittedKey = key;
            lastSubmittedAt = now;
            LastHeard = clean;
            submit(clean);
        }

        static string Key(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
