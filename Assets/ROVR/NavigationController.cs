// Assets/ROVR/NavigationController.cs
// The deterministic, engine-native half of the Semantic-Geometric Division of Labor (Section
// 6.2.3): the LLM never touches trajectory math, it only hands off a NavigationCommand. This
// controller is what actually moves the CharacterController, and it's the layer responsible for
// the cybersickness mitigations described in Chapter 2: forward locomotion locked to the horizontal
// plane regardless of head pitch (Section 3.3.1), short eased turns (or snap turns) with a comfort
// vignette (ComfortVignette), and, since Miguel's test session, a short acceleration and deceleration
// instead of starting and stopping dead.
//
// Comfort and safety rules:
//   - Starting, stopping and changing direction ramp the speed (acceleration / deceleration, m/s^2). A
//     move of a set distance slows down so it ends exactly there.
//   - A new command interrupts the running one at once: what was moving or turning eases out (a fraction
//     of a second, still through the walls' collisions), then the new command runs. Only one command
//     ever moves the body. The interrupted one reports Interrupted and is not resumed.
//   - Every spoken stop ("stop", "wait", "freeze", ...) eases to a stop (Halt, at stopDeceleration: gentler
//     still, as a sudden stop is what made people queasy). EmergencyStop stops dead, this frame, only for the
//     cancel button (B), teleports and breaks.
//   - Walls always stop the body at once: speed is never carried into an obstacle, so smoothing can't
//     push the body through anything (the CharacterController sweeps every move).
//   - Forward / back / left / right are fixed when each step starts, from where the head faces then
//     (kept level), so looking around mid-move doesn't bend the path. Left / right are sidesteps
//     (strafing): the view never turns for them.
//   - Gravity is PlayerGravity's job (the same body); "up" is a jump when it's there.
//
// Step commands ("take three steps", "step left", "walk in steps to the door") step the whole way: each step
// is its own stride, quicker in the middle and slower where the foot comes down (never stopping between
// steps), with the view dipping once per step (StepBob), from the first step to the last. Other moves glide.
//
// Going to an object ("go to the table") heads straight for it, stops a safe gap short of it (stopGap, from
// the body's edge) and ends facing it. It never finds its own way round anything: if something is in the way
// (even a small stool), the move stops there as Blocked ("Something is in the way") instead of sliding round
// it, and the user decides how to get past. Only "get me out of this room" plans a route (RoutePlanner).
//
// Every command ends in an outcome (Completed / Halted / Blocked / NotFound / Interrupted) reported
// through OnCommandFinished, so nothing fails silently and a blocked step can't stall a chain.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ROVR
{
    [RequireComponent(typeof(CharacterController))]
    public class NavigationController : MonoBehaviour
    {
        [SerializeField] Transform pov;
        [SerializeField] FOVMetadataGrounding grounding; // needed for "turn until you see X"
        [SerializeField] float moveSpeed = 2f;           // m/s at Normal speed
        [SerializeField] float slowSpeed = 1f;           // m/s at Slow
        [SerializeField] float fastSpeed = 3f;           // m/s at Fast
        [Tooltip("Step commands (\"take 3 steps\") walk at a rhythm, in steps per second at Slow / Normal / Fast; the speed is that times the step length.")]
        // A walking pace: 1.3 steps a second of 1 m is about 1.3 m/s on average, peaking near 1.9 m/s mid-stride.
        [SerializeField] float slowStepsPerSecond = 1f;
        [SerializeField] float stepsPerSecond = 1.3f;
        [SerializeField] float fastStepsPerSecond = 1.7f;

        [Header("Comfort")]
        [Tooltip("m/s² when speeding up. 4 = from standing to 2 m/s in half a second.")]
        public float acceleration = 4f;
        [Tooltip("m/s² when slowing down at the end of a move or when a new command takes over. 5 = from 2 m/s to standing in 0.4 s (40 cm).")]
        public float deceleration = 5f;
        [Tooltip("m/s² when a spoken \"stop\" slows the player down: gentler than the end of a move. 3 = from 2 m/s to standing in 0.67 s (67 cm). Walls still stop at once.")]
        public float stopDeceleration = 3f;
        [Tooltip("Degrees/s² for easing a turn out when it's interrupted.")]
        public float turnDeceleration = 400f;
        [Tooltip("Turn in instant jumps instead of a smooth eased turn (some people find this more comfortable).")]
        public bool snapTurning;
        [Tooltip("With snap turning: the size of each jump while searching (\"turn until you see the door\").")]
        public float snapDegrees = 30f;
        [Tooltip("Seconds a 90-degree turn takes. Turns ease in and out; other angles take proportionally long (but at least shortestTurnSeconds).")]
        [SerializeField] float quarterTurnSeconds = 0.9f;
        [SerializeField] float shortestTurnSeconds = 0.35f;
        [Tooltip("Degrees per second while sweeping round for 'turn until you see X'.")]
        [SerializeField] float searchTurnSpeed = 120f;

        [Header("Going to things")]
        [Tooltip("Metres between the body's edge and the object when a move to it stops.")]
        public float stopGap = 0.5f;
        [SerializeField] float rampLength = 1f;          // m over which speed eases to a stop before stopGap (6.3.4)
        [SerializeField] float minCreepSpeed = 0.15f;    // m/s floor while easing to a stop, so the stop is reached
        [SerializeField] float maxConditionalDistance = 60f; // safety cap so "move until X" can't run forever
        [Tooltip("Turn to face the object on arrival when it's more than this many degrees off.")]
        [SerializeField] float faceOnArrivalDegrees = 10f;
        [SerializeField] LayerMask detectionMask = ~0;

        const int BlockedFramesLimit = 10;   // consecutive frames with (almost) no progress = blocked
        const float StrictProgress = 0.85f;  // heading for an object: less than this share of the move = held back
        const int StrictBlockedFrames = 3;
        const float ProbeLift = 0.1f;        // probe capsule starts this far above the feet, clear of the floor
        const float LookInterval = 0.1f;     // how often "until you see X" looks (a full scan of the view)
        const float DefaultTurnDegrees = 90f; // "turn left" with no angle
        const float FacedDegrees = 3f;       // "turn until you see X" ends with X this close to the middle of the view
        const float SnapPause = 0.25f;       // s between snaps while searching

        public event Action<NavigationCommand, CommandOutcome> OnCommandFinished;

        public bool IsBusy => activeRoutine != null;
        public bool IsTurning { get; private set; } // for the comfort vignette
        public bool IsMoving => speedNow > 0.05f;
        public float SpeedNow => speedNow;
        // Walking forward or back (not a sidestep or a turn), including easing to a stop: for the movement vignette.
        public bool IsMovingForwardBack => IsMoving && (walking == DirectionType.Forward || walking == DirectionType.Back);

        // Walking speed for moves without their own; changing it takes effect mid-move.
        public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
        public float SpeedOf(SpeedLevel level) { return level == SpeedLevel.Slow ? slowSpeed : level == SpeedLevel.Fast ? fastSpeed : moveSpeed; }
        public float StepsPerSecondOf(SpeedLevel level) { return level == SpeedLevel.Slow ? slowStepsPerSecond : level == SpeedLevel.Fast ? fastStepsPerSecond : stepsPerSecond; }
        // The step that ended a command early (blocked, or its target not found), for the message.
        public NavigationStep FailedStep { get; private set; }

        CharacterController controller;
        PlayerGravity gravity;
        Coroutine activeRoutine, settleRoutine;
        NavigationCommand current;
        CommandOutcome stepOutcome;

        // What the body is doing right now, carried across commands so a new one can ease out of it.
        float speedNow;          // m/s along moveDir
        Vector3 moveDir;
        DirectionType walking = DirectionType.None; // the way the current (or easing-out) walk goes
        float yawRate;           // degrees/s, positive = right

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (pov == null) pov = Camera.main != null ? Camera.main.transform : transform;
            if (grounding == null) grounding = GetComponent<FOVMetadataGrounding>();
            gravity = GetComponent<PlayerGravity>();
            if (gravity == null) gravity = gameObject.AddComponent<PlayerGravity>();

            // Gravity holds the body on the floor, so a small step up (a rug, a threshold) is fine: the
            // body comes back down after it. Higher than this (a bed, a rock) blocks.
            controller.stepOffset = 0.2f;
            // Sub-millimetre moves are legitimate while easing to a stop; don't let Unity drop them.
            controller.minMoveDistance = 0f;
        }

        public void Execute(NavigationCommand command)
        {
            // A new command supersedes whatever was running (the old one reports Interrupted). Any speed
            // left over is eased out at the start of the new command, inside its own routine.
            Stop(CommandOutcome.Interrupted);

            if (command.steps.Length == 0)
            {
                OnCommandFinished?.Invoke(command, CommandOutcome.Completed);
                return;
            }

            // For "go back": where this command starts, and which way the user faces.
            path.Clear();
            startYaw = HeadYaw();
            current = command;
            activeRoutine = StartCoroutine(RunSteps(command));
        }

        // "Stop": the running command reports Halted, and the body eases to a stop (a fraction of a second).
        public void Halt()
        {
            bool wasRunning = activeRoutine != null;
            Stop(CommandOutcome.Halted);
            if (!wasRunning && settleRoutine != null) return; // already easing out
            if (speedNow > 0.01f || Mathf.Abs(yawRate) > 1f) settleRoutine = StartCoroutine(SettleAlone(stopDeceleration));
        }

        // Stops dead, this frame: the cancel button, "freeze", teleports, breaks.
        public void EmergencyStop()
        {
            Stop(CommandOutcome.Halted);
            speedNow = 0f;
            yawRate = 0f;
            IsTurning = false;
            walking = DirectionType.None;
        }

        void Stop(CommandOutcome outcome)
        {
            if (settleRoutine != null) { StopCoroutine(settleRoutine); settleRoutine = null; }
            if (activeRoutine == null) return;

            StopCoroutine(activeRoutine);
            activeRoutine = null;
            IsTurning = false;

            var finished = current;
            current = null;
            OnCommandFinished?.Invoke(finished, outcome);
        }

        // ---------------------------------------------------------------- "go back"

        // Where each move of the last command started (it records as it goes, so a command halted halfway
        // has only the moves it actually made), and which way the user faced before it.
        readonly List<Vector3> path = new List<Vector3>();
        float startYaw;

        // Between participants: there's nothing to go back to.
        public void ForgetPath() { path.Clear(); }

        // The command that undoes the last one, from where the user is now: for each move, last first, turn
        // to face where it started and walk there; then turn to face the way the user faced before it. A
        // single move comes out as "turn round, walk back, turn round"; a lone turn as the turn back.
        // Null if there's nothing to undo.
        public NavigationCommand PlanGoBack()
        {
            var steps = new List<NavigationStep>();
            Vector3 at = transform.position;
            float yaw = HeadYaw();
            bool any = false;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                Vector3 to = path[i] - at;
                to.y = 0f;
                if (to.magnitude < 0.05f) continue;
                float face = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                AddTurn(steps, Mathf.DeltaAngle(yaw, face));
                steps.Add(new NavigationStep { action = ActionType.Move, direction = DirectionType.Forward, amount = AmountType.Exact, magnitude = to.magnitude, condition = string.Empty });
                yaw = face;
                at = path[i];
                any = true;
            }
            float back = Mathf.DeltaAngle(yaw, startYaw);
            if (!any && Mathf.Abs(back) < 2f) return null;
            AddTurn(steps, back);
            return steps.Count == 0 ? null : new NavigationCommand { steps = steps.ToArray(), summary = "Going back" };
        }

        static void AddTurn(List<NavigationStep> steps, float degrees)
        {
            if (Mathf.Abs(degrees) < 2f) return;
            steps.Add(new NavigationStep { action = ActionType.Turn, direction = degrees < 0f ? DirectionType.Left : DirectionType.Right, amount = AmountType.Exact, magnitude = Mathf.Abs(degrees), condition = string.Empty });
        }

        // ---------------------------------------------------------------- running a command

        IEnumerator RunSteps(NavigationCommand command)
        {
            var outcome = CommandOutcome.Completed;
            FailedStep = null;

            // Ease out of whatever the last command was doing, so changing commands never jolts.
            yield return Settle();

            foreach (var step in command.steps)
            {
                stepOutcome = CommandOutcome.Completed;
                if (step.action == ActionType.Move || step.action == ActionType.GoTo) path.Add(transform.position);
                yield return RunStep(step);

                if (stepOutcome != CommandOutcome.Completed)
                {
                    outcome = stepOutcome; // a blocked / halted / not-found step ends the whole chain
                    FailedStep = step;
                    break;
                }
            }

            // Whatever ended the command, nothing keeps moving after it.
            yield return Settle();

            activeRoutine = null;
            current = null;
            OnCommandFinished?.Invoke(command, outcome);
        }

        IEnumerator RunStep(NavigationStep step)
        {
            switch (step.action)
            {
                case ActionType.Move: yield return RunMove(step); break;
                case ActionType.Turn: yield return RunTurn(step); break;
                case ActionType.GoTo: yield return RunGoTo(step); break;
                case ActionType.Stop: stepOutcome = CommandOutcome.Halted; break;
            }
        }

        // Eases the leftover speed and turn out (through the walls' collisions), then returns. rate: m/s²,
        // `deceleration` unless given (a spoken stop is gentler).
        IEnumerator Settle(float rate = -1f)
        {
            if (rate <= 0f) rate = deceleration;
            while (speedNow > 0.01f || Mathf.Abs(yawRate) > 1f)
            {
                float dt = Time.deltaTime;
                if (speedNow > 0.01f)
                {
                    speedNow = Mathf.MoveTowards(speedNow, 0f, rate * dt);
                    Vector3 before = transform.position;
                    controller.Move(moveDir * speedNow * dt);
                    if (Vector3.Dot(transform.position - before, moveDir) < speedNow * dt * 0.25f) speedNow = 0f; // touching something: stop now
                }
                else speedNow = 0f;

                if (Mathf.Abs(yawRate) > 1f)
                {
                    IsTurning = true;
                    yawRate = Mathf.MoveTowards(yawRate, 0f, turnDeceleration * dt);
                    Rotate(yawRate * dt);
                }
                else yawRate = 0f;
                yield return null;
            }
            speedNow = 0f;
            yawRate = 0f;
            IsTurning = false;
            walking = DirectionType.None;
        }

        IEnumerator SettleAlone(float rate)
        {
            yield return Settle(rate);
            settleRoutine = null;
        }

        // ---------------------------------------------------------------- movement

        IEnumerator RunMove(NavigationStep step)
        {
            // Up is a jump (gravity brings the body back down); there's nowhere to go down to.
            if (step.direction == DirectionType.Up)
            {
                if (!gravity.Jump()) { stepOutcome = CommandOutcome.Blocked; yield break; }
                float until = Time.time + 2f;
                while (Time.time < until && (gravity.JustJumped || !gravity.IsGrounded)) yield return null;
                yield break;
            }
            if (step.direction == DirectionType.Down) { stepOutcome = CommandOutcome.Blocked; yield break; }

            Vector3 dir;
            switch (step.direction)
            {
                case DirectionType.Forward: dir = PlanarForward(); break;
                case DirectionType.Back: dir = -PlanarForward(); break;
                case DirectionType.Left: dir = -PlanarRight(); break;   // a sidestep: the view doesn't turn
                case DirectionType.Right: dir = PlanarRight(); break;
                default: yield break;
            }
            walking = step.direction;

            bool horizontal = step.direction == DirectionType.Forward || step.direction == DirectionType.Left || step.direction == DirectionType.Right;

            // Step commands ("take 3 steps", "walk in steps to the door"): a number of steps of the user's
            // step length, at a walking rhythm, with the view dipping once per step (StepBob).
            bool inSteps = step.InSteps && step.stepLength > 0f;
            StepBob bob = inSteps ? GetComponent<StepBob>() : null;

            // Distance to cover: an explicit magnitude ("move 10 meters", "take 3 steps"), a condition
            // ("until you reach the door"), or neither (continuous until halted or blocked).
            float target = !step.HasMagnitude ? float.PositiveInfinity : inSteps ? step.magnitude * step.stepLength : step.magnitude;
            if (step.HasCondition) target = Mathf.Min(target, maxConditionalDistance);

            float travelled = 0f;
            int blockedFrames = 0;

            // In steps: stride by stride the whole way (WalkInSteps), never a glide.
            if (inSteps)
            {
                Transform toward = null;
                if (step.HasCondition && step.until == UntilType.Reach && grounding != null && horizontal)
                {
                    toward = grounding.ObjectWithId(step.condition);
                    if (toward == null && !grounding.IsCollapsedName(step.condition))
                    {
                        toward = NearestThatWay(step.condition, step.direction);
                        if (toward == null) { stepOutcome = CommandOutcome.NotFound; yield break; }
                    }
                }
                yield return WalkInSteps(step, dir, toward, bob);
                if (toward != null && stepOutcome == CommandOutcome.Completed) yield return FaceTowards(FOVMetadataGrounding.CenterOf(toward));
                yield break;
            }

            // "Move forward until you see the door": go that way, and stop the moment it's in view.
            if (step.HasCondition && step.until == UntilType.See && grounding != null)
            {
                float nextLook = 0f;
                while (travelled < target)
                {
                    if (Time.time >= nextLook)
                    {
                        nextLook = Time.time + LookInterval;
                        if (grounding.IsVisible(step.condition)) yield break;
                    }
                    if (!Step(dir, WalkingSpeed(step, inSteps), ref travelled, target, ref blockedFrames)) yield break;
                    if (bob != null) bob.Walk(travelled / step.stepLength);
                    yield return null;
                }
                stepOutcome = CommandOutcome.NotFound;
                yield break;
            }

            // Heading for one particular object ("plant-4"). A later step of a chain ("...then go to the
            // door") names a kind of object: take the nearest one that way.
            Transform goal = null;
            if (step.HasCondition && grounding != null && horizontal)
            {
                goal = grounding.ObjectWithId(step.condition);
                if (goal == null && !grounding.IsCollapsedName(step.condition))
                {
                    goal = NearestThatWay(step.condition, step.direction);
                    if (goal == null) { stepOutcome = CommandOutcome.NotFound; yield break; }
                }
            }

            while (travelled < target)
            {
                float speed = WalkingSpeed(step, inSteps);
                if (goal != null)
                {
                    Vector3 to = Vector3.ProjectOnPlane(FOVMetadataGrounding.CenterOf(goal) - transform.position, Vector3.up);
                    if (to.sqrMagnitude > 0.01f) dir = to.normalized;
                }

                if (step.HasCondition)
                {
                    // Sense-made conditional (6.3.4): ease off and stop stopGap short of the target.
                    float gap = GapToTarget(dir, step.condition, stopGap + rampLength + speed * Time.deltaTime + 0.1f);
                    if (gap <= stopGap) break;
                    if (gap < stopGap + rampLength)
                        speed = Mathf.Max(minCreepSpeed, speed * (gap - stopGap) / rampLength);
                }

                if (!Step(dir, speed, ref travelled, target, ref blockedFrames, goal != null)) yield break;
                if (bob != null) bob.Walk(travelled / step.stepLength);
                yield return null;
            }

            // At the object: end facing it.
            if (goal != null) yield return FaceTowards(FOVMetadataGrounding.CenterOf(goal));
        }

        // Speed at the moment a foot comes down, as a share of the step's average: the body slows there but never
        // stops between steps (a stop-start every step would be its own jolt).
        const float FootfallSpeed = 0.4f;

        // A step command, stride by stride. Each step covers one step length in 1 / (steps per second) seconds;
        // its speed runs from the speed at the last footfall, up through the middle of the stride, down to the
        // next footfall (0 before the first step and after the last, so it starts and ends at rest). The speed is
        // continuous from step to step, so it never jerks. A counted command ("take 2.5 steps") ends exactly at
        // its distance; one with a target ends a safe gap short of it, or as soon as it's seen ("take steps until
        // you see the door"); walls end it (Blocked) as for any move.
        IEnumerator WalkInSteps(NavigationStep step, Vector3 dir, Transform toward, StepBob bob)
        {
            float length = step.stepLength;
            bool counted = step.HasMagnitude;
            float total = counted ? step.magnitude * length : maxConditionalDistance;
            int strides = Mathf.Max(1, Mathf.CeilToInt(total / length - 0.001f));
            bool lookFor = step.HasCondition && step.until == UntilType.See && grounding != null;
            bool reach = step.HasCondition && step.until == UntilType.Reach;
            float startSpeed = 0f, nextLook = 0f;
            int blockedFrames = 0;

            for (int k = 0; k < strides; k++)
            {
                float thisLength = counted && k == strides - 1 ? total - k * length : length;
                float average = StepsPerSecondOf(step.speed ?? Speed) * length; // read per step: "faster" mid-walk takes effect
                bool last = counted && k == strides - 1;
                float endSpeed = last ? 0f : FootfallSpeed * average;
                float seconds = Mathf.Max(0.05f, thisLength / average);
                // speed(u) = startSpeed + (endSpeed - startSpeed) u + bump sin(pi u), averaging `average`.
                float bump = (average - (startSpeed + endSpeed) / 2f) * Mathf.PI / 2f;
                float t = 0f, planned = 0f, done = 0f;

                while (t < seconds)
                {
                    t = Mathf.Min(seconds, t + Time.deltaTime);
                    float u = t / seconds;
                    float at = seconds * (startSpeed * u + (endSpeed - startSpeed) * u * u / 2f + bump * (1f - Mathf.Cos(Mathf.PI * u)) / Mathf.PI);
                    float delta = Mathf.Max(0f, at - planned);
                    planned = at;

                    if (toward != null)
                    {
                        Vector3 to = Vector3.ProjectOnPlane(FOVMetadataGrounding.CenterOf(toward) - transform.position, Vector3.up);
                        if (to.sqrMagnitude > 0.01f) dir = to.normalized;
                    }
                    if (lookFor && Time.time >= nextLook)
                    {
                        nextLook = Time.time + LookInterval;
                        if (grounding.IsVisible(step.condition)) { yield return Settle(stopDeceleration); yield break; }
                    }
                    if (reach && GapToTarget(dir, step.condition, stopGap + delta + 0.1f) <= stopGap + delta)
                    {
                        yield return Settle(stopDeceleration); // there: ease out of the stride
                        yield break;
                    }

                    Vector3 before = transform.position;
                    controller.Move(dir * delta);
                    Vector3 actual = transform.position - before;
                    actual.y = 0f;
                    float moved = Mathf.Max(0f, Vector3.Dot(actual, dir));
                    done += moved;
                    moveDir = dir;
                    speedNow = startSpeed + (endSpeed - startSpeed) * u + bump * Mathf.Sin(Mathf.PI * u);

                    bool strict = toward != null; // heading for an object: no edging round things
                    if (delta > 0.0005f && moved < delta * (strict ? StrictProgress : 0.25f))
                    {
                        speedNow = 0f;
                        if (++blockedFrames >= (strict ? StrictBlockedFrames : BlockedFramesLimit)) { stepOutcome = CommandOutcome.Blocked; yield break; }
                    }
                    else if (delta > 0.0005f) blockedFrames = 0;

                    if (bob != null) bob.Walk(k + Mathf.Clamp01(done / Mathf.Max(0.01f, thisLength)) * (thisLength / length));
                    yield return null;
                }
                startSpeed = endSpeed;
            }
            speedNow = 0f;
            if (lookFor) stepOutcome = CommandOutcome.NotFound; // took every step it could and never saw it
        }

        // Turns to face the next corner of a route, then walks there, slowing down to arrive.
        IEnumerator WalkLeg(Vector3 corner, NavigationStep step)
        {
            Vector3 to = Vector3.ProjectOnPlane(corner - transform.position, Vector3.up);
            if (to.magnitude < 0.1f) yield break;
            yield return Settle();
            yield return FaceTowards(corner);
            to = Vector3.ProjectOnPlane(corner - transform.position, Vector3.up);
            Vector3 dir = to.normalized;
            walking = DirectionType.Forward;
            float target = to.magnitude, travelled = 0f;
            int blockedFrames = 0;
            while (travelled < target)
            {
                if (!Step(dir, WalkingSpeed(step, false), ref travelled, target, ref blockedFrames)) yield break;
                yield return null;
            }
        }

        // An engine-made trip to a point ("get me out of this room"): the route there, then optionally
        // facing a point.
        IEnumerator RunGoTo(NavigationStep step)
        {
            if (!step.point.HasValue) yield break;
            var corners = new List<Vector3>();
            if (!RoutePlanner.TryRoute(transform.position, step.point.Value, controller.radius, corners))
            {
                corners.Clear();
                corners.Add(transform.position);
                corners.Add(step.point.Value);
            }
            for (int i = 1; i < corners.Count; i++)
            {
                yield return WalkLeg(corners[i], step);
                if (stepOutcome != CommandOutcome.Completed) yield break;
            }
            if (step.faceTowards.HasValue)
            {
                yield return Settle();
                yield return FaceTowards(step.faceTowards.Value);
            }
        }

        // m/s for a walking move, read every frame so "faster" / "slower" take effect mid-move. In steps:
        // the rhythm (steps per second) times the step length.
        float WalkingSpeed(NavigationStep step, bool inSteps)
        {
            var level = step.speed ?? Speed;
            return inSteps ? StepsPerSecondOf(level) * step.stepLength : SpeedOf(level);
        }

        // One frame of movement, with the speed ramped: up at `acceleration`, and down so that a move of a
        // set distance comes to rest right at its end. Counts what actually happened, not what was asked
        // for, so walls end the step (speed drops to zero at once: never carried into an obstacle), and
        // after BlockedFramesLimit frames of no progress the step ends as Blocked.
        // strict (heading for an object): anything that holds the body back or pushes it sideways (sliding
        // round a stool) counts as blocked straight away, so it never edges its own way round.
        bool Step(Vector3 dir, float wanted, ref float travelled, float target, ref int blockedFrames, bool strict = false)
        {
            // Within half a millimetre is there. Far from the origin (the Plain is at x = 250) a smaller
            // move is lost to float precision, and the step used to end as Blocked on arrival.
            float remaining = target - travelled;
            if (remaining < 0.0005f)
            {
                travelled = target;
                return true;
            }

            float dt = Time.deltaTime;
            // A change of direction within a command (the next leg, steering to a target) keeps only the
            // part of the speed that still points the new way.
            if (speedNow > 0f && moveDir != dir) speedNow *= Mathf.Max(0f, Vector3.Dot(moveDir, dir));
            moveDir = dir;

            float desired = wanted;
            if (!float.IsInfinity(target)) desired = Mathf.Min(desired, Mathf.Max(minCreepSpeed, Mathf.Sqrt(2f * deceleration * remaining)));
            speedNow = Mathf.MoveTowards(speedNow, desired, (desired > speedNow ? acceleration : deceleration) * dt);
            if (speedNow < minCreepSpeed && desired >= minCreepSpeed) speedNow = Mathf.Max(speedNow, Mathf.Min(desired, minCreepSpeed));

            float delta = Mathf.Min(speedNow * dt, remaining);
            Vector3 before = transform.position;
            controller.Move(dir * delta);

            Vector3 actual = transform.position - before;
            actual.y = 0f;
            float moved = Vector3.Dot(actual, dir);
            travelled += Mathf.Max(0f, moved);

            bool held = strict ? delta > 0.001f && moved < delta * StrictProgress : moved < delta * 0.25f;
            if (held)
            {
                speedNow = 0f; // touching something: no speed builds up against it
                if (++blockedFrames >= (strict ? StrictBlockedFrames : BlockedFramesLimit))
                {
                    stepOutcome = CommandOutcome.Blocked;
                    return false;
                }
            }
            else
            {
                blockedFrames = 0;
            }
            return true;
        }

        // The nearest object with this name in view that lies the way of the move.
        Transform NearestThatWay(string name, DirectionType direction)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var o in grounding.ScanFieldOfView())
            {
                if (!grounding.Matches(o, name)) continue;
                bool thatWay = direction == DirectionType.Forward ? Mathf.Abs(o.angleDegrees) <= 25f
                    : direction == DirectionType.Left ? o.angleDegrees < -5f : o.angleDegrees > 5f;
                if (thatWay && o.distanceMeters < bestDistance) { best = o.target; bestDistance = o.distanceMeters; }
            }
            return best;
        }

        // Distance the body can still travel along `dir` before touching the target (anything with that
        // name, e.g. "tree", or one particular object, e.g. "tree-3"), or infinity if something solid (or
        // nothing) is in the way first. Measured from the body's surface, so it doesn't depend on the
        // avatar's radius.
        float GapToTarget(Vector3 dir, string target, float castDistance)
        {
            Vector3 c = transform.TransformPoint(controller.center);
            float half = Mathf.Max(controller.height * 0.5f, controller.radius) - controller.radius;
            Vector3 top = c + Vector3.up * half;
            Vector3 bottom = c - Vector3.up * Mathf.Max(0f, half - ProbeLift);

            var hits = Physics.CapsuleCastAll(top, bottom, controller.radius, dir, castDistance, detectionMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue; // ourselves (and the minimap, hands)
                if (grounding != null ? grounding.IsTarget(hit.collider, target) : hit.collider.CompareTag(target)) return hit.distance;
                if (!hit.collider.isTrigger) return float.PositiveInfinity; // something solid in front of the target
            }
            return float.PositiveInfinity;
        }

        // Forward on the horizontal plane (3.3.1). Looking straight up/down, fall back to the
        // head's up vector so "forward" still means the way the user is facing.
        Vector3 PlanarForward()
        {
            Vector3 f = Vector3.ProjectOnPlane(pov.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f)
            {
                Vector3 u = pov.forward.y > 0f ? -pov.up : pov.up;
                f = Vector3.ProjectOnPlane(u, Vector3.up);
            }
            return f.normalized;
        }

        Vector3 PlanarRight()
        {
            return Vector3.Cross(Vector3.up, PlanarForward()).normalized;
        }

        // ---------------------------------------------------------------- turning

        IEnumerator RunTurn(NavigationStep step)
        {
            float sign = step.direction == DirectionType.Left ? -1f : 1f;
            IsTurning = true;

            if (step.HasCondition)
            {
                // "Turn until you see X" / "face X" (6.4.2). If X is in view, turn the short way to face
                // it. Otherwise sweep round the way asked until X shows up (at most a full circle), then
                // ease onto it, so it ends up straight ahead.
                if (TryAngleTo(step.condition, out float angle))
                {
                    if (Mathf.Abs(angle) > FacedDegrees) yield return TurnBy(angle);
                }
                else if (snapTurning)
                {
                    float swept = 0f;
                    bool found = false;
                    while (swept < 360f && !found)
                    {
                        float jump = Mathf.Min(snapDegrees, 360f - swept);
                        Rotate(sign * jump);
                        swept += jump;
                        yield return new WaitForSeconds(SnapPause);
                        found = TryAngleTo(step.condition, out angle);
                    }
                    if (!found) { IsTurning = false; stepOutcome = CommandOutcome.NotFound; yield break; }
                    if (Mathf.Abs(angle) > FacedDegrees) Rotate(angle);
                }
                else
                {
                    float swept = 0f, speed = Mathf.Abs(yawRate), nextLook = 0f;
                    bool found = false;
                    while (swept < 360f && !found)
                    {
                        speed = Mathf.MoveTowards(speed, searchTurnSpeed, searchTurnSpeed * 4f * Time.deltaTime); // eases in over 0.25 s
                        float delta = Mathf.Min(speed * Time.deltaTime, 360f - swept);
                        Rotate(sign * delta);
                        yawRate = sign * speed;
                        swept += delta;
                        if (Time.time >= nextLook)
                        {
                            nextLook = Time.time + LookInterval;
                            found = TryAngleTo(step.condition, out angle);
                        }
                        yield return null;
                    }
                    if (!found)
                    {
                        IsTurning = false;
                        stepOutcome = CommandOutcome.NotFound;
                        yield break;
                    }

                    // Slow down onto it from the sweep speed, rather than stopping dead and starting again.
                    float aim = HeadYaw() + angle;
                    while (true)
                    {
                        float remaining = Mathf.DeltaAngle(HeadYaw(), aim);
                        if (Mathf.Abs(remaining) <= FacedDegrees * 0.5f) break;
                        speed = Mathf.Min(searchTurnSpeed, Mathf.Max(20f, Mathf.Abs(remaining) * 4f));
                        float d = Mathf.Sign(remaining) * Mathf.Min(Mathf.Abs(remaining), speed * Time.deltaTime);
                        Rotate(d);
                        yawRate = d / Mathf.Max(Time.deltaTime, 1e-4f);
                        yield return null;
                    }
                    yawRate = 0f;
                }
                IsTurning = false;
                yield break;
            }

            // Any angle ("turn 30 degrees right"); "turn left" is 90, "turn around" 180.
            float degrees = step.HasMagnitude ? step.magnitude : DefaultTurnDegrees;
            yield return TurnBy(sign * degrees);
            IsTurning = false;
        }

        // Turns to face a point, if it's more than faceOnArrivalDegrees off.
        IEnumerator FaceTowards(Vector3 point)
        {
            Vector3 to = Vector3.ProjectOnPlane(point - pov.position, Vector3.up);
            if (to.sqrMagnitude < 0.01f) yield break;
            float angle = Vector3.SignedAngle(PlanarForward(), to, Vector3.up);
            if (Mathf.Abs(angle) <= faceOnArrivalDegrees) yield break;
            yield return Settle();
            IsTurning = true;
            yield return TurnBy(angle);
            IsTurning = false;
        }

        // A turn of an exact angle (positive = right): eased (slow, faster, slow), or one snap.
        IEnumerator TurnBy(float degrees)
        {
            if (snapTurning)
            {
                IsTurning = true;          // the vignette blinks round the snap
                yield return null;
                Rotate(degrees);
                yawRate = 0f;
                yield return new WaitForSeconds(0.1f);
                yield break;
            }

            float seconds = Mathf.Max(shortestTurnSeconds, quarterTurnSeconds * Mathf.Abs(degrees) / 90f);
            float done = 0f, t = 0f;
            while (t < seconds)
            {
                t = Mathf.Min(seconds, t + Time.deltaTime);
                float target = degrees * Mathf.SmoothStep(0f, 1f, t / seconds);
                float d = target - done;
                Rotate(d);
                yawRate = d / Mathf.Max(Time.deltaTime, 1e-4f);
                done = target;
                yield return null;
            }
            yawRate = 0f;
        }

        // Horizontal angle from where the user faces to the middle of the nearest visible X; false if
        // no X is in view.
        bool TryAngleTo(string target, out float angle)
        {
            angle = 0f;
            if (grounding == null) return false;
            if (!grounding.TryFindVisible(grounding.ScanFieldOfView(), target, out GroundedObject o)) return false;
            Vector3 to = Vector3.ProjectOnPlane(FOVMetadataGrounding.CenterOf(o.target) - pov.position, Vector3.up);
            angle = Vector3.SignedAngle(PlanarForward(), to, Vector3.up);
            return true;
        }

        float HeadYaw()
        {
            Vector3 f = PlanarForward();
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        // Yaw about the user's head, not the rig origin, so the view stays put in the room.
        void Rotate(float degrees)
        {
            Vector3 pivot = pov != null ? pov.position : transform.position;
            transform.RotateAround(pivot, Vector3.up, degrees);
        }
    }
}
