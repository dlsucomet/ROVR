// Assets/GUI/StudyTasks.cs
// The objectives of a study session, per world, in order. Each ends by itself when the participant gets there:
//
//   Spot      the head within `distance` m of a point (the Maze checkpoints).
//   Object    the body within `distance` m of an object's footprint ("Go to the bed"). Measured from the
//             body's edge, so ROVR's "go to X", which stops 0.5 m short, always counts.
//   Room      the head inside a room, past its doorway ("Go into the guest bathroom").
//   Face      the head turned within `distance` degrees of an object, held for half a second
//             ("Turn towards the toilet").
//   MazeExit  the head within `distance` m of the Maze's exit pad.
//
// Markers: a yellow pillar beside the goal, with a beacon above it that fades from translucent to clear
// (the Plain), a pillar alone (the Maze checkpoints), or nothing (the House, where the object's name is the
// only cue). Only the current objective's marker is shown. Markers have no colliders and no SceneLabel, so
// they never block movement and ROVR can't name them.
//
// The Maze also gets a glowing trail along its correct route (MazeTrail); the checkpoints sit on that route at
// a quarter, a half and three quarters of the way.
//
// There are no time limits; the operator can skip or restart from the control page. To change the
// objectives, edit the lists in Plain(), Maze() and House() below, or add this component to the panel in the
// scene and fill in its list in the Inspector (a non-empty list there is used instead).
using System;
using System.Collections.Generic;
using UnityEngine;

public enum GoalKind { Spot, Object, Room, Face, MazeExit }
public enum GoalLook { None, Pillar, Beacon }

[Serializable]
public class StudyTask
{
    public string world;          // Practice, Plain, Maze or House
    [TextArea] public string instruction;
    public GoalKind kind;
    [Tooltip("Object / Face: the object, as a path under the world's root (e.g. Props/Bed).")]
    public string target;
    [Tooltip("Spot: the point. Room: the room's centre. Face: where to stand (for the flow check). Object: where the participant comes from, which sets the marker's side. All in the world root's own coordinates.")]
    public Vector3 goal;
    [Tooltip("Room: the room's floor size (x, z), already inset from its walls.")]
    public Vector2 size;
    [Tooltip("Spot / MazeExit: metres from the head. Object: metres from the body's edge. Face: degrees.")]
    public float distance = 1.5f;
    public GoalLook look;
}

public class StudyTasks : MonoBehaviour
{
    [Tooltip("Leave empty to use the built-in objectives.")]
    public List<StudyTask> tasks = new List<StudyTask>();

    const float PlainGap = 1.5f;        // Plain landmarks: body within 1.5 m (an open field, less precision)
    const float HouseGap = 1f;          // House furniture: body within 1 m
    const float CheckpointRadius = 2f;  // half a 4 m maze corridor: anyone walking the route passes it
    const float FaceDegrees = 15f;
    const float FaceHoldSeconds = 0.5f;
    const float BeaconHeight = 30f;

    private VRControlPanel _panel;
    private MazeTrail _trail;
    private GameObject _marker;
    private Material _beaconMaterial;
    private float _facingSince = -1f;

    public int MazeRouteCells => _trail != null ? _trail.Cells.Count : 0;

    public List<StudyTask> For(string world)
    {
        return tasks.FindAll(t => string.Equals(t.world, world, StringComparison.OrdinalIgnoreCase));
    }

    // Lays the Maze's trail and fills in the built-in objectives when the Inspector list is empty.
    public void Setup(VRControlPanel panel)
    {
        _panel = panel;
        var maze = RootOf("Maze");
        if (maze != null) _trail = MazeTrail.Build(maze);
        if (tasks.Count > 0) return;
        tasks.AddRange(Plain());
        tasks.AddRange(Maze());
        tasks.AddRange(House());
    }

    // ---------------------------------------------------------------- the objectives

    // Five landmarks round the 100 x 100 m field, one in each quarter; the start is in the middle, facing the tree.
    private static IEnumerable<StudyTask> Plain()
    {
        yield return Landmark("Go to the tree", "Tree", new Vector3(50f, 0f, 50f));
        yield return Landmark("Go to the pond under the beacon", "Scenery/Ponds/Pond 1", new Vector3(50f, 0f, 68f));
        yield return Landmark("Go to the rock under the beacon", "Scenery/Rocks/Rock 8", new Vector3(22f, 0f, 72f));
        yield return Landmark("Go to the pond under the beacon", "Scenery/Ponds/Pond 2", new Vector3(31.3f, 0f, 28.2f));
        yield return Landmark("Go to the rock under the beacon", "Scenery/Rocks/Rock 14", new Vector3(80f, 0f, 28f));
    }

    private static StudyTask Landmark(string instruction, string target, Vector3 comingFrom)
    {
        return new StudyTask { world = "Plain", instruction = instruction, kind = GoalKind.Object, target = target,
                               goal = comingFrom, distance = PlainGap, look = GoalLook.Beacon };
    }

