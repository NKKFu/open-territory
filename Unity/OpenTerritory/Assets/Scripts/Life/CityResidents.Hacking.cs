using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        sealed class HackReaction
        {
            public Vector3 source, goal;
            public float until, speakAt;
            public string line;
            public bool phone, approach, spoke;
        }
        readonly Dictionary<Resident, HackReaction> hackReactions = new Dictionary<Resident, HackReaction>();
        readonly List<Resident> hackObservers = new List<Resident>(32);
        public bool IsReactingToHack(Resident person) => person != null && hackReactions.TryGetValue(person, out var reaction) && ctx.Clock < reaction.until;
        bool HackPhoneBusy(Resident person) => IsReactingToHack(person) && hackReactions[person].phone && !person.Controlled && !person.Down;
        void CancelHackReaction(Resident person)
        {
            if (person != null && hackReactions.Remove(person)) { person.Path.Clear(); person.PathIndex = 0; }
        }
        public bool HackPhone(Resident person)
        {
            if (person == null || !person.Root.gameObject.activeInHierarchy || person.Down || person.Health < 99.99f || person.Controlled || person.Calling || person.Crossing || person.Bus != null || person.FearUntil > ctx.Clock) return false;
            Interrupt(person); person.MoveSpeed = 0; person.NextSocial = ctx.Clock + 10;
            hackReactions[person] = new HackReaction { source = person.Root.position, until = ctx.Clock + 5, phone = true,
                line = "Ué?", speakAt = ctx.Clock + .8f };
            person.SocialUntil = ctx.Clock + 5; return true;
        }
        public int ReactToHack(Vector3 source, string line, bool investigate, float radius = 12)
        {
            int count = 0; Resident investigator = null;
            hackObservers.Clear();
            foreach (var person in People) if (CanReactToHack(person, source, radius)) hackObservers.Add(person);
            hackObservers.Sort((a, b) => (a.Root.position - source).sqrMagnitude.CompareTo((b.Root.position - source).sqrMagnitude));
            foreach (var person in hackObservers)
            {
                if (count >= 3) break;
                if (investigator == null && !person.Data.stationary && !person.Data.sitting && !person.Data.performance && person.Doorway == null) investigator = person;
                Interrupt(person); person.Gesture = "look"; person.Threat = source; person.SocialUntil = ctx.Clock + 6;
                person.NextLook = ctx.Clock + 8; person.NextSocial = ctx.Clock + 10;
                Vector3 goal = person.Root.position;
                bool approach = investigate && person == investigator && HackInvestigationPoint(person, source, out goal);
                hackReactions[person] = new HackReaction { source = source, goal = goal, approach = approach,
                    until = ctx.Clock + 6, speakAt = ctx.Clock + .7f + count * .3f, line = count == 0 ? line : null };
                count++;
            }
            return count;
        }
        bool CanReactToHack(Resident person, Vector3 source, float radius) =>
            person.Root && person.Root.gameObject.activeInHierarchy && !person.Down && person.Health >= 99.99f && !person.Controlled && !person.Calling && !person.Crossing &&
            !person.Data.child && person.Bus == null && person.FearUntil <= ctx.Clock && person.TalkUntil <= ctx.Clock &&
            !IsReactingToHack(person) && Vector3.Distance(person.Root.position, source) <= radius &&
            ctx.Visible(person.Root.position + Vector3.up * 1.7f, source);
        bool HackInvestigationPoint(Resident person, Vector3 source, out Vector3 goal)
        {
            goal = person.Root.position; float best = float.MaxValue;
            foreach (var node in ctx.Data.nav)
            {
                if (node.edges.Length == 0 || System.Array.Exists(node.edges, e => e.crossing || ctx.LifeNavigation.Crossing(node.id, e.to))) continue;
                Vector3 point = new Vector3(node.x, source.y, node.z);
                float fromSource = CityPhysics.Flat(point - source).magnitude;
                if (fromSource < 1.8f || fromSource > 5.5f || CityPhysics.Flat(point - person.Root.position).magnitude > 10) continue;
                point.y = ctx.Ground(node.x, node.z) + .04f;
                if (!CanOccupy(person, point)) continue;
                float score = fromSource + CityPhysics.Flat(point - person.Root.position).magnitude * .4f;
                if (score < best) { best = score; goal = point; }
            }
            return best < float.MaxValue && CityPhysics.Flat(goal - person.Root.position).magnitude > .8f;
        }
        bool TickHackReaction(Resident person, float dt)
        {
            if (!hackReactions.TryGetValue(person, out var reaction)) return false;
            if (ctx.Clock >= reaction.until)
            {
                hackReactions.Remove(person); person.Gesture = null; person.SocialUntil = 0;
                person.Path.Clear(); person.PathIndex = 0; person.MoveSpeed = 0; return false;
            }
            if (!reaction.spoke && ctx.Clock >= reaction.speakAt)
            {
                reaction.spoke = true;
                if (!string.IsNullOrEmpty(reaction.line)) ctx.Say(person.Data.name + " · " + reaction.line, person.Root.position + Vector3.up * 2.5f, 3);
            }
            if (reaction.approach)
            {
                if (MoveToward(person, reaction.goal, dt, Mathf.Min(1.2f, person.Speed))) reaction.approach = false;
            }
            else { person.MoveSpeed = 0; if (!reaction.phone) Face(person, reaction.source, dt); }
            return true;
        }
    }
}
