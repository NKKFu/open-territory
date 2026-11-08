using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Explicit opt-in. Never installed in an ordinary game.
    public sealed partial class CityVehicleSafetyProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Sample { public string scenario, reason; public float time, speed, target, gap; public bool braking; }
        [Serializable] public class Report
        {
            public bool passed = true; public string unity;
            public List<Check> checks = new List<Check>(); public List<Sample> samples = new List<Sample>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report(); CityContext ctx; CityVehicles.Vehicle car; CityResidents.Resident person;
        string dir; float started; bool finished; Vector3 pedestrianVelocity; int crimes;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-vehicle-safety-qa") >= 0) new GameObject("Vehicle safety QA opt-in").AddComponent<CityVehicleSafetyProbe>(); }
        void Awake() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool pass, string detail = "")
        { report.passed &= pass; report.checks.Add(new Check { name = name, passed = pass, detail = detail }); Debug.Log("VEHICLE_SAFETY_QA " + name + " " + pass + " " + detail); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - started > 330) { CheckResult("completed-within-timeout", false); StopAllCoroutines(); Finish(); return; }
            if (person != null && person.Root && person.Controller && person.Controller.enabled && !ctx.Paused)
            { person.Velocity = pedestrianVelocity; person.Controller.Move(pedestrianVelocity * Time.deltaTime + Vector3.down * Time.deltaTime * 2); }
        }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator PressE() { ctx.Input.TouchInteract = true; yield return Frame(); }
        IEnumerator WaitForEntry()
        {
            float until = Time.time + 7;
            while (Time.time < until && (ctx.Vehicles.RemovingDriver || ctx.Motor.Busy)) yield return null;
            yield return Frame();
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/vehicle-safety/native"));
            Directory.CreateDirectory(dir);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; report.unity = Application.unityVersion;
            ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.Residents.enabled = false; ctx.Combat.Holster();
            ctx.Pause(false); ctx.Motor.SetRide(null); ctx.Motor.SetInputLock(false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA isolated vehicle floor";
            floor.layer = CityContext.GroundLayer; floor.transform.position = new Vector3(0, 199, 35); floor.transform.localScale = new Vector3(100, 2, 180);
            var data = ctx.Data.vehicles.Where(v => v.auto && v.kind == "car" && string.IsNullOrEmpty(v.role)).OrderByDescending(v => v.width).First();
            car = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == data.id) ?? ctx.Vehicles.Spawn(data, new Vector3(0, 200.1f, 0), Quaternion.identity);
            car.Route = new[] { new Vector3(0, 200, -40), new Vector3(0, 200, 110), new Vector3(35, 200, 110), new Vector3(35, 200, -40) };
            car.Cumulative = new float[car.Route.Length + 1];
            for (int i = 0; i < car.Route.Length; i++) car.Cumulative[i + 1] = car.Cumulative[i] + Vector3.Distance(car.Route[i], car.Route[(i + 1) % car.Route.Length]);
            car.RouteLength = car.Cumulative[car.Route.Length]; car.StopProgress = new float[ctx.Data.stops.Length];
            ctx.Vehicles.Crime += Crime; ctx.Motor.Warp(new Vector3(-12, 200.04f, 0));
            yield return new WaitForSeconds(.3f);
            bool trafficOnly=Array.IndexOf(args,"--city-vehicle-traffic-only")>=0;
            if(!trafficOnly)yield return EntryChecks();
            var definition = ctx.Data.residents.First(p => !p.child && !p.sitting && !p.performance && string.IsNullOrEmpty(p.role));
            person = ctx.Residents.Find(definition.id);
            if (!ctx.Residents.IsSpawned(definition.id)) ctx.Residents.Spawn(definition.id, new Vector3(15, 200.04f, 15), Quaternion.identity);
            ctx.Residents.PrepareBody(person); person.Controlled = true;
            if(trafficOnly)
            {
                yield return TrafficChecks();
                CheckResult("no-runtime-errors",report.errors.Count==0,string.Join("\n",report.errors));Finish();yield break;
            }
            yield return StopForPerson("player-centre", true, 0, false, false);
            yield return StopForPerson("player-bumper-edge", true, car.Data.width * .5f + .12f, false, false);
            yield return StopForPerson("resident-centre", false, 0, false, false);
            yield return StopForPerson("resident-crossing", false, 4.0f, true, false);
            yield return StopForPerson("wet-road", true, 0, false, true);
            yield return StopForPerson("fallen-resident", false, 0, false, false, true);
            yield return CurveCheck();
            yield return FreeRoadChecks();
            yield return TrafficChecks();
            yield return IncidentTakeover();
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            Finish();
        }
        void Crime(string kind, Vector3 spot) { if (kind == "furto" || kind == "furto-policial") crimes++; }
        void PlaceCar(float z = 0, float speed = 0, bool auto = false)
        {
            car.Data.auto = auto; car.Occupied = auto; car.Commanded = car.Controlled = car.Passenger = car.ExitRequested = false;
            car.Traffic = new CityTrafficPolicy.State(); car.TrafficHold = false; car.CruiseSpeed = 8; car.Acceleration = 1.7f; car.Headway = .8f;
            car.LaneBias = car.LaneAmplitude = car.LaneOffset = 0; car.RemoteUntil = car.JamUntil = 0;
            car.Root.SetPositionAndRotation(new Vector3(0, 200.10f, z), Quaternion.identity); car.Body.position = car.Root.position; car.Body.rotation = Quaternion.identity;
            car.Body.linearVelocity = Vector3.forward * speed; car.Body.angularVelocity = Vector3.zero;
            car.Motion = new CityVehicleMath.State { Speed = speed, Direction = 1, Gear = 1 }; car.TargetSpeed = speed;
            car.Progress = ctx.Vehicles.RouteDistance(car, car.Position); if (car.Driver) car.Driver.gameObject.SetActive(auto);
            car.Body.WakeUp(); Physics.SyncTransforms();
        }
        void ActorAt(Vector3 feet)
        {
            bool on = person.Controller.enabled; person.Controller.enabled = false; person.Root.position = feet;
            person.Controller.enabled = on; person.LastPosition = feet; person.Velocity = pedestrianVelocity = Vector3.zero;
        }
        void Approach(int side)
        {
            var feet = car.Root.TransformPoint(new Vector3(side * (car.Data.width * .5f + .9f), 0, car.Data.seatMount.z)); feet.y = 200.04f;
            ctx.Motor.Warp(feet); ctx.Motor.SetInputLock(false);
            var a = ctx.Actions.Find(x => x.id == "vehicle-" + car.Data.id);
            ctx.Camera.transform.position = feet + new Vector3(side * 2, 2.4f, 2); ctx.Camera.transform.LookAt(a.LookPoint); ctx.RefreshAction();
        }
        IEnumerator EntryChecks()
        {
            foreach (int side in new[] { -1, 1 })
            {
                PlaceCar(); car.Occupied = true; if (car.Driver) car.Driver.gameObject.SetActive(true); car.JamUntil = ctx.Clock + 4; car.RemoteUntil = ctx.Clock + 3;
                Approach(side); yield return Frame();
                CheckResult("occupied-car-selectable-side-" + side, ctx.CurrentAction?.id == "vehicle-" + car.Data.id && ctx.CurrentAction.Text == "Steal vehicle", "width=" + car.Data.width);
                int before = crimes; Capture("entry-side-" + side); yield return PressE(); yield return WaitForEntry();
                CheckResult("E-takes-control-once-side-" + side, ctx.Vehicles.Active == car && !ctx.Motor.Busy && !car.Occupied && crimes == before + 1);
                CheckResult("takeover-clears-remote-brakes-side-" + side, car.RemoteUntil == 0 && car.JamUntil == 0);
                ctx.Input.TouchMove = Vector2.up; yield return new WaitForSeconds(.65f); ctx.Input.TouchMove = Vector2.zero;
                CheckResult("stolen-car-drives-side-" + side, car.Speed > .4f, car.Speed.ToString("F2"));
                yield return PressE(); yield return new WaitForSeconds(2);
                CheckResult("E-brakes-and-exits-side-" + side, ctx.Vehicles.Active == null && !ctx.Motor.Riding && !ctx.Motor.Busy && ctx.Motor.Controller.enabled);
                int after = crimes; Approach(side); yield return Frame(); string reentrySelection = ctx.CurrentAction?.id;
                yield return PressE(); yield return WaitForEntry();
                CheckResult("reentry-does-not-report-theft-again-side-" + side, ctx.Vehicles.Active == car && crimes == after,
                    "selected=" + reentrySelection + "; active=" + ctx.Vehicles.Active?.Data.id + "; crimes=" + after + "->" + crimes);
                yield return PressE(); yield return new WaitForSeconds(1.2f);
            }
            PlaceCar(); Approach(1); car.Body.linearVelocity = car.Root.right * 2; ctx.RefreshAction();
            CheckResult("sliding-car-cannot-be-entered", ctx.CurrentAction?.id != "vehicle-" + car.Data.id);
            PlaceCar(); Approach(1); car.Body.linearVelocity = car.Root.forward * 6; ctx.RefreshAction();
            CheckResult("fast-car-cannot-be-entered", ctx.CurrentAction?.id != "vehicle-" + car.Data.id);
            PlaceCar(); Approach(1); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(car.Data.width * .5f + .65f, 201, car.Data.seatMount.z); wall.transform.localScale = new Vector3(.12f, 2, 5);
            Physics.SyncTransforms(); ctx.RefreshAction(); CheckResult("wall-blocks-entry", ctx.CurrentAction?.id != "vehicle-" + car.Data.id);
            Destroy(wall); yield return Frame(); Approach(1); ctx.Motor.Warp(ctx.Player.position + Vector3.right * 8); ctx.RefreshAction();
            CheckResult("entry-requires-character-reach", ctx.CurrentAction?.id != "vehicle-" + car.Data.id);
        }
        IEnumerator StopForPerson(string name, bool player, float x, bool crossing, bool wet, bool down = false)
        {
            ctx.Motor.SetInputLock(true); ctx.Wetness = wet ? 1 : 0; person.Down = down;
            ctx.Motor.Warp(new Vector3(player ? x : -12, 200.04f, 14));
            ActorAt(new Vector3(player ? 15 : x, 200.04f, 14));
            PlaceCar(-13, 8, true); if (crossing) pedestrianVelocity = Vector3.left * 1.8f;
            float minimum = float.PositiveInfinity, next = 0, start = Time.time; bool braked = false, stopped = false, predicted = false, crossedClear = false;
            ctx.Camera.transform.position = new Vector3(10, 207, -3); ctx.Camera.transform.LookAt(new Vector3(0, 201, 8));
            while (Time.time - start < 6)
            {
                float feetZ = player ? ctx.Player.position.z : person.Root.position.z;
                float gap = feetZ - car.Position.z - car.Data.length * .5f - car.Data.centerOffset - .4f;
                minimum = Mathf.Min(minimum, gap); braked |= car.Motion.Braking;
                if (crossing && car.Motion.Braking && Mathf.Abs(car.Root.InverseTransformPoint(person.Root.position).x) > car.Data.width * .5f + .5f) predicted = true;
                if (Time.time >= next) { next = Time.time + .15f; report.samples.Add(new Sample { scenario = name, time = Time.time - start, speed = car.Speed, target = car.TargetSpeed, gap = gap, reason = car.StoppedReason, braking = car.Motion.Braking }); }
                if (Mathf.Abs(car.Speed) < .12f && braked) { stopped = true; break; }
                if (crossing && person.Root.position.x < -car.Data.width * .5f - .7f) { crossedClear = true; break; }
                yield return null;
            }
            pedestrianVelocity = Vector3.zero;
            CheckResult(name + "-brakes-before-contact", braked && (stopped || crossedClear) && minimum > .25f, "minimum gap=" + minimum.ToString("F2") + "; speed=" + car.Speed.ToString("F2") + "; crossed clear=" + crossedClear);
            if (crossing) CheckResult("crossing-brakes-before-person-enters-lane", predicted);
            Capture(name); person.Down = false;
            ctx.Motor.Warp(new Vector3(-12, 200.04f, 14)); ActorAt(new Vector3(15, 200.04f, 14));
            // Includes the driver's continuous-clear interval before take-off.
            yield return new WaitForSeconds(2.5f);
            CheckResult(name + "-resumes-after-clear", car.Speed > .35f, car.Speed.ToString("F2"));
        }
        IEnumerator CurveCheck()
        {
            var savedRoute = car.Route; var savedDistances = car.Cumulative; float savedLength = car.RouteLength;
            var route = new List<Vector3> { new Vector3(0, 200, -40), new Vector3(0, 200, 0) };
            for (int i = 1; i <= 12; i++)
            { float a = i * Mathf.PI / 24; route.Add(new Vector3(12 * (1 - Mathf.Cos(a)), 200, 12 * Mathf.Sin(a))); }
            route.Add(new Vector3(40, 200, 12)); route.Add(new Vector3(40, 200, -40)); car.Route = route.ToArray();
            car.Cumulative = new float[car.Route.Length + 1];
            for (int i = 0; i < car.Route.Length; i++) car.Cumulative[i + 1] = car.Cumulative[i] + Vector3.Distance(car.Route[i], car.Route[(i + 1) % car.Route.Length]);
            car.RouteLength = car.Cumulative[car.Route.Length];
            ActorAt(new Vector3(12 * (1 - Mathf.Cos(.8f)), 200.04f, 12 * Mathf.Sin(.8f)));
            ctx.Motor.Warp(new Vector3(-12, 200.04f, 12)); PlaceCar(-8, 6, true);
            float minimum = float.PositiveInfinity, start = Time.time; bool braked = false, stopped = false;
            while (Time.time - start < 8)
            {
                var local = car.Root.InverseTransformPoint(person.Root.position);
                float x = Mathf.Max(0, Mathf.Abs(local.x) - car.Data.width * .5f), z = Mathf.Max(0, Mathf.Abs(local.z - car.Data.centerOffset) - car.Data.length * .5f);
                minimum = Mathf.Min(minimum, new Vector2(x, z).magnitude - .4f); braked |= car.Motion.Braking;
                if (car.Speed < .12f && braked) { stopped = true; break; } yield return null;
            }
            CheckResult("curve-swept-body-stops-before-person", stopped && minimum > .25f, "minimum body clearance=" + minimum.ToString("F2") + "; speed=" + car.Speed.ToString("F2"));
            Capture("curve-pedestrian"); car.Route = savedRoute; car.Cumulative = savedDistances; car.RouteLength = savedLength;
        }
        IEnumerator FreeRoadChecks()
        {
            ctx.Wetness = 0; ActorAt(new Vector3(4, 200.04f, 12)); ctx.Motor.Warp(new Vector3(-12, 200.04f, 12));
            PlaceCar(-13, 6, true); yield return new WaitForSeconds(1);
            CheckResult("person-on-pavement-does-not-stop-traffic", car.Speed > 5.5f, car.Speed.ToString("F2"));
            ActorAt(new Vector3(0, 208, 12)); PlaceCar(-13, 6, true); yield return new WaitForSeconds(.5f);
            CheckResult("person-on-another-level-is-not-in-lane", car.TargetSpeed > 5.5f, car.TargetSpeed.ToString("F2"));
            ActorAt(new Vector3(15, 200.04f, 15)); PlaceCar(-13, 6, true);
            var position = car.Position; var clock = ctx.Clock; ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.35f);
            CheckResult("Esc-still-pauses-traffic", ctx.Paused && ctx.Clock == clock && Vector3.Distance(position, car.Position) < .02f);
            ctx.UI.SetSettings(false); yield return Frame();
            PlaceCar(); car.Data.auto = true; car.TrafficHold = true; car.TrafficHoldReason = "qa";
        }
        IEnumerator IncidentTakeover()
        {
            // Each theft now leaves a real person behind. Earlier scenarios
            // freeze residents; move only those fixture owners to a separate
            // part of the QA floor so they do not block both inspection paths.
            int oldDriver = 0;
            foreach (var p in ctx.Residents.People.Where(p => p.Data.id == -car.Data.id))
            {
                bool enabled = p.Controller.enabled; p.Controller.enabled = false;
                p.Root.position = new Vector3(18, 200.04f, 5 + oldDriver++ * 2); p.LastPosition = p.Root.position; p.Velocity = Vector3.zero;
                p.Controller.enabled = enabled;
            }
            ctx.Motor.SetInputLock(false); ActorAt(new Vector3(15, 200.04f, 15)); PlaceCar(); car.Data.auto = car.Occupied = true;
            var begin = typeof(CityDriverIncidents).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic);
            bool began = (bool)begin.Invoke(ctx.Incidents, new object[] { car, 5f });
            float until = Time.time + 10;
            while (Time.time < until && !ctx.Incidents.CanTakeover(car)) yield return null;
            var incident = ctx.Incidents.Get(car); var driver = incident?.Actor;
            CheckResult("incident-driver-can-leave-car", began && driver != null && ctx.Incidents.CanTakeover(car), incident?.State + "; wait=" + incident?.WaitReason);
            if (driver != null)
            {
                Approach(-incident.Side); yield return Frame();
                CheckResult("car-with-driver-outside-can-be-taken", ctx.CurrentAction?.id == "vehicle-" + car.Data.id);
                yield return PressE(); yield return new WaitForSeconds(1.3f);
                CheckResult("takeover-releases-incident-and-keeps-owner", ctx.Vehicles.Active == car && ctx.Incidents.Get(car) == null && !car.TrafficHold && ctx.Residents.People.Contains(driver) && driver.Root.gameObject.activeInHierarchy);
                yield return new WaitForSeconds(5);
                CheckResult("former-owner-does-not-reclaim-controlled-car", ctx.Vehicles.Active == car && ctx.Incidents.Get(car) == null && !car.Occupied);
            }
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = ctx.Camera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var modes = overlays.Select(c => c.renderMode).ToArray(); var cameras = overlays.Select(c => c.worldCamera).ToArray(); var distances = overlays.Select(c => c.planeDistance).ToArray();
            var target = RenderTexture.GetTemporary(1280, 800, 24); Texture2D picture = null;
            try
            {
                camera.targetTexture = target;
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = camera; c.planeDistance = .3f; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(1280, 800, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = modes[i]; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = distances[i]; }
                RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture);
            }
        }
        void Finish()
        {
            finished = true;
            if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchInteract = false; ctx.UI.SetSettings(false); ctx.Vehicles.Crime -= Crime; }
            report.passed &= report.errors.Count == 0;
            File.WriteAllText(Path.Combine(dir, "vehicle-safety-qa.json"), JsonUtility.ToJson(report, true)); Debug.Log("VEHICLE_SAFETY_QA_DONE " + report.passed);
            if (Application.isEditor) { if (ctx) ctx.Pause(true); } else Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (ctx && ctx.Vehicles) ctx.Vehicles.Crime -= Crime; }
    }
}
