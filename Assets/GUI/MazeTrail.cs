// Assets/GUI/MazeTrail.cs
// A glowing trail on the Maze's floor along the correct route, from the start pad to the exit pad, with
// chevrons pointing the way (ROVR/GlowTrail shader). The route is found when Play starts, from the maze's own
// wall colliders (a breadth-first search over its 5 m cells), so it always matches the generated maze. The
// study's checkpoints sit on this route.
//
// No collider and no SceneLabel: the trail never blocks movement and ROVR can't name it.
using System.Collections.Generic;
using UnityEngine;

public class MazeTrail : MonoBehaviour
{
    public const float CellSize = 5f; // the maze's cells; MazeGenerator builds with this
    const float Width = 0.6f;
    const float Corner = 1.2f;     // radius of the rounded turns
    const float Lift = 0.02f;      // above the floor, so it never flickers into it

    public List<Vector3> Cells { get; private set; } = new List<Vector3>(); // world cell centres, entrance to exit

    public static MazeTrail Build(Transform maze)
    {
        var existing = maze.GetComponentInChildren<MazeTrail>();
        if (existing != null) return existing;

        var go = new GameObject("Maze Trail");
        go.transform.SetParent(maze, false);
        var trail = go.AddComponent<MazeTrail>();
        trail.Cells = FindRoute(maze, out Vector3 start, out Vector3 exit);
        if (trail.Cells.Count == 0)
        {
            Debug.LogWarning("[ROVR] Maze trail: no route found from the Start to the Goal under " + maze.name + ".");
            return trail;
        }
        var points = new List<Vector3> { start };
        points.AddRange(trail.Cells);
        points.Add(exit);
        trail.Draw(points);
        return trail;
    }

    // Checkpoint n of 3: a quarter, a half and three quarters of the way along the route.
    public Vector3 Checkpoint(int n)
    {
        if (Cells.Count == 0) return transform.position;
        int i = Mathf.RoundToInt(n * (Cells.Count - 1) / 4f);
        return Cells[Mathf.Clamp(i, 0, Cells.Count - 1)];
    }

    // ---------------------------------------------------------------- the route

    static List<Vector3> FindRoute(Transform maze, out Vector3 start, out Vector3 exit)
    {
        start = exit = maze.position;
        var route = new List<Vector3>();

        Bounds walls = default;
        bool any = false;
        foreach (var c in maze.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !c.CompareTag("Wall")) continue;
            if (!any) { walls = c.bounds; any = true; } else walls.Encapsulate(c.bounds);
        }
        var startMarker = maze.Find("Start");
        Transform goal = null;
        foreach (var t in maze.GetComponentsInChildren<Transform>())
            if (t.CompareTag("Goal")) { goal = t; break; }
        if (!any || startMarker == null || goal == null) return route;
        start = startMarker.position;
        exit = goal.position;

        // The maze's corner is its root; walls stick out half their thickness past the grid.
        Vector3 far = maze.InverseTransformPoint(walls.max);
        int cols = Mathf.RoundToInt(far.x / CellSize), rows = Mathf.RoundToInt(far.z / CellSize);
        if (cols <= 0 || rows <= 0) return route;
        Physics.SyncTransforms();

        Vector2Int from = CellOf(maze, start, cols, rows), to = CellOf(maze, exit, cols, rows);
        var previous = new Dictionary<Vector2Int, Vector2Int> { { from, from } };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (cell == to) break;
            foreach (var step in steps)
            {
                var next = cell + step;
                if (next.x < 0 || next.y < 0 || next.x >= cols || next.y >= rows || previous.ContainsKey(next)) continue;
                if (!Open(Centre(maze, cell), Centre(maze, next))) continue;
                previous[next] = cell;
                queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return route;

        for (var cell = to; ; cell = previous[cell])
        {
            route.Add(Centre(maze, cell));
            if (cell == from) break;
        }
        route.Reverse();
        for (int i = 0; i < route.Count; i++) route[i] = new Vector3(route[i].x, maze.position.y, route[i].z);
        return route;
    }

    static Vector3 Centre(Transform maze, Vector2Int cell)
    {
        return maze.TransformPoint(new Vector3((cell.x + 0.5f) * CellSize, 1.5f, (cell.y + 0.5f) * CellSize));
    }

    static Vector2Int CellOf(Transform maze, Vector3 world, int cols, int rows)
    {
        Vector3 local = maze.InverseTransformPoint(world);
        return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(local.x / CellSize), 0, cols - 1),
                              Mathf.Clamp(Mathf.FloorToInt(local.z / CellSize), 0, rows - 1));
    }

    // No hedge between two neighbouring cells' centres.
    static bool Open(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        foreach (var hit in Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.CompareTag("Wall")) return false;
        return true;
    }

    // ---------------------------------------------------------------- the strip

    void Draw(List<Vector3> worldPoints)
    {
        var shader = Shader.Find("ROVR/GlowTrail");
        if (shader == null) { Debug.LogWarning("[ROVR] No ROVR/GlowTrail shader (Assets/Resources/Shaders), so the maze has no trail."); return; }

        var local = new List<Vector3>();
        foreach (var p in worldPoints)
        {
            var l = transform.InverseTransformPoint(p);
            local.Add(new Vector3(l.x, Lift, l.z));
        }
        var points = Rounded(local);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float along = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) along += Vector3.Distance(points[i], points[i - 1]);
            Vector3 dir = i == 0 ? points[1] - points[0] : i == points.Count - 1 ? points[i] - points[i - 1] : points[i + 1] - points[i - 1];
            dir.y = 0f;
            dir.Normalize();
            Vector3 right = new Vector3(dir.z, 0f, -dir.x) * (Width / 2f);
            vertices.Add(points[i] - right);
            vertices.Add(points[i] + right);
            uvs.Add(new Vector2(along, 0f)); // u: metres along the route, v: 0 left edge, 1 right edge
            uvs.Add(new Vector2(along, 1f));
            if (i == 0) continue;
            int a = 2 * (i - 1);
            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }

        var mesh = new Mesh { name = "Maze Trail" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(shader);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Each right-angle turn becomes a smooth curve; near-duplicate points are dropped.
    static List<Vector3> Rounded(List<Vector3> p)
    {
        var o = new List<Vector3> { p[0] };
        for (int i = 1; i < p.Count - 1; i++)
        {
            Vector3 a = p[i - 1], b = p[i], c = p[i + 1];
            Vector3 into = b - a, outOf = c - b;
            if (Vector3.Angle(into, outOf) < 1f) { Add(o, b); continue; }
            float r = Mathf.Min(Corner, into.magnitude / 2f, outOf.magnitude / 2f);
            Vector3 s = b - into.normalized * r, e = b + outOf.normalized * r;
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f;
                Add(o, (1 - t) * (1 - t) * s + 2 * (1 - t) * t * b + t * t * e);
            }
        }
        Add(o, p[p.Count - 1]);
        return o;
    }

    static void Add(List<Vector3> points, Vector3 p)
    {
        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) > 0.01f) points.Add(p);
    }
}
