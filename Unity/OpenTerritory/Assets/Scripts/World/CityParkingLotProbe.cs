using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Only installed for --city-parking-qa, never during a normal game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityParkingLotProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool passed = true; public string unity, graphics;
            public int surfaceSamples, drivewaySamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityParkingLot lot; string directory; Vector2 move; bool brake, interact, finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-parking-qa") >= 0)
                new GameObject("Parking acceptance • opt-in").AddComponent<CityParkingLotProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
        void Update()
        {
            if (!ctx || finished) return;
            ctx.Input.TouchMove = move; ctx.Input.TouchBrake = brake;
            if (interact) { ctx.Input.TouchInteract = true; interact = false; }
        }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("PARKING_QA " + name + " " + passed + " " + detail); }
        Vector3 Feet(Vector3 p) { p.y = ctx.Ground(p.x, p.z) + .06f; return p; }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "parking-qa");
            Directory.CreateDirectory(directory); report.unity = Application.unityVersion; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            float deadline = Time.realtimeSinceStartup + 60;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; lot = FindFirstObjectByType<CityParkingLot>();
            Test("city-and-parking-ready", ctx && ctx.Ready && lot && lot.surface);
            if (!ctx || !ctx.Ready || !lot) { Finish(); yield break; }
            ctx.Population.enabled = false; ctx.Combat.Holster(); ctx.UI.SetSettings(false); ctx.Follow.enabled = false;
            ctx.Systems.Weather.Request("clear", true); ctx.Clock = 0;
            Test("nine-marked-bays-and-nine-physical-stops", lot.bayCount == 9 && lot.movedStops.Length == 6 && lot.newStops.Length == 3 &&
                lot.movedStops.All(c => c && c.enabled) && lot.newStops.All(c => c && c.enabled));
            Test("neighbouring-home-preserved", ctx.Data.places.Any(p => p.name == "#888471") &&
                FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Any(t => t.name == "Casa • #888471"));
            var paint = lot.transform.Find("Parking bay markings");
            var paintMesh = paint ? paint.GetComponent<MeshFilter>().sharedMesh : null;
            Test("bay-lines-face-upwards", paintMesh && paintMesh.normals.Length > 0 && paintMesh.normals.Average(n => n.y) > .9f);
            Test("existing-stop-mesh-remains-correct-in-all-lods", lot.stopLod && lot.stopLod.parts[lot.stopLodPart].full == lot.stopFilter.sharedMesh &&
                lot.stopLod.parts[lot.stopLodPart].medium == lot.stopFilter.sharedMesh && lot.stopLod.parts[lot.stopLodPart].far == lot.stopFilter.sharedMesh);
            int oldPositions = 0, addedSolids = 0;
            foreach (var box in lot.movedStops)
            {
                int id = int.Parse(box.name.Substring(8));
                var matching = ctx.Data.solids.Where(s => s.index == id).ToArray();
                if (matching.Length == 0 || matching.Any(s => Vector2.Distance(new Vector2(s.x, s.z), new Vector2(box.transform.position.x, box.transform.position.z)) > .05f)) oldPositions++;
            }
            foreach (var box in lot.newStops)
                if (ctx.Data.solids.Any(s => Vector2.Distance(new Vector2(s.x, s.z), new Vector2(box.transform.position.x, box.transform.position.z)) < .05f)) addedSolids++;
            Test("navigation-data-matches-wheel-stops", oldPositions == 0 && addedSolids == 3, "old=" + oldPositions + " added=" + addedSolids);

            int missing = 0, hidden = 0;
            for (float u = -9.5f; u <= 17.6f; u += .8f) for (float v = -6.5f; v <= 6.8f; v += .8f)
            {
                var p = CityParkingLot.Point(u, v);
                if (!Physics.Raycast(p + Vector3.up * 15, Vector3.down, out var hit, 10, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) missing++;
                else if (!hit.transform.IsChildOf(lot.transform))
                {
                    var own = Physics.RaycastAll(p + Vector3.up * 15, Vector3.down, 10, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)
                        .FirstOrDefault(h => h.transform.IsChildOf(lot.transform));
                    if (!own.collider || hit.point.y - own.point.y > .02f) hidden++;
                }
                report.surfaceSamples++;
            }
            Test("continuous-physical-parking-surface", missing == 0 && hidden == 0, "samples=" + report.surfaceSamples + " missing=" + missing + " otherSurface=" + hidden);
            int blocked = 0, unsupported = 0; var obstacleNames = new HashSet<string>();
            for (float v = 1; v <= 12.3f; v += .35f) foreach (float u in new[] { -4.2f, -2.55f, -.9f })
            {
                var p = Feet(CityParkingLot.Point(u, v)); report.drivewaySamples++;
                if (!Physics.Raycast(p + Vector3.up, Vector3.down, 2, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) unsupported++;
                foreach (var hit in Physics.OverlapCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.7f, .4f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                { blocked++; obstacleNames.Add(hit.name); }
            }
            Test("driveway-and-sidewalk-are-open", blocked == 0 && unsupported == 0,
                "samples=" + report.drivewaySamples + " unsupported=" + unsupported + " blocked=" + blocked + " " + string.Join(",", obstacleNames.Take(6)));
            float steepest = 0;
            foreach (float u in new[] { -4.2f, -2.55f, -.9f })
            {
                float previous = 0;
                for (float v = 9.5f; v <= 12.3f; v += .2f)
                {
                    var p = Feet(CityParkingLot.Point(u, v));
                    if (previous > 0) steepest = Mathf.Max(steepest, Mathf.Abs(p.y - previous));
                    previous = p.y;
                }
            }
            Test("driveway-has-smooth-street-transition", steepest < .075f, "max height change per 20 cm=" + steepest.ToString("F3"));

            var cars = new List<CityVehicles.Vehicle>();
            foreach (int id in lot.vehicleIds)
            {
                var data = ctx.Data.vehicles.First(v => v.id == id); var node = ctx.Node(id);
                bool position = node && Vector3.Distance(node.position, data.position) < .05f;
                var vehicle = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == id) ?? ctx.Vehicles.Spawn(data, data.position, Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0));
                if (vehicle != null) cars.Add(vehicle);
                Test("parked-vehicle-" + id + "-uses-new-bay", position && vehicle != null && Mathf.Abs(CityParkingLot.Local(data.position).z + 3.3f) < .15f);
            }
            ctx.Motor.SetInputLock(true); ctx.Motor.Warp(Feet(CityParkingLot.Point(-2.55f, 4))); yield return new WaitForSeconds(.6f);
            Capture("01-overview", new Vector3(522, 25, 657), new Vector3(520, 9, 629), 55);
            Capture("02-overhead", new Vector3(522, 70, 634), new Vector3(522, 0, 634), 55);
            var eye = Feet(CityParkingLot.Point(-2.55f, 10.4f)) + Vector3.up * 1.7f;
            Capture("03-entrance", eye, Feet(CityParkingLot.Point(-2.55f, -1)) + Vector3.up, 65);

            ctx.Motor.SetInputLock(false); ctx.Camera.transform.rotation = Quaternion.identity;
            yield return Walk("pedestrian-crosses-driveway", CityParkingLot.Point(-2.55f, 10.4f), CityParkingLot.Point(-2.55f, 3.5f));
            yield return Walk("pedestrian-crosses-parking-aisle", CityParkingLot.Point(-8.5f, 3.8f), CityParkingLot.Point(14.5f, 3.8f));

            if (cars.Count > 0)
            {
                var car = cars[0];
                var entrance = car.Root.TransformPoint(new Vector3(-car.Data.width * .5f - .75f, 0, car.Data.seatMount.z)); entrance = Feet(entrance);
                ctx.Motor.Warp(entrance); ctx.Camera.transform.position = entrance + Vector3.up * 1.8f;
                ctx.Camera.transform.LookAt(car.Root.TransformPoint(new Vector3(0, 1, car.Data.seatMount.z)));
                yield return new WaitForSeconds(.6f); ctx.RefreshAction();
                bool selected = ctx.CurrentAction != null && ctx.CurrentAction.id == "vehicle-" + car.Data.id;
                interact = true; yield return null; deadline = Time.realtimeSinceStartup + 8;
                while ((interact || ctx.Input.TouchInteract || ctx.Motor.Busy) && Time.realtimeSinceStartup < deadline) yield return null;
                Test("E-enters-parked-car", selected && ctx.Vehicles.Active == car, "selected=" + selected);
                if (ctx.Vehicles.Active == car)
                {
                    yield return DriveStraight("car-leaves-marked-bay", car, 7.02f, -3.3f, false, .4f);
                    yield return DriveManeuver(car);
                    yield return DriveStraight("car-enters-from-road", car, -2.55f, 11.6f, true, 5.2f);
                    yield return DriveStraight("car-exits-to-road", car, -2.55f, 4.5f, false, 12.3f);
                }
            }
            else Test("parked-car-test-available", false);
            Test("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors.Take(3)));
            Finish();
        }

        IEnumerator Walk(string name, Vector3 from, Vector3 target)
        {
            ctx.Motor.Warp(Feet(from)); ctx.Camera.transform.rotation = Quaternion.identity; yield return new WaitForSeconds(.2f);
            float deadline = Time.realtimeSinceStartup + 22;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .5f && Time.realtimeSinceStartup < deadline)
            {
                var d = CityPhysics.Flat(target - ctx.Player.position).normalized; move = new Vector2(d.x, d.z); yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.15f);
            float remaining = CityPhysics.Flat(target - ctx.Player.position).magnitude;
            Test(name, remaining < 1 && ctx.Motor.Grounded, "remaining=" + remaining.ToString("F2"));
        }

        IEnumerator DriveStraight(string name, CityVehicles.Vehicle car, float u, float startV, bool towardNegativeV, float targetV)
        {
            move = Vector2.zero; brake = false;
            var p = Feet(CityParkingLot.Point(u, startV));
            var rotation = Quaternion.Euler(0, CityParkingLot.Yaw + (towardNegativeV ? 180 : 0), 0);
            car.Body.position = p; car.Body.rotation = rotation; car.Root.SetPositionAndRotation(p, rotation);
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.Motion = new CityVehicleMath.State { Direction = 1, Gear = 1 }; car.JamUntil = 0; car.Body.WakeUp(); Physics.SyncTransforms();
            yield return new WaitForSeconds(.5f);
            float deadline = Time.realtimeSinceStartup + 10; float currentV = CityParkingLot.Local(car.Position).z;
            while ((towardNegativeV ? currentV > targetV : currentV < targetV) && Time.realtimeSinceStartup < deadline)
            { move = new Vector2(0, .27f); currentV = CityParkingLot.Local(car.Position).z; yield return null; }
            move = Vector2.zero; brake = true; yield return new WaitForSeconds(.6f); brake = false;
            var local = CityParkingLot.Local(car.Position);
            bool reached = towardNegativeV ? local.z < targetV + .6f : local.z > targetV - .6f;
            Test(name, reached && Mathf.Abs(local.x - u) < 1.8f && Vector3.Angle(car.Root.up, Vector3.up) < 25,
                "start=" + startV.ToString("F1") + " end=" + local.z.ToString("F2") + " lateral=" + (local.x - u).ToString("F2"));
        }

        IEnumerator DriveManeuver(CityVehicles.Vehicle car)
        {
            // Start where the real car stopped after leaving its bay, turn into
            // the aisle, then turn into the driveway without moving the body by hand.
            brake = false; float deadline = Time.realtimeSinceStartup + 12;
            float west = CityParkingLot.Yaw - 90;
            while (Mathf.Abs(Mathf.DeltaAngle(car.Root.eulerAngles.y, west)) > 9 && Time.realtimeSinceStartup < deadline)
            {
                float angle = Mathf.DeltaAngle(car.Root.eulerAngles.y, west);
                move = new Vector2(Mathf.Clamp(angle / 30, -1, 1), Mathf.Abs(car.Speed) < 2.2f ? .22f : 0);
                yield return null;
            }
            bool turned = Mathf.Abs(Mathf.DeltaAngle(car.Root.eulerAngles.y, west)) < 15;
            deadline = Time.realtimeSinceStartup + 8;
            while (CityParkingLot.Local(car.Position).x > 1.7f && Time.realtimeSinceStartup < deadline)
            {
                float angle = Mathf.DeltaAngle(car.Root.eulerAngles.y, west);
                move = new Vector2(Mathf.Clamp(angle / 30, -.6f, .6f), Mathf.Abs(car.Speed) < 2.2f ? .22f : 0);
                yield return null;
            }
            bool crossed = CityParkingLot.Local(car.Position).x <= 2.2f;
            deadline = Time.realtimeSinceStartup + 12;
            while (CityParkingLot.Local(car.Position).z < 12.3f && Time.realtimeSinceStartup < deadline)
            {
                float angle = Mathf.DeltaAngle(car.Root.eulerAngles.y, CityParkingLot.Yaw);
                move = new Vector2(Mathf.Clamp(angle / 30, -1, 1), Mathf.Abs(car.Speed) < 2.2f ? .22f : 0);
                yield return null;
            }
            move = Vector2.zero; brake = true; yield return new WaitForSeconds(.6f); brake = false;
            var local = CityParkingLot.Local(car.Position);
            Test("car-maneuvers-from-bay-through-aisle-to-street", turned && crossed && local.z > 11.7f && local.x > -5.3f && local.x < .3f,
                "turned=" + turned + " crossed=" + crossed + " final=" + local.ToString("F2"));
        }

        void Capture(string name, Vector3 eye, Vector3 target, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = fov;
            var lod = FindFirstObjectByType<CityLodManager>(); if (lod) foreach (var item in lod.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1400, 800, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1400, 800, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1400, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = old;
            rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish()
        {
            finished = true; if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = false; }
            File.WriteAllText(Path.Combine(directory, "parking-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
