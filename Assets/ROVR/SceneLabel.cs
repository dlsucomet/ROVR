// Assets/ROVR/SceneLabel.cs
// Names an object for the LLM: "sofa", "rock", "tree". When a scan ray hits any part of an object,
// the nearest SceneLabel above that part (on it or a parent) gives the name, so put it on the root of
// a multi-part object. The world generators add these. Objects without one are named by their tag
// (Wall, Door, Chair, Tree), or not at all (floors, ceilings, grass); solid ones still block the view.
using System.Collections.Generic;
using UnityEngine;

namespace ROVR
{
    public class SceneLabel : MonoBehaviour
    {
        [Tooltip("What the LLM calls this object, lower case and singular: sofa, rock, tree.")]
        public string label;

        // Every enabled label, for the scan's per-object visibility check.
        public static readonly List<SceneLabel> All = new List<SceneLabel>();

        Bounds? bounds;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        // World-space bounds of what can be seen of the object. Measured once: the worlds don't move.
        public Bounds Bounds
        {
            get
            {
                if (bounds == null) bounds = Measure();
                return bounds.Value;
            }
        }

        Bounds Measure()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                Bounds b = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);
                return b;
            }
            return new Bounds(transform.position, Vector3.zero);
        }
    }
}
