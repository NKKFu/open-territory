using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Derived offline from the physical street layout, never a second world.
    public sealed class CityLifeNavigation
    {
        [Serializable] public sealed class Circuit { public string id; public Vector3[] points; public float length; }
        [Serializable] public sealed class Edge { public int a, b; }
        [Serializable] sealed class Data
        {
            public string layoutId;
            public Circuit[] routes = Array.Empty<Circuit>();
            public Edge[] blockedEdges = Array.Empty<Edge>(), crossingEdges = Array.Empty<Edge>();
        }
        readonly HashSet<long> blocked = new HashSet<long>(), crossings = new HashSet<long>();
        readonly System.Random random = new System.Random(30179);
        struct RoadSegment { public Vector3 a, b; public float radius; }
        readonly Dictionary<Vector2Int, List<RoadSegment>> roads = new Dictionary<Vector2Int, List<RoadSegment>>();
        public Circuit[] Circuits { get; private set; } = Array.Empty<Circuit>();
        static long Key(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        public CityLifeNavigation(WorldData world)
        {
            var asset = Resources.Load<TextAsset>("living-city");
            if (!asset) return;
            var data = JsonUtility.FromJson<Data>(asset.text);
            if (data.layoutId != world.layoutId) return;
            Circuits = data.routes;
            foreach (var edge in data.blockedEdges) blocked.Add(Key(edge.a, edge.b));
            foreach (var edge in data.crossingEdges) crossings.Add(Key(edge.a, edge.b));
            if (ReferenceMap.Active) foreach (var road in ReferenceMap.Layout.roads)
            {
                if (road.kind != "street") continue;
                for (int i = 0; i + 1 < road.points.Length; i++)
                {
                    var a = road.points[i]; var b = road.points[i + 1];
                    var cell = new Vector2Int(Mathf.FloorToInt(a.x / 32), Mathf.FloorToInt(a.z / 32));
                    if (!roads.TryGetValue(cell, out var segments)) roads[cell] = segments = new List<RoadSegment>();
                    segments.Add(new RoadSegment { a = a, b = b, radius = road.width * .5f });
                }
            }
        }
        public bool Blocked(int a, int b) => blocked.Contains(Key(a, b));
        public bool Crossing(int a, int b) => crossings.Contains(Key(a, b));
        public float RoadClearance(Vector3 point)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(point.x / 32), Mathf.FloorToInt(point.z / 32));
            float best = float.NegativeInfinity;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                if (roads.TryGetValue(cell + new Vector2Int(x, z), out var segments)) foreach (var segment in segments)
                {
                    var d = CityPhysics.Flat(segment.b - segment.a);
                    float t = Mathf.Clamp01(Vector3.Dot(CityPhysics.Flat(point - segment.a), d) / Mathf.Max(.001f, d.sqrMagnitude));
                    best = Mathf.Max(best, segment.radius - CityPhysics.Flat(point - segment.a - d * t).magnitude);
                }
            return best;
        }
        public Vector3[] ChooseCircuit(CityContext ctx, Vector3 observer)
        {
            Vector3[] selected = null; float total = 0;
            foreach (var circuit in Circuits)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < circuit.points.Length; i += 6)
                    nearest = Mathf.Min(nearest, CityPhysics.Flat(circuit.points[i] - observer).sqrMagnitude);
                if (nearest > CityPopulation.VehicleRadius * CityPopulation.VehicleRadius) continue;
                int users = 0;
                foreach (var car in ctx.Vehicles.Fleet) if (car.Route == circuit.points) users++;
                float weight = 1f / (1 + users * 4);
                total += weight;
                if (random.NextDouble() * total < weight) selected = circuit.points;
            }
            return selected;
        }
    }
}