    // Three checkpoints on the correct route (their positions come from the trail), then the exit.
    private static IEnumerable<StudyTask> Maze()
    {
        for (int i = 1; i <= 3; i++)
            yield return new StudyTask { world = "Maze", instruction = $"Follow the trail to checkpoint {i}", kind = GoalKind.Spot,
                                         target = "checkpoint " + i, distance = CheckpointRadius, look = GoalLook.Pillar };
        yield return new StudyTask { world = "Maze", instruction = "Follow the trail to the exit", kind = GoalKind.MazeExit,
                                     distance = 2f, look = GoalLook.None };
    }

    // Two objectives per room. House coordinates: living room (0..9, 0..10), bedroom (0..9, 10..20) with its
    // bathroom (0..4, 15..20), hall (9..15), kitchen and dining (15..24, 0..20) with the guest bathroom (15..19, 15..20).
    private static IEnumerable<StudyTask> House()
    {
        Vector3 livingDoor = new Vector3(8f, 0f, 5f), kitchenDoor = new Vector3(16f, 0f, 5f), bedroomDoor = new Vector3(8f, 0f, 13f);
        yield return Furniture("Go to the TV in the living room", "Props/Television", livingDoor);
        yield return Furniture("Go to the bookshelf in the living room", "Props/Bookshelf", new Vector3(4.5f, 0f, 6f));
        yield return Furniture("Go to the fridge in the kitchen", "Props/Fridge", kitchenDoor);
        yield return Furniture("Go to the dining table", "Props/Dining Table", new Vector3(17.5f, 0f, 8f));
        yield return Furniture("Go to the desk in the bedroom", "Props/Desk", bedroomDoor);
        yield return Furniture("Go to the bed", "Props/Bed", new Vector3(5f, 0f, 13f));

        // Inset 0.5 m from the walls, so "entering" means past the doorway.
        var bedroomBath = new Vector3(2f, 0f, 17.5f);
        var guestBath = new Vector3(17f, 0f, 17.5f);
        var bath = new Vector2(2.8f, 3.8f);
        yield return new StudyTask { world = "House", instruction = "Go into the bedroom bathroom", kind = GoalKind.Room, goal = bedroomBath, size = bath };
        yield return new StudyTask { world = "House", instruction = "Turn towards the shower", kind = GoalKind.Face, target = "Props/Ensuite Shower", goal = bedroomBath, distance = FaceDegrees };
        yield return new StudyTask { world = "House", instruction = "Go into the guest bathroom", kind = GoalKind.Room, goal = guestBath, size = bath };
        yield return new StudyTask { world = "House", instruction = "Turn towards the toilet", kind = GoalKind.Face, target = "Props/Powder Toilet", goal = guestBath, distance = FaceDegrees };
    }

    private static StudyTask Furniture(string instruction, string target, Vector3 comingFrom)
    {
        return new StudyTask { world = "House", instruction = instruction, kind = GoalKind.Object, target = target,
                               goal = comingFrom, distance = HouseGap, look = GoalLook.None };
    }

    // ---------------------------------------------------------------- reached?

    public bool Reached(StudyTask task, Transform head)
    {
        Vector3 h = head.position;
        switch (task.kind)
        {
            case GoalKind.Object:
                return TryFootprint(task, out Rect area) && DistanceTo(area, h) <= task.distance + BodyRadius();
            case GoalKind.Room:
            {
                Vector3 c = World(task, task.goal);
                return Mathf.Abs(h.x - c.x) <= task.size.x / 2f && Mathf.Abs(h.z - c.z) <= task.size.y / 2f;
            }
            case GoalKind.Face:
            {
                bool facing = TryCentre(task, out Vector3 centre) && FlatAngle(head.forward, centre - h) <= task.distance;
                if (!facing) { _facingSince = -1f; return false; }
                if (_facingSince < 0f) _facingSince = Time.time;
                return Time.time - _facingSince >= FaceHoldSeconds;
            }
            default:
            {
                Vector3 to = GoalOf(task) - h;
                to.y = 0f;
                return to.magnitude <= task.distance;
            }
        }
    }

    // A spot that counts as reached: where the marker points the participant, and where the flow check stands.
    public Vector3 GoalOf(StudyTask task)
    {
        switch (task.kind)
        {
            case GoalKind.MazeExit:
                var exit = GameObject.FindGameObjectWithTag("Goal");
                return exit != null ? exit.transform.position : Vector3.zero;
            case GoalKind.Spot:
                if (_trail != null && task.target != null && task.target.StartsWith("checkpoint "))
                    return _trail.Checkpoint(int.Parse(task.target.Substring(11)));
                return World(task, task.goal);
            case GoalKind.Object:
                if (!TryFootprint(task, out Rect area)) return World(task, task.goal);
                Edge(task, area, out Vector3 edge, out Vector3 outward);
                return edge + outward * (BodyRadius() + task.distance * 0.5f);
            default:
                return World(task, task.goal);
        }
    }

