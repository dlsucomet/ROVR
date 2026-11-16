// Assets/GUI/VirtualHands.cs
// Draws the user's hands where their real hands are, so they can see themselves in the world:
//
//   - Holding the controllers: a simple gloved hand wrapped round each controller, following it exactly
//     (position and rotation, every frame).
//   - Hands tracked by the headset (controllers put down, hand tracking on): each hand's joints and bones,
//     from the headset's hand tracking (XR Hands), so fingers move as the real ones do.
//
// Both are drawn in the rig's tracking space (under the player's body), so they stay aligned through every
// move and turn. When a hand or controller loses tracking (out of the cameras' view, put down) it's hidden
// at once, never left frozen or floating where it was last seen; it comes back as soon as it's tracked.
//
// For the minimap (WorldMinimap): TryGetPinch / TryGetPinchStarted report a tracked hand's pinch (thumb and
// index finger together), so the map can be picked up and moved by hand. With controllers, the map is
// grabbed with the grip button through XRI instead.
//
// Hand tracking needs the XR Hands package and the OpenXR "Hand Tracking Subsystem" feature (both set up by
// Tools > ROVR > Set Up Hand Tracking), and hand tracking turned on in the Quest's settings.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

public class VirtualHands : MonoBehaviour
{
    [Tooltip("Colour of the hands (a neutral glove).")]
    public Color handColour = new Color(0.78f, 0.82f, 0.9f, 1f);
    [Tooltip("Thumb and index tips closer than this (m) is a pinch; it ends past pinchRelease.")]
    public float pinchStart = 0.02f;
    public float pinchRelease = 0.035f;

    class ControllerHand
    {
        public XRNode node;
        public Transform controller;
        public GameObject model;
    }

    class TrackedHand
    {
        public GameObject root;
        public Transform[] joints;  // by XRHandJointID index
        public List<(Transform bone, int from, int to)> bones = new List<(Transform, int, int)>();
        public bool pinching;
        public int pinchStartFrame = -1;
        public Vector3 pinchPoint;
        public bool tracked;
    }

    static VirtualHands _instance;
    readonly ControllerHand[] _controllers = new ControllerHand[2];
    readonly TrackedHand[] _tracked = new TrackedHand[2];
    readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
    XRHandSubsystem _hands;
    Transform _space; // the rig's tracking space (the camera's parent)
    Material _material;

    // A tracked hand's pinch: where (between thumb and index tips) and whether it's pinching now.
    public static bool TryGetPinch(bool left, out Vector3 point, out bool pinching)
    {
        point = default;
        pinching = false;
        var h = _instance != null ? _instance._tracked[left ? 0 : 1] : null;
        if (h == null || !h.tracked) return false;
        point = h.pinchPoint;
        pinching = h.pinching;
        return true;
    }

    // True on the frame a tracked hand starts pinching.
    public static bool TryGetPinchStarted(bool left, out Vector3 point)
    {
        point = default;
        var h = _instance != null ? _instance._tracked[left ? 0 : 1] : null;
        if (h == null || !h.tracked || h.pinchStartFrame != Time.frameCount) return false;
        point = h.pinchPoint;
        return true;
    }

    void Awake() { _instance = this; }

    void Start()
    {
        var cam = Camera.main;
        _space = cam != null ? cam.transform.parent : null;
        // A copy of the primitives' own material (always in the build), in the hands' colour.
        var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _material = new Material(probe.GetComponent<Renderer>().sharedMaterial) { color = handColour, enableInstancing = true };
        DestroyImmediate(probe);

        _controllers[0] = new ControllerHand { node = XRNode.LeftHand, controller = FindController("Left Controller") };
        _controllers[1] = new ControllerHand { node = XRNode.RightHand, controller = FindController("Right Controller") };
        foreach (var c in _controllers)
            if (c.controller != null) c.model = BuildControllerHand(c.controller, c.node == XRNode.LeftHand);

        for (int i = 0; i < 2; i++) _tracked[i] = BuildTrackedHand(i == 0);
    }

