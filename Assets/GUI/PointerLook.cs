// Assets/GUI/PointerLook.cs
// Makes the controllers' pointer rays easy to see and to aim (the XRI Starter Assets rays were thin and
// faint in Miguel's test): a wider line, white when it points at nothing, bright cyan over the panel or an
// object, yellow while the trigger selects; and at the end of the ray a dot, with the name of the object
// it lands on ("sofa", "door") above it, so the user can tell what they're pointing at.
//
// Set up at run time on the rig's curve visuals, so the rig prefab stays as XRI ships it.
using System.Collections.Generic;
using ROVR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

public class PointerLook : MonoBehaviour
{
    [Tooltip("Line width at the controller and at the far end, in metres.")]
    public float startWidth = 0.008f;
    public float endWidth = 0.005f;
    [Tooltip("Size of the dot where the ray lands, in metres at 1 m away (it grows with distance so it stays visible).")]
    public float dotSize = 0.018f;
    [Tooltip("Furthest an object can be and still be named.")]
    public float nameRange = 15f;

    static readonly Color Idle = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color Hit = new Color(0.15f, 0.9f, 1f, 1f);
    static readonly Color Pressed = new Color(1f, 0.85f, 0.2f, 1f);

    class Ray
    {
        public CurveVisualController visual;
        public Transform dot;
        public Renderer dotRenderer;
        public TextMesh name;
    }

    readonly List<Ray> _rays = new List<Ray>();
    Transform _head;
    float _nextLook;

    void Start()
    {
        _head = Camera.main != null ? Camera.main.transform : null;
        foreach (var visual in FindObjectsByType<CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Style(visual);
            _rays.Add(new Ray { visual = visual });
        }
    }

    void Style(CurveVisualController visual)
    {
        Set(visual.noValidHitProperties, Idle, 0.6f);
        Set(visual.hoverHitProperties, Hit, 1f);
        Set(visual.uiHitProperties, Hit, 1f);
        Set(visual.selectHitProperties, Pressed, 1.2f);
        Set(visual.uiPressHitProperties, Pressed, 1.2f);
    }

    void Set(LineProperties p, Color c, float width)
    {
        if (p == null) return;
        p.adjustWidth = true;
        p.startWidth = startWidth * width;
        p.endWidth = endWidth * width;
        p.adjustGradient = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(c.a * 0.6f, 0f), new GradientAlphaKey(c.a, 1f) });
        p.gradient = g;
    }

    void LateUpdate()
    {
        bool look = Time.time >= _nextLook;
        if (look) _nextLook = Time.time + 0.1f;

        foreach (var ray in _rays)
        {
            if (ray.visual == null) continue;
            var line = ray.visual.lineRenderer;
            bool shown = line != null && line.enabled && line.gameObject.activeInHierarchy && line.positionCount >= 2;
            Vector3 start = default, end = default;
            if (shown)
            {
                start = Point(line, 0);
                end = Point(line, line.positionCount - 1);
                // Resting (pointing at nothing), the line is its short resting length: no dot then.
                shown = Vector3.Distance(start, end) > ray.visual.restingVisualLineLength + 0.05f;
            }
            if (ray.dot == null) Make(ray);
            ray.dot.gameObject.SetActive(shown);
            if (!shown) { ray.name.gameObject.SetActive(false); continue; }

            float distance = _head != null ? Vector3.Distance(_head.position, end) : 1f;
            ray.dot.position = end;
            ray.dot.localScale = Vector3.one * dotSize * Mathf.Max(0.5f, distance);
            ray.dotRenderer.material.color = line.endColor.a > 0.01f ? new Color(line.endColor.r, line.endColor.g, line.endColor.b, 1f) : Hit;

            if (look) ray.name.text = NameAt(start, end);
            bool named = ray.name.text.Length > 0 && _head != null;
            ray.name.gameObject.SetActive(named);
            if (named)
            {
                ray.name.transform.position = end + Vector3.up * (0.06f * Mathf.Max(0.5f, distance));
                ray.name.transform.rotation = Quaternion.LookRotation(ray.name.transform.position - _head.position, Vector3.up);
                ray.name.transform.localScale = Vector3.one * 0.012f * Mathf.Max(0.5f, distance);
            }
        }
    }

    static Vector3 Point(LineRenderer line, int i)
    {
        Vector3 p = line.GetPosition(i);
        return line.useWorldSpace ? p : line.transform.TransformPoint(p);
    }

    // The label of the object at the end of the ray, or "".
    string NameAt(Vector3 start, Vector3 end)
    {
        Vector3 dir = end - start;
        float length = dir.magnitude;
        if (length < 0.01f || length > nameRange) return "";
        if (!Physics.Raycast(start, dir / length, out RaycastHit hit, length + 0.1f, ~0, QueryTriggerInteraction.Ignore)) return "";
        var label = hit.collider.GetComponentInParent<SceneLabel>();
        if (label != null && !string.IsNullOrEmpty(label.label)) return label.label;
        string tag = hit.collider.tag;
        return tag == "Untagged" || tag == "Wall" || tag == "Furniture" ? "" : tag.ToLowerInvariant();
    }

    void Make(Ray ray)
    {
        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = "Pointer Dot";
        Destroy(dot.GetComponent<Collider>());
        var r = dot.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        // The primitive's own material (always in the build); its colour is set each frame.
        ray.dot = dot.transform;
        ray.dotRenderer = r;
        dot.SetActive(false);

        var text = new GameObject("Pointer Name");
        var mesh = text.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            mesh.font = font;
            text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        mesh.anchor = TextAnchor.LowerCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.fontSize = 48;
        mesh.characterSize = 0.5f;
        mesh.color = Color.white;
        mesh.text = "";
        ray.name = mesh;
        text.SetActive(false);
    }
}
