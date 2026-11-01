// Assets/GUI/LazyFollow.cs
// Keeps a world-space UI (the panel, the caption strip, the tutorial card) in front of the user without
// chasing every glance: it stays where it is until the user looks more than recenterDegrees away from it
// or moves away from it, then glides back in front of them. It always turns to face them, upright.
// Replaces VFFloatingMenu, which followed every head movement.
using UnityEngine;

public class LazyFollow : MonoBehaviour
{
    [Tooltip("Metres in front of the eyes.")]
    public float distance = 1.2f;
    [Tooltip("Metres above (+) or below (-) eye level.")]
    public float heightOffset = 0f;
    [Tooltip("How far the user can look away (degrees, left or right) before it comes back in front.")]
    public float recenterDegrees = 25f;
    [Tooltip("Roughly how long the glide back takes, in seconds.")]
    public float glideSeconds = 0.6f;

    private Transform _head;
    private bool _gliding;
    private Vector3 _velocity;

    // Puts it straight in front of the user now (when it's shown, or after a teleport).
    public void SnapNow()
    {
        if (!FindHead()) return;
        transform.position = Target();
        Face();
        _gliding = false;
        _velocity = Vector3.zero;
    }

    private void OnEnable() => SnapNow();

    private void LateUpdate()
    {
        if (!FindHead()) return;

        Vector3 toUi = transform.position - _head.position;
        Vector3 level = new Vector3(toUi.x, 0f, toUi.z);
        if (level.magnitude > distance + 2f) { SnapNow(); return; } // teleported: don't fly across the world

        float away = Vector3.Angle(Level(_head.forward), level);
        if (away > recenterDegrees || Mathf.Abs(level.magnitude - distance) > 0.4f) _gliding = true;
        if (_gliding)
        {
            Vector3 target = Target();
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, glideSeconds * 0.4f);
            if ((transform.position - target).sqrMagnitude < 0.0004f) _gliding = false;
        }
        Face();
    }

    private bool FindHead()
    {
        if (_head == null && Camera.main != null) _head = Camera.main.transform;
        return _head != null;
    }

    private Vector3 Target()
    {
        return _head.position + Level(_head.forward) * distance + Vector3.up * heightOffset;
    }

    private void Face()
    {
        Vector3 away = transform.position - _head.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away);
    }

    // Looking straight up or down, forward has no level part: use the head's up vector instead.
    private Vector3 Level(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.01f) { v = _head.forward.y > 0f ? -_head.up : _head.up; v.y = 0f; }
        return v.normalized;
    }
}
