// Assets/Editor/PlainGenerator.cs
// Menu: Tools > ROVR > Generate Plain
// Builds the ROVR plain world: a flat, barrier-free 100 x 100 m field with no walls, doors, or
// furniture — isolates baseline locomotive comfort (Ch. 3.5) — plus a single tree as the only
// point of reference, 18 m directly ahead of the Start point.
//
// The tree is tagged "Tree" so ROVR's raycast grounding can name it. Set TreeTag to null to leave
// it untagged (a purely visual landmark the LLM can't reference).
//
// LOOK: the tree and the scenery use the Kenney Nature Kit (CC0, https://kenney.nl/assets/nature-kit),
// loaded from Assets/Resources/Props/Nature/<name>.fbx. The scenery (rocks, grass tufts, flowers)
// is scattered with a fixed random seed, so every participant gets the identical field. It is
// untagged on purpose: only the one `Tree` is visible to the LLM grounding, so it stays the only
// landmark. Everything is solid (MeshColliders), and two ponds are obstacles to walk round. The area around Start and the line to the tree is
// kept clear, so the tree is always in view straight ahead. Models are sized by target height in
// metres, so the result does not depend on the FBX import scale. A missing model is skipped with a
// warning (the tree falls back to the old cylinder-and-spheres tree).
//
// Needs WorldKit.cs alongside it.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class PlainGenerator
{
    const float Width = 100f;
    const float Depth = 100f;
    const float TreeDistance = 18f;
    const string TreeTag = "Tree";

    // ---- Nature Kit scenery ------------------------------------------------------------------
    const string NatureFolder = "Props/Nature/";
    const string TreeModel = "tree_default";
    const float TreeHeight = 8f;          // metres, tip to ground
    const int ScenerySeed = 20260930;     // fixed, so every build of the plain is identical
    const float StartClearRadius = 6f;    // nothing within this distance of Start
    const float CorridorHalfWidth = 3f;   // keep the view from Start to the tree clear
    const float CorridorPastTree = 4f;
    const float TreeClearRadius = 4f;
    const float EdgeMargin = 3f;

    // Ponds: x, z (field coordinates) and radius in metres. A flat disc of water ringed with
    // stones and lily pads. The disc is solid, so a pond is something to walk around.
    static readonly Vector3[] Ponds = { new Vector3(22f, 72f, 5f), new Vector3(80f, 28f, 4f) };
    const float WaterThickness = 0.08f;
    static readonly string[] PondStones = { "rock_smallA", "rock_smallB", "rock_smallC" };
    static readonly string[] PondLilies = { "lily_large", "lily_small" };

    struct Scatter
    {
        public string group;
        public string[] models;
        public int count;
        public float minHeight, maxHeight;   // metres; picked at random per item
        public float spacing;                // keep-out radius around each item
        public float minStartDistance;       // keep this far from Start (0 = just the clear radius)

        public Scatter(string group, string[] models, int count, float minHeight, float maxHeight, float spacing, float minStartDistance = 0f)
        {
            this.group = group; this.models = models; this.count = count;
            this.minHeight = minHeight; this.maxHeight = maxHeight; this.spacing = spacing;
            this.minStartDistance = minStartDistance;
        }
    }

    static readonly Scatter[] Scenery =
    {
        new Scatter("Rocks", new[] { "rock_largeA", "rock_largeB", "rock_largeC", "stone_largeA", "rock_tallA", "rock_smallA", "rock_smallB", "rock_smallC" }, 18, 0.6f, 1.8f, 2.5f),
        new Scatter("Grass", new[] { "grass", "grass_large", "grass_leafs" }, 79, 0.35f, 0.7f, 1.2f),
        new Scatter("Flowers", new[] { "flower_redA", "flower_redB", "flower_purpleA", "flower_purpleB", "flower_yellowA", "flower_yellowB" }, 40, 0.35f, 0.6f, 1.2f),
        // Background trees: untagged, and kept far from Start, so the one tagged Tree 18 m ahead
        // stays the obvious landmark. Set the count to 0 to remove them.
        new Scatter("Trees", new[] { "tree_oak", "tree_default" }, 6, 6f, 9f, 8f, 35f),
    };

    [MenuItem("Tools/ROVR/Generate Plain")]
    static void Generate() => Generate(Vector3.zero);

    public static void Generate(Vector3 offset)
    {
        if (TreeTag != null) WorldKit.EnsureTags(TreeTag);

        var root = WorldKit.NewRoot("Plain");

        var floorMat = Resources.Load<Material>("Props/FloorMaterial") ?? WorldKit.Mat("PlainGrass"); // FloorMaterial.mat wins if present, like the house; otherwise grass green
        WorldKit.Box("Floor", null, new Vector3(Width / 2f, -0.05f, Depth / 2f), new Vector3(Width, 0.1f, Depth), root, floorMat);

        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(Width / 2f, 0f, Depth / 2f); // facing +Z, toward the tree

        var treePos = new Vector3(Width / 2f, 0f, Depth / 2f + TreeDistance);
        BuildTree(root, treePos);
        BuildScenery(root, start.localPosition, treePos);

        root.position = offset;
        WorldKit.LinkTeleportDestination("plainDestination", root);
    }

    // The one tagged landmark: a Nature Kit tree, or the old primitive tree if the model is missing.
    static void BuildTree(Transform root, Vector3 position)
    {
        var prefab = Resources.Load<GameObject>(NatureFolder + TreeModel);
        if (prefab == null)
        {
            Debug.LogWarning("[ROVR Plain] No model at Assets/Resources/" + NatureFolder + TreeModel
                             + ".fbx, so the plain uses the simple primitive tree. Copy Assets/Resources/Props/Nature into the project.");
            BuildPrimitiveTree(root, position);
            return;
        }

        var tree = PlaceModel(root, prefab, "Tree", position.x, position.z, TreeHeight, 0f);
        TagAll(tree, TreeTag); // every child too, so a ray hitting any part reads "Tree"
    }

    // Rocks, grass and flowers, scattered with a fixed seed. Untagged and solid.
    static void BuildScenery(Transform root, Vector3 start, Vector3 treePos)
    {
        var scenery = WorldKit.Group("Scenery", root);
        var rng = new System.Random(ScenerySeed);
        var taken = new List<Vector3>();   // x, z, keep-out radius
        taken.Add(new Vector3(treePos.x, treePos.z, TreeClearRadius));
        var missing = new List<string>();

        BuildPonds(scenery, rng, taken, missing);

        foreach (var s in Scenery)
        {
            var group = WorldKit.Group(s.group, scenery);
            var prefabs = new List<GameObject>();
            foreach (var m in s.models)
            {
                var prefab = Resources.Load<GameObject>(NatureFolder + m);
                if (prefab != null) prefabs.Add(prefab); else missing.Add(m);
            }
            if (prefabs.Count == 0) continue;

            int placed = 0;
            for (int attempt = 0; attempt < s.count * 40 && placed < s.count; attempt++)
            {
                float x = Mathf.Lerp(EdgeMargin, Width - EdgeMargin, (float)rng.NextDouble());
                float z = Mathf.Lerp(EdgeMargin, Depth - EdgeMargin, (float)rng.NextDouble());
                if (!IsFree(x, z, s.spacing, s.minStartDistance, start, treePos, taken)) continue;

                var prefab = prefabs[rng.Next(prefabs.Count)];
                float height = Mathf.Lerp(s.minHeight, s.maxHeight, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                PlaceModel(group, prefab, (s.group == "Trees" ? "Background Tree" : s.group.TrimEnd('s')) + " " + (placed + 1), x, z, height, yaw);

                taken.Add(new Vector3(x, z, s.spacing));
                placed++;
            }
        }

        if (missing.Count > 0)
            Debug.LogWarning("[ROVR Plain] Skipped missing scenery models in Assets/Resources/" + NatureFolder + ": " + string.Join(", ", missing.ToArray()));
    }

    static bool IsFree(float x, float z, float radius, float minStartDistance, Vector3 start, Vector3 treePos, List<Vector3> taken)
    {
        float awayFromStart = Mathf.Max(StartClearRadius + radius, minStartDistance);
        if (Vector2.Distance(new Vector2(x, z), new Vector2(start.x, start.z)) < awayFromStart) return false;

        // The strip from Start to the tree (and a little past it) stays empty.
        if (Mathf.Abs(x - start.x) < CorridorHalfWidth + radius && z > start.z - radius && z < treePos.z + CorridorPastTree) return false;

        foreach (var t in taken)
            if (Vector2.Distance(new Vector2(x, z), new Vector2(t.x, t.y)) < t.z + radius) return false;

        return true;
    }

    // Ponds: a solid disc of water, a ring of stones round the edge and a few lily pads on top.
    static void BuildPonds(Transform scenery, System.Random rng, List<Vector3> taken, List<string> missing)
    {
        var group = WorldKit.Group("Ponds", scenery);
        var water = WorldKit.Mat("Water");

        for (int i = 0; i < Ponds.Length; i++)
        {
            float px = Ponds[i].x, pz = Ponds[i].y, r = Ponds[i].z;
            var pond = WorldKit.Group("Pond " + (i + 1), group);

            // A cylinder primitive is 2 m tall at scale 1, so scale.y = thickness / 2.
            var disc = WorldKit.Prim(PrimitiveType.Cylinder, "Water", null,
                new Vector3(px, WaterThickness / 2f, pz), new Vector3(2f * r, WaterThickness / 2f, 2f * r), pond, water);
            Object.DestroyImmediate(disc.GetComponent<Collider>()); // the default capsule would be a tall bubble
            disc.AddComponent<MeshCollider>().sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;

            taken.Add(new Vector3(px, pz, r + 1.5f));

            var stones = LoadAll(PondStones, missing);
            if (stones.Count > 0)
            {
                int n = Mathf.RoundToInt(r * 2.2f);
                for (int k = 0; k < n; k++)
                {
                    float a = (k + (float)rng.NextDouble() * 0.5f) / n * Mathf.PI * 2f;
                    float ring = r + 0.4f;
                    PlaceModel(pond, stones[rng.Next(stones.Count)], "Stone " + (k + 1),
                        px + Mathf.Cos(a) * ring, pz + Mathf.Sin(a) * ring, Mathf.Lerp(0.4f, 0.8f, (float)rng.NextDouble()), (float)rng.NextDouble() * 360f);
                }
            }

            var lilies = LoadAll(PondLilies, missing);
            if (lilies.Count > 0)
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(r));
                for (int k = 0; k < n; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = Mathf.Sqrt((float)rng.NextDouble()) * (r - 1f);
                    PlaceModel(pond, lilies[rng.Next(lilies.Count)], "Lily Pad " + (k + 1),
                        px + Mathf.Cos(a) * d, pz + Mathf.Sin(a) * d, 0.8f, (float)rng.NextDouble() * 360f, WaterThickness, true);
                }
            }
        }
    }

    static List<GameObject> LoadAll(string[] models, List<string> missing)
    {
        var list = new List<GameObject>();
        foreach (var m in models)
        {
            var prefab = Resources.Load<GameObject>(NatureFolder + m);
            if (prefab != null) list.Add(prefab); else if (!missing.Contains(m)) missing.Add(m);
        }
        return list;
    }

    // Puts a Nature Kit model on the floor with its footprint centred on (x, z), scaled so it is
    // `size` metres tall (or wide, for flat things like lily pads), and gives it solid colliders.
    // y0 lifts it off the floor. The world root is still at the origin here, so world and local
    // positions are the same.
    static GameObject PlaceModel(Transform parent, GameObject prefab, string name, float x, float z, float size, float yaw, float y0 = 0f, bool bySpan = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Vector3 native = RenderBounds(go).size;
        float reference = bySpan ? Mathf.Max(native.x, native.z) : native.y;
        if (reference > 0.0001f) go.transform.localScale = Vector3.one * (size / reference);

        Bounds b = RenderBounds(go);
        go.transform.localPosition += new Vector3(x - b.center.x, y0 - b.min.y, z - b.center.z);

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        return go;
    }

    static Bounds RenderBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void TagAll(GameObject root, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.tag = tag;
    }

    // The original blockout tree, used only when the Nature Kit model is not in the project.
    static void BuildPrimitiveTree(Transform root, Vector3 position)
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
