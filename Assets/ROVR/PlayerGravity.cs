// Assets/ROVR/PlayerGravity.cs
// Gravity for the player's body (PlayerBody's CharacterController), whatever moves it: voice commands
// (NavigationController), the sticks (ManualLocomotion) or keeping the body under the head
// (BodyUnderHead). Only the body falls; the head and hands keep their own tracking on top of it.
//
//   - Each frame the body is pushed down; on the floor that's a small constant press (so it follows the
//     floor and never floats), in the air it's real gravity, up to a terminal speed.
//   - Jump() (voice "up" / "jump") gives an upward speed when standing; landing returns to the floor press.
//   - The body can't pass through floors or walls: every move is a CharacterController sweep.
//   - If the body ever falls far below the last floor it stood on (a hole in a world, a bad teleport),
//     it's put back there rather than falling forever.
//   - Teleports call Settle() so no fall speed carries into the new world.
using UnityEngine;

namespace ROVR
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGravity : MonoBehaviour
    {
        [Tooltip("m/s² downward.")]
        public float gravity = 9.81f;
        [Tooltip("m/s: the fastest a fall gets.")]
        public float terminalSpeed = 20f;
        [Tooltip("m/s upward at the start of a jump (1.0 m high at 4.4).")]
        public float jumpSpeed = 3.5f;
        [Tooltip("Metres below the last floor stood on before the body is put back there.")]
        public float fallLimit = 15f;

        const float GroundPress = 2f; // m/s pressed into the floor while standing, so slopes and steps down are followed

        CharacterController _body;
        float _verticalSpeed;
        Vector3 _lastGround;
        bool _haveGround;
        float _jumpedAt = -10f;

        public bool IsGrounded { get; private set; }
        public float VerticalSpeed => _verticalSpeed;
        // Within the first moments of a jump, before the body has left the floor.
        public bool JustJumped => Time.time - _jumpedAt < 0.15f;

        void Awake()
        {
            _body = GetComponent<CharacterController>();
        }

        // Jumps if standing on something. False if already in the air.
        public bool Jump()
        {
            if (!IsGrounded && !JustJumped) return false;
            _verticalSpeed = jumpSpeed;
            _jumpedAt = Time.time;
            IsGrounded = false;
            return true;
        }

        // After a teleport: no fall speed carries over, and the new spot is the floor to come back to.
        public void Settle()
        {
            _verticalSpeed = 0f;
            _haveGround = false;
        }

        // After the frame's walking and turning (Update), so this frame's ground contact is known.
        void LateUpdate()
        {
            if (_body == null || !_body.enabled) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsGrounded && _verticalSpeed <= 0f && !JustJumped) _verticalSpeed = -GroundPress;
            else _verticalSpeed = Mathf.Max(-terminalSpeed, _verticalSpeed - gravity * dt);

            var flags = _body.Move(Vector3.up * _verticalSpeed * dt);
            bool grounded = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && _verticalSpeed > 0f) _verticalSpeed = 0f; // head on the ceiling
            IsGrounded = grounded;
            if (grounded)
            {
                if (!JustJumped) _verticalSpeed = -GroundPress;
                _lastGround = transform.position;
                _haveGround = true;
            }
            else if (_haveGround && transform.position.y < _lastGround.y - fallLimit)
            {
                Debug.LogWarning("[ROVR] Fell " + fallLimit + " m below the last floor: back to " + _lastGround.ToString("F1"));
                _body.enabled = false;
                transform.position = _lastGround;
                _body.enabled = true;
                _verticalSpeed = 0f;
            }
        }
    }
}
