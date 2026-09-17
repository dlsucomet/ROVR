// Assets/Editor/MazeGenerator.cs
// Menu: Tools > ROVR > Generate Maze
// Builds the ROVR labyrinth world: a fixed (seeded) perfect maze, walls only —
// no chairs/furniture, since World 2 deliberately excludes semantic objects.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class MazeGenerator
{
    const int Cols = 7;
    const int Rows = 5;
    const float CellSize = 4f;
    const float WallHeight = 3f;
    const float WallThickness = 0.2f;
    const int Seed = 12345; // fixed so every participant walks the same maze

    struct Wall { public Vector2 a, b; public Wall(float x1, float z1, float x2, float z2) { a = new Vector2(x1, z1); b = new Vector2(x2, z2); } }

    [MenuItem("Tools/ROVR/Generate Maze")]
    static void Generate() => Generate(Vector3.zero);

    public static void Generate(Vector3 offset)
    {
        EnsureTags("Wall", "Goal");

        var root = new GameObject("Maze").transform;
        SpawnWalls(CarveMaze(), root);
        SpawnMarker("Start", new Vector3(CellSize * 0.5f, 0, CellSize * 0.5f), null, root);
        SpawnMarker("Goal", new Vector3(CellSize * (Cols - 0.5f), 0, CellSize * (Rows - 0.5f)), "Goal", root);

        root.position = offset;
    }

    // Recursive backtracker: carves a perfect maze (exactly one path between any two
    // cells), then returns every wall left standing plus the outer perimeter.
    static List<Wall> CarveMaze()
    {
        var rng = new System.Random(Seed);
        var visited = new bool[Cols, Rows];
        var wallEast = new bool[Cols - 1, Rows];   // wall between (x,y) and (x+1,y)
        var wallNorth = new bool[Cols, Rows - 1];  // wall between (x,y) and (x,y+1)
        for (int x = 0; x < Cols - 1; x++) for (int y = 0; y < Rows; y++) wallEast[x, y] = true;
        for (int x = 0; x < Cols; x++) for (int y = 0; y < Rows - 1; y++) wallNorth[x, y] = true;

        var stack = new Stack<Vector2Int>();
        visited[0, 0] = true;
        stack.Push(new Vector2Int(0, 0));

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            int cx = current.x, cy = current.y;

            var options = new List<Vector2Int>();
            if (cx > 0 && !visited[cx - 1, cy]) options.Add(new Vector2Int(cx - 1, cy));
            if (cx < Cols - 1 && !visited[cx + 1, cy]) options.Add(new Vector2Int(cx + 1, cy));
            if (cy > 0 && !visited[cx, cy - 1]) options.Add(new Vector2Int(cx, cy - 1));
            if (cy < Rows - 1 && !visited[cx, cy + 1]) options.Add(new Vector2Int(cx, cy + 1));

            if (options.Count == 0) { stack.Pop(); continue; }

            var next = options[rng.Next(options.Count)];
            if (next.x != cx) wallEast[Mathf.Min(cx, next.x), cy] = false;
            else wallNorth[cx, Mathf.Min(cy, next.y)] = false;

            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        var walls = new List<Wall>
        {
            new Wall(0, 0, Cols * CellSize, 0),
            new Wall(Cols * CellSize, 0, Cols * CellSize, Rows * CellSize),
            new Wall(Cols * CellSize, Rows * CellSize, 0, Rows * CellSize),
            new Wall(0, Rows * CellSize, 0, 0),
        };

        for (int x = 0; x < Cols - 1; x++)
            for (int y = 0; y < Rows; y++)
                if (wallEast[x, y])
                    walls.Add(new Wall((x + 1) * CellSize, y * CellSize, (x + 1) * CellSize, (y + 1) * CellSize));

        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows - 1; y++)
                if (wallNorth[x, y])
                    walls.Add(new Wall(x * CellSize, (y + 1) * CellSize, (x + 1) * CellSize, (y + 1) * CellSize));

        return walls;
    }

    static void SpawnWalls(List<Wall> walls, Transform root)
    {
        var group = new GameObject("Walls").transform;
        group.SetParent(root);

        var wallMat = Resources.Load<Material>("Props/WallMaterial"); // same convention as the house

        foreach (var w in walls)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.tag = "Wall";
            wall.transform.SetParent(group);

            Vector2 mid = (w.a + w.b) / 2f;
            float length = Vector2.Distance(w.a, w.b);
            bool runsAlongX = Mathf.Approximately(w.a.y, w.b.y);

            wall.transform.position = new Vector3(mid.x, WallHeight / 2f, mid.y);
            wall.transform.localScale = runsAlongX
                ? new Vector3(length, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, length);

            if (wallMat != null) wall.GetComponent<Renderer>().sharedMaterial = wallMat;
        }
    }

    static void SpawnMarker(string name, Vector3 pos, string tag, Transform root)
    {
        var marker = new GameObject(name);
        marker.transform.SetParent(root);
        marker.transform.position = pos;

        if (tag != null)
        {
            marker.tag = tag;
            var box = marker.AddComponent<BoxCollider>();
            box.size = new Vector3(CellSize * 0.6f, WallHeight, CellSize * 0.6f);
            box.isTrigger = true; // detects task completion, doesn't block movement
        }
    }

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
