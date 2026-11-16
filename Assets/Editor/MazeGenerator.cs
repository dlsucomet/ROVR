// Assets/Editor/MazeGenerator.cs
// Menu: Tools > ROVR > Generate Maze
// Builds the ROVR labyrinth world: a fixed 8 x 6 cell maze (5 m cells, 40 x 30 m). Made easier after
// Miguel's test session (it was 12 x 9 cells of 4 m, a 52-cell route with 30 dead ends):
//
//   - One clear route of 19 cells (~95 m) and only three short dead ends; cells off the route are solid
//     hedge ("###" in the plan), so there are no confusing open pockets or extra junctions.
//   - 5 m cells: corridors about 4.8 m wide, room to turn and sidestep without brushing the hedges.
//   - The first stretch teaches the moves in order: straight ahead to the red flag (forward), turn left
//     there (turning), then at the blue lamp the corridor jogs one cell to the right (sidestepping: "take
//     five steps right"), then on north and west to the white statue and the long straight to the exit.
//   - Landmarks at those three points (named, so ROVR can be told "go to the red flag"), a blue start
//     pad, and the green exit pad (the study marks it with a yellow beacon).
//
// The layout is the ASCII plan below, so every participant walks the identical maze and it can be edited
// by hand. Outer-wall gaps are the entrance (west side, bottom row) and the exit (east side, top row).
// Start is just outside the entrance facing in; the green Goal pad is just outside the exit.

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
// The hedges carry no decoration: the landmarks are the only distinctive things, on purpose.
//
// Needs WorldKit.cs alongside it.
using UnityEngine;
using UnityEditor;

public static class MazeGenerator
{
    const float CellSize = MazeTrail.CellSize; // 5 m; the trail's route search uses the same
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
        "+---+---+---+---+---+---+---+---+",
        "|###|                            ",
        "+---+   +---+---+---+---+   +---+",
        "|###|               |###|       |",
        "+---+---+---+---+   +---+---+---+",
        "|###|###|###|###|           |###|",
        "+---+---+---+---+   +---+---+---+",
        "|###|               |###|###|###|",
        "+---+---+---+   +---+---+---+---+",
        "|###|###|###|   |###|###|###|###|",
        "+---+---+---+   +---+---+---+---+",
        "                |###|###|###|###|",
        "+---+---+---+---+---+---+---+---+"
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
        BuildLandmarks(root);

        // Nothing in the maze ever moves, so let Unity batch the 65 wall pieces and the floor.
        MarkStatic(floor.transform);
        MarkStatic(walls);

        root.position = offset;
        WorldKit.LinkTeleportDestination("mazeDestination", root);
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

        // Cells off the route are solid hedge.
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (Layout[2 * (Rows - 1 - r) + 1].Substring(4 * c + 1, 3) == "###")
                    WorldKit.Box("Solid Hedge", "Wall", new Vector3((c + 0.5f) * CellSize, WorldKit.WallHeight / 2f, (r + 0.5f) * CellSize),
                                 new Vector3(CellSize, WorldKit.WallHeight, CellSize), walls);

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

    // Three named landmarks where the route teaches a move: the red flag where the first straight ends (turn
    // left), the blue lamp where the corridor jogs right (sidestep), the white statue where it turns north
    // toward the last straight. Solid, so they're walked round, not through, and tagged nothing but named.
    static void BuildLandmarks(Transform root)
    {
        var group = WorldKit.Group("Landmarks", root);

        // Red flag: against the east hedge of cell (3, 0), straight ahead from the start.
        var flag = new GameObject("Red Flag").transform;
        flag.SetParent(group, false);
        flag.localPosition = new Vector3(4f * CellSize - 0.6f, 0f, 0.5f * CellSize);
        WorldKit.Prim(PrimitiveType.Cylinder, "Pole", null, new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), flag, WorldKit.Mat("Steel"));
        WorldKit.Box("Cloth", null, new Vector3(0f, 2.75f, 0.55f), new Vector3(0.05f, 0.7f, 1f), flag, WorldKit.Mat("LandmarkRed"));
        WorldKit.Label(flag.gameObject, "red flag");

        // Blue lamp: against the north hedge of cell (3, 2), ahead when walking north; the way on is to the right.
        var lamp = new GameObject("Blue Lamp").transform;
        lamp.SetParent(group, false);
        lamp.localPosition = new Vector3(3.5f * CellSize, 0f, 3f * CellSize - 0.6f);
        WorldKit.Prim(PrimitiveType.Cylinder, "Post", null, new Vector3(0f, 1.3f, 0f), new Vector3(0.15f, 1.3f, 0.15f), lamp, WorldKit.Mat("Dark"));
        WorldKit.Prim(PrimitiveType.Sphere, "Light", null, new Vector3(0f, 2.8f, 0f), new Vector3(0.6f, 0.6f, 0.6f), lamp, WorldKit.Mat("LandmarkBlue"));
        WorldKit.Label(lamp.gameObject, "blue lamp");

        // White statue: against the west hedge of cell (1, 4), ahead when walking west along row 4.
        var statue = new GameObject("White Statue").transform;
        statue.SetParent(group, false);
        statue.localPosition = new Vector3(1f * CellSize + 0.8f, 0f, 4.5f * CellSize);
        WorldKit.Box("Plinth", null, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.8f, 0.9f), statue, WorldKit.Mat("Dark"));
        WorldKit.Prim(PrimitiveType.Capsule, "Figure", null, new Vector3(0f, 1.55f, 0f), new Vector3(0.6f, 0.75f, 0.6f), statue, WorldKit.Mat("White"));
        WorldKit.Prim(PrimitiveType.Sphere, "Head", null, new Vector3(0f, 2.5f, 0f), new Vector3(0.4f, 0.4f, 0.4f), statue, WorldKit.Mat("White"));
        WorldKit.Label(statue.gameObject, "statue");

        MarkStatic(group);
    }

    static void BuildMarkers(Transform root, float width, float depth)
    {
        // Start: 3 m outside the entrance, facing east into the maze, down the first straight to the red flag.
        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(-3f, 0f, (EntranceRow + 0.5f) * CellSize);
        start.localRotation = Quaternion.Euler(0f, 90f, 0f);
        WorldKit.Box("Start Pad", null, new Vector3(0f, 0.03f, 0f), new Vector3(3f, 0.05f, 3f), start, WorldKit.Mat("Start"))
            .GetComponent<Collider>().enabled = false;

        // Goal: visible pad just outside the exit; the trigger detects task completion.
        var goal = new GameObject("Goal");
        goal.tag = "Goal";
        WorldKit.Label(goal, "exit");
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
