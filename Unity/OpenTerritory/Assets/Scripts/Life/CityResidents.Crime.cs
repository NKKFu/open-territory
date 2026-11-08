using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        sealed class StreetEncounter
        {
            public Resident Offender, Victim;
            public bool Robbery;
            public string Stage = "approach";
            public float Until;
            public Vector3 EscapeFrom;
        }
        StreetEncounter streetCrime;
        float nextStreetCrime = 60;
        public bool AmbientCrimeEnabled = true;
        public int StreetCrimesStarted { get; private set; }
        public bool IsStreetOffender(Resident p) => p != null && streetCrime?.Offender == p;
        public string StreetCrimeStage => streetCrime?.Stage ?? "none";
        bool StreetEligible(Resident p) => p != null && p.Root && p.Root.gameObject.activeInHierarchy && !p.Down && !p.Data.child &&
            p.Data.role != "police" && !p.Data.stationary && !p.Data.sitting && !p.Data.performance && !p.Controlled && !p.Calling &&
            p.Bus == null && !p.Crossing && p.Doorway == null && p.Health >= 99.99f && p.CrimeCooldown <= ctx.Clock &&
            p.FearUntil <= ctx.Clock && p.DodgeUntil <= ctx.Clock && p.TalkUntil <= ctx.Clock && p.Partner == null &&
            p.Reaction.Kind == ReactionKind.None && p.EmoteUntil <= ctx.Clock && string.IsNullOrEmpty(p.MotionMode);

        public bool CanRob(Resident p) => ctx && ctx.Ready && !ctx.Paused && ctx.Combat.CanUse() && ctx.Motor.Grounded &&
            !ctx.Remote.Active && p != null && p.Root && p.Root.gameObject.activeInHierarchy && !p.Down && !p.Data.child &&
            p.Data.role != "police" && !p.Controlled && !p.Calling && p.Bus == null && ctx.Combat.Aiming && ctx.Combat.AimTarget == p &&
            p.Reaction.Kind == ReactionKind.Surrender && ctx.Clock - p.Reaction.Started >= .35f && ctx.Clock >= p.RobbedUntil &&
            Vector3.Distance(ctx.Player.position, p.Root.position) < 3.4f && ctx.Visible(ctx.Player.position + Vector3.up * 1.5f, p.Root.position + Vector3.up * 1.5f);
        int Cash(Resident p) { if (p.PocketCash < 0) p.PocketCash = 8 + Mathf.Abs(p.Data.id % 29); return p.PocketCash; }
        public bool Rob(Resident p)
        {
            if (!CanRob(p)) return false;
            int taken = ctx.Commerce.ReceiveCash(Cash(p)); p.PocketCash -= taken;
            p.RobbedUntil = ctx.Clock + 120; p.CrimeCooldown = ctx.Clock + 150;
            ctx.Police.Report("roubo", p.Root.position, p, false, ctx.Player);
            ctx.Sound("shove", p.Root.position, .3f); return true;
        }
        // The same encounter entry point drives ambient life and explicit QA.
        // At most one spontaneous encounter runs; police can handle other crimes
        // at the same time without replacing this suspect with the player.
        public bool TryStartStreetCrime(Resident offender, Resident victim, bool robbery)
        {
            if (!ctx || !ctx.Ready || ctx.Paused || streetCrime != null || offender == victim || !StreetEligible(offender) || !StreetEligible(victim) || !string.IsNullOrEmpty(offender.Data.role)) return false;
            float distance = Vector3.Distance(offender.Root.position, victim.Root.position);
            if (distance > 5 || distance < .7f || !ClearWalk(offender.Root.position, victim.Root.position, offender) ||
                !ctx.Visible(offender.Root.position + Vector3.up * 1.5f, victim.Root.position + Vector3.up * 1.5f)) return false;
            streetCrime = new StreetEncounter { Offender = offender, Victim = victim, Robbery = robbery, Until = ctx.Clock + 7 };
            Control(offender, true); offender.CrimeCooldown = victim.CrimeCooldown = ctx.Clock + 150;
            StreetCrimesStarted++; nextStreetCrime = ctx.Clock + R(100, 170); return true;
        }
        internal void EndStreetCrime()
        {
            if (streetCrime == null) return;
            var p = streetCrime.Offender;
            if (p != null && !ctx.Police.IsDetained(p.Root)) { Control(p, false); p.Reaction.Kind = ReactionKind.None; p.FearUntil = 0; }
            streetCrime = null;
        }
        public void Detain(Resident p, Vector3 officer)
        {
            if (p == null) return;
            if (streetCrime?.Offender == p) EndStreetCrime();
            Control(p, true); p.MoveSpeed = 0; p.Velocity = Vector3.zero; p.CrimeCooldown = ctx.Clock + 180;
            BeginAlarm(p, officer, ReactionKind.Surrender, 6.2f, false);
        }
        public void ReleaseDetained(Resident p)
        {
            if (p == null) return;
            Control(p, false); RecoverReaction(p); p.CrimeCooldown = ctx.Clock + 180;
        }
        void TickStreetCrimes(float dt)
        {
            if (streetCrime == null)
            {
                if (!AmbientCrimeEnabled || ctx.Clock < nextStreetCrime || ctx.Combat.Down || ctx.Police.ActiveCases > 0) return;
                nextStreetCrime = ctx.Clock + R(12, 20);
                foreach (var offender in People)
                {
                    if (!StreetEligible(offender) || Mathf.Abs(offender.Data.id % 7) != 0 || Vector3.Distance(offender.Root.position, ctx.Player.position) > 45 || Inside(offender.Root.position) != null) continue;
                    foreach (var victim in People)
                    {
                        if (victim == offender || !StreetEligible(victim) || Inside(victim.Root.position) != null) continue;
                        if (TryStartStreetCrime(offender, victim, random.Next(2) == 0)) return;
                    }
                }
                return;
            }
            var crime = streetCrime; var p = crime.Offender; var victimOfCrime = crime.Victim;
            if (!p.Root || !p.Root.gameObject.activeInHierarchy || p.Down || ctx.Police.IsDetained(p.Root) || !victimOfCrime.Root || !victimOfCrime.Root.gameObject.activeInHierarchy)
            { EndStreetCrime(); return; }
            if (crime.Stage == "approach")
            {
                if (victimOfCrime.Down || victimOfCrime.Bus != null || victimOfCrime.Controlled || victimOfCrime.Calling || ctx.Clock >= crime.Until ||
                    Vector3.Distance(p.Root.position, victimOfCrime.Root.position) > 7) { EndStreetCrime(); return; }
                float distance = Vector3.Distance(p.Root.position, victimOfCrime.Root.position);
                if (distance > 1.25f) { MoveToward(p, victimOfCrime.Root.position, dt, 1.9f, true); return; }
                Face(p, victimOfCrime.Root.position, dt);
                if (crime.Robbery)
                {
                    ctx.Police.Report("roubo", victimOfCrime.Root.position, victimOfCrime, false, p.Root);
                    if (!victimOfCrime.Calling) BeginAlarm(victimOfCrime, p.Root.position, ReactionKind.Surrender, 2, false, "Take it. Just leave me alone.");
                    crime.Stage = "threat"; crime.Until = ctx.Clock + 1.2f;
                }
                else if (ctx.Combat.TryResidentStrike(p, victimOfCrime.Root, 18, true))
                { crime.Stage = "strike"; crime.Until = ctx.Clock + .85f; }
                return;
            }
            if (crime.Stage == "threat" || crime.Stage == "strike")
            {
                if (crime.Stage == "threat") { p.MoveSpeed = 0; Face(p, victimOfCrime.Root.position, dt); }
                if (ctx.Clock < crime.Until) return;
                if (crime.Robbery && !victimOfCrime.Down && Vector3.Distance(p.Root.position, victimOfCrime.Root.position) < 3)
                { p.PocketCash = Cash(p) + Cash(victimOfCrime); victimOfCrime.PocketCash = 0; victimOfCrime.RobbedUntil = ctx.Clock + 120; }
                crime.EscapeFrom = victimOfCrime.Root.position; crime.Stage = "flee"; crime.Until = ctx.Clock + 9;
                BeginAlarm(victimOfCrime, p.Root.position, ReactionKind.Flee, 8, false); return;
            }
            if (crime.Stage == "flee")
            {
                if (ctx.Clock >= crime.Until) { EndStreetCrime(); return; }
                p.Reaction.Source = crime.EscapeFrom;
                if (!ctx.Combat.MeleeActing(p.Root)) Retreat(p, 3.2f, 24, dt);
            }
        }
        void PoseStreetCrime(Resident p)
        {
            if (streetCrime?.Offender != p || streetCrime.Stage != "threat" || p.Rig == null || ctx.Combat.MeleeActing(p.Root)) return;
            float weight = Mathf.SmoothStep(0, 1, (ctx.Clock - streetCrime.Until + 1.2f) / .18f);
            CityActors.Reach(p.Rig, 1, p.Root.TransformPoint(new Vector3(.20f, 1.58f, .72f)), weight);
            CityActors.Reach(p.Rig, 0, p.Root.TransformPoint(new Vector3(-.24f, 1.52f, .30f)), weight * .8f);
            p.Rig.Chest.localRotation *= Quaternion.Euler(5 * weight, -8 * weight, 0);
        }
    }
}
