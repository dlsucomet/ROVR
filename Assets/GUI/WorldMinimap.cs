using System.Collections.Generic;
using ROVR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Minimaps of the Maze and the House (was MazeMinimap). Each shows its world's walls from above, the player
// as an arrow pointing the way they face, and the current objective's spot as a yellow marker; the House's
// map also has its doorways (gaps, in green), its room names and the room the player is in, and the Maze's
// its start pad and exit. A world's map shows while the player is in or near it.
//
// Where it sits: when a world loads, low on the left of the view, about 55 cm away, far enough in from the
// edge not to be cut off and below the line of sight, so it never covers the way ahead. It then stays put
// relative to the player: it travels with every move, and turns with voice / stick turns, so it keeps its
// place in the view; a glance doesn't move it, and only when the head turns well away (past followDegrees)
// does it ease round to the same place again.
//
// Moving it: grab it with a controller (the grip button, pointing at it or reaching it) and let go where
// you want it; or, with hand tracking, pinch it (thumb and index finger) and move. It keeps that place
// (relative to where you face) from then on, until the next world loads it in the lower left again.
//
// "Up" on the map is the way the player faces (headingUp), eased so a glance doesn't spin it; room names
// stay upright. It's drawn from the worlds' own colliders when Play starts, so it always matches them.
// Drawn on top of everything, so standing next to a hedge or a wall doesn't hide it.
public class WorldMinimap : MonoBehaviour
{
    [Tooltip("The player (found from the VR Control Panel if empty).")]
    public Transform player;

    [Header("Look")]
    [Tooltip("Up on the map is the way the player faces (eased). Off: north stays up and the arrow turns.")]
    public bool headingUp = true;
    [Tooltip("Seconds the map takes to catch up with where the player faces. Higher = steadier.")]
    public float turnSmoothing = 0.6f;
    [Tooltip("Width of the minimap in metres.")]
    public float size = 0.24f;
    [Tooltip("Where it appears when a world loads, relative to the eyes and the way they face: right (negative = left), up, forward, in metres.")]
    public Vector3 startOffset = new Vector3(-0.2f, -0.24f, 0.55f);
    [Tooltip("Degrees the head can turn away before the map eases round to its place in front again.")]
    public float followDegrees = 55f;

    const int FramePixels = 240;     // the minimap's canvas size in UI pixels
    const float PixelsPerMetre = 8f; // resolution of the drawn map texture
    const float Margin = 2f;         // metres of floor drawn around a world
    const float ShowWithin = 6f;     // the map shows when the player is this close to the world
    const float PinchGrabDistance = 0.12f;

    static readonly Color Floor = new Color(0.07f, 0.08f, 0.1f, 0.85f);
    static readonly Color Hedge = new Color(0.55f, 0.85f, 0.45f, 1f);
    static readonly Color HouseWall = new Color(0.92f, 0.93f, 0.96f, 1f);
    static readonly Color Furniture = new Color(0.45f, 0.48f, 0.55f, 1f);
    static readonly Color Doorway = new Color(0.35f, 0.95f, 0.5f, 1f);
    static readonly Color Exit = new Color(0.35f, 0.95f, 0.5f, 1f);
    static readonly Color StartPad = new Color(0.4f, 0.65f, 1f, 1f);
    static readonly Color Objective = new Color(1f, 0.85f, 0.2f, 1f);
    static readonly Color You = new Color(0.35f, 0.75f, 1f);
    static readonly Color CurrentRoom = new Color(1f, 1f, 1f, 0.12f);

    class Map
    {
        public string world;
        public Transform root;
        public Rect area;               // the mapped part of the world (x, z), in metres
        public float pixelsPerMetre;
        public GameObject content;      // under mapPivot
        public readonly List<(RectTransform label, string room)> rooms = new List<(RectTransform, string)>();
        public RectTransform roomHighlight;
    }

