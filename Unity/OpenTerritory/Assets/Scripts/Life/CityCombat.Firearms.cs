using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityCombat
    {
        sealed class ResidentGun
        {
            public CityResidents.Resident Person;
            public CityWeaponVisual Visual;
            public CityWeaponHands Hands;
            public Transform Target;
            public float Until, ReadyAt, NextShot, RecoilUntil;
            public int Loaded = 8;
        }
        readonly Dictionary<Transform, ResidentGun> residentGuns = new Dictionary<Transform, ResidentGun>();
        public int ResidentShots { get; private set; }
        public int PlayerBulletHits { get; private set; }
        public int ResidentBulletHits { get; private set; }

        void InitializeResidentGuns()
        {
            foreach (var person in ctx.Residents.People) PrepareResidentGun(person);
            ctx.Residents.Spawned += PrepareResidentGun;
        }
        void PrepareResidentGun(CityResidents.Resident person)
        {
            if (person.Data.role != "police" || person.Rig == null || residentGuns.ContainsKey(person.Root)) return;
            // Prepare the complete model and hands at spawn, never on a shot.
            var hands = new CityWeaponHands(person.Rig);
            residentGuns[person.Root] = new ResidentGun { Person = person, Hands = hands, Visual = new CityWeaponVisual(Definition("sidearm"), person.Rig.Hands[1]) };
        }
        public bool ReceiveGunshot(Transform victim, CityWeaponDefinition weapon, RaycastHit hit, Vector3 source)
        {
            if (!victim || weapon == null || ctx.Paused || !Damage(victim, weapon.Damage, source)) return false;
            if (weapon.Stun > 0 && !IsDown(victim)) Damage(victim, 0, source, weapon.Stun);
            if (weapon.Damage > 0 && weapon.Stun <= 0)
            {
                GunEffects.Bleed(victim, hit.point, hit.normal, (hit.point - source).normalized, weapon.Damage);
                if (victim == ctx.Player) PlayerBulletHits++; else ResidentBulletHits++;
            }
            return true;
        }
        // Called only for an identified armed suspect. Losing sight, surrender,
        // downed actors and close cover all stop the shot before damage occurs.
        public bool TryResidentShot(CityResidents.Resident person, Transform target)
        {
            if (ctx.Paused || person == null || person.Down || !target || IsDown(target) ||
                !residentGuns.TryGetValue(person.Root, out var gun) || person.Bus != null || ctx.Police.IsDetained(target)) return false;
            Vector3 eye = person.Root.position + Vector3.up * (1.48f * person.Root.lossyScale.y);
            Vector3 goal = target.position + Vector3.up * (1.30f * target.lossyScale.y);
            float distance = Vector3.Distance(eye, goal);
            if (distance < 2.2f || distance > 24 || !ctx.Visible(eye, goal)) return false;
            if (ctx.Clock > gun.Until || gun.Target != target) gun.ReadyAt = ctx.Clock + 1.05f;
            gun.Target = target; gun.Until = ctx.Clock + .22f; gun.Visual.Root.gameObject.SetActive(true);
            if (ctx.Clock < gun.ReadyAt || ctx.Clock < gun.NextShot) return true;
            PoseResidentWeapon(person);
            var visual = gun.Visual; Vector3 from = visual.Muzzle.position;
            // A small deterministic spread leaves room to move between shots.
            float phase = ResidentShots * 2.39996f + person.Data.id;
            goal += person.Root.right * (Mathf.Sin(phase) * .10f) + Vector3.up * (Mathf.Cos(phase) * .06f);
            RaycastHit hit;
            bool contact = FirstHit(eye, (from - eye).normalized, Vector3.Distance(eye, from), out hit, person.Root);
            bool closeCover = contact;
            if (!contact)
            {
                int count = Physics.OverlapSphereNonAlloc(from, .025f, overlaps, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (overlaps[i] && Vector3.Distance(overlaps[i].ClosestPoint(from), from) < .002f) return true;
                contact = FirstHit(from, (goal - from).normalized, 26, out hit, person.Root);
            }
            Vector3 impact = contact ? hit.point : from + (goal - from).normalized * 26;
            Transform victim = null;
            if (contact && hit.collider)
            {
                var resident = ctx.Residents.FromCollider(hit.collider);
                victim = hit.transform.IsChildOf(ctx.Player) ? ctx.Player : resident?.Root;
                if (victim) ReceiveGunshot(victim, Definition("sidearm"), hit, from);
                else ctx.Systems.Hit(hit.collider, (impact - from).normalized * 3, 36, impact, CityPropDamageKind.Bullet);
            }
            Vector3 muzzle = closeCover ? hit.point + hit.normal * .02f : from;
            ctx.Sound("shot", muzzle, .92f); visual.Kick(ctx.Clock);
            GunEffects.Shoot("sidearm", visual, muzzle, impact, contact ? hit : default, victim);
            ResidentShots++; gun.RecoilUntil = ctx.Clock + .14f;
            gun.Loaded--;
            gun.NextShot = ctx.Clock + (gun.Loaded > 0 ? .86f : 2.05f);
            if (gun.Loaded == 0) { gun.Loaded = 8; ctx.Sound("reload-sidearm", person.Root.position, .5f); }
            return true;
        }
        void UpdateResidentGuns()
        {
            foreach (var gun in residentGuns.Values)
            {
                bool active = gun.Person.Root && gun.Person.Root.gameObject.activeInHierarchy && !gun.Person.Down &&
                    gun.Target && !IsDown(gun.Target) && ctx.Clock <= gun.Until && !ctx.Police.IsDetained(gun.Target);
                gun.Visual.Root.gameObject.SetActive(active); gun.Hands.Show(active);
            }
        }
        public void PoseResidentWeapon(CityResidents.Resident person)
        {
            if (!residentGuns.TryGetValue(person.Root, out var gun) || !gun.Visual.Root.gameObject.activeSelf || !gun.Target || person.Down) return;
            float recoil = Mathf.Clamp01((gun.RecoilUntil - ctx.Clock) / .14f);
            PoseFirearm(person.Rig, gun.Visual, gun.Target.position + Vector3.up * (1.30f * gun.Target.lossyScale.y), true, false, recoil, 0);
            gun.Hands.Show(true); gun.Visual.Animate(ctx.Clock, false, 0, gun.Loaded);
        }
        void PoseFirearm(CityRig rig, CityWeaponVisual visual, Vector3 target, bool aiming, bool reloading, float recoil, float progress)
        {
            if (rig == null || visual == null) return;
            bool carbine = visual == Visual("carbine");
            var root = rig.Root; float scale = root.lossyScale.y;
            Vector3 shoulders = (rig.Arms[0].position + rig.Arms[1].position) * .5f;
            Vector3 direction = target - shoulders;
            if (direction.sqrMagnitude < .001f) direction = root.forward;
            direction.Normalize();
            Quaternion rotation;
            Vector3 palm;
            if (aiming && !reloading)
            {
                rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(-recoil * (carbine ? 2.2f : 4), 0, recoil * .8f);
                palm = shoulders + rotation * new Vector3(carbine ? .075f : .11f, -.115f, (carbine ? .31f : .47f) - recoil * .045f) * scale;
            }
            else
            {
                rotation = root.rotation * Quaternion.Euler(reloading ? 24 : carbine ? 24 : 53, carbine ? -15 : -7, reloading ? -25 : -5);
                palm = root.TransformPoint(reloading ? new Vector3(.10f, 1.39f, .35f) : carbine ? new Vector3(.18f, 1.36f, .24f) : new Vector3(.30f, 1.12f, .23f));
            }
            CityActors.Reach(rig, 1, palm);
            rig.Hands[1].rotation = rotation;
            visual.Root.rotation = rotation;
            visual.Root.position = rig.Hands[1].position - visual.Root.TransformVector(visual.RightGrip);
            if (aiming || reloading || carbine)
            {
                Vector3 support = visual.Root.TransformPoint(visual.SupportGrip);
                if (reloading)
                {
                    float change = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .30f, progress)) *
                        (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.64f, .85f, progress)));
                    support = Vector3.Lerp(support, visual.Magazine.position - visual.Root.up * (.075f + .15f * change), .9f);
                }
                CityActors.Reach(rig, 0, support);
                rig.Hands[0].rotation = rotation * (reloading ? Quaternion.Euler(0, 0, 20) : visual.SupportRotation);
            }
        }
        void DisposeResidentGuns()
        {
            if (ctx && ctx.Residents) ctx.Residents.Spawned -= PrepareResidentGun;
            foreach (var gun in residentGuns.Values) { gun.Hands.Dispose(); gun.Visual.Dispose(); }
            residentGuns.Clear();
        }
    }
}
