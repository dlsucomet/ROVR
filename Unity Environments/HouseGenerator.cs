// Assets/Editor/HouseGenerator.cs
// Menu: Tools > ROVR > Generate House
// Builds the ROVR house world: two storeys, 24 x 20 m footprint, 1 unit = 1 meter.
// Floor plans: rovr-house-floorplan.svg (generated from the same data as this script).
//
//   Ground floor: entrance hall, living room, kitchen + dining area, powder room,
//                 bedroom with ensuite bathroom, and the staircase.
//   Upper floor:  landing, master bedroom, walk-in closet, master bathroom.
//
// Needs WorldKit.cs alongside it. Drop real models in Assets/Resources/Props/<model>.prefab
// (model names are the 3rd argument of each Prop below) plus WallMaterial.mat / FloorMaterial.mat
// to use them automatically — anything missing falls back to a coloured blockout box.
//
// The upper-floor slab doubles as the ground-floor ceiling, and the flat roof over the upper
// storey is its own "Roof" object: disable it to look down into the rooms in the Scene view.
using UnityEngine;
using UnityEditor;

public static class HouseGenerator
{
    const float Ground = 0f;
    const float Upper = 3.2f;          // top of the upper-floor slab
    const float SlabThickness = 0.2f;
    const float H = WorldKit.WallHeight;
    const float Rail = 0.9f;

    // Straight stair flight, 16 risers of 0.2 m, running north inside a walled alcove.
    const float StairX0 = 13f, StairX1 = 15f, StairZ0 = 8f;
    const int Risers = 16;
    const float Tread = 0.3f;
    const float StairZ1 = StairZ0 + (Risers - 1) * Tread;

    const string F = "Furniture";
    const string C = "Chair";

    struct Prop
    {
        public int level;
        public string name, tag, model, mat;
        public Vector3 pos, size;
        public float y0;

        // (x, z) is the footprint centre; y0 is height above the floor (for wall-mounted / stacked items)
        public Prop(int level, string name, string tag, string model, float x, float z, float sx, float sy, float sz, string mat, float y0 = 0f)
        {
            this.level = level; this.name = name; this.tag = tag; this.model = model; this.mat = mat;
            this.pos = new Vector3(x, 0f, z); this.size = new Vector3(sx, sy, sz); this.y0 = y0;
        }
    }

    [MenuItem("Tools/ROVR/Generate House")]
    static void Generate() => Generate(Vector3.zero);

    // offset shifts the whole world after it's built, so it can sit anywhere without
    // touching any of the coordinates below.
    public static void Generate(Vector3 offset)
    {
        WorldKit.EnsureTags("Wall", "Door", "Chair", "Furniture", "Stairs");

        var root = WorldKit.NewRoot("House");
        BuildSlabs(root);
        BuildWalls(root);
        BuildStairs(root);
        BuildProps(root);
        BuildLights(root);

        var start = new GameObject("Start").transform;
        start.SetParent(root, false);
        start.localPosition = new Vector3(12f, 0f, 1.5f); // just inside the entrance, facing north

        root.position = offset;
    }

    static void BuildSlabs(Transform root)
    {
        var slabs = WorldKit.Group("Slabs", root);

        var floorMat = Resources.Load<Material>("Props/FloorMaterial");
        WorldKit.Box("Floor", null, new Vector3(12f, -0.05f, 10f), new Vector3(24f, 0.1f, 20f), slabs, floorMat);

        // Upper-floor slab / ground-floor ceiling, with an opening over the stairs.
        Slab(slabs, 0f, 24f, 0f, StairZ0);
        Slab(slabs, 0f, 24f, StairZ1, 20f);
        Slab(slabs, 0f, StairX0, StairZ0, StairZ1);
        Slab(slabs, StairX1, 24f, StairZ0, StairZ1);

        // Flat roof over the upper storey (x 0..15, z 6..20) — disable this object to peek inside.
        WorldKit.Box("Roof", null, new Vector3(7.5f, Upper + H + SlabThickness / 2f, 13f), new Vector3(15.4f, SlabThickness, 14.4f), root);
    }

    static void Slab(Transform parent, float x0, float x1, float z0, float z1)
    {
        WorldKit.Box("Slab", null,
            new Vector3((x0 + x1) / 2f, Upper - SlabThickness / 2f, (z0 + z1) / 2f),
            new Vector3(x1 - x0, SlabThickness, z1 - z0), parent);
    }

