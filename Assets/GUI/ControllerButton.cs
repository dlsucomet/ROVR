// Assets/GUI/ControllerButton.cs
// One button on a Quest controller (by default A, the right controller's primary button), as a short
// press or a long press. Released before longPressSec = short press. Held that long = long press,
// which fires while the button is still down, so the user feels it happen. A short buzz confirms each.
//
// The panel uses it for A: short press mutes / unmutes the mic, long press shows / hides the panel.
// In the Editor, F1 does the same, for testing without a headset.
using System;
using UnityEngine;
using UnityEngine.XR;

public class ControllerButton : MonoBehaviour
{
    [Tooltip("Which controller.")]
    public XRNode hand = XRNode.RightHand;
    [Tooltip("Seconds held for a long press.")]
    public float longPressSec = 0.6f;

    public event Action OnShortPress;
    public event Action OnLongPress;

    // For tests: when set, used instead of the controller.
    public bool? SimulatedDown { get; set; }

    private bool _down;
    private float _downAt;
    private bool _longFired;

    private void Update()
    {
        bool down = SimulatedDown ?? (ReadController() || EditorKey());
        float now = Time.realtimeSinceStartup;

        if (down && !_down)
        {
            _downAt = now;
            _longFired = false;
        }
        if (down && !_longFired && now - _downAt >= longPressSec)
        {
            _longFired = true;
            Buzz(0.6f, 0.12f);
            OnLongPress?.Invoke();
        }
        if (!down && _down && !_longFired)
        {
            Buzz(0.3f, 0.05f);
            OnShortPress?.Invoke();
        }
        _down = down;
    }

    private bool ReadController()
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        return device.isValid && device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed) && pressed;
    }

    private static bool EditorKey()
    {
#if UNITY_EDITOR
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.f1Key.isPressed;
#else
        return false;
#endif
    }

    private void Buzz(float amplitude, float seconds)
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        if (device.isValid) device.SendHapticImpulse(0, amplitude, seconds);
    }
}
