// Assets/ROVR/SemanticIntentResolver.cs
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
- condition: the object to move toward or stop at, in the user's own word, singular (tree, door, couch); when there is no target it is an empty string """" (never the word none)

TURNS. Only use a turn step when the user says turn, rotate, face or look. A bare direction ('left', 'back', 'a little to the left') means MOVE that way. 'turn around' = turn right, amount exact, magnitude 180. 'turn left' or 'turn right' with no number = amount none. 'turn around until you see the tree' = a turn step with condition Tree and amount none. For 'turn until you see X' the condition is always X as the user said it (singular), even when X is not in the visible objects list and even when something similar is listed, because turning is how the user finds it.

NUMBERS. Convert number words: 'five meters' = magnitude 5, 'half a meter' = magnitude 0.5, both amount exact.

VISIBLE OBJECTS is what the user can see right now. Each entry has name, distance_m, angle_deg (negative = left, positive = right, 0 = straight ahead) and side. Use it to understand the command. You do not decide whether an object is in view or which of several the user means: write the step with the object in the user's own word, and the system checks it against what is in view and asks the user when needed.

PREVIOUS COMMAND is only for follow-ups: 'a bit more', 'more' or 'again' repeat its direction with amount small; 'back' or 'the other way' keeps the same action (a turn stays a turn) with the opposite direction. If the command needs a previous command and it says 'Previous command: none', return no steps and ask what they want to do. Never take object information from the previous command.

CHOOSING. You cannot aim at one of several objects. If the user answers a question by picking one ('the left one'), return no steps and ask them to face it and repeat the command.

If you cannot tell what the user wants, return no steps and put a short question in clarification. Otherwise clarification is an empty string.

