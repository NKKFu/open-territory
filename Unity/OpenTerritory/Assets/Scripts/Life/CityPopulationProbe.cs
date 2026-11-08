using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Installed only with --city-population-qa or explicitly on a QA Editor run.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityPopulationProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report
        {
            public bool passed = true; public int residentSpawns, vehicleSpawns, residentRetirements, vehicleRetirements;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; string dir; bool badRange, badBody, visibleSpawn, checkingReuse, finished; int maxPeople, maxCars;
        public bool QuitWhenFinished = true;
        public string ReportDirectory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-population-qa") >= 0)
                new GameObject("QA de população • opt-in").AddComponent<CityPopulationProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + " " + trace); }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (ctx && ctx.Residents) ctx.Residents.Spawned -= PersonSpawned;
            if (ctx && ctx.Vehicles) ctx.Vehicles.Spawned -= CarSpawned;
        }
        void Update()
        {
            if (!ctx || finished) return;
            maxPeople = Mathf.Max(maxPeople, ctx.Residents.People.Count); maxCars = Mathf.Max(maxCars, ctx.Vehicles.Fleet.Count);
        }
        void PersonSpawned(CityResidents.Resident p)
        {
            if (checkingReuse) return;
            float distance = CityPhysics.Flat(p.Root.position - ctx.Player.position).magnitude;
            badRange |= distance > CityPopulation.ResidentRadius + .1f || distance < 7.9f;
            badBody |= !p.Root.gameObject.activeInHierarchy || !p.Controller || !p.Controller.enabled || p.Rig == null;
            InspectVisibility(p.Root);
        }
        void CarSpawned(CityVehicles.Vehicle car)
        {
            if (checkingReuse) return;
            float distance = CityPhysics.Flat(car.Position - ctx.Player.position).magnitude;
            badRange |= distance > CityPopulation.VehicleRadius + .1f || distance < (car.Data.auto ? 54.9f : 11.9f);
            badBody |= !car.Root.gameObject.activeInHierarchy || !car.Body || car.Root.GetComponents<Rigidbody>().Length != 1;
            InspectVisibility(car.Root);
        }
        void InspectVisibility(Transform root)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(ctx.Camera);
            int mask = (1 << CityContext.WorldLayer) | (1 << CityContext.GroundLayer);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && GeometryUtility.TestPlanesAABB(planes, renderer.bounds) &&
                    !Physics.Linecast(ctx.Camera.transform.position, renderer.bounds.center, mask, QueryTriggerInteraction.Ignore)) visibleSpawn = true;
        }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("POPULATION_QA " + name + " " + passed + " " + detail);
        }
        void CheckPoolReuse()
        {
            // Exercise warm bodies directly as well as the preceding geographic
            // streaming test, without depending on the random model selection.
            var p = ctx.Residents.People.First(a => a.Data.id > 0 && !a.Down && !a.Controlled && a.Bus == null);
            var car = ctx.Vehicles.Fleet.First(a => a != ctx.Vehicles.Active && !a.TrafficHold);
            var root = p.Root; var controller = p.Controller; var rig = p.Rig; var body = car.Body; var engine = car.Engine;
            Vector3 point = root.position, carPoint = car.Position; Quaternion rotation = root.rotation, carRotation = car.Root.rotation;
            int actions = ctx.Actions.Count;
            checkingReuse = true;
            ctx.Residents.Despawn(p); ctx.Vehicles.Despawn(car);
            bool dormant = !root.gameObject.activeInHierarchy && !controller.enabled && !car.Root.gameObject.activeInHierarchy;
            ctx.Residents.Spawn(p.Data.id, point, rotation); ctx.Vehicles.Spawn(car.Data, carPoint, carRotation);
            checkingReuse = false;
            CheckResult("pool-disables-dormant-actors-and-reuses-rig-body-audio", dormant && p.Root == root && p.Controller == controller && p.Rig == rig &&
                car.Body == body && car.Engine == engine && root.GetComponents<CharacterController>().Length == 1 && ctx.Actions.Count == actions);
        }
        IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--city-report-dir");
            dir = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "population-qa");
            Directory.CreateDirectory(dir);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance;
            ctx.Residents.Spawned += PersonSpawned; ctx.Vehicles.Spawned += CarSpawned;
            CheckResult("starts-without-citywide-population", ctx.Residents.People.Count == 0 && ctx.Vehicles.Fleet.Count == 0);
            yield return Wait(8);
            CheckResult("nearby-population-arrives-gradually", ctx.Residents.People.Count >= 8 && ctx.Vehicles.Fleet.Count >= 3,
                "people=" + ctx.Residents.People.Count + " cars=" + ctx.Vehicles.Fleet.Count);
            CheckResult("new-vehicles-have-engine-audio-and-actions", ctx.Vehicles.Fleet.All(c => c.Engine && ctx.Actions.Count(a => a.id == "vehicle-" + c.Data.id) == 1));
            var originalPeople = ctx.Residents.People.ToArray(); var originalCars = ctx.Vehicles.Fleet.ToArray();
            var originalPosition = ctx.Player.position;
            var person = originalPeople.FirstOrDefault(p => !p.Data.sitting && !p.Data.stationary);
            var car = originalCars.FirstOrDefault();
            if (person != null) ctx.Residents.Talk(person);
            CheckResult("spawned-resident-retains-world-dialogue", person != null && ctx.Speech.Contains(person.Data.name));
            if (person != null) { ctx.Residents.Control(person, true); person.Calling = true; }
            if (car != null) ctx.Vehicles.SetTrafficHold(car, true, "QA de retenção");
            int count = ctx.Population.ResidentSpawns + ctx.Population.VehicleSpawns; float clock = ctx.Clock;
            ctx.UI.SetSettings(true); yield return Wait(.75f);
            CheckResult("Esc-pauses-streaming-and-clock", ctx.Clock == clock && count == ctx.Population.ResidentSpawns + ctx.Population.VehicleSpawns && ctx.Paused);
            ctx.UI.SetSettings(false);
            var destination = ctx.Data.roadRoute.OrderByDescending(p => CityPhysics.Flat(p - originalPosition).sqrMagnitude).First();
            destination.y = ctx.Ground(destination.x, destination.z) + .05f;
            ctx.Motor.Warp(destination); ctx.Follow.Face(110); yield return Wait(9);
            CheckResult("distant-pool-is-retired-and-replaced", ctx.Population.ResidentRetirements > 0 && ctx.Population.VehicleRetirements > 0 &&
                ctx.Residents.People.Any(p => !originalPeople.Contains(p)) && ctx.Vehicles.Fleet.Any(c => !originalCars.Contains(c)),
                "clock=" + ctx.Clock + " retired people=" + ctx.Population.ResidentRetirements + " cars=" + ctx.Population.VehicleRetirements + " current cars=" +
                string.Join(";", ctx.Vehicles.Fleet.Select(c => c.Data.id + ":distance=" + CityPhysics.Flat(c.Position - ctx.Player.position).magnitude.ToString("F0") + ":impact=" + c.LastImpact.ToString("F1"))));
            CheckResult("witness-and-driver-incident-stay-present", person != null && car != null && ctx.Residents.People.Contains(person) && ctx.Vehicles.Fleet.Contains(car));
            if (person != null) { person.Calling = false; ctx.Residents.Control(person, false); }
            if (car != null) ctx.Vehicles.SetTrafficHold(car, false);
            ctx.Motor.Warp(originalPosition); ctx.Follow.Face(180); yield return Wait(11);
            CheckResult("returning-repopulates-the-original-neighborhood", ctx.Residents.People.Count >= 8 && ctx.Vehicles.Fleet.Count >= 3);
            CheckResult("musician-keeps-authored-performance-and-guitar", ctx.Residents.IsSpawned(ctx.Data.music.musician));
            CheckPoolReuse();
            var seated = ctx.Data.residents.First(p => p.sitting && p.indoor);
            var staff = ctx.Data.residents.Where(p => p.indoor && !p.sitting).OrderBy(p => CityPhysics.Flat(p.position - seated.position).sqrMagnitude).First();
            var approach = seated.position - Vector3.forward * 45; approach.y = ctx.Ground(approach.x, approach.z) + .05f;
            ctx.Motor.Warp(approach); ctx.Follow.Face(180); yield return Wait(8);
            CheckResult("seated-resident-and-shopkeeper-spawn-in-their-own-places", ctx.Residents.IsSpawned(seated.id) && ctx.Residents.IsSpawned(staff.id),
                "seated=" + ctx.Residents.IsSpawned(seated.id) + " staff=" + ctx.Residents.IsSpawned(staff.id));
            CheckResult("spawns-respect-player-distance", !badRange);
            CheckResult("spawned-meshes-are-out-of-sight", !visibleSpawn);
            CheckResult("spawned-actors-have-one-physical-body", !badBody && ctx.Residents.People.All(p => p.Root.GetComponents<CharacterController>().Length == 1));
            CheckResult("population-stays-bounded", maxPeople <= CityPopulation.ResidentLimit && maxCars <= CityPopulation.VehicleLimit, "maximum people=" + maxPeople + " cars=" + maxCars);
            CheckResult("pool-does-not-duplicate-actions-or-audio", ctx.Actions.GroupBy(a => a.id).All(g => g.Count() == 1) &&
                ctx.Vehicles.Fleet.All(c => c.Root.GetComponentsInChildren<AudioSource>(true).Count(s => s.name == "Som • tire") == 1));
            CheckResult("only-requested-QA-probe-is-installed", FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(p => !p.GetType().Name.EndsWith("Probe", StringComparison.Ordinal) || p == this));
            CheckResult("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void Finish()
        {
            finished = true; report.residentSpawns = ctx.Population.ResidentSpawns; report.vehicleSpawns = ctx.Population.VehicleSpawns;
            report.residentRetirements = ctx.Population.ResidentRetirements; report.vehicleRetirements = ctx.Population.VehicleRetirements;
            File.WriteAllText(Path.Combine(dir, "population-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("POPULATION_QA_FINISHED " + report.passed);
            if (QuitWhenFinished) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
