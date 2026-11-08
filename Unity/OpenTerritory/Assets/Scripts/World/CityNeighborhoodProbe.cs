using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Offline opt-in integration run; no probes run during a normal game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityNeighborhoodProbe : MonoBehaviour
    {
        [Serializable] public class Space { public string id, kind; public int lot; public float area; public Vector3 center; public Vector3[] samples; }
        [Serializable] public class Wall { public Vector3 center; public float yaw, width, length, height; }
        [Serializable] public class Reused { public string name, space; public Vector3 position; }
        [Serializable] public class Layout { public bool oldSquareRemoved; public int reusedPlazaPieces, additionalPlazaTrees, additionalBenches; public Vector3 oldSquareCenter; public Space[] spaces; public Wall[] walls; public Reused[] assets; }
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(), obstacles = new List<string>(); }
        CityContext ctx; Layout layout; Vector2 move; string dir; bool finished; readonly Report report = new Report();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-neighborhood-qa") >= 0) new GameObject("QA de quintais e jardins • opt-in").AddComponent<CityNeighborhoodProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, float value = 0, string detail = "") { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("NEIGHBORHOOD_QA " + name + " " + passed + " " + value + " " + detail); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir"); dir = at >= 0 ? args[at + 1] : Application.persistentDataPath; Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            var asset = Resources.Load<TextAsset>("neighborhood-spaces"); layout = asset ? JsonUtility.FromJson<Layout>(asset.text) : null;
            if (layout != null)
            {
                var exported = JsonUtility.FromJson<WorldData>(ctx.GetComponent<WorldRegistry>().manifest.text);
                layout.spaces = layout.spaces.Where(s => s.lot < 0 || s.lot < ctx.Data.places.Length &&
                    ctx.Data.places[s.lot].kind != "retired" && Mathf.Abs(ctx.Data.places[s.lot].lotWidth-exported.places[s.lot].lotWidth)<.05f &&
                    Mathf.Abs(ctx.Data.places[s.lot].lotDepth-exported.places[s.lot].lotDepth)<.05f &&
                    CityPhysics.Flat(new Vector3(ctx.Data.places[s.lot].x-exported.places[s.lot].x,0,ctx.Data.places[s.lot].z-exported.places[s.lot].z)).magnitude<.1f).ToArray();
            }
            Test("square-dismantled-and-assets-redistributed", layout != null && layout.oldSquareRemoved && layout.assets.Select(a => a.space).Distinct().Count() >= 15, layout == null ? 0 : layout.reusedPlazaPieces);
            if (layout == null) { Finish(); yield break; }
            Test("backyards-created-between-real-buildings", layout.spaces.Count(s => s.kind == "yard") >= 100, layout.spaces.Count(s => s.kind == "yard"));
            Test("small-parking-courts-and-pocket-gardens", layout.spaces.Count(s => s.kind == "parking") >= 6 && layout.spaces.Count(s => s.kind == "garden") >= 6, layout.spaces.Count(s => s.kind == "parking"));
            Test("plaza-trees-and-usable-benches-reused", layout.additionalPlazaTrees >= 50 && layout.additionalBenches >= 50, layout.additionalBenches);
            yield return new WaitForSeconds(1);
            int unsupported = 0, blocked = 0, steep = 0, checkedPoints = 0, missingNav = 0; float maxHeightError = 0;
            foreach (var s in layout.spaces)
            {
                Vector3 last = Vector3.zero; bool haveLast = false;
                foreach (var p in s.samples)
                {
                    checkedPoints++;
                    if (!Physics.Raycast(p + Vector3.up * 8, Vector3.down, out var hit, 16, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    float error = Mathf.Abs(hit.point.y - p.y); maxHeightError = Mathf.Max(maxHeightError, error);
                    if (error > .24f) { unsupported++; Problem("floor " + s.id + " " + p + " error=" + error); }
                    if (haveLast && Mathf.Abs(hit.point.y - last.y) > CityPhysics.Flat(hit.point - last).magnitude * .62f + .17f) { steep++; Problem("step " + s.id + " " + hit.point + " previous=" + last); }
                    last = hit.point; haveLast = true;
                    var obstacles = Physics.OverlapCapsule(hit.point + Vector3.up * .55f, hit.point + Vector3.up * 1.85f, .28f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
                    if (obstacles.Length > 0) { blocked++; Problem("blocked " + s.id + " " + p + " " + string.Join(",", obstacles.Select(c => c.name))); }
                }
                var start = s.samples[0];
                if (!ctx.Data.nav.Any(n => Mathf.Pow(n.x - start.x, 2) + Mathf.Pow(n.z - start.z, 2) < 1 && n.edges.Length > 0)) missingNav++;
            }
            Test("all-yard-and-parking-paths-supported", unsupported == 0, unsupported, "points=" + checkedPoints + " maxHeightError=" + maxHeightError);
            Test("gates-and-side-paths-clear-of-walls-and-furniture", blocked == 0, blocked);
            Test("access-paths-have-no-abrupt-ground-steps", steep == 0, steep);
            Test("every-new-space-connected-to-pedestrian-navigation", missingNav == 0, missingNav);
            int wallMissing = 0;
            foreach (var wall in layout.walls)
            {
                var p = wall.center + Vector3.up * (wall.height * .5f);
                if (!Physics.CheckBox(p, new Vector3(.035f, .08f, wall.length * .25f), Quaternion.Euler(0, wall.yaw * Mathf.Rad2Deg, 0), 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) wallMissing++;
            }
            Test("low-walls-have-real-collision", wallMissing == 0 && layout.walls.Length > 300, wallMissing, "walls=" + layout.walls.Length);
            var choices = layout.spaces.Where(s => s.kind == "yard" && s.samples.All(p => ctx.Free(new Vector3(p.x, ctx.Ground(p.x, p.z) + .04f, p.z), .32f))).ToArray();
            Test("yards-have-usable-walking-routes", choices.Length >= layout.spaces.Count(s => s.kind == "yard") * .8f, choices.Length);
            for (int i = 0; i < Mathf.Min(4, choices.Length); i++) yield return Walk(choices[i * (choices.Length / 4)], i);
            var benches = ctx.Data.seats.Skip(ctx.Data.seats.Length - layout.additionalBenches).Where(b =>
            {
                var p = Approach(b); return ctx.Free(p, .36f);
            }).Take(3).ToArray();
            foreach (var bench in benches)
            {
                ctx.Motor.SetInputLock(false); ctx.Motor.Warp(Approach(bench)); yield return new WaitForSeconds(.3f);
                var action = ctx.Actions.First(a => a.id == "seat-" + bench.id); bool enabled = action.Enabled == null || action.Enabled(); action.Execute(); yield return new WaitForSeconds(.9f);
                Test("reused-garden-bench-operates-" + bench.id, enabled && ctx.Motor.SeatHeight.HasValue && Mathf.Abs(ctx.Motor.SeatHeight.Value - .82f) < .08f);
                ctx.Actions.First(a => a.id == "stand").Execute(); yield return new WaitForSeconds(.9f);
                Test("reused-garden-bench-has-clear-exit-" + bench.id, !ctx.Motor.SeatHeight.HasValue && ctx.Motor.Grounded);
            }
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true);
            var yard = layout.spaces.Where(s => s.kind == "yard" && s.lot > 20).OrderByDescending(s => s.area).First();
            var home = ctx.Data.places[yard.lot]; var front = Quaternion.Euler(0, home.yaw * Mathf.Rad2Deg, 0) * Vector3.forward; var right = Vector3.Cross(Vector3.up, front);
            Capture("01-quintais-muros-jardins", yard.center - front * 24 + right * 18 + Vector3.up * 26, yard.center + Vector3.up * 1.5f, 55);
            var center = ReferenceMap.FromPixel(990, 355); center.y = ctx.Ground(center.x, center.z);
            Capture("02-bairro-com-lotes-ocupados", center + new Vector3(65, 100, -90), center + Vector3.up * 2);
            var garden = layout.assets.First(a => a.name == "fountain").position;
            Capture("03-assets-da-praca-redistribuidos", garden + new Vector3(24, 26, -26), garden);
            var parking = layout.spaces.First(s => s.kind == "parking");
            Capture("04-pequenos-estacionamentos", parking.center + new Vector3(18, 22, -22), parking.center + Vector3.up);
            Capture("05-local-da-praca-removida", layout.oldSquareCenter + new Vector3(35, 65, -65), layout.oldSquareCenter);
            foreach (var id in new[] { "yard-104", "yard-124" })
            {
                var access = layout.spaces.FirstOrDefault(s => s.id == id);
                if (access == null) continue;
                var point = access.samples[Mathf.Min(3, access.samples.Length - 1)];
                Capture("06-rampa-" + id, point + new Vector3(8, 9, -10), point);
            }
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish();
        }
        Vector3 Approach(SeatData seat) { var p = new Vector3(seat.x, 0, seat.z) + Quaternion.Euler(0, seat.yaw * Mathf.Rad2Deg, 0) * Vector3.forward * (seat.depth * .5f + .95f); p.y = ctx.Ground(p.x, p.z) + .05f; return p; }
        IEnumerator Walk(Space space, int serial)
        {
            ctx.Motor.SetInputLock(false); ctx.Motor.SetRide(null); ctx.Follow.enabled = false;
            var start = space.samples[0]; start.y = ctx.Ground(start.x, start.z) + .06f; ctx.Motor.Warp(start);
            ctx.Camera.transform.SetPositionAndRotation(start + Vector3.up * 6 - Vector3.forward * 8, Quaternion.identity); yield return new WaitForSeconds(.25f);
            // Traverse the full side access, including its corners and opening.
            for (int i = 2; i < space.samples.Length; i += 2)
            {
                var target = space.samples[i]; float timeout = ctx.Clock + CityPhysics.Flat(target - ctx.Player.position).magnitude / 2 + 1.5f;
                while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .35f && ctx.Clock < timeout)
                {
                    var direction = CityPhysics.Flat(target - ctx.Player.position).normalized;
                    // Keep the camera basis fixed while steering around the
                    // corners. Held movement deliberately retains its heading
                    // when the normal follow camera is pushed by a facade.
                    move = new Vector2(direction.x, direction.z); yield return null;
                }
                move = Vector2.zero;
                if (CityPhysics.Flat(target - ctx.Player.position).magnitude > .8f) { Problem("walk " + space.id + " target=" + target + " player=" + ctx.Player.position); break; }
            }
            move = Vector2.zero; yield return new WaitForSeconds(.25f);
            Test("walk-through-side-gate-into-yard-" + serial, ctx.Motor.Grounded && CityPhysics.Flat(ctx.Player.position - space.samples.Last()).magnitude < 1.4f, CityPhysics.Flat(ctx.Player.position - space.samples.Last()).magnitude, space.id);
        }
        void Problem(string text) { if (report.obstacles.Count < 100) report.obstacles.Add(text); }
        void Capture(string name, Vector3 eye, Vector3 target, float fieldOfView = 65)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = fieldOfView; camera.farClipPlane = 4000;
            // The normal LOD manager ticks every 120 ms. An immediate offscreen
            // camera render must evaluate the new viewpoint before capture.
            var lod = UnityEngine.Object.FindFirstObjectByType<CityLodManager>();
            if (lod) foreach (var item in lod.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            bool fog = RenderSettings.fog; RenderSettings.fog = false; var active = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = active; RenderSettings.fog = fog; rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish() { finished = true; File.WriteAllText(Path.Combine(dir, "neighborhood-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2); }
    }
}
