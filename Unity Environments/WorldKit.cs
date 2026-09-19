// Assets/Editor/WorldKit.cs
// Shared helpers for the ROVR world generators (House, Maze, Plain). Not a menu item itself.
// 1 unit = 1 meter.
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public static class WorldKit
{
    public const float WallHeight = 3f;
    public const float WallThickness = 0.2f;
    public const float DoorHeight = 2.2f;

    public struct DoorGap
    {
        public float center, width;
        public DoorGap(float center, float width = 2f) { this.center = center; this.width = width; }
    }

    // Flat colours so rooms and landmarks are readable in blockout form. Materials are created
    // once as assets under Assets/Resources/Props/Generated so they survive saving the scene.
    // Real models/materials in Assets/Resources/Props still take priority where the generators
    // look for them (WallMaterial, FloorMaterial, prefab per prop model).
    static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        { "Fabric", new Color(0.35f, 0.42f, 0.62f) },
        { "Wood",   new Color(0.55f, 0.38f, 0.22f) },
        { "White",  new Color(0.92f, 0.92f, 0.92f) },
        { "Dark",   new Color(0.18f, 0.18f, 0.20f) },
        { "Steel",  new Color(0.70f, 0.72f, 0.75f) },
        { "Linen",  new Color(0.85f, 0.85f, 0.95f) },
        { "Trunk",  new Color(0.40f, 0.26f, 0.13f) },
        { "Leaves", new Color(0.20f, 0.55f, 0.22f) },
        { "Goal",   new Color(0.15f, 0.75f, 0.25f) },
        { "Start",  new Color(0.20f, 0.40f, 0.90f) },
    };

    public static void EnsureTags(params string[] tags)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
        var so = new SerializedObject(asset);
        var tagsProp = so.FindProperty("tags");

        foreach (var tag in tags)
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }

            if (!exists)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            }
        }
        so.ApplyModifiedProperties();
    }

    // Creates the world's root object. If a previous run left one with the same name, it is
    // removed first (undoable), so re-running a generator replaces the world instead of stacking.
    public static Transform NewRoot(string name)
    {
        var old = GameObject.Find(name);
        if (old != null && old.transform.parent == null)
            Undo.DestroyObjectImmediate(old);

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Generate " + name);
        EditorSceneManager.MarkSceneDirty(go.scene);
        return go.transform;
    }

    public static Transform Group(string name, Transform parent)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        return g;
    }

    public static Material Mat(string key)
    {
        Color color;
        if (string.IsNullOrEmpty(key) || !Palette.TryGetValue(key, out color)) return null;

        string path = "Assets/Resources/Props/Generated/ROVR_" + key + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Props");
        EnsureFolder("Assets/Resources/Props", "Generated");

        // Clone whatever the active render pipeline uses for a default primitive, so this works
        // in Built-in, URP and HDRP without naming a shader.
        var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var template = probe.GetComponent<Renderer>().sharedMaterial;
        Object.DestroyImmediate(probe);

        mat = new Material(template);
        mat.color = color;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    public static GameObject Prim(PrimitiveType type, string name, string tag, Vector3 localPos, Vector3 scale, Transform parent, Material mat = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!string.IsNullOrEmpty(tag)) go.tag = tag;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    public static GameObject Box(string name, string tag, Vector3 localCenter, Vector3 size, Transform parent, Material mat = null)
    {
        return Prim(PrimitiveType.Cube, name, tag, localCenter, size, parent, mat);
    }

    // Real prefabs often ship without colliders. If the instance has no solid one, add a box
    // that fits its renderers so it still blocks movement like the blockout version does.
    public static void EnsureCollider(GameObject obj)
    {
        foreach (var c in obj.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger) return;

        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        var scale = obj.transform.lossyScale;
        var box = obj.AddComponent<BoxCollider>();
        box.center = obj.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(bounds.size.x / Mathf.Abs(scale.x), bounds.size.y / Mathf.Abs(scale.y), bounds.size.z / Mathf.Abs(scale.z));
    }

    public static void AddLight(Transform parent, string name, Vector3 localPos, float range)
    {
        var go = new GameObject(name + " Light");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = 1.2f;
        l.shadows = LightShadows.None;
    }

    // Builds one straight, axis-aligned wall run from (x1,z1) to (x2,z2) starting at height y0.
    // Doorways are cut out at the given positions along the run (a `Door` trigger marker fills
    // each gap, with a lintel above it), and the ends of the run are extended by half the wall
    // thickness so corners close cleanly. Pass doors=null when there are no gaps.
    public static void Wall(Transform walls, Transform doors, float x1, float z1, float x2, float z2, float y0, float height, params DoorGap[] gaps)
    {
        bool alongX = Mathf.Approximately(z1, z2);
        float a0 = alongX ? Mathf.Min(x1, x2) : Mathf.Min(z1, z2);
        float a1 = alongX ? Mathf.Max(x1, x2) : Mathf.Max(z1, z2);
        float fixedCoord = alongX ? z1 : x1;
        float half = WallThickness / 2f;
        var mat = Resources.Load<Material>("Props/WallMaterial");

        System.Array.Sort(gaps, (a, b) => a.center.CompareTo(b.center));

        float cursor = a0;
        foreach (var g in gaps)
        {
            float g0 = g.center - g.width / 2f;
            float g1 = g.center + g.width / 2f;

            if (g0 > cursor + 0.001f)
            {
                float start = Mathf.Approximately(cursor, a0) ? cursor - half : cursor;
                WallPiece(walls, alongX, fixedCoord, start, g0, y0, height, mat);
            }
            cursor = g1;

            if (height > DoorHeight + 0.01f)
                WallPiece(walls, alongX, fixedCoord, g0, g1, y0 + DoorHeight, height - DoorHeight, mat);
            AddDoor(doors, alongX, fixedCoord, g.center, g.width, y0);
        }

        if (a1 > cursor + 0.001f)
        {
            float start = Mathf.Approximately(cursor, a0) ? cursor - half : cursor;
            WallPiece(walls, alongX, fixedCoord, start, a1 + half, y0, height, mat);
        }
    }

    static void WallPiece(Transform parent, bool alongX, float fixedCoord, float p0, float p1, float yBottom, float h, Material mat)
    {
        float len = p1 - p0;
        float mid = (p0 + p1) / 2f;
        Vector3 center = alongX ? new Vector3(mid, yBottom + h / 2f, fixedCoord) : new Vector3(fixedCoord, yBottom + h / 2f, mid);
        Vector3 size = alongX ? new Vector3(len, h, WallThickness) : new Vector3(WallThickness, h, len);
        Box("Wall", "Wall", center, size, parent, mat);
    }

    static void AddDoor(Transform doors, bool alongX, float fixedCoord, float center, float width, float y0)
    {
        if (doors == null) return;

        var door = new GameObject("Door");
        door.tag = "Door";
        door.transform.SetParent(doors, false);
        door.transform.localPosition = alongX
            ? new Vector3(center, y0 + DoorHeight / 2f, fixedCoord)
            : new Vector3(fixedCoord, y0 + DoorHeight / 2f, center);

        var box = door.AddComponent<BoxCollider>();
        box.size = alongX
            ? new Vector3(width, DoorHeight, WallThickness)
            : new Vector3(WallThickness, DoorHeight, width);
        box.isTrigger = true; // metadata marker only, doesn't block movement
    }
}
