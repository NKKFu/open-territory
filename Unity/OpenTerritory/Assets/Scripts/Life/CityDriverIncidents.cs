using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // A driver remains part of the same car. Only while outside does the
    // existing character art acquire a pedestrian capsule and walking rig.
    [DefaultExecutionOrder(140)]
    public sealed class CityDriverIncidents : MonoBehaviour
    {
        public sealed class Incident
        {
            public CityVehicles.Vehicle Vehicle;
            public CityResidents.Resident Actor;
            public string State = "waiting";
            public float Elapsed, Door, ImpactSpeed, Started, DistanceWalked;
            public int Side, Index;
            public bool HadHazards;
            public string WaitReason = "settling-delay", BlockedBy = "";
            public Vector3 Seat, Exit;
            public readonly List<Vector3> Inspection = new List<Vector3>();
            internal Transform DriverHead, HeadParent;
            internal Vector3 HeadPosition, HeadScale, LastPosition;
            internal Quaternion HeadRotation;
            internal float Footstep;
        }
        CityContext ctx;
        readonly Dictionary<CityVehicles.Vehicle, Incident> active = new Dictionary<CityVehicles.Vehicle, Incident>();
        readonly Dictionary<CityVehicles.Vehicle, float> cooldown = new Dictionary<CityVehicles.Vehicle, float>();
        readonly List<Incident> work = new List<Incident>();
        readonly List<Incident> displaced = new List<Incident>();
        readonly HashSet<Transform> imported = new HashSet<Transform>();
        readonly Collider[] overlaps = new Collider[96];
        readonly RaycastHit[] hits = new RaycastHit[96];
        CityResidents.Resident template;
        readonly HashSet<CityVehicles.Vehicle> registered = new HashSet<CityVehicles.Vehicle>();
        public int StartedCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int ActiveCount => active.Count;
        public float LastCompletedAt { get; private set; }
        public Incident Get(CityVehicles.Vehicle vehicle) => vehicle != null && active.TryGetValue(vehicle, out var value) ? value : null;
        public bool CanTakeover(CityVehicles.Vehicle vehicle)
        {
            var i = Get(vehicle);
            return i == null || i.Actor != null && i.State != "removing" && i.State != "entering" && i.State != "exiting" &&
                Mathf.Abs(vehicle.Root.InverseTransformPoint(i.Actor.Root.position).x) > vehicle.Data.width * .5f + .45f;
        }
        public Incident BeginRemoval(CityVehicles.Vehicle car, int side, Vector3 outside)
        {
            if (template == null || !car.Driver || !car.Occupied || Get(car) != null) return null;
            ctx.Residents.PrepareBody(template);
            var i = new Incident { Vehicle = car, State = "removing", Side = side, Seat = car.Driver.localPosition,
                Exit = outside, Started = ctx.Clock, HadHazards = car.Hazards };
            active.Add(car, i); ctx.Vehicles.SetTrafficHold(car, true, "retirada"); return i;
        }
        public void MaterializeRemoval(Incident i)
        {
            if (i == null || i.State != "removing" || i.Actor != null) return;
            MakeActor(i, true);
            // The paired animation owns this rig until both feet are outside.
            // Ordinary reactions resume when the driver joins the pavement.
            ctx.Residents.People.Remove(i.Actor); i.Actor.Controller.enabled = false;
        }
        public void CompleteRemoval(Incident i)
        {
            if (i?.Actor == null) return;
            i.Vehicle.Occupied = false; i.State = "removed";
            if (!ctx.Residents.People.Contains(i.Actor)) ctx.Residents.People.Add(i.Actor);
            i.Actor.Controller.enabled = !i.Actor.Down;
            ReleaseForTakeover(i.Vehicle);
        }
        public void CancelRemoval(Incident i)
        {
            if (i == null) return;
            RemoveActor(i); RestoreDoors(i.Vehicle); i.Vehicle.Hazards = i.HadHazards; i.Vehicle.DoorOpen = 0;
            ctx.Vehicles.SetTrafficHold(i.Vehicle, false); active.Remove(i.Vehicle);
        }
        public void ReleaseForTakeover(CityVehicles.Vehicle vehicle)
        {
            var i = Get(vehicle); if (i == null || !CanTakeover(vehicle)) return;
            // The driver stays in the world and can witness the theft. Restore
            // the car's borrowed head, retaining a copy on the walking rig.
            if (i.DriverHead)
            {
                Instantiate(i.DriverHead, i.Actor.Rig.Head, false);
                RestoreHead(i); i.DriverHead = null;
            }
            var p = i.Actor; p.MotionMode = null; p.Data.stationary = false;
            p.Data.line = "Era o meu carro! Vou chamar a polícia."; p.State = "activity"; p.Age = p.Duration = 0;
            ctx.Residents.Control(p, false);
            foreach (var collider in vehicle.Root.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(p.Controller, collider, false);
            RestoreDoors(vehicle); vehicle.Hazards = i.HadHazards; vehicle.DoorOpen = 0;
            ctx.Vehicles.SetTrafficHold(vehicle, false); active.Remove(vehicle); displaced.Add(i);
            CompletedCount++; LastCompletedAt = ctx.Clock;
        }

        public void Initialize(CityContext context)
        {
            ctx = context;
            foreach (var transform in ctx.Nodes.Values) if (transform) imported.Add(transform);
            var definition = Array.Find(ctx.Data.residents, p => !p.child && !p.sitting && !p.performance && string.IsNullOrEmpty(p.role));
            template = definition != null ? ctx.Residents.Find(definition.id) : null;
            ctx.Vehicles.Impact += OnImpact;
            ctx.Vehicles.Spawned += RegisterCar;
            foreach (var car in ctx.Vehicles.Fleet) RegisterCar(car);
        }
        void RegisterCar(CityVehicles.Vehicle car)
        {
                if (!registered.Add(car)) return;
                ctx.AddAction("driver-incident-" + car.Data.id,
                    () => Get(car)?.Actor != null ? Get(car).Actor.Root.position + Vector3.up * 1.45f : car.Position,
                    () => "Conversar com o motorista", () => Talk(car), () => CanTalk(Get(car)), 2.8f, 2, conversation: true, target: () => Get(car)?.Actor?.Root);
        }
        bool CanTalk(Incident i) => i?.Actor != null && i.State != "removing" && i.State != "exiting" && i.State != "entering" && !ctx.Motor.Busy &&
            ctx.Residents.CanTalk(i.Actor.Root) && !ctx.Combat.Down && !ctx.Combat.WeaponVisible && !ctx.Motor.Riding && !ctx.Police.Custody;
        void Talk(CityVehicles.Vehicle car)
        {
            var i = Get(car); if (!CanTalk(i)) return;
            ctx.Residents.BeginConversation(i.Actor, "Vou conferir a batida e já volto para o carro.", 3.6f);
        }
        void OnImpact(CityVehicles.Vehicle vehicle, float speed, Collider other)
        {
            if (!ctx.Ready || !other || speed < 3.5f) return;
            bool player = CityPhysics.Belongs(other, ctx.Player) || ctx.Motor.Riding && CityPhysics.Belongs(other, ctx.Motor.Riding);
            if (player) Begin(vehicle, speed);
            if (vehicle == ctx.Vehicles.Active && vehicle.Controlled)
                foreach (var candidate in ctx.Vehicles.Fleet) if (CityPhysics.Belongs(other, candidate.Root)) { Begin(candidate, speed); break; }
        }
        bool Begin(CityVehicles.Vehicle car, float speed)
        {
            if (template != null) ctx.Residents.PrepareBody(template);
            if (template == null || car == null || !car.Data.auto || !car.Occupied || car.Controlled || car.Data.kind == "bus" || car.Data.kind == "bike" ||
                !car.Driver || active.ContainsKey(car) || cooldown.TryGetValue(car, out var until) && ctx.Clock < until) return false;
            var i = new Incident { Vehicle = car, ImpactSpeed = speed, Started = ctx.Clock, HadHazards = car.Hazards };
            active.Add(car, i); StartedCount++; car.Hazards = true; car.DoorOpen = 0;
            ctx.Vehicles.SetTrafficHold(car, true, "averiguando");
            ctx.Say("Motorista · Ei! Vou verificar essa batida.", car.Position + Vector3.up * 2.6f, 3.4f);
            ctx.Sound("horn", car.Position, .5f); return true;
        }
        static void Stage(Incident i, string state) { i.State = state; i.Elapsed = 0; i.Index = 0; }
        Vector3 World(Incident i, Vector3 local)
        {
            var point = i.Vehicle.Root.TransformPoint(local); point.y = ctx.Ground(point.x, point.z) + .035f; return point;
        }
        bool Clear(Incident i, Vector3 point)
        {
            // A capsule reaches the curb with its toes before its centre does.
            // Find the support under the complete footprint, just as the
            // CharacterController steps up when its leading edge meets a curb.
            float centre = ctx.Ground(point.x, point.z), support = centre;
            for (int spoke = 0; spoke < 8; spoke++)
            {
                float angle = spoke * Mathf.PI * .25f;
                float ground = ctx.Ground(point.x + Mathf.Cos(angle) * .36f, point.z + Mathf.Sin(angle) * .36f);
                if (Mathf.Abs(ground - centre) > .32f) { i.BlockedBy = "footprint-step:" + (ground - centre).ToString("F2"); return false; }
                support = Mathf.Max(support, ground);
            }
            point.y = support + .035f;
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .42f, point + Vector3.up * 1.92f, .38f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) { i.BlockedBy = "occupied-capacity"; return false; }
            for (int j = 0; j < count; j++)
            {
                var hit = overlaps[j]; if (CityPhysics.Belongs(hit, i.Actor?.Root) || CityPhysics.Belongs(hit, i.Vehicle.Root)) continue;
                i.BlockedBy = "occupied:" + hit.name + " at " + point.ToString("F2"); return false;
            }
            return true;
        }
        bool Segment(Incident i, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a; float length = delta.magnitude;
            if (length > .005f)
            {
                // The lower sweep follows the controller's 30 cm step
                // allowance. Full standing capsules below still check every
                // sampled ground height, so low obstacles are never ignored.
                int count = Physics.CapsuleCastNonAlloc(a + Vector3.up * .72f, a + Vector3.up * 1.92f, .38f, delta / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) { i.BlockedBy = "sweep-capacity"; return false; }
                for (int j = 0; j < count; j++)
                {
                    var hit = hits[j]; if (!hit.collider || CityPhysics.Belongs(hit.collider, i.Actor?.Root) || CityPhysics.Belongs(hit.collider, i.Vehicle.Root)) continue;
                    i.BlockedBy = "sweep:" + hit.collider.name + " at " + hit.point.ToString("F2"); return false;
                }
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(CityPhysics.Flat(b - a).magnitude / .28f));
            float previousGround = a.y;
            for (int j = 0; j <= steps; j++)
            {
                var p = Vector3.Lerp(a, b, (float)j / steps); float ground = ctx.Ground(p.x, p.z) + .035f;
                if (Mathf.Abs(ground - p.y) > .30f || Mathf.Abs(ground - previousGround) > .32f)
                { i.BlockedBy = "ground-step:" + (ground - previousGround).ToString("F2"); return false; }
                previousGround = ground; p.y = ground;
                if (!Clear(i, p)) return false;
            }
            return true;
        }
        bool FindExit(Incident i)
        {
            var car = i.Vehicle;
            if (Vector3.Dot(car.Root.up, Vector3.up) < .85f) { i.WaitReason = "body-tilted"; return false; }
            string refused = "";
            foreach (int side in new[] { -1, 1 })
            {
                float outX = Mathf.Max(2.8f, car.Data.width * .5f + 1.15f), z = car.Data.seatMount.z;
                var edge = new Vector3(side * (car.Data.width * .5f + .42f), 0, z);
                var exit = new Vector3(side * outX, 0, z);
                var inspect = new Vector3(side * outX, 0, 1.8f);
                if (!Segment(i, World(i, edge), World(i, exit)) || !Segment(i, World(i, exit), World(i, inspect)))
                { refused += (side < 0 ? "left=" : "; right=") + i.BlockedBy; continue; }
                i.Side = side; i.Seat = car.Data.seatMount; i.Seat.y = 0; i.Exit = exit;
                i.Inspection.Clear(); i.Inspection.Add(inspect);
                var front = new Vector3(side * outX, 0, car.Data.length * .5f + car.Data.centerOffset + .6f);
                if (Segment(i, World(i, inspect), World(i, front))) i.Inspection.Add(front);
                i.WaitReason = "clear-exit"; return true;
            }
            i.WaitReason = refused;
            return false;
        }
        static Transform Named(Transform root, string name)
        { foreach (var child in root.GetComponentsInChildren<Transform>(true)) if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return child; return null; }
        Transform CopyVisual(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var root = new GameObject(source.name).transform; root.gameObject.layer = CityContext.ActorLayer;
            root.SetParent(parent, false); root.localPosition = source.localPosition; root.localRotation = source.localRotation; root.localScale = source.localScale;
            root.gameObject.SetActive(source.gameObject.activeSelf); map[source] = root;
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (mesh && renderer)
            {
                root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var art = root.gameObject.AddComponent<MeshRenderer>(); art.sharedMaterials = renderer.sharedMaterials;
                art.enabled = renderer.enabled; art.shadowCastingMode = renderer.shadowCastingMode; art.receiveShadows = renderer.receiveShadows;
            }
            foreach (Transform child in source) if (imported.Contains(child)) CopyVisual(child, root, map);
            return root;
        }
        static Material MainMaterial(Transform root, Transform head = null, Transform[] arms = null)
        {
            Material material = null; float area = 0;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (head && renderer.transform.IsChildOf(head)) continue;
                bool arm = false; if (arms != null) foreach (var t in arms) if (renderer.transform.IsChildOf(t)) arm = true; if (arm) continue;
                var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) continue;
                Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, renderer.transform.lossyScale);
                float score = size.x * size.y; if (score <= area) continue;
                area = score; material = renderer.sharedMaterial;
            }
            return material;
        }
        void MakeActor(Incident i, bool seated = false)
        {
            var map = new Dictionary<Transform, Transform>(); var root = CopyVisual(template.Root, null, map); var source = template.Rig;
            root.name = "Motorista conferindo " + i.Vehicle.Data.model; root.localScale = Vector3.one;
            root.SetPositionAndRotation(seated ? i.Vehicle.Root.TransformPoint(i.Seat) : World(i, i.Seat), Quaternion.Euler(0, i.Vehicle.Root.eulerAngles.y, 0)); root.gameObject.SetActive(true);
            Transform[] Map(Transform[] nodes) { var values = new Transform[nodes.Length]; for (int j = 0; j < nodes.Length; j++) values[j] = map[nodes[j]]; return values; }
            var rig = new CityRig { Root = root, Hips = map[source.Hips], Chest = map[source.Chest], Head = map[source.Head], Legs = Map(source.Legs), Knees = Map(source.Knees),
                Feet = Map(source.Feet), Arms = Map(source.Arms), Elbows = Map(source.Elbows), Hands = Map(source.Hands), HipRest = source.HipRest, ChestRest = source.ChestRest,
                HipHeight = source.HipHeight, Thigh = source.Thigh, Shin = source.Shin, Upper = source.Upper, Lower = source.Lower, Ankle = source.Ankle,
                Previous = root.position, LastYaw = root.eulerAngles.y, Phase = Mathf.PI * .6f };
            for (int j = 0; j < 2; j++) rig.FootAnchor[j] = rig.FootPoint[j] = rig.SwingFrom[j] = root.TransformPoint(new Vector3(j == 0 ? -.2f : .2f, rig.Ankle, .08f));
            var shirt = MainMaterial(source.Chest, source.Head, source.Arms); var driverChest = Named(i.Vehicle.Driver, "driver-chest");
            var driverShirt = driverChest ? MainMaterial(driverChest) : null;
            if (shirt && driverShirt) foreach (var mesh in root.GetComponentsInChildren<MeshRenderer>(true))
            { var materials = mesh.sharedMaterials; for (int j = 0; j < materials.Length; j++) if (materials[j] == shirt) materials[j] = driverShirt; mesh.sharedMaterials = materials; }
            i.DriverHead = Named(i.Vehicle.Driver, "driver-head");
            if (i.DriverHead)
            {
                i.HeadParent = i.DriverHead.parent; i.HeadPosition = i.DriverHead.localPosition; i.HeadRotation = i.DriverHead.localRotation; i.HeadScale = i.DriverHead.localScale;
                foreach (Transform child in rig.Head) child.gameObject.SetActive(false);
                i.DriverHead.SetParent(rig.Head, false); i.DriverHead.localPosition = Vector3.zero; i.DriverHead.localRotation = Quaternion.identity;
            }
            var cc = root.gameObject.AddComponent<CharacterController>(); cc.height = 2.32f; cc.center = Vector3.up * 1.16f; cc.radius = .38f;
            cc.skinWidth = .035f; cc.stepOffset = .32f; cc.slopeLimit = 50; cc.minMoveDistance = 0;
            foreach (var collider in i.Vehicle.Root.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(cc, collider, true);
            var person = new CityResidents.Resident { Root = root, Rig = rig, Controller = cc, Home = World(i, i.Exit), LastPosition = root.position,
                Data = new ResidentData { id = -i.Vehicle.Data.id, name = "Motorista", role = "driver", stationary = true, line = "Vou conferir a batida e já volto para o carro." },
                Controlled = true, State = "incident", Speed = 1.65f, Health = 100 };
            root.gameObject.AddComponent<CityResidentBody>().Resident = person; ctx.Residents.People.Add(person);
            i.Actor = person; i.LastPosition = root.position; i.Vehicle.Driver.gameObject.SetActive(false);
        }
        bool Move(Incident i, Vector3 local, float dt)
        {
            var p = i.Actor; Vector3 target = World(i, local), direction = CityPhysics.Flat(target - p.Root.position); float distance = direction.magnitude;
            if (distance < .10f) return true;
            Vector3 next = p.Root.position + direction.normalized * Mathf.Min(distance, dt * 1.65f); next.y = ctx.Ground(next.x, next.z) + .035f;
            if (!Segment(i, p.Root.position, next)) return false;
            p.Controller.Move(CityPhysics.Flat(next - p.Root.position) + Vector3.down * dt * 2.5f);
            p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-dt * 9));
            float travelled = CityPhysics.Flat(p.Root.position - i.LastPosition).magnitude; i.DistanceWalked += travelled; i.Footstep += travelled; i.LastPosition = p.Root.position;
            if (i.Footstep > 1.15f) { i.Footstep = 0; ctx.Sound("footstep", p.Root.position, .35f); }
            return CityPhysics.Flat(target - p.Root.position).magnitude < .12f;
        }
        void RestoreHead(Incident i)
        {
            if (i.DriverHead && i.HeadParent)
            { i.DriverHead.SetParent(i.HeadParent, false); i.DriverHead.localPosition = i.HeadPosition; i.DriverHead.localRotation = i.HeadRotation; i.DriverHead.localScale = i.HeadScale; }
        }
        void RemoveActor(Incident i, bool restoreDriver = true)
        {
            RestoreHead(i);
            if (i.Actor != null)
            {
                ctx.Residents.Interrupt(i.Actor); ctx.Residents.People.Remove(i.Actor); ctx.Combat.ForgetTemporaryActor(i.Actor.Root); ctx.Actors.ForgetTemporaryRig(i.Actor.Root);
                if (i.Actor.Root) Destroy(i.Actor.Root.gameObject); i.Actor = null;
            }
            if (restoreDriver && i.Vehicle.Driver) i.Vehicle.Driver.gameObject.SetActive(i.Vehicle.Occupied);
        }
        void Finish(Incident i)
        {
            RemoveActor(i); RestoreDoors(i.Vehicle); i.Vehicle.Hazards = i.HadHazards; i.Vehicle.DoorOpen = 0;
            ctx.Vehicles.SetTrafficHold(i.Vehicle, false); cooldown[i.Vehicle] = ctx.Clock + 8; active.Remove(i.Vehicle);
            CompletedCount++; LastCompletedAt = ctx.Clock;
        }
        void RestoreDoors(CityVehicles.Vehicle car)
        { for (int j = 0; j < car.Doors.Length; j++) if (car.Doors[j]) car.Doors[j].localRotation = car.DoorRotations[j]; }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return; float dt = Mathf.Min(Time.deltaTime, .05f);
            for (int j = displaced.Count - 1; j >= 0; j--)
            {
                var p = displaced[j].Actor;
                var view = p != null && p.Root ? ctx.Camera.WorldToViewportPoint(p.Root.position + Vector3.up) : Vector3.zero;
                bool hidden = view.z <= 0 || view.x < -.1f || view.x > 1.1f || view.y < -.1f || view.y > 1.1f;
                if (p == null || !p.Root || !p.Root.gameObject.activeInHierarchy ||
                    hidden && CityPhysics.Flat(p.Root.position - ctx.Player.position).magnitude > CityPopulation.ResidentRetireRadius)
                { RemoveActor(displaced[j], false); displaced.RemoveAt(j); }
            }
            work.Clear(); work.AddRange(active.Values);
            foreach (var i in work)
            {
                if (i.State == "removing") continue;
                var car = i.Vehicle; car.Hazards = true; car.DoorOpen = 0;
                bool atDoor = i.State == "entering" || i.State == "exiting";
                if (i.Actor != null && !atDoor) i.Actor.MotionMode = null;
                if (i.Actor != null && (i.Actor.Down || i.Actor.Calling || i.Actor.TalkUntil > ctx.Clock || !atDoor && i.Actor.Reaction.Kind != CityResidents.ReactionKind.None)) continue;
                if (i.Actor != null) { i.Actor.Controlled = true; i.Actor.MotionMode = null; }
                i.Elapsed += dt;
                switch (i.State)
                {
                    case "waiting":
                        i.Door = Mathf.MoveTowards(i.Door, 0, dt * 2);
                        if (i.Elapsed < .8f) i.WaitReason = "settling-delay";
                        else if (car.Body.linearVelocity.magnitude >= .4f) i.WaitReason = "linear-motion";
                        else if (car.Body.angularVelocity.magnitude >= .3f) i.WaitReason = "angular-motion";
                        else if (FindExit(i)) Stage(i, "opening");
                        break;
                    case "opening":
                        if (!Segment(i, World(i, new Vector3(i.Side * (car.Data.width * .5f + .42f), 0, i.Seat.z)), World(i, i.Exit))) { Stage(i, "waiting"); break; }
                        i.Door = Mathf.MoveTowards(i.Door, 1, dt * 2); if (i.Door >= 1) { MakeActor(i); Stage(i, "exiting"); ctx.Sound("door", car.Position); }
                        break;
                    case "exiting":
                        i.Actor.MotionMode = "car-door"; i.Actor.MotionProgress = Mathf.Clamp01(CityPhysics.Flat(i.Actor.Root.position - World(i, i.Seat)).magnitude / Mathf.Max(.1f, Mathf.Abs(i.Exit.x - i.Seat.x)));
                        if (Move(i, i.Exit, dt)) Stage(i, "walking"); break;
                    case "walking":
                        i.Door = Mathf.MoveTowards(i.Door, 0, dt * 2);
                        if (i.Index >= i.Inspection.Count) { Stage(i, "inspecting"); ctx.Say("Motorista · Deixa eu conferir aqui…", i.Actor.Root.position + Vector3.up * 2.5f, 2.6f); }
                        else if (Move(i, i.Inspection[i.Index], dt)) i.Index++; break;
                    case "inspecting":
                        FaceCar(i, dt); if (i.Elapsed > 4.5f) Stage(i, "returning"); break;
                    case "returning":
                        var goal = i.Index < i.Inspection.Count ? i.Inspection[i.Inspection.Count - 1 - i.Index] : i.Exit;
                        if (Move(i, goal, dt) && ++i.Index > i.Inspection.Count) Stage(i, "reopening"); break;
                    case "reopening":
                        i.Door = Mathf.MoveTowards(i.Door, 1, dt * 2); if (i.Door >= 1) Stage(i, "entering"); break;
                    case "entering":
                        i.Actor.MotionMode = "car-door"; i.Actor.MotionProgress = Mathf.Clamp01(CityPhysics.Flat(i.Actor.Root.position - World(i, i.Exit)).magnitude / Mathf.Max(.1f, Mathf.Abs(i.Exit.x - i.Seat.x)));
                        if (Move(i, i.Seat, dt)) { RemoveActor(i); Stage(i, "closing"); } break;
                    case "closing":
                        i.Door = Mathf.MoveTowards(i.Door, 0, dt * 2); if (i.Door <= 0) Finish(i); break;
                }
            }
        }
        void FaceCar(Incident i, float dt)
        {
            Vector3 direction = CityPhysics.Flat(i.Vehicle.Position - i.Actor.Root.position);
            if (direction.sqrMagnitude > .01f) i.Actor.Root.rotation = Quaternion.Slerp(i.Actor.Root.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-dt * 6));
        }
        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            foreach (var i in active.Values)
            {
                if (i.State == "removing") continue;
                var car = i.Vehicle; RestoreDoors(car); int door = i.Side < 0 ? 0 : 1;
                if (door < car.Doors.Length && car.Doors[door]) car.Doors[door].localRotation = car.DoorRotations[door] * Quaternion.Euler(0, -i.Side * i.Door * 60, 0);
                if (i.Actor == null || i.Actor.Down || i.Actor.Calling || i.Actor.TalkUntil > ctx.Clock || i.Actor.Reaction.Kind != CityResidents.ReactionKind.None || i.State != "inspecting") continue;
                var rig = i.Actor.Rig; rig.Head.localRotation = Quaternion.Euler(14, Mathf.Sin(ctx.Clock * .8f) * 9, 0);
                CityActors.Reach(rig, 1, rig.Root.TransformPoint(new Vector3(.23f, 1.12f + Mathf.Sin(ctx.Clock * 2) * .04f, .59f)));
            }
        }
        void OnDestroy()
        {
            if (!ctx) return; if (ctx.Vehicles) { ctx.Vehicles.Impact -= OnImpact; ctx.Vehicles.Spawned -= RegisterCar; }
            foreach (var i in active.Values) { RemoveActor(i); if (ctx.Vehicles) ctx.Vehicles.SetTrafficHold(i.Vehicle, false); RestoreDoors(i.Vehicle); }
            foreach (var i in displaced) RemoveActor(i, false);
        }
    }
}
