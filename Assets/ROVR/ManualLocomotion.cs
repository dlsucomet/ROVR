// Assets/ROVR/ManualLocomotion.cs
// The Manual (controller) condition: the left stick walks, the right stick turns. It moves the same
// body (PlayerBody's CharacterController) through the same walls as voice movement, and turns with the
// same comfort vignette, so the two conditions differ only in how the user controls them.
//
//   Left stick: forward / back / sideways, relative to where the user looks (kept level). The speed is
//   in proportion to how far the stick is pushed, up to maxSpeed (2 m/s, ROVR's Normal) at full push.
//   Right stick, left / right: a smooth turn about the head, faster the further it's pushed.
//
// Each stretch of moving or turning is summarised for the transcript when the stick is let go
// ("Moved 3.2 m in 2.4 s, top speed 1.9 m/s"), so Manual sessions have movement data too.
//
// For the study's world log (OnInput): the left stick's direction in one of 8 (forward, forward-right,
// right, back-right, back, back-left, left, forward-left), reported each time it changes, with how far the
// stick is pushed; "stopped" when it's let go. A direction near a boundary has to cross it by a few
// degrees before it counts as changed, so a thumb resting on the edge doesn't flicker between two.
// The right stick: "left" / "right" when a turn starts or reverses, "stopped" when it ends.
using System;
using UnityEngine;
using UnityEngine.XR;

namespace ROVR
{
    [RequireComponent(typeof(CharacterController))]
    public class ManualLocomotion : MonoBehaviour
    {
        [Tooltip("m/s with the left stick pushed all the way.")]
        public float maxSpeed = 2f;
        [Tooltip("Degrees per second with the right stick pushed all the way.")]
        public float maxTurnSpeed = 90f;
        [Tooltip("Stick travel ignored around the centre, so a resting thumb doesn't drift.")]
        public float deadZone = 0.15f;

        public bool IsTurning { get; private set; } // for the comfort vignette
        public bool IsMoving { get; private set; }

        // A finished stretch of moving or turning, for the transcript.
        public event Action<string> OnLog;

        // ("move", direction, push) or ("turn", "left" / "right" / "stopped", push), each time it changes.
        public event Action<string, string, float> OnInput;

        public static readonly string[] Directions =
            { "forward", "forward-right", "right", "back-right", "back", "back-left", "left", "forward-left" };
        const float DirectionHysteresis = 8f; // degrees past a boundary before the direction counts as changed

        // For tests: when set, used instead of the controllers.
        public Vector2? SimulatedMove { get; set; }
        public Vector2? SimulatedTurn { get; set; }

        CharacterController _body;
        Transform _head;
        float _moveStart = -1f, _moved, _topSpeed;
        float _turnStart = -1f, _turned;
        int _direction = -1; // index into Directions while the left stick is pushed, else -1
        int _turnSign;       // -1 left, 1 right, 0 not turning

        void Awake()
        {
            _body = GetComponent<CharacterController>();
        }

        void Start()
        {
            _head = Camera.main != null ? Camera.main.transform : transform;
        }

        void OnDisable()
        {
            Summarise(true);
            IsTurning = IsMoving = false;
            ReportMove(-1, 0f);
            ReportTurn(0, 0f);
        }

        void Update()
        {
            Vector2 move = SimulatedMove ?? Stick(XRNode.LeftHand);
            Vector2 turn = SimulatedTurn ?? Stick(XRNode.RightHand);

            // Walk: speed in proportion to the push beyond the dead zone.
            float push = Mathf.Clamp01((Mathf.Min(1f, move.magnitude) - deadZone) / (1f - deadZone));
            IsMoving = push > 0f && _body.enabled;
            if (IsMoving)
            {
                Vector3 forward = LevelDirection(_head.forward, _head.forward.y > 0f ? -_head.up : _head.up);
                Vector3 right = LevelDirection(_head.right, _head.right);
                Vector3 dir = (forward * move.y + right * move.x).normalized;
                float speed = push * maxSpeed;
                Vector3 before = transform.position;
                _body.Move(dir * speed * Time.deltaTime);
                if (_moveStart < 0f) { _moveStart = Time.time; _moved = 0f; _topSpeed = 0f; }
                Vector3 moved = transform.position - before;
                moved.y = 0f;
                _moved += moved.magnitude;
                _topSpeed = Mathf.Max(_topSpeed, speed);
            }
            ReportMove(IsMoving ? DirectionOf(move, _direction) : -1, push);

            // Turn: left / right only.
            float t = Mathf.Abs(turn.x) <= deadZone ? 0f : Mathf.Sign(turn.x) * (Mathf.Abs(turn.x) - deadZone) / (1f - deadZone);
            IsTurning = t != 0f;
            if (IsTurning)
            {
                float degrees = t * maxTurnSpeed * Time.deltaTime;
                transform.RotateAround(_head.position, Vector3.up, degrees); // about the head, so the view stays put in the room
                if (_turnStart < 0f) { _turnStart = Time.time; _turned = 0f; }
                _turned += degrees;
            }
            ReportTurn(IsTurning ? (int)Mathf.Sign(t) : 0, Mathf.Abs(t));

            Summarise(false);
        }

        // Which of the 8 directions the stick points, keeping the current one until the stick is clearly
        // past its boundary (22.5 degrees from its centre plus the hysteresis).
        public static int DirectionOf(Vector2 stick, int current)
        {
            float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg; // 0 = forward, 90 = right
            if (current >= 0 && Mathf.Abs(Mathf.DeltaAngle(angle, current * 45f)) <= 22.5f + DirectionHysteresis) return current;
            return (int)Mathf.Repeat(Mathf.Round(angle / 45f), 8f);
        }

        void ReportMove(int direction, float push)
        {
            if (direction == _direction) return;
            _direction = direction;
            OnInput?.Invoke("move", direction >= 0 ? Directions[direction] : "stopped", direction >= 0 ? push : 0f);
        }

        void ReportTurn(int sign, float push)
        {
            if (sign == _turnSign) return;
            _turnSign = sign;
            OnInput?.Invoke("turn", sign < 0 ? "left" : sign > 0 ? "right" : "stopped", sign != 0 ? push : 0f);
        }

        // Logs a stretch of moving or turning once its stick is let go (or now, when stopping).
        void Summarise(bool now)
        {
            if (_moveStart >= 0f && (now || !IsMoving))
            {
                OnLog?.Invoke($"Moved {_moved:F1} m in {Time.time - _moveStart:F1} s, top speed {_topSpeed:F1} m/s");
                _moveStart = -1f;
            }
            if (_turnStart >= 0f && (now || !IsTurning))
            {
                OnLog?.Invoke($"Turned {(_turned < 0f ? "left" : "right")} {Mathf.Abs(_turned):F0}° in {Time.time - _turnStart:F1} s");
                _turnStart = -1f;
            }
        }

        static Vector2 Stick(XRNode hand)
        {
            var device = InputDevices.GetDeviceAtXRNode(hand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 value) ? value : Vector2.zero;
        }

        // A direction kept level. Looking straight up or down, forward has no level part: use the
        // head's up vector instead (as NavigationController does), so "forward" is still where you face.
        static Vector3 LevelDirection(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 0.01f) { v = fallback; v.y = 0f; }
            return v.normalized;
        }
    }
}
