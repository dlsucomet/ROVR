// Assets/ROVR/RoutePlanner.cs
// Finds a walkable way for the body to a point or an object: round furniture, through doorways. A
// navigation mesh of the world the player is in is built at run time from its colliders (Unity's
// built-in AI module, no package), the first time a route is wanted there (or by Prepare, right after a
// teleport), sized for the body's radius so every route fits through.
//
// Used by NavigationController for "go to the table" (only when the straight way there is blocked does
// the route have extra corners) and for "get me out of this room". Without a mesh (or no way found) the
// callers fall back to walking straight, which the walls still stop.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ROVR
{
    public static class RoutePlanner
    {
        const float Area = 130f;          // m square of world built round the player: any whole world fits
        const float AreaHeight = 12f;
        const float SnapDistance = 2f;    // how far a point may be from the mesh and still be used

        static NavMeshDataInstance _instance;
        static Bounds _built;
        static bool _have;
        static float _radius;
        static readonly NavMeshPath _path = new NavMeshPath();

        // Builds the mesh round this point now (e.g. right after a teleport), so the first route there
        // doesn't wait for it.
        public static void Prepare(Vector3 around, float radius)
        {
            Ensure(around, radius);
        }

        public static void Clear()
        {
            if (_have) _instance.Remove();
            _have = false;
        }

        // A route from `from` to a point beside `target` (gap metres from its edge, plus the body's radius),
        // as corners from `from` to that point. False when there's no mesh or no way there.
        public static bool TryRouteTo(Transform target, Vector3 from, float radius, float gap, List<Vector3> corners)
        {
            if (target == null || !TryApproach(target, from, radius, gap, out Vector3 stand)) return false;
            return TryRoute(from, stand, radius, corners);
        }

        public static bool TryRoute(Vector3 from, Vector3 to, float radius, List<Vector3> corners)
        {
            corners.Clear();
            if (!Ensure(from, radius)) return false;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, SnapDistance, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit b, SnapDistance, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete) return false;
            foreach (var c in _path.corners) corners.Add(c);
            if (corners.Count > 0) corners[0] = from;
            return corners.Count >= 2;
        }

        // Where to stand to be beside the object: on the side nearest `from`, `gap` m from its footprint.
        public static bool TryApproach(Transform target, Vector3 from, float radius, float gap, out Vector3 stand)
        {
            stand = from;
            if (!TryFootprint(target, out Bounds b)) return false;
            Vector3 edge = b.ClosestPoint(new Vector3(from.x, b.center.y, from.z));
            Vector3 outward = new Vector3(from.x - edge.x, 0f, from.z - edge.z);
            if (outward.sqrMagnitude < 0.0001f) outward = new Vector3(from.x - b.center.x, 0f, from.z - b.center.z);
            if (outward.sqrMagnitude < 0.0001f) outward = Vector3.back;
            outward.Normalize();
            stand = new Vector3(edge.x, from.y, edge.z) + outward * (radius + gap);

            // The spot itself may be inside something else (a chair by the table): the nearest walkable one.
            if (Ensure(from, radius) && NavMesh.SamplePosition(stand, out NavMeshHit hit, SnapDistance, NavMesh.AllAreas))
                stand = hit.position;
            return true;
        }

        static bool TryFootprint(Transform target, out Bounds bounds)
        {
            bounds = default;
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

        static bool Ensure(Vector3 around, float radius)
        {
            if (_have && Mathf.Approximately(radius, _radius) && _built.Contains(new Vector3(around.x, _built.center.y, around.z)))
                return true;
            Clear();
            _radius = radius;

            var bounds = new Bounds(new Vector3(around.x, around.y + AreaHeight / 2f - 2f, around.z), new Vector3(Area, AreaHeight, Area));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);

            // Triggers (doorway markers, the Maze's goal) never block; the player's own body, its minimap and
            // hands are not obstacles.
            var player = Object.FindAnyObjectByType<NavigationController>();
            sources.RemoveAll(s =>
            {
                var c = s.component as Collider;
                if (c != null && (c.isTrigger || !c.enabled)) return true;
                return player != null && s.component != null && s.component.transform.IsChildOf(player.transform);
            });
            if (sources.Count == 0) return false;

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = radius;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.2f;   // NavigationController's step offset
            settings.agentSlope = 45f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = Mathf.Max(0.08f, radius / 3f);

            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) return false;
            _instance = NavMesh.AddNavMeshData(data);
            _built = bounds;
            _have = _instance.valid;
            return _have;
        }
    }
}
