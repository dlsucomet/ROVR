// Assets/ROVR/SpeechSegmenter.cs
// Splits live microphone audio into sentences. A sentence ends only after DeadAirSec of silence, so
// a speaker who pauses mid-sentence ("move forward... five meters") isn't cut off. While a sentence
// is being spoken, its audio so far is handed out every PartialEverySec and at each short pause, so
// a halt word ("stop", "ops") can act before the sentence is finished.
// Plain C# with no Unity types, so it can be unit-tested; VRControlPanel feeds it microphone audio.
//
// Voice is told apart from silence by loudness: each 0.1 s frame is compared with the room's noise
// floor (a low percentile of the last few seconds), so it adapts to a fan or a quiet microphone.
using System;
using System.Collections.Generic;

namespace ROVR
{
    public class SpeechSegmenter
    {
        public float DeadAirSec = 1.5f;      // silence that ends a sentence
        public float PartialEverySec = 0.5f; // how often the sentence so far is handed out while speaking
        public float PreRollSec = 0.3f;      // audio kept from just before speech starts, so the first word isn't clipped
        public float MinSpeechSec = 0.2f;    // shorter bursts (a click, a tap) don't start a sentence
        public float MaxSentenceSec = 15f;   // a sentence is cut here even without a pause
        public float VoiceRatio = 3f;        // a frame is voice when this many times louder than the noise floor...
        public float MinVoiceLevel = 0.002f; // ...and at least this loud (RMS), so a silent mic isn't amplified into voice
        public float WarmUpFloor = 0.01f;    // noise floor assumed until the room has been heard for a second

        const float FrameSec = 0.1f;
        const float NoiseWindowSec = 5f;
        const float NoisePercentile = 0.2f;
        const int TrailingSilenceFramesKept = 3;

        public event Action<float[]> OnPartial;  // the sentence so far (for early halts and live captions)
        public event Action<float[]> OnSentence; // a finished sentence

        public int SampleRate { get; }
        public bool InSentence { get; private set; }

        readonly int frameSize;
        readonly float[] frame;
        int frameFill;

        readonly Queue<float> noiseHistory = new Queue<float>();
        readonly List<float> sortScratch = new List<float>();
        readonly Queue<float[]> recent = new Queue<float[]>(); // pre-roll plus a possible speech onset
        int onsetFrames;

        readonly List<float[]> sentence = new List<float[]>();
        int silenceFrames, framesSincePartial, speechSincePartial;

        public SpeechSegmenter(int sampleRate)
        {
            SampleRate = sampleRate;
            frameSize = Math.Max(1, (int)(sampleRate * FrameSec));
            frame = new float[frameSize];
        }

        // Mono samples at SampleRate, in any amount.
        public void AddSamples(float[] samples)
        {
            if (samples == null) return;
            foreach (float s in samples)
            {
                frame[frameFill++] = s;
                if (frameFill == frameSize)
                {
                    ProcessFrame((float[])frame.Clone());
                    frameFill = 0;
                }
            }
        }

        // Drops the sentence in progress (e.g. when the microphone is muted). What the room sounds like is
        // kept, since it hasn't changed.
        public void Reset()
        {
            frameFill = 0;
            recent.Clear();
            onsetFrames = 0;
            sentence.Clear();
            InSentence = false;
            silenceFrames = framesSincePartial = speechSincePartial = 0;
        }

        void ProcessFrame(float[] f)
        {
            bool voiced = IsVoice(f);

            if (!InSentence)
            {
                recent.Enqueue(f);
                onsetFrames = voiced ? onsetFrames + 1 : 0;
                int keep = Frames(PreRollSec) + Math.Max(1, Frames(MinSpeechSec));
                while (recent.Count > keep) recent.Dequeue();

                if (onsetFrames < Math.Max(1, Frames(MinSpeechSec))) return;

                InSentence = true;
                sentence.AddRange(recent);
                recent.Clear();
                onsetFrames = 0;
                silenceFrames = 0;
                framesSincePartial = 0;
                speechSincePartial = Frames(MinSpeechSec);
                return;
            }

            sentence.Add(f);
            framesSincePartial++;
            if (voiced) { silenceFrames = 0; speechSincePartial++; }
            else silenceFrames++;

            if (silenceFrames >= Frames(DeadAirSec) || sentence.Count >= Frames(MaxSentenceSec))
            {
                int drop = Math.Max(0, silenceFrames - TrailingSilenceFramesKept);
                sentence.RemoveRange(sentence.Count - drop, drop);
                float[] audio = Join(sentence);
                sentence.Clear();
                InSentence = false;
                silenceFrames = 0;
                OnSentence?.Invoke(audio);
                return;
            }

            // Hand out the sentence so far on a timer, and right at a short pause (the moment "stop."
            // has been said), but only when there's new speech since the last time.
            bool pauseJustStarted = !voiced && silenceFrames == 1;
            if (speechSincePartial > 0 && (framesSincePartial >= Frames(PartialEverySec) || pauseJustStarted))
            {
                framesSincePartial = 0;
                speechSincePartial = 0;
                OnPartial?.Invoke(Join(sentence));
            }
        }

        bool IsVoice(float[] f)
        {
            double sum = 0;
            foreach (float s in f) sum += s * s;
            float rms = (float)Math.Sqrt(sum / f.Length);

            noiseHistory.Enqueue(rms);
            while (noiseHistory.Count > Frames(NoiseWindowSec)) noiseHistory.Dequeue();

            sortScratch.Clear();
            sortScratch.AddRange(noiseHistory);
            sortScratch.Sort();
            float floor = sortScratch[(int)((sortScratch.Count - 1) * NoisePercentile)];
            // Too little history yet (just unmuted, maybe already talking): don't let speech become the floor.
            if (noiseHistory.Count < Frames(1f)) floor = Math.Min(floor, WarmUpFloor);

            return rms > Math.Max(floor * VoiceRatio, MinVoiceLevel);
        }

        int Frames(float seconds) { return (int)Math.Round(seconds / FrameSec); }

        static float[] Join(List<float[]> frames)
        {
            int n = 0;
            foreach (var f in frames) n += f.Length;
            var audio = new float[n];
            int at = 0;
            foreach (var f in frames) { Array.Copy(f, 0, audio, at, f.Length); at += f.Length; }
            return audio;
        }
    }
}
