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
//   - Which one (5.4.3): when several matching objects are in view, the engine asks and makes them
//     glow; the user answers by looking at one and saying "that one", or in words ("the left one",
//     "the one by the pond"). The pick is remembered, so repeating the command later doesn't ask again.
using System;
using System.Collections.Generic;
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
        public event Action<IList<Transform>> OnChoicesChanged; // objects to glow while asking "which one?"; empty = none
        public event Action<SpeedLevel> OnSpeedChanged;

        public MovementHabits Habits { get; } = new MovementHabits();
        public float LastLatencySeconds => ollama != null ? ollama.LastLatencySeconds : 0f;

        int requestId;
        NavigationCommand lastCommand;
        CommandOutcome? lastOutcome;          // null while it's still running
        float lastCommandTime;
        string pendingClarification;
        float clarificationTime;

        const float LookDegrees = 15f; // "that one" = the option within this of the middle of the view
        static readonly Transform[] NoObjects = new Transform[0];

        // Asked "which tree?": the command waiting for the answer, and the options.
        class PendingChoice
        {
            public NavigationCommand command;
            public NavigationStep step;
            public string name;
            public List<string> optionIds;
            public float time;
        }
        PendingChoice pendingChoice;
        List<Transform> glowNext; // what to glow once the current command has been checked

        // What the user last picked out of several, per name ("plant" -> "plant-4"), so repeating the
        // command after turning to face it doesn't ask again; the last pick is also what "it" means.
        readonly Dictionary<string, string> pickedFor = new Dictionary<string, string>();
        readonly Dictionary<string, float> pickedTime = new Dictionary<string, float>();
        string lastPickedId;
        float lastPickTime;

        const string SystemPrompt = @"You are the navigation intent parser for ROVR, a voice-driven VR locomotion system. Turn the user's spoken command into JSON that matches the schema. Reply with JSON only, no prose.

STEPS. Each step has:
- action: move | turn | stop
- direction: forward | back | left | right | up | down | none
- amount: exact = the user gave a number (magnitude is meters for move, degrees for turn); small = they said a bit / a little / slightly / a touch (magnitude 0); none = no amount was given (magnitude 0, the movement continues until the user stops it or the condition is met)
- condition: the object to move toward or stop at, in the user's own word, singular (tree, door, couch); when there is no target it is an empty string """" (never the word none)
- until: reach = the step ends on getting to the object (the default); see = it ends as soon as the object comes into view ('when you see the door')
- speed: slow, normal or fast when the user says how fast for this command ('slowly walk to the door', 'quickly go to the tree'); otherwise an empty string

SEVERAL STEPS. A command can have several steps, done in order. 'Move forward 10 meters, then turn right' = a move step, then a turn step. 'Face right when you reach the tree' = move to the tree, then turn right: put the steps in the order they happen, not the order they are said. 'Move forward and turn left when you see the door' = move forward with condition door and until see, then turn left. 'Turn right three times' = three turn steps; 'move forward 2 meters twice' = two move steps of 2 meters.

TURNS. Only use a turn step when the user says turn, rotate, face or look. A bare direction ('left', 'back', 'a little to the left') means MOVE that way. 'turn around' = turn right, amount exact, magnitude 180. 'turn left' or 'turn right' with no number = amount none. A turn can be any angle: 'turn 30 degrees to the right' = amount exact, magnitude 30; 'turn left a little' = amount small. 'Face the door', 'turn to the door' or 'look at the door' = a turn step with condition door (direction none unless a side is said). 'turn around until you see the tree' = a turn step with condition tree and amount none. For 'turn until you see X' the condition is always X as the user said it (singular), even when X is not in the visible objects list and even when something similar is listed, because turning is how the user finds it.

NUMBERS. Convert number words: 'five meters' = magnitude 5, 'half a meter' = magnitude 0.5, both amount exact.

VISIBLE OBJECTS is what the user can see right now. Each entry has name, distance_m, angle_deg (negative = left, positive = right, 0 = straight ahead) and side. Use it to understand the command. You do not decide whether an object is in view or which of several the user means: write the step with the object in the user's own word, and the system checks it against what is in view and asks the user when needed.

