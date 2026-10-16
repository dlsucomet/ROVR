// Assets/ROVR/NavigationController.cs
// The deterministic, engine-native half of the Semantic-Geometric Division of Labor (Section
// 6.2.3): the LLM never touches trajectory math, it only hands off a NavigationCommand. This
// controller is what actually moves the CharacterController, and it's the layer responsible for
// the cybersickness mitigations described in Chapter 2 — fixed velocity (no acceleration ramp up),
// short eased turns of any angle with a comfort vignette (ComfortVignette; these replaced instant
// 90-degree snap-turns after QA feedback found the snaps jarring), and forward locomotion locked to
// the horizontal plane regardless of head pitch (Section 3.3.1).
//
// Every step ends in an outcome (Completed / Halted / Blocked / NotFound) reported through
// OnCommandFinished, so nothing fails silently and a blocked step can't stall a chain.
using System;
using System.Collections;
using UnityEngine;

namespace ROVR
{
    [RequireComponent(typeof(CharacterController))]
    public class NavigationController : MonoBehaviour
    {
        [SerializeField] Transform pov;
        [SerializeField] FOVMetadataGrounding grounding; // needed for "turn until you see X"
        [SerializeField] float moveSpeed = 2f;           // m/s at Normal speed, fixed cap — no acceleration curve
        [SerializeField] float slowSpeed = 1f;           // m/s at Slow
        [SerializeField] float fastSpeed = 3f;           // m/s at Fast
        [SerializeField] float verticalSpeed = 1.5f;     // m/s for explicit up/down commands
        [Tooltip("Seconds a 90-degree turn takes. Turns ease in and out; other angles take proportionally long (but at least shortestTurnSeconds).")]
        [SerializeField] float quarterTurnSeconds = 0.9f;
        [SerializeField] float shortestTurnSeconds = 0.35f;
        [Tooltip("Degrees per second while sweeping round for 'turn until you see X'.")]
        [SerializeField] float searchTurnSpeed = 120f;
        [SerializeField] float stopGap = 0.5f;           // m between the body and the target when a conditional move stops (6.3.4)
        [SerializeField] float rampLength = 1f;          // m over which speed eases to a stop before stopGap (6.3.4)
        [SerializeField] float minCreepSpeed = 0.15f;    // m/s floor during the ramp so the stop is reached
        [SerializeField] float maxConditionalDistance = 60f; // safety cap so "move until X" can't run forever
        [SerializeField] LayerMask detectionMask = ~0;

        const int BlockedFramesLimit = 10;   // consecutive frames with (almost) no progress = blocked
        const float ProbeLift = 0.1f;        // probe capsule starts this far above the feet, clear of the floor
        const float LookInterval = 0.1f;     // how often "until you see X" looks (a full scan of the view)
        const float DefaultTurnDegrees = 90f; // "turn left" with no angle
        const float FacedDegrees = 3f;       // "turn until you see X" ends with X this close to the middle of the view

        public event Action<NavigationCommand, CommandOutcome> OnCommandFinished;

        public bool IsBusy => activeRoutine != null;
        public bool IsTurning { get; private set; } // for the comfort vignette

        // Walking speed for moves without their own; changing it takes effect mid-move.
        public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
        public float SpeedOf(SpeedLevel level) { return level == SpeedLevel.Slow ? slowSpeed : level == SpeedLevel.Fast ? fastSpeed : moveSpeed; }
        // The step that ended a command early (blocked, or its target not found), for the message.
        public NavigationStep FailedStep { get; private set; }

        CharacterController controller;
        Coroutine activeRoutine;
        NavigationCommand current;
        CommandOutcome stepOutcome;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (pov == null) pov = Camera.main != null ? Camera.main.transform : transform;
            if (grounding == null) grounding = GetComponent<FOVMetadataGrounding>();

            // The worlds are flat and there's no gravity here, so a step-up would leave the avatar
            // hovering on top of whatever it hopped onto. Walls, props and the tree just block.
            controller.stepOffset = 0f;
            // Sub-millimetre moves are legitimate while easing to a stop; don't let Unity drop them.
            controller.minMoveDistance = 0f;
        }

        public void Execute(NavigationCommand command)
        {
            Halt(); // a new command supersedes whatever was running (the old one reports Halted)

            if (command.steps.Length == 0)
            {
                OnCommandFinished?.Invoke(command, CommandOutcome.Completed);
                return;
            }

            current = command;
            activeRoutine = StartCoroutine(RunSteps(command));
        }

        // Called by the Interrupt Module (bypasses the LLM) and by Execute() before a new command.
        public void Halt()
        {
            if (activeRoutine == null) return;

            StopCoroutine(activeRoutine);
            activeRoutine = null;
            IsTurning = false;

            var finished = current;
            current = null;
            OnCommandFinished?.Invoke(finished, CommandOutcome.Halted);
        }

