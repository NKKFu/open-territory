using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        public enum ReactionKind { None, Talking, Cautious, Surrender, Flee, Recover }
        public sealed class PlayerReaction
        {
            public ReactionKind Kind;
            public Vector3 Source;
            public float Until, Started, MemoryUntil, NextScan, NextLine, NextReport, PoseWeight;
            public int Conversations;
            public bool WeaponConcern;
        }
        float nextReactionLine;
        static readonly float[] retreatAngles = { 0, 40, -40, 75, -75, 110, -110 };

        bool UnsafeReaction(Resident p) => p.Reaction.Kind == ReactionKind.Cautious || p.Reaction.Kind == ReactionKind.Surrender || p.Reaction.Kind == ReactionKind.Flee;
        void ResetPlayerReaction(Resident p)
        {
            var r = p.Reaction; r.Kind = ReactionKind.None; r.Until = r.PoseWeight = 0;
            r.NextScan = ctx.Clock + Mathf.Abs(p.Data.id % 7) * .035f; r.WeaponConcern = false;
            p.TalkUntil = p.FearUntil = 0;
        }
        void EndConversation(Resident p)
        {
            ctx.Systems.Audio?.StopNpcVoice(p);
            p.TalkUntil = 0;
            if (p.Reaction.Kind == ReactionKind.Talking) p.Reaction.Kind = ReactionKind.None;
        }
        public void BeginConversation(Resident p, string line, float duration = 4.5f)
        {
            if (p == null || p.Down || ctx.Paused || ctx.Combat.WeaponVisible || !CanTalk(p.Root)) return;
            Interrupt(p); var r = p.Reaction;
            if (r.MemoryUntil > ctx.Clock) line = "Ainda estou assustado com o que você fez. Preciso de um tempo.";
            else if (r.Conversations > 0) line = "Oi de novo! " + line;
            r.Conversations++; r.Kind = ReactionKind.Talking; r.Started = ctx.Clock;
            r.Until = p.TalkUntil = ctx.Clock + duration; r.Source = ctx.Player.position;
            p.MoveSpeed = 0; p.LookYaw = 0; p.NextSocial = r.Until + 5;
            p.NextMurmur = ctx.Clock; TickConversationVoice(p);
            ctx.Say(p.Data.name + " · " + line, p.Root.position + Vector3.up * 2.55f, duration);
        }
        internal bool Notices(Resident p, Vector3 point, float radius, bool alerted = false)
        {
            if (!p.Root || !p.Root.gameObject.activeInHierarchy || p.Down || p.Bus != null) return false;
            Vector3 direction = point - p.Root.position; float distance = direction.magnitude;
            if (ctx.Systems) radius *= ctx.Systems.HackVisibility(point);
            if (distance > radius || !alerted && distance > 3.5f && Vector3.Dot(p.Root.forward, direction.normalized) < .05f) return false;
            return ctx.Visible(p.Root.position + Vector3.up * (1.75f * p.Root.lossyScale.y), point + Vector3.up * 1.45f);
        }
        void ReactionLine(Resident p, string line)
        {
            if (string.IsNullOrEmpty(line) || ctx.Clock < nextReactionLine || ctx.Clock < p.Reaction.NextLine ||
                Vector3.Distance(p.Root.position, ctx.Player.position) > 20 || ctx.Police.Calling) return;
            nextReactionLine = ctx.Clock + 1.8f; p.Reaction.NextLine = ctx.Clock + 8;
            ctx.Say(p.Data.name + " · " + line, p.Root.position + Vector3.up * 2.55f, 3);
        }
        void BeginAlarm(Resident p, Vector3 source, ReactionKind kind, float duration, bool remember, string line = null)
        {
            if (p == null || p.Down || p.Bus != null) return;
            var r = p.Reaction; bool changed = r.Kind != kind;
            if (kind == ReactionKind.Cautious && (r.Kind == ReactionKind.Flee || r.Kind == ReactionKind.Surrender))
            { if (remember) r.MemoryUntil = ctx.Clock + 45; return; }
            if (changed || p.TalkUntil > ctx.Clock || p.Partner != null || p.Emote != CityEmote.None) Interrupt(p);
            p.TalkUntil = 0; p.Threat = r.Source = source;
            if (changed) { r.Started = ctx.Clock; p.Path.Clear(); p.PathIndex = 0; p.MoveSpeed = 0; }
            r.Kind = kind; r.Until = changed ? ctx.Clock + duration : Mathf.Max(r.Until, ctx.Clock + duration);
            p.NextSocial = r.Until + 8; p.PauseUntil = 0;
            if (kind == ReactionKind.Flee || kind == ReactionKind.Surrender) p.FearUntil = Mathf.Max(p.FearUntil, r.Until);
            if (remember) r.MemoryUntil = ctx.Clock + 45;
            if (changed) { ctx.Systems.Audio?.NpcVocal(p, "gasp", kind == ReactionKind.Cautious ? .65f : 1); ReactionLine(p, line); }
        }
        void RecoverReaction(Resident p)
        {
            var r = p.Reaction; r.Kind = ReactionKind.Recover; r.Started = ctx.Clock; r.Until = ctx.Clock + 1.6f;
            r.WeaponConcern = false; p.FearUntil = ctx.Clock; p.MoveSpeed = 0;
        }
        // Every witness reacts, while CityPolice still chooses just one caller.
        // Hearing a nearby noise never identifies a player hidden behind a wall.
        public void ReactToCrime(string type, Vector3 source, Resident victim, bool loud, Transform offender = null)
        {
            if (!offender) offender = ctx.Player;
            bool violent = type == "disparo" || type == "agressao" || type == "atropelamento" || type == "ameaca" || type == "roubo";
            foreach (var p in People)
            {
                if (!p.Root || !p.Root.gameObject.activeInHierarchy || p.Root == offender || p.Down || p.Bus != null || p.Calling || p.Data.role == "police") continue;
                bool saw = p == victim || Notices(p, source, 22, loud);
                bool heard = loud && Vector3.Distance(p.Root.position, source) < (type == "disparo" ? 27 : 16) &&
                    (ctx.Visible(p.Root.position + Vector3.up * 1.7f, source + Vector3.up * 1.4f) || Vector3.Distance(p.Root.position, source) < 7);
                if (!saw && !heard) continue;
                bool knows = offender == ctx.Player && saw && (p == victim || Notices(p, offender.position, 26, true));
                string line = !saw ? "Que barulho foi esse? Vou sair daqui!" : type == "disparo" ? "Tiros! Saiam daqui!" :
                    type == "atropelamento" ? "Cuidado! Atropelaram uma pessoa!" : type == "agressao" ? "Pare! Alguém precisa de ajuda!" :
                    type == "dano" ? "Ei! Cuidado com o que é dos outros!" : type == "ameaca" ? "Abaixe essa arma!" : type == "roubo" ? "Help! A robbery!" : "Ei! Esse veículo não é seu!";
                BeginAlarm(p, source, violent || heard && !saw ? ReactionKind.Flee : ReactionKind.Cautious, violent ? 6.5f : 4, knows, line);
                p.Reaction.WeaponConcern = false;
            }
        }
        void ObserveWeapon(Resident p)
        {
            var r = p.Reaction;
            if (!ctx.Combat.WeaponVisible) return;
            float radius = ctx.Combat.Equipped == "carbine" ? 16 : ctx.Combat.Equipped == "pulse" ? 8 : 12;
            if (!Notices(p, ctx.Player.position, radius, UnsafeReaction(p) || p.TalkUntil > ctx.Clock)) return;
            bool targeted = ctx.Combat.Aiming && ctx.Combat.AimTarget == p;
            if (p.Data.role == "police")
            {
                if (targeted || ctx.Combat.Aiming && ctx.Combat.AimTarget != null && Notices(p, ctx.Combat.AimTarget.Root.position, 22, true))
                {
                    if (ctx.Clock >= r.NextReport) { r.NextReport = ctx.Clock + 10; ctx.Police.Report("ameaca", ctx.Player.position); }
                    return;
                }
                if (!UnsafeReaction(p)) BeginAlarm(p, ctx.Player.position, ReactionKind.Cautious, 2, false, "Guarde a arma e mantenha as mãos à vista.");
            }
            else if (targeted)
            {
                bool running = r.Kind == ReactionKind.Flee || r.Kind == ReactionKind.Surrender && ctx.Clock - r.Started > 1.3f && Mathf.Abs(p.Data.id % 3) != 0 && !p.Data.stationary && !p.Data.sitting;
                BeginAlarm(p, ctx.Player.position, running ? ReactionKind.Flee : ReactionKind.Surrender, 4, true, "Calma! Não atire! Vou levantar as mãos!");
            }
            else if (r.Kind != ReactionKind.Flee && r.Kind != ReactionKind.Surrender)
                BeginAlarm(p, ctx.Player.position, ReactionKind.Cautious, 2, false, "Guarde essa arma antes de falar comigo.");
            r.WeaponConcern = true;
            if (r.Kind == ReactionKind.Cautious) { r.Source = ctx.Player.position; r.Until = ctx.Clock + 2; }
        }
        bool Retreat(Resident p, float speed, float separation, float dt)
        {
            if (p.Data.stationary || p.Data.sitting || p.Data.performance || !p.Controller || !p.Controller.enabled) return false;
            Vector3 away = CityPhysics.Flat(p.Root.position - p.Reaction.Source);
            if (away.magnitude >= separation) return false;
            if (away.sqrMagnitude < .01f) away = -p.Root.forward;
            foreach (float angle in retreatAngles)
            {
                Vector3 goal = p.Root.position + Quaternion.Euler(0, angle, 0) * away.normalized * 2.8f;
                goal.y = ctx.Ground(goal.x, goal.z) + .04f;
                if (goal.y < .15f || Mathf.Abs(goal.y - p.Root.position.y) > .8f || !ClearWalk(p.Root.position, goal, p) || !TrafficClear(p.Root.position, goal)) continue;
                // The whole retreat segment was checked for traffic above;
                // fleeing can accelerate promptly without bypassing that check.
                MoveToward(p, goal, dt, speed, speed > 1.5f); return true;
            }
            p.MoveSpeed = 0; return false;
        }
        bool TickPlayerReaction(Resident p, float dt)
        {
            if (p.Bus != null || p.Calling || p.Controlled && p.Data.role != "driver" || !string.IsNullOrEmpty(p.MotionMode)) return false;
            var r = p.Reaction;
            if (ctx.Clock >= r.NextScan) { r.NextScan = ctx.Clock + .22f; ObserveWeapon(p); }
            if (p.Controlled && p.Data.role != "driver") return false;
            if (p.DodgeUntil > ctx.Clock && r.Kind != ReactionKind.Talking) return false;
            if (r.Kind == ReactionKind.None && p.FearUntil > ctx.Clock) BeginAlarm(p, p.Threat, ReactionKind.Flee, p.FearUntil - ctx.Clock, false);
            if (r.Kind == ReactionKind.Talking)
            {
                if (ctx.Clock >= p.TalkUntil || Vector3.Distance(p.Root.position, ctx.Player.position) > 4.5f ||
                    ctx.Motor.Riding || ctx.Combat.Down || !ctx.Visible(p.Root.position + Vector3.up * 1.6f, ctx.Player.position + Vector3.up * 1.5f))
                { EndConversation(p); return false; }
                TickConversationVoice(p);
                r.Source = ctx.Player.position; p.MoveSpeed = 0; Face(p, r.Source, dt); return true;
            }
            if (r.Kind == ReactionKind.None) return false;
            if (r.WeaponConcern && !ctx.Combat.WeaponVisible && (r.Kind == ReactionKind.Cautious || r.Kind == ReactionKind.Surrender)) RecoverReaction(p);
            if (ctx.Clock >= r.Until)
            {
                if (r.Kind != ReactionKind.Recover) { RecoverReaction(p); return true; }
                r.Kind = ReactionKind.None; p.Path.Clear(); p.PathIndex = 0; p.NextLook = ctx.Clock + 2; return false;
            }
            if (r.Kind == ReactionKind.Flee) { if (!Retreat(p, 3.6f * p.Pace, 12, dt)) Face(p, r.Source, dt); }
            else if (r.Kind == ReactionKind.Cautious) { Retreat(p, .85f, 3.4f, dt); Face(p, r.Source, dt); }
            else { p.MoveSpeed = 0; Face(p, r.Source, dt); }
            return true;
        }
        void PosePlayerReaction(Resident p, float dt)
        {
            var r = p.Reaction; var rig = p.Rig;
            bool active = r.Kind != ReactionKind.None && !p.Calling && (!p.Controlled || p.Data.role == "driver" || ctx.Police.IsDetained(p.Root)) && string.IsNullOrEmpty(p.MotionMode);
            r.PoseWeight = Mathf.MoveTowards(r.PoseWeight, active ? 1 : 0, dt * 5);
            float weight = r.PoseWeight; if (weight <= 0 || !active) return;
            Vector3 look = p.Root.InverseTransformPoint(r.Source + Vector3.up * 1.7f);
            float yaw = Mathf.Clamp(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -60, 60);
            float nod = r.Kind == ReactionKind.Talking ? Mathf.Sin((ctx.Clock - r.Started) * 3.6f + p.Data.phase) * 4 : 0;
            rig.Head.localRotation = Quaternion.Slerp(rig.Head.localRotation, Quaternion.Euler(nod, yaw, 0), weight);
            bool handsUp = r.Kind == ReactionKind.Surrender;
            bool cover = r.Kind == ReactionKind.Flee && CityPhysics.Flat(p.Velocity).magnitude < .4f;
            if (handsUp || cover)
            {
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1 : 1;
                    CityActors.Reach(rig, i, p.Root.TransformPoint(new Vector3(side * (handsUp ? .48f : .24f), handsUp ? 2.18f : 1.96f, .16f)), weight);
                    CityActors.Upright(rig.Hands[i], p.Root, new Vector3(-75, 0, side * 12));
                }
                rig.Chest.localRotation *= Quaternion.Euler(cover ? 12 : -4, 0, 0);
            }
            else if (r.Kind == ReactionKind.Cautious)
                CityActors.Reach(rig, 0, p.Root.TransformPoint(new Vector3(-.32f, 1.5f, .45f)), weight * .8f);
            else if (r.Kind == ReactionKind.Talking)
            {
                float beat = .5f + .5f * Mathf.Sin((ctx.Clock - r.Started) * 3 + p.Data.phase);
                int hand = Mathf.Abs(p.Data.id % 2);
                CityActors.Reach(rig, hand, p.Root.TransformPoint(new Vector3(hand == 0 ? -.32f : .32f, 1.18f + beat * .18f, .40f + beat * .1f)), weight * .75f);
            }
        }
    }
}
