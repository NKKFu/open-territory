using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-30)]
    public sealed class CityPolice : MonoBehaviour
    {
        // Each suspect owns their evidence and response. Nearby innocent people
        // and victims defending themselves never inherit somebody else's charges.
        sealed class Case
        {
            public Transform Suspect;
            public CityResidents.Resident Person, Witness;
            public readonly List<CityResidents.Resident> Unit = new List<CityResidents.Resident>(2);
            public string Stage = "chamada";
            public Vector3 LastKnown, CallSpot;
            public float Until, ScanAt, LastSeen = -100, LastEvent = -100, CloseSince, SpawnAt;
            public int Level, BurstWeight;
            public bool Identified, CallIdentified;
        }
        const int CaseLimit = 4;
        CityContext ctx;
        readonly List<CityResidents.Resident> officers = new List<CityResidents.Resident>(), returning = new List<CityResidents.Resident>();
        readonly List<Case> cases = new List<Case>(CaseLimit);
        readonly Dictionary<CityResidents.Resident, float> jammed = new Dictionary<CityResidents.Resident, float>();
        readonly Dictionary<string, int> charges = new Dictionary<string, int> { { "ameaca", 1 }, { "disparo", 2 }, { "agressao", 2 }, { "roubo", 2 }, { "furto", 1 }, { "furto-policial", 3 }, { "atropelamento", 3 }, { "dano", 1 } };
        int patrolId;
        float patrolAt;
        Case PlayerCase => FindCase(ctx ? ctx.Player : null);
        Case CurrentCall
        {
            get
            {
                var player = PlayerCase; if (player != null && player.Witness != null && player.Witness.Calling) return player;
                Case nearest = null; float distance = float.MaxValue;
                foreach (var c in cases) if (Present(c.Witness) && c.Witness.Calling)
                { float d = (c.Witness.Root.position - ctx.Player.position).sqrMagnitude; if (d < distance) { nearest = c; distance = d; } }
                return nearest;
            }
        }
        public string Stage => PlayerCase?.Stage ?? "livre";
        public bool Custody => Stage == "rendicao";
        public bool TradeBlocked => PlayerCase != null;
        public bool Calling => CurrentCall != null;
        public int Level => PlayerCase?.Level ?? 0;
        public int ActiveCases => cases.Count;
        public int LastFine { get; private set; }
        public Vector3 CallPosition => CurrentCall?.Witness.Root.position + Vector3.up * 1.8f ?? Vector3.zero;
        public bool CanInterruptCall => ctx && CanInterruptCallFrom(ctx.Player.position + Vector3.up * 1.5f, 18);
        public bool CanInterruptCallFrom(Vector3 eye, float range) => ctx && Calling && !ctx.Combat.Down && !ctx.Paused &&
            Vector3.Distance(eye, CallPosition) < range && ctx.Visible(eye, CallPosition);
        public bool Approaching => Stage == "abordagem" && ctx.Motor.Grounded && !ctx.Motor.Riding && !ctx.Motor.Swimming && NearestOfficer(PlayerCase, 4.1f) != null;
        Case FindCase(Transform suspect) { if (suspect) foreach (var c in cases) if (c.Suspect == suspect) return c; return null; }
        public string SuspectStage(Transform suspect) => FindCase(suspect)?.Stage ?? "livre";
        public bool IsDetained(Transform suspect) => SuspectStage(suspect) == "rendicao";
        public bool Involved(CityResidents.Resident p)
        { foreach (var c in cases) if (c.Person == p || c.Witness == p || c.Unit.Contains(p)) return true; return false; }
        public Transform OfficerTarget(CityResidents.Resident p)
        { foreach (var c in cases) if (c.Unit.Contains(p)) return c.Suspect; return null; }

        public void Initialize(CityContext context)
        {
            ctx = context;
            foreach (var data in ctx.Data.residents) if (data.role == "police") { var p = ctx.Residents.Find(data.id); if (p != null) officers.Add(p); }
            foreach (var data in ctx.Data.vehicles) if (data.role == "police" && data.auto) { patrolId = data.id; break; }
            ctx.AddAction("police-surrender", () => ctx.Player.position + Vector3.up, () => "Surrender", () => Surrender(), () => Approaching && !ctx.Combat.Down, 4.5f, 100, requiresLook: false);
            if (ctx.Vehicles) ctx.Vehicles.Crime += OnVehicleCrime;
        }
        void OnVehicleCrime(string kind, Vector3 position) => Report(kind, position, null, kind == "dano");
        bool Present(CityResidents.Resident p) => p != null && p.Root && p.Root.gameObject.activeInHierarchy && !p.Down && p.Bus == null;
        bool Sees(CityResidents.Resident p, Vector3 point, float radius, bool alerted = false)
        {
            if (!Present(p)) return false;
            if (p.Data.role != "police") return ctx.Residents.Notices(p, point, radius, alerted);
            Vector3 direction = point - p.Root.position; float distance = direction.magnitude;
            if (ctx.Systems) radius *= ctx.Systems.HackVisibility(point);
            if (distance > radius || !alerted && distance > 5 && Vector3.Dot(p.Root.forward, direction.normalized) < -.12f) return false;
            return ctx.Visible(p.Root.position + Vector3.up * (1.75f * p.Root.lossyScale.y), point + Vector3.up * 1.35f);
        }
        bool Hears(CityResidents.Resident p, Vector3 point, bool loud)
        { float d = Vector3.Distance(p.Root.position, point); return loud && d < 18 && (d < 7 || ctx.Visible(p.Root.position + Vector3.up * 1.7f, point + Vector3.up * 1.35f)); }
        void Notice(CityResidents.Resident person, string text, Vector3 point, float duration = 4)
        {
            Vector3 at = person != null && person.Root ? person.Root.position : point;
            if (Vector3.Distance(at, ctx.Player.position) <= 55) ctx.Say((person == null ? "PATROL RADIO" : person.Data.name) + " · " + text, at + Vector3.up * 2.5f, duration);
        }
        void StopCall(Case c)
        {
            if (c.Witness != null) { c.Witness.Calling = false; ctx.Residents.Control(c.Witness, false); c.Witness.NextSocial = ctx.Clock + 15; }
            c.Witness = null;
        }
        void StartCall(Case c)
        {
            c.Until = ctx.Clock + 4.6f; ctx.Residents.Control(c.Witness, true); c.Witness.Calling = true;
            Notice(c.Witness, "Police? Someone needs help!", c.CallSpot);
        }
        bool WitnessAssigned(CityResidents.Resident p)
        { foreach (var c in cases) if (c.Witness == p) return true; return false; }
        internal void SuspendVictimCall(CityResidents.Resident p)
        {
            if (!p.Calling) return;
            foreach (var c in cases) if (c.Witness == p) { p.Calling = false; ctx.Residents.Control(p, false); return; }
        }
        public bool InterruptCall() => InterruptCallFrom(ctx.Player.position + Vector3.up * 1.5f, 18);
        public bool InterruptCallFrom(Vector3 eye, float range)
        {
            if (!CanInterruptCallFrom(eye, range)) return false;
            var c = CurrentCall; var source = c.Witness; jammed[source] = ctx.Clock + 14; Resolve(c);
            ctx.Sound("hack", source.Root.position); Notice(source, "The call dropped…", source.Root.position, 2.7f); return true;
        }
        public bool Report(string type, Vector3 spot, CityResidents.Resident victim = null, bool loud = false, Transform offender = null)
        {
            if (!ctx || ctx.Paused || !charges.TryGetValue(type, out int weight)) return false;
            if (!offender) offender = ctx.Player;
            if (!offender.gameObject.activeInHierarchy || victim != null && victim.Root == offender || offender == ctx.Player && Custody) return false;
            if (offender == ctx.Player && type == "agressao" && victim != null && ctx.Combat.Punching && !ctx.Combat.WeaponVisible && ctx.Combat.DefendingAgainst(victim.Root))
            {
                ctx.Residents.ReactToCrime(type, spot, victim, loud, victim.Root); return false;
            }
            ctx.Residents.ReactToCrime(type, spot, victim, loud, offender);
            CityResidents.Resident direct = null, observer = null, heard = null;
            float nearestOfficer = float.MaxValue, nearestWitness = float.MaxValue, nearestHeard = float.MaxValue;
            bool directKnows = false, observerKnows = false;
            foreach (var p in ctx.Residents.People)
            {
                if (!Present(p) || p.Data.child || p.Root == offender) continue;
                float distance = Vector3.Distance(p.Root.position, spot);
                bool saw = p == victim && distance < 4 || Sees(p, spot, p.Data.role == "police" ? 30 : 21, loud || p == victim);
                bool knows = saw && Sees(p, offender.position, 31, true);
                bool audible = Hears(p, spot, loud);
                if (p.Data.role == "police")
                { if ((saw || audible) && distance < nearestOfficer) { direct = p; nearestOfficer = distance; directKnows = knows; } continue; }
                if (p.Calling || WitnessAssigned(p) || p.Controlled && p != victim || jammed.TryGetValue(p, out float jam) && ctx.Clock < jam) continue;
                float witnessScore = distance + (p == victim ? 8 : 0);
                if (saw && witnessScore < nearestWitness) { observer = p; nearestWitness = witnessScore; observerKnows = knows; }
                if (audible && distance < nearestHeard) { heard = p; nearestHeard = distance; }
            }
            var cruiser = patrolId != 0 ? ctx.Node(patrolId) : null;
            bool cruiserWitness = cruiser && cruiser.gameObject.activeInHierarchy && Vector3.Distance(cruiser.position, spot) < 27 && ctx.Visible(cruiser.position + Vector3.up * 2.3f, spot + Vector3.up * 1.8f);
            if (direct == null && observer == null && heard == null && !cruiserWitness) return false;
            var c = FindCase(offender);
            if (c == null)
            {
                if (cases.Count >= CaseLimit) return false;
                c = new Case { Suspect = offender, Person = ctx.Residents.Find(offender), LastKnown = spot };
                cases.Add(c);
            }
            if (c.Stage == "rendicao") return true;
            bool burst = ctx.Clock - c.LastEvent < .9f;
            c.Level = Mathf.Min(3, c.Level + (burst ? Mathf.Max(0, weight - c.BurstWeight) : weight));
            c.BurstWeight = burst ? Mathf.Max(weight, c.BurstWeight) : weight; c.LastEvent = ctx.Clock;
            if (direct != null || cruiserWitness)
            {
                bool knows = direct != null ? directKnows : ctx.Visible(cruiser.position + Vector3.up * 2.3f, offender.position + Vector3.up * 1.35f);
                if (knows) { c.Identified = true; c.LastKnown = offender.position; c.LastSeen = ctx.Clock; }
                if (c.Stage != "chamada" && c.Unit.Count > 0) return true;
                Dispatch(c, knows ? offender.position : spot, knows, direct); return true;
            }
            if (c.Witness != null)
            {
                if (Sees(c.Witness, offender.position, 26, true)) { c.CallIdentified = true; c.CallSpot = offender.position; }
                return true;
            }
            if (c.Stage != "chamada") return true;
            c.Witness = observer ?? heard; c.CallIdentified = observer != null && observerKnows;
            c.CallSpot = c.CallIdentified ? offender.position : spot;
            // Let a victim finish a close defensive exchange before occupying
            // their hand and controller with a phone. Other witnesses call now.
            if (!ctx.Combat.NeedsSelfDefence(c.Witness)) StartCall(c); return true;
        }
        bool Assigned(CityResidents.Resident p, Case except)
        { foreach (var c in cases) if (c != except && c.Unit.Contains(p)) return true; return false; }
        void Assign(Case c, CityResidents.Resident p)
        {
            foreach (var other in cases) if (other != c) other.Unit.Remove(p);
            returning.Remove(p); c.Unit.Add(p); ctx.Residents.Control(p, true);
        }
        void FillUnit(Case c, CityResidents.Resident direct = null)
        {
            int wanted = c.Level >= 2 ? 2 : 1;
            if (direct != null && Present(direct) && !c.Unit.Contains(direct) && (!Assigned(direct, c) || c.Suspect == ctx.Player)) Assign(c, direct);
            while (c.Unit.Count < wanted)
            {
                CityResidents.Resident nearest = null; float distance = float.MaxValue;
                foreach (var p in officers)
                {
                    if (!Present(p) || c.Unit.Contains(p) || Assigned(p, c)) continue;
                    float d = (p.Root.position - c.LastKnown).sqrMagnitude; if (d < distance) { nearest = p; distance = d; }
                }
                if (nearest == null) break; Assign(c, nearest);
            }
            if (c.Unit.Count >= wanted || ctx.Clock < c.SpawnAt) return;
            c.SpawnAt = ctx.Clock + 2;
            foreach (var p in officers)
                if (!Present(p) && !p.Down && !ctx.Residents.IsSpawned(p.Data.id) && ctx.Population.TrySpawnResponder(p.Data.id, c.LastKnown))
                { Assign(c, p); if (c.Unit.Count >= wanted) break; }
        }
        void Dispatch(Case c, Vector3 point, bool knows, CityResidents.Resident direct)
        {
            StopCall(c); c.Identified |= knows; c.LastKnown = point; c.ScanAt = c.CloseSince = 0;
            FillUnit(c, direct);
            if (direct != null && knows) { c.Stage = "perseguicao"; c.LastSeen = ctx.Clock; Notice(direct, "Stop! Keep your hands visible.", point); }
            else { c.Stage = "despacho"; c.Until = ctx.Clock + 34; Notice(null, knows ? "Description received. Officers on the way." : "Check the reported disturbance.", point); }
            ctx.Sound("radio", point, .6f); patrolAt = 0;
        }
        CityResidents.Resident NearestOfficer(Case c, float radius)
        {
            if (c == null || !c.Suspect) return null;
            CityResidents.Resident result = null; float distance = radius;
            foreach (var p in c.Unit) if (Present(p))
            { float d = Vector3.Distance(p.Root.position, c.Suspect.position); if (d < distance && Sees(p, c.Suspect.position, radius + 1, true)) { distance = d; result = p; } }
            return result;
        }
        public bool Surrender()
        {
            if (!Approaching) return false;
            var c = PlayerCase; c.Stage = "rendicao"; c.Until = ctx.Clock + 2.1f;
            ctx.Combat.Holster(); ctx.Actors.CancelInteraction(); ctx.Motor.ClearMotion();
            Notice(NearestOfficer(c, 5), "Stay still while I record the incident.", ctx.Player.position); return true;
        }
        void Return(CityResidents.Resident p)
        { if (p != null && !returning.Contains(p)) { returning.Add(p); ctx.Residents.Control(p, true); } }
        void Resolve(Case c)
        {
            StopCall(c); foreach (var p in c.Unit) Return(p); c.Unit.Clear();
            if (c.Person != null && c.Stage == "rendicao") ctx.Residents.ReleaseDetained(c.Person);
            cases.Remove(c); patrolAt = 0;
            if (cases.Count == 0 && patrolId != 0) ctx.Vehicles.ReleaseCommand(patrolId);
        }
        public void Clear() { for (int i = cases.Count - 1; i >= 0; i--) Resolve(cases[i]); }
        void UpdateCase(Case c, float dt)
        {
            bool player = c.Suspect == ctx.Player;
            if (!c.Suspect || !c.Suspect.gameObject.activeInHierarchy || player && ctx.Combat.Down) { Resolve(c); return; }
            if (c.Stage == "rendicao")
            {
                if (ctx.Clock < c.Until) return;
                if (player) { LastFine = ctx.Commerce.Fine(15 + c.Level * 20); Notice(null, LastFine > 0 ? "Incident recorded. Fine: R$ " + LastFine + ". You may go." : "Incident recorded. You may go.", c.Suspect.position); }
                else Notice(null, "Suspect detained. The area is safe.", c.Suspect.position);
                Resolve(c); return;
            }
            if (c.Witness != null)
            {
                if (!Present(c.Witness)) { Resolve(c); return; }
                if (!c.Witness.Calling) { if (!ctx.Combat.NeedsSelfDefence(c.Witness)) StartCall(c); return; }
                if (ctx.Clock >= c.Until) Dispatch(c, c.CallSpot, c.CallIdentified, null);
                return;
            }
            for (int i = c.Unit.Count - 1; i >= 0; i--) if (!Present(c.Unit[i])) { ctx.Residents.Control(c.Unit[i], false); c.Unit.RemoveAt(i); }
            FillUnit(c);
            if (ctx.Clock >= c.ScanAt)
            {
                c.ScanAt = ctx.Clock + .18f; bool seen = false;
                foreach (var p in c.Unit) if (Sees(p, c.Suspect.position, c.Identified ? 35 : 25, true)) { seen = true; break; }
                // Unknown noises supply a location, never a magical identity.
                bool corroborated = player ? ctx.Combat.IsThreatening() : ctx.Residents.IsStreetOffender(c.Person);
                if (seen && (c.Identified || corroborated && Vector3.Distance(c.Suspect.position, c.LastKnown) < 7))
                {
                    c.Identified = true; c.LastSeen = ctx.Clock; c.LastKnown = c.Suspect.position;
                    if (c.Stage == "busca" || c.Stage == "despacho") { c.Stage = "perseguicao"; Notice(null, "Suspect in sight. Following.", c.LastKnown); }
                }
            }
            if (c.Identified && ctx.Clock - c.LastSeen > 3 && (c.Stage == "perseguicao" || c.Stage == "abordagem"))
            { c.Stage = "busca"; c.Until = ctx.Clock + 17 + c.Level * 3; Notice(null, "Lost contact. Check the nearby streets.", c.LastKnown); }
            if (c.Stage == "despacho" && ctx.Clock >= c.Until) { c.Stage = "busca"; c.Until = ctx.Clock + 15; }
            if (c.Stage == "busca" && ctx.Clock >= c.Until) { Resolve(c); return; }
            bool onFoot = !player || !ctx.Motor.Riding && !ctx.Motor.Swimming;
            if (c.Stage == "perseguicao" || c.Stage == "abordagem")
            {
                var nearby = onFoot && ctx.Clock - c.LastSeen < .6f ? NearestOfficer(c, 3.7f) : null;
                if (nearby != null && c.Stage != "abordagem") { c.Stage = "abordagem"; c.CloseSince = ctx.Clock; Notice(nearby, player ? "Stop. Surrender!" : "Stop! Hands up!", c.LastKnown, 3.2f); }
                else if (nearby == null && c.Stage == "abordagem") c.Stage = "perseguicao";
                if (!player && nearby != null && Vector3.Distance(nearby.Root.position, c.Suspect.position) < 2 && ctx.Clock - c.CloseSince > .8f)
                {
                    c.Stage = "rendicao"; c.Until = ctx.Clock + 6;
                    ctx.Residents.Detain(c.Person, nearby.Root.position); Notice(nearby, "You're under arrest.", c.LastKnown, 3); return;
                }
            }
            foreach (var p in c.Unit)
            {
                if (!Present(p) || ctx.Combat.MeleeActing(p.Root)) continue;
                float distance = Vector3.Distance(p.Root.position, c.Suspect.position);
                bool armedThreat = player && c.Identified && c.Level >= 2 && onFoot && ctx.Clock - c.LastSeen < .6f &&
                    (c.Stage == "perseguicao" || c.Stage == "abordagem") && ctx.Combat.WeaponVisible && ctx.Combat.Equipped != "pulse" &&
                    (ctx.Combat.Aiming || ctx.Clock - ctx.Combat.LastGunfireAt < 6);
                if (armedThreat && distance > 2.2f && distance < 24)
                {
                    Vector3 facing = CityPhysics.Flat(c.Suspect.position - p.Root.position);
                    if (facing.sqrMagnitude > .01f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(facing), dt * 7);
                    if (ctx.Combat.TryResidentShot(p, c.Suspect)) continue;
                }
                Vector3 goal = c.LastKnown;
                if (c.Stage == "busca") { float a = (ctx.Clock * .13f + p.Data.id) * 1.5f; goal += new Vector3(Mathf.Sin(a) * 4, 0, Mathf.Cos(a) * 4); }
                if ((c.Stage == "perseguicao" || c.Stage == "abordagem") && distance < 3.7f && ctx.Clock - c.LastSeen < .6f && onFoot)
                {
                    Vector3 direction = CityPhysics.Flat(c.Suspect.position - p.Root.position);
                    if (direction.sqrMagnitude > .01f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(direction), dt * 6);
                    if (distance > 1.65f) ctx.Residents.MoveToward(p, c.Suspect.position, dt, player ? 1.9f : 3.9f, true);
                    else if (player && ctx.Clock - c.CloseSince > 3.3f && (ctx.Combat.IsThreatening() || c.Level >= 2))
                    { if (ctx.Combat.TryResidentStrike(p, c.Suspect, 18, false)) c.CloseSince = ctx.Clock - 2; }
                }
                else ctx.Residents.MoveToward(p, goal, dt, c.Stage == "perseguicao" ? 3.9f : 2.1f, true);
            }
        }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return; float dt = Time.deltaTime;
            for (int i = returning.Count - 1; i >= 0; i--)
            {
                var p = returning[i];
                if (!Present(p) || ctx.Residents.MoveToward(p, p.Home, dt, 1.5f, true)) { returning.RemoveAt(i); ctx.Residents.Control(p, false); }
            }
            for (int i = cases.Count - 1; i >= 0; i--) UpdateCase(cases[i], dt);
            if (patrolId == 0 || ctx.Clock < patrolAt) return;
            patrolAt = ctx.Clock + .8f; Case response = PlayerCase;
            if (response == null) foreach (var c in cases) if (c.Stage != "chamada" && c.Stage != "rendicao") { response = c; break; }
            if (response != null && response.Stage != "chamada" && response.Stage != "rendicao") ctx.Vehicles.Command(patrolId, response.LastKnown, response.Stage != "busca");
            else ctx.Vehicles.ReleaseCommand(patrolId);
        }
        void OnDestroy() { if (ctx && ctx.Vehicles) ctx.Vehicles.Crime -= OnVehicleCrime; }
    }
}
