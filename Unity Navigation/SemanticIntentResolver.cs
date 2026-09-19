// Assets/Scripts/ROVR/SemanticIntentResolver.cs
// Top-level orchestrator for the pipeline in Section 6.2.1:
//   utterance -> Interrupt Module -> POV metadata scan -> LLM -> engine checks -> execute or clarify.
//
// Design rules this class enforces in code, not just in the prompt:
//   - Closed World (6.2.4 / 6.3.2): a move may only target something in the current view. The engine
//     checks every condition the model returns against the scan and turns violations into a
//     clarification. "Turn until you see X" is the one exception (6.4.2): it's how you find X.
//   - One command of state: the previous command, how it ended, and any pending question are handed
//     back to the model so follow-ups ("a bit more", "the other way") work. Object knowledge still
//     only ever comes from the current view. (This relaxes the strict statelessness of 5.5.4 by
//     exactly one command; the thesis text needs to match.)
//   - Halts win (5.5.2 / 6.3.3): a halt cancels any request still waiting on the LLM, so a late reply
//     can never restart movement. A newer utterance likewise supersedes an older, unanswered one.
//   - No silent failures (5.5.6): unusable output, unknown objects, blocked moves and turns that find
//     nothing all produce a message for the user.
using System;
using UnityEngine;

namespace ROVR
{
    public class SemanticIntentResolver : MonoBehaviour
    {
        [SerializeField] OllamaClient ollama;
        [SerializeField] FOVMetadataGrounding grounding;
        [SerializeField] NavigationController controller;
        [SerializeField] float stateTimeoutSeconds = 60f; // older than this, the previous command is forgotten

        const float AheadDegrees = 25f;             // a target further off-axis than this isn't "ahead"
        const float AmbiguousSpreadDegrees = 10f;   // matches this far apart are different targets
        const float SideDegrees = 5f;               // 'to the left' means at least this far left of centre

        public event Action<string> OnClarificationNeeded;
        public event Action<string> OnStatus;   // blocked / not found notices
        public event Action<string> OnError;
        public event Action<NavigationCommand> OnCommandResolved;

        public MovementHabits Habits { get; } = new MovementHabits();
        public float LastLatencySeconds => ollama != null ? ollama.LastLatencySeconds : 0f;

        int requestId;
        NavigationCommand lastCommand;
        CommandOutcome? lastOutcome;          // null while it's still running
        float lastCommandTime;
        string pendingClarification;
        float clarificationTime;

