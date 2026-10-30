// Assets/ROVR/NavigationCommand.cs
// The four-vector kinematic schema the LLM is constrained to (Section 6.3.4): every parsed
// utterance becomes a sequence of { action, direction, amount/magnitude, condition } steps, or a
// clarification request when the command can't be grounded (6.2.5).
//
// Two layers on purpose: RawNavigationStep/RawNavigationCommand mirror the JSON the model
// returns (string enums, since Unity's JsonUtility can't deserialize enums from string names).
// NavigationCommand/NavigationStep are the typed form the rest of the pipeline consumes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ROVR
{
    public enum ActionType { Move, Turn, Stop, Unknown }
    public enum DirectionType { Forward, Back, Left, Right, Up, Down, None }

    // How much the user asked for. The LLM only labels it; the engine decides what "small" and a step
    // mean in metres (MovementHabits), so the model never does geometry. Steps: magnitude is the number
    // of steps ("take 3 steps"), 0 for walking in steps without a count ("walk in steps to the door").
    public enum AmountType { None, Exact, Small, Steps }

    public enum CommandOutcome { Completed, Halted, Blocked, NotFound }

    // What ends a step that has a target: getting to it (the default), or just seeing it
    // ("move forward and turn left when you see the door").
    public enum UntilType { Reach, See }

    // Walking speed: set by the experimenter's buttons or "faster" / "slower", or for one command.
    public enum SpeedLevel { Slow, Normal, Fast }

    [Serializable]
    public class RawNavigationStep
    {
        public string action;
        public string direction;
        public string amount;
        public float magnitude;
        public string condition;
        public string until;
        public string speed;
    }

    [Serializable]
    public class RawNavigationCommand
    {
        public RawNavigationStep[] steps;
        public string clarification;
        public string which;      // which of several matching objects: left | right | middle | nearest | farthest | looking
        public string which_near; // or the one next to this object ("the tree by the pond" = pond)
    }

    public class NavigationStep
    {
        public ActionType action;
        public DirectionType direction;
        public AmountType amount;
        public float magnitude;   // meters for Move (steps when amount is Steps), degrees for Turn; 0 = not specified
        public string condition;  // object to move to / turn until seen: a name ("tree") or one object's id ("tree-3"); empty = none
        public UntilType until;   // with a condition: stop on reaching it, or as soon as it's in view
        public SpeedLevel? speed; // this step only ("slowly go to the door"); null = the current speed
        public float stepLength;  // metres per step when amount is Steps; set by the engine (MovementHabits)

        public bool HasCondition => !string.IsNullOrEmpty(condition);
        public bool HasMagnitude => magnitude > 0f;
        public bool InSteps => amount == AmountType.Steps;
    }

    // The JSON schema handed to Ollama's `format` field to force structured output that maps
    // 1:1 onto RawNavigationCommand. Keep this in sync with the fields above by hand.
    public static class NavigationCommandSchema
    {
        public const string Json = @"{
  ""type"": ""object"",
  ""properties"": {
    ""steps"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": { ""type"": ""string"", ""enum"": [""move"", ""turn"", ""stop""] },
          ""direction"": { ""type"": ""string"", ""enum"": [""forward"", ""back"", ""left"", ""right"", ""up"", ""down"", ""none""] },
          ""amount"": { ""type"": ""string"", ""enum"": [""none"", ""exact"", ""small"", ""steps""] },
          ""magnitude"": { ""type"": ""number"" },
          ""condition"": { ""type"": ""string"" },
          ""until"": { ""type"": ""string"", ""enum"": [""reach"", ""see""] },
          ""speed"": { ""type"": ""string"", ""enum"": ["""", ""slow"", ""normal"", ""fast""] }
        },
        ""required"": [""action"", ""direction"", ""amount"", ""magnitude"", ""condition"", ""until"", ""speed""]
      }
    },
    ""clarification"": { ""type"": ""string"" },
    ""which"": { ""type"": ""string"", ""enum"": ["""", ""left"", ""right"", ""middle"", ""nearest"", ""farthest"", ""looking""] },
    ""which_near"": { ""type"": ""string"" }
  },
  ""required"": [""steps"", ""clarification"", ""which"", ""which_near""]
}";
    }

    public class NavigationCommand
    {
        public const string DidntCatch = "I didn't catch a movement command in that.";
        public const int MaxSteps = 10; // a longer chain is cut here

        public NavigationStep[] steps = Array.Empty<NavigationStep>();
        public string clarificationPrompt;
        public string which = string.Empty;     // how the user singled out one of several objects, if they did
        public string whichNear = string.Empty; // "the tree by the pond": pond

        public bool IsClarification => !string.IsNullOrEmpty(clarificationPrompt);
        public bool HasChoice => which.Length > 0 || whichNear.Length > 0;
        // Only an answer to "which one?" ("the left one", "that one"), with nothing to do yet.
        public bool IsChoiceOnly => steps.Length == 0 && !IsClarification && HasChoice;

        public static NavigationCommand Clarification(string prompt)
        {
            return new NavigationCommand { clarificationPrompt = prompt };
        }

        // Never returns a command that would silently do nothing (Section 5.5.6): anything the
        // model returns that has no usable step becomes a clarification instead.
        public static NavigationCommand FromRaw(RawNavigationCommand raw)
        {
            if (raw == null) return Clarification(DidntCatch);

            string which = (raw.which ?? string.Empty).Trim().ToLowerInvariant();
            string whichNear = (raw.which_near ?? string.Empty).Trim();
            if (IsNothing(whichNear)) whichNear = string.Empty;

            if (!string.IsNullOrEmpty(raw.clarification))
                return Clarification(raw.clarification);

            var steps = new List<NavigationStep>();
            if (raw.steps != null)
            {
                foreach (var r in raw.steps)
                {
                    if (r == null) continue;

                    var action = ParseEnum(r.action, ActionType.Unknown);
                    var direction = ParseEnum(r.direction, DirectionType.None);
                    if (action == ActionType.Unknown) continue;

                    string condition = (r.condition ?? string.Empty).Trim();
                    // Small models sometimes write the word instead of leaving it empty.
                    if (IsNothing(condition)) condition = string.Empty;
                    // "Go to the chair": moving toward an object with no stated direction means forward.
                    if (action == ActionType.Move && direction == DirectionType.None && condition.Length > 0)
                        direction = DirectionType.Forward;
                    if (action == ActionType.Move && direction == DirectionType.None) continue;

                    float magnitude = Math.Max(0f, r.magnitude);
                    var amount = ParseEnum(r.amount, AmountType.None);
                    if (amount == AmountType.Exact && magnitude <= 0f) amount = AmountType.None;
                    if (amount == AmountType.None && magnitude > 0f) amount = AmountType.Exact;
                    if (amount == AmountType.Small) magnitude = 0f;
                    if (amount == AmountType.Steps && action != ActionType.Move) { amount = AmountType.None; magnitude = 0f; } // only walking has steps

                    steps.Add(new NavigationStep
                    {
                        action = action,
                        direction = direction,
                        amount = amount,
                        magnitude = magnitude,
                        condition = condition,
                        until = ParseEnum(r.until, UntilType.Reach),
                        speed = string.IsNullOrEmpty(r.speed) ? (SpeedLevel?)null : ParseEnum(r.speed, SpeedLevel.Normal)
                    });
                    if (steps.Count == MaxSteps) break;
                }
            }

            if (steps.Count == 0 && which.Length == 0 && whichNear.Length == 0) return Clarification(DidntCatch);
            return new NavigationCommand { steps = steps.ToArray(), which = which, whichNear = whichNear };
        }

        // Compact form handed back to the LLM as "the previous command" for follow-ups.
        public string ToStateJson()
        {
            var sb = new StringBuilder("{\"steps\":[");
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"action\":\"").Append(s.action.ToString().ToLowerInvariant()).Append("\",")
                  .Append("\"direction\":\"").Append(s.direction.ToString().ToLowerInvariant()).Append("\",")
                  .Append("\"amount\":\"").Append(s.amount.ToString().ToLowerInvariant()).Append("\",")
                  .Append("\"magnitude\":").Append(s.magnitude.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"condition\":\"").Append(s.condition ?? string.Empty).Append("\",")
                  .Append("\"until\":\"").Append(s.until.ToString().ToLowerInvariant()).Append("\"}");
            }
            return sb.Append("]}").ToString();
        }

        static bool IsNothing(string s)
        {
            return s.Equals("none", StringComparison.OrdinalIgnoreCase) || s.Equals("null", StringComparison.OrdinalIgnoreCase)
                || s.Equals("n/a", StringComparison.OrdinalIgnoreCase) || s == "-";
        }

        static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            return Enum.TryParse(value, true, out T result) ? result : fallback;
        }
    }
}