    readonly List<Map> _maps = new List<Map>();
    Transform _head;
    GameObject _canvasObject;
    RectTransform _mapPivot, _arrow, _goal;
    Text _title;
    XRGrabInteractable _grab;
    Collider _collider;
    Map _shown;
    float _mapYaw;
    float _anchorYaw;         // the head yaw the map's place is measured from
    float _lastBodyYaw;
    Vector3 _offset;          // where the map sits relative to the eyes and _anchorYaw
    int _pinchHand = -1;      // 0 left, 1 right while a tracked hand pinches the map
    Vector3 _pinchFrom;
    VRControlPanel _panel;
    StudySession _session;

    public bool IsShowing => _canvasObject != null && _canvasObject.activeSelf;
    public string ShownWorld => _shown != null ? _shown.world : null;
    public float MapYaw => _mapYaw;

    void Start()
    {
        _panel = FindAnyObjectByType<VRControlPanel>();
        if (player == null && _panel != null) player = _panel.playerBody;
        _head = Camera.main != null ? Camera.main.transform : null;
        if (player == null || _head == null) { enabled = false; return; }

        BuildPanel();
        AddMap("Maze");
        AddMap("House");
        if (_maps.Count == 0) { enabled = false; Destroy(_canvasObject); return; }
        _offset = startOffset;
        _lastBodyYaw = player.eulerAngles.y;
        _canvasObject.SetActive(false);
    }

    // ---------------------------------------------------------------- building

    void BuildPanel()
    {
        _canvasObject = new GameObject("World Minimap", typeof(RectTransform), typeof(Canvas));
        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)_canvasObject.transform;
        // Under the player's body: it travels with every move, the body's own collisions, the LLM's view
        // scan and the route planner all leave it out.
        root.SetParent(player, false);
        root.sizeDelta = new Vector2(FramePixels, FramePixels + 26f);
        root.localScale = Vector3.one * (size / FramePixels);

        var onTop = new Material(Graphic.defaultGraphicMaterial);
        onTop.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        onTop.renderQueue = (int)RenderQueue.Overlay;

        var frame = NewImage<Image>("Frame", root, onTop);
        frame.color = new Color(0.07f, 0.08f, 0.1f, 0.7f);
        Stretch(frame.rectTransform);

        // A mask keeps the turning map inside the frame.
        var window = new GameObject("Window", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        window.SetParent(root, false);
        window.anchorMin = window.anchorMax = new Vector2(0.5f, 0f);
        window.pivot = new Vector2(0.5f, 0f);
        window.sizeDelta = new Vector2(FramePixels - 8f, FramePixels - 8f);
        window.anchoredPosition = new Vector2(0f, 4f);

        _mapPivot = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
        _mapPivot.SetParent(window, false);
        _mapPivot.sizeDelta = Vector2.zero;

        _goal = NewImage<RawImage>("Objective", _mapPivot, onTop).rectTransform;
        ((RawImage)_goal.GetComponent<Graphic>()).texture = DiscTexture();
        _goal.GetComponent<Graphic>().color = Objective;
        _goal.sizeDelta = new Vector2(16f, 16f);

        var you = NewImage<RawImage>("You", _mapPivot, onTop);
        you.texture = ArrowTexture();
        you.rectTransform.sizeDelta = new Vector2(18f, 18f);
        you.color = You;
        _arrow = you.rectTransform;

        _title = NewText("Title", root, onTop, 15, TextAnchor.MiddleLeft);
        var tr = _title.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-16f, 24f);
        tr.anchoredPosition = new Vector2(0f, -2f);
        _title.color = new Color(0.85f, 0.88f, 0.93f);

