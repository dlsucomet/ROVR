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
        readonly Action<SpeedChange> speed;
        readonly Func<float> clock;
        readonly float duplicateWindow;

        bool haltedThisSentence;
        bool speedThisSentence;
        string lastSubmittedKey;
        float lastSubmittedAt = float.NegativeInfinity;

        public string LastHeard { get; private set; }

        // duplicateWindow: whisper.unity can report one sentence twice, almost at once. A person repeating
        // it takes longer (saying it, then the pause that ends it), so a short window drops only duplicates
        // and keeps rapid-fire repeats like "a bit more... a bit more".
        // speed: "faster" / "slower" / "normal speed" said on its own, acted on as soon as it's heard.
        public VoiceCommandRouter(Action halt, Action<string> submit, Func<float> clock, float duplicateWindow = 1.0f, Action<SpeedChange> speed = null)
        {
            this.halt = halt;
            this.submit = submit;
            this.speed = speed;
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
                return;
            }

            if (speed != null && !speedThisSentence)
            {
                var change = SpeedWords.Evaluate(clean);
                if (change != SpeedChange.None)
                {
                    speedThisSentence = true;
                    speed(change);
                }
            }
        }

        public void OnFinal(string text)
        {
            haltedThisSentence = false;
            bool speedDone = speedThisSentence;
            speedThisSentence = false;

            string clean = TranscriptFilter.Clean(text);
            if (clean == null) return;
            if (speedDone && SpeedWords.Evaluate(clean) != SpeedChange.None) { LastHeard = clean; return; } // already acted on

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