PREVIOUS COMMAND is only for follow-ups: 'a bit more', 'more' or 'again' repeat its direction with amount small; 'back' or 'the other way' keeps the same action (a turn stays a turn) with the opposite direction. If the command needs a previous command and it says 'Previous command: none', return no steps and ask what they want to do. Never take object information from the previous command.

WHICH ONE. When the user singles out one of several objects, set which: left, right, middle, nearest (the closer one), farthest, or looking (that one, this one, that tree: the one they are looking at); or set which_near to the object it is next to ('the tree by the pond' = pond). Otherwise both are empty strings. When you asked the user which one and they only answer that ('the left one', 'that one', 'the one by the pond'), return no steps and set which or which_near.

If you cannot tell what the user wants, return no steps and put a short question in clarification. Otherwise clarification is an empty string.

EXAMPLES
Command: move forward 5 meters
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""exact"",""magnitude"":5,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: go forward until you reach the door (a door is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""door"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: back up a bit
{""steps"":[{""action"":""move"",""direction"":""back"",""amount"":""small"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: a bit more (previous command was a small move forward)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""small"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: left
{""steps"":[{""action"":""move"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: move right half a meter
{""steps"":[{""action"":""move"",""direction"":""right"",""amount"":""exact"",""magnitude"":0.5,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: a bit more (Previous command: none)
{""steps"":[],""clarification"":""More of what? Tell me which way to move."",""which"":"""",""which_near"":""""}
Command: turn around until you see a chair
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""chair"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: turn around until you see the tree (no tree in the visible objects list)
{""steps"":[{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":""tree"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: go to the couch (a sofa is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""couch"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: go to the left plant (two plants are listed)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""plant"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":""left"",""which_near"":""""}
Command: go to that tree
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""tree"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":""looking"",""which_near"":""""}
Command: the one next to the pond (Question you asked: Which tree?)
{""steps"":[],""clarification"":"""",""which"":"""",""which_near"":""pond""}
Command: the closer one (Question you asked: Which chair?)
{""steps"":[],""clarification"":"""",""which"":""nearest"",""which_near"":""""}
Command: move forward 10 meters, then turn right
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""exact"",""magnitude"":10,""condition"":"""",""until"":""reach"",""speed"":""""},{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: face to the right when you reach the tree (a tree is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""tree"",""until"":""reach"",""speed"":""""},{""action"":""turn"",""direction"":""right"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: turn left when you get to the tree (a tree is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""tree"",""until"":""reach"",""speed"":""""},{""action"":""turn"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: move back 3 meters then left 2 meters
{""steps"":[{""action"":""move"",""direction"":""back"",""amount"":""exact"",""magnitude"":3,""condition"":"""",""until"":""reach"",""speed"":""""},{""action"":""move"",""direction"":""left"",""amount"":""exact"",""magnitude"":2,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: keep going forward and turn left when you see a door
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""door"",""until"":""see"",""speed"":""""},{""action"":""turn"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: turn left twice
{""steps"":[{""action"":""turn"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""},{""action"":""turn"",""direction"":""left"",""amount"":""none"",""magnitude"":0,""condition"":"""",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: slowly walk to the door (a door is ahead)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""door"",""until"":""reach"",""speed"":""slow""}],""clarification"":"""",""which"":"""",""which_near"":""""}
Command: go to the lamp (no lamp in the visible objects list)
{""steps"":[{""action"":""move"",""direction"":""forward"",""amount"":""none"",""magnitude"":0,""condition"":""lamp"",""until"":""reach"",""speed"":""""}],""clarification"":"""",""which"":"""",""which_near"":""""}";

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
            ClearChoice();
            pickedFor.Clear();
            pickedTime.Clear();
            lastPickedId = null;
            SetSpeed(SpeedLevel.Normal);
        }

        public SpeedLevel Speed => controller.Speed;

        // The experimenter's speed buttons.
        public void SetSpeed(SpeedLevel level)
        {
            controller.Speed = level;
            OnSpeedChanged?.Invoke(level);
        }

        // "Faster" / "slower" / "normal speed", one level at a time.
        public void ChangeSpeed(SpeedChange change)
        {
            var level = controller.Speed;
            if (change == SpeedChange.Faster) level = level == SpeedLevel.Slow ? SpeedLevel.Normal : SpeedLevel.Fast;
            else if (change == SpeedChange.Slower) level = level == SpeedLevel.Fast ? SpeedLevel.Normal : SpeedLevel.Slow;
            else if (change == SpeedChange.Normal) level = SpeedLevel.Normal;
            SetSpeed(level);
        }

        // Stops movement now and cancels any request still waiting on the LLM, so a late reply can't
        // restart it. For voice input's early "stop" check, which fires before the sentence is finished.
        public void HaltNow()
        {
            requestId++;
            controller.Halt();
            ClearChoice();
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
                ClearChoice();
                if (string.IsNullOrEmpty(interrupt.remainder)) return;
                utterance = interrupt.remainder; // "wait, I mean left": the correction still goes through
            }

            // "Faster", "slow down": a fixed keyword, like a halt (the LLM handles "slowly go to the door").
            var speedChange = SpeedWords.Evaluate(utterance);
            if (speedChange != SpeedChange.None)
            {
                ChangeSpeed(speedChange);
                return;
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

            // An answer to "which one?" ("that one", "the left one", "the one by the stool") is read
            // here, not by the LLM: it's a short, fixed set of forms, and the model was seen to turn
            // "the one next to the stool" into "go to the stool".
            if (ChoicePending() && TryReadAnswer(utterance, out NavigationCommand answer))
            {
                requestId++;
                ResolveChoice(answer);
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
                command =>
                {
                    if (id != requestId) return;
                    // The model was seen to drop a step from "move forward 10 meters, then turn right".
                    // If it returned fewer steps than the actions the user chained, ask about each part.
                    var clauses = SplitClauses(utterance);
                    if (clauses.Count > 1 && !command.IsClarification && command.steps.Length < clauses.Count)
                        ResolveClauses(clauses, 0, new List<NavigationStep>(), visible, utterance, id);
                    else
                        OnResolved(command, visible, utterance);
                },
                error => { if (id == requestId) OnError?.Invoke(error); });
        }

        // Asks the LLM about each chained part in turn and runs them as one command.
        void ResolveClauses(List<string> clauses, int index, List<NavigationStep> steps, List<GroundedObject> visible, string utterance, int id)
        {
            if (index == clauses.Count)
            {
                OnResolved(new NavigationCommand { steps = steps.ToArray() }, visible, utterance);
                return;
            }
            ollama.RequestCommand(SystemPrompt, BuildUserPrompt(clauses[index], grounding.ToJson(visible)),
                part =>
                {
                    if (id != requestId) return;
                    if (part.IsClarification) { OnResolved(part, visible, utterance); return; }
                    steps.AddRange(part.steps);
                    ResolveClauses(clauses, index + 1, steps, visible, utterance, id);
                },
                error => { if (id == requestId) OnError?.Invoke(error); });
        }

        static readonly System.Text.RegularExpressions.Regex ClauseBreak = new System.Text.RegularExpressions.Regex(
            @"\s*,?\s*(?:\band then\b|\bthen\b|\bafter that\b|\band\b(?=\s+(?:turn|move|go|walk|face|step|head|run|back)\b))\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // "Move forward 10 meters, then turn right" -> two parts. Sentences that tie steps to an event
        // ("turn left when you see the door") stay whole: those aren't simple sequences.
        public static List<string> SplitClauses(string utterance)
        {
            var parts = new List<string>();
            string lower = " " + utterance.ToLowerInvariant() + " ";
            foreach (var tie in new[] { " when ", " until ", " once ", " as soon as ", " after you ", " before ", " till " })
                if (lower.Contains(tie)) { parts.Add(utterance); return parts; }

            foreach (var part in ClauseBreak.Split(utterance))
                if (part.Trim().Length > 0 && part.IndexOfAny("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray()) >= 0)
                    parts.Add(part.Trim().Trim(',', '.'));
            return parts;
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

        void OnResolved(NavigationCommand command, List<GroundedObject> visible, string utterance)
        {
            KeepSaidChoice(command, utterance);
            if (command.IsChoiceOnly)
            {
                if (ChoicePending()) { ResolveChoice(command); return; }
                command = NavigationCommand.Clarification("Which one, and what should I do with it?");
            }
            else
            {
                ClearChoice(); // a new command replaces an unanswered "which one?"
            }
            Proceed(command, visible, utterance);
        }

        // Closed World checks (enforced by the engine rather than trusted to the prompt), then either
        // ask (6.2.5: pause and ask rather than guess or fail silently, 5.5.6) or execute.
        void Proceed(NavigationCommand command, List<GroundedObject> visible, string utterance)
        {
            glowNext = null;
            string problem = command.IsClarification ? null : FindGroundingProblem(command, visible, utterance);
            if (problem != null) command = NavigationCommand.Clarification(problem);
            OnChoicesChanged?.Invoke(glowNext != null ? (IList<Transform>)glowNext : NoObjects);

            OnCommandResolved?.Invoke(command);

            if (command.IsClarification)
            {
                Ask(command.clarificationPrompt);
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

        void Ask(string question)
        {
            controller.Halt();
            pendingClarification = question;
            clarificationTime = Time.time;
            OnClarificationNeeded?.Invoke(question);
        }

        // Returns a question for the user if any step targets something the user can't currently see.
        string FindGroundingProblem(NavigationCommand command, List<GroundedObject> visible, string utterance)
        {
            AttachMissingTarget(command, utterance);
            ApplyRepeatsAndTurnAround(command, utterance);

            bool viewChanged = false; // after a move or a turn, what's in view is different: later targets are found when their step starts

            foreach (var step in command.steps)
            {
                bool changesView = step.action == ActionType.Move || step.action == ActionType.Turn;
                if (!step.HasCondition) { viewChanged |= changesView; continue; }

                string target = ResolveTarget(step.condition, utterance, visible);
                if (target == null)
                    return "I'm not sure what '" + step.condition + "' is here. What should I move toward?";
                step.condition = target;
                string name = grounding.NameFor(target);

                if (viewChanged) continue; // NavigationController finds it when the step starts
                viewChanged |= changesView;

                if (step.action == ActionType.Move && step.until == UntilType.See)
                {
                    // "Turn left when you see the door" with the door already in view means at the door.
                    if (visible.Exists(o => grounding.Matches(o, target))) step.until = UntilType.Reach;
                    else continue;
                }

                if (step.action == ActionType.Turn)
                {
                    // Turning until X is in view is how X gets found (6.4.2), so X needn't be in view now; if
                    // it is, the turn faces it. Already facing it, the turn would silently do nothing.
                    if (command.steps.Length == 1 && visible.Exists(o => grounding.Matches(o, target) && Mathf.Abs(o.angleDegrees) <= 3f))
                        return "You're already facing the " + name + ". Where do you want to go?";
                    continue;
                }

                if (!visible.Exists(o => grounding.Matches(o, target)))
                    return "I don't see " + Article(name) + " right now. Where is it relative to what you can see?";

                bool horizontal = step.direction == DirectionType.Forward || step.direction == DirectionType.Left || step.direction == DirectionType.Right;
                if (horizontal && !grounding.IsCollapsedName(name))
                {
                    string aim = CheckAim(step, command, visible);
                    if (aim != null) return aim;
                }
            }
            return null;
        }

        // "Go to the tree" must never become an endless walk because the model left out the target: a
        // one-step move with no distance (or a "turn until you see") that has no target, when the user
        // named exactly one object, gets that object. Only one-step commands: in "face right when you
        // reach the tree" the turn has no target, and mustn't get one.
        void AttachMissingTarget(NavigationCommand command, string utterance)
        {
            if (command.steps.Length != 1) return;
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

        // Things the model was seen to get wrong in chains, read from the user's words instead: "turn
        // right three times" came back as one turn, "...then turn around" lost its 180 degrees, and
        // "left 2 meters" became a turn.
        void ApplyRepeatsAndTurnAround(NavigationCommand command, string utterance)
        {
            string text = " " + utterance.ToLowerInvariant().Replace(',', ' ').Replace('.', ' ') + " ";

            int times = text.Contains(" twice ") || text.Contains(" two times ") || text.Contains(" 2 times ") ? 2
                : text.Contains(" three times ") || text.Contains(" thrice ") || text.Contains(" 3 times ") ? 3
                : text.Contains(" four times ") || text.Contains(" 4 times ") ? 4
                : text.Contains(" five times ") || text.Contains(" 5 times ") ? 5 : 1;
            if (times > 1 && command.steps.Length == 1)
            {
                var one = command.steps[0];
                var repeated = new NavigationStep[times];
                for (int i = 0; i < times; i++)
                    repeated[i] = new NavigationStep { action = one.action, direction = one.direction, amount = one.amount, magnitude = one.magnitude, condition = one.condition, until = one.until };
                command.steps = repeated;
            }

            // "Left 2 meters" came back as a 2-degree turn. Turns are in degrees: a sized "turn" in a
            // command that talks in meters, never degrees, is a move.
            bool saysMeters = text.Contains(" meter") || text.Contains(" metre") || text.Contains(" m ");
            bool saysDegrees = text.Contains(" degree");
            if (saysMeters && !saysDegrees)
                foreach (var step in command.steps)
                    if (step.action == ActionType.Turn && step.HasMagnitude && step.magnitude < 20f) step.action = ActionType.Move;

            bool turnAround = (text.Contains(" turn around ") || text.Contains(" turn round ")) && !text.Contains(" until ") && !text.Contains(" see ");
            if (turnAround)
                foreach (var step in command.steps)
                    if (step.action == ActionType.Turn && !step.HasMagnitude && !step.HasCondition && step.amount == AmountType.None)
                    {
                        step.amount = AmountType.Exact;
                        step.magnitude = 180f;
                    }
        }

        // The object a step is about, as a scan name ("sofa") or one object's id, worked out from the
        // word the model wrote and, above all, the words the user actually said: if the model named
        // something the user never mentioned, what the user said wins.
        string ResolveTarget(string condition, string utterance, List<GroundedObject> visible)
        {
            if (grounding.IsId(condition)) return condition.Trim().ToLowerInvariant();

            // "Go to it", "face that": the object the user picked last.
            string word = condition.Trim().ToLowerInvariant();
            if ((word == "it" || word == "that" || word == "this" || word == "that one" || word == "this one" || word == "there")
                && lastPickedId != null && Time.time - lastPickTime <= stateTimeoutSeconds)
                return lastPickedId;

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
        // Several that way is the thesis's ambiguity case (5.4.3): use how the user singled one out
        // ("the left one", "that one"), or the one they picked before; otherwise ask, with the options
        // glowing. Objects in a line (two doors down a corridor) aren't ambiguous: the nearest is
        // reached first. A pick that is off to the side still asks the user to turn to it first.
        string CheckAim(NavigationStep step, NavigationCommand command, List<GroundedObject> visible)
        {
            string name = grounding.NameFor(step.condition);
            var matches = visible.FindAll(o => grounding.Matches(o, step.condition));

            // "Go to the left plant": the user already said which, even if it's off to the side.
            if (command.HasChoice && matches.Count > 1 && TryPick(matches, command.which, command.whichNear, visible, out GroundedObject said))
            {
                Remember(name, said.id);
                step.condition = said.id;
                matches = new List<GroundedObject> { said };
            }

            var thatWay = matches.FindAll(o => InDirection(o, step.direction));
            if (thatWay.Count == 0)
            {
                var nearest = matches[0];
                foreach (var o in matches) if (o.distanceMeters < nearest.distanceMeters) nearest = o;
                if (grounding.IsId(step.condition)) glowNext = new List<Transform> { nearest.target }; // show which one to turn to
                string where = nearest.angleDegrees < 0f ? "left" : "right";
                return step.direction == DirectionType.Forward
                    ? "The " + name + " is off to your " + where + ". Turn to face it, then say it again."
                    : "I don't see " + Article(name) + " to your " + (step.direction == DirectionType.Left ? "left" : "right") + ". It's off to your " + where + ".";
            }

            float minAngle = float.MaxValue, maxAngle = float.MinValue;
            foreach (var o in thatWay) { minAngle = Mathf.Min(minAngle, o.angleDegrees); maxAngle = Mathf.Max(maxAngle, o.angleDegrees); }
            if (maxAngle - minAngle <= AmbiguousSpreadDegrees)
            {
                // One object that way (or several in a line): head for the nearest, so the walk steers to it.
                var nearestThatWay = thatWay[0];
                foreach (var o in thatWay) if (o.distanceMeters < nearestThatWay.distanceMeters) nearestThatWay = o;
                step.condition = nearestThatWay.id;
                return null;
            }

            if (TryPicked(name, thatWay, out GroundedObject before))
            {
                step.condition = before.id;
                return null;
            }

            pendingChoice = new PendingChoice
            {
                command = command, step = step, name = name, time = Time.time,
                optionIds = thatWay.ConvertAll(o => o.id)
            };
            glowNext = thatWay.ConvertAll(o => o.target);
            return "Which " + name + "? Look at the one you mean and say \"that one\", or tell me " + DescribeOptions(thatWay) + ".";
        }

        bool InDirection(GroundedObject o, DirectionType direction)
        {
            return direction == DirectionType.Forward ? Mathf.Abs(o.angleDegrees) <= AheadDegrees :
                   direction == DirectionType.Left ? o.angleDegrees < -SideDegrees :
                   o.angleDegrees > SideDegrees;
        }

        // ---------------------------------------------------------------- which one

        bool ChoicePending()
        {
            return pendingChoice != null && Time.time - pendingChoice.time <= stateTimeoutSeconds;
        }

        void ClearChoice()
        {
            bool had = pendingChoice != null;
            pendingChoice = null;
            if (had || glowNext != null) { glowNext = null; OnChoicesChanged?.Invoke(NoObjects); }
        }

        // The answer to "which one?": pick from the options still in view and run the waiting command.
        void ResolveChoice(NavigationCommand answer)
        {
            var pc = pendingChoice;
            var visible = grounding.ScanFieldOfView();
            var options = visible.FindAll(o => pc.optionIds.Contains(o.id));
            if (options.Count == 0)
            {
                ClearChoice();
                OnCommandResolved?.Invoke(NavigationCommand.Clarification("I can't see any of them from here now. Where do you want to go?"));
                Ask("I can't see any of them from here now. Where do you want to go?");
                return;
            }

            if (!TryPick(options, answer.which, answer.whichNear, visible, out GroundedObject picked))
            {
                string again = answer.which == "looking"
                    ? "I can't tell which one you're looking at. Look straight at it and say \"that one\", or tell me " + DescribeOptions(options) + "."
                    : "Which " + pc.name + "? Look at the one you mean and say \"that one\", or tell me " + DescribeOptions(options) + ".";
                OnCommandResolved?.Invoke(NavigationCommand.Clarification(again));
                Ask(again);
                return;
            }

            Remember(pc.name, picked.id);
            pc.step.condition = picked.id;
            pendingChoice = null;
            Proceed(pc.command, visible, string.Empty);
        }

        // The words that make each choice. The model sometimes adds a choice the user never made ("go to
        // the plant" came back as "the nearest"), which would skip the question, so a choice only
        // counts when the user's own words say it.
        static readonly Dictionary<string, string[]> ChoiceWords = new Dictionary<string, string[]>
        {
            { "left", new[] { "left" } },
            { "right", new[] { "right" } },
            { "middle", new[] { "middle", "center", "centre" } },
            { "nearest", new[] { "near", "nearer", "nearest", "close", "closer", "closest" } },
            { "farthest", new[] { "far", "farther", "farthest", "further", "furthest" } },
            { "looking", new[] { "that", "this", "there", "looking" } },
        };

        static readonly string[] SmallWords = { "bit", "little", "slightly", "touch", "tad", "nudge", "smidge", "more", "again", "further" }; // "more" and "again" repeat a small move

        void KeepSaidChoice(NavigationCommand command, string utterance)
        {
            // A move to an object heads for the object. The model was seen to turn "the left plant" into
            // a move to the left, and to copy "left" from the previous command into "go to the plant":
            // a side the user never said is dropped, and "the left X" is which one, not which way.
            string spoken = " " + string.Join(" ", WordsOf(utterance)) + " ";

            // "Slowly walk to the tree" came back as "a bit" toward the tree: "a bit" needs the words for it.
            bool saidSmall = Array.Exists(SmallWords, w => spoken.Contains(" " + w + " "));
            foreach (var step in command.steps)
                if (step.amount == AmountType.Small && !saidSmall) step.amount = AmountType.None;

            foreach (var step in command.steps)
            {
                if (step.action != ActionType.Move || !step.HasCondition) continue;
                if (step.direction != DirectionType.Left && step.direction != DirectionType.Right) continue;
                string side = step.direction == DirectionType.Left ? "left" : "right";
                if (!spoken.Contains(" " + side + " ")) step.direction = DirectionType.Forward;
                else if (spoken.Contains(" the " + side + " "))
                {
                    step.direction = DirectionType.Forward;
                    if (command.which.Length == 0) command.which = side;
                }
            }

            // "The plant by the stool": the target is the plant, picked by what it's next to.
            var known = grounding.KnownNames();
            string nearWord = ObjectVocabulary.NearObjectIn(utterance, known);
            if (nearWord != null)
            {
                command.whichNear = nearWord;
                string other = ObjectVocabulary.WordsIn(utterance, known).Find(w => !ObjectVocabulary.SameThing(w, nearWord, known));
                foreach (var step in command.steps)
                    if (step.HasCondition && other != null && ObjectVocabulary.SameThing(step.condition, nearWord, known))
                        step.condition = other;
            }

            var words = new HashSet<string>(utterance.ToLowerInvariant().Split(new[] { ' ', ',', '.', '!', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries));
            if (command.which.Length > 0 && !(ChoiceWords.TryGetValue(command.which, out string[] cues) && Array.Exists(cues, words.Contains)))
                command.which = string.Empty;

            if (command.whichNear.Length > 0)
            {
                var nearNames = ObjectVocabulary.NamesFor(command.whichNear, known);
                if (!ObjectVocabulary.WordsIn(utterance, known).Exists(w => ObjectVocabulary.NamesFor(w, known).Exists(nearNames.Contains)))
                    command.whichNear = string.Empty;
            }
        }

        bool TryPick(List<GroundedObject> options, string which, string near, List<GroundedObject> visible, out GroundedObject picked)
        {
            picked = default;
            if (options.Count == 0) return false;

            if (!string.IsNullOrEmpty(near))
            {
                var names = ObjectVocabulary.NamesFor(near, grounding.KnownNames());
                var anchors = visible.FindAll(o => names.Contains(o.name) && !options.Exists(c => c.target == o.target));
                if (anchors.Count == 0) return false;
                float best = float.MaxValue;
                foreach (var o in options)
                    foreach (var a in anchors)
                    {
                        Vector3 d = CenterOf(o.target) - CenterOf(a.target); d.y = 0f;
                        if (d.magnitude < best) { best = d.magnitude; picked = o; }
                    }
                return true;
            }

            switch (which)
            {
                case "looking":
                    var looked = LookedAt(options.ConvertAll(o => o.target));
                    if (looked == null) return false;
                    picked = options.Find(o => o.target == looked);
                    return true;
                case "left": picked = Best(options, o => -o.angleDegrees); return true;
                case "right": picked = Best(options, o => o.angleDegrees); return true;
                case "middle": picked = Best(options, o => -Mathf.Abs(o.angleDegrees)); return true;
                case "nearest": picked = Best(options, o => -o.distanceMeters); return true;
                case "farthest": picked = Best(options, o => o.distanceMeters); return true;
                default: return false;
            }
        }

        static GroundedObject Best(List<GroundedObject> options, Func<GroundedObject, float> score)
        {
            var best = options[0];
            foreach (var o in options) if (score(o) > score(best)) best = o;
            return best;
        }

        bool TryPicked(string name, List<GroundedObject> options, out GroundedObject picked)
        {
            picked = default;
            if (!pickedFor.TryGetValue(name, out string id) || Time.time - pickedTime[name] > stateTimeoutSeconds) return false;
            int i = options.FindIndex(o => o.id == id);
            if (i < 0) return false;
            picked = options[i];
            return true;
        }

        void Remember(string name, string id)
        {
            pickedFor[name] = id;
            pickedTime[name] = Time.time;
            lastPickedId = id;
            lastPickTime = Time.time;
        }

        // Of these objects, the one the user is looking at: nearest the middle of the view, within
        // LookDegrees. Null if none is.
        public Transform LookedAt(IList<Transform> objects)
        {
            Transform pov = grounding.Pov;
            if (pov == null || objects == null) return null;
            Transform best = null;
            float bestAngle = LookDegrees;
            foreach (var t in objects)
            {
                if (t == null) continue;
                float angle = Vector3.Angle(pov.forward, CenterOf(t) - pov.position);
                if (angle <= bestAngle) { bestAngle = angle; best = t; }
            }
            return best;
        }

        static Vector3 CenterOf(Transform t) { return FOVMetadataGrounding.CenterOf(t); }

        // "the one on your left, the one ahead or the one on your right" (or nearer / farther).
        static string DescribeOptions(List<GroundedObject> options)
        {
            var sides = new List<string>();
            foreach (var side in new[] { "left", "ahead", "right" })
                if (options.Exists(o => FOVMetadataGrounding.SideOf(o.angleDegrees) == side))
                    sides.Add(side == "ahead" ? "the one ahead" : "the one on your " + side);
            if (sides.Count < 2)
                return "the nearer or the farther one";
            return sides.Count == 2 ? sides[0] + " or " + sides[1] : sides[0] + ", " + sides[1] + " or " + sides[2];
        }

        // Reads a short answer to "which one?": "that one" (by looking), "the left one", "the one on the
        // right", "the closer one", "the one next to the stool". False for anything that's a command.
        bool TryReadAnswer(string utterance, out NavigationCommand answer)
        {
            answer = null;
            var words = WordsOf(utterance);
            if (words.Count == 0 || words.Count > 8) return false;
            foreach (var verb in new[] { "go", "move", "walk", "turn", "head", "take", "face", "stop", "back" })
                if (words.Contains(verb)) return false;

            if (IsPointing(utterance)) { answer = new NavigationCommand { which = "looking" }; return true; }

            string near = ObjectVocabulary.NearObjectIn(utterance, grounding.KnownNames());
            if (near != null) { answer = new NavigationCommand { whichNear = near }; return true; }

            foreach (var choice in ChoiceWords)
                if (choice.Key != "looking" && Array.Exists(choice.Value, words.Contains))
                {
                    answer = new NavigationCommand { which = choice.Key };
                    return true;
                }
            if (words.Contains("ahead") || words.Contains("straight") || words.Contains("front"))
            {
                answer = new NavigationCommand { which = "middle" };
                return true;
            }
            return false;
        }

        static List<string> WordsOf(string utterance)
        {
            var words = new List<string>();
            var sb = new System.Text.StringBuilder();
            foreach (char c in utterance.ToLowerInvariant() + " ")
            {
                if (char.IsLetter(c) || c == '\'') sb.Append(c);
                else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Length = 0; }
            }
            return words;
        }

        // "That one", "this one", "that tree", "the one I'm looking at": pointing by looking.
        static bool IsPointing(string utterance)
        {
            var words = new List<string>();
            var sb = new System.Text.StringBuilder();
            foreach (char c in utterance.ToLowerInvariant() + " ")
            {
                if (char.IsLetter(c) || c == '\'') sb.Append(c);
                else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Length = 0; }
            }
            if (words.Count == 0 || words.Count > 6) return false;
            if (words[0] == "that" || words[0] == "this" || words[0] == "there") return words.Count <= 3;
            string text = string.Join(" ", words);
            return text.StartsWith("the one i") || text.StartsWith("the one that i");
        }

        static string Article(string name)
        {
            return ("aeiou".IndexOf(char.ToLowerInvariant(name[0])) >= 0 ? "an " : "a ") + name;
        }

        const float SmallTurnDegrees = 30f; // "turn left a little"

        // "A bit" becomes the user's current bit size in metres; a small turn is 30 degrees.
        void ResolveAmounts(NavigationCommand command)
        {
            foreach (var step in command.steps)
            {
                if (step.amount != AmountType.Small) continue;
                step.magnitude = step.action == ActionType.Move ? Habits.BitMeters : SmallTurnDegrees;
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
                var failed = controller.FailedStep;
                string what = failed != null && failed.HasCondition ? Article(grounding.NameFor(failed.condition)) : "it";
                if (failed != null && failed.action == ActionType.Turn)
                    OnStatus?.Invoke("I turned all the way around and couldn't find " + what + ".");
                else if (failed != null && failed.until == UntilType.See)
                    OnStatus?.Invoke("I went as far as I could and didn't see " + what + ".");
                else
                    OnStatus?.Invoke("I can't see " + what + " that way from here.");
            }
        }
    }
}
