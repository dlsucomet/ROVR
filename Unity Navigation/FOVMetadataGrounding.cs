// Assets/Scripts/ROVR/FOVMetadataGrounding.cs
// The Physics-based Raycast Matrix from Section 6.3.2: casts a fan of rays outward from the
// user's HMD forward vector to build the POV-scoped metadata index. Only objects this scan hits
// are ever visible to the LLM (Closed-World Assumption, Section 3.3.2 / 6.2.4) — nothing outside
// the fan or past maxDistance exists as far as the model is concerned, by design.
using System.Collections.Generic;
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
        [SerializeField] float fovAngle = 90f;
        [SerializeField] int rayCount = 9;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] LayerMask detectionMask = ~0;

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
            var hits = new List<GroundedObject>();
            if (pov == null) return hits;

            float half = fovAngle * 0.5f;
            for (int i = 0; i < rayCount; i++)
            {
                float t = rayCount == 1 ? 0.5f : (float)i / (rayCount - 1);
                float angle = Mathf.Lerp(-half, half, t);
                Vector3 dir = Quaternion.AngleAxis(angle, pov.up) * pov.forward;

                if (Physics.Raycast(pov.position, dir, out RaycastHit hit, maxDistance, detectionMask))
                {
                    string tag = hit.collider.tag;
                    if (!string.IsNullOrEmpty(tag) && tag != "Untagged")
                    {
                        hits.Add(new GroundedObject
                        {
                            tag = tag,
                            distanceMeters = hit.distance,
                            angleDegrees = angle
                        });
                    }
                }
            }

            return DeduplicateByClosest(hits);
        }

        // Adjacent rays frequently land on the same object (e.g. a wide wall) — collapse those
        // down to one entry each so the LLM sees a clean object list, not a sampling artifact.
        static List<GroundedObject> DeduplicateByClosest(List<GroundedObject> raw)
        {
            var byTag = new Dictionary<string, GroundedObject>();
            foreach (var obj in raw)
            {
                if (!byTag.TryGetValue(obj.tag, out var existing) || obj.distanceMeters < existing.distanceMeters)
                    byTag[obj.tag] = obj;
            }
            return new List<GroundedObject>(byTag.Values);
        }

        public string ToJson(List<GroundedObject> objects)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"tag\":\"").Append(o.tag).Append("\",")
                  .Append("\"distance_m\":").Append(o.distanceMeters.ToString("F1")).Append(',')
                  .Append("\"angle_deg\":").Append(o.angleDegrees.ToString("F0")).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
