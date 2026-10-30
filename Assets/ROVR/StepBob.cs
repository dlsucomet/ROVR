// Assets/ROVR/StepBob.cs
// The slight up-and-down of walking, for step commands only ("take 3 steps", "walk in steps to the
// door"): the view dips once per step and comes back up, about 1.5 cm by default. Ordinary moves glide.
//
// NavigationController calls Walk() every frame of a step command with how many steps are done. The
// dip follows distance, not time, so each step is exactly one dip at any speed, and a whole number of
// steps ends level. If Walk() stops being called (the move ended, was halted or blocked mid-step), the
// view eases back to level within SettleSeconds.
//
// A view moving without the head moving can cause VR sickness, so it's subtle by default, and the
// experimenter can make it stronger or turn it off (the panel's Bob button, or Level here).
using UnityEngine;

namespace ROVR
{
    public class StepBob : MonoBehaviour
    {
        public enum Strength { Off, Subtle, Strong }

        [Tooltip("How far the view dips with each step: Subtle 1.5 cm, Strong 3 cm.")]
        public Strength level = Strength.Subtle;

        const float SettleSeconds = 0.2f;

        Transform _rig;
        float _applied;  // metres the rig is lowered by now (0 or negative)
        float _target;
        int _walkFrame = -1;

        public static float DepthOf(Strength strength)
        {
            return strength == Strength.Subtle ? 0.015f : strength == Strength.Strong ? 0.03f : 0f;
        }

        // How far the view is lowered right now, in metres (for tests and logs).
        public float Offset => _applied;

        // stepsDone: steps walked so far in this command (0 at the start, 2.5 halfway through the third).
        public void Walk(float stepsDone)
        {
            float s = Mathf.Sin(Mathf.PI * stepsDone);
            _target = -DepthOf(level) * s * s; // level at each whole step, lowest halfway (the foot coming down)
            _walkFrame = Time.frameCount;
        }

        void Start()
        {
            _rig = BodyUnderHead.RigOf(transform);
        }

        // After NavigationController has moved for this frame.
        void LateUpdate()
        {
            if (_rig == null) return;
            float next = _walkFrame == Time.frameCount
                ? _target
                : Mathf.MoveTowards(_applied, 0f, DepthOf(Strength.Strong) / SettleSeconds * Time.deltaTime);
            if (next == _applied) return;
            _rig.position += Vector3.up * (next - _applied);
            _applied = next;
        }
    }
}
