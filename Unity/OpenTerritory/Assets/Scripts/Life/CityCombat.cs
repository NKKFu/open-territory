using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    [Serializable] public sealed class CityWeaponSave { public string id; public int loaded, reserve; }
    [Serializable] public sealed class CityLoadoutSave { public int version = 1; public CityWeaponSave[] weapons; }
    public sealed class CityWeaponDefinition
    {
        public string Id, Name; public int Capacity, InitialReserve, MaxReserve; public float Interval, Reload, Range, Damage, Stun; public bool Automatic;
    }

    [DefaultExecutionOrder(-70)]
    public sealed partial class CityCombat : MonoBehaviour
    {
        public static readonly CityWeaponDefinition[] Weapons = {
            new CityWeaponDefinition { Id="pulse",Name="Pulsor Maré",Capacity=1,InitialReserve=8,MaxReserve=12,Interval=.65f,Reload=1.1f,Range=18,Damage=12,Stun=4 },
            new CityWeaponDefinition { Id="sidearm",Name="P-9 Farol",Capacity=8,InitialReserve=16,MaxReserve=64,Interval=.29f,Reload=1.45f,Range=52,Damage=36 },
            new CityWeaponDefinition { Id="carbine",Name="C-12 Orla",Capacity=16,InitialReserve=32,MaxReserve=96,Interval=.15f,Reload=1.85f,Range=65,Damage=25,Automatic=true }
        };
        sealed class HealthState
        {
            public Transform Root; public CityResidents.Resident Person; public float Health = 100, LastHit = -100, DownUntil, SafeUntil, FearUntil;
            public bool Fallen, WasSwimming; public Rigidbody Body; public CapsuleCollider Collider; public CharacterController Controller;
            public Vector3 Home; public Quaternion StandingRotation; public float RetryAt;
        }
        sealed class PracticePlate { public Transform Target, Lamp; public Quaternion Rotation; public Renderer Light; public Color Original; public float Age = 2; public int Hits; }
        CityContext ctx;
        public float Health = 100;
        public string Equipped { get; private set; }
        public bool Aiming { get; private set; }
        public bool WeaponVisible => Equipped != null && CanUse();
        public CityResidents.Resident AimTarget { get; private set; }
        public bool Down => playerState != null && playerState.Fallen;
        public bool Reloading => reloadUntil > 0;
        public Vector3 AimPoint { get; private set; }
        public float Recoil => ctx == null ? 0 : Mathf.Clamp01((recoilUntil - ctx.Clock) / .14f);
        public int Shots { get; private set; }
        public Vector3 LastImpact { get; private set; }
        public int PracticeRounds { get; private set; }
        public int PracticeHits { get; private set; }
        public event Action Changed;
        readonly Dictionary<string, CityWeaponSave> loadout = new Dictionary<string, CityWeaponSave>();
        readonly Dictionary<Transform, HealthState> health = new Dictionary<Transform, HealthState>();
        readonly Dictionary<string, Transform> models = new Dictionary<string, Transform>();
        readonly Dictionary<string, CityWeaponVisual> weaponVisuals = new Dictionary<string, CityWeaponVisual>();
        public CityGunEffects GunEffects { get; private set; }
        public Vector3 LastMuzzle { get; private set; }
        public float LastGunfireAt { get; private set; } = -100;
        CityWeaponHands playerHands;
        public CityWeaponVisual Visual(string id) => id != null && weaponVisuals.TryGetValue(id, out var visual) ? visual : null;
        readonly List<PracticePlate> plates = new List<PracticePlate>();
        readonly Collider[] overlaps = new Collider[48];
        readonly RaycastHit[] hits = new RaycastHit[128];
        HealthState playerState; float reloadUntil, reloadStarted, nextShot, recoilUntil; bool aimToggle, triggerWasHeld;

        public void Initialize(CityContext context)
        {
            ctx = context; playerState = State(ctx.Player); Grant("pulse");
            BuildWeapons(); GunEffects = gameObject.AddComponent<CityGunEffects>(); GunEffects.Initialize(ctx);
            InitializeResidentGuns();
            if (ctx.Vehicles) ctx.Vehicles.Impact += VehicleImpact;
            BindPracticeRange();
        }
        void BuildWeapons()
        {
            var hand = ctx.Actors.PlayerRig?.Hands[1]; if (!hand) return;
            playerHands = new CityWeaponHands(ctx.Actors.PlayerRig);
            foreach (var definition in Weapons)
            {
                var visual = new CityWeaponVisual(definition, hand); weaponVisuals[definition.Id] = visual; models[definition.Id] = visual.Root;
            }
        }
        public CityWeaponDefinition Definition(string id) { foreach (var definition in Weapons) if (definition.Id == id) return definition; return null; }
        public bool Owns(string id) => id != null && loadout.ContainsKey(id);
        public bool Grant(string id, int reserve = -1)
        {
            var definition = Definition(id); if (definition == null || Owns(id)) return false;
            loadout[id] = new CityWeaponSave { id = id, loaded = definition.Capacity, reserve = reserve < 0 ? definition.InitialReserve : Mathf.Clamp(reserve, 0, definition.MaxReserve) }; Changed?.Invoke(); return true;
        }
        public bool Remove(string id)
        { if (id == "pulse" || !Owns(id)) return false; if (Equipped == id) Holster(); loadout.Remove(id); Changed?.Invoke(); return true; }
        public int Ammo(string id) => Owns(id) ? loadout[id].loaded : 0;
        public int Reserve(string id) => Owns(id) ? loadout[id].reserve : 0;
        public int MaxReserve(string id) => Definition(id)?.MaxReserve ?? 0;
        public int AddAmmo(string id, int amount)
        { if (!Owns(id) || amount <= 0) return 0; int added = Mathf.Min(Mathf.Min(amount, 9999), MaxReserve(id) - loadout[id].reserve); loadout[id].reserve += added; if (added > 0) Changed?.Invoke(); return added; }
        public string ExportLoadoutJson()
        { var save = new CityLoadoutSave { weapons = new CityWeaponSave[loadout.Count] }; int i = 0; foreach (var pair in loadout) save.weapons[i++] = new CityWeaponSave { id = pair.Key, loaded = pair.Value.loaded, reserve = pair.Value.reserve }; return JsonUtility.ToJson(save); }
        public void RestoreLoadoutJson(string json)
        {
            CityLoadoutSave save; try { save = JsonUtility.FromJson<CityLoadoutSave>(json); } catch { return; }
            if (save?.weapons == null || save.version != 1) return;
            loadout.Clear(); foreach (var item in save.weapons) { if (item == null) continue; var definition = Definition(item.id); if (definition == null || loadout.ContainsKey(item.id)) continue; loadout[item.id] = new CityWeaponSave { id = item.id, loaded = Mathf.Clamp(item.loaded, 0, definition.Capacity), reserve = Mathf.Clamp(item.reserve, 0, definition.MaxReserve) }; }
            if (!Owns("pulse")) Grant("pulse"); Holster(); Changed?.Invoke();
        }
        public void Holster()
        { CancelMelee(); Equipped = null; Aiming = false; AimTarget = null; aimToggle = false; reloadUntil = 0; triggerWasHeld = false; foreach (var model in models.Values) model.gameObject.SetActive(false); playerHands?.Show(false); }
        public bool Select(string id)
        {
            if (!Owns(id) || !CanUse()) return false; CancelMelee(); Equipped = id; reloadUntil = 0; Aiming = false; AimTarget = null; aimToggle = false;
            foreach (var pair in models) pair.Value.gameObject.SetActive(pair.Key == id);
            return true;
        }
        public bool IsThreatening() => (Aiming || Punching) && !Down;
        public bool CanUse() => ctx != null && !Down && !ctx.Motor.Busy && !ctx.Motor.Riding && !ctx.Motor.Swimming && !ctx.Motor.Remote && !ctx.Police.Custody && !ctx.Motor.SeatHeight.HasValue;
        public bool Reload()
        {
            var definition = Definition(Equipped); if (definition == null || !CanUse() || Reloading || Ammo(Equipped) == definition.Capacity || Reserve(Equipped) == 0) return false;
            reloadStarted = ctx.Clock; reloadUntil = ctx.Clock + definition.Reload; ctx.Sound("reload-" + Equipped, ctx.Player.position, .7f); return true;
        }
        HealthState State(Transform root)
        {
            if (health.TryGetValue(root, out var result)) return result;
            var person = ctx.Residents.Find(root); result = new HealthState { Root = root, Person = person, Controller = root.GetComponent<CharacterController>(), Home = root.position, StandingRotation = root.rotation };
            health[root] = result; return result;
        }
        public bool IsDown(Transform root) => root && health.TryGetValue(root, out var state) && state.Fallen;
        public float HealthOf(Transform root) => root && health.TryGetValue(root, out var state) ? state.Health : 100;
        public void ForgetTemporaryActor(Transform root)
        { if (root && root != ctx.Player && !IsDown(root)) { health.Remove(root); meleeReactions.Remove(root); } }
        public void Damage(float amount, Vector3 source) => Damage(ctx.Player, amount, source);
        public bool Damage(Transform root, float amount, Vector3 source, float stun = 0)
        {
            if (!root || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 && stun <= 0) return false;
            var state = State(root); if (state.Fallen || ctx.Clock < state.SafeUntil) return false;
            if (stun <= 0) state.Health = Mathf.Max(0, state.Health - Mathf.Clamp(amount, 0, 100)); state.LastHit = ctx.Clock; state.FearUntil = ctx.Clock + 4;
            if (state.Person != null) { state.Person.Health = state.Health; ctx.Residents.Startle(state.Person, source, 4); }
            if (root == ctx.Player) { InterruptPunch(); Health = state.Health; ctx.Actors.CancelInteraction(); ctx.Sound("hurt", root.position); }
            if (stun > 0 || state.Health <= 0) Fall(state, stun > 0 ? Mathf.Clamp(stun, .4f, 12) : 7, source);
            return true;
        }
        public float HealAtClinic()
        {
            if (Down || IsThreatening() || Health >= 100) return 0; float healed = 100 - Health; Health = playerState.Health = 100; return healed;
        }
        public void Startle(Transform root, float seconds = 4)
        { if (!root || root == ctx.Player) return; var state = State(root); state.FearUntil = Mathf.Max(state.FearUntil, ctx.Clock + seconds); if (state.Person != null) ctx.Residents.Startle(state.Person, ctx.Player.position, seconds); }
        void Fall(HealthState state, float duration, Vector3 source)
        {
            state.Fallen = true; state.DownUntil = ctx.Clock + duration; state.StandingRotation = Quaternion.Euler(0, state.Root.eulerAngles.y, 0);
            if (state.Controller) state.Controller.enabled = false;
            if (state.Person != null) ctx.Residents.SetDown(state.Person, true);
            if (state.Root == ctx.Player) { Holster(); state.WasSwimming = ctx.Motor.Swimming; ctx.Motor.ClearMotion(); }
            state.Collider = state.Root.gameObject.AddComponent<CapsuleCollider>(); state.Collider.radius = .34f; state.Collider.height = 2.04f; state.Collider.center = Vector3.up * 1.08f;
            state.Body = state.Root.gameObject.AddComponent<Rigidbody>(); state.Body.mass = 68; state.Body.linearDamping = 1.2f; state.Body.angularDamping = 2.3f;
            state.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; state.Body.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 away = Vector3.ProjectOnPlane(state.Root.position - source, Vector3.up).normalized; if (away.sqrMagnitude < .01f) away = -state.Root.forward;
            state.Body.AddForce((away * 1.7f + Vector3.up * .6f), ForceMode.VelocityChange); state.Body.AddTorque(Vector3.Cross(Vector3.up, away) * 5, ForceMode.VelocityChange);
        }
        bool RecoverySpot(HealthState state, out Vector3 point)
        {
            Vector3 origin = state.Root.position;
            if (state.Person != null && state.Person.Data.stationary) origin = state.Home;
            if (state.Collider) state.Collider.enabled = false;
            for (int attempt = 0; attempt < 25; attempt++)
            {
                float radius = attempt == 0 ? 0 : .35f + (attempt / 8) * .4f, angle = attempt * 2.399963f;
                point = origin + new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius); point.y = ctx.Ground(point.x, point.z);
                if (state.WasSwimming && point.z < -165) { point.y = -.95f; return true; }
                int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .43f, point + Vector3.up * 1.9f, .39f, overlaps, ctx.AllMask, QueryTriggerInteraction.Ignore);
                bool clear = true; for (int i = 0; i < count; i++) if (overlaps[i] && !overlaps[i].transform.IsChildOf(state.Root)) { clear = false; break; }
                if (clear) return true;
            }
            if (state.Collider) state.Collider.enabled = true; point = origin; return false;
        }
        void Recover(HealthState state)
        {
            if (ctx.Clock < state.RetryAt) return;
            if (!RecoverySpot(state, out var point)) { state.RetryAt = ctx.Clock + .5f; return; }
            if (state.Body) { state.Body.isKinematic = true; Destroy(state.Body); } if (state.Collider) Destroy(state.Collider);
            state.Root.SetPositionAndRotation(point, state.StandingRotation); state.Fallen = false; state.SafeUntil = ctx.Clock + 1.5f; state.Health = Mathf.Max(54, state.Health); state.LastHit = ctx.Clock;
            if (state.Controller) state.Controller.enabled = !state.WasSwimming;
            if (state.Person != null) { state.Person.Health = state.Health; ctx.Residents.SetDown(state.Person, false); }
            if (state.Root == ctx.Player) { Health = state.Health; ctx.Motor.ClearMotion(); }
        }
        void VehicleImpact(CityVehicles.Vehicle vehicle, float speed, Collider collider)
        {
            if (ctx.Paused || speed < 2 || !collider) return;
            var p = ctx.Residents.FromCollider(collider);
            Transform victim = p != null && p.Bus == null ? p.Root :
                collider.transform.IsChildOf(ctx.Player) && !ctx.Motor.Riding ? ctx.Player : null;
            if (!victim || !Damage(victim, Mathf.Clamp((speed - 1.5f) * 9, 4, 90), vehicle.Position)) return;
            var state = State(victim);
            // Damage and knockdown are independent: a full-health resident
            // must still fall when the first impact deals less than 100 damage.
            if (!state.Fallen) Fall(state, Mathf.Clamp(1.2f + speed * .16f, 1.5f, 5), vehicle.Position);
            if (state.Body)
            {
                var direction = CityPhysics.Flat(vehicle.Body.linearVelocity).normalized;
                if (direction.sqrMagnitude < .01f) direction = CityPhysics.Flat(victim.position - vehicle.Position).normalized;
                state.Body.linearVelocity = direction * Mathf.Clamp(speed * .55f, 1.7f, 11) + Vector3.up * Mathf.Clamp(speed * .08f, .6f, 2);
            }
            if (p != null && ctx.Motor.Riding == vehicle.Root) ctx.Police.Report("atropelamento", p.Root.position, p, true);
        }
        bool FirstHit(Vector3 from, Vector3 direction, float distance, out RaycastHit result, Transform ignored = null)
        {
            var shooter = ignored ? ignored : ctx.Player;
            int count = Physics.RaycastNonAlloc(from, direction, hits, distance, ctx.AllMask | (1 << CityContext.PlayerLayer), QueryTriggerInteraction.Ignore); float nearest = float.MaxValue; result = default;
            for (int i = 0; i < count; i++) { var hit = hits[i]; if (!hit.collider || hit.transform.IsChildOf(shooter)) continue; if (hit.distance < nearest) { nearest = hit.distance; result = hit; } } return nearest < float.MaxValue;
        }
        void BindPracticeRange()
        {
            if (ctx.Data.practice != null && ctx.Data.practice.targets != null && ctx.Data.practice.targets.Length > 0)
            {
                for (int i = 0; i < ctx.Data.practice.targets.Length; i++)
                {
                    var target = ctx.Node(ctx.Data.practice.targets[i]); var lamp = i < ctx.Data.practice.lamps.Length ? ctx.Node(ctx.Data.practice.lamps[i]) : null;
                    if (!target) continue;
                    var plate = new PracticePlate { Target = target, Lamp = lamp, Rotation = target.localRotation, Light = lamp ? lamp.GetComponent<Renderer>() : null };
                    if (plate.Light) plate.Original = plate.Light.material.color; plates.Add(plate);
                }
                return;
            }
            float floor = ctx.Ground(54, 104);
            foreach (float x in new[] { 51.92f, 54f, 56.08f })
            {
                Transform target = null, lamp = null;
                foreach (var node in ctx.Nodes.Values)
                {
                    if (Vector3.Distance(node.position, new Vector3(x, floor + 2.67f, 97.288f)) < .035f && node.childCount > 0) target = node;
                    if (Vector3.Distance(node.position, new Vector3(x, floor + 2.88f, 97.27f)) < .035f && node.GetComponent<Renderer>()) lamp = node;
                }
                if (!target) continue;
                var plate = new PracticePlate { Target = target, Lamp = lamp, Rotation = target.localRotation, Light = lamp ? lamp.GetComponent<Renderer>() : null };
                if (plate.Light) plate.Original = plate.Light.material.color; plates.Add(plate);
            }
        }
        public bool AuthorizedPractice(Vector3 feet, RaycastHit hit)
        {
            if (!hit.collider || Mathf.Abs(feet.x - 54) >= 3.27f || feet.z < 105.1f || feet.z > 110.5f || Mathf.Abs(feet.y - ctx.Ground(54, 104)) >= .65f || Mathf.Abs(hit.point.z - 97.23f) >= .035f) return false;
            foreach (var solid in ctx.Data.solids)
                if (solid.rangeBackstop && Mathf.Abs(hit.collider.bounds.center.x - solid.x) < .06f && Mathf.Abs(hit.collider.bounds.center.z - solid.z) < .06f && Mathf.Abs(hit.point.x - solid.x) <= solid.w * .5f && hit.point.y >= solid.bottom && hit.point.y <= solid.top) return true;
            return false;
        }
        void PracticeImpact(Vector3 point)
        {
            PracticeRounds++; float floor = ctx.Ground(54, 104);
            foreach (var plate in plates)
            {
                if (Mathf.Abs(point.x - plate.Target.position.x) >= .745f || point.y - floor <= .89f || point.y - floor >= 2.67f) continue;
                PracticeHits++; plate.Hits++; plate.Age = 0; if (plate.Light) plate.Light.material.color = new Color(1, .77f, .28f);
                ctx.Sound("metal", point, .7f); break;
            }
        }
        public bool Fire()
        {
            if (ctx == null || !ctx.Ready || ctx.Paused) return false;
            var weapon = Definition(Equipped); if (weapon == null || !CanUse() || !Aiming || Reloading || ctx.Clock < nextShot) return false;
            if (Ammo(Equipped) <= 0) { ctx.Sound("empty", ctx.Player.position, .45f); ctx.Say(Reserve(Equipped) > 0 ? "Recarregue · R" : "Sem munição", ctx.Player.position + Vector3.up * 1.5f, 1.2f); nextShot = ctx.Clock + .4f; return false; }
            string previousSpeech = ctx.Speech; float previousSpeechUntil = ctx.SpeechUntil;
            loadout[Equipped].loaded--; Changed?.Invoke(); Shots++; nextShot = ctx.Clock + weapon.Interval; recoilUntil = ctx.Clock + .14f;
            if (weapon.Id != "pulse") LastGunfireAt = ctx.Clock;
            var aim = ctx.Follow.AimRay;
            Vector3 target = FirstHit(aim.origin, aim.direction, weapon.Range + 8, out var visualHit) ? visualHit.point : aim.GetPoint(weapon.Range);
            Vector3 chest = ctx.Player.position + Vector3.up * 1.48f;
            var visual = Visual(Equipped);
            if (visual != null) visual.Root.rotation = Quaternion.LookRotation(target - visual.Root.position);
            Vector3 muzzle = visual != null ? visual.Muzzle.position : chest + ctx.Player.forward * .6f;
            RaycastHit hit; bool blocked = FirstHit(chest, (muzzle - chest).normalized, Vector3.Distance(chest, muzzle), out hit);
            bool chestBlocked = blocked;
            if (!blocked)
            {
                // A ray starting in cover misses that collider in PhysX. Check its
                // volume too, so a hand pushed through a corner cannot bypass it.
                int count = Physics.OverlapSphereNonAlloc(muzzle, .025f, overlaps, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) if (overlaps[i] && Vector3.Distance(overlaps[i].ClosestPoint(muzzle), muzzle) < .002f) { blocked = true; target = muzzle; break; }
            }
            if (!blocked) blocked = FirstHit(muzzle, (target - muzzle).normalized, weapon.Range, out hit);
            Vector3 impact = blocked && hit.collider ? hit.point : blocked ? target : muzzle + (target - muzzle).normalized * weapon.Range;
            LastImpact=impact;
            CityResidents.Resident victim = blocked && hit.collider ? ctx.Residents.FromCollider(hit.collider) : null;
            if (victim != null) { if (ReceiveGunshot(victim.Root, weapon, hit, muzzle)) Provoke(victim); ctx.Police.Report("agressao", victim.Root.position, victim, false); }
            else if (blocked && hit.collider) ctx.Systems.Hit(hit.collider, (impact - muzzle).normalized * 3, weapon.Damage > 0 ? weapon.Damage : 18, impact, CityPropDamageKind.Bullet);
            bool practice = victim == null && blocked && AuthorizedPractice(ctx.Player.position, hit);
            if (practice) PracticeImpact(impact);
            if (weapon.Id != "pulse" && !practice) ctx.Police.Report("disparo", ctx.Player.position, null, true);
            if (!practice && weapon.Id == "pulse") ctx.Residents.ReactToCrime("agressao", muzzle, victim, false);
            Vector3 effectMuzzle = chestBlocked && hit.collider ? hit.point + hit.normal * .02f : muzzle;
            LastMuzzle = effectMuzzle;
            ctx.Sound(weapon.Id == "pulse" ? "pulse" : weapon.Id == "carbine" ? "carbine" : "shot", effectMuzzle);
            if (visual != null) { visual.Kick(ctx.Clock); GunEffects.Shoot(weapon.Id, visual, effectMuzzle, impact, blocked ? hit : default, victim != null); }
            if (ctx.Speech == previousSpeech && ctx.SpeechUntil == previousSpeechUntil) ctx.Say(Ammo(Equipped) + " / " + Reserve(Equipped), muzzle + Vector3.up * .28f, .8f);
            AimPoint = impact; return true;
        }
        void Update()
        {
            if (ctx == null || !ctx.Ready || ctx.Paused) return; float dt = Time.deltaTime; var input = ctx.Input;
            foreach (var plate in plates) if (plate.Age < 1.2f) { plate.Age += dt; plate.Target.localRotation = plate.Rotation * Quaternion.Euler(-16 * Mathf.Sin(plate.Age * 24) * Mathf.Exp(-plate.Age * 4.5f), 0, 0); if (plate.Age >= 1.2f) { plate.Target.localRotation = plate.Rotation; if (plate.Light) plate.Light.material.color = plate.Original; } }
            foreach (var state in health.Values)
            {
                if (state.Fallen)
                {
                    if (state.WasSwimming && state.Body) { state.Body.useGravity = false; Vector3 position = state.Body.position; position.y = -.45f + Mathf.Sin(ctx.Clock * 1.5f) * .05f; state.Body.MovePosition(position); }
                    if (ctx.Clock >= state.DownUntil) Recover(state); continue;
                }
                if (state.Health < 100 && ctx.Clock - state.LastHit > 7) { state.Health = Mathf.Min(100, state.Health + dt * 7); if (state.Person != null) state.Person.Health = state.Health; }
            }
            Health = playerState.Health;
            if (input.Holster) Holster(); if (input.Weapon >= 1 && input.Weapon <= 3) Select(Weapons[input.Weapon - 1].Id);
            if (!CanUse())
            {
                CancelMelee();
                CancelMeleeReplies();
                UpdateMeleeReplies();
                Aiming = false; AimTarget = null; aimToggle = false; triggerWasHeld = false;
                if (ctx.Motor.Riding || ctx.Motor.Swimming || ctx.Motor.Remote || Down) Holster();
                else { foreach (var model in models.Values) model.gameObject.SetActive(false); playerHands?.Show(false); }
                return;
            }
            foreach (var pair in models) pair.Value.gameObject.SetActive(pair.Key == Equipped);
            if (input.AimToggle && Equipped != null) aimToggle = !aimToggle; Aiming = Equipped != null && (input.AimHeld || aimToggle);
            if (input.Reload && Equipped != null) Reload();
            else if (input.Fall && Equipped == null && !Down) Fall(playerState, 2.7f, ctx.Player.position + ctx.Player.forward);
            if (Reloading && ctx.Clock >= reloadUntil && Equipped != null)
            { var definition = Definition(Equipped); int transfer = Mathf.Min(definition.Capacity - Ammo(Equipped), Reserve(Equipped)); loadout[Equipped].loaded += transfer; loadout[Equipped].reserve -= transfer; reloadUntil = 0; Changed?.Invoke(); ctx.Sound("reload-ready", ctx.Player.position); }
            triggerWasHeld = input.FireHeld;
            UpdateMelee();
            UpdateMeleeReplies();
        }
        // Resolve the reticle and trigger after this frame's camera transform,
        // before the articulated arms pose. Shots and the crosshair share one ray.
        void LateUpdate()
        {
            if(ctx==null||!ctx.Ready||ctx.Paused)return;
            UpdateResidentGuns();
            AimTarget = null; if(!CanUse())return;
            var input=ctx.Input;
            if (Aiming)
            {
                var ray = ctx.Follow.AimRay; AimPoint = FirstHit(ray.origin, ray.direction, 80, out var hit) ? hit.point : ray.GetPoint(60);
                AimTarget = hit.collider ? ctx.Residents.FromCollider(hit.collider) : null;
                if (input.Fire || input.FireHeld && Definition(Equipped).Automatic) Fire();
            }
        }
        public void PoseWeapon()
        {
            if (ctx == null || !ctx.Ready || Equipped == null || !models.TryGetValue(Equipped, out var model) || !CanUse()) { playerHands?.Show(false); return; }
            var visual = Visual(Equipped);
            float progress = Reloading ? Mathf.InverseLerp(reloadStarted, reloadUntil, ctx.Clock) : 0;
            PoseFirearm(ctx.Actors.PlayerRig, visual, AimPoint, Aiming, Reloading, Recoil, progress);
            playerHands?.Show(true, Aiming || Reloading || Equipped == "carbine");
            visual?.Animate(ctx.Clock, Reloading, progress, Ammo(Equipped));
        }
        void OnDestroy() { if (ctx && ctx.Vehicles) ctx.Vehicles.Impact -= VehicleImpact; playerHands?.Dispose(); DisposeResidentGuns(); foreach (var visual in weaponVisuals.Values) visual.Dispose(); }
    }
}