        // Grabbable with the controllers (XRI): a thin box over the panel.
        var box = _canvasObject.AddComponent<BoxCollider>();
        box.size = new Vector3(FramePixels, FramePixels + 26f, 8f);
        _collider = box;
        var body = player.GetComponent<CharacterController>();
        if (body != null) Physics.IgnoreCollision(body, box, true);
        var rb = _canvasObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        _grab = _canvasObject.AddComponent<XRGrabInteractable>();
        _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        _grab.throwOnDetach = false;
        _grab.trackRotation = false; // it always turns to face the eyes
        _grab.useDynamicAttach = true; // held where it was grabbed, not snapped to its middle
        _grab.retainTransformParent = true;
        _grab.selectExited.AddListener(_ => Placed());
    }

    void AddMap(string world)
    {
        var go = GameObject.Find(world);
        if (go == null || go.transform.parent != null) return;
        var root = go.transform;
        bool house = world == "House";

        var colliders = root.GetComponentsInChildren<Collider>();
        Bounds all = default;
        bool any = false;
        foreach (var c in colliders)
        {
            if (c.isTrigger || !c.CompareTag("Wall")) continue;
            if (!any) { all = c.bounds; any = true; } else all.Encapsulate(c.bounds);
        }
        if (!any) { Debug.LogWarning("[ROVR] Minimap: no walls found under " + world + "."); return; }

        var map = new Map { world = world, root = root };
        map.area = Rect.MinMaxRect(all.min.x - Margin, all.min.z - Margin, all.max.x + Margin, all.max.z + Margin);
        int w = Mathf.CeilToInt(map.area.width * PixelsPerMetre), h = Mathf.CeilToInt(map.area.height * PixelsPerMetre);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Floor;

        // Furniture first (faint), walls over it, doorways and pads last.
        if (house)
            foreach (var c in colliders)
                if (!c.isTrigger && (c.CompareTag("Furniture") || c.CompareTag("Chair")) && c.bounds.size.y < 2.6f)
                    Fill(pixels, w, h, map.area, c.bounds, Furniture, 0f);
        foreach (var c in colliders)
            if (!c.isTrigger && c.CompareTag("Wall") && c.bounds.size.y > 0.5f)
                Fill(pixels, w, h, map.area, c.bounds, house ? HouseWall : Hedge, house ? 0.05f : 0.25f);
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t.CompareTag("Door") && t.TryGetComponent(out Collider door) && door.isTrigger)
                Fill(pixels, w, h, map.area, door.bounds, Doorway, 0.15f);
            else if (t.CompareTag("Goal"))
                Fill(pixels, w, h, map.area, new Bounds(t.position, new Vector3(4f, 0f, 4f)), Exit, 0f);
            else if (t.name == "Start Pad")
                Fill(pixels, w, h, map.area, new Bounds(t.position, new Vector3(2.5f, 0f, 2.5f)), StartPad, 0f);
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        // The whole world fits in the window at any angle: its diagonal across the window.
        float diagonal = Mathf.Sqrt(map.area.width * map.area.width + map.area.height * map.area.height);
        map.pixelsPerMetre = (FramePixels - 14f) / (headingUp ? diagonal : Mathf.Max(map.area.width, map.area.height));

        var content = new GameObject(world + " Map", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(_mapPivot, false);
        content.SetAsFirstSibling(); // under the arrow and the marker
        content.sizeDelta = Vector2.zero;
        map.content = content.gameObject;
        var onTop = _arrow.GetComponent<Graphic>().material;

        var image = NewImage<RawImage>("Plan", content, onTop);
        image.texture = tex;
        image.rectTransform.sizeDelta = new Vector2(map.area.width, map.area.height) * map.pixelsPerMetre;

        if (house && HouseRooms.Root == root)
        {
            var highlight = NewImage<Image>("Current Room", content, onTop);
            highlight.color = CurrentRoom;
            map.roomHighlight = highlight.rectTransform;
            map.roomHighlight.SetSiblingIndex(1);
            foreach (var room in HouseRooms.Rooms)
            {
                Vector3 c = root.TransformPoint(new Vector3(room.area.center.x, 0f, room.area.center.y));
                var label = NewText("Room " + room.name, content, onTop, 13, TextAnchor.MiddleCenter);
                label.text = room.name.Replace("bedroom bathroom", "bath").Replace("guest bathroom", "guest bath");
                label.color = new Color(1f, 1f, 1f, 0.9f);
                label.rectTransform.sizeDelta = new Vector2(90f, 16f);
                label.rectTransform.anchoredPosition = MapPoint(map, c);
                map.rooms.Add((label.rectTransform, room.name));
            }
        }
        map.content.SetActive(false);
        _maps.Add(map);
    }

    // ---------------------------------------------------------------- each frame

    void LateUpdate()
    {
        Vector3 p = player.position;
        Map now = null;
        foreach (var m in _maps)
            if (p.x > m.area.xMin - ShowWithin && p.x < m.area.xMax + ShowWithin && p.z > m.area.yMin - ShowWithin && p.z < m.area.yMax + ShowWithin)
            { now = m; break; }

        if (now != _shown)
        {
            if (_shown != null) _shown.content.SetActive(false);
            _shown = now;
            _canvasObject.SetActive(now != null);
            if (now != null)
            {
                now.content.SetActive(true);
                _title.text = now.world == "House" ? "House" : "Maze";
                // A new world: back in the lower left of the view.
                _offset = startOffset;
                _anchorYaw = HeadYaw();
                _mapYaw = HeadYaw();
                _lastBodyYaw = player.eulerAngles.y;
                PlaceNow();
            }
        }
        if (now == null) return;

        // Voice and stick turns turn the whole player: the map's place turns with them, at once.
        float bodyYaw = player.eulerAngles.y;
        float turned = Mathf.DeltaAngle(_lastBodyYaw, bodyYaw);
        _lastBodyYaw = bodyYaw;
        _anchorYaw += turned;
        _mapYaw += turned;

        UpdatePinch();
        bool held = (_grab != null && _grab.isSelected) || _pinchHand >= 0;
        if (!held)
        {
            // The head turned well away (physically): ease round to the same place in front again.
            float off = Mathf.DeltaAngle(_anchorYaw, HeadYaw());
            if (Mathf.Abs(off) > followDegrees) _anchorYaw = Mathf.LerpAngle(_anchorYaw, HeadYaw(), 1f - Mathf.Exp(-Time.deltaTime / 0.35f));
            PlaceNow();
        }
        else FaceEyes();

        // The map: up = where the player faces (eased), or north up.
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, turnSmoothing));
        _mapYaw = Mathf.LerpAngle(_mapYaw, HeadYaw(), k);
        float yaw = headingUp ? _mapYaw : 0f;
        _mapPivot.localRotation = Quaternion.Euler(0f, 0f, yaw);
        // Rotate round the player, so they stay in the middle of the window.
        Vector2 at = MapPoint(now, p);
        _mapPivot.anchoredPosition = headingUp ? -(Vector2)(Quaternion.Euler(0f, 0f, yaw) * at) : Vector2.zero;

        _arrow.anchoredPosition = at;
        _arrow.localRotation = Quaternion.Euler(0f, 0f, -HeadYaw());
        foreach (var (label, _) in now.rooms) label.localRotation = Quaternion.Euler(0f, 0f, -yaw); // names stay upright

        // The current room, and the objective's spot.
        if (now.roomHighlight != null)
        {
            string room = HouseRooms.RoomAt(_head.position);
            bool inRoom = false;
            foreach (var r in HouseRooms.Rooms)
            {
                if (r.name != room) continue;
                Vector3 a = now.root.TransformPoint(new Vector3(r.area.xMin, 0f, r.area.yMin));
                Vector3 b = now.root.TransformPoint(new Vector3(r.area.xMax, 0f, r.area.yMax));
                Vector2 pa = MapPoint(now, a), pb = MapPoint(now, b);
                now.roomHighlight.anchoredPosition = (pa + pb) / 2f;
                now.roomHighlight.sizeDelta = new Vector2(Mathf.Abs(pb.x - pa.x), Mathf.Abs(pb.y - pa.y));
                inRoom = true;
            }
            now.roomHighlight.gameObject.SetActive(inRoom);
            _title.text = room != null ? "House: " + room : "House";
        }
        bool hasGoal = TryObjective(now, out Vector3 goal);
        _goal.gameObject.SetActive(hasGoal);
        if (hasGoal) _goal.anchoredPosition = MapPoint(now, goal);
    }

    // Puts the panel at its place relative to the eyes, facing them.
    void PlaceNow()
    {
        Vector3 at = _head.position + Quaternion.Euler(0f, _anchorYaw, 0f) * _offset;
        _canvasObject.transform.position = at;
        FaceEyes();
    }

    void FaceEyes()
    {
        var t = _canvasObject.transform;
        Vector3 away = t.position - _head.position;
        if (away.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    // Let go: the map keeps this place relative to the eyes (within reach and in front).
    void Placed()
    {
        _anchorYaw = HeadYaw();
        Vector3 local = Quaternion.Euler(0f, -_anchorYaw, 0f) * (_canvasObject.transform.position - _head.position);
        local.z = Mathf.Max(0.25f, local.z);
        float d = local.magnitude;
        if (d > 1.2f) local *= 1.2f / d;
        if (d < 0.3f) local *= 0.3f / Mathf.Max(0.01f, d);
        _offset = local;
    }

    // Pinching the map with a tracked hand moves it with the pinch.
    void UpdatePinch()
    {
        if (_pinchHand >= 0)
        {
            if (VirtualHands.TryGetPinch(_pinchHand == 0, out Vector3 point, out bool pinching) && pinching)
            {
                _canvasObject.transform.position += point - _pinchFrom;
                _pinchFrom = point;
                return;
            }
            _pinchHand = -1;
            Placed();
            return;
        }
        for (int hand = 0; hand < 2; hand++)
        {
            if (!VirtualHands.TryGetPinchStarted(hand == 0, out Vector3 point)) continue;
            if (_collider == null || Vector3.Distance(_collider.ClosestPoint(point), point) > PinchGrabDistance) continue;
            _pinchHand = hand;
            _pinchFrom = point;
            return;
        }
    }

    // The current objective's spot, when it's in this world.
    bool TryObjective(Map map, out Vector3 goal)
    {
        goal = default;
        if (_session == null && _panel != null) _session = _panel.GetComponent<StudySession>();
        var task = _session != null ? _session.CurrentTask : null;
        var tasks = _session != null ? _session.Tasks : null;
        if (task == null || tasks == null || !string.Equals(task.world, map.world, System.StringComparison.OrdinalIgnoreCase)) return false;
        if (task.kind == GoalKind.Face || task.kind == GoalKind.Object) return tasks.TryCentre(task, out goal);
        goal = tasks.GoalOf(task);
        return true;
    }

    void OnDestroy()
    {
        if (_canvasObject != null) Destroy(_canvasObject);
    }

    float HeadYaw()
    {
        Vector3 f = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
        return f.sqrMagnitude < 1e-6f ? _mapYaw : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static Vector2 MapPoint(Map map, Vector3 world)
    {
        return new Vector2(world.x - map.area.center.x, world.z - map.area.center.y) * map.pixelsPerMetre;
    }

    static void Fill(Color32[] pixels, int w, int h, Rect area, Bounds b, Color color, float grow)
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

    static Text NewText(string name, Transform parent, Material material, int fontSize, TextAnchor anchor)
    {
        var t = NewImage<Text>(name, parent, material);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
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

    // A white ring with a dot, for the objective.
    static Texture2D DiscTexture()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                bool inside = r < 0.35f || (r > 0.65f && r < 0.95f);
                px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
