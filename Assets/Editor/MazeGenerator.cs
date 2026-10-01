// Assets/Editor/MazeGenerator.cs
// Menu: Tools > ROVR > Generate Maze
// Builds the ROVR labyrinth world: a fixed 12 x 9 cell maze (4 m cells, 48 x 36 m), walls only —
// no chairs/furniture, since World 2 deliberately excludes semantic objects.
//
// The layout is the ASCII plan below, so every participant walks the identical maze and it can
// be edited by hand. The correct route is 52 cells (~210 m) with 30 dead ends. Outer-wall gaps
// are the entrance (west side, bottom row) and the exit (east side, top row). Start is just
// outside the entrance facing in; the green Goal pad is just outside the exit.
// The plan is drawn in rovr-maze-plan.svg.
//
// LOOK: a hedge maze on a pale gravel floor. The walls and floor use the "ROVR/WorldSpaceTiled"
// shader (Assets/Shaders), which tiles a texture by real-world position, so the texture is not
// stretched along the long stretched-cube walls and there are no seams between wall pieces.
// The two tileable textures are in Assets/Resources/Props/Maze. Which material is used, in order:
//     1. Assets/Resources/Props/MazeWall.mat   / MazeFloor.mat   (drop yours in to override)
//     2. Assets/Resources/Props/WallMaterial.mat / FloorMaterial.mat (the old "all worlds" slots)
//     3. the built-in hedge and gravel (generated once into Props/Generated)
//     4. flat green / sand, if the shader or textures are missing
// To change how big the texture looks, select ROVR_MazeWallTiled / ROVR_MazeFloorTiled in
// Assets/Resources/Props/Generated and edit "Metres per texture repeat".
// The walls carry no tagged decoration on purpose: only `Wall` is visible to the LLM grounding,
// and the maze has no landmarks, so the look must stay uniform (a distinctive wall = a hint).
//
// Needs WorldKit.cs alongside it.
using UnityEngine;
using UnityEditor;

public static class MazeGenerator
{
    const float CellSize = 4f;
    const float Margin = 8f;       // floor extends this far beyond the maze on every side
    const int EntranceRow = 0;     // row 0 is the south-most row

    const string HedgeTexture = "Assets/Resources/Props/Maze/MazeHedge.png";
    const string GroundTexture = "Assets/Resources/Props/Maze/MazeGround.png";
    const float HedgeTileSize = 3f;    // metres per texture repeat on the walls (= wall height)
    const float GroundTileSize = 4f;   // metres per texture repeat on the floor (= one cell)

    // First line is the north edge. Each cell is "+---+" wide / "|   |" tall; a gap in the outer
    // wall (a space where '|' or "---" would be) is an opening.
    static readonly string[] Layout =
    {
        "+---+---+---+---+---+---+---+---+---+---+---+---+",
        "|               |               |   |            ",
        "+---+   +   +---+   +---+---+   +   +---+   +---+",
        "|       |   |   |       |   |   |       |       |",
        "+   +   +---+   +---+   +   +   +   +   +---+   +",
        "|   |   |       |   |       |       |       |   |",
        "+   +   +   +---+   +---+   +---+   +   +---+   +",
        "|   |   |   |               |       |   |   |   |",
        "+   +---+   +---+   +---+---+   +   +   +   +   +",
        "|           |               |   |   |   |       |",
        "+---+---+   +---+   +---+---+---+   +---+   +   +",
        "|       |       |           |       |       |   |",
        "+   +---+---+   +---+   +---+   +---+---+   +---+",
        "|           |       |   |   |           |       |",
        "+   +---+   +---+   +   +   +---+   +---+---+   +",
        "|       |               |           |           |",
        "+   +---+   +   +---+   +   +   +---+   +   +---+",
        "    |       |   |       |   |           |       |",
        "+---+---+---+---+---+---+---+---+---+---+---+---+"
    };

    static int Cols => (Layout[0].Length - 1) / 4;
    static int Rows => (Layout.Length - 1) / 2;

    [MenuItem("Tools/ROVR/Generate Maze")]
    static void Generate() => Generate(Vector3.zero);

    public static void Generate(Vector3 offset)
    {
        WorldKit.EnsureTags("Wall", "Goal");

        var root = WorldKit.NewRoot("Maze");
        float width = Cols * CellSize;
        float depth = Rows * CellSize;

        var floorMat = MazeLook("MazeFloor", "FloorMaterial", "ROVR_MazeFloorTiled", GroundTexture, GroundTileSize, "MazeFloor");
        var wallMat = MazeLook("MazeWall", "WallMaterial", "ROVR_MazeWallTiled", HedgeTexture, HedgeTileSize, "MazeWall");

        var floor = WorldKit.Box("Floor", null,
            new Vector3(width / 2f, -0.05f, depth / 2f),
            new Vector3(width + 2f * Margin, 0.1f, depth + 2f * Margin), root, floorMat);

        var walls = BuildWalls(root, wallMat);
        BuildMarkers(root, width, depth);

        // Nothing in the maze ever moves, so let Unity batch the 65 wall pieces and the floor.
        MarkStatic(floor.transform);
        MarkStatic(walls);

        root.position = offset;
    }

