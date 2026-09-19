// Assets/Scripts/ROVR/NavigationController.cs
// The deterministic, engine-native half of the Semantic-Geometric Division of Labor (Section
// 6.2.3): the LLM never touches trajectory math, it only hands off a NavigationCommand. This
// controller is what actually moves the CharacterController, and it's the layer responsible for
// the cybersickness mitigations described in Chapter 2 — fixed velocity (no acceleration ramp up),
// instant discrete snap-turns instead of continuous yaw, and forward locomotion locked to the
// horizontal plane regardless of head pitch (Section 3.3.1).
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
        [SerializeField] float moveSpeed = 2f;           // m/s, fixed cap — no acceleration curve
        [SerializeField] float verticalSpeed = 1.5f;     // m/s for explicit up/down commands
        [SerializeField] float snapDegrees = 90f;        // turns are whole multiples of this (Section 2.1)
        [SerializeField] float snapPause = 0.25f;        // seconds between successive snaps
        [SerializeField] float stopGap = 0.5f;           // m between the body and the target when a conditional move stops (6.3.4)
        [SerializeField] float rampLength = 1f;          // m over which speed eases to a stop before stopGap (6.3.4)
        [SerializeField] float minCreepSpeed = 0.15f;    // m/s floor during the ramp so the stop is reached
        [SerializeField] float maxConditionalDistance = 60f; // safety cap so "move until X" can't run forever
        [SerializeField] LayerMask detectionMask = ~0;

        const int BlockedFramesLimit = 10;   // consecutive frames with (almost) no progress = blocked
        const float ProbeLift = 0.1f;        // probe capsule starts this far above the feet, clear of the floor

        public event Action<NavigationCommand, CommandOutcome> OnCommandFinished;

        public bool IsBusy => activeRoutine != null;

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

            var finished = current;
            current = null;
            OnCommandFinished?.Invoke(finished, CommandOutcome.Halted);
        }

        IEnumerator RunSteps(NavigationCommand command)
        {
            var outcome = CommandOutcome.Completed;

            foreach (var step in command.steps)
            {
                stepOutcome = CommandOutcome.Completed;
                yield return RunStep(step);

                if (stepOutcome != CommandOutcome.Completed)
                {
                    outcome = stepOutcome; // a blocked / halted / not-found step ends the whole chain
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

            while (travelled < target)
            {
                float thisSpeed = speed;

                if (step.HasCondition)
                {
                    // Sense-made conditional (6.3.4): ease off and stop stopGap short of the target.
                    float gap = GapToTag(dir, step.condition, stopGap + rampLength + speed * Time.deltaTime + 0.1f);
                    if (gap <= stopGap) yield break;
                    if (gap < stopGap + rampLength)
                        thisSpeed = Mathf.Max(minCreepSpeed, speed * (gap - stopGap) / rampLength);
                }

                float delta = Mathf.Min(thisSpeed * Time.deltaTime, target - travelled);
                Vector3 before = transform.position;
                controller.Move(dir * delta);

                // Count what actually happened, not what was asked for, so walls end the step.
                float moved = Vector3.Dot(transform.position - before, dir);
                travelled += Mathf.Max(0f, moved);

                if (moved < delta * 0.25f)
                {
                    if (++blockedFrames >= BlockedFramesLimit)
                    {
                        stepOutcome = CommandOutcome.Blocked;
                        yield break;
                    }
                }
                else
                {
                    blockedFrames = 0;
                }

                yield return null;
            }
        }

        // Distance the body can still travel along `dir` before touching an object tagged `tag`, or
        // infinity if something solid (or nothing) is in the way first. Measured from the body's
        // surface, so it doesn't depend on the avatar's radius.
        float GapToTag(Vector3 dir, string tag, float castDistance)
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
                if (hit.collider.CompareTag(tag)) return hit.distance;
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

            if (step.HasCondition)
            {
                // "Turn around until you see the door" (6.4.2): keep snapping until it's in view, at
                // most one full circle.
                int maxSnaps = Mathf.Max(1, Mathf.RoundToInt(360f / snapDegrees));
                for (int i = 0; i <= maxSnaps; i++)
                {
                    if (grounding != null && grounding.IsVisible(step.condition)) yield break;
                    if (i == maxSnaps) break;

                    Snap(sign * snapDegrees);
                    yield return new WaitForSeconds(snapPause);
                }

                stepOutcome = CommandOutcome.NotFound;
                yield break;
            }

            // Whole multiples of the snap angle only: "turn around" is two snaps, 45 rounds up to one.
            float degrees = step.HasMagnitude ? step.magnitude : snapDegrees;
            int snaps = Mathf.Max(1, Mathf.RoundToInt(degrees / snapDegrees));

            for (int i = 0; i < snaps; i++)
            {
                Snap(sign * snapDegrees);
                yield return new WaitForSeconds(snapPause);
            }
        }

        // Instant yaw about the user's head, not the rig origin, so the view stays put in the room.
        void Snap(float degrees)
        {
            Vector3 pivot = pov != null ? pov.position : transform.position;
            transform.RotateAround(pivot, Vector3.up, degrees);
        }
    }
}
