using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed class CityResidentBody : MonoBehaviour { public CityResidents.Resident Resident; }

    [DefaultExecutionOrder(-40)]
    public sealed partial class CityResidents : MonoBehaviour
    {
        public sealed class Resident
        {
            public ResidentData Data;
            public Transform Root;
            public CityRig Rig;
            public CharacterController Controller;
            public string Activity = "rest", State = "idle";
            public Vector3 Home, Destination, LastPosition, Velocity;
            public readonly List<int> Path = new List<int>();
            public int PathIndex, ProgramIndex, Location = -1, Slot;
            public float Age, Duration, Speed, TalkUntil, FearUntil, SocialUntil, NextSocial, NextLook, LookYaw, LookPitch, CheckUntil, NextCheck, Stuck, DodgeUntil, TrafficBoostUntil;
            public float NextVocal, NextMurmur;
            public bool Controlled, Down, Reaching, Crossing, Calling;
            public string MotionMode; public float MotionProgress;
            public Resident Partner;
            public CityVehicles.Vehicle Bus;
            public int BoardedStop;
            public string Gesture;
            public CityEmote Emote = CityEmote.None;
            public float EmoteStarted, EmoteUntil;
            public PlaceData HomePlace, Doorway;
            public bool EnteringDoor;
            public float MoveSpeed, Pace = 1, RouteSalt, Lateral, NextPause, PauseUntil, NoProgress;
            public Vector3 Steering, RouteGoal;
            public readonly Queue<int> RecentLocations = new Queue<int>();
            public Transform PropRoot, Phone, Book, Cup, Broom, Brush, Palette, Umbrella;
            public float Health = 100;
            public int PocketCash = -1;
            public float RobbedUntil, CrimeCooldown;
            public Vector3 Threat, DodgeGoal;
            public readonly PlayerReaction Reaction = new PlayerReaction();
            public bool CanTalk => !Down && Health >= 99.99f && FearUntil <= Time.time;
        }
        sealed class Animal
        {
            public AnimalData Data; public Transform Root, Head; public Transform[] Legs, Wings;
            public Vector3 Home, Goal, FlightFrom; public float ChangeAt, Flight, FlightDuration, Phase, Cooldown, LandingWait; public bool Flying;
        }
        CityContext ctx;
        public readonly List<Resident> People = new List<Resident>();
        public event Action<Resident> Spawned;
        readonly List<Animal> animals = new List<Animal>();
        readonly Dictionary<int, Resident> byId = new Dictionary<int, Resident>();
        readonly Dictionary<int, int> navIndex = new Dictionary<int, int>();
        readonly Dictionary<int, HashSet<int>> reservations = new Dictionary<int, HashSet<int>>();
        readonly Dictionary<int, Vector3> lastVehicles = new Dictionary<int, Vector3>(), vehicleVelocities = new Dictionary<int, Vector3>();
        readonly Collider[] overlap = new Collider[32];
        readonly RaycastHit[] casts = new RaycastHit[32];
        readonly System.Random random = new System.Random(80241);
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        float nextTraffic, nextConversation;
        float R(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        public void Initialize(CityContext context)
        {
            ctx = context; navIndex.Clear(); reachableCandidates.Clear();
#if !CITY_PERFORMANCE_REFERENCE
            routeWorkspace = new RouteWorkspace(ctx.Data.nav.Length);
#endif
            for (int i = 0; i < ctx.Data.nav.Length; i++) navIndex[ctx.Data.nav[i].id] = i;
            BuildDestinations();
            foreach (var data in ctx.Data.residents)
            {
                var root = ctx.Node(data.id); if (!root) continue;
                var resident = new Resident { Data = data, Root = root, Home = root.position, LastPosition = root.position, Speed = Mathf.Max(.65f, data.speed), NextSocial = ctx.Clock + R(4, 15), NextLook = R(2, 8) };
                byId[data.id] = resident;
                ctx.AddAction("resident-" + data.id, () => root.position + Vector3.up * 1.4f, () => "Conversar",
                    () => Talk(resident), () => AvailableForTalk(resident), 2.9f, data.performance ? -2 : 1, conversation: true, target: () => root);
                ctx.AddAction("rob-resident-" + data.id, () => root.position + Vector3.up * 1.4f, () => "Rob",
                    () => Rob(resident), () => CanRob(resident), 3.4f, 8, target: () => root);
            }
            foreach (var data in ctx.Data.animals)
            {
                var root = ctx.Node(data.id); if (!root) continue;
                var animal = new Animal { Data = data, Root = root, Head = ctx.Node(data.head), Home = root.position, Goal = root.position,
                    Phase = data.phase, ChangeAt = R(1, 4), Cooldown = R(3, 8), Legs = Map(data.legs), Wings = Map(data.wings) };
                animals.Add(animal);
                ctx.AddAction("animal-" + data.id, () => root.position + Vector3.up * .6f, () => data.type == "pigeon" ? "Aproximar a mão" : "Fazer carinho",
                    () => { if (ctx.Actors.Pet(animal.Head ? animal.Head : root)) { animal.Goal = root.position; animal.ChangeAt = ctx.Clock + 4; ctx.Sound("pet", root.position); } },
                    () => !animal.Flying && !ctx.Combat.Down && !ctx.Combat.IsThreatening() && !ctx.Motor.Riding && !ctx.Motor.Swimming, 2.4f, 3, target: () => root);
            }
        }
        public bool IsSpawned(int id) => byId.TryGetValue(id, out var p) && People.Contains(p);
        public void PrepareBody(Resident p)
        {
            if (p.Controller) return;
            var root = p.Root; var data = p.Data;
            p.Rig = ctx.Actors.BindData(data.rig, data.sitting);
            root.gameObject.layer = CityContext.ActorLayer;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var cc = root.gameObject.AddComponent<CharacterController>(); cc.enabled = false;
            cc.center = new Vector3(0, data.sitting ? .85f : 1.16f, 0);
            cc.height = data.sitting ? 1.7f : 2.32f; cc.radius = .38f; cc.skinWidth = .035f; cc.stepOffset = .32f; cc.slopeLimit = 50; cc.minMoveDistance = 0;
            p.Controller = cc; root.gameObject.AddComponent<CityResidentBody>().Resident = p;
        }
        public bool Spawn(int id, Vector3 position, Quaternion rotation)
        {
            if (!byId.TryGetValue(id, out var p) || People.Contains(p)) return false;
            PrepareBody(p); p.Controller.enabled = false; p.Root.SetPositionAndRotation(position, rotation);
            p.LastPosition = position; p.Velocity = Vector3.zero; p.Age = 0; p.State = "idle"; p.ProgramIndex = 0;
            ResetPlayerReaction(p);
            p.MoveSpeed = p.NoProgress = 0; p.Steering = Vector3.zero; p.Doorway = null;
            p.RouteSalt = R(1, 10000); p.Pace = R(.88f, 1.18f); p.Lateral = R(.10f, .32f);
            p.NextPause = ctx.Clock + R(12, 32); p.PauseUntil = 0; AssignHome(p, position);
            p.NextSocial = ctx.Clock + R(4, 15); p.NextLook = ctx.Clock + R(2, 8); p.CheckUntil = p.DodgeUntil = p.TrafficBoostUntil = 0;
            p.Root.gameObject.SetActive(true); p.Controller.enabled = !p.Down;
            foreach (var contact in ctx.Population.PoseContacts(id)) if (contact) Physics.IgnoreCollision(p.Controller, contact, true);
            if (p.Rig != null)
            {
                p.Rig.Previous = position; p.Rig.LastYaw = p.Root.eulerAngles.y;
                p.Rig.GroundGait = false; p.Rig.GaitVelocity = Vector3.zero;
                p.Rig.Speed = p.Rig.Weight = p.Rig.Run = p.Rig.Turn = p.Rig.Landing = 0;
            }
            People.Add(p);
            if (!p.Data.stationary && !p.Data.sitting && !p.Data.performance && !OccupySpawn(p)) Choose(p);
            Spawned?.Invoke(p); return true;
        }
        public void Despawn(Resident p)
        {
            if (streetCrime?.Offender == p || streetCrime?.Victim == p) EndStreetCrime();
            Interrupt(p); Release(p); p.Path.Clear(); p.PathIndex = 0; p.Crossing = false;
            if (p.Controller) p.Controller.enabled = false;
            p.Root.gameObject.SetActive(false); People.Remove(p);
        }
        Transform[] Map(int[] ids) { var result = new Transform[ids.Length]; for (int i = 0; i < ids.Length; i++) result[i] = ctx.Node(ids[i]); return result; }
        public Resident Find(int id) => byId.TryGetValue(id, out var person) ? person : null;
        public Resident Find(Transform root) => root ? root.GetComponentInParent<CityResidentBody>()?.Resident : null;
        public Resident FromCollider(Collider collider) => collider ? collider.GetComponentInParent<CityResidentBody>()?.Resident : null;
        public bool CanTalk(Transform root) { var p = Find(root); return root && root.gameObject.activeInHierarchy && (p == null || !p.Down && p.Health >= 99.99f && ctx.Clock >= p.FearUntil && !p.Calling && !UnsafeReaction(p)); }
        public bool AvailableForTalk(Resident p) => p != null && CanTalk(p.Root) && !p.Controlled && p.Bus == null && !ctx.Combat.Down && !ctx.Combat.WeaponVisible && !ctx.Police.Custody && !ctx.Motor.Riding;
        public void Talk(Resident p)
        {
            if (!AvailableForTalk(p)) return;
            string line = p.Data.lines != null && p.Data.lines.Length > 0 ? p.Data.lines[random.Next(p.Data.lines.Length)] : p.Data.line;
            if (string.IsNullOrWhiteSpace(line)) line = p.Activity == "read" ? "Estou gostando deste livro. Bom passeio!" : "Bom te ver por aqui!";
            BeginConversation(p, line);
        }
        public void Interrupt(Resident p)
        {
            p.Emote = CityEmote.None; p.EmoteUntil = 0;
            CancelHackReaction(p);
            EndConversation(p);
            if (p.Partner != null) { ctx.Systems.Audio?.StopNpcVoice(p.Partner); p.Partner.Partner = null; p.Partner.SocialUntil = 0; p.Partner.NextSocial = ctx.Clock + 12; }
            p.Partner = null; p.SocialUntil = 0; p.Gesture = null; HideProps(p);
        }
        public void Control(Resident p, bool active) { if (p == null) return; Interrupt(p); p.Controlled = active; p.Path.Clear(); p.PathIndex = 0; }
        public void Startle(Resident p, Vector3 source, float duration = 4)
        {
            if (p == null || p.Down) return; Interrupt(p); p.TalkUntil = 0; p.FearUntil = Mathf.Max(p.FearUntil, ctx.Clock + duration); p.Threat = source;
            BeginAlarm(p, source, ReactionKind.Flee, duration, false);
        }
        public void ReactToSound(Vector3 source, float radius = 10)
        {
            int count = 0;
            foreach (var p in People)
            {
                if (count >= 3 || p.Down || p.Crossing || p.Calling || p.Controlled || p.TalkUntil > ctx.Clock || ctx.Clock < p.NextLook || Vector3.Distance(source, p.Root.position) > radius || !ctx.Visible(p.Root.position + Vector3.up * 1.8f, source + Vector3.up)) continue;
                p.NextLook = ctx.Clock + 9; p.Gesture = "look"; p.SocialUntil = ctx.Clock + 2.1f; p.Threat = source; count++;
            }
        }
        public void ReactToHorn(Vector3 source, bool bicycle = false) => ReactToSound(source, bicycle ? 7 : 12);
        public void SetDown(Resident p, bool down) { if (p == null) return; Interrupt(p); p.Down = down; p.TalkUntil = 0; p.Calling = false; p.Path.Clear(); }

        void Release(Resident p)
        { if (p.Location >= 0 && reservations.TryGetValue(p.Location, out var slots)) slots.Remove(p.Slot); p.Location = -1; }
        void Choose(Resident p)
        {
            ChooseDestination(p);
        }
        bool CanOccupy(Resident p, Vector3 feet)
        {
            float scale = p.Root.lossyScale.y, radius = .40f * scale;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (radius + .05f), feet + Vector3.up * (2.15f * scale - radius), radius, overlap, ctx.AllMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (overlap[i] && !overlap[i].transform.IsChildOf(p.Root) && overlap[i].transform != p.Root) return false;
            return true;
        }
        int ClosestNode(Vector3 point)
        { int best = -1; float distance = float.MaxValue; for (int i = 0; i < ctx.Data.nav.Length; i++) { var n = ctx.Data.nav[i]; float d = (new Vector2(n.x - point.x, n.z - point.z)).sqrMagnitude; if (d < distance) { best = i; distance = d; } } return best; }
        bool Route(Resident p, Vector3 goal) =>
#if CITY_PERFORMANCE_REFERENCE
            ReferenceRoute(p, goal);
#else
            FindRoute(p, goal);
#endif
        bool TrafficClear(Vector3 from, Vector3 to)
        {
            Vector3 line = Vector3.ProjectOnPlane(to - from, Vector3.up); float length = line.magnitude; if (length < .1f) return true;
            foreach (var data in ctx.Data.vehicles)
            {
                Transform vehicle = ctx.Node(data.id); if (!vehicle || !vehicle.gameObject.activeInHierarchy || !vehicleVelocities.TryGetValue(data.id, out var velocity)) continue;
                if (velocity.magnitude < .5f) continue;
                Vector3 relative = from - vehicle.position; float ahead = Vector3.Dot(relative, velocity.normalized);
                if (ahead < -data.length || ahead / velocity.magnitude > 3.2f) continue;
                Vector3 closest = from + line * Mathf.Clamp01(Vector3.Dot(vehicle.position + velocity * Mathf.Clamp(ahead / velocity.magnitude, 0, 2.8f) - from, line) / line.sqrMagnitude);
                if (Vector3.ProjectOnPlane(closest - vehicle.position - velocity * Mathf.Clamp(ahead / velocity.magnitude, 0, 2.8f), Vector3.up).magnitude < data.width * .6f + .7f) return false;
            }
            return true;
        }
        PlaceData Inside(Vector3 point)
        { foreach (var place in ctx.Data.places) if (place.Contains(point,.2f)) return place; return null; }
        public bool MoveToward(Resident p, Vector3 goal, float dt, float speed = 1.25f, bool urgent = false)
        {
            if (p == null || p.Down || !p.Controller || !p.Controller.enabled) return false;
            Vector3 toGoal = Vector3.ProjectOnPlane(goal - p.Root.position, Vector3.up); if (toGoal.magnitude < .20f) return true;
            Vector3 finalGoal = goal; var fromRoom = Inside(p.Root.position); var toRoom = Inside(goal); bool doorway = false;
            if (p.Doorway != null)
            {
                var normal = Quaternion.Euler(0, p.Doorway.door.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                float plane = Vector3.Dot(p.Root.position - new Vector3(p.Doorway.door.x, p.Root.position.y, p.Doorway.door.z), normal);
                if (p.EnteringDoor ? plane < -1.65f : plane > 1.65f) { p.Doorway = null; p.Path.Clear(); }
            }
            if (p.Doorway == null && fromRoom != toRoom && fromRoom?.door != null) { p.Doorway = fromRoom; p.EnteringDoor = false; }
            if (p.Doorway == null && fromRoom != toRoom && toRoom?.door != null)
            {
                Vector3 entrance = toRoom.door.Approach(p.Root.position.y, 1.9f);
                if (CityPhysics.Flat(entrance - p.Root.position).magnitude > .65f) goal = entrance;
                else { p.Doorway = toRoom; p.EnteringDoor = true; p.Path.Clear(); }
            }
            if (p.Doorway != null) { goal = p.Doorway.door.Approach(p.Root.position.y, p.EnteringDoor ? -2.15f : 2.15f); doorway = true; }
            goal.y = ctx.Ground(goal.x, goal.z) + .04f;
            toGoal = Vector3.ProjectOnPlane(goal - p.Root.position, Vector3.up);
            bool direct = (doorway || fromRoom != null && fromRoom == toRoom || toGoal.magnitude < 3.5f) && ClearWalk(p.Root.position, goal, p, doorway ? p.Doorway.door : null);
            Vector3 target = goal; p.Crossing = false;
            if (!direct && fromRoom != null)
            {
                // A visible pocket near the door provides a short local detour
                // around counters and tables without using outdoor waypoints.
                float best = float.MaxValue;
                foreach (int index in interiorLocations)
                {
                    if (destinationRooms[index] != fromRoom) continue;
                    Vector3 pocket = Point(ctx.Data.locations[index]); float distance = CityPhysics.Flat(pocket - p.Root.position).magnitude;
                    if (distance < .6f || !ClearWalk(p.Root.position, pocket, p) || !ClearWalk(pocket, goal, p, doorway ? p.Doorway.door : null)) continue;
                    float score = distance + CityPhysics.Flat(pocket - goal).magnitude;
                    if (score < best) { best = score; target = pocket; }
                }
                direct = true;
            }
            if (!direct)
            {
                if (CityPhysics.Flat(goal - p.RouteGoal).magnitude > 1.5f) { p.Path.Clear(); p.PathIndex = 0; }
                if (p.Path.Count == 0 && !Route(p, goal)) return false;
                if (p.PathIndex < p.Path.Count)
                {
                    var node = ctx.Data.nav[p.Path[p.PathIndex]]; target = new Vector3(node.x, p.Root.position.y, node.z);
                    if (Vector3.ProjectOnPlane(target - p.Root.position, Vector3.up).magnitude < .60f) p.PathIndex++;
                    if (p.PathIndex >= p.Path.Count) { p.Path.Clear(); target = goal; }
                    else
                    {
                        node = ctx.Data.nav[p.Path[p.PathIndex]]; target = new Vector3(node.x, p.Root.position.y, node.z);
                        if (p.PathIndex > 0) p.Crossing = CrossingEdge(p.Path[p.PathIndex - 1], node);
                        // Look through close, visible nodes on the same sidewalk
                        // so a dense graph does not produce a stop at each node.
                        while (!p.Crossing && p.PathIndex + 1 < p.Path.Count)
                        {
                            var next = ctx.Data.nav[p.Path[p.PathIndex + 1]];
                            var point = new Vector3(next.x, ctx.Ground(next.x, next.z) + .04f, next.z);
                            if (CityPhysics.Flat(point - p.Root.position).magnitude > 3.8f || CrossingEdge(p.Path[p.PathIndex], next) || !ClearWalk(p.Root.position, point, p)) break;
                            p.PathIndex++; target = point;
                        }
                        if (!p.Crossing)
                        {
                            var travel = CityPhysics.Flat(target - p.Root.position).normalized;
                            Vector3 offset = target + Vector3.Cross(Vector3.up, travel) * p.Lateral;
                            if (ClearWalk(p.Root.position, offset, p)) target = offset;
                        }
                    }
                }
            }
            Vector3 direction = Vector3.ProjectOnPlane(target - p.Root.position, Vector3.up); float gap = direction.magnitude; if (gap < .02f) return false; direction /= gap;
            if (p.Crossing && !urgent)
            {
                if (ctx.Clock >= p.NextCheck) { p.CheckUntil = ctx.Clock + R(.35f, .85f); p.NextCheck = ctx.Clock + 9; }
                if (ctx.Clock < p.CheckUntil || !TrafficClear(p.Root.position, target)) { p.MoveSpeed = 0; p.LookYaw = Mathf.Sin(ctx.Clock * 8) * .7f; return false; }
            }
            // Slow sidesteps use the same controller; nobody teleports through a crowd.
            Vector3 avoidance = Vector3.zero;
            foreach (var other in People)
            {
                if (other == p || other.Down) continue; Vector3 delta = Vector3.ProjectOnPlane(p.Root.position - other.Root.position, Vector3.up); float d = delta.magnitude;
                if (d > 1.45f || d < .01f || Vector3.Dot(-delta, direction) < -.25f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, direction); if (Vector3.Dot(side, delta) < 0) side = -side;
                avoidance += side * (1.45f - d) * .8f + delta.normalized * Mathf.Max(0, .92f - d);
            }
            Vector3 playerDelta = Vector3.ProjectOnPlane(p.Root.position - ctx.Player.position, Vector3.up);
            if (playerDelta.magnitude < 1.3f && Vector3.Dot(-playerDelta, direction) > 0) avoidance += playerDelta.normalized * (1.3f - playerDelta.magnitude) * 1.7f;
            direction = (direction + avoidance).normalized;
            // Probe both sides of furniture before committing the controller.
            if (!ClearWalk(p.Root.position, p.Root.position + direction * .9f, p, doorway ? p.Doorway.door : null))
            {
                float sign = (p.Data.id & 1) == 0 ? 1 : -1;
                foreach (float angle in new[] { 30f, -30f, 60f, -60f, 85f, -85f })
                {
                    Vector3 detour = Quaternion.Euler(0, angle * sign, 0) * direction;
                    if (ClearWalk(p.Root.position, p.Root.position + detour * 1.1f, p, doorway ? p.Doorway.door : null)) { direction = detour; break; }
                }
            }
            if (p.Steering.sqrMagnitude < .01f || urgent || doorway) p.Steering = direction;
            else p.Steering = Vector3.Slerp(p.Steering, direction, 1 - Mathf.Exp(-dt * 5)).normalized;
            float desiredSpeed = Mathf.Min(speed, Mathf.Sqrt(2.8f * Mathf.Max(.02f, CityPhysics.Flat(finalGoal - p.Root.position).magnitude)));
            p.MoveSpeed = Mathf.MoveTowards(p.MoveSpeed, desiredSpeed, dt * (urgent ? 6 : 1.8f));
            direction = p.Steering; float amount = Mathf.Min(gap, p.MoveSpeed * dt);
            var flags = p.Controller.Move(direction * amount + Vector3.down * dt * 2.5f);
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 side = Vector3.Cross(Vector3.up, direction) * ((p.Data.id & 1) == 0 ? 1 : -1);
                p.Controller.Move(side * amount * .45f + Vector3.down * dt);
            }
            if (direction.sqrMagnitude > .01f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-dt * 5));
            return Vector3.ProjectOnPlane(finalGoal - p.Root.position, Vector3.up).magnitude < .25f;
        }

        void Update()
        {
            if (ctx == null || !ctx.Ready || ctx.Paused) return; float dt = Time.deltaTime;
            TickStreetCrimes(dt);
            if (ctx.Clock >= nextTraffic)
            { foreach (var vehicle in ctx.Vehicles.Fleet) { vehicleVelocities[vehicle.Data.id] = vehicle.Body.linearVelocity; } SenseTraffic(); nextTraffic = ctx.Clock + .15f; }
            foreach (var p in People)
            {
                p.Velocity = (p.Root.position - p.LastPosition) / Mathf.Max(dt, .001f); p.LastPosition = p.Root.position;
                if (p.Down) continue;
                if (ctx.Combat.TickMeleeReaction(p, dt)) continue;
                if (TickPlayerReaction(p, dt)) continue;
                if (p.Controlled) continue;
                if (p.Bus != null)
                {
                    var bus = p.Bus;
                    if (bus.AtStop != null && bus.StopCount > p.BoardedStop && Mathf.Abs(bus.Speed) < .3f && ctx.Vehicles.PassengerTransition(p.Root) >= 1)
                    {
                        if (ctx.Vehicles.TryPassengerLanding(p.Root, out var exit) && ctx.Vehicles.LeavePassenger(p.Root, exit))
                        {
                            p.Controller.enabled = true; p.Bus = null; p.LastPosition = p.Root.position; p.NextSocial = ctx.Clock + 5; Choose(p);
                        }
                    }
                    continue;
                }
                if (p.TalkUntil > ctx.Clock) { Face(p, ctx.Player.position, dt); continue; }
                if (p.Calling) continue;
                if (p.DodgeUntil > ctx.Clock) { MoveToward(p, p.DodgeGoal, dt, 2.1f, true); continue; }
                if (p.FearUntil > ctx.Clock)
                {
                    if (!p.Data.stationary && !p.Data.sitting) { Vector3 away = Vector3.ProjectOnPlane(p.Root.position - p.Threat, Vector3.up).normalized; MoveToward(p, p.Root.position + away * 3, dt, 2.8f, true); }
                    continue;
                }
                if (TickHackReaction(p, dt)) continue;
                if (TickEmote(p, dt)) continue;
                if (p.SocialUntil > ctx.Clock && p.Partner != null) { TickSocialVoice(p); Face(p, p.Partner.Root.position, dt); continue; }
                if (p.Partner != null) Interrupt(p);
                if (p.Data.stationary || p.Data.sitting || p.Data.performance) { Social(p); continue; }
                p.Age += dt;
                if (p.State == "travel")
                {
                    if (!p.Crossing && p.Doorway == null && p.Stuck < .5f && ctx.Clock > p.NextPause)
                    { p.PauseUntil = ctx.Clock + R(.6f, 2.2f); p.NextPause = ctx.Clock + R(18, 48); }
                    if (!p.Crossing && ctx.Clock < p.PauseUntil) { p.MoveSpeed = 0; p.LookYaw = Mathf.Sin(ctx.Clock * 1.5f + p.Data.phase) * .35f; continue; }
                    if (MoveToward(p, p.Destination, dt, p.Speed * p.Pace * (p.TrafficBoostUntil > ctx.Clock ? 1.55f : ctx.Rain > .35f ? 1.14f : 1)))
                    {
                        p.State = "activity"; p.Age = 0; p.MoveSpeed = 0;
                    }
                    p.Stuck = p.Velocity.magnitude < .06f ? p.Stuck + dt : 0;
                    p.NoProgress = p.Velocity.magnitude < .12f && !p.Crossing ? p.NoProgress + dt : Mathf.Max(0, p.NoProgress - dt * .3f);
                    if (p.Stuck > 6 && !p.Crossing) { p.Path.Clear(); p.Stuck = 0; }
                    if (p.Age > 200 || p.NoProgress > 13) Choose(p);
                }
                else if (p.Age > p.Duration) Choose(p);
                else if (p.State == "activity" && p.Location >= 0)
                {
                    float yaw = ctx.Data.locations[p.Location].yaw * Mathf.Rad2Deg;
                    p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.Euler(0, yaw, 0), 1 - Mathf.Exp(-dt * 2));
                    p.LookYaw = Mathf.Sin(ctx.Clock * .55f + p.Data.phase) * .2f;
                }
                if (p.State == "activity" && p.Activity == "wait" && p.Location >= 0)
                {
                    string stopId = ctx.Data.locations[p.Location].id;
                    foreach (var bus in ctx.Vehicles.Fleet)
                    {
                        if (bus.Data.kind != "bus" || bus.AtStop == null || !stopId.EndsWith(bus.AtStop.id, StringComparison.Ordinal) || Mathf.Abs(bus.Speed) > .3f || ctx.Vehicles.PassengerCount(bus) >= 6) continue;
                        Vector3 door = ctx.Vehicles.PassengerEntryPoint(bus);
                        if (Vector3.Distance(p.Root.position, door) > 1.05f) { MoveToward(p, door, dt, 1.25f); break; }
                        if (ctx.Vehicles.BoardPassenger(p.Root, bus)) { p.Controller.enabled = false; p.Bus = bus; p.BoardedStop = bus.StopCount; p.State = "riding"; HideProps(p); Release(p); }
                        break;
                    }
                }
                Social(p);
            }
            UpdateAnimals(dt);
        }
        void Face(Resident p, Vector3 point, float dt)
        { var direction = Vector3.ProjectOnPlane(point - p.Root.position, Vector3.up); if (direction.sqrMagnitude > .01f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-dt * 5)); }
        void Social(Resident p)
        {
            if (p.Data.child || p.Data.performance || p.Bus != null || p.Crossing || p.SocialUntil > ctx.Clock || p.NextSocial > ctx.Clock || p.State == "activity" && p.Activity != "rest" && p.Activity != "chat") return;
            p.NextSocial = ctx.Clock + R(12, 24);
            if (ctx.Remote.DroneActive && Vector3.Distance(p.Root.position + Vector3.up * 2, ctx.Remote.DronePosition) < 12 && ctx.Visible(p.Root.position + Vector3.up * 2, ctx.Remote.DronePosition))
            { p.Gesture = (p.Data.id & 1) == 0 ? "photo" : "look"; p.SocialUntil = ctx.Clock + 3.8f; p.Threat = ctx.Remote.DronePosition; return; }
            if (ctx.Clock < nextConversation) return;
            if (ctx.Rain > .4f) return;
            foreach (var other in People)
            {
                if (other == p || other.Down || other.Controlled || other.Calling || other.Data.performance || other.Crossing || other.FearUntil > ctx.Clock || other.TalkUntil > ctx.Clock || other.SocialUntil > ctx.Clock) continue;
                float distance = Vector3.Distance(p.Root.position, other.Root.position); if (distance < 1.0f || distance > 2.6f || Vector3.Distance(p.Root.position, ctx.Player.position) > 28) continue;
                if (other.State == "activity" && other.Activity != "rest" && other.Activity != "chat") continue;
                if (!ctx.Visible(p.Root.position + Vector3.up * 1.7f, other.Root.position + Vector3.up * 1.7f)) continue;
                p.Partner = other; other.Partner = p; p.SocialUntil = other.SocialUntil = ctx.Clock + R(3.5f, 6); other.NextSocial = ctx.Clock + 25;
                p.NextMurmur = ctx.Clock; other.NextMurmur = ctx.Clock + 2.7f;
                TickSocialVoice(p);
                nextConversation = ctx.Clock + 8; HideProps(p); HideProps(other); ctx.Say(p.Data.name + " · " + (random.Next(2) == 0 ? "A gente se vê na praça?" : "Hoje a baía está bonita."), p.Root.position + Vector3.up * 2.5f, 2.8f); break;
            }
        }
        void SenseTraffic()
        {
            foreach (var p in People)
            {
                if (p.Down || p.Controlled || p.Calling || p.Bus != null || p.Data.stationary || p.Data.sitting || p.TalkUntil > ctx.Clock) continue;
                foreach (var vehicle in ctx.Vehicles.Fleet)
                {
                    Vector3 velocity = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity, Vector3.up); float speed = velocity.magnitude; if (speed < 1.2f) continue;
                    Vector3 delta = Vector3.ProjectOnPlane(p.Root.position - vehicle.Root.position, Vector3.up); if (delta.magnitude > 14) continue;
                    float time = Vector3.Dot(delta, velocity) / velocity.sqrMagnitude;
                    if (time < 0 || time > 2.1f) continue;
                    Vector3 closest = delta - velocity * time + Vector3.ProjectOnPlane(p.Velocity, Vector3.up) * time;
                    if (closest.magnitude > vehicle.Data.width * .5f + .6f || !ctx.Visible(vehicle.Root.position + Vector3.up, p.Root.position + Vector3.up)) continue;
                    p.Gesture = "brace"; p.Threat = vehicle.Root.position; p.SocialUntil = ctx.Clock + 1.1f;
                    if (p.Crossing) { p.TrafficBoostUntil = ctx.Clock + 1.3f; break; }
                    Vector3 side = Vector3.Cross(Vector3.up, velocity.normalized); if (Vector3.Dot(side, delta) < 0) side = -side;
                    Vector3 goal = p.Root.position + side * 1.2f; goal.y = ctx.Ground(goal.x, goal.z);
                    if (CanOccupy(p, goal) && !Physics.SphereCast(p.Root.position + Vector3.up, .4f, side, out _, 1.2f, ctx.AllMask, QueryTriggerInteraction.Ignore)) { p.DodgeGoal = goal; p.DodgeUntil = ctx.Clock + .6f; }
                    break;
                }
            }
        }

        Material Material(Color color)
        { if (!materials.TryGetValue(color, out var m)) { m = new Material(Shader.Find("Standard")); m.color = color; m.SetFloat("_Glossiness", .1f); materials[color] = m; } return m; }
        Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Destroy(go.GetComponent<Collider>()); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = Material(color); return go.transform; }
        Transform Group(Transform parent, string name) { var g = new GameObject(name).transform; g.SetParent(parent, false); return g; }
        public void OnFurnitureDestroyed(Transform furniture)
        {
            foreach(var p in People)
            {
                if(!p.Root||!p.Data.sitting||CityPhysics.Flat(p.Root.position-furniture.position).magnitude>2.2f)continue;
                p.Data.sitting=false;p.Data.stationary=false;
                if(p.Controller){p.Controller.height=2.32f;p.Controller.center=Vector3.up*1.16f;}
                if(p.Rig!=null)p.Rig.HipHeight=p.Data.rig.hipHeight;
                p.FearUntil=Mathf.Max(p.FearUntil,ctx.Clock+3);p.Threat=furniture.position;
            }
        }
        void Props(Resident p)
        {
            if (p.PropRoot || p.Rig == null) return; var r = p.Rig; var wood = new Color(.61f, .44f, .27f); var cream = new Color(.92f, .86f, .68f);
            p.PropRoot = Group(p.Root, "Ofícios e objetos");
            p.Phone = Group(r.Hands[1], "Celular"); Box(p.Phone, "Corpo", new Vector3(0, 0, .03f), new Vector3(.18f, .31f, .045f), new Color(.15f, .23f, .27f)); Box(p.Phone, "Tela", new Vector3(0, .005f, .005f), new Vector3(.14f, .25f, .01f), new Color(.48f, .79f, .74f));
            p.Book = Group(r.Chest, "Livro aberto"); p.Book.localPosition = new Vector3(0, .34f, .51f); p.Book.localRotation = Quaternion.Euler(-13, 0, 0);
            for (int i = 0; i < 2; i++) Box(p.Book, "Página", new Vector3(i == 0 ? -.18f : .18f, 0, 0), new Vector3(.35f, .07f, .4f), cream);
            p.Cup = Group(r.Hands[1], "Copo"); Box(p.Cup, "Copo", new Vector3(0, .05f, .03f), new Vector3(.18f, .23f, .18f), cream);
            p.Broom = Group(p.PropRoot, "Vassoura"); Box(p.Broom, "Cabo", new Vector3(0, .89f, 0), new Vector3(.055f, 1.6f, .055f), wood); Box(p.Broom, "Cerdas", new Vector3(0, .09f, 0), new Vector3(.55f, .18f, .20f), cream);
            p.Brush = Group(r.Hands[1], "Pincel"); Box(p.Brush, "Cabo", new Vector3(0, 0, .12f), new Vector3(.055f, .055f, .3f), wood); Box(p.Brush, "Tinta", new Vector3(0, 0, .3f), new Vector3(.11f, .07f, .1f), Color.yellow);
            p.Palette = Group(r.Hands[0], "Paleta"); Box(p.Palette, "Madeira", new Vector3(0, -.04f, .06f), new Vector3(.34f, .055f, .4f), wood);
            for (int i = 0; i < 3; i++) Box(p.Palette, "Pigmento", new Vector3((i - 1) * .09f, -.009f, .15f), new Vector3(.06f, .012f, .07f), i == 0 ? Color.cyan : i == 1 ? Color.yellow : new Color(.9f, .4f, .3f));
            p.Umbrella = Group(r.Hands[1], "Guarda-chuva"); Box(p.Umbrella, "Haste", new Vector3(0, .45f, 0), new Vector3(.035f, 1.02f, .035f), Color.gray);
            var canopy = new GameObject("Cobertura octogonal"); canopy.transform.SetParent(p.Umbrella, false); var mesh = new Mesh(); var vertices = new Vector3[48]; var indices = new int[48];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4; Vector3 top = new Vector3(0, 1.12f, 0), left = new Vector3(Mathf.Sin(a) * .84f, .83f, Mathf.Cos(a) * .84f), right = new Vector3(Mathf.Sin(b) * .84f, .83f, Mathf.Cos(b) * .84f);
                vertices[i * 6] = vertices[i * 6 + 3] = top; vertices[i * 6 + 1] = vertices[i * 6 + 5] = left; vertices[i * 6 + 2] = vertices[i * 6 + 4] = right;
                for (int j = 0; j < 6; j++) indices[i * 6 + j] = i * 6 + j;
            }
            mesh.vertices = vertices; mesh.triangles = indices; mesh.RecalculateNormals(); canopy.AddComponent<MeshFilter>().sharedMesh = mesh; canopy.AddComponent<MeshRenderer>().sharedMaterial = Material(new Color(.30f, .56f, .57f)); HideProps(p);
        }
        void HideProps(Resident p)
        { foreach (var prop in new[] { p.Phone, p.Book, p.Cup, p.Broom, p.Brush, p.Palette, p.Umbrella }) if (prop) prop.gameObject.SetActive(false); }
        void LateUpdate()
        {
            if (ctx == null || !ctx.Ready || ctx.Paused) return; float dt = Time.deltaTime;
            foreach (var p in People)
            {
                if (p.Rig == null || p.Down) continue; HideProps(p); float t = ctx.Clock + p.Data.phase;
                bool phoneHack = HackPhoneBusy(p) && p.Reaction.Kind == ReactionKind.None;
                bool emoting = p.EmoteUntil > ctx.Clock && p.FearUntil <= ctx.Clock && p.DodgeUntil <= ctx.Clock && !p.Crossing && !p.Controlled && !p.Calling;
                bool talking = p.TalkUntil > ctx.Clock || p.Partner != null, activity = p.State == "activity" && !talking && !emoting && !p.Controlled && p.FearUntil <= ctx.Clock && !IsReactingToHack(p) && p.Reaction.Kind == ReactionKind.None;
                bool observing = p.SocialUntil > ctx.Clock && (p.Gesture == "photo" || p.Gesture == "look") && !talking && !p.Controlled;
                bool rain = p.Bus == null && ctx.Rain > .3f && !activity && !talking && !p.Data.indoor && !p.Data.sitting && !p.Data.performance && !p.Controlled && p.FearUntil <= ctx.Clock && p.Reaction.Kind == ReactionKind.None && !p.Crossing && !Physics.Raycast(p.Root.position + Vector3.up * 2.4f, Vector3.up, 8, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                if (phoneHack || p.Calling || rain || observing && p.Gesture == "photo" || activity && (p.Activity == "read" || p.Activity == "phone" || p.Activity == "eat" || p.Activity == "work" || p.Activity == "paint" || p.Activity == "smoke")) Props(p);
                float speed = CityPhysics.Flat(p.Velocity).magnitude;
                float jog = 2.2f * p.Root.lossyScale.y * (p.Rig.Thigh + p.Rig.Shin) / .89f;
                var pose = new CityPose { Time = t, Speed = speed, Mode = speed > jog ? "run" : speed > .1f ? "walk" : "idle", Talking = talking, Seated = p.Data.sitting, SeatHeight = p.Rig.HipHeight - .10f, LookYaw = p.LookYaw };
                if (!string.IsNullOrEmpty(p.MotionMode)) { pose.Mode = p.MotionMode; pose.Progress = p.MotionProgress; }
                if (p.Bus != null) { pose.Seated = ctx.Vehicles.PassengerTransition(p.Root) >= .75f; pose.SeatHeight = .67f; pose.Mode = pose.Seated ? "sit" : "walk"; pose.Speed = pose.Seated ? 0 : .9f; ctx.Actors.Animate(p.Rig, pose, dt); continue; }
                if (activity && (p.Activity == "read" || p.Activity == "phone")) pose.LookPitch = .24f;
                if (p.Calling) { pose.LookPitch = .1f; activity = false; }
                bool music = p.Data.performance && ctx.Systems.MusicPlaying && p.Reaction.Kind == ReactionKind.None && p.FearUntil <= ctx.Clock;
                if (music) { pose.HandNodes = Map(ctx.Data.music.handNodes); pose.LookPitch = Mathf.Sin(t * 3) * .035f; }
                bool watchingMusic = !emoting && !p.Data.performance && !p.Controlled && !talking && p.FearUntil <= ctx.Clock && Vector3.Distance(p.Root.position, ctx.Data.music.position) < 7 && ctx.Rain < .4f && (p.Data.musicGuest || p.State != "travel");
                if (watchingMusic) { pose.Clap = Mathf.Sin(t * .4f + p.Data.id) > .3f; pose.LookPitch = Mathf.Sin(t * 2.4f) * .035f; Face(p, ctx.Data.music.position, dt); }
                ctx.Actors.Animate(p.Rig, pose, dt);
                if (emoting) { CityEmoteAnimation.Apply(p.Rig, p.Emote, ctx.Clock - p.EmoteStarted, p.EmoteUntil - ctx.Clock, p.Data.sitting); continue; }
                var r = p.Rig;
                if (activity)
                {
                    if (p.Activity == "read") { p.Book.gameObject.SetActive(true); for (int i = 0; i < 2; i++) CityActors.Reach(r, i, p.Book.TransformPoint(new Vector3(i == 0 ? -.25f : .25f, -.035f, .06f))); }
                    if (p.Activity == "phone") { p.Phone.gameObject.SetActive(true); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.29f, 1.37f, .46f))); CityActors.Reach(r, 0, p.Root.TransformPoint(new Vector3(-.16f, 1.25f, .44f))); CityActors.Upright(p.Phone, p.Root, new Vector3(-10, 0, 0)); }
                    if (p.Activity == "eat" || p.Activity == "smoke") { p.Cup.gameObject.SetActive(true); float lift = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * .8f)), 3); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.2f, 1.22f + lift * .63f, .39f - lift * .09f))); CityActors.Upright(p.Cup, p.Root, new Vector3(-10 * lift, 0, 0)); }
                    if (p.Activity == "work") { p.Broom.gameObject.SetActive(true); p.Broom.localPosition = new Vector3(0, .025f, .43f + .06f * Mathf.Sin(t * 1.7f)); for (int i = 0; i < 2; i++) CityActors.Reach(r, i, p.Broom.TransformPoint(new Vector3(0, i == 0 ? 1.35f : 1.56f, 0))); }
                    if (p.Activity == "paint") { p.Brush.gameObject.SetActive(true); p.Palette.gameObject.SetActive(true); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.33f + .07f * Mathf.Sin(t * 1.2f), 1.55f + .14f * Mathf.Sin(t * 1.9f), .58f))); CityActors.Reach(r, 0, p.Root.TransformPoint(new Vector3(-.28f, 1.2f, .38f))); CityActors.Upright(p.Brush, p.Root); CityActors.Upright(p.Palette, p.Root); }
                }
                if (p.Calling) { p.Phone.gameObject.SetActive(true); CityActors.Reach(r, 1, r.Root.TransformPoint(new Vector3(.35f, 1.93f, .06f))); CityActors.Upright(p.Phone, p.Root, new Vector3(0, 90, 0)); }
                if (phoneHack) { p.Phone.gameObject.SetActive(true); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.29f, 1.37f, .46f))); CityActors.Reach(r, 0, p.Root.TransformPoint(new Vector3(-.16f, 1.25f, .44f))); CityActors.Upright(p.Phone, p.Root, new Vector3(-10, 0, 0)); }
                if (rain)
                { p.Umbrella.gameObject.SetActive(true); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.40f, 1.66f, .24f))); CityActors.Upright(p.Umbrella, p.Root); }
                if (observing || p.SocialUntil > ctx.Clock && p.Gesture == "brace")
                {
                    if (observing && ctx.Remote.DroneActive && !IsReactingToHack(p)) p.Threat = ctx.Remote.DronePosition;
                    Vector3 look = p.Threat - p.Root.position; float elevation = Mathf.Clamp(Mathf.Atan2(look.y - 2, new Vector2(look.x, look.z).magnitude), -.15f, .6f);
                    r.Head.localRotation = Quaternion.Euler(-elevation * Mathf.Rad2Deg, Mathf.Clamp(Vector3.SignedAngle(p.Root.forward, Vector3.ProjectOnPlane(look, Vector3.up), Vector3.up), -65, 65), 0);
                    if (p.Gesture == "photo") { p.Phone.gameObject.SetActive(true); CityActors.Reach(r, 1, p.Root.TransformPoint(new Vector3(.12f, 1.8f + elevation * .22f, .51f - elevation * .1f))); CityActors.Reach(r, 0, p.Root.TransformPoint(new Vector3(-.09f, 1.77f + elevation * .22f, .51f - elevation * .1f))); CityActors.Upright(p.Phone, p.Root, new Vector3(-elevation * Mathf.Rad2Deg, 0, 0)); }
                    if (p.Gesture == "brace") { r.Arms[0].localRotation = Quaternion.Euler(-50, 0, -28); r.Elbows[0].localRotation = Quaternion.Euler(-75, 0, 0); }
                }
                PosePlayerReaction(p, dt);
                ctx.Combat.PoseMelee(p.Rig);
                PoseStreetCrime(p);
                ctx.Combat.PoseResidentWeapon(p);
                if (ctx.Combat.MeleeActing(p.Root)) HideProps(p);
            }
        }
        void UpdateAnimals(float dt)
        {
            foreach (var a in animals)
            {
                if (ctx.Actors.PetTarget == a.Head || ctx.Actors.PetTarget == a.Root) { if (a.Head) a.Head.localRotation = Quaternion.Euler(Mathf.Sin(ctx.Clock * 7) * 5, 0, 0); continue; }
                if (a.Flying)
                {
                    a.Flight += dt;
                    if (a.Flight > a.FlightDuration * .85f && !ctx.Free(a.Goal, .28f, .70f))
                    {
                        a.Flight = a.FlightDuration * .85f; a.LandingWait += dt;
                        if (a.LandingWait > 2)
                        {
                            Vector3 alternative = a.Goal + new Vector3(R(-1.4f, 1.4f), 0, R(-1.4f, 1.4f)); alternative.y = ctx.Ground(alternative.x, alternative.z);
                            if (ctx.Free(alternative, .28f, .70f) && ctx.Visible(a.Root.position, alternative + Vector3.up * .4f)) a.Goal = alternative;
                            a.LandingWait = 0;
                        }
                    }
                    float progress = Mathf.Clamp01(a.Flight / a.FlightDuration); a.Root.position = Vector3.Lerp(a.FlightFrom, a.Goal, progress) + Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 2.1f);
                    for (int i = 0; i < a.Wings.Length; i++) if (a.Wings[i]) a.Wings[i].localRotation = Quaternion.Euler(0, 0, (i == 0 ? -1 : 1) * (35 + 40 * Mathf.Sin(ctx.Clock * 19)));
                    if (progress >= 1) { a.Flying = false; a.Cooldown = ctx.Clock + R(8, 16); foreach (var wing in a.Wings) if (wing) wing.localRotation = Quaternion.identity; }
                    continue;
                }
                float playerDistance = Vector3.Distance(ctx.Player.position, a.Root.position);
                if (a.Data.type == "pigeon" && ctx.Clock > a.Cooldown && playerDistance < (ctx.Motor.Speed > 5 ? 5 : 2) && ctx.Motor.Speed > .6f)
                {
                    Vector3 away = Vector3.ProjectOnPlane(a.Root.position - ctx.Player.position, Vector3.up).normalized;
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        Vector3 goal = a.Root.position + Quaternion.Euler(0, R(-70, 70), 0) * away * R(4, 8); goal.y = ctx.Ground(goal.x, goal.z);
                        if (Vector3.Distance(goal, a.Home) > 14 || !ctx.Free(goal, .28f, .7f) || !ctx.Visible(a.Root.position + Vector3.up * 1.4f, goal + Vector3.up * 1.4f)) continue;
                        a.Goal = goal; a.FlightFrom = a.Root.position; a.FlightDuration = 1.2f + Vector3.Distance(a.Goal, a.Root.position) / 5; a.Flight = 0; a.Flying = true; a.Root.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(goal - a.Root.position, Vector3.up)); ctx.Sound("bird", a.Root.position, .7f); break;
                    }
                    a.Cooldown = ctx.Clock + 3;
                }
                if (ctx.Clock > a.ChangeAt)
                { Vector2 circle = new Vector2(R(-1, 1), R(-1, 1)).normalized * R(1, 5); a.Goal = a.Home + new Vector3(circle.x, 0, circle.y); a.Goal.y = ctx.Ground(a.Goal.x, a.Goal.z); a.ChangeAt = ctx.Clock + R(3, 7); }
                Vector3 direction = Vector3.ProjectOnPlane(a.Goal - a.Root.position, Vector3.up); float speed = a.Data.type == "dog" ? .7f : a.Data.type == "cat" ? .45f : .35f;
                if (direction.magnitude > .2f && playerDistance > 1.15f)
                {
                    Vector3 next = a.Root.position + direction.normalized * speed * dt;
                    if (!Physics.SphereCast(a.Root.position + Vector3.up * .35f, .28f, direction.normalized, out _, speed * dt + .15f, ctx.AllMask, QueryTriggerInteraction.Ignore)) { next.y = ctx.Ground(next.x, next.z); a.Root.position = next; }
                    else a.ChangeAt = ctx.Clock;
                    a.Root.rotation = Quaternion.Slerp(a.Root.rotation, Quaternion.LookRotation(direction), dt * 5);
                    for (int i = 0; i < a.Legs.Length; i++) if (a.Legs[i]) a.Legs[i].localRotation = Quaternion.Euler(Mathf.Sin(ctx.Clock * 6 + (i == 0 || i == 3 ? 0 : Mathf.PI)) * 22, 0, 0);
                }
            }
        }
    }
}
