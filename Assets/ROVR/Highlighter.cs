// Assets/ROVR/Highlighter.cs
// Makes objects glow while the system asks "which one?", so the user can see the options. The one the
// user is looking at (nearest the middle of the view) glows brighter: that's the one "that one" picks.
// Listens to the SemanticIntentResolver on the same object; the glow is an extra material drawn over
// the object's own (Resources/ROVR/Highlight.shader), removed again when the question is answered.
using System.Collections.Generic;
using UnityEngine;

namespace ROVR
{
    public class Highlighter : MonoBehaviour
    {
        [SerializeField] SemanticIntentResolver resolver;
        [SerializeField] Color glow = new Color(1f, 0.8f, 0.2f, 0.45f);
        [SerializeField] Color lookedAtGlow = new Color(1f, 0.9f, 0.4f, 0.9f);

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
        }

        void Update()
        {
            if (targets.Count == 0) return;

            Transform looked = resolver != null ? resolver.LookedAt(targets) : null;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            foreach (var pair in original)
            {
                if (pair.Key == null) continue;
                bool isLooked = looked != null && pair.Key.transform.IsChildOf(looked);
                Color c = isLooked ? lookedAtGlow : glow;
                c.a *= pulse;
                pair.Key.GetPropertyBlock(block);
                block.SetColor(GlowColor, c);
                pair.Key.SetPropertyBlock(block);
            }
        }
    }
}
