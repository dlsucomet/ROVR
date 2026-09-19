// Assets/Scripts/ROVR/MovementHabits.cs
// How far "a bit" is. It starts at 0.5 m and adapts to the person: the LLM only labels a move as
// "small", and the engine turns that into metres from this value (the model never does geometry).
//
//   - Nudge again the same way soon after a "small" move ("a bit forward... a bit more") -> the bit
//     was too short, so it grows.
//   - Reverse soon after a "small" move ("a bit forward... back a bit") -> it overshot, so it shrinks.
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

        public float BitMeters { get; private set; } = DefaultBitMeters;

        public void Reset() { BitMeters = DefaultBitMeters; }

        // `last` is the previous executed command and `lastOutcome` how it ended; `secondsSinceLast`
        // is how long ago it started. Only a cleanly completed "small" move counts as evidence.
        public void Observe(NavigationCommand next, NavigationCommand last, CommandOutcome? lastOutcome, float secondsSinceLast)
        {
            if (last == null || lastOutcome != CommandOutcome.Completed) return;
            if (secondsSinceLast > FollowUpWindowSeconds) return;

            var before = FirstMove(last);
            var after = FirstMove(next);
            if (before == null || after == null || before.amount != AmountType.Small) return;

            if (after.direction == before.direction)
                BitMeters = Math.Min(MaxBitMeters, BitMeters * GrowFactor);
            else if (after.direction == Opposite(before.direction))
                BitMeters = Math.Max(MinBitMeters, BitMeters * ShrinkFactor);
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
