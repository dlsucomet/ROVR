// Assets/Scripts/ROVR/NavigationController.cs
// The deterministic, engine-native half of the Semantic-Geometric Division of Labor (Section
// 6.2.3): the LLM never touches trajectory math, it only hands off a NavigationCommand. This
// controller is what actually moves the CharacterController, and it's the layer responsible for
// the cybersickness mitigations described in Chapter 2 — fixed velocity (no acceleration ramp),
// discrete snap-turns instead of continuous yaw, and forward locomotion locked to the horizontal
// plane regardless of head pitch (Section 3.3.1).
using System.Collections;
using UnityEngine;

namespace ROVR
{
    [RequireComponent(typeof(CharacterController))]
    public class NavigationController : MonoBehaviour
    {
        [SerializeField] Transform pov;
        [SerializeField] float moveSpeed = 2f;        // m/s, fixed cap — no acceleration curve
        [SerializeField] float verticalSpeed = 1.5f;   // m/s for explicit up/down commands
        [SerializeField] float turnDuration = 0.15f;   // seconds to complete one discrete snap-turn
        [SerializeField] float defaultTurnDegrees = 90f;
        [SerializeField] float wallStopThreshold = 0.5f; // meters, sense-made-conditional stop distance (6.3.4)
        [SerializeField] LayerMask detectionMask = ~0;

        CharacterController controller;
        Coroutine activeRoutine;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (pov == null) pov = Camera.main != null ? Camera.main.transform : transform;

            // The worlds are flat and there's no gravity here, so a step-up would leave the avatar
            // hovering on top of whatever it hopped onto. Walls, props and the tree just block.
            controller.stepOffset = 0f;
        }

        public void Execute(NavigationCommand command)
        {
            Halt();
            if (command.steps.Length == 0) return;
            activeRoutine = StartCoroutine(RunSteps(command.steps));
        }

        // Called by the Interrupt Module (bypasses the LLM) and internally before starting a new command.
        public void Halt()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }
        }

        IEnumerator RunSteps(NavigationStep[] steps)
        {
            foreach (var step in steps)
                yield return RunStep(step);
            activeRoutine = null;
        }

        IEnumerator RunStep(NavigationStep step)
        {
            switch (step.action)
            {
                case ActionType.Move: yield return RunMove(step); break;
                case ActionType.Turn: yield return RunTurn(step); break;
                case ActionType.Stop: Halt(); break;
                default: yield break; // Unknown never reaches the controller
            }
        }

        IEnumerator RunMove(NavigationStep step)
        {
            // 3.3.1: forward/back/left/right stay on the horizontal (X,Z) plane no matter where
            // the user is looking — only explicit up/down commands move along Y.
            Vector3 planarForward = Vector3.ProjectOnPlane(pov.forward, Vector3.up).normalized;
            Vector3 planarRight = Vector3.ProjectOnPlane(pov.right, Vector3.up).normalized;

            Vector3 dir;
            float speed;
            switch (step.direction)
            {
                case DirectionType.Forward: dir = planarForward; speed = moveSpeed; break;
                case DirectionType.Back: dir = -planarForward; speed = moveSpeed; break;
                case DirectionType.Left: dir = -planarRight; speed = moveSpeed; break;
                case DirectionType.Right: dir = planarRight; speed = moveSpeed; break;
                case DirectionType.Up: dir = Vector3.up; speed = verticalSpeed; break;
                case DirectionType.Down: dir = Vector3.down; speed = verticalSpeed; break;
                default: yield break;
            }

            if (step.HasCondition)
            {
                // Sense-made conditional (6.3.4): "move forward until you hit a wall" — translate
                // until the raycast toward the named tag drops below the safety threshold, then
                // stop. No exact stopping coordinate is ever computed by the LLM.
                float probe = wallStopThreshold + moveSpeed * Time.deltaTime + 0.1f;
                while (true)
                {
                    if (Physics.Raycast(transform.position, dir, out RaycastHit hit, probe, detectionMask)
                        && hit.collider.CompareTag(step.condition)
                        && hit.distance <= wallStopThreshold)
                    {
                        yield break;
                    }
                    controller.Move(dir * speed * Time.deltaTime);
                    yield return null;
                }
            }

            if (step.HasMagnitude)
            {
                // Discrete geometric magnitude (6.3.4): "move 10 meters forward".
                float travelled = 0f;
                while (travelled < step.magnitude)
                {
                    float delta = Mathf.Min(speed * Time.deltaTime, step.magnitude - travelled);
                    controller.Move(dir * delta);
                    travelled += delta;
                    yield return null;
                }
                yield break;
            }

            // Basic directional translation (6.3.4): continuous velocity until the Interrupt
            // Module halts it or the next resolved command supersedes it via Execute()->Halt().
            while (true)
            {
                controller.Move(dir * speed * Time.deltaTime);
                yield return null;
            }
        }

        IEnumerator RunTurn(NavigationStep step)
        {
            // Angular rotation (6.3.4): fixed discrete snap-turn via Quaternion.Euler, not
            // continuous unconstrained yaw — avoids the sensory-conflict cybersickness driver
            // described in Section 2.1. Left is negative yaw; Right or unspecified is positive,
            // so an unqualified "turn around" (direction=none, magnitude=180) still resolves.
            float degrees = step.HasMagnitude ? step.magnitude : defaultTurnDegrees;
            if (step.direction == DirectionType.Left) degrees = -degrees;

            Quaternion start = transform.rotation;
            Quaternion end = start * Quaternion.Euler(0f, degrees, 0f);
            float elapsed = 0f;
            while (elapsed < turnDuration)
            {
                elapsed += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(start, end, elapsed / turnDuration);
                yield return null;
            }
            transform.rotation = end;
        }
    }
}
