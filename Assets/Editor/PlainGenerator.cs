// Assets/Editor/PlainGenerator.cs
// Menu: Tools > ROVR > Generate Plain
// Builds the ROVR plain world: a flat, barrier-free 100 x 100 m field with no walls, doors, or
// furniture — isolates baseline locomotive comfort (Ch. 3.5) — plus a single tree as the only
// point of reference, 18 m directly ahead of the Start point.
//
// The tree is tagged "Tree" so ROVR's raycast grounding can name it. Set TreeTag to null to leave
// it untagged (a purely visual landmark the LLM can't reference).
//
// Needs WorldKit.cs alongside it.
using UnityEngine;
using UnityEditor;

public static class PlainGenerator
{
    const float Width = 100f;
    const float Depth = 100f;
    const float TreeDistance = 18f;
    const string TreeTag = "Tree";

    [MenuItem("Tools/ROVR/Generate Plain")]
    static void Generate() => Generate(Vector3.zero);

    public static void Generate(Vector3 offset)
    {
        if (TreeTag != null) WorldKit.EnsureTags(TreeTag);

        var root = WorldKit.NewRoot("Plain");

        var floorMat = Resources.Load<Material>("Props/FloorMaterial"); // same convention as the house
        WorldKit.Box("Floor", null, new Vector3(Width / 2f, -0.05f, Depth / 2f), new Vector3(Width, 0.1f, Depth), root, floorMat);

        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(Width / 2f, 0f, Depth / 2f); // facing +Z, toward the tree

        BuildTree(root, new Vector3(Width / 2f, 0f, Depth / 2f + TreeDistance));

        root.position = offset;
    }

    static void BuildTree(Transform root, Vector3 position)
    {
        var tree = new GameObject("Tree").transform;
        tree.SetParent(root, false);
        tree.localPosition = position;
        if (TreeTag != null) tree.gameObject.tag = TreeTag; // tag the root too, so trunk + canopy read as one object

        var trunk = WorldKit.Mat("Trunk");
        var leaves = WorldKit.Mat("Leaves");

        // Cylinder primitive is 2 m tall at scale 1, so scale.y = 2 gives a 4 m trunk.
        WorldKit.Prim(PrimitiveType.Cylinder, "Trunk", TreeTag, new Vector3(0f, 2f, 0f), new Vector3(0.8f, 2f, 0.8f), tree, trunk);
        WorldKit.Prim(PrimitiveType.Sphere, "Canopy", TreeTag, new Vector3(0f, 6.5f, 0f), new Vector3(7f, 7f, 7f), tree, leaves);
        WorldKit.Prim(PrimitiveType.Sphere, "Canopy Side A", TreeTag, new Vector3(1.6f, 8.2f, 0.6f), new Vector3(4.5f, 4.5f, 4.5f), tree, leaves);
        WorldKit.Prim(PrimitiveType.Sphere, "Canopy Side B", TreeTag, new Vector3(-1.5f, 7.6f, -0.9f), new Vector3(4f, 4f, 4f), tree, leaves);
    }
}
