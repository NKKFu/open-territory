using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit opt-in; exercises the shipped scene, controller, input and truck.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityFireGarageProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public int floorSamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityFireGarage bay; CityVehicles.Vehicle truck;
        Vector2 move; bool brake, interact, finished;
        string directory; float began;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-garage-qa") >= 0)
                new GameObject("Garage QA • opt-in").AddComponent<CityFireGarageProbe>();
        }
        void Awake() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - began > 150) { Test("probe-timeout", false); StopAllCoroutines(); Finish(); return; }
            if (!ctx) return;
            ctx.Input.TouchMove = move; ctx.Input.TouchBrake = brake;
            if (interact) { ctx.Input.TouchInteract = true; interact = false; }
        }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("GARAGE_QA " + name + " " + passed + " " + detail); }
        Vector3 Point(float x, float z)
        {
            var point = bay.transform.TransformPoint(new Vector3(x, CityFireGarage.Floor, z));
            point.y = ctx.Ground(point.x, point.z) + .045f; return point;
        }
        void Warp(float x, float z) { move = Vector2.zero; ctx.Motor.Warp(Point(x, z)); }
        IEnumerator Walk(float x, float z)
        {
            ctx.Camera.transform.rotation = Quaternion.identity;
            var target = Point(x, z); float deadline = Time.realtimeSinceStartup + Vector3.Distance(ctx.Player.position, target) / 2 + 3;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .2f && Time.realtimeSinceStartup < deadline)
            {
                var direction = CityPhysics.Flat(target - ctx.Player.position).normalized;
                move = new Vector2(direction.x, direction.z); yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.2f);
            Test("walk-" + x + "-" + z, CityPhysics.Flat(target - ctx.Player.position).magnitude < .55f && ctx.Motor.Grounded,
                "actual=" + bay.transform.InverseTransformPoint(ctx.Player.position));
        }
        bool GateBlocks()
        {
            var from = bay.transform.TransformPoint(new Vector3(-6.2f, 1.5f, 12));
            return Physics.Raycast(from, -bay.transform.forward, 3, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/garage/editor"));
            Directory.CreateDirectory(directory);
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup - began < 75) yield return null;
            ctx = CityContext.Instance; bay = FindFirstObjectByType<CityFireGarage>();
            Test("native-scene-and-garage-ready", ctx && ctx.Ready && bay);
            if (!ctx || !ctx.Ready || !bay) { Finish(); yield break; }
            ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.UI.SetSettings(false); ctx.Combat.Holster();
            ctx.Systems.Weather.Request("clear", true); ctx.Clock = 35;
            var data = ctx.Data.vehicles.First(v => v.id == bay.vehicle);
            truck = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == data.id) ?? ctx.Vehicles.Spawn(data, bay.TruckPosition, bay.transform.rotation);
            Test("existing-fire-truck-parked-in-bay", truck != null && Vector3.Distance(truck.Position, bay.TruckPosition) < .7f);
            Warp(4, 7); yield return new WaitForSeconds(5);
            Test("closed-gate-is-solid", bay.OpenAmount < .01f && GateBlocks());
            Capture("01-exterior-fechado", new Vector3(1, 4.5f, 23), new Vector3(-4, 2.5f, 10), 61);
            Capture("02-planta", new Vector3(-3, 24, 15), new Vector3(-4, 0, 8), 55);

            Warp(-8.5f, 12.3f); yield return new WaitForSeconds(.35f);
            float amount = bay.OpenAmount, clock = ctx.Clock;
            Test("approach-opens-physical-sectional-gate", amount > .05f && amount < .9f);
            ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.35f);
            Test("Esc-freezes-gate-and-simulation", ctx.Paused && Mathf.Abs(bay.OpenAmount - amount) < .001f && Mathf.Abs(ctx.Clock - clock) < .001f);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(1.8f);
            Test("gate-resumes-and-clears-vehicle-height", bay.OpenAmount > .99f && !GateBlocks() && bay.panels.All(p => p.localPosition.y > 4.1f));
            Capture("03-exterior-aberto", new Vector3(-2, 3.3f, 20), new Vector3(-6.2f, 2.1f, 4), 63);
            Capture("04-interior", new Vector3(-8.9f, 2.7f, 8.8f), new Vector3(-5.8f, 2, -3.8f), 68);
            Capture("05-portao-por-dentro", new Vector3(-8.6f, 2.5f, 5.2f), new Vector3(-5, 2.4f, 12), 70);
            int unsupported = 0;
            for (float x = -8.5f; x <= -3.9f; x += 1.15f) for (float z = -3.8f; z <= 12.6f; z += .4f)
            {
                var point = Point(x, z); report.floorSamples++;
                if (!Physics.Raycast(point + Vector3.up * .5f, Vector3.down, .8f, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) unsupported++;
            }
            Test("continuous-floor-and-sidewalk-ramp", unsupported == 0, "samples=" + report.floorSamples);
            Warp(-8.5f, 10.55f); yield return new WaitForSeconds(3.2f);
            Test("occupied-threshold-holds-gate-open", bay.Occupied && bay.OpenAmount > .99f);
            yield return Walk(-8.5f, -3.5f);
            yield return Walk(-8.5f, 5.6f);
            yield return Walk(-3.5f, 5.6f);
            yield return Walk(-3.5f, 1.2f);
            yield return Walk(-1.3f, 1.2f);
            yield return Walk(0, 9.6f);
            yield return Walk(0, 12.3f);
            Test("original-personnel-door-still-accessible", bay.transform.InverseTransformPoint(ctx.Player.position).z > 11.8f);
            Warp(4, 7); yield return new WaitForSeconds(5);
            Test("empty-gate-closes-again", bay.OpenAmount < .01f && GateBlocks());

            // Board with the normal E action and drive the original truck through the actual aperture.
            var entrance = truck.Root.TransformPoint(new Vector3(-truck.Data.width * .5f - .75f, 0, truck.Data.seatMount.z));
            entrance.y = ctx.Ground(entrance.x, entrance.z) + .05f; ctx.Motor.Warp(entrance);
            var eye = entrance + Vector3.up * 1.8f;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(truck.Root.TransformPoint(new Vector3(0, 1, truck.Data.seatMount.z)) - eye));
            yield return new WaitForSeconds(.5f); ctx.RefreshAction();
            Test("E-selects-parked-fire-truck", ctx.CurrentAction != null && ctx.CurrentAction.id == "vehicle-" + truck.Data.id);
            interact = true; yield return new WaitForSeconds(1.5f);
            Test("E-enters-fire-truck", ctx.Vehicles.Active == truck && ctx.Motor.Riding == truck.Root);
            if (ctx.Vehicles.Active == truck)
            {
                int gateHits = 0;
                Action<CityVehicles.Vehicle, float, Collider> hit = (car, speed, collider) =>
                { if (car == truck && collider && collider.name == "Chapa vermelha" && collider.transform.IsChildOf(bay.transform)) gateHits++; };
                ctx.Vehicles.Impact += hit;
                var relocated = FindFirstObjectByType<CityFireStationSite>();
                float departure = relocated ? relocated.streetLocalZ - 2 : 17.1f;
                float deadline = Time.realtimeSinceStartup + (relocated ? 22 : 13);
                move = new Vector2(0, 1);
                while (bay.transform.InverseTransformPoint(truck.Position).z < departure && Time.realtimeSinceStartup < deadline)
                {
                    if (bay.transform.InverseTransformPoint(truck.Position).z > 6) move = new Vector2(0, .20f);
                    yield return null;
                }
                move = Vector2.zero; brake = true; yield return new WaitForSeconds(1.5f);
                var outside = bay.transform.InverseTransformPoint(truck.Position);
                Test("W-drives-truck-through-garage-to-street", outside.z > departure - .1f && Mathf.Abs(outside.x + 6.2f) < 1, "position=" + outside + " gate=" + bay.OpenAmount);
                Test("gate-anticipates-full-throttle-departure-without-impact", gateHits == 0, "gateHits=" + gateHits);
                Capture("06-caminhao-saindo", new Vector3(1, 4.3f, 24), new Vector3(-6.2f, 1.7f, 13), 64);
                brake = false; move = new Vector2(0, -1); deadline = Time.realtimeSinceStartup + (relocated ? 25 : 16);
                while (bay.transform.InverseTransformPoint(truck.Position).z > 2.5f && Time.realtimeSinceStartup < deadline)
                {
                    if (bay.transform.InverseTransformPoint(truck.Position).z < 7) move = new Vector2(0, -.45f);
                    yield return null;
                }
                move = Vector2.zero; brake = true; yield return new WaitForSeconds(1.5f);
                var inside = bay.transform.InverseTransformPoint(truck.Position);
                Test("S-reverses-truck-through-gate-into-bay", inside.z < 3 && inside.z > -1 && Mathf.Abs(inside.x + 6.2f) < 1, "position=" + inside);
                ctx.Vehicles.Impact -= hit;
                interact = true; yield return new WaitForSeconds(1.4f);
                Test("E-exits-truck-inside-bay", ctx.Vehicles.Active == null && !ctx.Motor.Riding && ctx.Motor.Grounded && bay.transform.InverseTransformPoint(ctx.Player.position).z < 9.5f);
            }
            brake = false; move = Vector2.zero;
            Warp(-8.5f, 10.5f); yield return new WaitForSeconds(2);
            Warp(4, 7); yield return new WaitForSeconds(3.25f);
            amount = bay.OpenAmount; Warp(-8.5f, 10.5f); yield return new WaitForSeconds(1.7f);
            Test("obstruction-during-closing-reopens-gate", amount < .95f && bay.OpenAmount > .99f && bay.Occupied, "closingAmount=" + amount);
            ctx.Clock = 168; yield return new WaitForSeconds(.5f);
            Test("bay-lights-use-building-circuit-at-night", bay.GetComponentsInChildren<Light>().All(l => l.enabled && l.intensity > .1f));
            Capture("07-garagem-noite", new Vector3(-2, 3.3f, 19), new Vector3(-6.2f, 2.1f, 3), 62);
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void Capture(string name, Vector3 eye, Vector3 target, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = bay.transform.TransformPoint(eye);
            camera.transform.LookAt(bay.transform.TransformPoint(target)); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>();
            if (manager) foreach (var item in manager.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.antiAliasing = 2; rt.Create();
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = old; rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish()
        {
            finished = true;
            if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = false; ctx.UI.SetSettings(false); }
            if (string.IsNullOrEmpty(directory)) directory = Application.temporaryCachePath;
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "garage-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
