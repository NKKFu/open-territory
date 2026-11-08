using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityCombat
    {
        public const float MeleeReach = 1.18f;
        sealed class Strike
        {
            public float Start = -100, End = -100, Contact;
            public int Step;
            public Vector3 Forward;
            public bool Resolved;
        }
        sealed class MeleeReaction
        {
            public CityResidents.Resident Person;
            public Transform Target;
            public float Damage = 15;
            public float AttackedPlayerUntil;
            public bool Manual, ReportCrime;
            public float HitAt = -100, ProvokedUntil, NextReply;
            public Vector3 Push;
            public readonly Strike Reply = new Strike();
        }
        readonly Strike punch = new Strike();
        readonly Collider[] meleeCandidates = new Collider[48];
        readonly Dictionary<Transform, MeleeReaction> meleeReactions = new Dictionary<Transform, MeleeReaction>();
        bool queuedPunch, guardToggle;
        float comboUntil, nextPunch, guardImpact = -100;
        Transform chainVictim;
        int chainHits;
        public int MeleeSwings { get; private set; }
        public int MeleeHits { get; private set; }
        public int BlockedMeleeHits { get; private set; }
        public int CounterSwings { get; private set; }
        public int ComboStep => punch.Step + 1;
        public bool Punching => ctx && ctx.Clock < punch.End;
        public bool Guarding { get; private set; }
        public bool Fighting => Punching || Guarding;
        public bool MeleeActing(Transform root) => meleeReactions.TryGetValue(root, out var reaction) &&
            (ctx.Clock - reaction.HitAt < .42f || ctx.Clock < reaction.Reply.End);
        public bool DefendingAgainst(Transform root) => root && !IsDown(root) && !ctx.Police.IsDetained(root) &&
            meleeReactions.TryGetValue(root, out var reaction) && reaction.Person.Data.role != "police" && ctx.Clock < reaction.AttackedPlayerUntil;
        internal bool NeedsSelfDefence(CityResidents.Resident person) => person != null && meleeReactions.TryGetValue(person.Root, out var reaction) &&
            !reaction.Manual && ctx.Clock < reaction.ProvokedUntil && CanReply(reaction);
        public Vector3 MeleeForward => Punching ? punch.Forward : CameraForward();
        public bool CanMelee => ctx && ctx.Ready && !ctx.Paused && !ctx.Input.Pause && Equipped == null && CanUse() &&
            ctx.Motor.Grounded && (!ctx.Emotes || !ctx.Emotes.WheelOpen) && !(ctx.UI && ctx.UI.MapOpen) && !ctx.Remote.Active && !ctx.Remote.Scanning;

        Vector3 CameraForward()
        {
            Vector3 direction = Vector3.ProjectOnPlane(ctx.Camera.transform.forward, Vector3.up);
            return direction.sqrMagnitude > .001f ? direction.normalized : ctx.Player.forward;
        }
        public void ReleaseMeleeInput() { queuedPunch = guardToggle = Guarding = false; }
        void InterruptPunch() { punch.End = -100; queuedPunch = false; chainVictim = null; chainHits = 0; comboUntil = 0; }
        public void CancelMelee() { InterruptPunch(); ReleaseMeleeInput(); }

        // One press starts one strike. A second press during recovery can buffer
        // exactly one successor; holding a mouse/touch button never repeats it.
        public bool TryPunch()
        {
            if (!CanMelee) return false;
            if (Punching)
            {
                if (queuedPunch || punch.Step == 2 || ctx.Clock < punch.Contact + .025f) return false;
                queuedPunch = true; return true;
            }
            if (ctx.Clock < nextPunch) return false;
            int step = ctx.Clock < comboUntil && punch.Step < 2 ? punch.Step + 1 : 0;
            BeginPunch(step); return true;
        }
        void BeginPunch(int step)
        {
            Vector3 forward = CameraForward(), origin = ctx.Player.position + Vector3.up * 1.45f;
            float best = float.MaxValue;
            int count = Physics.OverlapSphereNonAlloc(origin, 1.85f, meleeCandidates, 1 << CityContext.ActorLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var person = ctx.Residents.FromCollider(meleeCandidates[i]);
                if (person == null || person.Down || person.Bus != null) continue;
                Vector3 delta = Vector3.ProjectOnPlane(person.Root.position - ctx.Player.position, Vector3.up);
                if (delta.sqrMagnitude < .01f || Vector3.Dot(delta.normalized, forward) < .82f) continue;
                float score = delta.magnitude + (1 - Vector3.Dot(delta.normalized, forward)) * 3;
                // Assistance only turns toward a person the actual fist volume can reach.
                // It never snaps to an actor behind cover or outside the forward cone.
                if (score >= best || !MeleeContact(ctx.Player, delta.normalized, out var collider, out _) || ctx.Residents.FromCollider(collider) != person) continue;
                best = score; punch.Forward = delta.normalized;
            }
            forward = best < float.MaxValue ? punch.Forward : forward;
            punch.Step = step; punch.Start = ctx.Clock; punch.End = ctx.Clock + (step == 2 ? .62f : .43f);
            punch.Contact = ctx.Clock + (step == 2 ? .23f : .16f); punch.Resolved = false; punch.Forward = forward;
            comboUntil = punch.End + .30f; nextPunch = punch.End + (step == 2 ? .22f : 0);
            if (step == 0) { chainVictim = null; chainHits = 0; }
            queuedPunch = Guarding = false; MeleeSwings++;
            ctx.Player.rotation = Quaternion.LookRotation(forward);
            ctx.Emotes.CancelPerformance(); ctx.Actors.CancelInteraction();
            ctx.Sound("shove", origin, .24f);
        }
        void UpdateMelee()
        {
            if (!CanMelee) { CancelMelee(); return; }
            var input = ctx.Input;
            if (input.AimToggle) guardToggle = !guardToggle;
            if (!punch.Resolved && ctx.Clock >= punch.Contact && punch.End > -1)
            {
                punch.Resolved = true;
                if (MeleeContact(ctx.Player, punch.Forward, out var collider, out var point))
                {
                    LastImpact = point;
                    var person = ctx.Residents.FromCollider(collider);
                    if (person != null && !person.Down && person.Bus == null && Damage(person.Root, punch.Step == 2 ? 34 : punch.Step == 1 ? 28 : 24, ctx.Player.position))
                    {
                        MeleeHits++;
                        if (person.Root == chainVictim) chainHits++; else { chainVictim = person.Root; chainHits = 1; }
                        Provoke(person);
                        var reaction = meleeReactions[person.Root]; reaction.HitAt = ctx.Clock; reaction.Push = punch.Forward;
                        if (punch.Step == 2 && chainHits >= 3 && !IsDown(person.Root)) Fall(State(person.Root), 2.4f, ctx.Player.position);
                        ctx.Police.Report("agressao", person.Root.position, person, false);
                        ctx.Sound("hurt", point, .55f);
                    }
                    else
                    {
                        chainHits = 0; chainVictim = null;
                        if(person==null)ctx.Systems.Hit(collider,punch.Forward*3.2f,punch.Step==2?38:28,point,CityPropDamageKind.Melee);
                    }
                    ctx.Sound(person != null ? "shove" : "impact", point, .65f);
                }
                else { chainHits = 0; chainVictim = null; }
            }
            if (!Punching && queuedPunch)
            {
                int step = punch.Step + 1; queuedPunch = false;
                if (step <= 2) BeginPunch(step);
            }
            Guarding = !Punching && (input.AimHeld || guardToggle);
            if (Guarding) ctx.Emotes.CancelPerformance();
            if (input.Fire || input.MeleePressed) TryPunch();
        }
        bool MeleeContact(Transform attacker, Vector3 forward, out Collider collider, out Vector3 point)
        {
            float scale = attacker.lossyScale.y;
            Vector3 origin = attacker.position + Vector3.up * (1.45f * scale);
            // Looking down allows a real descending strike against low props.
            // The same sphere still stops at the first wall or solid surface.
            if(attacker==ctx.Player&&ctx.Camera.transform.forward.y<-.45f)
                forward=(forward+Vector3.down*Mathf.Clamp(-ctx.Camera.transform.forward.y*1.3f,0,1.3f)).normalized;
            float radius = .18f * scale, nearest = float.MaxValue;
            collider = null; point = origin + forward * MeleeReach * scale;
            int mask = ctx.AllMask | 1 << CityContext.PlayerLayer;
            // Sphere casts do not report the colliders containing their origin.
            // A wall already touching the chest must still stop the punch.
            int count = Physics.OverlapSphereNonAlloc(origin, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var candidate = overlaps[i]; if (!candidate || candidate.transform.IsChildOf(attacker)) continue;
                bool solid = (ctx.SolidMask & 1 << candidate.gameObject.layer) != 0;
                if (collider && !solid) continue;
                collider = candidate; point = candidate.ClosestPoint(origin); nearest = 0;
                if (solid) return true;
            }
            count = Physics.SphereCastNonAlloc(origin, radius, forward, hits, MeleeReach * scale, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.transform.IsChildOf(attacker) || hit.distance >= nearest) continue;
                nearest = hit.distance; collider = hit.collider; point = hit.point;
            }
            return collider;
        }
        void Provoke(CityResidents.Resident person)
        {
            ctx.Police.SuspendVictimCall(person);
            if (!meleeReactions.TryGetValue(person.Root, out var reaction))
            { reaction = new MeleeReaction { Person = person }; meleeReactions.Add(person.Root, reaction); }
            reaction.ProvokedUntil = ctx.Clock + 4;
            reaction.Target = ctx.Player; reaction.Damage = 15; reaction.Manual = reaction.ReportCrime = false;
            reaction.NextReply = ctx.Clock + .48f;
            reaction.Reply.End = -100;
        }
        void CancelMeleeReplies()
        { foreach (var reaction in meleeReactions.Values) if (reaction.Target == ctx.Player) reaction.Reply.End = -100; }
        public bool TryResidentStrike(CityResidents.Resident person, Transform target, float damage = 15, bool reportCrime = true)
        {
            if (person == null || !target || !ctx || ctx.Paused || person.Root == target || person.Down || person.Calling || person.Bus != null ||
                ctx.Police.IsDetained(person.Root) || !person.Root.gameObject.activeInHierarchy || IsDown(target) || ctx.Police.IsDetained(target) ||
                !target.gameObject.activeInHierarchy || CityPhysics.Flat(target.position - person.Root.position).sqrMagnitude < .01f || Vector3.Distance(person.Root.position, target.position) > 1.85f ||
                !ctx.Visible(person.Root.position + Vector3.up * 1.45f, target.position + Vector3.up * 1.45f)) return false;
            if (!meleeReactions.TryGetValue(person.Root, out var reaction))
            { reaction = new MeleeReaction { Person = person }; meleeReactions.Add(person.Root, reaction); }
            if (ctx.Clock < reaction.Reply.End || ctx.Clock < reaction.NextReply) return false;
            reaction.Target = target; reaction.Damage = Mathf.Clamp(damage, 1, 35); reaction.Manual = true; reaction.ReportCrime = reportCrime;
            StartResidentStrike(reaction); return true;
        }
        void StartResidentStrike(MeleeReaction reaction)
        {
            var strike = reaction.Reply;
            strike.Start = ctx.Clock; strike.Contact = ctx.Clock + .32f; strike.End = ctx.Clock + .72f; strike.Step = 1;
            strike.Forward = CityPhysics.Flat(reaction.Target.position - reaction.Person.Root.position).normalized; strike.Resolved = false;
            reaction.NextReply = strike.End + 1.1f; CounterSwings++;
        }
        bool CanReply(MeleeReaction reaction)
        {
            var p = reaction.Person;
            var target = reaction.Target;
            return target && target.gameObject.activeInHierarchy && !IsDown(target) && !ctx.Police.IsDetained(target) &&
                p.Root && p.Root.gameObject.activeInHierarchy && !p.Down && !p.Calling && !ctx.Police.IsDetained(p.Root) && !p.Data.child && !p.Data.sitting &&
                !p.Crossing && p.Bus == null && (target != ctx.Player || CanUse()) && (reaction.Manual || ctx.Clock < reaction.ProvokedUntil) &&
                Vector3.Distance(p.Root.position, target.position) < 1.85f &&
                ctx.Visible(p.Root.position + Vector3.up * 1.45f, target.position + Vector3.up * 1.45f);
        }
        void UpdateMeleeReplies()
        {
            bool another = false;
            foreach (var reaction in meleeReactions.Values) if (ctx.Clock < reaction.Reply.End) another = true;
            foreach (var reaction in meleeReactions.Values)
            {
                var strike = reaction.Reply; var p = reaction.Person;
                if (ctx.Clock < strike.End)
                {
                    if (!CanReply(reaction)) { strike.End = -100; continue; }
                    if (!strike.Resolved && ctx.Clock >= strike.Contact)
                    {
                        strike.Resolved = true;
                        if (MeleeContact(p.Root, strike.Forward, out var collider, out var contact) && collider.transform.IsChildOf(reaction.Target))
                        {
                            bool hit = reaction.Target == ctx.Player ? ReceiveMelee(p.Root.position, reaction.Damage) : Damage(reaction.Target, reaction.Damage, p.Root.position);
                            if (hit)
                            {
                                if (reaction.ReportCrime && reaction.Target == ctx.Player) reaction.AttackedPlayerUntil = ctx.Clock + 5;
                                ctx.Sound("shove", contact, .7f);
                                if (reaction.ReportCrime) ctx.Police.Report("agressao", reaction.Target.position, ctx.Residents.Find(reaction.Target), false, p.Root);
                            }
                        }
                    }
                    continue;
                }
                if (reaction.Manual || another || ctx.Clock < reaction.NextReply || !CanReply(reaction)) continue;
                StartResidentStrike(reaction); another = true;
            }
        }
        bool ReceiveMelee(Vector3 source, float damage)
        {
            Vector3 toward = Vector3.ProjectOnPlane(source - ctx.Player.position, Vector3.up).normalized;
            bool blocked = Guarding && CanMelee && Vector3.Dot(ctx.Player.forward, toward) > .45f;
            if (!Damage(ctx.Player, blocked ? damage * .2f : damage, source)) return false;
            if (blocked) { BlockedMeleeHits++; guardImpact = ctx.Clock; }
            ctx.Sound("shove", ctx.Player.position + Vector3.up * 1.45f, blocked ? .5f : .8f); return true;
        }
        public bool TickMeleeReaction(CityResidents.Resident person, float dt)
        {
            if (!meleeReactions.TryGetValue(person.Root, out var reaction) || person.Down || person.Bus != null) return false;
            float age = ctx.Clock - reaction.HitAt;
            bool stagger = age < .42f, counter = ctx.Clock < reaction.Reply.End;
            if (!stagger && !counter) return false;
            if (person.Controller && person.Controller.enabled)
                person.Controller.Move((stagger ? reaction.Push * (1.2f * Mathf.Clamp01(1 - age / .23f)) : Vector3.zero) * dt + Vector3.down * (2 * dt));
            person.Velocity = Vector3.zero; person.MoveSpeed = 0;
            if (counter) person.Root.rotation = Quaternion.RotateTowards(person.Root.rotation, Quaternion.LookRotation(reaction.Reply.Forward), 720 * dt);
            return true;
        }
        public void PoseMelee(CityRig rig)
        {
            if (rig == null || !rig.Valid) return;
            if (rig == ctx.Actors.PlayerRig)
            {
                if (Punching) PoseStrike(rig, punch);
                else if (Guarding)
                {
                    float flinch = Mathf.Clamp01(1 - (ctx.Clock - guardImpact) / .20f);
                    rig.Chest.localRotation *= Quaternion.Euler(-5 - flinch * 8, -12, 0);
                    for (int i = 0; i < 2; i++) CityActors.Reach(rig, i, rig.Root.TransformPoint(new Vector3(i == 0 ? -.22f : .22f, 1.83f, .31f - flinch * .05f)));
                }
                return;
            }
            if (!meleeReactions.TryGetValue(rig.Root, out var reaction)) return;
            float age = ctx.Clock - reaction.HitAt;
            if (age < .42f)
            {
                float recoil = Mathf.Sin(Mathf.Clamp01(age / .42f) * Mathf.PI);
                Vector3 local = rig.Root.InverseTransformDirection(reaction.Push);
                rig.Chest.localRotation *= Quaternion.Euler(-local.z * recoil * 23, recoil * 12, local.x * recoil * 18);
                rig.Head.localRotation *= Quaternion.Euler(-recoil * 16, 0, recoil * 6);
                for (int i = 0; i < 2; i++) CityActors.Reach(rig, i, rig.Root.TransformPoint(new Vector3(i == 0 ? -.22f : .22f, 1.60f, .34f)), recoil);
            }
            else if (ctx.Clock < reaction.Reply.End) PoseStrike(rig, reaction.Reply);
        }
        void PoseStrike(CityRig rig, Strike strike)
        {
            float age = ctx.Clock - strike.Start, contact = strike.Contact - strike.Start, duration = strike.End - strike.Start;
            float wind = Mathf.SmoothStep(0, 1, age / Mathf.Max(.01f, contact - .07f));
            float extension = age < contact ? Mathf.SmoothStep(0, 1, (age - contact + .10f) / .10f) : 1 - Mathf.SmoothStep(0, 1, (age - contact) / .21f);
            float blend = Mathf.SmoothStep(0, 1, age / .065f) * (1 - Mathf.SmoothStep(0, 1, (age - duration + .13f) / .13f));
            int hand = strike.Step == 1 ? 1 : 0; float side = hand == 0 ? -1 : 1;
            float twist = side * (wind * 15 - extension * (strike.Step == 2 ? 48 : 34));
            rig.Hips.localRotation *= Quaternion.Euler(0, twist * .35f * blend, 0);
            rig.Chest.localRotation *= Quaternion.Euler(extension * 6, twist * blend, side * extension * 4);
            for (int i = 0; i < 2; i++)
            {
                Vector3 target = new Vector3(i == 0 ? -.22f : .22f, 1.72f, .30f);
                if (i == hand) target = Vector3.Lerp(new Vector3(side * .28f, 1.62f, .20f),
                    new Vector3(strike.Step == 2 ? -side * .12f : side * .08f, strike.Step == 2 ? 1.65f : 1.62f, .91f), extension);
                if(i==hand&&rig==ctx.Actors.PlayerRig&&ctx.Camera.transform.forward.y<-.45f)
                    target.y-=extension*Mathf.Clamp(-ctx.Camera.transform.forward.y,0,1)*.75f;
                CityActors.Reach(rig, i, rig.Root.TransformPoint(target), blend);
            }
        }
    }
}