    static void BuildWalls(Transform root)
    {
        var walls = WorldKit.Group("Walls", root);
        var doors = WorldKit.Group("Doors", root);

        // Wall(walls, doors, x1, z1, x2, z2, floor height, wall height, doorways...)
        // Each DoorGap is (position along the wall, width); width defaults to 2 m.
        // ---- ground floor ----
        // south exterior, entrance
        WorldKit.Wall(walls, doors, 0f, 0f, 24f, 0f, Ground, H, new WorldKit.DoorGap(12f));
        // east exterior
        WorldKit.Wall(walls, doors, 24f, 0f, 24f, 20f, Ground, H);
        // north exterior
        WorldKit.Wall(walls, doors, 0f, 20f, 24f, 20f, Ground, H);
        // west exterior
        WorldKit.Wall(walls, doors, 0f, 0f, 0f, 20f, Ground, H);
        // hall | living room (door) and bedroom (door)
        WorldKit.Wall(walls, doors, 9f, 0f, 9f, 20f, Ground, H, new WorldKit.DoorGap(5f), new WorldKit.DoorGap(13f));
        // hall | kitchen (door) and powder room (door)
        WorldKit.Wall(walls, doors, 15f, 0f, 15f, 20f, Ground, H, new WorldKit.DoorGap(5f), new WorldKit.DoorGap(17f));
        // living room | bedroom
        WorldKit.Wall(walls, doors, 0f, 10f, 9f, 10f, Ground, H);
        // ensuite east wall (door)
        WorldKit.Wall(walls, doors, 4f, 15f, 4f, 20f, Ground, H, new WorldKit.DoorGap(17f));
        // ensuite south wall
        WorldKit.Wall(walls, doors, 0f, 15f, 4f, 15f, Ground, H);
        // powder room south wall
        WorldKit.Wall(walls, doors, 15f, 15f, 19f, 15f, Ground, H);
        // powder room east wall
        WorldKit.Wall(walls, doors, 19f, 15f, 19f, 20f, Ground, H);
        // stair alcove, west side (full height up to the slab)
        WorldKit.Wall(walls, doors, 13f, 8f, 13f, 12.5f, Ground, H);
        // ---- upper floor ----
        // upper south exterior
        WorldKit.Wall(walls, doors, 0f, 6f, 15f, 6f, Upper, H);
        // upper east exterior
        WorldKit.Wall(walls, doors, 15f, 6f, 15f, 20f, Upper, H);
        // upper north exterior
        WorldKit.Wall(walls, doors, 0f, 20f, 15f, 20f, Upper, H);
        // upper west exterior
        WorldKit.Wall(walls, doors, 0f, 6f, 0f, 20f, Upper, H);
        // master bedroom | landing (door)
        WorldKit.Wall(walls, doors, 9f, 6f, 9f, 20f, Upper, H, new WorldKit.DoorGap(11f));
        // master bedroom | closet (door) and bathroom (door)
        WorldKit.Wall(walls, doors, 0f, 15f, 9f, 15f, Upper, H, new WorldKit.DoorGap(2.25f), new WorldKit.DoorGap(6.75f));
        // closet | bathroom
        WorldKit.Wall(walls, doors, 4.5f, 15f, 4.5f, 20f, Upper, H);
        // stairwell railing, west
        WorldKit.Wall(walls, doors, 13f, 8f, 13f, 12.5f, Upper, Rail);
        // stairwell railing, south
        WorldKit.Wall(walls, doors, 13f, 8f, 15f, 8f, Upper, Rail);
    }

    static void BuildStairs(Transform root)
    {
        var stairs = WorldKit.Group("Stairs", root);
        var mat = WorldKit.Mat("Wood");
        float rise = Upper / Risers;

        // The last riser (step 16) is the upper floor itself, so 15 solid steps are built.
        for (int i = 1; i < Risers; i++)
        {
            float h = rise * i;
            WorldKit.Box("Step " + i, "Stairs",
                new Vector3((StairX0 + StairX1) / 2f, h / 2f, StairZ0 + (i - 0.5f) * Tread),
                new Vector3(StairX1 - StairX0, h, Tread), stairs, mat);
        }
    }

