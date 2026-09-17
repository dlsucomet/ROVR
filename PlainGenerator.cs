// Assets/Editor/PlainGenerator.cs
// Menu: Tools > ROVR > Generate Plain
// Builds the ROVR plain world: a flat, barrier-free control environment with no
// walls, doors, or objects — isolates baseline locomotive comfort (Ch. 3.5).
using UnityEngine;
using UnityEditor;

public static class PlainGenerator
{
    const float Width = 40f;
    const float Depth = 40f;

    [MenuItem("Tools/ROVR/Generate Plain")]
    static void Generate() => Generate(Vector3.zero);

    public static void Generate(Vector3 offset)
    {
        var root = new GameObject("Plain").transform;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(root);
        floor.transform.localScale = new Vector3(Width, 0.1f, Depth);
        floor.transform.position = new Vector3(Width / 2f, -0.05f, Depth / 2f);

        var floorMat = Resources.Load<Material>("Props/FloorMaterial"); // same convention as the house
        if (floorMat != null) floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        var start = new GameObject("Start");
        start.transform.SetParent(root);
        start.transform.position = new Vector3(Width / 2f, 0f, Depth / 2f);

        root.position = offset;
    }
}
