using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityBikeShopProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true; public int routeSamples;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report(); CityContext ctx; CityBikeShop shop; Vector2 move; string dir; bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-bike-shop-qa") >= 0)
                new GameObject("Bicycle shop acceptance • opt-in").AddComponent<CityBikeShopProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + trace); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("BIKE_SHOP_QA " + name + " " + passed + " " + detail); }
        Vector3 Feet(Vector3 p) { p.y = ctx.Ground(p.x, p.z) + .05f; return p; }
        Vector3 P(int wing, float x, float z) => Feet(shop.halls[wing].TransformPoint(new Vector3(x, .25f, z)));
        IEnumerator Walk(Vector3 target)
        {
            float deadline = Time.realtimeSinceStartup + CityPhysics.Flat(target - ctx.Player.position).magnitude / 4 + 4;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .35f && Time.realtimeSinceStartup < deadline)
            { var d = CityPhysics.Flat(target - ctx.Player.position).normalized; move = new Vector2(d.x, d.z); yield return null; }
            move = Vector2.zero;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "bike-shop-qa"); Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; shop = FindFirstObjectByType<CityBikeShop>();
            Test("native-bike-shop-ready", ctx && ctx.Ready && shop);
            if (!ctx || !ctx.Ready || !shop) { Finish(); yield break; }
            yield return new WaitForSeconds(.7f);
            Test("QA-storage-is-isolated", CityCommerce.IsReviewRun);
            Test("two-halls-six-displays-two-rideable-bikes", shop.halls.Length == 2 && shop.displayBikes == 6 && shop.bicycles.Length == 2);
            Test("old-buildings-doors-and-garage-retired", shop.retiredObjects.All(o => o.target && !o.target.activeSelf));
            Func<Vector3, bool> oldLot = point => shop.halls.Any(h => {
                var q = h.InverseTransformPoint(point);
                return Mathf.Abs(q.x) < 11.95f && q.z > -10.95f && q.z < 12.75f;
            });
            Test("old-solid-definitions-disabled", ctx.Data.solids.Where(s => shop.retiredSolids.Contains(s.index)
                && oldLot(new Vector3(s.x, s.bottom, s.z))).All(s => !s.active));
            // Numeric IDs alone cannot identify a retired panel: the legacy
            // export also assigned three of those IDs to distant live trees.
            var remoteTrees = ctx.Data.natureTrees.Where(t => shop.retiredSolids.Contains(t.solidIndex) && !oldLot(t.position)).ToArray();
            Test("remote-tree-solid-definitions-remain-active", remoteTrees.All(t => ctx.Data.solids.Any(s => s.index == t.solidIndex
                && s.owner == 0 && s.active && Vector2.Distance(new Vector2(s.x, s.z), new Vector2(t.position.x, t.position.z)) < .05f)), remoteTrees.Length.ToString());
            Test("old-fire-services-do-not-remain-at-the-shop", ctx.Data.services.Where(s => s.id == "service-12").All(s => Vector3.Distance(s.position, shop.halls[1].position) > 40) &&
                ctx.Data.devices.Where(s => s.id == "device-98").All(s => Vector3.Distance(s.position, shop.halls[1].position) > 40));
            Test("unique-bicycle-node-ids", shop.bicycles.Select(b => b.id).Distinct().Count() == 2 && shop.bicycles.All(b => ctx.Node(b.id) && ctx.Node(b.rider)));
            Test("English-shop-signs", shop.GetComponentsInChildren<Text>().Any(t => t.text == "SUNSPOKE CYCLES") && shop.GetComponentsInChildren<Text>().Any(t => t.text == "REPAIRS & SERVICE"));
            var titles = shop.GetComponentsInChildren<Text>().Where(t => t.text == "SUNSPOKE CYCLES" || t.text == "REPAIRS & SERVICE").ToArray();
            Test("shop-titles-fit-the-sign-panels", titles.Length == 2 && titles.All(t => t.preferredHeight <= t.rectTransform.rect.height + .1f),
                string.Join(", ", titles.Select(t => t.text + " required=" + t.preferredHeight + " available=" + t.rectTransform.rect.height)));
            ctx.Combat.Holster(); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 0;
            ctx.Motor.Warp(P(0, 0, 15)); ctx.Motor.SetInputLock(true);
            for (int wing = 0; wing < 2; wing++) shop.SetDoor(wing, 1); Physics.SyncTransforms();
            var blockers = new HashSet<string>(); int unsupported = 0;
            for (int wing = 0; wing < 2; wing++)
            {
                var hall = shop.halls[wing];
                var foreign = Physics.OverlapBox(hall.position + Vector3.up * 3.1f, new Vector3(11.35f, 2.8f, 9.8f), hall.rotation,
                    1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(shop.transform)).ToArray();
                Test("hall-" + wing + "-preserves-neighboring-buildings-and-furniture", foreign.Length == 0, string.Join(", ", foreign.Select(c => c.name)));
                for (int step = 0; step <= 110; step++) for (int side = -1; side <= 1; side++)
                {
                    var p = P(wing, side * .85f, Mathf.Lerp(15, -4, step / 110f)); report.routeSamples++;
                    if (!Physics.Raycast(p + Vector3.up * 2, Vector3.down, out var hit, 4, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    p.y = hit.point.y + .04f;
                    foreach (var c in Physics.OverlapCapsule(p + Vector3.up * .43f, p + Vector3.up * 1.84f, .36f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) blockers.Add(c.name);
                }
            }
            Test("wide-supported-sidewalk-entrances-and-interior-aisles", unsupported == 0 && blockers.Count == 0, "samples=" + report.routeSamples + " unsupported=" + unsupported + " obstacles=" + string.Join(", ", blockers));
            for (int wing = 0; wing < 2; wing++) shop.SetDoor(wing, 0);
            Capture("01-overview", new Vector3(585, 65, 633), new Vector3(585, 10, 706), 56);
            Capture("02-showroom-front", shop.halls[0].TransformPoint(new Vector3(-7, 3.1f, 23)), shop.halls[0].TransformPoint(new Vector3(0, 2.2f, 2)), 68);
            Capture("03-showroom-interior", shop.halls[0].TransformPoint(new Vector3(0, 2.3f, 7.3f)), shop.halls[0].TransformPoint(new Vector3(0, 1.8f, -5)), 85);
            Capture("04-workshop-interior", shop.halls[1].TransformPoint(new Vector3(0, 2.3f, 7.3f)), shop.halls[1].TransformPoint(new Vector3(0, 1.8f, -5)), 85);
            ctx.Camera.transform.rotation = Quaternion.identity; ctx.Motor.SetInputLock(false); ctx.Input.TouchRun = true;
            for (int wing = 0; wing < 2; wing++)
            {
                ctx.Motor.Warp(P(wing, 0, 15)); yield return new WaitForSeconds(.4f);
                foreach (float z in new[] { 11.7f, 7, -4, 7, 15 })
                {
                    var target = P(wing, 0, z); yield return Walk(target);
                    float gap = CityPhysics.Flat(target - ctx.Player.position).magnitude;
                    Test("walk-real-controller-hall-" + wing + "-z-" + z, gap < .9f && ctx.Motor.Grounded, "gap=" + gap.ToString("F3"));
                    if (gap >= .9f) break;
                }
            }
            ctx.Input.TouchRun = false; move = Vector2.zero;
            for (int wing = 0; wing < 2; wing++)
            {
                ctx.Motor.Warp(P(wing, 0, 10.8f)); yield return new WaitForSeconds(1.2f);
                Test("automatic-door-" + wing + "-opens", shop.DoorAmounts[wing] > .95f);
                ctx.UI.SetSettings(true); float amount = shop.DoorAmounts[wing], clock = ctx.Clock;
                yield return new WaitForSecondsRealtime(.4f);
                Test("Esc-freezes-door-" + wing + "-and-simulation", ctx.Paused && Mathf.Abs(ctx.Clock - clock) < .001f && Mathf.Abs(shop.DoorAmounts[wing] - amount) < .001f);
                ctx.UI.SetSettings(false); ctx.Motor.Warp(P(wing, 0, -4)); yield return new WaitForSeconds(2.3f);
                Test("automatic-door-" + wing + "-closes-after-resume", !ctx.Paused && shop.DoorAmounts[wing] < .01f);
                ctx.Motor.Warp(P(wing, 6.5f, 12)); yield return new WaitForSeconds(.2f);
                var direction = -shop.halls[wing].forward; move = new Vector2(direction.x, direction.z); ctx.Input.TouchRun = true;
                yield return new WaitForSeconds(.9f); move = Vector2.zero; ctx.Input.TouchRun = false;
                Test("glazing-" + wing + "-blocks-player", shop.halls[wing].InverseTransformPoint(ctx.Player.position).z > 9.7f);
            }
            ctx.Motor.Warp(P(0, 0, 3)); yield return new WaitForSeconds(.3f); float ground = ctx.Player.position.y;
            ctx.Input.TouchJump = true; yield return new WaitForSeconds(.4f); bool airborne = ctx.Player.position.y > ground + .2f;
            yield return new WaitForSeconds(1.5f); Test("jump-and-land-in-showroom", airborne && ctx.Motor.Grounded && Mathf.Abs(ctx.Player.position.y - ground) < .15f);
            ctx.UI.Map.SetOpen(true); yield return new WaitForSeconds(.2f);
            Test("bike-shop-appears-on-Places-map", ctx.UI.Map.Places.Any(p => p.kind == "bike-shop" && p.name == CityBikeShop.DisplayName)); ctx.UI.Map.SetOpen(false);
            Test("shop-bikes-appear-through-normal-population", shop.bicycles.All(b => ctx.Vehicles.IsSpawned(b.id)));
            foreach (var data in shop.bicycles)
            {
                var bike = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == data.id) ?? ctx.Vehicles.Spawn(data, data.position, Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0));
                Test("rideable-bike-" + data.id + "-spawned", bike != null && bike.Body && bike.WheelPivots.Length == 2);
                if (bike == null) continue;
                ctx.Motor.Warp(Feet(bike.Root.TransformPoint(new Vector3(-1.1f, 0, 0))));
                ctx.Camera.transform.position = bike.Position + new Vector3(-5, 3.0f, -2); ctx.Camera.transform.LookAt(bike.Position + Vector3.up);
                yield return new WaitForSeconds(.4f); ctx.RefreshAction();
                Test("E-selects-shop-bike-" + data.id, ctx.CurrentAction != null && ctx.CurrentAction.id == "vehicle-" + data.id, ctx.CurrentAction != null ? ctx.CurrentAction.id : "none");
                ctx.Input.TouchInteract = true; yield return new WaitForSeconds(.5f);
                bool mounted = ctx.Motor.Riding == bike.Root; Test("E-mounts-shop-bike-" + data.id, mounted && bike.Rider && bike.Rider.gameObject.activeInHierarchy);
                if (!mounted) continue;
                float pedal = bike.Pedal; move = Vector2.up; yield return new WaitForSeconds(1.1f); move = Vector2.zero;
                Test("shop-bike-" + data.id + "-pedals-and-moves", Mathf.Abs(bike.Pedal - pedal) > .15f && bike.Speed > .5f, "speed=" + bike.Speed.ToString("F3"));
                ctx.Input.TouchBrake = true; yield return new WaitForSeconds(1.4f); ctx.Input.TouchBrake = false; ctx.Input.TouchInteract = true; yield return new WaitForSeconds(.5f);
                Test("E-dismounts-shop-bike-" + data.id, !ctx.Motor.Riding && ctx.Motor.Controller.enabled);
            }
            ctx.Motor.Warp(P(1, 0, 3)); ctx.Motor.SetInputLock(true); ctx.Clock = 168; yield return new WaitForSeconds(.8f);
            Test("night-lighting-follows-city-cycle", ctx.Night > .9f && shop.lights.All(l => l && l.enabled && l.intensity > 1.8f));
            Capture("05-night-front", new Vector3(586, 16, 666), new Vector3(587, 12, 703), 65);
            ctx.Motor.SetInputLock(false); ctx.Follow.enabled = true; Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count); Finish();
        }
        void Capture(string name, Vector3 eye, Vector3 target, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>(); if (manager) foreach (var lod in manager.objects) if (lod && lod.isActiveAndEnabled) lod.ApplyLevel(lod.SelectLevel(camera));
            var previous = RenderTexture.active; var texture = new RenderTexture(1600, 1000, 24) { antiAliasing = 2 }; texture.Create();
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            var png = new Texture2D(1600, 1000, TextureFormat.RGB24, false); png.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); png.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), png.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = previous; texture.Release(); Destroy(texture); Destroy(png);
        }
        void Finish()
        {
            finished = true; move = Vector2.zero; File.WriteAllText(Path.Combine(dir, "bike-shop-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