    static readonly Prop[] Props =
    {
        // living room (0..9, 0..10), door east z 4..6
        new Prop(0, "Living Sofa", F, "Sofa", 4.5f, 7.4f, 3.2f, 0.85f, 1f, "Fabric"),
        new Prop(0, "Coffee Table", F, "Table", 4.5f, 4.8f, 1.8f, 0.45f, 0.9f, "Wood"),
        new Prop(0, "TV Stand", F, "TVStand", 4.5f, 0.5f, 2.4f, 0.5f, 0.5f, "Dark"),
        new Prop(0, "Television", F, "TV", 4.5f, 0.45f, 1.6f, 0.9f, 0.1f, "Dark", 0.5f),
        new Prop(0, "Living Armchair", C, "Chair", 1.5f, 4.8f, 0.9f, 0.85f, 0.9f, "Fabric"),
        new Prop(0, "Bookshelf", F, "Bookshelf", 0.45f, 8.6f, 0.4f, 2f, 2f, "Wood"),
        new Prop(0, "Side Table", F, "Table", 7.9f, 8.8f, 0.5f, 0.6f, 0.5f, "Wood"),
        new Prop(0, "Floor Lamp", F, "Lamp", 0.6f, 1f, 0.35f, 1.6f, 0.35f, "Steel"),

        // hall
        new Prop(0, "Console Table", F, "Table", 9.5f, 2f, 0.5f, 0.9f, 1.4f, "Wood"),
        new Prop(0, "Coat Rack", F, "CoatRack", 14.65f, 1.5f, 0.4f, 1.8f, 0.4f, "Wood"),

        // kitchen (15..24, 0..15) + dining (19..24, 15..20), door west z 4..6
        new Prop(0, "Fridge", F, "Fridge", 16f, 0.6f, 1f, 1.9f, 0.9f, "Steel"),
        new Prop(0, "Counter West", F, "Counter", 17.5f, 0.45f, 2f, 0.9f, 0.7f, "White"),
        new Prop(0, "Stove", F, "Stove", 19.5f, 0.45f, 1f, 0.9f, 0.7f, "Dark"),
        new Prop(0, "Range Hood", F, "Hood", 19.5f, 0.4f, 1f, 0.6f, 0.6f, "Steel", 1.8f),
        new Prop(0, "Kitchen Sink", F, "Sink", 21f, 0.45f, 2f, 0.9f, 0.7f, "Steel"),
        new Prop(0, "Counter East", F, "Counter", 23f, 0.45f, 1.8f, 0.9f, 0.7f, "White"),
        new Prop(0, "Counter Wall", F, "Counter", 23.55f, 3f, 0.7f, 0.9f, 3.6f, "White"),
        new Prop(0, "Kitchen Island", F, "Counter", 19.5f, 4.5f, 3f, 0.95f, 1.2f, "Wood"),
        new Prop(0, "Bar Stool 1", C, "Stool", 18.6f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop(0, "Bar Stool 2", C, "Stool", 19.5f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop(0, "Bar Stool 3", C, "Stool", 20.4f, 5.8f, 0.4f, 0.7f, 0.4f, "Dark"),
        new Prop(0, "Dining Table", F, "Table", 20f, 11.5f, 2.4f, 0.75f, 1.2f, "Wood"),
        new Prop(0, "Dining Chair 1", C, "Chair", 19.5f, 10.4f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "Dining Chair 2", C, "Chair", 20.5f, 10.4f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "Dining Chair 3", C, "Chair", 19.5f, 12.6f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "Dining Chair 4", C, "Chair", 20.5f, 12.6f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "Dining Chair 5", C, "Chair", 18.3f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "Dining Chair 6", C, "Chair", 21.7f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood"),
        new Prop(0, "China Cabinet", F, "Cabinet", 23.5f, 14f, 0.5f, 1.7f, 2f, "Wood"),
        new Prop(0, "Pantry Shelf", F, "Shelf", 23.55f, 18.2f, 0.5f, 2f, 2.6f, "Wood"),

        // powder room (15..19, 15..20), door west z 16..18
        new Prop(0, "Powder Toilet", F, "Toilet", 17.5f, 19.5f, 0.45f, 0.45f, 0.75f, "White"),
        new Prop(0, "Powder Vanity", F, "Vanity", 18.6f, 17f, 0.5f, 0.9f, 0.9f, "White"),

        // ground bedroom (0..9, 10..20), door east z 12..14
        new Prop(0, "Bed", F, "Bed", 6.5f, 18.75f, 1.6f, 0.6f, 2.1f, "Linen"),
        new Prop(0, "Nightstand Left", F, "Nightstand", 5.2f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop(0, "Nightstand Right", F, "Nightstand", 7.8f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop(0, "Wardrobe", F, "Wardrobe", 2f, 10.6f, 2f, 2f, 0.6f, "Wood"),
        new Prop(0, "Dresser", F, "Dresser", 0.5f, 12.5f, 0.5f, 0.9f, 1.6f, "Wood"),
        new Prop(0, "Desk", F, "Desk", 5.5f, 10.6f, 1.4f, 0.75f, 0.7f, "Wood"),
        new Prop(0, "Bedroom Chair", C, "Chair", 5.5f, 11.5f, 0.5f, 0.9f, 0.5f, "Dark"),

        // ensuite (0..4, 15..20), door east z 16..18
        new Prop(0, "Ensuite Toilet", F, "Toilet", 0.5f, 19.2f, 0.75f, 0.45f, 0.45f, "White"),
        new Prop(0, "Ensuite Vanity", F, "Vanity", 2.5f, 19.55f, 1f, 0.9f, 0.5f, "White"),
        new Prop(0, "Ensuite Shower", F, "Shower", 0.6f, 15.6f, 1f, 2f, 1f, "Steel"),

        // hall upstairs / landing
        new Prop(1, "Landing Bookshelf", F, "Bookshelf", 14.65f, 18f, 0.4f, 2f, 2f, "Wood"),

        // master bedroom (0..9, 6..15), door east z 10..12
        new Prop(1, "Master Bed", F, "Bed", 1.2f, 10.5f, 2.1f, 0.6f, 2f, "Linen"),
        new Prop(1, "Master Nightstand Left", F, "Nightstand", 0.4f, 8.7f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop(1, "Master Nightstand Right", F, "Nightstand", 0.4f, 12.3f, 0.5f, 0.5f, 0.5f, "Wood"),
        new Prop(1, "Bed Bench", F, "Bench", 3.6f, 10.5f, 0.6f, 0.5f, 1.8f, "Fabric"),
        new Prop(1, "Master Dresser", F, "Dresser", 5f, 6.5f, 1.8f, 0.9f, 0.5f, "Wood"),
        new Prop(1, "Reading Chair", C, "Chair", 7.5f, 7f, 0.8f, 0.85f, 0.8f, "Fabric"),
        new Prop(1, "Reading Table", F, "Table", 8.2f, 8.3f, 0.5f, 0.5f, 0.5f, "Wood"),

        // closet (0..4.5, 15..20), door south x 1.25..3.25
        new Prop(1, "Clothes Rack", F, "Rack", 0.45f, 17.5f, 0.5f, 1.8f, 4f, "Steel"),
        new Prop(1, "Closet Shelves", F, "Shelf", 4.15f, 17.5f, 0.5f, 2f, 4f, "Wood"),
        new Prop(1, "Shoe Rack", F, "Shelf", 2.25f, 19.6f, 1.6f, 0.8f, 0.5f, "Wood"),
        new Prop(1, "Closet Island", F, "Dresser", 2.25f, 17.5f, 1f, 0.9f, 1.4f, "Wood"),

        // master bath (4.5..9, 15..20), door south x 5.75..7.75
        new Prop(1, "Bathtub", F, "Bathtub", 7.9f, 19.5f, 1.8f, 0.55f, 0.75f, "White"),
        new Prop(1, "Master Vanity", F, "Vanity", 8.6f, 17.2f, 0.5f, 0.9f, 2f, "White"),
        new Prop(1, "Master Toilet", F, "Toilet", 4.98f, 17.5f, 0.75f, 0.45f, 0.45f, "White"),
        new Prop(1, "Master Shower", F, "Shower", 5.15f, 19.4f, 1f, 2f, 1f, "Steel"),
    };

    static void BuildProps(Transform root)
    {
        var group = WorldKit.Group("Props", root);

        foreach (var p in Props)
        {
            float baseY = p.level == 0 ? Ground : Upper;
            Vector3 center = new Vector3(p.pos.x, baseY + p.y0 + p.size.y / 2f, p.pos.z);

            var prefab = Resources.Load<GameObject>($"Props/{p.model}");
            GameObject obj;

            if (prefab != null)
            {
                obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab); // keeps the prefab link
                obj.tag = p.tag;
                obj.transform.SetParent(group, false);
                obj.transform.localPosition = center;
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
        WorldKit.AddLight(lights, "Master Bedroom", new Vector3(4.5f, 5.8f, 10.5f), 9f);
        WorldKit.AddLight(lights, "Closet", new Vector3(2.25f, 5.8f, 17.5f), 5f);
        WorldKit.AddLight(lights, "Master Bath", new Vector3(6.75f, 5.8f, 17.5f), 5f);
        WorldKit.AddLight(lights, "Landing", new Vector3(12f, 5.8f, 13f), 9f);
        WorldKit.AddLight(lights, "Hall South", new Vector3(12f, 2.6f, 4f), 7f);
        WorldKit.AddLight(lights, "Hall North", new Vector3(12f, 2.6f, 16f), 7f);
        WorldKit.AddLight(lights, "Kitchen Centre", new Vector3(19.5f, 2.6f, 8f), 7f);
        WorldKit.AddLight(lights, "Dining Table", new Vector3(19.5f, 2.6f, 11.5f), 6f);
    }
}
