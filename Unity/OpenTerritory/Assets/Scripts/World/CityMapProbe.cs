using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Runs only in an explicit --city-map-qa session, never in a normal game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityMapProbe : MonoBehaviour
    {
        [Serializable] public class Crossing { public Vector3 position; public string road; public Vector3[] ends; }
        [Serializable] public class Gangway { public Vector3 waterward, landward; public float width; }
        [Serializable] public class Parking { public int id; public Vector3 position; public float yaw, width, length; }
        [Serializable] public class Map { public int revision; public float sidewalkWidth, curbHeight; public Vector3 harbor; public Vector3[] junctions, sidewalkSamples; public Vector3[] widenedSidewalkSamples = Array.Empty<Vector3>(); public Crossing[] crossings; public Gangway[] gangways; public Parking[] parking; }
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(), obstacles = new List<string>(); }
        readonly Report report = new Report(); CityContext ctx; Map map; Vector2 move; string dir; bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-map-qa") >= 0) new GameObject("Auditoria do mapa • opt-in").AddComponent<CityMapProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, float value = 0, string detail = "") { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("MAP_QA " + name + " " + passed + " " + value + " " + detail); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir"); dir = at >= 0 ? args[at + 1] : Application.persistentDataPath; Directory.CreateDirectory(dir);
            float end = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < end) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            var text = Resources.Load<TextAsset>("street-network"); map = text ? JsonUtility.FromJson<Map>(text.text) : null;
            Test("unified-street-network-loaded", map != null && map.revision == 3);
            if (map == null) { Finish(); yield break; }
            yield return new WaitForSeconds(2);
            int missing = 0, blocked = 0; float error = 0;
            foreach (var p in map.sidewalkSamples)
            {
                if (!Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var hit, 8, 1 << CityContext.GroundLayer)) { missing++; continue; }
                error = Mathf.Max(error, Mathf.Abs(hit.point.y - p.y));
                if (Mathf.Abs(hit.point.y - p.y) > .11f) missing++;
                if (Obstacle(hit.point, "sidewalk")) blocked++;
            }
            Test("sidewalks-have-continuous-physical-support", missing == 0, missing, "samples=" + map.sidewalkSamples.Length + " maxHeightError=" + error);
            Test("sidewalks-clear-of-static-obstacles", blocked == 0, blocked);
            if (map.widenedSidewalkSamples.Length > 0)
            {
                Test("widened-sidewalk-outer-lanes-supported", map.sidewalkWidth >= 3.6f && map.widenedSidewalkSamples.Length > 400, map.widenedSidewalkSamples.Length);
                var traversed = new List<Vector3>();
                for (int i = 0; i < map.widenedSidewalkSamples.Length && traversed.Count < 4; i++)
                {
                    var from = map.widenedSidewalkSamples[i];
                    if (traversed.Any(p => CityPhysics.Flat(p - from).magnitude < 65)) continue;
                    for (int candidate = i + 1; candidate < Mathf.Min(i + 12, map.widenedSidewalkSamples.Length); candidate++)
                    {
                        var to = map.widenedSidewalkSamples[candidate]; float distance = CityPhysics.Flat(to - from).magnitude;
                        if (distance < 7 || distance > 14) continue;
                        bool clear = true;
                        for (int k = 0; k <= 28; k++)
                        {
                            var p = Vector3.Lerp(from, to, k / 28f);
                            if (!Physics.Raycast(p + Vector3.up, Vector3.down, out var hit, 2, 1 << CityContext.GroundLayer)
                                || Mathf.Abs(hit.point.y - p.y) > .09f || !ctx.Free(hit.point + Vector3.up * .06f, .38f)) { clear = false; break; }
                        }
                        if (!clear) continue;
                        yield return Walk("widened-sidewalk-" + traversed.Count, from, to);
                        traversed.Add(from); break;
                    }
                }
                Test("four-widened-sidewalk-sections-walked", traversed.Count == 4, traversed.Count);
            }
            int laneBlocked = 0, lanes = 0;
            foreach (var road in ReferenceMap.Layout.roads)
            {
                if (road.kind == "trail") continue;
                for (int i = 4; i < road.points.Length - 4; i += 13)
                {
                    var forward = CityPhysics.Flat(road.points[i + 1] - road.points[i - 1]).normalized; var right = Vector3.Cross(Vector3.up, forward);
                    foreach (float side in new[] { -.25f, 0, .25f })
                    {
                        var p = road.points[i] + right * road.width * side; p.y = ctx.Ground(p.x, p.z); lanes++;
                        if (Obstacle(p, "lane")) laneBlocked++;
                    }
                }
            }
            Test("full-road-width-clear-of-static-obstacles", laneBlocked == 0, laneBlocked, "samples=" + lanes);
            int unsupportedCars = 0;
            foreach (var car in ctx.Vehicles.Fleet)
                if (car.Position.y < ctx.Ground(car.Position.x, car.Position.z) - .32f || car.Position.y > ctx.Ground(car.Position.x, car.Position.z) + 1) unsupportedCars++;
            Test("vehicles-spawn-on-supported-surfaces", unsupportedCars == 0, unsupportedCars);
            int parked = 0;
            foreach (var slot in map.parking)
            {
                var data = ctx.Data.vehicles.First(c => c.id == slot.id);
                // Population streams distant vehicles. Materialize the authored
                // parking fixture instead of assuming every bay is populated.
                // The operational fire garage owns its current truck pose.
                var garage = FindFirstObjectByType<CityFireGarage>();
                Vector3 expected = garage && garage.vehicle == slot.id ? garage.TruckPosition : slot.position;
                var car = ctx.Vehicles.Fleet.FirstOrDefault(c => c.Data.id == slot.id)
                    ?? ctx.Vehicles.Spawn(data, expected, Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0));
                if (CityPhysics.Flat(car.Position - expected).magnitude < 2) parked++;
            }
            Test("parked-vehicles-have-separate-bays", parked == map.parking.Length && parked == 14, parked);
            var choices = map.crossings.Where(c => c.ends.Length == 2 && ctx.Free(c.ends[0] + Vector3.up * .05f) && ctx.Free(c.ends[1] + Vector3.up * .05f)).ToArray();
            Test("crosswalks-have-two-clear-sidewalk-approaches", choices.Length >= 12, choices.Length);
            for (int i = 0; i < Mathf.Min(4, choices.Length); i++)
            {
                var crossing = choices[i * (choices.Length / 4)];
                yield return Walk("crosswalk-ramp-" + i, crossing.ends[0], crossing.ends[1]);
            }
            for (int i = 0; i < map.gangways.Length; i++) yield return Walk("pier-to-land-" + i, map.gangways[i].waterward, map.gangways[i].landward);
            int reachable = 0;
            foreach (var boat in ctx.Marine.Fleet)
            {
                var dock = ctx.Data.docks.OrderBy(d => CityPhysics.Flat(new Vector3(d.x, 0, d.z) - boat.Root.position).sqrMagnitude).First();
                float x = dock.x + Mathf.Sign(boat.Root.position.x - dock.x) * (dock.w * .5f - .45f);
                var action = ctx.Actions.First(a => a.id == "boat-" + boat.Data.id);
                bool found = false; float best = float.MaxValue;
                // A bollard or ladder can occupy one point along an otherwise
                // accessible berth. Check actual clear approaches along it.
                foreach (float shift in new[] { 0f, 1f, -1f, 1.8f, -1.8f })
                {
                    var p = new Vector3(x, dock.top + .06f, Mathf.Clamp(boat.Root.position.z + shift, dock.z - dock.d * .5f + .65f, dock.z + dock.d * .5f - .65f));
                    if (!ctx.Free(p, .38f)) continue;
                    ctx.Motor.Warp(p); yield return new WaitForSeconds(.22f);
                    float distance = Vector3.Distance(ctx.Player.position + Vector3.up, action.Position); best = Mathf.Min(best, distance);
                    if (ctx.Motor.Grounded && (action.Enabled == null || action.Enabled()) && distance <= action.range) { found = true; break; }
                }
                if (found) reachable++; else report.obstacles.Add("boat " + boat.Data.id + " bestApproachDistance=" + best + " berth=" + dock.x + "," + dock.z);
            }
            Test("all-five-boats-reachable-from-the-piers", reachable == 5, reachable);
            Test("bench-seat-heights-are-relative-to-the-floor", ctx.Data.seats.All(s => s.seatTop > .4f && s.seatTop < 1.5f));
            var bench = ctx.Data.seats.First(s => !s.occupied);
            var approach = new Vector3(bench.x, 0, bench.z) + Quaternion.Euler(0, bench.yaw * Mathf.Rad2Deg, 0) * Vector3.forward * (bench.depth * .5f + .9f);
            approach.y = ctx.Ground(approach.x, approach.z) + .05f; ctx.Motor.Warp(approach); yield return new WaitForSeconds(.4f);
            ctx.Actions.First(a => a.id == "seat-" + bench.id).Execute(); yield return new WaitForSeconds(.9f);
            Test("relocated-bench-can-be-sat-on-at-the-correct-height", ctx.Motor.SeatHeight.HasValue && Mathf.Abs(ctx.Motor.SeatHeight.Value - bench.seatTop) < .15f, ctx.Motor.SeatHeight ?? -1);
            ctx.Actions.First(a => a.id == "stand").Execute(); yield return new WaitForSeconds(.9f);
            Test("relocated-bench-has-a-clear-exit", !ctx.Motor.SeatHeight.HasValue && ctx.Motor.Grounded);
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); yield return new WaitForSeconds(.2f);
            Vector3 j = ReferenceMap.FromPixel(934, 403); j.y = ctx.Ground(j.x, j.z);
            Capture("01-cruzamento", j + new Vector3(25, 26, -26), j);
            j = ReferenceMap.FromPixel(900, 411); j.y = ctx.Ground(j.x, j.z);
            Capture("02-travessias-e-calcadas", j + new Vector3(20, 15, -22), j);
            Capture("03-porto-fora-da-via", map.harbor + new Vector3(37, 32, -44), map.harbor + Vector3.forward * 4);
            var g = map.gangways[0]; Capture("04-pier-conectado", g.waterward + new Vector3(24, 24, -24), Vector3.Lerp(g.waterward, g.landward, .5f));
            var park = map.parking[0].position; Capture("05-estacionamento", park + new Vector3(16, 13, -16), park);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish();
        }
        bool Obstacle(Vector3 p, string kind)
        {
            var overlaps = Physics.OverlapCapsule(p + Vector3.up * .65f, p + Vector3.up * 1.8f, .28f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            if (overlaps.Length == 0) return false;
            if (report.obstacles.Count < 45) report.obstacles.Add(kind + " " + p.ToString("F2") + " " + overlaps[0].name);
            return true;
        }
        IEnumerator Walk(string name, Vector3 from, Vector3 to)
        {
            ctx.Marine.Fall(); ctx.Motor.SetRide(null); ctx.Motor.SetInputLock(false);
            ctx.Motor.Warp(from + Vector3.up * .06f); Vector3 delta = CityPhysics.Flat(to - from); float length = delta.magnitude;
            // Dense frontages can push the follow camera sideways. Give the
            // held forward input an exact basis for this traversal test.
            ctx.Follow.enabled = false; ctx.Camera.transform.SetPositionAndRotation(from + Vector3.up * 4 - delta.normalized * 7, Quaternion.LookRotation(delta.normalized));
            yield return new WaitForSeconds(.3f);
            if (name.StartsWith("crosswalk"))
            {
                float clearUntil = ctx.Clock + 25;
                var center = Vector3.Lerp(from, to, .5f) + Vector3.up * 1.1f;
                var half = new Vector3(.85f, 1.2f, length * .5f + .25f);
                int moving = (1 << CityContext.VehicleLayer) | (1 << CityContext.ActorLayer);
                while (ctx.Clock < clearUntil && Physics.CheckBox(center, half, Quaternion.LookRotation(delta.normalized), moving, QueryTriggerInteraction.Ignore)) yield return null;
            }
            float end = ctx.Clock + length / 3.1f + 1.7f; int falls = 0; move = Vector2.up;
            while (ctx.Clock < end && Vector3.Dot(CityPhysics.Flat(ctx.Player.position - from), delta.normalized) < length - .2f)
            {
                if (ctx.Motor.Swimming || ctx.Player.position.y < ctx.Ground(ctx.Player.position.x, ctx.Player.position.z) - .25f) falls++;
                yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.2f);
            float distance = CityPhysics.Flat(ctx.Player.position - to).magnitude;
            Test(name, distance < 1.3f && falls == 0 && ctx.Motor.Grounded, distance, "falls=" + falls + " from=" + from + " to=" + to);
        }
        void Capture(string name, Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = 65; camera.farClipPlane = 4000;
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            var previous = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; RenderSettings.fog = fog; rt.Release(); Destroy(rt); Destroy(texture);
        }
        void Finish() { finished = true; File.WriteAllText(Path.Combine(dir, "map-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2); }
    }
}
