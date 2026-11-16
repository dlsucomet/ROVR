// Assets/ROVR/HouseRooms.cs
// The House's rooms and doorways, in the House root's own coordinates (metres; the root is its south-west
// corner). They match the walls in Assets/Editor/HouseGenerator.cs: change both together.
//
//   living room (0..9, 0..10)      hall (9..15, 0..20)      kitchen and dining (15..24, 0..20)
//   bedroom (0..9, 10..20)                                  guest bathroom (15..19, 15..20)
//   bedroom bathroom (0..4, 15..20)
//
// Used by the house minimap (room names, the room the user is in) and by "get me out of this room"
// (TryWayOut). Single storey, so there's no floor to choose.
using UnityEngine;

namespace ROVR
{
    public static class HouseRooms
    {
        public struct Room
        {
            public string name;
            public Rect area;     // x, z in the House's coordinates
            public int priority;  // a smaller room inside a bigger one wins (the bathrooms)
        }

        public struct Doorway
        {
            public Vector2 at;      // the middle of the gap, x, z
            public Vector2 across;  // unit, pointing from `a` to `b`
            public string a, b;     // the rooms either side ("outside" for the front door)
        }

        public static readonly Room[] Rooms =
        {
            new Room { name = "living room", area = Rect.MinMaxRect(0f, 0f, 9f, 10f) },
            new Room { name = "bedroom", area = Rect.MinMaxRect(0f, 10f, 9f, 20f) },
            new Room { name = "bedroom bathroom", area = Rect.MinMaxRect(0f, 15f, 4f, 20f), priority = 1 },
            new Room { name = "hall", area = Rect.MinMaxRect(9f, 0f, 15f, 20f) },
            new Room { name = "kitchen", area = Rect.MinMaxRect(15f, 0f, 24f, 20f) },
            new Room { name = "guest bathroom", area = Rect.MinMaxRect(15f, 15f, 19f, 20f), priority = 1 },
        };

        public static readonly Doorway[] Doorways =
        {
            new Doorway { at = new Vector2(12f, 0f), across = new Vector2(0f, -1f), a = "hall", b = "outside" },
            new Doorway { at = new Vector2(9f, 5f), across = new Vector2(1f, 0f), a = "living room", b = "hall" },
            new Doorway { at = new Vector2(9f, 13f), across = new Vector2(1f, 0f), a = "bedroom", b = "hall" },
            new Doorway { at = new Vector2(15f, 5f), across = new Vector2(1f, 0f), a = "hall", b = "kitchen" },
            new Doorway { at = new Vector2(15f, 17f), across = new Vector2(1f, 0f), a = "hall", b = "guest bathroom" },
            new Doorway { at = new Vector2(4f, 17f), across = new Vector2(-1f, 0f), a = "bedroom", b = "bedroom bathroom" },
        };

        public const float Width = 24f, Depth = 20f;
        const float Beyond = 1.6f; // m past the doorway where "out" ends: the whole body through it

        static Transform _root;

        public static Transform Root
        {
            get
            {
                if (_root == null) { var go = GameObject.Find("House"); _root = go != null ? go.transform : null; }
                return _root;
            }
        }

        // The room at this world position, or null (outside the house, or no house).
        public static string RoomAt(Vector3 world)
        {
            var root = Root;
            if (root == null) return null;
            Vector3 p = root.InverseTransformPoint(world);
            string best = null;
            int bestPriority = -1;
            foreach (var r in Rooms)
                if (r.area.Contains(new Vector2(p.x, p.z)) && r.priority > bestPriority) { best = r.name; bestPriority = r.priority; }
            return best;
        }

        // The way out of the room the body is in: a spot just through the nearest doorway that leads to a
        // bigger space (the hall, or outside from the hall; from a bathroom, the room it opens into), and a
        // point further on to face. False outside the house.
        public static bool TryWayOut(Vector3 body, out Vector3 point, out Vector3 face, out string room, out string into)
        {
            point = face = body;
            room = RoomAt(body);
            into = null;
            var root = Root;
            if (room == null || root == null) return false;

            float best = float.MaxValue;
            bool found = false;
            Vector3 local = root.InverseTransformPoint(body);
            foreach (var d in Doorways)
            {
                string other = d.a == room ? d.b : d.b == room ? d.a : null;
                if (other == null) continue;
                // From the hall, out means outside; from a room, it means toward the hall, not into its bathroom.
                bool smaller = other.Contains("bathroom") && !room.Contains("bathroom");
                float cost = Vector2.Distance(new Vector2(local.x, local.z), d.at) + (smaller ? 100f : 0f);
                if (cost >= best) continue;
                best = cost;
                Vector2 outward = d.a == room ? d.across : -d.across;
                Vector2 p = d.at + outward * Beyond, f = d.at + outward * (Beyond + 3f);
                point = root.TransformPoint(new Vector3(p.x, local.y, p.y));
                face = root.TransformPoint(new Vector3(f.x, local.y + 1.6f, f.y));
                into = other;
                found = true;
            }
            return found;
        }
    }
}
