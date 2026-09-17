// Assets/Editor/HouseGenerator.cs
// Menu: Tools > ROVR > Generate House
// Builds the ROVR house world (matches rovr-house-floorplan.svg exactly, 1 unit = 1 meter).
// Drop real models in Assets/Resources/Props/<model>.prefab (Chair, Sofa, Bed, Counter, Sink)
// and a wall material at Assets/Resources/Props/WallMaterial.mat to use them automatically —
// anything missing just falls back to a blockout box, so this keeps working either way.
using UnityEngine;
using UnityEditor;

public static class HouseGenerator
{
    const float WallHeight = 3f;
    const float WallThickness = 0.2f;

    // (x, z) endpoints of a wall segment, in meters
    struct Wall { public Vector2 a, b; public Wall(float x1, float z1, float x2, float z2) { a = new Vector2(x1, z1); b = new Vector2(x2, z2); } }
    struct Door { public Vector3 pos, size; public Door(float x, float z, float sx, float sz) { pos = new Vector3(x, WallHeight / 2f, z); size = new Vector3(sx, WallHeight, sz); } }
    struct Prop { public string name, tag, model; public Vector3 pos, size; }

    [MenuItem("Tools/ROVR/Generate House")]
    static void Generate() => Generate(Vector3.zero);

    // offset shifts the whole world after it's built, so it can sit anywhere without
    // touching any of the coordinates below.
    public static void Generate(Vector3 offset)
    {
        EnsureTags("Wall", "Door", "Chair", "Furniture");

        var root = new GameObject("House").transform;
        BuildFloor(root);
        BuildWalls(root);
        BuildDoors(root);
        BuildProps(root);

        root.position = offset;
    }

    static void BuildFloor(Transform root)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(root);
        floor.transform.localScale = new Vector3(24f, 0.1f, 20f);
        floor.transform.position = new Vector3(12f, -0.05f, 10f);

        var floorMat = Resources.Load<Material>("Props/FloorMaterial");
        if (floorMat != null) floor.GetComponent<Renderer>().sharedMaterial = floorMat;
    }

    static readonly Wall[] Walls =
    {
        // exterior (gap at 11-13 = entrance)
        new Wall(0, 20, 24, 20),
        new Wall(24, 20, 24, 0),
        new Wall(24, 0, 13, 0),
        new Wall(11, 0, 0, 0),
        new Wall(0, 0, 0, 20),

        // living <-> hallway (gap 3.5-5.5 = door), bathroom <-> hallway (solid)
        new Wall(10, 0, 10, 3.5f),
        new Wall(10, 5.5f, 10, 9),
        new Wall(10, 11, 10, 20),

        // kitchen <-> hallway (gap 3.5-5.5 = door), bedroom <-> hallway (solid)
        new Wall(14, 0, 14, 3.5f),
        new Wall(14, 5.5f, 14, 9),
        new Wall(14, 11, 14, 20),

        // living/kitchen north walls (solid, corridor is on the other side)
        new Wall(0, 9, 10, 9),
        new Wall(14, 9, 24, 9),

        // bathroom south wall (gap 4-6 = door)
        new Wall(0, 11, 4, 11),
        new Wall(6, 11, 10, 11),

        // bedroom south wall (gap 18-20 = door)
        new Wall(14, 11, 18, 11),
        new Wall(20, 11, 24, 11),
    };

    static void BuildWalls(Transform root)
    {
        var group = new GameObject("Walls").transform;
        group.SetParent(root);

        foreach (var w in Walls)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.tag = "Wall";
            wall.transform.SetParent(group);

            Vector2 mid = (w.a + w.b) / 2f;
            float length = Vector2.Distance(w.a, w.b);
            bool runsAlongX = Mathf.Approximately(w.a.y, w.b.y); // same z -> horizontal in plan

            wall.transform.position = new Vector3(mid.x, WallHeight / 2f, mid.y);
            wall.transform.localScale = runsAlongX
                ? new Vector3(length, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, length);

            var wallMat = Resources.Load<Material>("Props/WallMaterial");
            if (wallMat != null) wall.GetComponent<Renderer>().sharedMaterial = wallMat;
        }
    }

    static readonly Door[] Doors =
    {
        new Door(12, 0,    2f, WallThickness), // entrance
        new Door(10, 4.5f, WallThickness, 2f), // living
        new Door(14, 4.5f, WallThickness, 2f), // kitchen
        new Door(5,  11,   2f, WallThickness), // bathroom
        new Door(19, 11,   2f, WallThickness), // bedroom
    };

    static void BuildDoors(Transform root)
    {
        var group = new GameObject("Doors").transform;
        group.SetParent(root);

        foreach (var d in Doors)
        {
            var door = new GameObject("Door");
            door.tag = "Door";
            door.transform.SetParent(group);
            door.transform.position = d.pos;

            var box = door.AddComponent<BoxCollider>();
            box.size = d.size;
            box.isTrigger = true; // metadata marker only, doesn't block movement
        }
    }

    static readonly Prop[] Props =
    {
        new Prop { name = "Living Chair",  tag = "Chair",     model = "Chair",   pos = new Vector3(2, 0.4f, 1),        size = new Vector3(0.6f, 0.8f, 0.6f) },
        new Prop { name = "Kitchen Chair", tag = "Chair",     model = "Chair",   pos = new Vector3(20, 0.4f, 1),       size = new Vector3(0.6f, 0.8f, 0.6f) },
        new Prop { name = "Bedroom Chair", tag = "Chair",     model = "Chair",   pos = new Vector3(20, 0.4f, 13),      size = new Vector3(0.6f, 0.8f, 0.6f) },
        new Prop { name = "Sofa",          tag = "Furniture", model = "Sofa",    pos = new Vector3(3, 0.4f, 8),        size = new Vector3(4, 0.8f, 1) },
        new Prop { name = "Counter",       tag = "Furniture", model = "Counter", pos = new Vector3(18, 0.4f, 8),       size = new Vector3(4, 0.8f, 1) },
        new Prop { name = "Sink",          tag = "Furniture", model = "Sink",    pos = new Vector3(2, 0.4f, 18.5f),    size = new Vector3(2, 0.8f, 1) },
        new Prop { name = "Bed",           tag = "Furniture", model = "Bed",     pos = new Vector3(17.5f, 0.3f, 18),   size = new Vector3(3, 0.6f, 2) },
    };

    static void BuildProps(Transform root)
    {
        var group = new GameObject("Props").transform;
        group.SetParent(root);

        foreach (var p in Props)
        {
            var prefab = Resources.Load<GameObject>($"Props/{p.model}");
            GameObject obj;

            if (prefab != null)
            {
                obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab); // keeps the prefab link
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube); // no model yet — blockout box
                obj.transform.localScale = p.size;
            }

            obj.name = p.name;
            obj.tag = p.tag;
            obj.transform.SetParent(group);
            obj.transform.position = p.pos;
        }
    }

    // Creates any of the given tags that don't already exist in the project.
    static void EnsureTags(params string[] tags)
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
}