        IEnumerator RunSteps(NavigationCommand command)
        {
            var outcome = CommandOutcome.Completed;
            FailedStep = null;

            foreach (var step in command.steps)
            {
                stepOutcome = CommandOutcome.Completed;
                yield return RunStep(step);

                if (stepOutcome != CommandOutcome.Completed)
                {
                    outcome = stepOutcome; // a blocked / halted / not-found step ends the whole chain
                    FailedStep = step;
                    break;
                }
            }

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
                case ActionType.Stop: stepOutcome = CommandOutcome.Halted; break;
            }
        }

        // ---------------------------------------------------------------- movement

        IEnumerator RunMove(NavigationStep step)
        {
            Vector3 dir;
            float speed;
            switch (step.direction)
            {
                case DirectionType.Forward: dir = PlanarForward(); speed = moveSpeed; break;
                case DirectionType.Back: dir = -PlanarForward(); speed = moveSpeed; break;
                case DirectionType.Left: dir = -PlanarRight(); speed = moveSpeed; break;
                case DirectionType.Right: dir = PlanarRight(); speed = moveSpeed; break;
                case DirectionType.Up: dir = Vector3.up; speed = verticalSpeed; break;
                case DirectionType.Down: dir = Vector3.down; speed = verticalSpeed; break;
                default: yield break;
            }

            // Distance to cover: an explicit magnitude ("move 10 meters"), a condition
            // ("until you reach the door"), or neither (continuous until halted or blocked).
            float target = step.HasMagnitude ? step.magnitude : float.PositiveInfinity;
            if (step.HasCondition) target = Mathf.Min(target, maxConditionalDistance);

            float travelled = 0f;
            int blockedFrames = 0;
            bool horizontal = step.direction == DirectionType.Forward || step.direction == DirectionType.Left || step.direction == DirectionType.Right;
            bool walking = horizontal || step.direction == DirectionType.Back;

            // "Move forward until you see the door": go that way, and stop the moment it's in view.
            if (step.HasCondition && step.until == UntilType.See && grounding != null)
            {
                float nextLook = 0f;
                while (travelled < target)
                {
                    if (walking) speed = SpeedOf(step.speed ?? Speed);
                    if (Time.time >= nextLook)
                    {
                        nextLook = Time.time + LookInterval;
                        if (grounding.IsVisible(step.condition)) yield break;
                    }
                    if (!Step(dir, speed, ref travelled, target, ref blockedFrames)) yield break;
                    yield return null;
                }
                stepOutcome = CommandOutcome.NotFound;
                yield break;
            }

            // Heading for one particular object ("plant-4"): walk straight toward it, even if it's a
            // little to one side of where the user faces. The view doesn't turn. A later step of a
            // chain ("...then go to the door") names a kind of object: take the nearest one that way.
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
                if (walking) speed = SpeedOf(step.speed ?? Speed);
                float thisSpeed = speed;
                if (goal != null)
                {
                    Vector3 to = Vector3.ProjectOnPlane(FOVMetadataGrounding.CenterOf(goal) - transform.position, Vector3.up);
                    if (to.sqrMagnitude > 0.01f) dir = to.normalized;
                }

                if (step.HasCondition)
                {
                    // Sense-made conditional (6.3.4): ease off and stop stopGap short of the target.
                    float gap = GapToTarget(dir, step.condition, stopGap + rampLength + speed * Time.deltaTime + 0.1f);
                    if (gap <= stopGap) yield break;
                    if (gap < stopGap + rampLength)
                        thisSpeed = Mathf.Max(minCreepSpeed, speed * (gap - stopGap) / rampLength);
                }

                if (!Step(dir, thisSpeed, ref travelled, target, ref blockedFrames)) yield break;
                yield return null;
            }
        }

        // One frame of movement. Counts what actually happened, not what was asked for, so walls end
        // the step: false (with stepOutcome Blocked) after BlockedFramesLimit frames of no progress.
        bool Step(Vector3 dir, float speed, ref float travelled, float target, ref int blockedFrames)
        {
            float delta = Mathf.Min(speed * Time.deltaTime, target - travelled);
            Vector3 before = transform.position;
            controller.Move(dir * delta);

            float moved = Vector3.Dot(transform.position - before, dir);
            travelled += Mathf.Max(0f, moved);

            if (moved < delta * 0.25f)
            {
                if (++blockedFrames >= BlockedFramesLimit)
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
                if (hit.collider.transform.IsChildOf(transform)) continue; // ourselves
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
                else
                {
                    float swept = 0f, speed = 0f, nextLook = 0f;
                    bool found = false;
                    while (swept < 360f && !found)
                    {
                        speed = Mathf.MoveTowards(speed, searchTurnSpeed, searchTurnSpeed * 4f * Time.deltaTime); // eases in over 0.25 s
                        float delta = Mathf.Min(speed * Time.deltaTime, 360f - swept);
                        Rotate(sign * delta);
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
                        Rotate(Mathf.Sign(remaining) * Mathf.Min(Mathf.Abs(remaining), speed * Time.deltaTime));
                        yield return null;
                    }
                }
                IsTurning = false;
                yield break;
            }

            // Any angle ("turn 30 degrees right"); "turn left" is 90, "turn around" 180.
            float degrees = step.HasMagnitude ? step.magnitude : DefaultTurnDegrees;
            yield return TurnBy(sign * degrees);
            IsTurning = false;
        }

        // An eased turn of an exact angle (positive = right): slow, faster, slow.
        IEnumerator TurnBy(float degrees)
        {
            float seconds = Mathf.Max(shortestTurnSeconds, quarterTurnSeconds * Mathf.Abs(degrees) / 90f);
            float done = 0f, t = 0f;
            while (t < seconds)
            {
                t = Mathf.Min(seconds, t + Time.deltaTime);
                float target = degrees * Mathf.SmoothStep(0f, 1f, t / seconds);
                Rotate(target - done);
                done = target;
                yield return null;
            }
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
