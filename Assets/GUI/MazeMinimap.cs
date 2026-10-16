using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// A minimap of the Maze in the lower-right corner of the view: the whole maze, an arrow for the
// player and a marker for the exit. Shown only while the player is in the Maze.
// It turns with the player's head so "up" on the map is roughly where they face, but damped: it
// eases round over about a second instead of spinning with every glance, so the maze's layout stays
// readable. The map is drawn from the Maze's own wall colliders when Play starts, so it always
// matches the generated maze. Drawn on top of everything, so standing next to a hedge doesn't hide it.
public class MazeMinimap : MonoBehaviour
{
    [Tooltip("The maze's root object (found by the name \"Maze\" if empty).")]
    public Transform maze;
    [Tooltip("The player (found from the VR Control Panel if empty).")]
    public Transform player;

    [Header("Look")]
    [Tooltip("Seconds the map takes to catch up with where the player looks. Higher = steadier.")]
    public float turnSmoothing = 0.6f;
    [Tooltip("Width of the minimap in metres, at its distance from the eyes.")]
    public float size = 0.16f;
    [Tooltip("Where it sits relative to the eyes: right, down, forward (metres).")]
    public Vector3 offset = new Vector3(0.2f, -0.12f, 0.45f);

    const int FramePixels = 220;   // the minimap's canvas size in UI pixels
    const float PixelsPerMetre = 8f; // resolution of the drawn map texture
    const float Margin = 3f;       // metres of floor drawn around the maze
    const float ShowWithin = 6f;   // the map shows when the player is this close to the maze

    static readonly Color Floor = new Color(0.08f, 0.1f, 0.08f, 0.75f);
    static readonly Color Hedge = new Color(0.55f, 0.85f, 0.45f, 1f);
    static readonly Color Exit = new Color(1f, 0.85f, 0.2f, 1f);

    Transform head;
    GameObject canvasObject;
    RectTransform mapPivot, arrow;
    Rect area;            // the mapped part of the world (x, z), in metres
    float mapPixelsPerMetre;
    float mapYaw;
    bool built;

    public bool IsShowing => canvasObject != null && canvasObject.activeSelf;
    public float MapYaw => mapYaw;

    void Start()
    {
        if (maze == null) { var m = GameObject.Find("Maze"); if (m != null) maze = m.transform; }
        if (player == null) { var panel = FindAnyObjectByType<VRControlPanel>(); if (panel != null) player = panel.playerBody; }
        head = Camera.main != null ? Camera.main.transform : null;
        if (maze == null || player == null || head == null) { enabled = false; return; }
        built = Build();
        if (!built) enabled = false;
    }

    bool Build()
    {
        // The maze's walls, as seen from above.
        var walls = maze.Find("Walls");
        var colliders = (walls != null ? walls : maze).GetComponentsInChildren<Collider>();
        Bounds all = default;
        bool any = false;
        foreach (var c in colliders)
        {
            if (c.isTrigger || !c.CompareTag("Wall")) continue;
            if (!any) { all = c.bounds; any = true; } else all.Encapsulate(c.bounds);
        }
        if (!any) { Debug.LogWarning("[ROVR] Maze minimap: no walls found under " + maze.name + "."); return false; }

        Transform exit = null;
        foreach (var t in maze.GetComponentsInChildren<Transform>())
            if (t.CompareTag("Goal")) { exit = t; all.Encapsulate(t.position); break; }

        area = Rect.MinMaxRect(all.min.x - Margin, all.min.z - Margin, all.max.x + Margin, all.max.z + Margin);
        int w = Mathf.CeilToInt(area.width * PixelsPerMetre), h = Mathf.CeilToInt(area.height * PixelsPerMetre);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Floor;
        foreach (var c in colliders)
            if (!c.isTrigger && c.CompareTag("Wall")) Fill(pixels, w, h, c.bounds, Hedge, 0.25f);
        if (exit != null) Fill(pixels, w, h, new Bounds(exit.position, new Vector3(4f, 0f, 4f)), Exit, 0f);
        tex.SetPixels32(pixels);
        tex.Apply();

        // A world-space canvas fixed to the head, in the corner of the view.
        canvasObject = new GameObject("Maze Minimap", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.SetParent(head, false);
        root.sizeDelta = new Vector2(FramePixels, FramePixels);
        root.localPosition = offset;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * (size / FramePixels);

        var onTop = new Material(Graphic.defaultGraphicMaterial);
        onTop.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        var frame = NewImage<Image>("Frame", root, onTop);
        frame.color = new Color(0f, 0f, 0f, 0.35f);
        Stretch(frame.rectTransform);

        // The whole maze fits in the frame at any angle: scale its diagonal to the frame.
        float diagonal = Mathf.Sqrt(area.width * area.width + area.height * area.height);
        mapPixelsPerMetre = (FramePixels - 12f) / diagonal;

        mapPivot = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
        mapPivot.SetParent(root, false);
        mapPivot.sizeDelta = Vector2.zero;

        var map = NewImage<RawImage>("Maze", mapPivot, onTop);
        map.texture = tex;
        map.rectTransform.sizeDelta = new Vector2(area.width, area.height) * mapPixelsPerMetre;

        var you = NewImage<RawImage>("You", mapPivot, onTop);
        you.texture = ArrowTexture();
        you.rectTransform.sizeDelta = new Vector2(16f, 16f);
        you.color = new Color(0.35f, 0.75f, 1f); // blue: stands out against the hedges
        arrow = you.rectTransform;

        mapYaw = HeadYaw();
        return true;
    }

    void LateUpdate()
    {
        if (!built) return;

        Vector3 p = player.position;
        bool inMaze = p.x > area.xMin - ShowWithin && p.x < area.xMax + ShowWithin && p.z > area.yMin - ShowWithin && p.z < area.yMax + ShowWithin;
        if (canvasObject.activeSelf != inMaze)
        {
            canvasObject.SetActive(inMaze);
            mapYaw = HeadYaw(); // appears already facing the way the player looks
        }
        if (!inMaze) return;

        // Ease toward where the head faces; a quick glance barely moves it.
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, turnSmoothing));
        mapYaw = Mathf.LerpAngle(mapYaw, HeadYaw(), k);
        mapPivot.localRotation = Quaternion.Euler(0f, 0f, mapYaw);

        arrow.anchoredPosition = new Vector2(p.x - area.center.x, p.z - area.center.y) * mapPixelsPerMetre;
        arrow.localRotation = Quaternion.Euler(0f, 0f, -HeadYaw());
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    float HeadYaw()
    {
        Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        return f.sqrMagnitude < 1e-6f ? mapYaw : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    void Fill(Color32[] pixels, int w, int h, Bounds b, Color color, float grow)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - grow - area.xMin) * PixelsPerMetre), 0, w - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x + grow - area.xMin) * PixelsPerMetre), 0, w - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - grow - area.yMin) * PixelsPerMetre), 0, h - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z + grow - area.yMin) * PixelsPerMetre), 0, h - 1);
        Color32 c = color;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                pixels[y * w + x] = c;
    }

    static T NewImage<T>(string name, Transform parent, Material material) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(T));
        go.transform.SetParent(parent, false);
        var g = go.GetComponent<T>();
        g.material = material;
        g.raycastTarget = false;
        return g;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // A white arrow pointing up (the way the player faces).
    static Texture2D ArrowTexture()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                float halfWidth = 0.42f * (1f - v);               // a triangle, point at the top
                bool inside = Mathf.Abs(u) <= halfWidth && !(v < 0.3f && Mathf.Abs(u) < 0.16f * (0.3f - v) / 0.3f);
                px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
