// Assets/Editor/PracticeGenerator.cs
// Menu: Tools > ROVR > Generate Practice Area
// PLACEHOLDER practice area, where a study session starts (Study/protocol/01-run-sheet.md, step 2): a 16 x 16 m
// fenced square with a few named things to try commands on ("go to the tree", "turn to the chair"). Not timed.
// Design the real one before user testing.
//
// Like the other worlds it is solid (walls, props with colliders) and its things carry a SceneLabel, so
// the voice system can see them. Its Start is linked to the panel's Practice destination.
using UnityEditor;
using UnityEngine;

public static class PracticeGenerator
{
    const float Size = 16f;
    const float FenceHeight = 1.2f;

    [MenuItem("Tools/ROVR/Generate Practice Area")]
    static void Generate() => Generate(new Vector3(-200f, 0f, 0f));

    public static void Generate(Vector3 offset)
    {
        WorldKit.EnsureTags("Wall");
        var root = WorldKit.NewRoot("Practice");

        WorldKit.Box("Floor", null, new Vector3(Size / 2f, -0.05f, Size / 2f), new Vector3(Size, 0.1f, Size), root, WorldKit.Mat("PlainGrass"));
        var walls = WorldKit.Group("Walls", root);
        var doors = WorldKit.Group("Doors", root);
        WorldKit.Wall(walls, doors, 0f, 0f, Size, 0f, 0f, FenceHeight);
        WorldKit.Wall(walls, doors, Size, 0f, Size, Size, 0f, FenceHeight);
        WorldKit.Wall(walls, doors, 0f, Size, Size, Size, 0f, FenceHeight);
        WorldKit.Wall(walls, doors, 0f, 0f, 0f, Size, 0f, FenceHeight);

        var things = WorldKit.Group("Things", root);
        Place(things, "Props/Nature/tree_default", "Tree", "tree", 8f, 12f, 5f, 0f);
        Place(things, "Props/Nature/rock_largeA", "Rock", "rock", 3.5f, 8f, 0.8f, 30f);
        Place(things, "Props/chair", "Chair", "chair", 11.5f, 8.5f, 0.9f, 270f);
        Place(things, "Props/Nature/plant_bush", "Plant", "plant", 4f, 13.5f, 1f, 0f);

        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(Size / 2f, 0f, 2f); // facing north, toward the tree

        root.position = offset;
        WorldKit.LinkTeleportDestination("practiceDestination", root);
    }

    // A model, `height` metres tall, standing on the floor at (x, z), solid, and named for the voice system.
    static void Place(Transform parent, string path, string name, string label, float x, float z, float height, float yaw)
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("[ROVR Practice] No model at Resources/" + path + "; " + name + " is a box instead.");
            WorldKit.Label(WorldKit.Box(name, "Wall", new Vector3(x, height / 2f, z), new Vector3(0.6f, height, 0.6f), parent, WorldKit.Mat("Trim")), label);
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float native = Bounds(go).size.y;
        if (native > 0.0001f) go.transform.localScale = Vector3.one * (height / native);
        Bounds b = Bounds(go);
        go.transform.localPosition += new Vector3(x - b.center.x, -b.min.y, z - b.center.z);

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        WorldKit.Label(go, label);
    }

    static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