    // The middle of the object a Face (or Object) objective is about.
    public bool TryCentre(StudyTask task, out Vector3 centre)
    {
        bool found = TryBounds(task, out Bounds b);
        centre = b.center;
        return found;
    }

    // Objectives whose object or maze route isn't in the scene (for the flow check).
    public List<string> Missing()
    {
        var missing = new List<string>();
        foreach (var t in tasks)
        {
            bool ok = t.kind == GoalKind.Object || t.kind == GoalKind.Face ? TryBounds(t, out _)
                    : t.kind == GoalKind.MazeExit ? GameObject.FindGameObjectWithTag("Goal") != null
                    : t.kind != GoalKind.Spot || t.target == null || (_trail != null && _trail.Cells.Count > 0);
            if (!ok) missing.Add(t.world + ": " + t.instruction + (t.target != null ? " (" + t.target + ")" : ""));
        }
        return missing;
    }

    // ---------------------------------------------------------------- markers

    public void ShowGoal(StudyTask task)
    {
        HideGoal();
        if (task == null || task.look == GoalLook.None) return;

        Vector3 at;
        if (task.kind == GoalKind.Object && TryFootprint(task, out Rect area))
        {
            Edge(task, area, out Vector3 edge, out Vector3 outward);
            at = edge + outward * 0.4f; // beside the object, on the side the participant comes from
        }
        else at = GoalOf(task);
        at.y = FloorBelow(task);

        _marker = new GameObject("Objective Marker");
        var pillar = Part(PrimitiveType.Cylinder, "Pillar", _marker.transform, at + Vector3.up * 0.6f, new Vector3(0.5f, 0.6f, 0.5f));
        pillar.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.2f); // the primitive's own material: nothing a build could strip

        if (task.look == GoalLook.Beacon)
        {
            if (_beaconMaterial == null)
            {
                var shader = Shader.Find("ROVR/Beacon");
                if (shader == null) { Debug.LogWarning("[ROVR] No ROVR/Beacon shader (Assets/Resources/Shaders), so the marker has no beacon."); return; }
                _beaconMaterial = new Material(shader);
            }
            var beacon = Part(PrimitiveType.Cylinder, "Beacon", _marker.transform, at + Vector3.up * (1.2f + BeaconHeight / 2f),
                              new Vector3(0.6f, BeaconHeight / 2f, 0.6f));
            beacon.GetComponent<Renderer>().sharedMaterial = _beaconMaterial;
        }
    }

    public void HideGoal()
    {
        if (_marker != null) Destroy(_marker);
        _marker = null;
        _facingSince = -1f;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // never in the way, never hit by ROVR's rays
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    // ---------------------------------------------------------------- geometry

    private Transform RootOf(string world)
    {
        if (_panel == null) return null;
        var start = _panel.StartOf(world);
        return start != null ? start.parent : null;
    }

    private Vector3 World(StudyTask task, Vector3 local)
    {
        var root = RootOf(task.world);
        return root != null ? root.TransformPoint(local) : local;
    }

    private float FloorBelow(StudyTask task)
    {
        var root = RootOf(task.world);
        return root != null ? root.position.y : 0f;
    }

    private float BodyRadius()
    {
        var body = _panel != null && _panel.playerBody != null ? _panel.playerBody.GetComponent<CharacterController>() : null;
        return body != null ? body.radius : 0.5f;
    }

    // The object's solid parts (or, failing that, what's drawn).
    private bool TryBounds(StudyTask task, out Bounds bounds)
    {
        bounds = default;
        var root = RootOf(task.world);
        var target = root != null && !string.IsNullOrEmpty(task.target) ? root.Find(task.target) : null;
        if (target == null) return false;

        bool any = false;
        foreach (var c in target.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger) continue;
            if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
        }
        if (any) return true;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private bool TryFootprint(StudyTask task, out Rect area)
    {
        bool found = TryBounds(task, out Bounds b);
        area = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        return found;
    }

    // The point of the footprint nearest where the participant comes from, and the way out from it.
    private void Edge(StudyTask task, Rect area, out Vector3 edge, out Vector3 outward)
    {
        Vector3 from = World(task, task.goal);
        edge = new Vector3(Mathf.Clamp(from.x, area.xMin, area.xMax), 0f, Mathf.Clamp(from.z, area.yMin, area.yMax));
        outward = new Vector3(from.x - edge.x, 0f, from.z - edge.z);
        outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.back;
    }

    private static float DistanceTo(Rect area, Vector3 p)
    {
        float dx = Mathf.Max(area.xMin - p.x, 0f, p.x - area.xMax);
        float dz = Mathf.Max(area.yMin - p.z, 0f, p.z - area.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float FlatAngle(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f ? 180f : Vector3.Angle(a, b);
    }
}
