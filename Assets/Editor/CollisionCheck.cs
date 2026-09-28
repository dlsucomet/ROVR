// Assets/Editor/CollisionCheck.cs
// Menu: Tools > ROVR > Check Collisions
// Verifies that the generated worlds (House, Plain, Maze) are solid: nobody can walk through a
// wall, prop, floor or ceiling. Run it after Generate All Three Worlds.
//
//   1. Coverage: every visible object in a world has an enabled, non-trigger collider
//      (the flat Start/Goal pads are deliberately non-solid markers and are skipped).
//   2. Walk-through fuzz: a CharacterController is dropped at random spots and driven along
//      random headings, sliding along whatever it hits, at both a normal frame step and a huge
//      "frame hitch" step. Every move is checked with a line cast; if the controller's centre
//      ever crosses solid geometry, that's a hole.
//   3. Floor and ceiling: it can't be pushed down through the floor, or up through the house roof.
//
// Doors are trigger markers and are ignored on purpose — they sit in open gaps.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

public static class CollisionCheck
{
    const float Radius = 0.3f;   // deliberately slimmer than a typical VR rig, so it's the harder case
    const float Height = 1.8f;
    static readonly string[] WorldNames = { "House", "Plain", "Maze" };

    [MenuItem("Tools/ROVR/Check Collisions")]
    static void RunFromMenu()
    {
        var problems = Check(out string summary);
        Debug.Log(summary);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("ROVR collision check", summary, "OK");
        if (problems.Count > 0)
            foreach (var p in problems) Debug.LogWarning("[ROVR collision] " + p);
    }

    public static List<string> Check(out string summary)
    {
        var problems = new List<string>();
        var log = new StringBuilder();
        Physics.SyncTransforms();
        var rng = new System.Random(20260919);

        foreach (var name in WorldNames)
        {
            var root = GameObject.Find(name);
            if (root == null) { log.AppendLine(name + ": not in scene, skipped"); continue; }

            int before = problems.Count;
            CheckCoverage(root, problems);
            var floor = root.transform.Find("Floor");
            if (floor == null) { problems.Add(name + ": no Floor object found"); continue; }
            Bounds area = floor.GetComponent<Renderer>().bounds;

            int moves = 0;
            moves += FuzzWalk(name, area, rng, 60, 400, 0.04f, problems);   // ~2 m/s at 50 fps
            moves += FuzzWalk(name, area, rng, 40, 150, 0.5f, problems);    // heavy frame hitch
            CheckVertical(name, area, rng, name == "House", problems);

            log.AppendLine(name + ": " + (problems.Count == before ? "PASS" : "FAIL (" + (problems.Count - before) + ")")
                           + " — " + moves + " moves checked");
        }

        summary = (problems.Count == 0 ? "All worlds solid.\n" : problems.Count + " collision problem(s):\n") + log;
        return problems;
    }

    static void CheckCoverage(GameObject root, List<string> problems)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r.name.EndsWith("Pad")) continue; // Start/Goal pads: flat markers, intentionally non-solid

            bool solid = false;
            foreach (var c in r.GetComponentsInParent<Collider>())
                if (c.enabled && !c.isTrigger) { solid = true; break; }

            if (!solid) problems.Add(root.name + ": '" + r.name + "' is visible but has no solid collider");
        }
    }

    static bool IsFree(Vector3 p)
    {
        return !Physics.CheckCapsule(p + Vector3.up * Radius, p + Vector3.up * (Height - Radius), Radius, ~0, QueryTriggerInteraction.Ignore);
    }

    static CharacterController MakeTester(Vector3 pos, out GameObject go)
    {
        go = new GameObject("CollisionTester");
        go.transform.position = pos;
        var cc = go.AddComponent<CharacterController>();
        cc.radius = Radius;
        cc.height = Height;
        cc.center = new Vector3(0f, Height / 2f, 0f);
        cc.stepOffset = 0f;       // same as NavigationController: no hopping onto props
        Physics.SyncTransforms();
        return cc;
    }

    // Returns true if the straight path between two points crosses a solid collider (other than the tester).
    static bool Tunnelled(Vector3 from, Vector3 to, Collider self, out string what)
    {
        what = null;
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 1e-4f) return false;

        foreach (var hit in Physics.RaycastAll(from, d / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == self) continue;
            what = hit.collider.name;
            return true;
        }
        return false;
    }

    static int FuzzWalk(string world, Bounds area, System.Random rng, int walks, int steps, float step, List<string> problems)
    {
        int moves = 0;
        for (int w = 0; w < walks; w++)
        {
            Vector3 start = default;
            bool found = false;
            for (int tries = 0; tries < 50 && !found; tries++)
            {
                start = new Vector3(
                    Mathf.Lerp(area.min.x + 0.5f, area.max.x - 0.5f, (float)rng.NextDouble()),
                    0.05f,
                    Mathf.Lerp(area.min.z + 0.5f, area.max.z - 0.5f, (float)rng.NextDouble()));
                found = IsFree(start);
            }
            if (!found) continue;

            var cc = MakeTester(start, out GameObject go);
            float heading = (float)rng.NextDouble() * 360f;

            for (int i = 0; i < steps; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                Vector3 before = go.transform.position;
                Vector3 centerBefore = before + cc.center;

                cc.Move(dir * step);
                moves++;

                Vector3 after = go.transform.position;
                if (Tunnelled(centerBefore, after + cc.center, cc, out string what))
                {
                    problems.Add(world + ": passed through '" + what + "' near " + Fmt(after) + " (step " + step + " m)");
                    break;
                }

                // Blocked or randomly re-aim, so the walk keeps sliding along walls instead of wandering off.
                if ((after - before).magnitude < step * 0.5f || rng.NextDouble() < 0.01)
                    heading = (float)rng.NextDouble() * 360f;
            }

            Object.DestroyImmediate(go);
        }
        return moves;
    }

    static void CheckVertical(string world, Bounds area, System.Random rng, bool hasRoof, List<string> problems)
    {
        for (int n = 0; n < 30; n++)
        {
            Vector3 p = new Vector3(
                Mathf.Lerp(area.min.x + 0.5f, area.max.x - 0.5f, (float)rng.NextDouble()), 0.05f,
                Mathf.Lerp(area.min.z + 0.5f, area.max.z - 0.5f, (float)rng.NextDouble()));
            if (!IsFree(p)) continue;

            var cc = MakeTester(p, out GameObject go);

            for (int i = 0; i < 40; i++) cc.Move(Vector3.down * 0.5f);
            if (go.transform.position.y < -0.15f)
                problems.Add(world + ": pushed down through the floor near " + Fmt(p));

            if (hasRoof)
            {
                for (int i = 0; i < 40; i++) cc.Move(Vector3.up * 0.5f);
                float top = go.transform.position.y + Height;
                if (top > WorldKit.WallHeight + 0.15f)
                    problems.Add(world + ": pushed up through the roof near " + Fmt(p));
            }

            Object.DestroyImmediate(go);
        }
    }

    static string Fmt(Vector3 v) { return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")"; }
}
