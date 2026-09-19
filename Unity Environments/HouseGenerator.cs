// Assets/Editor/HouseGenerator.cs
// Menu: Tools > ROVR > Generate House
// Builds the ROVR house world: single storey, 24 x 20 m, 1 unit = 1 meter.
// Floor plan: rovr-house-floorplan.svg (generated from the same data as this script).
//
//   Entrance hall down the middle; living room and bedroom (with ensuite bathroom) on the west
//   side; kitchen with dining area and a powder room on the east side.
//
// Every wall, prop, floor and the roof is a solid collider, so a CharacterController can't pass
// through them; the only openings are the door gaps. `Door` objects are trigger markers that
// sit in those gaps and never block movement.
//
// Needs WorldKit.cs alongside it. Drop real models in Assets/Resources/Props/<model>.prefab
// (model names are the 4th argument of each Prop below) plus WallMaterial.mat / FloorMaterial.mat
// to use them automatically — anything missing falls back to a coloured blockout box.
//
// A two-storey version (stairs, master suite upstairs) is in git commit f547f89.
// The roof is its own "Roof" object: disable it to look down into the rooms in the Scene view.
using UnityEngine;
using UnityEditor;

public static class HouseGenerator
{
    const float H = WorldKit.WallHeight;
    const float RoofThickness = 0.2f;

    const string F = "Furniture";
    const string C = "Chair";

    struct Prop
    {
        public string name, tag, model, mat;
        public Vector3 pos, size;
        public float y0;

        // (x, z) is the footprint centre; y0 is height above the floor (for wall-mounted / stacked items)
        public Prop(string name, string tag, string model, float x, float z, float sx, float sy, float sz, string mat, float y0 = 0f)
        {
            this.name = name; this.tag = tag; this.model = model; this.mat = mat;
            this.pos = new Vector3(x, 0f, z); this.size = new Vector3(sx, sy, sz); this.y0 = y0;
        }
    }

    [MenuItem("Tools/ROVR/Generate House")]
    static void Generate() => Generate(Vector3.zero);

    // offset shifts the whole world after it's built, so it can sit anywhere without
    // touching any of the coordinates below.
    public static void Generate(Vector3 offset)
    {
        WorldKit.EnsureTags("Wall", "Door", "Chair", "Furniture");

        var root = WorldKit.NewRoot("House");
        BuildFloorAndRoof(root);
        BuildWalls(root);
        BuildProps(root);
        BuildLights(root);

        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(12f, 0f, 1.5f); // just inside the entrance, facing north

        root.position = offset;
    }

    static void BuildFloorAndRoof(Transform root)
    {
        var floorMat = Resources.Load<Material>("Props/FloorMaterial");
        WorldKit.Box("Floor", null, new Vector3(12f, -0.05f, 10f), new Vector3(24f, 0.1f, 20f), root, floorMat);

        WorldKit.Box("Roof", null, new Vector3(12f, H + RoofThickness / 2f, 10f), new Vector3(24.4f, RoofThickness, 20.4f), root);
    }

    static void BuildWalls(Transform root)
    {
        var walls = WorldKit.Group("Walls", root);
        var doors = WorldKit.Group("Doors", root);

        // Wall(walls, doors, x1, z1, x2, z2, floor height, wall height, doorways...)
        // Each DoorGap is (position along the wall, width); width defaults to 2 m.
        // south exterior, entrance
        WorldKit.Wall(walls, doors, 0f, 0f, 24f, 0f, 0f, H, new WorldKit.DoorGap(12f));
        // east exterior
        WorldKit.Wall(walls, doors, 24f, 0f, 24f, 20f, 0f, H);
        // north exterior
        WorldKit.Wall(walls, doors, 0f, 20f, 24f, 20f, 0f, H);
        // west exterior
        WorldKit.Wall(walls, doors, 0f, 0f, 0f, 20f, 0f, H);
        // hall | living room (door) and bedroom (door)
        WorldKit.Wall(walls, doors, 9f, 0f, 9f, 20f, 0f, H, new WorldKit.DoorGap(5f), new WorldKit.DoorGap(13f));
        // hall | kitchen (door) and powder room (door)
        WorldKit.Wall(walls, doors, 15f, 0f, 15f, 20f, 0f, H, new WorldKit.DoorGap(5f), new WorldKit.DoorGap(17f));
        // living room | bedroom
        WorldKit.Wall(walls, doors, 0f, 10f, 9f, 10f, 0f, H);
        // ensuite east wall (door)
        WorldKit.Wall(walls, doors, 4f, 15f, 4f, 20f, 0f, H, new WorldKit.DoorGap(17f));
        // ensuite south wall
        WorldKit.Wall(walls, doors, 0f, 15f, 4f, 15f, 0f, H);
        // powder room south wall
        WorldKit.Wall(walls, doors, 15f, 15f, 19f, 15f, 0f, H);
        // powder room east wall
        WorldKit.Wall(walls, doors, 19f, 15f, 19f, 20f, 0f, H);
    }