    static Transform FindController(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == name && t.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) return t;
        return null;
    }

    void Update()
    {
        if (_hands == null || !_hands.running)
        {
            _hands = null;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var s in _subsystems) if (s.running) { _hands = s; break; }
        }

        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            bool handTracked = _hands != null && (left ? _hands.leftHand : _hands.rightHand).isTracked;
            UpdateTracked(_tracked[i], handTracked ? (left ? _hands.leftHand : _hands.rightHand) : default, handTracked);

            // The gloved controller hand only while that controller is tracked, and not when the real hand is.
            var c = _controllers[i];
            if (c?.model == null) continue;
            bool show = !handTracked && c.controller.gameObject.activeInHierarchy && ControllerTracked(c.node);
            if (c.model.activeSelf != show) c.model.SetActive(show);
        }
    }

    static bool ControllerTracked(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return false;
        if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked)) return tracked;
        return device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
               && (state & (InputTrackingState.Position | InputTrackingState.Rotation)) != 0;
    }

    void UpdateTracked(TrackedHand h, XRHand hand, bool tracked)
    {
        h.tracked = tracked && _space != null;
        if (h.root.activeSelf != h.tracked) h.root.SetActive(h.tracked);
        if (!h.tracked) { h.pinching = false; return; }

        for (int i = 0; i < h.joints.Length; i++)
        {
            var id = XRHandJointIDUtility.FromIndex(i);
            if (hand.GetJoint(id).TryGetPose(out Pose pose))
            {
                h.joints[i].gameObject.SetActive(true);
                h.joints[i].SetPositionAndRotation(_space.TransformPoint(pose.position), _space.rotation * pose.rotation);
            }
            else h.joints[i].gameObject.SetActive(false); // a joint without a pose this frame isn't drawn, not left behind
        }
        foreach (var (bone, from, to) in h.bones)
        {
            var a = h.joints[from]; var b = h.joints[to];
            bool both = a.gameObject.activeSelf && b.gameObject.activeSelf;
            bone.gameObject.SetActive(both);
            if (!both) continue;
            Vector3 d = b.position - a.position;
            bone.position = (a.position + b.position) / 2f;
            bone.rotation = d.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, d) : Quaternion.identity;
            bone.localScale = new Vector3(0.012f, d.magnitude / 2f, 0.012f);
        }

        // The pinch, with a gap between starting and ending so it doesn't flicker on the edge.
        var thumb = h.joints[XRHandJointID.ThumbTip.ToIndex()];
        var index = h.joints[XRHandJointID.IndexTip.ToIndex()];
        if (thumb.gameObject.activeSelf && index.gameObject.activeSelf)
        {
            float gap = Vector3.Distance(thumb.position, index.position);
            h.pinchPoint = (thumb.position + index.position) / 2f;
            bool was = h.pinching;
            h.pinching = was ? gap < pinchRelease : gap < pinchStart;
            if (h.pinching && !was) h.pinchStartFrame = Time.frameCount;
        }
        else h.pinching = false;
    }

    // ---------------------------------------------------------------- building

    TrackedHand BuildTrackedHand(bool left)
    {
        var h = new TrackedHand { root = new GameObject(left ? "Tracked Left Hand" : "Tracked Right Hand") };
        // In the rig's space (under the body), so it travels with every move and turn.
        if (_space != null) h.root.transform.SetParent(_space, false);
        int count = XRHandJointID.EndMarker.ToIndex();
        h.joints = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            var id = XRHandJointIDUtility.FromIndex(i);
            float r = id == XRHandJointID.Wrist || id == XRHandJointID.Palm ? 0.02f
                    : id.ToString().EndsWith("Tip") ? 0.012f : 0.016f;
            var j = Part(PrimitiveType.Sphere, id.ToString(), h.root.transform, Vector3.one * r);
            if (id == XRHandJointID.Palm) j.GetComponent<Renderer>().enabled = false;
            h.joints[i] = j.transform;
        }
        foreach (var chain in new[]
        {
            new[] { XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
            new[] { XRHandJointID.Wrist, XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
            new[] { XRHandJointID.Wrist, XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
            new[] { XRHandJointID.Wrist, XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
            new[] { XRHandJointID.Wrist, XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
        })
            for (int k = 0; k + 1 < chain.Length; k++)
            {
                var bone = Part(PrimitiveType.Cylinder, "Bone", h.root.transform, Vector3.one * 0.01f);
                h.bones.Add((bone.transform, chain[k].ToIndex(), chain[k + 1].ToIndex()));
            }
        // Across the knuckles, so the hand reads as a hand and not five sticks.
        var knuckles = new[] { XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal, XRHandJointID.RingProximal, XRHandJointID.LittleProximal };
        for (int k = 0; k + 1 < knuckles.Length; k++)
        {
            var bone = Part(PrimitiveType.Cylinder, "Knuckle", h.root.transform, Vector3.one * 0.01f);
            h.bones.Add((bone.transform, knuckles[k].ToIndex(), knuckles[k + 1].ToIndex()));
        }
        h.root.SetActive(false);
        return h;
    }

    // A gloved hand holding the controller: a palm on the outside of the handle, four fingers wrapped round
    // its front, the thumb resting on the face buttons. In the controller's own space (Quest Touch Plus grip).
    GameObject BuildControllerHand(Transform controller, bool left)
    {
        var root = new GameObject(left ? "Left Controller Hand" : "Right Controller Hand");
        root.transform.SetParent(controller, false);
        float side = left ? -1f : 1f; // the back of the hand faces outward
        var palm = Part(PrimitiveType.Cube, "Palm", root.transform, new Vector3(0.028f, 0.085f, 0.08f));
        palm.transform.localPosition = new Vector3(side * 0.03f, -0.035f, -0.03f);
        palm.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        for (int f = 0; f < 4; f++)
        {
            float y = -0.005f - f * 0.021f;
            var finger = Part(PrimitiveType.Capsule, "Finger", root.transform, new Vector3(0.018f, 0.03f, 0.018f));
            finger.transform.localPosition = new Vector3(side * 0.005f, y - 0.01f, 0.012f - f * 0.008f);
            finger.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(-35f, 0f, 0f);
        }
        var thumb = Part(PrimitiveType.Capsule, "Thumb", root.transform, new Vector3(0.02f, 0.03f, 0.02f));
        thumb.transform.localPosition = new Vector3(side * 0.012f, 0.012f, -0.01f);
        thumb.transform.localRotation = Quaternion.Euler(70f, side * -30f, 0f);
        root.SetActive(false);
        return root;
    }

    GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // never in the way, never hit by a ray
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = _material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
