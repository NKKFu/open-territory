using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityDriverIncidentProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample { public float elapsed, speed, angular; public string state, wait; public Vector3 position; public bool hold, hazards; }
        [Serializable] sealed class Report
        {
            public string suite = "native-driver-incident", unity, startedUtc;
            public bool passed; public float seconds, impactSpeed, distanceWalked;
            public List<Check> checks = new List<Check>(); public List<string> stages = new List<string>(), errors = new List<string>();
            public List<Sample> samples = new List<Sample>();
        }
        readonly Report report = new Report(); CityContext ctx; string output; float began;
        Vector2 move; bool brake, interact, finished; GameObject obstruction;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-incident-qa") >= 0) new GameObject("Verificação nativa do motorista após batida").AddComponent<CityDriverIncidentProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType kind)
        { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void Update()
        {
            if (!ctx || finished) return; ctx.Input.TouchMove = move; ctx.Input.TouchBrake = brake;
            if (interact) { ctx.Input.TouchInteract = true; interact = false; }
        }
        void CheckResult(string name, bool passed, string detail)
        { report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("[CityIncidentQA] " + (passed ? "PASS " : "FAIL ") + name + " · " + detail); }
        IEnumerator Start()
        {
            began = Time.realtimeSinceStartup; report.unity = Application.unityVersion; report.startedUtc = DateTime.UtcNow.ToString("o");
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Reports"));
            Directory.CreateDirectory(output); var run = Run();
            while (true)
            {
                object next = null; bool done = false;
                try { if (run.MoveNext()) next = run.Current; else done = true; }
                catch (Exception error) { CheckResult("unexpected-exception", false, error.ToString()); done = true; }
                if (done) break; yield return next;
            }
            finished = true; if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = false; }
            if (obstruction) Destroy(obstruction);
            CheckResult("no-runtime-errors", report.errors.Count == 0, report.errors.Count + " errors");
            report.seconds = Time.realtimeSinceStartup - began; report.passed = report.checks.Count > 0 && report.checks.TrueForAll(c => c.passed);
            File.WriteAllText(Path.Combine(output, "incident-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[CityIncidentQA] Finished " + report.passed + " -> " + output); Application.Quit(report.passed ? 0 : 2);
        }
        void PlaceVehicle(CityVehicles.Vehicle car, float x, float z)
        {
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.Body.position = new Vector3(x, ctx.Ground(x, z) + .09f, z); car.Body.rotation = Quaternion.identity; car.TargetSpeed = 0;
            car.Motion.Speed = car.Motion.Lateral = car.Motion.YawRate = 0; car.Body.WakeUp(); Physics.SyncTransforms();
        }
        IEnumerator Run()
        {
            float until = Time.realtimeSinceStartup + 80;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < until) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready || !ctx.Incidents) { CheckResult("scene-and-incidents-ready", false, "Native module not initialized."); yield break; }
            ctx.Pause(false); yield return new WaitForSeconds(.6f);
            CityVehicles.Vehicle playerCar = null, target = null;
            foreach (var car in ctx.Vehicles.Fleet)
            {
                if (playerCar == null && !car.Data.auto && car.Data.kind == "car" && car.Position.x < -145 && car.Position.z < -90) playerCar = car;
                if (target == null && car.Data.auto && car.Data.kind == "car" && string.IsNullOrEmpty(car.Data.role) && car.Data.length < 5.2f) target = car;
            }
            if (playerCar == null || target == null) { CheckResult("fixture-uses-existing-fleet", false, "Two existing civilian cars required."); yield break; }
            int residents = ctx.Residents.People.Count, fleet = ctx.Vehicles.Fleet.Count, completed = ctx.Incidents.CompletedCount;
            var route = target.Route; var originalHead = target.Driver.GetComponentsInChildren<Transform>(true);
            ctx.Vehicles.SetTrafficHold(target, true, "preparação-qa"); PlaceVehicle(target, -174, -94); PlaceVehicle(playerCar, -174, -108);
            // The left shoulder is deliberately occupied. The other side uses
            // the imported parking ground, with no synthetic floor or exit.
            obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube); obstruction.name = "Veículo de serviço ocupando o lado esquerdo (somente QA)";
            obstruction.layer = CityContext.WorldLayer; obstruction.transform.position = new Vector3(-176.8f, ctx.Ground(-176.8f, -92) + 1.05f, -92);
            obstruction.transform.localScale = new Vector3(.8f, 2.1f, 12);
            // PhysX bodies interpolate their visual transforms. Allow the
            // relocated fixture to settle before deriving its world-space door.
            yield return new WaitForSeconds(.5f);
            var entrance = playerCar.Root.TransformPoint(new Vector3(-playerCar.Data.width * .5f - .67f, 0, playerCar.Data.seatMount.z));
            entrance.y = ctx.Ground(entrance.x, entrance.z) + .045f; ctx.Motor.Warp(entrance); ctx.Player.rotation = Quaternion.identity;
            yield return new WaitForSeconds(.8f);
            CheckResult("normal-E-selects-player-car", ctx.CurrentAction?.id == "vehicle-" + playerCar.Data.id,
                (ctx.CurrentAction?.id ?? "no action") + "; player=" + ctx.Player.position.ToString("F2") + "; car=" + playerCar.Position.ToString("F2") + "; busy=" + ctx.Motor.Busy + "; speed=" + playerCar.Speed.ToString("F2"));
            interact = true; yield return new WaitForSeconds(1.35f);
            bool aboard = ctx.Vehicles.Active == playerCar && ctx.Motor.Riding == playerCar.Root;
            CheckResult("normal-E-boards-player-car", aboard, "Player entered through the registered vehicle action."); if (!aboard) yield break;
            move = Vector2.up; float driveUntil = ctx.Clock + 8;
            while (ctx.Incidents.Get(target) == null && ctx.Clock < driveUntil) yield return null;
            move = Vector2.zero; brake = true; var incident = ctx.Incidents.Get(target);
            CheckResult("real-PhysX-impact-starts-incident", incident != null && incident.ImpactSpeed >= 3.5f, incident == null ? "No impact reaction" : "relative speed=" + incident.ImpactSpeed.ToString("F2") + " m/s");
            if (incident == null) yield break;
            report.impactSpeed = incident.ImpactSpeed;
            bool stopped = false, visible = false, clear = true, inspecting = false, returned = false, capture = false;
            int exitSide = 0; float deadline = ctx.Clock + 42, nextSample = 0; string previous = null;
            while (ctx.Incidents.Get(target) != null && ctx.Clock < deadline)
            {
                incident = ctx.Incidents.Get(target);
                if (incident.State != previous) { previous = incident.State; report.stages.Add(previous); Debug.Log("[CityIncidentQA] state " + previous); }
                if (incident.Actor != null)
                {
                    visible |= !target.Driver.gameObject.activeInHierarchy && incident.Actor.Root.gameObject.activeInHierarchy && incident.DriverHead && incident.DriverHead.IsChildOf(incident.Actor.Root);
                    if (incident.State == "walking" || incident.State == "inspecting" || incident.State == "returning") clear &= CityPhysics.StandClear(incident.Actor.Root.position, incident.Actor.Root, target.Root);
                    report.distanceWalked = incident.DistanceWalked; exitSide = incident.Side;
                }
                stopped |= target.Body.linearVelocity.magnitude < .45f && target.Hazards;
                if (ctx.Clock >= nextSample)
                {
                    nextSample = ctx.Clock + 2;
                    report.samples.Add(new Sample { elapsed = ctx.Clock - incident.Started, state = incident.State, wait = incident.WaitReason,
                        speed = target.Body.linearVelocity.magnitude, angular = target.Body.angularVelocity.magnitude, position = target.Position, hold = target.TrafficHold, hazards = target.Hazards });
                    Debug.Log("[CityIncidentQA] wait=" + incident.WaitReason + "; speed=" + target.Body.linearVelocity.magnitude.ToString("F3") + "; angular=" + target.Body.angularVelocity.magnitude.ToString("F3") + "; position=" + target.Position.ToString("F2"));
                }
                if (incident.State == "inspecting")
                {
                    inspecting = true;
                    if (!capture && incident.Elapsed > .35f)
                    {
                        capture = true;
                        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                            Capture(target.Position + new Vector3(8, 5.5f, 5), incident.Actor.Root.position + Vector3.up * 1.25f);
                    }
                }
                returned |= incident.State == "closing" && incident.Actor == null && target.Driver.gameObject.activeInHierarchy;
                yield return null;
            }
            CheckResult("driver-stops-and-warns", stopped, "Vehicle held physically with hazards before walking.");
            CheckResult("blocked-left-exits-clear-right", exitSide == 1 && visible && clear, "side=" + exitSide + "; reused head=" + visible + "; clear walking capsule=" + clear);
            CheckResult("inspection-and-return-stages", inspecting && returned && report.stages.Contains("returning") && report.stages.Contains("entering"), string.Join(" → ", report.stages));
            CheckResult("temporary-driver-cleaned-up", ctx.Incidents.Get(target) == null && ctx.Incidents.CompletedCount == completed + 1 && ctx.Residents.People.Count == residents && ctx.Vehicles.Fleet.Count == fleet,
                "residents=" + ctx.Residents.People.Count + "; fleet=" + ctx.Vehicles.Fleet.Count + "; completed=" + ctx.Incidents.CompletedCount);
            Vector3 before = target.Position; yield return new WaitForSeconds(3.5f);
            bool headRestored = false; foreach (var t in originalHead) if (t && t.name.IndexOf("driver-head", StringComparison.OrdinalIgnoreCase) >= 0) headRestored |= t.IsChildOf(target.Driver);
            CheckResult("driver-reenters-and-route-resumes", returned && headRestored && target.Data.auto && !target.TrafficHold && target.Route == route && CityPhysics.Flat(target.Position - before).magnitude > .35f,
                "head restored=" + headRestored + "; auto=" + target.Data.auto + "; held=" + target.TrafficHold + "; distance=" + CityPhysics.Flat(target.Position - before).magnitude.ToString("F2"));
        }
        void Capture(Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; var position = camera.transform.position; var rotation = camera.transform.rotation;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var render = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32); var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                render.Create(); camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye)); camera.targetTexture = render;
                camera.Render(); RenderTexture.active = render; pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, "incident-inspection.png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.transform.SetPositionAndRotation(position, rotation); RenderTexture.active = previousActive;
                render.Release(); Destroy(render); Destroy(pixels);
            }
        }
    }
}
