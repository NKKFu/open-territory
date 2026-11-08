using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // The imported art is the pool. Controllers, rigs, physics and audio are
    // attached on first spawn; only the nearby population joins simulation.
    [DefaultExecutionOrder(-180)]
    public sealed class CityPopulation : MonoBehaviour
    {
        public const int ResidentLimit = 32, VehicleLimit = 14, TrafficLimit = 10;
        public const float ResidentRadius = 95, VehicleRadius = 155;
        public const float ResidentRetireRadius = 145, VehicleRetireRadius = 225;
        sealed class PersonSlot { public ResidentData data; public Vector3 home; public Quaternion rotation; public bool anchored; public float retry; }
        sealed class CarSlot { public VehicleData data; public Vector3 home; public Quaternion rotation; public float retry; }
        CityContext ctx;
        readonly List<PersonSlot> people = new List<PersonSlot>();
        readonly List<CarSlot> cars = new List<CarSlot>();
        readonly Dictionary<Vector2Int, List<NavNodeData>> navigation = new Dictionary<Vector2Int, List<NavNodeData>>();
        readonly List<NavNodeData> nearby = new List<NavNodeData>();
        readonly Dictionary<Transform, float> distantSince = new Dictionary<Transform, float>();
        readonly HashSet<int> requestedCars = new HashSet<int>();
        readonly Dictionary<int, Collider[]> poseContacts = new Dictionary<int, Collider[]>();
        readonly Dictionary<int, Renderer[]> spawnRenderers = new Dictionary<int, Renderer[]>();
        readonly Collider[] overlap = new Collider[64];
        readonly Plane[] planes = new Plane[6];
        readonly System.Random random = new System.Random(71903);
        int personCursor, carCursor;
        float next;
        public int ResidentSpawns { get; private set; }
        public int VehicleSpawns { get; private set; }
        public int ResidentRetirements { get; private set; }
        public int VehicleRetirements { get; private set; }
        public int ResidentDefinitions => people.Count;
        public int VehicleDefinitions => cars.Count;

        public void Initialize(CityContext context)
        {
            ctx = context;
            foreach (var data in ctx.Data.residents)
            {
                var root = ctx.Node(data.id); if (!root) continue;
                people.Add(new PersonSlot { data = data, home = root.position, rotation = root.rotation,
                    anchored = data.stationary || data.sitting || data.indoor || data.performance });
                root.gameObject.SetActive(false);
            }
            foreach (var data in ctx.Data.vehicles)
            {
                var root = ctx.Node(data.id); if (!root) continue;
                cars.Add(new CarSlot { data = data, home = root.position, rotation = root.rotation });
                root.gameObject.SetActive(false);
            }
            foreach (var node in ctx.Data.nav)
            {
                if (node.edges.Length == 0 || Array.Exists(node.edges, e => e.crossing || ctx.LifeNavigation.Crossing(node.id, e.to))) continue;
                var cell = Cell(new Vector3(node.x, 0, node.z));
                if (!navigation.TryGetValue(cell, out var entries)) navigation[cell] = entries = new List<NavNodeData>();
                entries.Add(node);
            }
            foreach (var slot in people) CollectPoseContacts(slot);
            next = .25f;
        }
        void CollectPoseContacts(PersonSlot slot)
        {
            if (!slot.data.sitting && !slot.data.performance) return;
            var contacts = new List<Collider>(); SeatData seat = null;
            foreach (var candidate in ctx.Data.seats)
                if (candidate.occupied && Distance(candidate.position, slot.home) < .15f) { seat = candidate; break; }
            Bounds guitar = new Bounds(); bool hasGuitar = false;
            if (slot.data.id == ctx.Data.music.musician)
            {
                var root = ctx.Node(ctx.Data.music.guitar);
                if (root) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                { if (!hasGuitar) guitar = renderer.bounds; else guitar.Encapsulate(renderer.bounds); hasGuitar = true; }
            }
            float scale = ctx.Node(slot.data.id).lossyScale.y, radius = .42f * scale;
            int count = Physics.OverlapCapsuleNonAlloc(slot.home + Vector3.up * (radius + .05f), slot.home + Vector3.up * (2.32f * scale - radius),
                radius, overlap, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var contact = overlap[i]; var box = contact as BoxCollider;
                if (!box) continue;
                var bounds = box.bounds;
                if (seat != null)
                {
                    var local = Quaternion.Euler(0, -seat.yaw * Mathf.Rad2Deg, 0) * (bounds.center - seat.position);
                    // Only the seat and its low backrest may intersect its own
                    // seated character. Walls, other furniture and actors still block.
                    if (Mathf.Abs(local.x) < seat.width * .5f + .1f && Mathf.Abs(local.z) < seat.depth * .5f + .1f &&
                        box.size.x <= seat.width + .1f && box.size.z <= seat.depth + .1f &&
                        bounds.min.y >= seat.position.y - .15f && bounds.max.y <= seat.position.y + seat.seatTop + 1)
                    { contacts.Add(contact); continue; }
                }
                if (hasGuitar && guitar.Contains(bounds.center) && box.size.x < 1.3f && box.size.z < .5f && box.size.y < 1.3f) contacts.Add(contact);
            }
            poseContacts[slot.data.id] = contacts.ToArray();
        }
        public Collider[] PoseContacts(int id) => poseContacts.TryGetValue(id, out var contacts) ? contacts : Array.Empty<Collider>();
        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 64), Mathf.FloorToInt(p.z / 64));
        static float Distance(Vector3 a, Vector3 b) => CityPhysics.Flat(a - b).magnitude;
        public void RequestVehicle(int id) { requestedCars.Add(id); next = Mathf.Min(next, ctx.Clock); }

        // Reuse a police model outside the camera, with the same support and
        // clearance tests as ordinary population. Never pop an officer into view.
        public bool TrySpawnResponder(int id, Vector3 incident)
        {
            if (!enabled || ctx.Paused || ctx.Residents.People.Count >= ResidentLimit || ctx.Residents.IsSpawned(id)) return false;
            var slot = people.Find(s => s.data.id == id && s.data.role == "police"); if (slot == null) return false;
            GeometryUtility.CalculateFrustumPlanes(ctx.Camera, planes);
            foreach (var node in ctx.Data.nav)
            {
                if (node.edges.Length == 0) continue;
                var point = new Vector3(node.x, 0, node.z); float distance = Distance(point, incident);
                if (distance < 26 || distance > 55 || Distance(point, ctx.Player.position) > ResidentRadius) continue;
                point.y = ctx.Ground(point.x, point.z) + .04f;
                if (!Hidden(point + Vector3.up * 1.4f, new Vector3(1.5f, 3, 1.5f)) || !PersonFree(point, slot.data) || !ModelHidden(id, point, slot.rotation)) continue;
                if (!ctx.Residents.Spawn(id, point, slot.rotation)) continue;
                ResidentSpawns++; return true;
            }
            return false;
        }

        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused || ctx.Clock < next) return;
            next = ctx.Clock + .35f;
            GeometryUtility.CalculateFrustumPlanes(ctx.Camera, planes);
            RetireDistant();
            nearby.Clear(); var cell = Cell(ctx.Player.position);
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                if (navigation.TryGetValue(cell + new Vector2Int(x, z), out var entries)) nearby.AddRange(entries);
            // Anchored shopkeepers and seated people keep their authored places.
            int budget = 3;
            for (int pass = 0; pass < 2 && budget > 0; pass++)
                for (int n = 0; n < people.Count && budget > 0; n++)
                {
                    var slot = people[(personCursor + n) % people.Count];
                    if (slot.anchored != (pass == 0) || ctx.Clock < slot.retry || ctx.Residents.IsSpawned(slot.data.id)) continue;
                    if (ctx.Residents.People.Count >= (slot.anchored ? ResidentLimit : 24)) continue;
                    if (!PersonPoint(slot, out var at)) continue;
                    if (ctx.Residents.Spawn(slot.data.id, at, slot.rotation)) { ResidentSpawns++; budget--; }
                }
            if (people.Count > 0) personCursor = (personCursor + 7) % people.Count;
            if (ctx.Vehicles.Fleet.Count >= VehicleLimit) return;
            int traffic = 0; foreach (var car in ctx.Vehicles.Fleet) if (car.Data.auto) traffic++;
            for (int pass = 0; pass < 2; pass++)
                for (int n = 0; n < cars.Count; n++)
                {
                    var slot = cars[(carCursor + n) % cars.Count]; bool requested = requestedCars.Contains(slot.data.id);
                    if (requested != (pass == 0) || ctx.Clock < slot.retry || ctx.Vehicles.IsSpawned(slot.data.id)) continue;
                    if (slot.data.auto && traffic >= TrafficLimit && !requested) continue;
                    if (!CarPoint(slot, out var at, out var rotation)) continue;
                    ctx.Vehicles.Spawn(slot.data, at, rotation); VehicleSpawns++; requestedCars.Remove(slot.data.id);
                    carCursor = (carCursor + n + 1) % cars.Count; return;
                }
        }
        bool PersonPoint(PersonSlot slot, out Vector3 at)
        {
            at = slot.home;
            if (slot.anchored)
            {
                if (Distance(at, ctx.Player.position) < 8 || Distance(at, ctx.Player.position) > ResidentRadius) return false;
                return Hidden(at + Vector3.up * 1.2f, new Vector3(1.4f, 2.8f, 1.4f)) && PersonFree(at, slot.data) && ModelHidden(slot.data.id, at, slot.rotation);
            }
            int visitors = 0, mobile = 0;
            foreach (var person in ctx.Residents.People)
            {
                if (person.Data.indoor || person.Data.stationary || person.Data.sitting || person.Data.performance) continue;
                mobile++;
                if (Array.Exists(ctx.Data.places, room => room.Contains(person.Root.position, .3f))) visitors++;
            }
            if (visitors < Mathf.Max(2, mobile * .28f))
                for (int attempt = 0; attempt < 3; attempt++)
                    if (ctx.Residents.TryInteriorSpawn(ctx.Player.position, out at) && Hidden(at + Vector3.up * 1.2f, new Vector3(1.4f, 2.8f, 1.4f)) && PersonFree(at, slot.data) && ModelHidden(slot.data.id, at, slot.rotation)) return true;
            bool found = false; float best = float.MaxValue; Vector3 selected = slot.home;
            for (int n = 0; n < 20 && nearby.Count > 0; n++)
            {
                var node = nearby[random.Next(nearby.Count)];
                at = new Vector3(node.x + (float)(random.NextDouble() - .5) * .7f, 0, node.z + (float)(random.NextDouble() - .5) * .7f);
                float distance = Distance(at, ctx.Player.position); if (distance < 24 || distance > ResidentRadius) continue;
                at.y = ctx.Ground(at.x, at.z) + .04f;
                Vector3 candidate = at;
                if (Array.Exists(ctx.Data.places, place => place.Contains(candidate, -.5f))) continue;
                int neighbours = 0;
                foreach (var person in ctx.Residents.People) if (Distance(person.Root.position, at) < 14) neighbours++;
                float score = neighbours * 24 + (float)random.NextDouble() * 12;
                if (score >= best || !Hidden(at + Vector3.up * 1.2f, new Vector3(1.4f, 2.8f, 1.4f)) || !PersonFree(at, slot.data) || !ModelHidden(slot.data.id, at, slot.rotation)) continue;
                best = score; selected = at; found = true;
            }
            at = selected; return found;
        }
        bool PersonFree(Vector3 at, ResidentData data)
        {
            float scale = ctx.Node(data.id).lossyScale.y, height = (data.sitting ? 1.7f : 2.32f) * scale;
            float radius = .42f * scale;
            if (!Supported(at, radius, data.sitting ? .6f : .3f)) return false;
            int mask = ctx.AllMask | (1 << CityContext.PlayerLayer);
            if (data.sitting) mask &= ~(1 << CityContext.GroundLayer);
            int count = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * (radius + .05f), at + Vector3.up * (height - radius), radius,
                overlap, mask, QueryTriggerInteraction.Ignore);
            if (count == overlap.Length) return false;
            var allowed = PoseContacts(data.id);
            for (int i = 0; i < count; i++) if (Array.IndexOf(allowed, overlap[i]) < 0) return false;
            return true;
        }
        bool CarPoint(CarSlot slot, out Vector3 at, out Quaternion rotation)
        {
            at = slot.home; rotation = slot.rotation; var data = slot.data;
            if (!data.auto) return CarFree(data, ref at, rotation, 12);
            var route = ctx.Vehicles.SelectAmbientRoute(data, ctx.Player.position);
            if (route == null || route.Length < 2) return false;
            int start = random.Next(route.Length);
            // Full traversal guarantees nearby road segments are considered even
            // on a large map; expensive physics only runs for points in the ring.
            for (int n = 0; n < route.Length; n++)
            {
                int i = (start + n) % route.Length; at = Vector3.Lerp(route[i], route[(i + 1) % route.Length], .2f + (float)random.NextDouble() * .6f);
                float distance = Distance(at, ctx.Player.position); if (distance < 55 || distance > VehicleRadius) continue;
                var direction = CityPhysics.Flat(route[(i + 1) % route.Length] - route[i]); if (direction.sqrMagnitude < .01f) continue;
                rotation = Quaternion.LookRotation(direction);
                if (CarFree(data, ref at, rotation, 55)) return true;
            }
            return false;
        }
        bool CarFree(VehicleData data, ref Vector3 at, Quaternion rotation, float minimum)
        {
            float distance = Distance(at, ctx.Player.position); if (distance < minimum || distance > VehicleRadius) return false;
            float ground = ctx.Ground(at.x, at.z); at.y = ground + .08f;
            Vector3 size = new Vector3(data.width + .8f, data.kind == "bus" ? 3.8f : 2.7f, data.length + 1.6f);
            Vector3 center = at + rotation * new Vector3(0, size.y * .5f + .25f, data.centerOffset);
            // The conservative world AABB also covers the corners of a rotated car.
            Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right;
            Vector3 extent = new Vector3(Mathf.Abs(right.x) * size.x + Mathf.Abs(forward.x) * size.z, size.y,
                Mathf.Abs(right.z) * size.x + Mathf.Abs(forward.z) * size.z);
            if (!Hidden(center, extent) || !Supported(at, data.width * .5f, .65f) || !ModelHidden(data.id, at, rotation)) return false;
            foreach (float side in new[] { -1f, 1f }) foreach (float end in new[] { -1f, 1f })
            {
                var wheel = at + rotation * new Vector3(side * data.width * .38f, 0, end * data.wheelbase * .5f);
                if (Mathf.Abs(ctx.Ground(wheel.x, wheel.z) - ground) > 1.6f) return false;
            }
            if (Physics.CheckBox(center, size * .5f, rotation, ctx.AllMask | (1 << CityContext.PlayerLayer), QueryTriggerInteraction.Ignore)) return false;
            foreach (var car in ctx.Vehicles.Fleet)
                if (Distance(at, car.Position) < (data.length + car.Data.length) * .5f + 7) return false;
            return true;
        }
        bool Supported(Vector3 at, float radius, float step)
        {
            if (!Physics.Raycast(at + Vector3.up * 2, Vector3.down, out var hit, 4, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore) || hit.normal.y < .7f) return false;
            if (ReferenceMap.Active && ReferenceMap.Water(at)) return false;
            for (int i = 0; i < 4; i++)
            {
                var offset = Quaternion.Euler(0, i * 90, 0) * Vector3.forward * radius;
                if (Mathf.Abs(ctx.Ground(at.x + offset.x, at.z + offset.z) - hit.point.y) > step) return false;
            }
            return true;
        }
        bool Hidden(Vector3 center, Vector3 size)
        {
            var bounds = new Bounds(center, size);
            if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return true;
            int mask = (1 << CityContext.WorldLayer) | (1 << CityContext.GroundLayer);
            var eye = ctx.Camera.transform.position;
            if (!Physics.Linecast(eye, center, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!Physics.Linecast(eye, center + Vector3.up * size.y * .45f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!Physics.Linecast(eye, center - Vector3.up * size.y * .4f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!Physics.Linecast(eye, center + ctx.Camera.transform.right * size.magnitude * .35f, mask, QueryTriggerInteraction.Ignore)) return false;
            return Physics.Linecast(eye, center - ctx.Camera.transform.right * size.magnitude * .35f, mask, QueryTriggerInteraction.Ignore);
        }
        bool ModelHidden(int id, Vector3 position, Quaternion rotation)
        {
            var root = ctx.Node(id);
            if (!spawnRenderers.TryGetValue(id, out var renderers)) spawnRenderers[id] = renderers = root.GetComponentsInChildren<Renderer>(true);
            var transform = Matrix4x4.TRS(position, rotation, root.lossyScale) * root.worldToLocalMatrix;
            int mask = (1 << CityContext.WorldLayer) | (1 << CityContext.GroundLayer);
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.enabled) continue;
                bool active = true;
                for (var part = renderer.transform; part != root && part; part = part.parent)
                    if (!part.gameObject.activeSelf) { active = false; break; }
                if (!active) continue;
                Vector3 center = transform.MultiplyPoint3x4(renderer.bounds.center);
                var bounds = new Bounds(center, Vector3.one * renderer.bounds.size.magnitude);
                if (GeometryUtility.TestPlanesAABB(planes, bounds) && !Physics.Linecast(ctx.Camera.transform.position, center, mask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }
        bool Retirable(Transform root, float radius, bool protectedNow, Vector3 size)
        {
            var lod = root.GetComponent<CityObjectLod>();
            bool unseen = lod && lod.SelectLevel(ctx.Camera) == 3 || Hidden(root.position + Vector3.up * size.y * .5f, size);
            if (protectedNow || Distance(root.position, ctx.Player.position) < radius || !unseen)
            { distantSince.Remove(root); return false; }
            if (!distantSince.TryGetValue(root, out float since)) { distantSince[root] = ctx.Clock; return false; }
            return ctx.Clock - since > 3;
        }
        void RetireDistant()
        {
            for (int i = ctx.Residents.People.Count - 1; i >= 0; i--)
            {
                var p = ctx.Residents.People[i];
                bool protect = p.Data.id < 0 || p.Down || p.Health < 99.99f || p.Controlled || p.Calling || p.Bus != null || ctx.Police.Involved(p) ||
                    p.TalkUntil + 8 > ctx.Clock || p.FearUntil > ctx.Clock || p.SocialUntil > ctx.Clock;
                if (!Retirable(p.Root, ResidentRetireRadius, protect, new Vector3(1.5f, 3, 1.5f))) continue;
                ctx.Residents.Despawn(p); ResidentRetirements++; distantSince.Remove(p.Root);
                var slot = people.Find(s => s.data.id == p.Data.id); if (slot != null) slot.retry = ctx.Clock + 5;
            }
            for (int i = ctx.Vehicles.Fleet.Count - 1; i >= 0; i--)
            {
                var car = ctx.Vehicles.Fleet[i];
                bool protect = car == ctx.Vehicles.Active || car.Controlled || car.Passenger || car.Commanded || car.TrafficHold ||
                    ctx.Vehicles.PassengerCount(car) > 0 || car.LastUsed + 45 > ctx.Clock || car.LastImpact + 15 > ctx.Clock || ctx.Incidents.Get(car) != null;
                if (!Retirable(car.Root, VehicleRetireRadius, protect, new Vector3(car.Data.length + 2, 4, car.Data.length + 2))) continue;
                ctx.Vehicles.Despawn(car); VehicleRetirements++; distantSince.Remove(car.Root);
                var slot = cars.Find(s => s.data.id == car.Data.id); if (slot != null) slot.retry = ctx.Clock + 5;
            }
        }
    }
}