    // Reads the ASCII plan and builds each straight run of wall as one piece.
    static Transform BuildWalls(Transform root, Material wallMat)
    {
        var walls = WorldKit.Group("Walls", root);

        // Horizontal runs, one grid line at a time (zi = 0 is the south edge).
        for (int zi = 0; zi <= Rows; zi++)
        {
            string line = Layout[2 * (Rows - zi)];
            int start = -1;
            for (int c = 0; c <= Cols; c++)
            {
                bool wall = c < Cols && line.Substring(4 * c + 1, 3) == "---";
                if (wall && start < 0) start = c;
                if (!wall && start >= 0)
                {
                    WorldKit.Wall(walls, null, start * CellSize, zi * CellSize, c * CellSize, zi * CellSize, 0f, WorldKit.WallHeight);
                    start = -1;
                }
            }
        }

        // Vertical runs (xi = 0 is the west edge).
        for (int xi = 0; xi <= Cols; xi++)
        {
            int start = -1;
            for (int r = 0; r <= Rows; r++)
            {
                bool wall = r < Rows && Layout[2 * (Rows - 1 - r) + 1][4 * xi] == '|';
                if (wall && start < 0) start = r;
                if (!wall && start >= 0)
                {
                    WorldKit.Wall(walls, null, xi * CellSize, start * CellSize, xi * CellSize, r * CellSize, 0f, WorldKit.WallHeight);
                    start = -1;
                }
            }
        }

        if (wallMat != null)
            foreach (var r in walls.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = wallMat;

        return walls;
    }

    // ---- look: materials, texture import settings, static batching --------------------------

    // Picks the wall / floor material (see the header for the order).
    static Material MazeLook(string custom, string generic, string generatedName, string texturePath, float tileSize, string flatColour)
    {
        var m = Resources.Load<Material>("Props/" + custom);
        if (m != null) return m;

        m = Resources.Load<Material>("Props/" + generic);
        if (m != null) return m;

        m = TiledMaterial(generatedName, texturePath, tileSize);
        return m != null ? m : WorldKit.Mat(flatColour);
    }

    // Creates (once) a material that uses the world-space shader with the given texture.
    static Material TiledMaterial(string assetName, string texturePath, float tileSize)
    {
        string path = "Assets/Resources/Props/Generated/" + assetName + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("ROVR/WorldSpaceTiled");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (shader == null || tex == null)
        {
            Debug.LogWarning("[ROVR Maze] Missing " + (shader == null ? "shader ROVR/WorldSpaceTiled" : "texture " + texturePath)
                             + ", so the maze uses flat colours. Copy Assets/Shaders and Assets/Resources/Props/Maze into the project.");
            return null;
        }

        PrepareTexture(texturePath);
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Props");
        EnsureFolder("Assets/Resources/Props", "Generated");

        var mat = new Material(shader);
        mat.mainTexture = tex;
        mat.SetFloat("_TileSize", tileSize);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    // Tiling textures need Repeat wrapping, mipmaps (no shimmer at distance) and anisotropic
    // filtering (so long corridors don't go blurry at a grazing angle).
    static void PrepareTexture(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        bool changed = false;
        if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; changed = true; }
        if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
        if (importer.anisoLevel < 8) { importer.anisoLevel = 8; changed = true; }
        if (changed) importer.SaveAndReimport();
    }

    static void MarkStatic(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
    }

    static void BuildMarkers(Transform root, float width, float depth)
    {
        // Start: 3 m outside the entrance, facing east into the maze.
        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(-3f, 0f, (EntranceRow + 0.5f) * CellSize);
        start.localRotation = Quaternion.Euler(0f, 90f, 0f);
        WorldKit.Box("Start Pad", null, new Vector3(0f, 0.03f, 0f), new Vector3(2f, 0.05f, 2f), start, WorldKit.Mat("Start"))
            .GetComponent<Collider>().enabled = false;

        // Goal: visible pad just outside the exit; the trigger detects task completion.
        var goal = new GameObject("Goal");
        goal.tag = "Goal";
        goal.transform.SetParent(root, false);
        goal.transform.localPosition = new Vector3(width + 3f, 0f, depth - CellSize / 2f);

        var box = goal.AddComponent<BoxCollider>();
        box.size = new Vector3(CellSize, WorldKit.WallHeight, CellSize);
        box.center = new Vector3(0f, WorldKit.WallHeight / 2f, 0f);
        box.isTrigger = true;

        WorldKit.Box("Goal Pad", null, new Vector3(0f, 0.03f, 0f), new Vector3(CellSize, 0.05f, CellSize), goal.transform, WorldKit.Mat("Goal"))
            .GetComponent<Collider>().enabled = false;
    }
}
