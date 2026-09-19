// Assets/Scripts/ROVR/FOVMetadataGrounding.cs
// The Physics-based Raycast Matrix from Section 6.3.2: casts a grid of rays (horizontal x
// vertical) through the user's field of view to build the POV-scoped metadata index. Only objects
// this scan reaches are ever visible to the LLM (Closed-World Assumption, Section 3.3.2 / 6.2.4).
//
// What counts as "seen":
//   - Solid colliders block the view, tagged or not (a sofa hides what's behind it).
//   - Trigger colliders (Door markers) do NOT block the view, so rooms are visible through doorways.
//   - Only tags in `groundedTags` are reported. That list is the world's semantic vocabulary and is
//     what keeps each environment to its intended metadata (Thesis 7.1.2: House = doors, walls,
//     chairs; Maze = walls; Plain = the tree landmark). Furniture and Goal are deliberately absent.
//   - Every distinct object is its own entry, so two chairs in view are two entries and the LLM can
//     ask which one. Tags in `collapsedTags` (Wall) are merged into one nearest entry.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ROVR
{
    public struct GroundedObject
    {
        public string tag;
        public float distanceMeters;
        public float angleDegrees; // 0 = straight ahead, negative = left, positive = right
    }

    public class FOVMetadataGrounding : MonoBehaviour
    {
        [SerializeField] Transform pov;
        [SerializeField] float horizontalFov = 100f;
        [SerializeField] float verticalFov = 80f;
        [SerializeField] int columns = 21;
        [SerializeField] int rows = 13;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] LayerMask detectionMask = ~0;
        [SerializeField] string[] groundedTags = { "Wall", "Door", "Chair", "Tree" };
        [SerializeField] string[] collapsedTags = { "Wall" };
        [SerializeField] int maxObjects = 12;

        class Sample
        {
            public string tag;
            public float minDistance = float.MaxValue;
            public float yawSum;
            public int hits;
        }

        void Reset()
        {
            pov = Camera.main != null ? Camera.main.transform : transform;
        }

        void Awake()
        {
            if (pov == null) pov = Camera.main != null ? Camera.main.transform : transform;
        }

        public List<GroundedObject> ScanFieldOfView()
        {
            var result = new List<GroundedObject>();
            if (pov == null) return result;

            Transform self = pov.root;
            var samples = new Dictionary<Transform, Sample>();
            float hHalf = horizontalFov * 0.5f;
            float vHalf = verticalFov * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                float pitch = rows == 1 ? 0f : Mathf.Lerp(-vHalf, vHalf, (float)r / (rows - 1));
                Quaternion pitchRot = Quaternion.AngleAxis(pitch, pov.right);

                for (int c = 0; c < columns; c++)
                {
                    float yaw = columns == 1 ? 0f : Mathf.Lerp(-hHalf, hHalf, (float)c / (columns - 1));
                    Vector3 dir = Quaternion.AngleAxis(yaw, pov.up) * pitchRot * pov.forward;

                    var hits = Physics.RaycastAll(pov.position, dir, maxDistance, detectionMask, QueryTriggerInteraction.Collide);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                    foreach (var hit in hits)
                    {
                        var col = hit.collider;
                        if (col.transform.root == self) continue;

                        if (IsGrounded(col.tag)) Record(samples, col, yaw, hit.distance);

                        if (!col.isTrigger) break; // solid: nothing behind it is visible from here
                    }
                }
            }

            var merged = new Dictionary<string, GroundedObject>();
            foreach (var s in samples.Values)
            {
                var o = new GroundedObject { tag = s.tag, distanceMeters = s.minDistance, angleDegrees = s.yawSum / s.hits };

                if (!IsCollapsed(s.tag)) { result.Add(o); continue; }

                if (!merged.TryGetValue(s.tag, out var existing) || o.distanceMeters < existing.distanceMeters)
                    merged[s.tag] = o;
            }
            result.AddRange(merged.Values);

            result.Sort((a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            if (result.Count > maxObjects) result.RemoveRange(maxObjects, result.Count - maxObjects);
            return result;
        }

        // True when an object with this tag is in view right now (used by "turn until you see X").
        public bool IsVisible(string tag)
        {
            foreach (var o in ScanFieldOfView())
                if (string.Equals(o.tag, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Maps whatever the model wrote ("door", "Doors", " chair ") onto a real grounded tag.
        public bool TryNormalizeTag(string raw, out string tag)
        {
            tag = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string t = raw.Trim();
            foreach (var g in groundedTags)
                if (string.Equals(g, t, StringComparison.OrdinalIgnoreCase)) { tag = g; return true; }

            if (t.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                string singular = t.Substring(0, t.Length - 1);
                foreach (var g in groundedTags)
                    if (string.Equals(g, singular, StringComparison.OrdinalIgnoreCase)) { tag = g; return true; }
            }
            return false;
        }

        public string ToJson(List<GroundedObject> objects)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (i > 0) sb.Append(',');
                string side = Mathf.Abs(o.angleDegrees) < 12f ? "ahead" : (o.angleDegrees < 0f ? "left" : "right");
                sb.Append("{\"tag\":\"").Append(o.tag).Append("\",")
                  .Append("\"distance_m\":").Append(o.distanceMeters.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"angle_deg\":").Append(o.angleDegrees.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"side\":\"").Append(side).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        bool IsGrounded(string tag) { return Contains(groundedTags, tag); }
        // Tags like Wall that are merged into one nearest entry: they're everywhere, not a single target.
        public bool IsCollapsedTag(string tag) { return Contains(collapsedTags, tag); }
        bool IsCollapsed(string tag) { return IsCollapsedTag(tag); }

        static bool Contains(string[] list, string tag)
        {
            if (list == null || string.IsNullOrEmpty(tag)) return false;
            foreach (var t in list)
                if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // One object may be built from several colliders (a tree = trunk + canopy). The object is the
        // topmost ancestor that carries the same tag, so tag the root of a multi-part object too.
        static Transform IdentityOf(Collider col)
        {
            Transform t = col.transform;
            string tag = col.tag;
            while (t.parent != null && t.parent.CompareTag(tag)) t = t.parent;
            return t;
        }

        static void Record(Dictionary<Transform, Sample> samples, Collider col, float yaw, float distance)
        {
            var id = IdentityOf(col);
            if (!samples.TryGetValue(id, out var s))
            {
                s = new Sample { tag = col.tag };
                samples[id] = s;
            }
            if (distance < s.minDistance) s.minDistance = distance;
            s.yawSum += yaw;
            s.hits++;
        }
    }
}
