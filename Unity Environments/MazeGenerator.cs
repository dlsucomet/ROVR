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
// Needs WorldKit.cs alongside it.
using UnityEngine;
using UnityEditor;

public static class MazeGenerator
{
    const float CellSize = 4f;
    const float Margin = 8f;       // floor extends this far beyond the maze on every side
    const int EntranceRow = 0;     // row 0 is the south-most row

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

        var floorMat = Resources.Load<Material>("Props/FloorMaterial");
        WorldKit.Box("Floor", null,
            new Vector3(width / 2f, -0.05f, depth / 2f),
            new Vector3(width + 2f * Margin, 0.1f, depth + 2f * Margin), root, floorMat);

        BuildWalls(root);
        BuildMarkers(root, width, depth);

        root.position = offset;
    }

    // Reads the ASCII plan and builds each straight run of wall as one piece.
    static void BuildWalls(Transform root)
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
