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
// FURNITURE: uses the Kenney Furniture Kit (CC0, https://kenney.nl/assets/furniture-kit).
// The models live in Assets/Resources/Props/<name>.fbx and the 4th argument of each Prop below
// is the model name (several names joined with '+' build a mixed run, e.g. sink + cabinet).
// Every model gets the same uniform scale (KitScale) so the whole set keeps the kit's real-world
// proportions. If a model is missing, that prop falls back to a coloured blockout box.
//
// How a prop is placed (see BuildKitProp):
//   * (x, z) is the centre of its footprint, y0 is the height above the floor (wall-mounted / stacked).
//   * yaw is the direction the prop FACES: 0 = +Z (north), 90 = +X (east), 180 = -Z (south), 270 = -X (west).
//   * back:true pushes the prop against the wall behind it (opposite of the way it faces), so
//     furniture sits flush instead of floating.
//   * row:true repeats the model along the prop's length to fill it (kitchen runs, shelving).
//   * scale multiplies KitScale for pieces that come out too big or small (toilets, sinks).
//   * sx/sy/sz are the ORIGINAL blockout size: used for the fallback box and as the target run
//     length / depth. The kit models keep their own proportions.
// The prop root carries the tag and ONE solid BoxCollider, so raycasts (FOV grounding, stop-at
// conditions) hit a correctly tagged object, and CollisionCheck sees every mesh as covered.
//
// DECOR: potted plants, a cactus and a few small stone statues come from the Kenney Nature Kit
// (CC0), in Assets/Resources/Props/Nature. They are sized by target height in metres. A statue,
// cactus or plant that stands on furniture is placed on that prop's measured top. A missing model
// is skipped with a warning. Baseboards (the "Trim" boxes) run along every wall. All of it is
// tagged Furniture / Wall, so the LLM sees nothing new.
//
// Needs WorldKit.cs alongside it. Optional: drop WallMaterial.mat / FloorMaterial.mat into
// Assets/Resources/Props to override the built-in house colours.
//
// A two-storey version (stairs, master suite upstairs) is in git commit f547f89.
// The roof is its own "Roof" object: disable it to look down into the rooms in the Scene view.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class HouseGenerator
{
    const float H = WorldKit.WallHeight;
    const float RoofThickness = 0.2f;

    const string F = "Furniture";
    const string C = "Chair";

    // ---- Kenney Furniture Kit settings -------------------------------------------------------
    // Real metres per native model unit. The kit is drawn ~5x smaller than its file units
    // suggest (a sofa is 9.8 units long and should be ~2 m), so 0.2 gives true-to-life sizes.
    const float KitScale = 0.2f;

    // kitchenCabinet is 4.5 native units tall. Its imported height tells us what unit scale
    // Unity applied on import, so the result is right whatever the FBX import settings are.
    const float KitReferenceHeight = 4.5f;

    // The world yaw a kit model faces when placed with no rotation. The kit's models face -Z
    // (south) in their files, so 180. IF EVERY CHAIR / SOFA / TOILET FACES THE WRONG WAY after
    // generating, change this to 0. Only 0 or 180 make sense.
    const float NativeFrontYaw = 180f;

    // A repeated run may stretch or squash each module a little so it fills its length exactly.
    const float MinStretch = 0.9f;
    const float MaxStretch = 1.15f;

    struct Prop
    {
        public string name, tag, model, mat;
        public Vector3 pos, size;
        public float y0, yaw, scale;
        public bool back, row;

        public Prop(string name, string tag, string model, float x, float z, float sx, float sy, float sz, string mat,
                    float y0 = 0f, float yaw = 0f, bool back = false, bool row = false, float scale = 1f)
        {
            this.name = name; this.tag = tag; this.model = model; this.mat = mat;
            this.pos = new Vector3(x, 0f, z); this.size = new Vector3(sx, sy, sz);
            this.y0 = y0; this.yaw = yaw; this.back = back; this.row = row; this.scale = scale;
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
        WorldKit.LinkTeleportDestination("houseDestination", root);
    }

    // WallMaterial / FloorMaterial in Resources/Props win if present; otherwise the house uses
    // its own soft off-white and peach (matching the Kenney kit's look). Only the house does
    // this, so the Maze walls and the Plain are untouched.
    static Material HouseMaterial(string resourceName, string paletteKey)
    {
        var custom = Resources.Load<Material>("Props/" + resourceName);
        return custom != null ? custom : WorldKit.Mat(paletteKey);
    }

    static void BuildFloorAndRoof(Transform root)
    {
        var floorMat = HouseMaterial("FloorMaterial", "HouseFloorOak");
        WorldKit.Box("Floor", null, new Vector3(12f, -0.05f, 10f), new Vector3(24f, 0.1f, 20f), root, floorMat);

        var wallMat = HouseMaterial("WallMaterial", "HouseWallCream");
        WorldKit.Box("Roof", null, new Vector3(12f, H + RoofThickness / 2f, 10f), new Vector3(24.4f, RoofThickness, 20.4f), root, wallMat);
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

        var wallMat = HouseMaterial("WallMaterial", "HouseWallCream");
        if (wallMat != null)
            foreach (var r in walls.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = wallMat;

        BuildTrim(root, walls);
    }

    // A dark baseboard along the foot of every wall piece (not the lintels over doorways). It is a
    // solid box 2 cm proud of the wall on both faces, tagged Wall so it reads as part of the wall.
    static void BuildTrim(Transform root, Transform walls)
    {
        var trim = WorldKit.Group("Trim", root);
        var mat = WorldKit.Mat("Trim");
        const float height = 0.12f, proud = 0.02f;

        foreach (var r in walls.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            if (b.min.y > 0.05f) continue; // lintel above a doorway

            bool alongX = b.size.x > b.size.z;
            Vector3 size = alongX
                ? new Vector3(b.size.x, height, b.size.z + 2f * proud)
                : new Vector3(b.size.x + 2f * proud, height, b.size.z);
            WorldKit.Box("Baseboard", "Wall", new Vector3(b.center.x, height / 2f, b.center.z), size, trim, mat);
        }
    }

    static readonly Prop[] Props =
    {
        // living room (0..9, 0..10), door east z 4..6
        new Prop("Living Sofa", F, "loungeSofa", 4.5f, 7.4f, 3.2f, 0.85f, 1f, "Fabric", yaw: 180f),
        new Prop("Coffee Table", F, "tableCoffee", 4.5f, 4.8f, 1.8f, 0.45f, 0.9f, "Wood", yaw: 0f),
        new Prop("TV Stand", F, "cabinetTelevision", 4.5f, 0.5f, 2.4f, 0.5f, 0.5f, "Dark", yaw: 0f, back: true),
        new Prop("Television", F, "televisionModern", 4.5f, 0.45f, 1.6f, 0.9f, 0.1f, "Dark", y0: 0.62f, yaw: 0f, back: true),
        new Prop("Living Armchair", C, "loungeChair", 1.5f, 4.8f, 0.9f, 0.85f, 0.9f, "Fabric", yaw: 90f),
        new Prop("Bookshelf", F, "bookcaseOpen", 0.45f, 8.6f, 0.4f, 2f, 2f, "Wood", yaw: 90f, back: true, row: true),
        new Prop("Side Table", F, "cabinetBed", 8.6f, 8.8f, 0.5f, 0.6f, 0.5f, "Wood", yaw: 270f, back: true),
        new Prop("Floor Lamp", F, "lampRoundFloor", 0.6f, 1f, 0.35f, 1.6f, 0.35f, "Steel", yaw: 0f),

        // hall
        new Prop("Console Table", F, "sideTable", 9.5f, 2f, 0.5f, 0.9f, 1.4f, "Wood", yaw: 90f, back: true),
        new Prop("Coat Rack", F, "coatRackStanding", 14.55f, 1.5f, 0.4f, 1.8f, 0.4f, "Wood", yaw: 0f),

        // kitchen (15..24, 0..15) + dining (19..24, 15..20), door west z 4..6
        new Prop("Fridge", F, "kitchenFridgeLarge", 16f, 0.6f, 1f, 1.9f, 0.9f, "Steel", yaw: 0f, back: true),
        new Prop("Counter West", F, "kitchenCabinetDrawer", 17.55f, 0.45f, 2f, 0.9f, 0.7f, "White", yaw: 0f, back: true, row: true),
        new Prop("Stove", F, "kitchenStoveElectric", 19.5f, 0.45f, 1f, 0.9f, 0.7f, "Dark", yaw: 0f, back: true, row: true),
        new Prop("Range Hood", F, "hoodLarge", 19.5f, 0.4f, 1f, 0.6f, 0.6f, "Steel", y0: 1.6f, yaw: 0f, back: true),
        new Prop("Kitchen Sink", F, "kitchenSink+kitchenCabinetDrawer", 21f, 0.45f, 2f, 0.9f, 0.7f, "Steel", yaw: 0f, back: true, row: true),
        new Prop("Counter East", F, "kitchenCabinet", 23f, 0.45f, 1.8f, 0.9f, 0.7f, "White", yaw: 0f, back: true, row: true),
        new Prop("Counter Wall", F, "kitchenCabinet", 23.55f, 3f, 0.7f, 0.9f, 3.6f, "White", yaw: 270f, back: true, row: true),
        new Prop("Kitchen Island", F, "kitchenCabinet", 19.5f, 4.5f, 3f, 0.95f, 1.2f, "Wood", yaw: 180f, row: true),
        new Prop("Bar Stool 1", C, "stoolBar", 18.6f, 5.3f, 0.4f, 0.7f, 0.4f, "Dark", yaw: 180f),
        new Prop("Bar Stool 2", C, "stoolBar", 19.5f, 5.3f, 0.4f, 0.7f, 0.4f, "Dark", yaw: 180f),
        new Prop("Bar Stool 3", C, "stoolBar", 20.4f, 5.3f, 0.4f, 0.7f, 0.4f, "Dark", yaw: 180f),
        new Prop("Dining Table", F, "table", 20f, 11.5f, 2.4f, 0.75f, 1.2f, "Wood", yaw: 0f),
        new Prop("Dining Chair 1", C, "chair", 19.5f, 10.65f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 0f),
        new Prop("Dining Chair 2", C, "chair", 20.5f, 10.65f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 0f),
        new Prop("Dining Chair 3", C, "chair", 19.5f, 12.35f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 180f),
        new Prop("Dining Chair 4", C, "chair", 20.5f, 12.35f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 180f),
        new Prop("Dining Chair 5", C, "chair", 18.75f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 90f),
        new Prop("Dining Chair 6", C, "chair", 21.25f, 11.5f, 0.5f, 0.9f, 0.5f, "Wood", yaw: 270f),
        new Prop("China Cabinet", F, "bookcaseClosedDoors", 23.5f, 14f, 0.5f, 1.7f, 2f, "Wood", yaw: 270f, back: true, row: true),
        new Prop("Pantry Shelf", F, "bookcaseOpen", 23.55f, 18.2f, 0.5f, 2f, 2.6f, "Wood", yaw: 270f, back: true, row: true),

        // powder room (15..19, 15..20), door west z 16..18
        new Prop("Powder Toilet", F, "toilet", 17.5f, 19.5f, 0.45f, 0.45f, 0.75f, "White", yaw: 180f, back: true, scale: 0.85f),
        new Prop("Powder Vanity", F, "bathroomSink", 18.6f, 17f, 0.5f, 0.9f, 0.9f, "White", yaw: 270f, back: true, scale: 0.8f),

        // ground bedroom (0..9, 10..20), door east z 12..14
        new Prop("Bed", F, "bedDouble", 6.5f, 18.75f, 1.6f, 0.6f, 2.1f, "Linen", yaw: 180f, back: true),
        new Prop("Nightstand Left", F, "cabinetBedDrawer", 5.2f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood", yaw: 180f, back: true),
        new Prop("Nightstand Right", F, "cabinetBedDrawer", 7.8f, 19.5f, 0.5f, 0.5f, 0.5f, "Wood", yaw: 180f, back: true),
        new Prop("Wardrobe", F, "bookcaseClosedDoors", 2f, 10.6f, 2f, 2f, 0.6f, "Wood", yaw: 0f, back: true, row: true),
        new Prop("Dresser", F, "cabinetTelevisionDoors", 0.5f, 12.5f, 0.5f, 0.9f, 1.6f, "Wood", yaw: 90f, back: true),
        new Prop("Desk", F, "desk", 5.5f, 10.6f, 1.4f, 0.75f, 0.7f, "Wood", yaw: 0f, back: true),
        new Prop("Bedroom Chair", C, "chairDesk", 5.5f, 11.5f, 0.5f, 0.9f, 0.5f, "Dark", yaw: 180f),

        // ensuite (0..4, 15..20), door east z 16..18
        new Prop("Ensuite Toilet", F, "toilet", 0.5f, 19.2f, 0.75f, 0.45f, 0.45f, "White", yaw: 90f, back: true, scale: 0.85f),
        new Prop("Ensuite Vanity", F, "bathroomSink", 2.5f, 19.55f, 1f, 0.9f, 0.5f, "White", yaw: 180f, back: true, scale: 0.8f),
        new Prop("Ensuite Shower", F, "showerRound", 0.62f, 15.62f, 1f, 2f, 1f, "Steel", yaw: 180f, scale: 0.85f),

        // extra furniture (all tagged Furniture, so the chair count the LLM sees does not change)
        new Prop("Side Table 2", F, "cabinetBed", 2f, 9.5f, 0.5f, 0.6f, 0.5f, "Wood", yaw: 180f, back: true),
        new Prop("Living Armchair 2", F, "loungeChair", 6.8f, 2.8f, 0.9f, 0.85f, 0.9f, "Fabric", yaw: 180f),
        new Prop("Bedroom Lamp", F, "lampRoundFloor", 8.5f, 19.4f, 0.35f, 1.6f, 0.35f, "Steel", yaw: 0f),
        new Prop("Hall Stool 1", F, "stoolBar", 9.55f, 7.6f, 0.4f, 0.7f, 0.4f, "Dark", yaw: 90f),
        new Prop("Hall Stool 2", F, "stoolBar", 9.55f, 8.3f, 0.4f, 0.7f, 0.4f, "Dark", yaw: 90f),
    };

    // ---- Nature Kit decor ----------------------------------------------------------------------
    // (model, height in metres), stacked bottom to top. surface = the name of a prop above, to
    // stand on its top (x, z are then taken from it); otherwise the item stands on the floor at x, z.
    struct Decor
    {
        public string name, surface;
        public float x, z;
        public (string model, float height)[] layers;

        public Decor(string name, string surface, float x, float z, params (string model, float height)[] layers)
        {
            this.name = name; this.surface = surface; this.x = x; this.z = z; this.layers = layers;
        }
    }

    const string DecorFolder = "Props/Nature/";
    static readonly (string model, float height)[] BigPlant = { ("pot_large", 0.45f), ("plant_bushDetailed", 0.9f) };
    static readonly (string model, float height)[] TallPlant = { ("pot_large", 0.45f), ("plant_bush", 1.3f) };

    static readonly Decor[] DecorItems =
    {
        // hall
        new Decor("Hall Plant 1", null, 14.4f, 10f, TallPlant),
        new Decor("Hall Plant 2", null, 14.4f, 13f, BigPlant),
        new Decor("Hall Plant 3", null, 9.6f, 10.6f, BigPlant),
        new Decor("Hall Plant 4", null, 12f, 19.3f, TallPlant),
        new Decor("Console Statue", "Console Table", 0f, 0f, ("statue_head", 0.35f)),

        // living room
        new Decor("Living Plant 1", null, 8.4f, 1f, TallPlant),
        new Decor("Living Plant 2", null, 0.7f, 6.4f, BigPlant),
        new Decor("Living Cactus", "Side Table", 0f, 0f, ("pot_small", 0.12f), ("cactus_short", 0.3f)),

        // bedroom
        new Decor("Bedroom Plant 1", null, 0.7f, 14.3f, BigPlant),
        new Decor("Bedroom Plant 2", null, 8.4f, 11f, TallPlant),
        new Decor("Nightstand Plant", "Nightstand Left", 0f, 0f, ("pot_small", 0.12f), ("plant_bushSmall", 0.25f)),

        // kitchen and dining
        new Decor("Dining Plant 1", null, 19.7f, 15.8f, BigPlant),
        new Decor("Dining Plant 2", null, 21.5f, 19.3f, TallPlant),
        new Decor("Cabinet Statue", "China Cabinet", 0f, 0f, ("statue_column", 0.5f)),
    };

    // Places one stack of Nature Kit models and gives the stack one solid box.
    static void BuildDecor(Transform props)
    {
        var group = WorldKit.Group("Decor", props.parent);
        var skipped = new List<string>();

        foreach (var d in DecorItems)
        {
            float x = d.x, z = d.z, y = 0f;
            if (d.surface != null)
            {
                var surface = props.Find(d.surface);
                if (surface == null) { skipped.Add(d.name + " (no prop called " + d.surface + ")"); continue; }
                Bounds sb = RenderBounds(surface.gameObject);
                x = sb.center.x; z = sb.center.z; y = sb.max.y;
            }

            var holder = new GameObject(d.name);
            holder.transform.SetParent(group, false);
            bool any = false;

            foreach (var layer in d.layers)
            {
                var prefab = Resources.Load<GameObject>(DecorFolder + layer.model);
                if (prefab == null) { skipped.Add(d.name + " (" + layer.model + ")"); continue; }

                var m = Spawn(prefab, holder.transform, 1f);
                float native = RenderBounds(m).size.y;
                if (native > 0.0001f) m.transform.localScale = Vector3.one * (layer.height / native);

                Bounds b = RenderBounds(m);
                m.transform.localPosition += new Vector3(x - b.center.x, y - b.min.y, z - b.center.z);
                y = RenderBounds(m).max.y;
                any = true;
            }

            if (!any) { Object.DestroyImmediate(holder); continue; }

            Bounds all = RenderBounds(holder);
            var box = holder.AddComponent<BoxCollider>();
            box.center = all.center;
            box.size = all.size;
            TagAll(holder, F);
        }

        if (skipped.Count > 0)
            Debug.LogWarning("[ROVR House] Skipped decor (copy Assets/Resources/Props/Nature into the project): " + string.Join(", ", skipped.ToArray()));
    }

    static void BuildProps(Transform root)
    {
        var group = WorldKit.Group("Props", root);
        float k = KitScale / KitUnit();
        var fallbacks = new List<string>();

        foreach (var p in Props)
        {
            GameObject obj = BuildKitProp(group, p, k);

            if (obj == null)
            {
                // Model not found: keep the old coloured blockout box so the world is still complete.
                Vector3 center = new Vector3(p.pos.x, p.y0 + p.size.y / 2f, p.pos.z);
                obj = WorldKit.Box(p.name, p.tag, center, p.size, group, WorldKit.Mat(p.mat));
                fallbacks.Add(p.name + " (" + p.model + ")");
            }

            obj.name = p.name;
        }

        BuildDecor(group);

        if (fallbacks.Count > 0)
            Debug.LogWarning("[ROVR House] " + fallbacks.Count + " prop(s) used blockout boxes because no model was found in "
                             + "Assets/Resources/Props: " + string.Join(", ", fallbacks.ToArray()));
    }

    // Unity units per native model unit, as imported. 1 when the FBX imported at its file scale.
    static float KitUnit()
    {
        var probe = Resources.Load<GameObject>("Props/kitchenCabinet");
        if (probe == null) return 1f;

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(probe);
        float height = RenderBounds(inst).size.y;
        Object.DestroyImmediate(inst);

        return height > 0.0001f ? height / KitReferenceHeight : 1f;
    }

    // Builds one prop from kit models. Returns null if any of its models is missing.
    //
    // Everything is first laid out in an un-rotated "holder" whose local axes are the model's own
    // (front = NativeFrontYaw), measured from the real mesh bounds, and only then is the holder
    // moved and turned to face `yaw`. That keeps the maths simple and makes the collider exact.
    static GameObject BuildKitProp(Transform group, Prop p, float k)
    {
        string[] names = p.model.Split('+');
        var prefabs = new GameObject[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            prefabs[i] = Resources.Load<GameObject>("Props/" + names[i]);
            if (prefabs[i] == null) return null;
        }

        float turn = p.yaw - NativeFrontYaw;                               // holder rotation about Y
        bool swap = Mathf.Abs(Mathf.Sin(turn * Mathf.Deg2Rad)) > 0.5f;     // turned a quarter: local X is world Z
        float runLength = swap ? p.size.z : p.size.x;                      // target length along local X
        float depthTarget = swap ? p.size.x : p.size.z;                    // target depth along local Z
        float backSign = Mathf.Cos(NativeFrontYaw * Mathf.Deg2Rad) < 0f ? 1f : -1f; // which local Z side is the back
        float s = k * p.scale;

        var holder = new GameObject(p.name);
        holder.transform.SetParent(group, false);

        // How many modules make up the run, and how much each is stretched to fill it.
        int count = names.Length;
        float moduleWidth = 0f;
        float stretch = 1f;
        if (p.row)
        {
            var probe = Spawn(prefabs[0], holder.transform, s);
            moduleWidth = RenderBounds(probe).size.x;
            Object.DestroyImmediate(probe);

            count = Mathf.Max(names.Length, Mathf.RoundToInt(runLength / moduleWidth));
            stretch = Mathf.Clamp(runLength / count / moduleWidth, MinStretch, MaxStretch);
        }

        for (int i = 0; i < count; i++)
        {
            var m = Spawn(prefabs[i % prefabs.Length], holder.transform, s);
            if (p.row)
            {
                Vector3 ls = m.transform.localScale;
                m.transform.localScale = new Vector3(ls.x * stretch, ls.y, ls.z);
            }

            // Kit models have their pivot at a corner, so centre each one from its measured bounds:
            // centred along the run, sitting on the floor, and (for wall pieces) flush at the back.
            Bounds b = RenderBounds(m);
            float cx = p.row ? (i - (count - 1) * 0.5f) * moduleWidth * stretch : 0f;
            float cz = p.back ? backSign * (depthTarget - b.size.z) * 0.5f : 0f;
            m.transform.localPosition += new Vector3(cx - b.center.x, -b.min.y, cz - b.center.z);
        }

        // One solid box over the whole prop, measured before the holder is moved or turned.
        Bounds all = RenderBounds(holder);
        var box = holder.AddComponent<BoxCollider>();
        box.center = all.center;
        box.size = all.size;

        holder.transform.localPosition = new Vector3(p.pos.x, p.y0, p.pos.z);
        holder.transform.localRotation = Quaternion.Euler(0f, turn, 0f);

        TagAll(holder, p.tag);
        return holder;
    }

    static GameObject Spawn(GameObject prefab, Transform parent, float scale)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;
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

    // The scene's grounding reads the tag of whatever collider a ray hits, so tag the root AND
    // every child. Then it doesn't matter which one is hit.
    static void TagAll(GameObject root, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.tag = tag;
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
