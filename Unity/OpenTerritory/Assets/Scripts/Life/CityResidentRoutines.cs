using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        readonly Dictionary<int, PlaceData> destinationRooms = new Dictionary<int, PlaceData>();
        readonly List<int> interiorLocations = new List<int>();
        readonly List<int> outdoorLocations = new List<int>();
        readonly Dictionary<LocationData, Vector3> destinationFeet = new Dictionary<LocationData, Vector3>();
        public IReadOnlyList<int> InteriorLocations => interiorLocations;
        public IReadOnlyList<int> OutdoorLocations => outdoorLocations;
        static bool Housing(PlaceData room) => room != null && (room.kind == "home" || room.kind == "residence");
        Vector3 Point(LocationData place, int slot = 0)
        {
            if (place.capacity == 1 && destinationFeet.TryGetValue(place, out var saved)) return saved;
            float offset = (slot - (place.capacity - 1) * .5f) * place.spacing;
            var goal = new Vector3(place.x + Mathf.Cos(place.yaw) * offset, 0, place.z - Mathf.Sin(place.yaw) * offset);
            goal.y = ctx.Ground(goal.x, goal.z) + .04f;
            if (place.capacity == 1) destinationFeet[place] = goal;
            return goal;
        }
        void BuildDestinations()
        {
            var places = new List<LocationData>(ctx.Data.locations);
            // Keep authored service points, but give every accessible building
            // several clear pockets connected to the inside of its real door.
            foreach (var room in ctx.Data.places)
            {
                if (room.door == null) continue;
                var entry = room.door.Approach(room.elevation + .04f, -1.8f);
                entry.y = ctx.Ground(entry.x, entry.z) + .04f;
                if (!room.Contains(entry, .55f) || !ctx.Free(entry, .44f, 2.35f)) continue;
                var right = Quaternion.Euler(0, room.yaw * Mathf.Rad2Deg, 0) * Vector3.right;
                int count = 0;
                foreach (float depth in new[] { 2.4f, 4.5f, 6.5f })
                    foreach (float side in new[] { 0f, -2.3f, 2.3f })
                    {
                        if (count >= 4) break;
                        Vector3 at = room.door.Approach(room.elevation + .04f, -depth) + right * side;
                        at.y = ctx.Ground(at.x, at.z) + .04f;
                        if (!room.Contains(at, .75f) || !ctx.Free(at, .44f, 2.35f) || !ClearWalk(entry, at, null, room.door)) continue;
                        bool close = false;
                        foreach (int existing in interiorLocations)
                            if (destinationRooms[existing] == room && CityPhysics.Flat(Point(places[existing]) - at).magnitude < 1.6f) { close = true; break; }
                        if (close) continue;
                        int index = places.Count;
                        string activity = Housing(room) ? (count == 0 ? "home" : count == 1 ? "read" : "rest") :
                            room.kind == "cafe" || room.kind == "restaurant" || room.kind == "bakery" ? "eat" : room.kind == "book" ? "read" : "shop";
                        places.Add(new LocationData { id = "interior-" + room.door.id + "-" + count, activity = activity,
                            x = at.x, z = at.z, yaw = room.yaw + Mathf.PI + (side < 0 ? -.55f : side > 0 ? .55f : 0),
                            capacity = 1, duration = Housing(room) ? 55 : 27 });
                        destinationRooms[index] = room; interiorLocations.Add(index); count++;
                    }
            }
            // Frontages, gardens and paths all become local destinations. A
            // spacing test keeps the pool independent of nav-node density.
            var cells = new HashSet<Vector2Int>();
            foreach (var node in ctx.Data.nav)
            {
                if (node.edges.Length == 0 || !cells.Add(new Vector2Int(Mathf.FloorToInt(node.x / 12), Mathf.FloorToInt(node.z / 12)))) continue;
                var at = new Vector3(node.x, ctx.Ground(node.x, node.z) + .04f, node.z);
                if (Inside(at) != null || ReferenceMap.Active && ReferenceMap.Water(at) || !ctx.Free(at, .45f, 2.35f)) continue;
                bool crossing = false;
                foreach (var edge in node.edges) if (edge.crossing || ctx.LifeNavigation.Crossing(node.id, edge.to)) { crossing = true; break; }
                if (crossing) continue;
                int index = places.Count;
                places.Add(new LocationData { id = "passeio-" + node.id, x = at.x, z = at.z, activity = "wander", capacity = 1,
                    duration = 7, yaw = R(-Mathf.PI, Mathf.PI) });
                outdoorLocations.Add(index);
            }
            ctx.Data.locations = places.ToArray();
        }
        void AssignHome(Resident p, Vector3 position)
        {
            p.HomePlace = null; float best = float.MaxValue;
            foreach (var room in ctx.Data.places)
            {
                if (p.Data.indoor && room.name == p.Data.place) { p.HomePlace = room; return; }
                if (!Housing(room)) continue;
                float distance = CityPhysics.Flat(new Vector3(room.x, 0, room.z) - position).magnitude;
                // Identity gives neighbours different homes instead of sending
                // every passer-by into the nearest front door.
                distance += Mathf.Repeat(p.Data.id * .131f + room.x * .21f + room.z * .17f, 28);
                if (distance < best) { best = distance; p.HomePlace = room; }
            }
        }
        bool Reserve(Resident p, int index, bool arrived = false)
        {
            var place = ctx.Data.locations[index];
            if (!reservations.TryGetValue(index, out var taken)) reservations[index] = taken = new HashSet<int>();
            for (int slot = 0; slot < Mathf.Max(1, place.capacity); slot++)
            {
                if (taken.Contains(slot)) continue;
                Vector3 goal = Point(place, slot);
                if (!CanOccupy(p, goal)) continue;
                p.Location = index; p.Slot = slot; taken.Add(slot); p.Destination = goal;
                p.Activity = p.Data.indoor && !Housing(p.HomePlace) ? "work" : place.activity;
                p.State = arrived ? "activity" : "travel"; p.Age = p.Stuck = p.NoProgress = 0;
                p.Path.Clear(); p.PathIndex = 0; p.Doorway = null; p.RouteSalt = R(1, 10000);
                p.Duration = StayDuration(p, place); p.Pace = R(.88f, 1.18f);
                p.RecentLocations.Enqueue(index); if (p.RecentLocations.Count > 5) p.RecentLocations.Dequeue();
                return true;
            }
            return false;
        }
        float StayDuration(Resident p, LocationData place) => place.activity == "wander" ? R(2.5f, 13) :
            Mathf.Max(8, place.duration) * R(.65f, 1.65f) * (destinationRooms.TryGetValue(p.Location, out var room) && Housing(room) && ctx.Night > .4f ? 1.7f : 1);
        bool OccupySpawn(Resident p)
        {
            var room = Inside(p.Root.position); if (room == null) return false;
            int best = -1; float distance = 1.1f;
            foreach (int index in interiorLocations)
            {
                if (destinationRooms[index] != room) continue;
                float d = CityPhysics.Flat(Point(ctx.Data.locations[index]) - p.Root.position).magnitude;
                if (d < distance) { best = index; distance = d; }
            }
            if (best >= 0 && Reserve(p, best, true)) return true;
            if (!p.Data.indoor) return false;
            p.Destination = p.Root.position; p.State = "activity"; p.Activity = Housing(room) ? "rest" : "work";
            p.Duration = R(15, 45); return true;
        }
        void ChooseDestination(Resident p)
        {
            Release(p); HideProps(p); p.ProgramIndex++;
            string[] routine = p.Data.routine;
            string requested = routine != null && routine.Length > 0 ? routine[random.Next(routine.Length)] : "rest";
            float kind = R(0, 1);
            var ranked = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < ctx.Data.locations.Length; i++)
            {
                var place = ctx.Data.locations[i];
                if (!string.IsNullOrEmpty(place.role) && place.role != p.Data.role || p.Data.child && place.activity == "smoke") continue;
                if (reservations.TryGetValue(i, out var used) && used.Count >= Mathf.Max(1, place.capacity)) continue;
                destinationRooms.TryGetValue(i, out var room);
                if (p.Data.indoor && room != p.HomePlace) continue;
                float distance = CityPhysics.Flat(new Vector3(place.x, p.Root.position.y, place.z) - p.Root.position).magnitude;
                if (distance > (p.Data.indoor ? 30 : 230) || distance < 2) continue;
                float preference = room == null ? (place.activity == "wander" ? (kind < .42f ? 4 : .45f) : place.activity == requested ? 2 : .25f) :
                    Housing(room) ? (kind > .72f ? 4 : .4f) * (room == p.HomePlace ? 3 : .35f) : (kind >= .42f && kind <= .72f ? 5 : .65f);
                if (ctx.Rain > .35f || ctx.Night > .6f) preference *= room != null ? 2.3f : .5f;
                if (p.RecentLocations.Contains(i)) preference *= .06f;
                float weight = preference / (18 + distance) / (1 + (used != null ? used.Count : 0));
                // Exponential races implement weighted choice without sorting
                // by nearest distance or biasing towards array order.
                float score = -Mathf.Log(Mathf.Max(.00001f, R(0, 1))) / Mathf.Max(.00001f, weight);
                ranked.Add(new KeyValuePair<int, float>(i, score));
            }
            ranked.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int attempt = 0; attempt < Mathf.Min(12, ranked.Count); attempt++) if (Reserve(p, ranked[attempt].Key)) return;
            p.State = "activity"; p.Activity = "rest"; p.Duration = R(3, 9); p.Age = p.NoProgress = 0;
        }
        public bool TryInteriorSpawn(Vector3 observer, out Vector3 point)
        {
            point = Vector3.zero; int selected = -1; float best = float.MaxValue;
            foreach (int index in interiorLocations)
            {
                var location = ctx.Data.locations[index];
                if (reservations.TryGetValue(index, out var used) && used.Count > 0) continue;
                Vector3 at = Point(location); float distance = CityPhysics.Flat(at - observer).magnitude;
                if (distance < 12 || distance > CityPopulation.ResidentRadius) continue;
                int neighbours = 0;
                foreach (var person in People) if (CityPhysics.Flat(person.Root.position - at).magnitude < 12) neighbours++;
                float score = R(0, 24) + neighbours * 28 + distance * .12f;
                if (score < best) { best = score; selected = index; point = at; }
            }
            return selected >= 0;
        }
        bool ClearWalk(Vector3 from, Vector3 to, Resident person, DoorData allowedDoor = null)
        {
            Vector3 delta = to - from; float length = delta.magnitude;
            if (length < .05f) return true;
            float radius = person != null ? .40f * person.Root.lossyScale.y : .44f;
            int count = Physics.SphereCastNonAlloc(from + Vector3.up * 1.05f, radius, delta / length, casts, length,
                (1 << CityContext.WorldLayer) | (1 << CityContext.VehicleLayer), QueryTriggerInteraction.Ignore);
            Transform door = allowedDoor != null ? ctx.Node(allowedDoor.id) : null;
            if (count == casts.Length) return false;
            for (int i = 0; i < count; i++) if (!door || !CityPhysics.Belongs(casts[i].collider, door)) return false;
            return true;
        }
        int ReachableNode(Vector3 point, Resident p)
        {
#if CITY_PERFORMANCE_REFERENCE
            return ReferenceReachableNode(point, p);
#else
            var nearest = reachableCandidates; nearest.Clear();
            for (int i = 0; i < ctx.Data.nav.Length; i++)
            {
                var n = ctx.Data.nav[i]; float distance = new Vector2(n.x - point.x, n.z - point.z).sqrMagnitude;
                if (distance < 24 * 24 && n.edges.Length > 0) nearest.Add(new KeyValuePair<int, float>(i, distance));
            }
            nearest.Sort(NodeDistanceComparer.Instance);
            for (int i = 0; i < Mathf.Min(20, nearest.Count); i++)
            {
                var n = ctx.Data.nav[nearest[i].Key]; Vector3 at = new Vector3(n.x, ctx.Ground(n.x, n.z) + .04f, n.z);
                if (ClearWalk(point, at, p)) return nearest[i].Key;
            }
            return -1;
#endif
        }
        bool CrossingEdge(int previous, NavNodeData node)
        {
            var before = ctx.Data.nav[previous];
            if (ctx.LifeNavigation.Crossing(before.id, node.id)) return true;
            foreach (var edge in before.edges) if (edge.to == node.id && edge.crossing) return true;
            return false;
        }
    }
}
