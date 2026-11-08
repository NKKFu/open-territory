using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Native integration, installed exclusively by --city-crime-response-qa.
    // The platform only isolates geometry; controllers, inputs, damage, rigs,
    // witness selection, dispatch and police movement are the production code.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityCrimeResponseProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true, completed;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        readonly List<CityResidents.Resident> subjects = new List<CityResidents.Resident>();
        CityContext ctx;
        CityResidents.Resident offender, victim, witness, bystander, officer;
        GameObject floor, wall;
        string directory;
        float began;
        Vector3 citySpawn;
        readonly Vector3 origin = new Vector3(5000, 300.04f, 5000);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-crime-response-qa") >= 0)
                new GameObject("Crime response QA · opt-in").AddComponent<CityCrimeResponseProbe>();
        }
        void Awake() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Error) report.errors.Add(message + "\n" + stack); }
        void Update()
        { if (!report.completed && Time.realtimeSinceStartup - began > 175) { CheckResult("probe-completes-within-timeout", false); StopAllCoroutines(); Finish(); } }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("CRIME_QA " + name + " " + passed + " " + detail);
            File.WriteAllText(Path.Combine(directory, "crime-response-qa.json"), JsonUtility.ToJson(report, true));
        }
        CityResidents.Resident Subject(Func<ResidentData, bool> predicate)
        {
            var data = ctx.Data.residents.First(d => predicate(d) && !ctx.Residents.IsSpawned(d.id) && subjects.All(p => p.Data.id != d.id));
            ctx.Residents.Spawn(data.id, origin + Vector3.forward * 20, Quaternion.identity);
            var p = ctx.Residents.Find(data.id); subjects.Add(p); return p;
        }
        void Place(CityResidents.Resident p, float x, float z, bool active = true)
        {
            ctx.Residents.Interrupt(p); ctx.Combat.ForgetTemporaryActor(p.Root); p.Root.gameObject.SetActive(active);
            p.Controller.enabled = false; p.Root.SetPositionAndRotation(origin + new Vector3(x, 0, z), Quaternion.Euler(0, 180, 0)); p.Controller.enabled = active;
            p.Home = p.LastPosition = p.Root.position; p.Velocity = p.Steering = Vector3.zero; p.MoveSpeed = 0; p.Pace = 1;
            p.Controlled = p.Calling = p.Crossing = false; p.Path.Clear(); p.PathIndex = 0; p.Doorway = null;
            p.Health = 100; p.TalkUntil = p.FearUntil = p.DodgeUntil = p.CrimeCooldown = p.RobbedUntil = 0; p.PocketCash = -1;
            p.Reaction.Kind = CityResidents.ReactionKind.None; p.Reaction.Until = p.Reaction.MemoryUntil = p.Reaction.NextReport = p.Reaction.PoseWeight = 0;
            p.Reaction.NextScan = ctx.Clock + 1000; p.Reaction.WeaponConcern = false;
            p.State = "activity"; p.Activity = "rest"; p.Age = 0; p.Duration = 1000; p.NextSocial = ctx.Clock + 1000;
        }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); yield return null; }
        IEnumerator Reset(bool police = false)
        {
            ctx.UI.SetSettings(false); ctx.Residents.EndStreetCrime(); ctx.Police.Clear(); ctx.Combat.Holster();
            ctx.Combat.HealAtClinic();
            ctx.Input.TouchAim = ctx.Input.TouchFire = false; ctx.Input.TouchMove = Vector2.zero;
            ctx.Motor.Warp(origin); ctx.Player.rotation = Quaternion.identity;
            Place(offender, -5, 5); Place(victim, 0, 5); Place(witness, 4, 6); Place(bystander, -4, 8); Place(officer, 9, 5, police);
            ctx.Camera.transform.SetPositionAndRotation(origin + new Vector3(5, 7, -7), Quaternion.Euler(24, -12, 0));
            if (wall) { Destroy(wall); wall = null; }
            Physics.SyncTransforms(); yield return Wait(.15f);
        }
        void Wall(float z = 3)
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA evidence occluder"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = origin + new Vector3(0, 2, z); wall.transform.localScale = new Vector3(24, 4, .4f); Physics.SyncTransforms();
        }
        bool Panics(CityResidents.Resident p) => p.Calling || p.Reaction.Kind == CityResidents.ReactionKind.Flee || p.Reaction.Kind == CityResidents.ReactionKind.Surrender;
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "crime-response-qa");
            Directory.CreateDirectory(directory);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; citySpawn = ctx.Player.position; ctx.Population.enabled = ctx.Follow.enabled = false; ctx.Residents.AmbientCrimeEnabled = false;
            foreach (var p in ctx.Residents.People) p.Controlled = true;
            CheckResult("native-QA-storage-is-isolated", CityCommerce.IsReviewRun && ctx.Commerce.SavePath.Contains("BaiaDoSol-QA"));
            CheckResult("probe-is-opt-in", FindObjectsByType<CityCrimeResponseProbe>(FindObjectsSortMode.None).Length == 1);
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA isolated street"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = origin - Vector3.up * 1.04f; floor.transform.localScale = new Vector3(160, 2, 160);
            Func<ResidentData, bool> mobile = d => !d.child && !d.sitting && !d.stationary && !d.performance && string.IsNullOrEmpty(d.role);
            offender = Subject(mobile); victim = Subject(d => mobile(d) && d.id % 3 == 0); witness = Subject(mobile); bystander = Subject(mobile);
            officer = Subject(d => d.role == "police"); ctx.Combat.Grant("sidearm");

            yield return Reset();
            // Drive the real input path, including camera targeting and E.
            Place(victim, 0, 2.2f); victim.Reaction.NextScan = 0;
            ctx.Camera.transform.position = origin + new Vector3(0, 2, -3); ctx.Camera.transform.LookAt(victim.Root.position + Vector3.up * 1.45f);
            ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.85f);
            CheckResult("aiming-intimidates-the-actual-target", ctx.Combat.AimTarget == victim && victim.Reaction.Kind == CityResidents.ReactionKind.Surrender);
            ctx.RefreshAction(); CheckResult("compact-Rob-action-is-selected", ctx.CurrentAction?.id == "rob-resident-" + victim.Data.id, ctx.CurrentAction?.id);
            Capture("00-rob-prompt");
            int balance = ctx.Commerce.Balance; ctx.Input.TouchInteract = true; yield return Wait(.25f);
            CheckResult("E-rob-transfers-cash-once", ctx.Commerce.Balance > balance && victim.PocketCash == 0, "balance=" + ctx.Commerce.Balance);
            CheckResult("robbery-starts-police-call-and-crowd-alarm", ctx.Police.Calling && Panics(victim) && Panics(witness) && Panics(bystander));
            CheckResult("victim-cannot-be-robbed-repeatedly", !ctx.Residents.Rob(victim) && victim.RobbedUntil > ctx.Clock);
            Capture("01-player-robbery");

            yield return Reset(true); Place(victim, 0, 1.1f);
            ctx.Camera.transform.SetPositionAndRotation(origin + new Vector3(.98f, 2.8f, -2.47f), Quaternion.Euler(18, 0, 0));
            ctx.Input.TouchFire = true; yield return Wait(.24f);
            CheckResult("actual-player-punch-damages-and-reports", ctx.Combat.HealthOf(victim.Root) < 100 && ctx.Police.Level >= 2);
            CheckResult("police-identifies-player-for-player-assault", ctx.Police.OfficerTarget(officer) == ctx.Player);
            CheckResult("assault-alarms-other-residents", Panics(witness) && Panics(bystander));

            yield return Reset(); Place(victim, 0, 1.1f); offender.Root.gameObject.SetActive(false); witness.Root.gameObject.SetActive(false); bystander.Root.gameObject.SetActive(false);
            ctx.Camera.transform.SetPositionAndRotation(origin + new Vector3(.98f, 2.8f, -2.47f), Quaternion.Euler(18, 0, 0));
            yield return Wait(.5f); // Respect the preceding real punch's recovery.
            int replies = ctx.Combat.CounterSwings; ctx.Input.TouchFire = true; yield return Wait(1.15f);
            CheckResult("victim-defends-before-using-phone", ctx.Combat.CounterSwings > replies && ctx.Combat.Health < 100 && !victim.Calling,
                "replies=" + (ctx.Combat.CounterSwings - replies) + " health=" + ctx.Combat.Health + " calling=" + victim.Calling + " level=" + ctx.Police.Level);
            ctx.Motor.Warp(origin - Vector3.forward * 6); yield return Wait(.25f);
            CheckResult("victim-calls-after-close-confrontation-ends", victim.Calling && ctx.Police.Calling && ctx.Police.Level >= 2,
                "calling=" + victim.Calling + " stage=" + ctx.Police.Stage + " reaction=" + victim.Reaction.Kind);

            yield return Reset(); Place(offender, 0, 5); Place(victim, 0, 6.1f);
            int swings = ctx.Combat.CounterSwings;
            CheckResult("NPC-can-start-real-assault", ctx.Combat.TryResidentStrike(offender, victim.Root, 18, true));
            yield return Wait(.12f);
            CheckResult("NPC-attack-has-visible-windup", ctx.Combat.CounterSwings == swings + 1 && ctx.Combat.MeleeActing(offender.Root) && ctx.Combat.HealthOf(victim.Root) == 100);
            float strikeClock = ctx.Clock; ctx.UI.SetSettings(true); yield return Wait(.3f);
            CheckResult("Esc-freezes-NPC-strike-before-contact", ctx.Clock == strikeClock && ctx.Combat.HealthOf(victim.Root) == 100);
            ctx.UI.SetSettings(false);
            yield return Wait(.3f);
            CheckResult("NPC-fist-contact-damages-victim", ctx.Combat.HealthOf(victim.Root) == 82, "health=" + ctx.Combat.HealthOf(victim.Root));
            CheckResult("NPC-assault-selects-one-witness", ctx.Police.SuspectStage(offender.Root) == "chamada" && subjects.Count(p => p.Calling) == 1);
            CheckResult("NPC-crime-does-not-charge-innocent-player", ctx.Police.Level == 0 && ctx.Police.Stage == "livre" && !ctx.Police.TradeBlocked);
            CheckResult("NPC-witnesses-do-not-blame-player-in-memory", victim.Reaction.MemoryUntil <= ctx.Clock && witness.Reaction.MemoryUntil <= ctx.Clock);
            CheckResult("offender-does-not-flee-from-own-crime", offender.Reaction.Kind == CityResidents.ReactionKind.None);
            float clock = ctx.Clock; var caller = subjects.FirstOrDefault(p => p.Calling); Vector3 before = witness.Root.position;
            ctx.UI.SetSettings(true); yield return Wait(.35f);
            CheckResult("Esc-freezes-call-and-crowd-reactions", ctx.Clock == clock && before == witness.Root.position && caller != null && caller.Calling);
            ctx.UI.SetSettings(false);
            CheckResult("hacking-interrupts-NPC-report", ctx.Police.InterruptCallFrom(ctx.Player.position + Vector3.up * 1.5f, 18) && ctx.Police.SuspectStage(offender.Root) == "livre");

            yield return Reset(true); Place(offender, 0, 5); Place(victim, 0, 6.1f); Place(officer, 2.8f, 5);
            ctx.Combat.TryResidentStrike(offender, victim.Root, 18, true); yield return Wait(.5f);
            CheckResult("nearby-officer-pursues-NPC-aggressor", ctx.Police.OfficerTarget(officer) == offender.Root && ctx.Police.Level == 0);
            CheckResult("self-defence-does-not-create-player-charges", ctx.Combat.TryResidentStrike(victim, offender.Root, 12, false) && ctx.Police.Level == 0);
            yield return Wait(1.7f);
            CheckResult("officer-physically-detains-NPC", ctx.Police.IsDetained(offender.Root), ctx.Police.SuspectStage(offender.Root));
            CheckResult("detained-NPC-stops-and-raises-hands", offender.Controlled && offender.Rig.Hands.All(h => offender.Root.InverseTransformPoint(h.position).y > 1.8f));
            CheckResult("detention-prevents-further-NPC-attacks", !ctx.Combat.TryResidentStrike(offender, victim.Root, 18, true));
            Capture("02-NPC-arrest");
            yield return Wait(6.3f);
            CheckResult("NPC-custody-cleans-up-and-resumes-life", ctx.Police.SuspectStage(offender.Root) == "livre" && !offender.Controlled && offender.CrimeCooldown > ctx.Clock);

            yield return Reset(true); Place(offender, 0, 1.1f); Place(officer, 9, 0); offender.Controlled = true;
            ctx.Camera.transform.SetPositionAndRotation(origin + new Vector3(.98f, 2.8f, -2.47f), Quaternion.Euler(18, 0, 0));
            CheckResult("NPC-can-attack-player-with-same-contact-rules", ctx.Combat.TryResidentStrike(offender, ctx.Player, 18, true)); yield return Wait(.45f);
            CheckResult("police-protects-player-victim", ctx.Combat.Health == 82 && ctx.Police.OfficerTarget(officer) == offender.Root && ctx.Police.Level == 0);
            ctx.Input.TouchFire = true; yield return Wait(.25f);
            CheckResult("actual-player-defensive-punch-does-not-create-charges", ctx.Combat.HealthOf(offender.Root) < 100 && ctx.Police.Level == 0, "suspectHealth=" + ctx.Combat.HealthOf(offender.Root));

            yield return Reset(); Place(offender, 0, 5); Place(victim, 0, 6.1f);
            witness.Root.LookAt(new Vector3(victim.Root.position.x, witness.Root.position.y, victim.Root.position.z));
            CheckResult("ambient-entry-starts-NPC-robbery", ctx.Residents.TryStartStreetCrime(offender, victim, true));
            yield return Wait(.35f);
            CheckResult("NPC-robbery-alarms-witnesses-and-identifies-robber", ctx.Police.SuspectStage(offender.Root) == "chamada" && Panics(victim) && Panics(witness),
                "encounter=" + ctx.Residents.StreetCrimeStage + " police=" + ctx.Police.SuspectStage(offender.Root) + " victim=" + victim.Reaction.Kind + " witness=" + witness.Reaction.Kind + " callers=" + subjects.Count(p => p.Calling));
            CheckResult("NPC-robbery-has-facing-and-demand-gesture", Vector3.Dot(offender.Root.forward, CityPhysics.Flat(victim.Root.position - offender.Root.position).normalized) > .6f && offender.Root.InverseTransformPoint(offender.Rig.Hands[1].position).z > .45f);
            Capture("03a-NPC-threat");
            balance = ctx.Commerce.Balance; yield return Wait(1.5f);
            CheckResult("NPC-robber-takes-victim-wallet-and-flees", victim.PocketCash == 0 && offender.PocketCash > 0 && ctx.Residents.StreetCrimeStage == "flee");
            CheckResult("NPC-robbery-does-not-touch-player-wallet", ctx.Commerce.Balance == balance && ctx.Police.Level == 0);
            before = offender.Root.position; yield return Wait(.65f);
            CheckResult("NPC-escape-uses-real-controller", CityPhysics.Flat(offender.Root.position - before).magnitude > .2f && Mathf.Abs(offender.Root.position.y - origin.y) < .15f);
            Capture("03-NPC-robbery");

            yield return Reset(true); Place(offender, 0, 5); Place(victim, 0, 6.1f); Place(officer, 10, 5);
            ctx.Police.Report("agressao", victim.Root.position, victim, false, offender.Root);
            ctx.Police.Report("roubo", ctx.Player.position, bystander, false, ctx.Player);
            CheckResult("simultaneous-crimes-keep-separate-suspects", ctx.Police.ActiveCases == 2 && ctx.Police.Level == 2 && ctx.Police.SuspectStage(offender.Root) != "livre");
            CheckResult("officer-has-only-one-target", ctx.Police.OfficerTarget(officer) == ctx.Player);

            yield return Reset(); Place(offender, 0, 7); Place(witness, 0, 2); victim.Root.gameObject.SetActive(false); bystander.Root.gameObject.SetActive(false);
            CheckResult("quiet-crime-behind-observer-does-not-invent-witness", !ctx.Police.Report("agressao", offender.Root.position, null, false, offender.Root) && witness.Reaction.Kind == CityResidents.ReactionKind.None);

            yield return Reset(); Place(offender, 0, 0); Place(victim, 0, 6); Place(witness, 3, 6); Place(bystander, -3, 6); Wall();
            bool observed = ctx.Police.Report("agressao", offender.Root.position, null, false, offender.Root);
            CheckResult("wall-blocks-quiet-assault-evidence", !observed && ctx.Police.ActiveCases == 0 && !Panics(witness));
            bool heard = ctx.Police.Report("disparo", offender.Root.position, null, true, offender.Root);
            CheckResult("nearby-hidden-shot-produces-location-report", heard && ctx.Police.Calling && ctx.Police.Level == 0);
            CheckResult("hearing-never-blames-nearby-player", subjects.All(p => p.Reaction.MemoryUntil <= ctx.Clock));

            yield return Reset(true); Place(offender, 0, 5); Place(officer, 12, 5);
            ctx.Police.Report("agressao", victim.Root.position, victim, false, offender.Root); yield return Wait(.2f);
            Wall(3); Place(offender, 0, 0); offender.Controlled = true; yield return Wait(3.3f);
            CheckResult("lost-NPC-sight-enters-search", ctx.Police.SuspectStage(offender.Root) == "busca", ctx.Police.SuspectStage(offender.Root));

            yield return Reset(true); Place(officer, 0, 2.6f);
            ctx.Police.Report("roubo", ctx.Player.position, victim, false); yield return Wait(.4f);
            CheckResult("police-approaches-player-and-offers-surrender", ctx.Police.Approaching, ctx.Police.Stage);
            balance = ctx.Commerce.Balance; ctx.Input.TouchInteract = true; yield return Wait(.2f);
            CheckResult("E-surrender-enters-custody", ctx.Police.Custody);
            yield return Wait(2.3f);
            CheckResult("surrender-records-fine-and-releases-controls", ctx.Police.Stage == "livre" && ctx.Commerce.Balance < balance && !ctx.Motor.Busy);
            yield return Reset(true); Place(officer, 0, 1.5f);
            ctx.Police.Report("agressao", ctx.Player.position, victim, false); yield return Wait(4.1f);
            CheckResult("resisting-violent-approach-triggers-real-police-strike", ctx.Combat.Health < 100 && ctx.Police.OfficerTarget(officer) == ctx.Player, "health=" + ctx.Combat.Health);
            yield return CityStreet();
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            Finish();
        }
        bool FreeAt(Vector3 point) => ctx.Free(point, .46f, 2.4f) && ctx.Ground(point.x, point.z) > .2f;
        void WorldPlace(CityResidents.Resident p, Vector3 point)
        {
            Place(p, 0, 0); p.Controller.enabled = false; p.Root.position = point; p.Controller.enabled = true;
            p.LastPosition = p.Home = point;
        }
        IEnumerator CityStreet()
        {
            ctx.Residents.EndStreetCrime(); ctx.Police.Clear(); ctx.Combat.Holster();
            foreach (var p in subjects) if (ctx.Residents.IsSpawned(p.Data.id)) ctx.Residents.Despawn(p);
            Destroy(floor); floor = null; if (wall) { Destroy(wall); wall = null; }
            ctx.Motor.Warp(citySpawn); yield return Wait(.1f);
            Vector3 spot = Vector3.zero, forward = Vector3.forward; bool found = false;
            foreach (var node in ctx.Data.nav.OrderBy(n => (new Vector3(n.x, citySpawn.y, n.z) - citySpawn).sqrMagnitude))
            {
                if (node.edges.Length == 0 || Vector2.Distance(new Vector2(node.x, node.z), new Vector2(citySpawn.x, citySpawn.z)) < 10) continue;
                var point = new Vector3(node.x, ctx.Ground(node.x, node.z) + .04f, node.z);
                foreach (var direction in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                {
                    var target = point + direction * 1.12f; target.y = ctx.Ground(target.x, target.z) + .04f;
                    var observer = point + Vector3.Cross(Vector3.up, direction) * 4; observer.y = ctx.Ground(observer.x, observer.z) + .04f;
                    if (Mathf.Abs(target.y - point.y) > .15f || Mathf.Abs(observer.y - point.y) > .15f || !FreeAt(point) || !FreeAt(target) || !FreeAt(observer) ||
                        !ctx.Visible(point + Vector3.up * 1.4f, target + Vector3.up * 1.4f) || !ctx.Visible(observer + Vector3.up * 1.4f, point + Vector3.up * 1.4f)) continue;
                    spot = point; forward = direction; found = true; break;
                }
                if (found) break;
            }
            CheckResult("real-city-has-clear-crime-and-witness-space", found, spot.ToString()); if (!found) yield break;
            ctx.Residents.Spawn(offender.Data.id, spot, Quaternion.LookRotation(forward));
            ctx.Residents.Spawn(victim.Data.id, spot + forward * 1.12f, Quaternion.LookRotation(-forward));
            ctx.Residents.Spawn(witness.Data.id, spot + Vector3.Cross(Vector3.up, forward) * 4, Quaternion.LookRotation(-Vector3.Cross(Vector3.up, forward)));
            WorldPlace(offender, spot); WorldPlace(victim, spot + forward * 1.12f); WorldPlace(witness, spot + Vector3.Cross(Vector3.up, forward) * 4);
            offender.Root.rotation = Quaternion.LookRotation(forward); victim.Root.rotation = Quaternion.LookRotation(-forward);
            witness.Root.LookAt(new Vector3(spot.x, witness.Root.position.y, spot.z));
            Vector3 playerSpot = spot - forward * 6; playerSpot.y = ctx.Ground(playerSpot.x, playerSpot.z) + .04f;
            if (FreeAt(playerSpot)) ctx.Motor.Warp(playerSpot);
            ctx.Camera.transform.position = spot - forward * 6 + Vector3.up * 3.5f;
            ctx.Camera.transform.LookAt(spot + Vector3.up * 1.3f); Physics.SyncTransforms(); yield return Wait(.15f);
            CheckResult("real-city-NPC-assault-starts", ctx.Combat.TryResidentStrike(offender, victim.Root, 18, true)); yield return Wait(.5f);
            CheckResult("real-city-assault-calls-police", ctx.Police.Calling && ctx.Police.Level == 0 && ctx.Combat.HealthOf(victim.Root) < 100);
            // Freeze routine population while allowing production responder spawn.
            ctx.Population.enabled = true;
            yield return Wait(5);
            var response = ctx.Residents.People.Where(p => ctx.Police.OfficerTarget(p) == offender.Root).ToArray();
            CheckResult("reinforcements-spawn-and-target-actual-NPC", response.Length > 0 && ctx.Residents.People.Count <= CityPopulation.ResidentLimit, "responders=" + response.Length);
            var positions = response.Select(p => p.Root.position).ToArray(); yield return Wait(1.5f);
            CheckResult("reinforcements-move-through-current-city", response.Length > 0 && response.Where((p, i) => Vector3.Distance(positions[i], p.Root.position) > .2f).Any());
            Capture("04-current-city-response");
            ctx.Population.enabled = false;
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            var oldTarget = ctx.Camera.targetTexture; var oldActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1280, 800, 24); var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                foreach (var canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ctx.Camera; canvas.planeDistance = 1; }
                Canvas.ForceUpdateCanvases();
                ctx.Camera.targetTexture = target; ctx.Camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                ctx.Camera.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); Destroy(texture);
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i]; }
            }
        }
        void Finish()
        {
            report.completed = true; report.passed &= report.errors.Count == 0;
            File.WriteAllText(Path.Combine(directory, "crime-response-qa.json"), JsonUtility.ToJson(report, true));
            if (ctx) { ctx.UI.SetSettings(false); ctx.Residents.EndStreetCrime(); ctx.Police.Clear(); }
            Debug.Log("CRIME_QA_FINISHED " + report.passed + " checks=" + report.checks.Count); enabled = false;
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
