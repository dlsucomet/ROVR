// Assets/ROVR/FOVMetadataGrounding.cs
// The Physics-based Raycast Matrix from Section 6.3.2: builds the POV-scoped metadata index, the list
// of what the user can see right now. Only objects in this list are ever known to the LLM
// (Closed-World Assumption, Section 3.3.2 / 6.2.4).
//
// What counts as "seen" (QA feedback: if it's on screen, the LLM should know about it):
//   - The scan covers exactly the camera's view: a grid of rays through the screen, out to the far
//     clip plane, plus a direct line-of-sight check on every labelled object, so small or distant
//     ones (a far tree, a flower) aren't missed between rays.
//   - Solid colliders block the view, named or not (a sofa hides what's behind it). Trigger colliders
//     (Door markers, the Maze exit) don't, so rooms are visible through doorways.
//   - Objects are named by a SceneLabel ("sofa", "rock", "tree"), or else by their tag (Wall, Door,
//     Chair, Tree). Anything with neither (floors, ceilings, grass) is never listed.
//   - Every object is its own entry with an id that stays the same all session ("tree-3"), so two
//     trees in view are two entries and the user can say which. Names in `collapsedNames` (walls) are
//     merged into one nearest entry: they're everywhere, not a single target.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ROVR
{
    public struct GroundedObject
    {
        public string id;             // this particular object, the same all session: "tree-3"
        public string name;           // what it is: "tree"
        public float distanceMeters;  // from the eyes to the nearest part that can be seen
        public float angleDegrees;    // 0 = straight ahead, negative = left, positive = right
        public Transform target;      // the object itself
    }

    public class FOVMetadataGrounding : MonoBehaviour
    {
        [SerializeField] Transform pov;
        [Tooltip("Rays across and down the screen. Small or far labelled objects are also checked one by one.")]
        [SerializeField] int columns = 40;
        [SerializeField] int rows = 24;
        [Tooltip("Only used when the view has no Camera to take the field of view from.")]
        [SerializeField] float fallbackHorizontalFov = 100f;
        [SerializeField] float fallbackVerticalFov = 80f;
        [SerializeField] LayerMask detectionMask = ~0;
        [Tooltip("Tags that name an object that has no SceneLabel (the tag, in lower case).")]
        [SerializeField] string[] groundedTags = { "Wall", "Door", "Chair", "Tree" };
        [SerializeField] string[] collapsedNames = { "wall" };
        [Tooltip("Longest list given to the LLM; the nearest objects are kept.")]
        [SerializeField] int maxObjects = 30;

        const float AheadDegrees = 12f; // within this of straight ahead counts as "ahead"

        class Sample
        {
            public string name;
            public float minDistance = float.MaxValue;
            public float yawSum;
            public int hits;
        }

        readonly RaycastHit[] hitBuffer = new RaycastHit[256];
        readonly Dictionary<Transform, string> idOf = new Dictionary<Transform, string>();
        readonly Dictionary<string, Transform> objectWithId = new Dictionary<string, Transform>();
        readonly Dictionary<string, int> countOf = new Dictionary<string, int>();
        Camera povCamera;

        public Transform Pov => pov;

        void Reset()
        {
            pov = Camera.main != null ? Camera.main.transform : transform;
        }

        void Awake()
        {
            if (pov == null) pov = Camera.main != null ? Camera.main.transform : transform;
            povCamera = pov.GetComponent<Camera>();
        }

        public List<GroundedObject> ScanFieldOfView()
        {
            var result = new List<GroundedObject>();
            if (pov == null) return result;
            if (povCamera == null) povCamera = pov.GetComponent<Camera>();

            Transform self = pov.root;
            float maxDistance = povCamera != null ? povCamera.farClipPlane : 1000f;
            var samples = new Dictionary<Transform, Sample>();

            // 1. A grid of rays through the screen.
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    Ray ray = ScreenRay((c + 0.5f) / columns, (r + 0.5f) / rows);
                    int n = Physics.RaycastNonAlloc(ray, hitBuffer, maxDistance, detectionMask, QueryTriggerInteraction.Collide);
                    Array.Sort(hitBuffer, 0, n, HitDistanceComparer.Instance);

                    for (int i = 0; i < n; i++)
                    {
                        var col = hitBuffer[i].collider;
                        if (col.transform.root == self) continue;

                        string name = NameOf(col, out Transform identity);
                        if (name != null) Record(samples, identity, name, hitBuffer[i].point, hitBuffer[i].distance);

                        if (!col.isTrigger) break; // solid: nothing behind it is visible from here
                    }
                }
            }

            // 2. Labelled objects the grid missed (small, or far away between two rays): visible if a
            //    line of sight to them reaches them first and they're on screen.
            Plane[] frustum = povCamera != null ? GeometryUtility.CalculateFrustumPlanes(povCamera) : null;
            foreach (var label in SceneLabel.All)
            {
                if (label == null || string.IsNullOrEmpty(label.label) || samples.ContainsKey(label.transform)) continue;
                if (label.transform.root == self) continue;

                Bounds b = label.Bounds;
                if (frustum != null && !GeometryUtility.TestPlanesAABB(frustum, b)) continue;

                foreach (Vector3 point in new[] { b.center, b.center + Vector3.up * b.extents.y * 0.6f })
                {
                    if (!OnScreen(point)) continue;
                    if (CanSee(label.transform, point, maxDistance, self, out RaycastHit hit))
                    {
                        Record(samples, label.transform, label.label.ToLowerInvariant(), hit.point, hit.distance);
                        break;
                    }
                }
            }

            var merged = new Dictionary<string, GroundedObject>();
            foreach (var pair in samples)
            {
                var s = pair.Value;
                var o = new GroundedObject
                {
                    name = s.name,
                    distanceMeters = s.minDistance,
                    angleDegrees = s.yawSum / s.hits,
                    target = pair.Key
                };

                if (!IsCollapsedName(s.name))
                {
                    o.id = IdFor(pair.Key, s.name);
                    result.Add(o);
                    continue;
                }

                o.id = s.name; // one entry for all walls
                if (!merged.TryGetValue(s.name, out var existing) || o.distanceMeters < existing.distanceMeters)
                    merged[s.name] = o;
            }
            result.AddRange(merged.Values);

            result.Sort((a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            if (result.Count > maxObjects) result.RemoveRange(maxObjects, result.Count - maxObjects);
            return result;
        }

        // True when the named object ("tree") or this exact one ("tree-3") is in view right now.
        public bool IsVisible(string nameOrId)
        {
            return TryFindVisible(ScanFieldOfView(), nameOrId, out _);
        }

        // The nearest visible object matching a name or id.
        public bool TryFindVisible(List<GroundedObject> visible, string nameOrId, out GroundedObject found)
        {
            found = default;
            bool any = false;
            foreach (var o in visible)
            {
                if (!Matches(o, nameOrId)) continue;
                if (!any || o.distanceMeters < found.distanceMeters) { found = o; any = true; }
            }
            return any;
        }

        public bool Matches(GroundedObject o, string nameOrId)
        {
            return string.Equals(o.id, nameOrId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.name, nameOrId, StringComparison.OrdinalIgnoreCase);
        }

        // Whether a collider belongs to the object a move is heading for: anything with that name, or
        // that one object if an id is given.
        public bool IsTarget(Collider col, string nameOrId)
        {
            string name = NameOf(col, out Transform identity);
            if (name == null) return false;
            if (string.Equals(name, nameOrId, StringComparison.OrdinalIgnoreCase)) return true;
            return objectWithId.TryGetValue(nameOrId.ToLowerInvariant(), out Transform t) && t == identity;
        }

        // What the object with this id is called ("tree-3" -> "tree"); a plain name is returned as is.
        public string NameFor(string nameOrId)
        {
            if (nameOrId != null && objectWithId.TryGetValue(nameOrId.ToLowerInvariant(), out Transform t) && t != null)
            {
                var label = t.GetComponent<SceneLabel>();
                if (label != null) return label.label;
                return t.tag.ToLowerInvariant();
            }
            return nameOrId;
        }

        // The object with this id, or null.
        public Transform ObjectWithId(string id)
        {
            return id != null && objectWithId.TryGetValue(id.ToLowerInvariant(), out Transform t) ? t : null;
        }

        // The middle of an object as seen: its label's bounds, else its renderers', else its position.
        public static Vector3 CenterOf(Transform t)
        {
            var label = t.GetComponent<SceneLabel>();
            if (label != null) return label.Bounds.center;
            var r = t.GetComponentInChildren<Renderer>();
            if (r != null) return r.bounds.center;
            var c = t.GetComponentInChildren<Collider>();
            return c != null ? c.bounds.center : t.position;
        }

        public bool IsId(string nameOrId)
        {
            return nameOrId != null && objectWithId.ContainsKey(nameOrId.ToLowerInvariant());
        }

        // The list as the LLM sees it. Ids are left out: the engine picks the particular object, since a
        // small model given ids was seen to pick a listed object the user never mentioned.
        public string ToJson(List<GroundedObject> objects, bool withIds = false)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                if (withIds) sb.Append("\"id\":\"").Append(o.id).Append("\",");
                sb.Append("\"name\":\"").Append(o.name).Append("\",")
                  .Append("\"distance_m\":").Append(o.distanceMeters.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"angle_deg\":").Append(o.angleDegrees.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"side\":\"").Append(SideOf(o.angleDegrees)).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string SideOf(float angleDegrees)
        {
            return Mathf.Abs(angleDegrees) < AheadDegrees ? "ahead" : (angleDegrees < 0f ? "left" : "right");
        }

        // Names like "wall" that are merged into one nearest entry: they're everywhere, not a single target.
        public bool IsCollapsedName(string name)
        {
            if (collapsedNames == null || string.IsNullOrEmpty(name)) return false;
            foreach (var n in collapsedNames)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ naming

        // What a collider is part of, and the object that is: the nearest SceneLabel above it, or else
        // the topmost ancestor carrying the same grounded tag. Null when it has no name.
        public string NameOf(Collider col, out Transform identity)
        {
            var label = col.GetComponentInParent<SceneLabel>();
            if (label != null && !string.IsNullOrEmpty(label.label))
            {
                identity = label.transform;
                return label.label.ToLowerInvariant();
            }

            identity = null;
            if (!IsGroundedTag(col.tag)) return null;

            Transform t = col.transform;
            while (t.parent != null && t.parent.CompareTag(col.tag)) t = t.parent;
            identity = t;
            return col.tag.ToLowerInvariant();
        }

        bool IsGroundedTag(string tag)
        {
            if (groundedTags == null || string.IsNullOrEmpty(tag) || tag == "Untagged") return false;
            foreach (var t in groundedTags)
                if (string.Equals(t, tag, StringComparison.Ordinal)) return true;
            return false;
        }

        // Every name an object in this world can have, whether in view or not.
        public HashSet<string> KnownNames()
        {
            var names = new HashSet<string>();
            foreach (var label in SceneLabel.All)
                if (label != null && !string.IsNullOrEmpty(label.label)) names.Add(label.label.ToLowerInvariant());
            if (groundedTags != null)
                foreach (var t in groundedTags) names.Add(t.ToLowerInvariant());
            return names;
        }

        string IdFor(Transform identity, string name)
        {
            if (idOf.TryGetValue(identity, out string id)) return id;
            countOf.TryGetValue(name, out int n);
            countOf[name] = ++n;
            id = name.Replace(' ', '-') + "-" + n;
            idOf[identity] = id;
            objectWithId[id] = identity;
            return id;
        }

        // ------------------------------------------------------------------ geometry

        Ray ScreenRay(float x, float y)
        {
            if (povCamera != null) return povCamera.ViewportPointToRay(new Vector3(x, y, 0f));

            float yaw = (x - 0.5f) * fallbackHorizontalFov;
            float pitch = (0.5f - y) * fallbackVerticalFov;
            Vector3 dir = Quaternion.AngleAxis(yaw, pov.up) * Quaternion.AngleAxis(pitch, pov.right) * pov.forward;
            return new Ray(pov.position, dir);
        }

        bool OnScreen(Vector3 point)
        {
            if (povCamera == null) return true;
            Vector3 v = povCamera.WorldToViewportPoint(point);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        // Whether a line of sight toward `point` reaches `target` before anything solid. Triggers (door
        // markers) don't block it.
        bool CanSee(Transform target, Vector3 point, float maxDistance, Transform self, out RaycastHit targetHit)
        {
            targetHit = default;
            Vector3 origin = pov.position;
            Vector3 to = point - origin;
            float distance = Mathf.Min(maxDistance, to.magnitude + 0.5f);

            int n = Physics.RaycastNonAlloc(origin, to.normalized, hitBuffer, distance, detectionMask, QueryTriggerInteraction.Collide);
            Array.Sort(hitBuffer, 0, n, HitDistanceComparer.Instance);
            for (int i = 0; i < n; i++)
            {
                var col = hitBuffer[i].collider;
                if (col.transform.root == self) continue;

                NameOf(col, out Transform identity);
                if (identity == target) { targetHit = hitBuffer[i]; return true; }
                if (!col.isTrigger) return false;
            }
            return false;
        }

        void Record(Dictionary<Transform, Sample> samples, Transform identity, string name, Vector3 point, float distance)
        {
            if (!samples.TryGetValue(identity, out var s))
            {
                s = new Sample { name = name };
                samples[identity] = s;
            }
            if (distance < s.minDistance) s.minDistance = distance;
            s.yawSum += YawTo(point);
            s.hits++;
        }

        // Horizontal angle from where the user faces to a point: negative = left, positive = right.
        float YawTo(Vector3 point)
        {
            Vector3 forward = Vector3.ProjectOnPlane(pov.forward, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(point - pov.position, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(forward, to, Vector3.up);
        }

        sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); }
        }
    }
}