    static readonly Prop[] Props =
    {
        // living room (0..9, 0..10), door east z 4..6
        new Prop("Living Sofa", F, "Sofa", 4.5f, 7.4f, 3.2f, 0.85f, 1f, "Fabric"),
        new Prop("Coffee Table", F, "Table", 4.5f, 4.8f, 1.8f, 0.45f, 0.9f, "Wood"),
        new Prop("TV Stand", F, "TVStand", 4.5f, 0.5f, 2.4f, 0.5f, 0.5f, "Dark"),
        new Prop("Television", F, "TV", 4.5f, 0.45f, 1.6f, 0.9f, 0.1f, "Dark", 0.5f),
        new Prop("Living Armchair", C, "Chair", 1.5f, 4.8f, 0.9f, 0.85f, 0.9f, "Fabric"),
        new Prop("Bookshelf", F, "Bookshelf", 0.45f, 8.6f, 0.4f, 2f, 2f, "Wood"),
        new Prop("Side Table", F, "Table", 7.9f, 8.8f, 0.5f, 0.6f, 0.5f, "Wood"),
        new Prop("Floor Lamp", F, "Lamp", 0.6f, 1f, 0.35f, 1.6f, 0.35f, "Steel"),

        // hall
        new Prop("Console Table", F, "Table", 9.5f, 2f, 0.5f, 0.9f, 1.4f, "Wood"),
        new Prop("Coat Rack", F, "CoatRack", 14.65f, 1.5f, 0.4f, 1.8f, 0.4f, "Wood"),

        // kitchen (15..24, 0..15) + dining (19..24, 15..20), door west z 4..6
        new Prop("Fridge", F, "Fridge", 16f, 0.6f, 1f, 1.9f, 0.9f, "Steel"),
        new Prop("Counter West", F, "Counter", 17.5f, 0.45f, 2f, 0.9f, 0.7f, "White"),
        new Prop("Stove", F, "Stove", 19.5f, 0.45f, 1f, 0.9f, 0.7f, "Dark"),
        new Prop("Range Hood", F, "Hood", 19.5f, 0.4f, 1f, 0.6f, 0.6f, "Steel", 1.8f),
        new Prop("Kitchen Sink", F, "Sink", 21f, 0.45f, 2f, 0.9f, 0.7f, "Steel"),
        new Prop("Counter East", F, "Counter", 23f, 0.45f, 1.8f, 0.9f, 0.7f, "White"),
        new Prop("Counter Wall", F, "Counter", 23.55f, 3f, 0.7f, 0.9f, 3.6f, "White"),
        new Prop("Kitchen Island", F, "Counter", 19.5f, 4.5f, 3f, 0.95f, 1.2f, "Wood"),
        new Prop("Bar Stool 1", C, "Stool", 18.6f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop("Bar Stool 2", C, "Stool", 19.5f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop("Bar Stool 3", C, "Stool", 20.4f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop("Dining Table", F, "Table", 20f, 11.5f, 2.4f, 0.75f, 1.2f, "Wood"),
        new Prop("Dining Chair 1", C, "Chair", 19.5f, 10.4f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("Dining Chair 2", C, "Chair", 20.5f, 10.4f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("Dining Chair 3", C, "Chair", 19.5f, 12.6f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("Dining Chair 4", C, "Chair", 20.5f, 12.6f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("Dining Chair 5", C, "Chair", 18.3f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("Dining Chair 6", C, "Chair", 21.7f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop("China Cabinet", F, "Cabinet", 23.5f, 14f, 0.5f, 1.7f, 2f, "Wood"),
        new Prop("Pantry Shelf", F, "Shelf", 23.55f, 18.2f, 0.5f, 2f, 2.6f, "Wood"),

        // powder room (15..19, 15..20), door west z 16..18
        new Prop("Powder Toilet", F, "Toilet", 17.5f, 19.5f, 0.45f, 0.45f, 0.75f, "White"),
        new Prop("Powder Vanity", F, "Vanity", 18.6f, 17f, 0.5f, 0.9f, 0.9f, "White"),

        // ground bedroom (0..9, 10..20), door east z 12..14
        new Prop("Bed", F, "Bed", 6.5f, 18.75f, 1.6f, 0.6f, 2.1f, "Linen"),
        new Prop("Nightstand Left", F, "Nightstand", 5.2f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop("Nightstand Right", F, "Nightstand", 7.8f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop("Wardrobe", F, "Wardrobe", 2f, 10.6f, 2f, 2f, 0.6f, "Wood"),
        new Prop("Dresser", F, "Dresser", 0.5f, 12.5f, 0.5f, 0.9f, 1.6f, "Wood"),
        new Prop("Desk", F, "Desk", 5.5f, 10.6f, 1.4f, 0.75f, 0.7f, "Wood"),
        new Prop("Bedroom Chair", C, "Chair", 5.5f, 11.5f, 0.5f, 0.9f, 0.5f, "Dark"),

        // ensuite (0..4, 15..20), door east z 16..18
        new Prop("Ensuite Toilet", F, "Toilet", 0.5f, 19.2f, 0.75f, 0.45f, 0.45f, "White"),
        new Prop("Ensuite Vanity", F, "Vanity", 2.5f, 19.55f, 1f, 0.9f, 0.5f, "White"),
        new Prop("Ensuite Shower", F, "Shower", 0.6f, 15.6f, 1f, 2f, 1f, "Steel"),
    };

    static void BuildProps(Transform root)
    {
        var group = WorldKit.Group("Props", root);

        foreach (var p in Props)
        {
            Vector3 center = new Vector3(p.pos.x, p.y0 + p.size.y / 2f, p.pos.z);

            var prefab = Resources.Load<GameObject>($"Props/{p.model}");
            GameObject obj;

            if (prefab != null)
            {
                obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab); // keeps the prefab link
                obj.tag = p.tag;
                obj.transform.SetParent(group, false);
                obj.transform.localPosition = center;
                WorldKit.EnsureCollider(obj);
            }
            else
            {
                obj = WorldKit.Box(p.name, p.tag, center, p.size, group, WorldKit.Mat(p.mat));
            }

            obj.name = p.name;
        }
    }

    static void BuildLights(Transform root)
    {
        var lights = WorldKit.Group("Lights", root);

        WorldKit.AddLight(lights, "Living Room", new Vector3(4.5f, 2.6f, 5f), 9f);
        WorldKit.AddLight(lights, "Bedroom", new Vector3(4.5f, 2.6f, 15f), 9f);
        WorldKit.AddLight(lights, "Ensuite", new Vector3(2f, 2.6f, 17.5f), 5f);
        WorldKit.AddLight(lights, "Hall", new Vector3(12f, 2.6f, 10f), 9f);
        WorldKit.AddLight(lights, "Kitchen", new Vector3(19.5f, 2.6f, 7.5f), 10f);
        WorldKit.AddLight(lights, "Dining", new Vector3(21.5f, 2.6f, 17.5f), 6f);
        WorldKit.AddLight(lights, "Powder Room", new Vector3(17f, 2.6f, 17.5f), 5f);
        WorldKit.AddLight(lights, "Hall South", new Vector3(12f, 2.6f, 4f), 7f);
        WorldKit.AddLight(lights, "Hall North", new Vector3(12f, 2.6f, 16f), 7f);
        WorldKit.AddLight(lights, "Kitchen Centre", new Vector3(19.5f, 2.6f, 8f), 7f);
        WorldKit.AddLight(lights, "Dining Table", new Vector3(19.5f, 2.6f, 11.5f), 6f);
    }
}
