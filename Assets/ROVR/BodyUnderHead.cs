// Assets/ROVR/BodyUnderHead.cs
// Keeps PlayerBody (its CharacterController) under the user's head. In a headset you stand anywhere in
// your play area, so the tracked head is rarely at the body's centre: walls then stopped the body, not
// your head, and the body's white capsule showed up beside you. Each frame the body steps to where the
// head is (blocked by walls like any movement) and the XR rig shifts back by the same amount, so the
// view itself never moves.
//
// FaceFrom(yaw) is for teleports: it puts the head, not the body, on the destination, facing yaw.
// InputTracking.Recenter did that for older XR plug-ins; under OpenXR it does nothing.
//
// The capsule (PlayerBody/BodyVisual) is hidden while playing. It only marks the player in the Scene view.
using UnityEngine;
using UnityEngine.XR;

namespace ROVR
{
    public class BodyUnderHead : MonoBehaviour
    {
        const float MinStep = 0.01f; // m; smaller head movements wait until they add up

        CharacterController _body;
        Transform _head, _rig;
        bool _facePending;
        float _faceYaw;

        void Start()
        {
            _body = GetComponent<CharacterController>();
            _head = Camera.main != null ? Camera.main.transform : null;

            // The XR rig: the child of PlayerBody that holds the head camera.
            if (_head != null && _head.IsChildOf(transform) && _head != transform)
            {
                _rig = _head;
                while (_rig.parent != transform) _rig = _rig.parent;
            }

            Transform visual = transform.Find("BodyVisual");
            if (visual != null)
                foreach (var shown in visual.GetComponentsInChildren<Renderer>()) shown.enabled = false;
        }

        // Puts the head on the body's position, looking toward yaw (degrees), wherever the user stands in
        // the room. Waits for the headset's first tracked pose if it doesn't have one yet.
        public void FaceFrom(float yaw)
        {
            _faceYaw = yaw;
            _facePending = true;
        }

        // After this frame's movement; the head pose for the frame is in by now.
        void LateUpdate()
        {
            if (_rig == null || _body == null || !_body.enabled) return;

            if (_facePending && Tracking())
            {
                _facePending = false;
                _rig.RotateAround(_head.position, Vector3.up, Mathf.DeltaAngle(_head.eulerAngles.y, _faceYaw));
                _rig.position -= Flat(_head.position - transform.position);
                return;
            }

            Vector3 offset = Flat(_head.position - transform.position);
            if (offset.sqrMagnitude < MinStep * MinStep) return;
            Vector3 before = transform.position;
            _body.Move(offset);
            _rig.position -= Flat(transform.position - before);
        }

        // In the Editor without a headset there is nothing to wait for. In the headset, the camera stays at
        // the rig's origin until the first tracked pose arrives.
        bool Tracking()
        {
            return !XRSettings.isDeviceActive || _head.localPosition.sqrMagnitude > 0.0001f;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
