// Assets/Scripts/ROVR/NavigationCommand.cs
// The four-vector kinematic schema the LLM is constrained to (Section 6.3.4): every parsed
// utterance becomes a sequence of { action, direction, magnitude, condition } steps, or a
// clarification request when the referenced object isn't in the POV-scoped metadata (6.2.5).
//
// Two layers on purpose: RawNavigationStep/RawNavigationCommand mirror the JSON the model
// returns (string enums, since Unity's JsonUtility can't deserialize enums from string names).
// NavigationCommand/NavigationStep are the typed form the rest of the pipeline consumes.
using System;
using System.Collections.Generic;

namespace ROVR
{
    public enum ActionType { Move, Turn, Stop, Unknown }
    public enum DirectionType { Forward, Back, Left, Right, Up, Down, None }

    [Serializable]
    public class RawNavigationStep
    {
        public string action;
        public string direction;
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
        public float magnitude;   // meters for Move, degrees for Turn; 0 = not specified
        public string condition;  // semantic tag to ground against (e.g. "Wall"); empty = none

        public bool HasCondition => !string.IsNullOrEmpty(condition);
        public bool HasMagnitude => magnitude > 0f;
    }

    // The JSON schema handed to Ollama's `format` field to force structured output that maps
    // 1:1 onto RawNavigationCommand. Keep this in sync with the fields above by hand — there's
    // no reflection-based generation here, it's a small enough shape not to need one.
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
          ""magnitude"": { ""type"": ""number"" },
          ""condition"": { ""type"": ""string"" }
        },
        ""required"": [""action"", ""direction"", ""magnitude"", ""condition""]
      }
    },
    ""clarification"": { ""type"": ""string"" }
  },
  ""required"": [""steps"", ""clarification""]
}";
    }

    public class NavigationCommand
    {
        public NavigationStep[] steps = Array.Empty<NavigationStep>();
        public string clarificationPrompt;

        public bool IsClarification => !string.IsNullOrEmpty(clarificationPrompt);

        public static NavigationCommand FromRaw(RawNavigationCommand raw)
        {
            var command = new NavigationCommand();

            if (raw == null)
            {
                command.clarificationPrompt = "I didn't catch a movement command in that.";
                return command;
            }

            if (!string.IsNullOrEmpty(raw.clarification))
            {
                command.clarificationPrompt = raw.clarification;
                return command;
            }

            var steps = new List<NavigationStep>();
            if (raw.steps != null)
            {
                foreach (var rawStep in raw.steps)
                {
                    steps.Add(new NavigationStep
                    {
                        action = ParseEnum(rawStep.action, ActionType.Unknown),
                        direction = ParseEnum(rawStep.direction, DirectionType.None),
                        magnitude = rawStep.magnitude,
                        condition = rawStep.condition
                    });
                }
            }

            command.steps = steps.ToArray();
            return command;
        }

        static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            return Enum.TryParse(value, true, out T result) ? result : fallback;
        }
    }
}
