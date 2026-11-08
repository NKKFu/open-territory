using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in integration coverage using the shipped scene/controller, never a normal game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityCemeteryProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public int routeSamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>(), obstacles = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx;
        CityCemetery cemetery;
        Vector2 move;
        string dir;
        bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-cemetery-qa") >= 0)
                new GameObject("Cemetery QA • opt-in").AddComponent<CityCemeteryProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + trace); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("CEMETERY_QA " + name + " " + passed + " " + detail);
        }
        Vector3 Point(float x, float z) => cemetery.transform.TransformPoint(new Vector3(x, .07f, z));
        Vector3 Feet(Vector3 point) { point.y = ctx.Ground(point.x, point.z) + .05f; return point; }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "cemetery-qa");
            Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; cemetery = FindFirstObjectByType<CityCemetery>();
            Test("native-scene-ready", ctx && ctx.Ready && cemetery);
            if (!ctx || !ctx.Ready || !cemetery) { Finish(); yield break; }
            yield return new WaitForSeconds(.7f);
            Test("48-graves-six-usable-benches-eight-cypresses", cemetery.graveCount == 48 && cemetery.seats.Length == 6 && cemetery.GetComponentsInChildren<LODGroup>().Length == 8);
            Test("original-landscape-collisions-reserved", ctx.Data.solids.Where(s => cemetery.reservedNaturalSolids.Contains(s.index)).All(s => !s.active));
            var landscape = new HashSet<string>(ctx.Data.natureTrees.Select(t => "Colisão-" + t.solidIndex));
            foreach (var rock in ctx.Data.rocks) { landscape.Add("Colisão-" + rock.solidIndex); landscape.Add("Rocha • " + rock.solidIndex); }
            var foreign = Physics.OverlapBox(cemetery.transform.position + Vector3.up * 2.6f,
                new Vector3(cemetery.size.x * .5f + 1, 2.5f, cemetery.size.y * .5f + 1), cemetery.transform.rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(cemetery.transform) && !landscape.Contains(c.name)).ToArray();
            Test("footprint-keeps-existing-buildings-gardens-and-walls-clear", foreign.Length == 0, string.Join(", ", foreign.Select(c => c.name)));

            // Continuous support and body clearance across the entire authored route.
            int unsupported = 0, blocked = 0;
            for (int i = 1; i < cemetery.walkRoute.Length; i++)
            {
                var a = cemetery.walkRoute[i - 1]; var b = cemetery.walkRoute[i];
                int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .4f);
                for (int j = 0; j <= count; j++)
                {
                    var p = Vector3.Lerp(a, b, j / (float)Mathf.Max(1, count));
                    if (!Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var hit, 8, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    p.y = hit.point.y + .04f; report.routeSamples++;
                    if (Physics.CheckCapsule(p + Vector3.up * .43f, p + Vector3.up * 1.87f, .38f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                    { blocked++; if (report.obstacles.Count < 12) report.obstacles.Add("route " + i + " " + cemetery.transform.InverseTransformPoint(p)); }
                }
            }
            Test("continuous-access-alamedas-chapel-and-exit", unsupported == 0 && blocked == 0, "samples=" + report.routeSamples + " unsupported=" + unsupported + " blocked=" + blocked);

            var graveColliders = cemetery.GetComponentsInChildren<BoxCollider>().Where(c => c.name.StartsWith("Sepultura ") && (c.name.EndsWith("lápide") || c.name.EndsWith("cruz"))).ToArray();
            Test("every-headstone-blocks-the-body", graveColliders.Length == 48 && graveColliders.All(c => c.enabled && !c.isTrigger && c.gameObject.layer == CityContext.WorldLayer));
            ctx.Combat.Holster(); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 0;
            ctx.Motor.Warp(Feet(cemetery.streetApproach)); ctx.Motor.SetInputLock(true);
            Capture("01-cemiterio", new Vector3(43, 31, -47), new Vector3(0, 0, 0), 56);
            Capture("02-portao", new Vector3(1.5f, 3.2f, -38.5f), new Vector3(0, 2.5f, -26), 64);
            Capture("03-capela", new Vector3(.2f, 2.4f, 18.1f), new Vector3(0, 1.5f, 24.4f), 70);

            // Exercise movement through the real motor, including the sidewalk seam.
            ctx.Motor.SetInputLock(false); ctx.Camera.transform.rotation = Quaternion.identity; ctx.Input.TouchRun = true;
            bool walked = true;
            foreach (var target in cemetery.walkRoute)
            {
                float end = Time.realtimeSinceStartup + CityPhysics.Flat(target - ctx.Player.position).magnitude / 5 + 2;
                while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .40f && Time.realtimeSinceStartup < end)
                {
                    var direction = CityPhysics.Flat(target - ctx.Player.position).normalized;
                    move = new Vector2(direction.x, direction.z); yield return null;
                }
                if (CityPhysics.Flat(target - ctx.Player.position).magnitude > .9f)
                { walked = false; report.obstacles.Add("walk target=" + cemetery.transform.InverseTransformPoint(target) + " player=" + cemetery.transform.InverseTransformPoint(ctx.Player.position)); break; }
            }
            move = Vector2.zero; ctx.Input.TouchRun = false; yield return new WaitForSeconds(.35f);
            Test("walk-run-through-gate-around-graves-enter-chapel-and-leave", walked && ctx.Motor.Grounded);

            ctx.Motor.Warp(Feet(Point(-19, -14))); yield return new WaitForSeconds(.2f);
            var west = -cemetery.transform.right; move = new Vector2(west.x, west.z); ctx.Input.TouchRun = true;
            yield return new WaitForSeconds(1.2f); move = Vector2.zero; ctx.Input.TouchRun = false;
            yield return new WaitForSeconds(.2f);
            float wallX = cemetery.transform.InverseTransformPoint(ctx.Player.position).x;
            Test("solid-perimeter-stops-running-player", wallX > -21.5f && wallX < -20.5f, "localX=" + wallX);

            ctx.Motor.Warp(Feet(Point(0, -24))); yield return new WaitForSeconds(.2f);
            float ground = ctx.Player.position.y; ctx.Input.TouchJump = true;
            yield return new WaitForSeconds(.35f); bool airborne = ctx.Player.position.y > ground + .20f;
            yield return new WaitForSeconds(1.5f);
            Test("jump-and-land-on-cemetery-path", airborne && ctx.Motor.Grounded && Mathf.Abs(ctx.Player.position.y - ground) < .12f);

            foreach (var bench in cemetery.seats)
            {
                var front = Quaternion.Euler(0, bench.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                var approach = Feet(new Vector3(bench.x, 0, bench.z) + front * 1.2f);
                ctx.Motor.Warp(approach); move = Vector2.zero; yield return new WaitForSeconds(.2f);
                ctx.Camera.transform.position = approach + front * .35f + Vector3.up * 2.6f;
                ctx.Camera.transform.LookAt(new Vector3(bench.x, ctx.Ground(bench.x, bench.z) + .9f, bench.z));
                ctx.RefreshAction(); bool selected = ctx.CurrentAction != null && ctx.CurrentAction.id == "seat-" + bench.id;
                ctx.Input.TouchInteract = true; yield return new WaitForSeconds(.9f);
                bool sat = ctx.Motor.SeatHeight.HasValue;
                ctx.Input.TouchInteract = true; yield return new WaitForSeconds(.9f);
                Test("E-sit-and-stand-" + bench.id, selected && sat && !ctx.Motor.SeatHeight.HasValue && ctx.Motor.Grounded, "selected=" + selected + " sat=" + sat);
            }

            ctx.UI.Map.SetOpen(true); yield return new WaitForSeconds(.25f);
            float clock = ctx.Clock;
            Test("cemetery-is-a-map-destination", ctx.UI.Map.Places.Any(p => p.kind == "cemetery" && p.name == CityCemetery.DisplayName));
            yield return new WaitForSeconds(.25f);
            Test("map-keeps-city-running", ctx.UI.Map.IsOpen && ctx.Clock > clock + .10f && !ctx.Paused);
            ctx.UI.Map.SetOpen(false);
            ctx.Motor.Warp(Feet(Point(0, -22))); ctx.Motor.SetInputLock(true); ctx.Clock = 168;
            yield return new WaitForSeconds(.7f);
            var lights = cemetery.GetComponentsInChildren<Light>();
            Test("night-lanterns-follow-city-cycle", ctx.Night > .9f && lights.Length == 6 && lights.All(l => l.enabled && l.intensity > 1));
            Capture("04-cemiterio-noite", new Vector3(25, 12, -32), new Vector3(0, 1.5f, 0), 62);
            ctx.UI.SetSettings(true); clock = ctx.Clock; var pausedPosition = ctx.Player.position;
            yield return new WaitForSecondsRealtime(.35f);
            Test("Esc-settings-pause-and-resume", ctx.Paused && ctx.UI.Open && Mathf.Abs(ctx.Clock - clock) < .001f && Vector3.Distance(ctx.Player.position, pausedPosition) < .001f);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(.2f);
            Test("settings-return-to-exploration", !ctx.Paused && ctx.Clock > clock + .08f);
            ctx.Motor.SetInputLock(false); ctx.Follow.enabled = true;
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void Capture(string name, Vector3 localEye, Vector3 localTarget, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = cemetery.transform.TransformPoint(localEye);
            camera.transform.LookAt(cemetery.transform.TransformPoint(localTarget)); camera.fieldOfView = fov;
            var lod = FindFirstObjectByType<CityLodManager>();
            if (lod) foreach (var item in lod.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.antiAliasing = 2; rt.Create();
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = old;
            rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish()
        {
            finished = true; File.WriteAllText(Path.Combine(dir, "cemetery-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