        const string SystemPrompt = @"You are the navigation intent parser for ROVR, a voice-driven VR locomotion system. Turn the user's spoken command into JSON that matches the schema. Reply with JSON only, no prose.

STEPS. Each step has:
- action: move | turn | stop
- direction: forward | back | left | right | up | down | none
- amount: exact = the user gave a number (magnitude is meters for move, degrees for turn); small = they said a bit / a little / slightly / a touch (magnitude 0); none = no amount was given (magnitude 0, the movement continues until the user stops it or the condition is met)
- condition: the object type to move toward or stop at (Wall, Door, Chair, Tree), copied from the visible objects list; when there is no target it is an empty string """" (never the word none)

TURNS. Only use a turn step when the user says turn, rotate, face or look. A bare direction ('left', 'back', 'a little to the left') means MOVE that way. 'turn around' = turn right, amount exact, magnitude 180. 'turn left' or 'turn right' with no number = amount none. 'turn around until you see the tree' = a turn step with condition Tree and amount none. For 'turn until you see X' the condition is always X, even when X is not in the visible objects list, because turning is how the user finds it.

NUMBERS. Convert number words: 'five meters' = magnitude 5, 'half a meter' = magnitude 0.5, both amount exact.

VISIBLE OBJECTS is the only thing you know about the world. Each entry has tag, distance_m, angle_deg (negative = left, positive = right, 0 = straight ahead) and side. Never use an object that is not in the list, except as the target of a turn ('turn until you see X'). If the user names an object that is not in the list, return no steps and ask where it is. If several listed objects match and the command does not say which, ask which one. Moving toward an object only works when it is ahead of the user; if its side is left or right, ask them to turn toward it first.

PREVIOUS COMMAND is only for follow-ups: 'a bit more', 'more' or 'again' repeat its direction with amount small; 'back' or 'the other way' keeps the same action (a turn stays a turn) with the opposite direction. If the command needs a previous command and it says 'Previous command: none', return no steps and ask what they want to do. Never take object information from the previous command.

CHOOSING. You cannot aim at one of several objects. If the user answers a question by picking one ('the left one'), return no steps and ask them to face it and repeat the command.

If you cannot tell what the user wants, return no steps and put a short question in clarification. Otherwise clarification is an empty string.

EXAMPLES
Command: move forward 5 meters
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""exact"",""magnitude"":5,""condition"":""""}],""clarification"":""""}
Command: go forward until you reach the door (a Door is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""Door""}],""clarification"":""""}
Command: back up a bit
{""steps"":[{""action"":""move"",""direction"":""back"",""amount"":""small"",""magnitude"":0,""condition"":""""}],""clarification"":""""}
Command: a bit more (previous command was a small move forward)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""small"",""magnitude"":0,""condition"":""""}],""clarification"":""""}
Command: left
{""steps"":[{""action"":""move"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":""""}],""clarification"":""""}
Command: move right half a meter
{""steps"":[{""action"":""move"",""direction"":""right"",""amount"":""exact"",""magnitude"":0.5,""condition"":""""}],""clarification"":""""}
Command: a bit more (Previous command: none)
{""steps"":[],""clarification"":""More of what? Tell me which way to move.""}
Command: turn around until you see a chair
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""Chair""}],""clarification"":""""}
Command: turn around until you see the tree (no Tree in the visible objects list)
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""Tree""}],""clarification"":""""}
Command: go to the tree (no Tree in the visible objects list)
{""steps"":[],""clarification"":""I don't see a tree. Where is it relative to what you can see?""}";

        void Awake()
        {
            if (ollama == null) ollama = GetComponent<OllamaClient>();
            if (grounding == null) grounding = GetComponent<FOVMetadataGrounding>();
            if (controller == null) controller = GetComponent<NavigationController>();
        }

        void OnEnable()
        {
            if (controller != null) controller.OnCommandFinished += HandleFinished;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnCommandFinished -= HandleFinished;
        }

        void Start()
        {
            if (ollama != null) ollama.WarmUp();
        }

        // Call between participants so one person's habits and context never carry into the next trial.
        public void ResetSession()
        {
            requestId++;
            controller.Halt();
            Habits.Reset();
            lastCommand = null;
            lastOutcome = null;
            pendingClarification = null;
        }

        public void SubmitUtterance(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance)) return;

            // 5.5.2 / 6.3.3: halt phrases bypass the LLM and take effect immediately.
            var interrupt = InterruptModule.Evaluate(utterance);
            if (interrupt.isHalt)
            {
                requestId++;      // anything still waiting on the LLM predates this halt and must not run
                controller.Halt();
                if (string.IsNullOrEmpty(interrupt.remainder)) return;
                utterance = interrupt.remainder; // "wait, I mean left": the correction still goes through
            }

            // "More" / "again" mean nothing without a command to repeat. Say so without asking the model,
            // which would otherwise invent one.
            if (IsBareFollowUp(utterance) && !HasRecentCommand())
            {
                const string question = "More of what? Tell me which way to move.";
                pendingClarification = question;
                clarificationTime = Time.time;
                OnClarificationNeeded?.Invoke(question);
                return;
            }

            Resolve(utterance);
        }

        bool HasRecentCommand()
        {
            return lastCommand != null && Time.time - lastCommandTime <= stateTimeoutSeconds;
        }

        static readonly string[] BareFollowUps =
        {
            "more", "a bit more", "a little more", "a touch more", "some more", "again", "once more", "one more",
            "a bit further", "a little further", "further", "the other way", "the opposite way", "same again"
        };

        static bool IsBareFollowUp(string utterance)
        {
            var sb = new System.Text.StringBuilder();
            bool space = true;
            foreach (char c in utterance.ToLowerInvariant())
            {
                if (char.IsLetter(c)) { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return Array.IndexOf(BareFollowUps, sb.ToString().Trim()) >= 0;
        }

        void Resolve(string utterance)
        {
            int id = ++requestId;
            var visible = grounding.ScanFieldOfView(); // what the user sees right now
            string userPrompt = BuildUserPrompt(utterance, grounding.ToJson(visible));

            ollama.RequestCommand(SystemPrompt, userPrompt,
                command => { if (id == requestId) OnResolved(command, visible); },
                error => { if (id == requestId) OnError?.Invoke(error); });
        }

        string BuildUserPrompt(string utterance, string visibleJson)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Visible objects: ").Append(visibleJson).Append('\n');

            if (lastCommand != null && Time.time - lastCommandTime <= stateTimeoutSeconds)
            {
                string status = lastOutcome.HasValue ? lastOutcome.Value.ToString().ToLowerInvariant() : "running";
                sb.Append("Previous command (").Append(Mathf.RoundToInt(Time.time - lastCommandTime))
                  .Append(" s ago, ").Append(status).Append("): ").Append(lastCommand.ToStateJson()).Append('\n');
            }
            else
            {
                sb.Append("Previous command: none\n");
            }

            if (!string.IsNullOrEmpty(pendingClarification) && Time.time - clarificationTime <= stateTimeoutSeconds)
                sb.Append("Question you asked the user and are waiting on: ").Append(pendingClarification).Append('\n');

            sb.Append("Command: \"").Append(utterance).Append('"');
            return sb.ToString();
        }

        void OnResolved(NavigationCommand command, System.Collections.Generic.List<GroundedObject> visible)
        {
            // Closed World, enforced by the engine rather than trusted to the prompt.
            string problem = command.IsClarification ? null : FindGroundingProblem(command, visible);
            if (problem != null) command = NavigationCommand.Clarification(problem);

            OnCommandResolved?.Invoke(command);

            if (command.IsClarification)
            {
                // 6.2.5: pause physical execution and ask (5.5.6), rather than guess or fail silently.
                controller.Halt();
                pendingClarification = command.clarificationPrompt;
                clarificationTime = Time.time;
                OnClarificationNeeded?.Invoke(command.clarificationPrompt);
                return;
            }

            Habits.Observe(command, lastCommand, lastOutcome, Time.time - lastCommandTime);
            ResolveAmounts(command);

            lastCommand = command;
            lastOutcome = null;
            lastCommandTime = Time.time;
            pendingClarification = null;

            controller.Execute(command);
        }

        // Returns a question for the user if any step targets something the user can't currently see.
        string FindGroundingProblem(NavigationCommand command, System.Collections.Generic.List<GroundedObject> visible)
        {
            var introducedByTurn = new System.Collections.Generic.HashSet<string>();

            foreach (var step in command.steps)
            {
                if (!step.HasCondition) continue;

                if (!grounding.TryNormalizeTag(step.condition, out string tag))
                    return "I'm not sure what '" + step.condition + "' is here. What should I move toward?";
                step.condition = tag;

                if (step.action == ActionType.Turn)
                {
                    introducedByTurn.Add(tag); // turning until X is in view is how X gets found (6.4.2)

                    // Turning until you see something you can already see would silently do nothing.
                    if (command.steps.Length == 1 && visible.Exists(o => string.Equals(o.tag, tag, StringComparison.OrdinalIgnoreCase)))
                        return "The " + tag.ToLowerInvariant() + " is already in view. Where do you want to go?";
                    continue;
                }

                bool inView = visible.Exists(o => string.Equals(o.tag, tag, StringComparison.OrdinalIgnoreCase));
                if (!inView && !introducedByTurn.Contains(tag))
                    return "I don't see a " + tag.ToLowerInvariant() + " right now. Where is it relative to what you can see?";

                bool horizontal = step.direction == DirectionType.Forward || step.direction == DirectionType.Left || step.direction == DirectionType.Right;
                if (inView && horizontal && !introducedByTurn.Contains(tag) && !grounding.IsCollapsedTag(tag))
                {
                    string aim = CheckAim(tag, step.direction, visible);
                    if (aim != null) return aim;
                }
            }
            return null;
        }

        // Moving toward "the chair" only makes sense if a chair lies that way: roughly ahead for
        // forward (the user faces where they want to go, 3.3.1), or on that side for left/right.
        // Several in different directions is the thesis's ambiguity case (5.4.3): ask, rather than
        // pick one. Objects in a line (two doors down a corridor) aren't ambiguous, since the
        // nearest is reached first.
        string CheckAim(string tag, DirectionType direction, System.Collections.Generic.List<GroundedObject> visible)
        {
            float minAngle = float.MaxValue, maxAngle = float.MinValue;
            int candidates = 0;
            GroundedObject nearest = default;
            bool haveNearest = false;

            foreach (var o in visible)
            {
                if (!string.Equals(o.tag, tag, StringComparison.OrdinalIgnoreCase)) continue;

                if (!haveNearest || o.distanceMeters < nearest.distanceMeters) { nearest = o; haveNearest = true; }

                bool inDirection =
                    direction == DirectionType.Forward ? Mathf.Abs(o.angleDegrees) <= AheadDegrees :
                    direction == DirectionType.Left ? o.angleDegrees < -SideDegrees :
                    o.angleDegrees > SideDegrees;
                if (!inDirection) continue;

                candidates++;
                minAngle = Mathf.Min(minAngle, o.angleDegrees);
                maxAngle = Mathf.Max(maxAngle, o.angleDegrees);
            }

            string name = tag.ToLowerInvariant();
            if (candidates == 0)
            {
                string where = nearest.angleDegrees < 0f ? "left" : "right";
                return direction == DirectionType.Forward
                    ? "The " + name + " is off to your " + where + ". Turn to face it, then say it again."
                    : "I don't see a " + name + " to your " + (direction == DirectionType.Left ? "left" : "right") + ". It's off to your " + where + ".";
            }
            if (maxAngle - minAngle > AmbiguousSpreadDegrees)
                return "There's more than one " + name + " in view. Face the one you mean and say it again.";
            return null;
        }

        // "A bit" becomes the user's current bit size in metres; a small turn is one snap.
        void ResolveAmounts(NavigationCommand command)
        {
            foreach (var step in command.steps)
            {
                if (step.amount != AmountType.Small) continue;
                step.magnitude = step.action == ActionType.Move ? Habits.BitMeters : 0f;
            }
        }

        void HandleFinished(NavigationCommand command, CommandOutcome outcome)
        {
            if (command != lastCommand) return; // an older command being superseded

            lastOutcome = outcome;

            if (outcome == CommandOutcome.Blocked)
            {
                OnStatus?.Invoke("Something is in the way.");
            }
            else if (outcome == CommandOutcome.NotFound)
            {
                string what = "it";
                foreach (var s in command.steps)
                    if (s.action == ActionType.Turn && s.HasCondition) { what = "a " + s.condition.ToLowerInvariant(); break; }
                OnStatus?.Invoke("I turned all the way around and couldn't find " + what + ".");
            }
        }
    }
}
