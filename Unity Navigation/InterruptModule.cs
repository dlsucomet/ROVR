// Assets/Scripts/ROVR/InterruptModule.cs
// The Hardware Interrupt Module from Sections 5.5.2 / 6.3.3: halt or self-correction phrases
// must bypass the primary NLU/LLM pipeline entirely so they take effect within milliseconds
// instead of waiting on a model round-trip.
//
// Caveat: the real design assumes a continuous streaming ASR buffer, so a halt phrase can
// interrupt mid-utterance. This text-command stub only ever sees a complete submitted string,
// so it's the closest testable analog for now — swap in a streaming check on the live transcript
// once real speech input replaces the debug console.
using System.Globalization;

namespace ROVR
{
    public static class InterruptModule
    {
        static readonly string[] HaltPhrases =
        {
            "stop", "wait", "halt", "hold on", "cancel", "no wait", "i mean", "actually"
        };

        public static bool TryDetectHalt(string utterance)
        {
            if (string.IsNullOrEmpty(utterance)) return false;

            string lower = utterance.ToLower(CultureInfo.InvariantCulture);
            foreach (var phrase in HaltPhrases)
            {
                if (lower.Contains(phrase)) return true;
            }
            return false;
        }
    }
}
