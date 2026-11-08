using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // The scene owns the authored geometry. This component only connects it to
    // the normal city seats, destinations and pedestrian graph at startup.
    public sealed class CityLinearPark : MonoBehaviour
    {
        [Serializable] public sealed class RetiredObject { public GameObject target; public bool active; }
        [Serializable] public sealed class RetiredCollider { public Collider target; public bool enabled; }
        [Serializable] public sealed class MeshChange { public MeshFilter target; public Mesh original; }
        [Serializable] public sealed class FloorChange { public MeshCollider target; public Mesh original; }
        [Serializable] public sealed class Path { public string name; public float width; public Vector3[] points; }
        [Serializable] public sealed class NavLink { public int a, b, externalId; }

        public static readonly int[] RetiredPlaces = { 56, 48, 36, 27, 23 };
        public RetiredObject[] retiredObjects = Array.Empty<RetiredObject>();
        public RetiredCollider[] retiredColliders = Array.Empty<RetiredCollider>();
        public MeshChange[] changedMeshes = Array.Empty<MeshChange>();
        public FloorChange[] changedFloors = Array.Empty<FloorChange>();
        public int[] retiredSolidSlots = Array.Empty<int>(), retiredSeats = Array.Empty<int>(), retiredNodes = Array.Empty<int>();
        public Path[] paths = Array.Empty<Path>();
        public SeatData[] seats = Array.Empty<SeatData>();
        public Transform[] seatNodes = Array.Empty<Transform>();
        public Light[] lights = Array.Empty<Light>();
        public Vector3[] navPoints = Array.Empty<Vector3>();
        public NavLink[] navLinks = Array.Empty<NavLink>();
        public Vector3 playground, pergola, exercise;
        public int trees, shrubs, smallRocks, flowerBeds, preservedColliders, removedYardTriangles;

        public static void Bind(CityContext ctx)
        {
            var park = FindFirstObjectByType<CityLinearPark>();
            if (!park) return;
            foreach (int i in park.retiredSolidSlots)
                if (i >= 0 && i < ctx.Data.solids.Length) ctx.Data.solids[i].active = false;
            var removedNodes = new HashSet<int>(park.retiredNodes);
            ctx.Data.destructibles = ctx.Data.destructibles.Where(d => !removedNodes.Contains(d.id)).ToArray();
            foreach (int id in removedNodes) ctx.Nodes.Remove(id);
            var retired = new HashSet<int>(RetiredPlaces);
            foreach (int i in retired)
            {
                var p = ctx.Data.places[i]; p.kind = "retired"; p.name = ""; p.door = null;
                p.w = p.d = p.lotWidth = p.lotDepth = .001f;
            }
            var homes = FindFirstObjectByType<CityHomeCatalog>();
            if (homes)
            {
                var removedRoots = homes.homes.Where(h => retired.Contains(h.place) && h.root).Select(h => h.root).ToArray();
                homes.doors = homes.doors.Where(d => d.leaf && !removedRoots.Any(r => d.leaf.IsChildOf(r))).ToArray();
                homes.rooms = homes.rooms.Where(r => !retired.Contains(r.place)).ToArray();
                homes.homes = homes.homes.Where(h => !retired.Contains(h.place)).ToArray();
            }
            var seats = ctx.Data.seats.Where(s => !park.retiredSeats.Contains(s.id)).ToList();
            for (int i = 0; i < park.seats.Length && i < park.seatNodes.Length; i++)
                if (park.seatNodes[i] && !ctx.Nodes.ContainsKey(park.seats[i].id))
                { ctx.Nodes.Add(park.seats[i].id, park.seatNodes[i]); seats.Add(park.seats[i]); }
            ctx.Data.seats = seats.ToArray();

            var nodes = ctx.Data.nav.ToList();
            var byId = nodes.ToDictionary(n => n.id);
            int first = nodes.Max(n => n.id) + 1;
            for (int i = 0; i < park.navPoints.Length; i++)
            {
                var p = park.navPoints[i]; var n = new NavNodeData { id = first+i, x = p.x, z = p.z };
                nodes.Add(n); byId.Add(n.id, n);
            }
            foreach (var link in park.navLinks)
            {
                if (link.a < 0 || link.a >= park.navPoints.Length) continue;
                var a = byId[first+link.a]; NavNodeData b;
                if (link.b >= 0 && link.b < park.navPoints.Length) b = byId[first+link.b];
                else if (!byId.TryGetValue(link.externalId, out b)) continue;
                float cost = Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
                a.edges = a.edges.Concat(new[]{new NavEdgeData { to=b.id, cost=cost }}).ToArray();
                b.edges = b.edges.Concat(new[]{new NavEdgeData { to=a.id, cost=cost }}).ToArray();
            }
            ctx.Data.nav = nodes.ToArray();
            var pockets = new[] { park.pergola, park.exercise, park.navPoints[Mathf.Min(park.navPoints.Length-1, 12)] };
            ctx.Data.locations = ctx.Data.locations.Concat(pockets.Select((p,i) => new LocationData {
                id="bay-gardens-"+i, x=p.x, z=p.z, activity=i==1?"wander":"rest", capacity=1, duration=18
            })).ToArray();
        }

        void Update()
        {
            var ctx = CityContext.Instance; if (!ctx || !ctx.Ready) return;
            float glow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .7f, ctx.Night));
            foreach (var light in lights) if (light) { light.enabled = glow > .01f; light.intensity = glow * 1.7f; }
        }
    }
}