EXAMPLES
Command: move forward 5 meters
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""exact"",""magnitude"":5,""condition"":""""}],""clarification"":""""}
Command: go forward until you reach the door (a door is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""door""}],""clarification"":""""}
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
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""chair""}],""clarification"":""""}
Command: turn around until you see the tree (no tree in the visible objects list)
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""tree""}],""clarification"":""""}
Command: go to the couch (a sofa is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""couch""}],""clarification"":""""}
Command: go to the lamp (no lamp in the visible objects list)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""lamp""}],""clarification"":""""}";

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
            if (ollama != null) ollama.WarmUp(SystemPrompt);
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

        // Stops movement now and cancels any request still waiting on the LLM, so a late reply can't
        // restart it. For voice input's early "stop" check, which fires before the sentence is finished.
        public void HaltNow()
        {
            requestId++;
            controller.Halt();
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
                command => { if (id == requestId) OnResolved(command, visible, utterance); },
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

        void OnResolved(NavigationCommand command, System.Collections.Generic.List<GroundedObject> visible, string utterance)
        {
            // Closed World, enforced by the engine rather than trusted to the prompt.
            string problem = command.IsClarification ? null : FindGroundingProblem(command, visible, utterance);
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
        string FindGroundingProblem(NavigationCommand command, System.Collections.Generic.List<GroundedObject> visible, string utterance)
        {
            AttachMissingTarget(command, utterance);

            var introducedByTurn = new System.Collections.Generic.HashSet<string>();

            foreach (var step in command.steps)
            {
                if (!step.HasCondition) continue;

                string target = ResolveTarget(step.condition, utterance, visible);
                if (target == null)
                    return "I'm not sure what '" + step.condition + "' is here. What should I move toward?";
                step.condition = target;
                string name = grounding.NameFor(target);

                if (step.action == ActionType.Turn)
                {
                    introducedByTurn.Add(target); // turning until X is in view is how X gets found (6.4.2)

                    // Turning until you see something you can already see would silently do nothing.
                    if (command.steps.Length == 1 && visible.Exists(o => grounding.Matches(o, target)))
                        return "The " + name + " is already in view. Where do you want to go?";
                    continue;
                }

                bool inView = visible.Exists(o => grounding.Matches(o, target));
                if (!inView && !introducedByTurn.Contains(target))
                    return "I don't see " + Article(name) + " right now. Where is it relative to what you can see?";

                bool horizontal = step.direction == DirectionType.Forward || step.direction == DirectionType.Left || step.direction == DirectionType.Right;
                if (inView && horizontal && !introducedByTurn.Contains(target) && !grounding.IsCollapsedName(name))
                {
                    string aim = CheckAim(target, step.direction, visible);
                    if (aim != null) return aim;
                }
            }
            return null;
        }

        // "Go to the tree" must never become an endless walk because the model left out the target: a
        // move with no distance (or a "turn until you see") that has no target, when the user named
        // exactly one object, gets that object.
        void AttachMissingTarget(NavigationCommand command, string utterance)
        {
            var said = ObjectVocabulary.WordsIn(utterance, grounding.KnownNames());
            if (said.Count != 1) return;

            string text = " " + utterance.ToLowerInvariant() + " ";
            bool seeking = text.Contains(" see ") || text.Contains(" find ") || text.Contains(" face ");
            foreach (var step in command.steps)
            {
                if (step.HasCondition || step.HasMagnitude || step.amount != AmountType.None) continue;
                bool openMove = step.action == ActionType.Move && step.direction != DirectionType.Up && step.direction != DirectionType.Down;
                bool openTurn = step.action == ActionType.Turn && seeking;
                if (openMove || openTurn) step.condition = said[0];
            }
        }

        // The object a step is about, as a scan name ("sofa") or one object's id, worked out from the
        // word the model wrote and, above all, the words the user actually said: if the model named
        // something the user never mentioned, what the user said wins.
        string ResolveTarget(string condition, string utterance, System.Collections.Generic.List<GroundedObject> visible)
        {
            if (grounding.IsId(condition)) return condition.Trim().ToLowerInvariant();

            var known = grounding.KnownNames();
            var names = ObjectVocabulary.NamesFor(condition, known);
            var said = ObjectVocabulary.WordsIn(utterance, known);
            if (said.Count > 0 && !said.Exists(w => ObjectVocabulary.NamesFor(w, known).Exists(names.Contains)))
                names = ObjectVocabulary.NamesFor(said[0], known);
            if (names.Count == 0) return null;

            // Asked for "a chair" and an armchair is what's in view: that's the one.
            foreach (var n in names)
                if (visible.Exists(o => o.name == n)) return n;
            return names[0];
        }

        // Moving toward "the chair" only makes sense if a chair lies that way: roughly ahead for
        // forward (the user faces where they want to go, 3.3.1), or on that side for left/right.
        // Several in different directions is the thesis's ambiguity case (5.4.3): ask, rather than
        // pick one. Objects in a line (two doors down a corridor) aren't ambiguous, since the
        // nearest is reached first.
        string CheckAim(string target, DirectionType direction, System.Collections.Generic.List<GroundedObject> visible)
        {
            float minAngle = float.MaxValue, maxAngle = float.MinValue;
            int candidates = 0;
            GroundedObject nearest = default;
            bool haveNearest = false;

            foreach (var o in visible)
            {
                if (!grounding.Matches(o, target)) continue;

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

            string name = grounding.NameFor(target);
            if (candidates == 0)
            {
                string where = nearest.angleDegrees < 0f ? "left" : "right";
                return direction == DirectionType.Forward
                    ? "The " + name + " is off to your " + where + ". Turn to face it, then say it again."
                    : "I don't see " + Article(name) + " to your " + (direction == DirectionType.Left ? "left" : "right") + ". It's off to your " + where + ".";
            }
            if (maxAngle - minAngle > AmbiguousSpreadDegrees)
                return "There's more than one " + name + " in view. Face the one you mean and say it again.";
            return null;
        }

        static string Article(string name)
        {
            return ("aeiou".IndexOf(char.ToLowerInvariant(name[0])) >= 0 ? "an " : "a ") + name;
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
                    if (s.action == ActionType.Turn && s.HasCondition) { what = Article(grounding.NameFor(s.condition)); break; }
                OnStatus?.Invoke("I turned all the way around and couldn't find " + what + ".");
            }
        }
    }
}
