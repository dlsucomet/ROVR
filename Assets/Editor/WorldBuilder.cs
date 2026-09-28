// Assets/Editor/WorldBuilder.cs
// Menu: Tools > ROVR > Generate All Three Worlds
// Builds House, Plain, and Maze into one scene. Each sits 200m from the others —
// far past any raycast, navmesh, or visual range, so there's no way to walk,
// see, or path from one into another. Re-running replaces the existing worlds.
using UnityEngine;
using UnityEditor;

public static class WorldBuilder
{
    [MenuItem("Tools/ROVR/Generate All Three Worlds")]
    static void GenerateAll()
    {
        HouseGenerator.Generate(new Vector3(0, 0, 0));
        PlainGenerator.Generate(new Vector3(200, 0, 0));
        MazeGenerator.Generate(new Vector3(0, 0, 200));
    }
}
