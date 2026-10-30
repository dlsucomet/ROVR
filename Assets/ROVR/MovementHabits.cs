// Assets/ROVR/MovementHabits.cs
// How far "a bit" and a step are. "A bit" starts at 0.5 m and a step at 0.7 m (a typical walking
// step), and both adapt to the person: the LLM only labels a move as "small" or as a number of steps,
// and the engine turns that into metres from these values (the model never does geometry).
//
//   - Nudge again the same way soon after a "small" move ("a bit forward... a bit more") -> the bit
//     was too short, so it grows.
//   - Reverse soon after a "small" move ("a bit forward... back a bit") -> it overshot, so it shrinks.
//   - The same for steps: right after "take 3 steps", "a bit more" or "one more step" means the steps
//     were short, so they grow; going back means they were long, so they shrink.
//
// Learning is slow (15% per observation) and bounded, and lives in memory for one session only:
// call Reset() between participants so one person's habits never leak into the next trial.
using System;

namespace ROVR
{
    public class MovementHabits
    {
        public const float DefaultBitMeters = 0.5f;
        public const float MinBitMeters = 0.2f;
        public const float MaxBitMeters = 2f;
        const float FollowUpWindowSeconds = 8f;
        const float GrowFactor = 1.15f;
        const float ShrinkFactor = 0.85f;

        public const float DefaultStepMeters = 0.7f;
        public const float MinStepMeters = 0.4f;
        public const float MaxStepMeters = 1f;

        public float BitMeters { get; private set; } = DefaultBitMeters;
        public float StepMeters { get; private set; } = DefaultStepMeters;

        public void Reset() { BitMeters = DefaultBitMeters; StepMeters = DefaultStepMeters; }

        // `last` is the previous executed command and `lastOutcome` how it ended; `secondsSinceLast`
        // is how long ago it started. Only a cleanly completed "small" move or counted steps count as evidence.
        // saidMore: the user said "more", "again" or "further" ("one more step"); a fresh "take 3 steps"
        // after steps is the next instruction, not a sign the steps were short.
        public void Observe(NavigationCommand next, NavigationCommand last, CommandOutcome? lastOutcome, float secondsSinceLast, bool saidMore = false)
        {
            if (last == null || lastOutcome != CommandOutcome.Completed) return;
            if (secondsSinceLast > FollowUpWindowSeconds) return;

            var before = FirstMove(last);
            var after = FirstMove(next);
            if (before == null || after == null) return;
            bool again = after.direction == before.direction;
            bool back = after.direction == Opposite(before.direction);

            if (before.amount == AmountType.Small)
            {
                if (again) BitMeters = Math.Min(MaxBitMeters, BitMeters * GrowFactor);
                else if (back) BitMeters = Math.Max(MinBitMeters, BitMeters * ShrinkFactor);
            }
            // Counted steps: "a bit more" or "one more step" means they were short; "back a bit" or "step back"
            // means they were long. A fresh instruction ("take 3 steps", "move 2 meters", "walk in steps to
            // the door") says nothing about them.
            else if (before.amount == AmountType.Steps && before.HasMagnitude)
            {
                bool more = saidMore && (after.amount == AmountType.Small || after.InSteps);
                bool littleBack = after.amount == AmountType.Small || (after.InSteps && after.HasMagnitude && after.magnitude <= 1f);
                if (again && more) StepMeters = Math.Min(MaxStepMeters, StepMeters * GrowFactor);
                else if (back && littleBack) StepMeters = Math.Max(MinStepMeters, StepMeters * ShrinkFactor);
            }
        }

        static NavigationStep FirstMove(NavigationCommand c)
        {
            if (c == null || c.steps == null) return null;
            foreach (var s in c.steps)
                if (s.action == ActionType.Move) return s;
            return null;
        }

        static DirectionType Opposite(DirectionType d)
        {
            switch (d)
            {
                case DirectionType.Forward: return DirectionType.Back;
                case DirectionType.Back: return DirectionType.Forward;
                case DirectionType.Left: return DirectionType.Right;
                case DirectionType.Right: return DirectionType.Left;
                case DirectionType.Up: return DirectionType.Down;
                case DirectionType.Down: return DirectionType.Up;
                default: return DirectionType.None;
            }
        }
    }
}
