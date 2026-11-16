// Assets/ROVR/Highlighter.cs
// Makes objects glow while the system asks "which one?", so the user can see the options. The one the
// user is looking at (nearest the middle of the view) glows brighter: that's the one "that one" picks.
// Listens to the SemanticIntentResolver on the same object; the glow is an extra material drawn over
// the object's own (Resources/ROVR/Highlight.shader), removed again when the question is answered.
//
// Steady on purpose (it flickered in Miguel's test): no pulsing, a cyan that stands out against wood,
// grass and walls, and the "looked at" glow only moves to another option once the gaze has rested on it
// for a moment, so it doesn't jump between two neighbours.
using System.Collections.Generic;
using UnityEngine;

namespace ROVR
{
    public class Highlighter : MonoBehaviour
    {
        [SerializeField] SemanticIntentResolver resolver;
        [SerializeField] Color glow = new Color(0.1f, 0.85f, 1f, 0.45f);
        [SerializeField] Color lookedAtGlow = new Color(0.85f, 1f, 1f, 0.85f);
        [Tooltip("Seconds the gaze must rest on another option before the bright glow moves to it.")]
        [SerializeField] float lookSettleSeconds = 0.25f;

        Transform looked, candidate;
        float candidateSince;

        static readonly int GlowColor = Shader.PropertyToID("_GlowColor");

        Material material;
        readonly Dictionary<Renderer, Material[]> original = new Dictionary<Renderer, Material[]>();
        readonly List<Transform> targets = new List<Transform>();
        MaterialPropertyBlock block;

        void Awake()
        {
            if (resolver == null) resolver = GetComponent<SemanticIntentResolver>();
            var shader = Resources.Load<Shader>("ROVR/Highlight");
            if (shader != null) material = new Material(shader);
            else Debug.LogWarning("[ROVR] Highlight shader missing (Assets/Resources/ROVR/Highlight.shader): options won't glow.");
            block = new MaterialPropertyBlock();
        }

        void OnEnable() { if (resolver != null) resolver.OnChoicesChanged += Show; }
        void OnDisable() { if (resolver != null) resolver.OnChoicesChanged -= Show; Clear(); }

        // Glows these objects; an empty list clears the glow.
        public void Show(IList<Transform> objects)
        {
            Clear();
            if (material == null || objects == null) return;

            foreach (var t in objects)
            {
                if (t == null) continue;
                targets.Add(t);
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (original.ContainsKey(r)) continue;
                    var mats = r.sharedMaterials;
                    original[r] = mats;
                    var withGlow = new Material[mats.Length + 1];
                    mats.CopyTo(withGlow, 0);
                    withGlow[mats.Length] = material;
                    r.sharedMaterials = withGlow;
                }
            }
        }

        public void Clear()
        {
            foreach (var pair in original)
                if (pair.Key != null) { pair.Key.sharedMaterials = pair.Value; pair.Key.SetPropertyBlock(null); }
            original.Clear();
            targets.Clear();
            looked = candidate = null;
        }

        void Update()
        {
            if (targets.Count == 0) return;

            Transform now = resolver != null ? resolver.LookedAt(targets) : null;
            if (now != candidate) { candidate = now; candidateSince = Time.time; }
            if (candidate != looked && Time.time - candidateSince >= lookSettleSeconds) looked = candidate;
            foreach (var pair in original)
            {
                if (pair.Key == null) continue;
                bool isLooked = looked != null && pair.Key.transform.IsChildOf(looked);
                Color c = isLooked ? lookedAtGlow : glow;
                pair.Key.GetPropertyBlock(block);
                block.SetColor(GlowColor, c);
                pair.Key.SetPropertyBlock(block);
            }
        }
    }
}
