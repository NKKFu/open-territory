using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit acceptance only. No instrumentation is installed in normal play.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityOverallMapProbe : MonoBehaviour
    {
        [Serializable] public sealed class BuildingSample { public int place; public float x, z, floor; }
        [Serializable] public sealed class Review
        {
            public int revision; public string sourceHash;
            public Vector3[] paintSamples, parkingSamples;
            public BuildingSample[] buildingSamples;
            public string[] openYards;
        }
        [Serializable] public sealed class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string verifiedAtUtc, sourceHash, graphicsApi;
            public int paintSamples, buildingSamples, parkingSamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>(), problems = new List<string>();
        }
        CityContext ctx; Review review; Vector2 move; string dir; bool finished, abort;
        readonly Report report = new Report();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-overall-map-qa") >= 0) new GameObject("Overall map QA • opt-in").AddComponent<CityOverallMapProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) { report.errors.Add(message); abort = true; } }
        void Update()
        {
            if (abort && !finished && dir != null) { Test("no-unhandled-runtime-error", false, report.errors.Count); Finish(); return; }
            if (ctx && !finished) ctx.Input.TouchMove = move;
        }
        void Test(string name, bool passed, float value = 0, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("OVERALL_MAP_QA " + name + " " + passed + " " + value + " " + detail); }
        void Problem(string detail) { if (report.problems.Count < 80) report.problems.Add(detail); }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 ? args[at + 1] : Application.persistentDataPath; Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance;
            Test("scene-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            Test("QA-storage-is-isolated", CityCommerce.IsReviewRun);
            var asset = Resources.Load<TextAsset>("overall-map-review"); review = asset ? JsonUtility.FromJson<Review>(asset.text) : null;
            Test("authored-review-matches-current-map", review != null && review.revision == 1 && review.sourceHash == ctx.Data.sourceHash);
            if (review == null) { Finish(); yield break; }
            report.sourceHash = ctx.Data.sourceHash; report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            ctx.Systems.Weather.Request("clear", true); yield return new WaitForSeconds(1);
            int missing = 0; float worst = 0;
            foreach (var s in review.buildingSamples)
            {
                report.buildingSamples++;
                if (!Physics.Raycast(new Vector3(s.x, s.floor + 7, s.z), Vector3.down, out var hit, 20, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) { missing++; continue; }
                float rise = hit.point.y - s.floor; worst = Mathf.Max(worst, rise);
            }
            Test("all-217-building-perimeters-audited", ctx.Data.places.Length == 217 && review.buildingSamples.Select(s => s.place).Distinct().Count() == 217, report.buildingSamples);
            // Sloping gardens and authored landings need not be level with the
            // building base. Test support here and actual window clearance below.
            Test("building-perimeters-have-physical-ground", missing == 0, missing, "maximumExteriorRise=" + worst);
            var windows = FindFirstObjectByType<CityWindows>(); int buriedWindows = 0, inspectedWindows = 0;
            if (windows) foreach (var window in windows.windows)
            {
                inspectedWindows++;
                float sill = window.center.y - window.height * .5f;
                var right = Vector3.Cross(Vector3.up, window.outward);
                foreach (float across in new[] { -.48f, -.25f, 0, .25f, .48f })
                {
                    var p = window.center + window.outward * .18f + right * window.width * across;
                    if (Physics.Raycast(new Vector3(p.x, 350, p.z), Vector3.down, out var hit, 500, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore) && hit.point.y > sill - .06f)
                    { buriedWindows++; Problem("buried window place=" + window.place + " center=" + window.center + " sill=" + sill + " ground=" + hit.point.y); }
                }
            }
            Test("all-real-window-sills-clear-of-terrain", inspectedWindows > 500 && buriedWindows == 0, buriedWindows, "windows=" + inspectedWindows);
            int submerged = 0, unsupportedPaint = 0; float clearanceMin = float.MaxValue, clearanceMax = 0;
            foreach (var p in review.paintSamples)
            {
                report.paintSamples++;
                if (!Physics.Raycast(p + Vector3.up * .25f, Vector3.down, out var hit, .6f, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) { unsupportedPaint++; continue; }
                float clearance = p.y - hit.point.y; clearanceMin = Mathf.Min(clearanceMin, clearance); clearanceMax = Mathf.Max(clearanceMax, clearance);
                if (clearance < .003f || clearance > .04f) { submerged++; Problem("paint clearance=" + clearance + " at " + p + " " + hit.collider.name); }
            }
            Test("road-markings-stay-above-the-real-asphalt", submerged == 0 && unsupportedPaint == 0, submerged + unsupportedPaint, "samples=" + report.paintSamples + " min=" + clearanceMin + " max=" + clearanceMax);
            int unsupportedParking = 0;
            foreach (var p in review.parkingSamples)
            {
                report.parkingSamples++;
                if (!Physics.Raycast(p + Vector3.up * .4f, Vector3.down, out var hit, 1, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - p.y) > .06f) unsupportedParking++;
            }
            Test("hillside-parking-follows-its-physical-apron", unsupportedParking == 0, unsupportedParking, "samples=" + report.parkingSamples);
            var layout = JsonUtility.FromJson<CityNeighborhoodProbe.Layout>(Resources.Load<TextAsset>("neighborhood-spaces").text);
            Test("steep-private-gardens-remain-open", review.openYards.Length > 0, review.openYards.Length);
            Test("remaining-garden-walls-are-low", layout.walls.All(w => w.height <= 1.351f), layout.walls.Max(w => w.height));
            int missingWalls = 0;
            foreach (var wall in layout.walls)
                if (!Physics.CheckBox(wall.center + Vector3.up * wall.height * .5f, new Vector3(.035f, .08f, wall.length * .22f), Quaternion.Euler(0, wall.yaw * Mathf.Rad2Deg, 0), 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) missingWalls++;
            Test("retained-low-walls-keep-their-collision", missingWalls == 0, missingWalls, "sections=" + layout.walls.Length);

            ctx.Follow.enabled = false;
            // Native shop compounds replace a few exported buildings and own
            // automatic entrances. Select live exported doors for this walk.
            var entrances = new[] { 1, 2, 4, 51, 118, 161 }.Where(i => ctx.Data.places[i].door != null).ToArray();
            Test("six-active-exported-entrances-selected", entrances.Length == 6, entrances.Length);
            foreach (int index in entrances)
            {
                var p = ctx.Data.places[index];
                yield return Walk("building-entrance-" + index, p.door.Approach(0, 2.1f), p.door.Approach(0, -2.1f));
            }
            // Walk both sides of the apron-to-trail connection with the actual
            // player capsule, then across the regraded parking surface.
            yield return Walk("parking-through-path", new Vector3(1053, 0, 943), new Vector3(1053, 0, 977));
            yield return Walk("parking-apron", new Vector3(1040, 0, 960), new Vector3(1066, 0, 960));
            var choices = layout.spaces.Where(s => s.kind == "yard" && review.openYards.Contains(s.id) && s.samples.Length > 3).Take(3).ToArray();
            foreach (var s in choices)
            {
                // Use each straight part of the existing, explicitly reserved
                // access. A curved side path is not tested as a shortcut.
                int count = Mathf.Min(7, s.samples.Length - 1);
                yield return Walk("open-yard-access-" + s.id, s.samples[0], s.samples[count]);
            }
            move = Vector2.zero; ctx.Motor.SetInputLock(true); yield return new WaitForSeconds(.2f);
            Capture("01-whole-map", new Vector3(-40, 2450, 680), new Vector3(-40, 0, 680), true);
            Capture("02-town-center", new Vector3(510, 75, 650), new Vector3(560, 10, 715));
            Capture("03-open-hillside-gardens", new Vector3(936, 52, 872), new Vector3(854, 13, 940));
            Capture("04-hillside-parking", new Vector3(1018, 48, 986), new Vector3(1054, 16, 957));
            Capture("05-parking-ground-level", new Vector3(1038, ctx.Ground(1038, 960) + 1.7f, 960), new Vector3(1062, 17, 960));
            Capture("06-hill-homes", new Vector3(745, 70, 1002), new Vector3(819, 12, 926));
            Capture("07-west-neighborhood", new Vector3(260, 68, 470), new Vector3(185, 9, 565));
            Capture("08-coast-and-harbor", new Vector3(1130, 125, 965), new Vector3(1188, 1, 854));
            Capture("09-road-markings", new Vector3(971, 60, 975), new Vector3(989, 22, 1070));
            if (windows)
            {
                var window = windows.windows.Where(w => w.place == 161).OrderBy(w => w.center.y).First();
                Capture("10-window-from-inside", window.center - window.outward * 1.6f, window.center + window.outward * 4);
                Capture("11-window-from-outside", window.center + window.outward * 5 + Vector3.up * .25f, window.center);
            }
            ctx.UI.SetSettings(true); float clock = ctx.Clock; yield return new WaitForSecondsRealtime(.3f);
            Test("Esc-keeps-settings-and-pauses-simulation", ctx.Paused && ctx.UI.Open && Mathf.Abs(ctx.Clock - clock) < .001f);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(.2f);
            Test("resume-exploration", !ctx.Paused && ctx.Clock > clock);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count);
            Finish();
        }

        IEnumerator Walk(string name, Vector3 from, Vector3 to)
        {
            ctx.Motor.SetInputLock(false); ctx.Motor.SetRide(null);
            from.y = ctx.Ground(from.x, from.z) + .055f; to.y = ctx.Ground(to.x, to.z) + .055f;
            ctx.Motor.Warp(from); Vector3 delta = CityPhysics.Flat(to - from); float length = delta.magnitude;
            ctx.Camera.transform.SetPositionAndRotation(from + Vector3.up * 4 - delta.normalized * 7, Quaternion.LookRotation(delta.normalized));
            yield return new WaitForSeconds(.3f);
            float until = ctx.Clock + length / 2.4f + 2; int falls = 0; move = Vector2.up;
            while (ctx.Clock < until && Vector3.Dot(CityPhysics.Flat(ctx.Player.position - from), delta.normalized) < length - .15f)
            {
                // Ground(x,z) deliberately returns the highest floor. Query
                // below the feet here so an upstairs room is not a false fall.
                if (!Physics.Raycast(ctx.Player.position + Vector3.up * .4f, Vector3.down, 2, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore) || ctx.Motor.Swimming) falls++;
                yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.25f);
            float distance = CityPhysics.Flat(ctx.Player.position - to).magnitude;
            Test(name, distance < 1.4f && falls == 0 && ctx.Motor.Grounded, distance, "falls=" + falls + " from=" + from + " to=" + to + " actual=" + ctx.Player.position);
        }
        void Capture(string name, Vector3 eye, Vector3 target, bool plan = false)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.farClipPlane = 6000; camera.fieldOfView = 65;
            camera.orthographic = plan; camera.orthographicSize = 1160;
            var lod = FindFirstObjectByType<CityLodManager>();
            if (lod) foreach (var item in lod.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(plan ? 2 : item.SelectLevel(camera));
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            var previous = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = previous;
            camera.orthographic = false; RenderSettings.fog = fog; rt.Release(); Destroy(rt); Destroy(texture);
        }
        void Finish()
        {
            finished = true; move = Vector2.zero; report.verifiedAtUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(dir, "overall-map-qa.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
