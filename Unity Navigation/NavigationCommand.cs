// Assets/Scripts/ROVR/NavigationCommand.cs
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

    // How much the user asked for. The LLM only labels it; the engine decides what "small" means
    // in metres (MovementHabits), so the model never does geometry.
    public enum AmountType { None, Exact, Small }

    public enum CommandOutcome { Completed, Halted, Blocked, NotFound }

    [Serializable]
    public class RawNavigationStep
    {
        public string action;
        public string direction;
        public string amount;
        public float magnitude;
        public string condition;
    }

    [Serializable]
    public class RawNavigationCommand
    {
        public RawNavigationStep[] steps;
        public string clarification;
    }

    public class NavigationStep
    {
        public ActionType action;
        public DirectionType direction;
        public AmountType amount;
        public float magnitude;   // meters for Move, degrees for Turn; 0 = not specified
        public string condition;  // object type to move to / turn until seen (e.g. "Wall"); empty = none

        public bool HasCondition => !string.IsNullOrEmpty(condition);
        public bool HasMagnitude => magnitude > 0f;
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
          ""amount"": { ""type"": ""string"", ""enum"": [""none"", ""exact"", ""small""] },
          ""magnitude"": { ""type"": ""number"" },
          ""condition"": { ""type"": ""string"" }
        },
        ""required"": [""action"", ""direction"", ""amount"", ""magnitude"", ""condition""]
      }
    },
    ""clarification"": { ""type"": ""string"" }
  },
  ""required"": [""steps"", ""clarification""]
}";
    }

    public class NavigationCommand
    {
        public const string DidntCatch = "I didn't catch a movement command in that.";

        public NavigationStep[] steps = Array.Empty<NavigationStep>();
        public string clarificationPrompt;

        public bool IsClarification => !string.IsNullOrEmpty(clarificationPrompt);

        public static NavigationCommand Clarification(string prompt)
        {
            return new NavigationCommand { clarificationPrompt = prompt };
        }

        // Never returns a command that would silently do nothing (Section 5.5.6): anything the
        // model returns that has no usable step becomes a clarification instead.
        public static NavigationCommand FromRaw(RawNavigationCommand raw)
        {
            if (raw == null) return Clarification(DidntCatch);

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
                    if (action == ActionType.Move && direction == DirectionType.None) continue;

                    float magnitude = Math.Max(0f, r.magnitude);
                    var amount = ParseEnum(r.amount, AmountType.None);
                    if (amount == AmountType.Exact && magnitude <= 0f) amount = AmountType.None;
                    if (amount == AmountType.None && magnitude > 0f) amount = AmountType.Exact;
                    if (amount == AmountType.Small) magnitude = 0f;

                    steps.Add(new NavigationStep
                    {
                        action = action,
                        direction = direction,
                        amount = amount,
                        magnitude = magnitude,
                        condition = (r.condition ?? string.Empty).Trim()
                    });
                }
            }

            if (steps.Count == 0) return Clarification(DidntCatch);
            return new NavigationCommand { steps = steps.ToArray() };
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
                  .Append("\"condition\":\"").Append(s.condition ?? string.Empty).Append("\"}");
            }
            return sb.Append("]}").ToString();
        }

        static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            return Enum.TryParse(value, true, out T result) ? result : fallback;
        }
    }
}
