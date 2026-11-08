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
    public sealed class CityCarDealershipProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public int routeSamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityCarDealership shop; Vector2 move; string dir; bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-dealership-qa") >= 0)
                new GameObject("Car dealership QA • opt-in").AddComponent<CityCarDealershipProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + trace); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("DEALERSHIP_QA " + name + " " + passed + " " + detail);
        }
        Vector3 P(float x, float z) => shop.transform.TransformPoint(new Vector3(x, .06f, z));
        Vector3 Feet(Vector3 p) { p.y = ctx.Ground(p.x, p.z) + .05f; return p; }
        IEnumerator Walk(Vector3 target)
        {
            float deadline = Time.realtimeSinceStartup + CityPhysics.Flat(target - ctx.Player.position).magnitude / 5 + 3;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .38f && Time.realtimeSinceStartup < deadline)
            {
                var d = CityPhysics.Flat(target - ctx.Player.position).normalized;
                move = new Vector2(d.x, d.z); yield return null;
            }
            move = Vector2.zero;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "dealership-qa");
            Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; shop = FindFirstObjectByType<CityCarDealership>();
            Test("native-dealership-ready", ctx && ctx.Ready && shop);
            if (!ctx || !ctx.Ready || !shop) { Finish(); yield break; }
            yield return new WaitForSeconds(.7f);
            Test("QA-storage-is-isolated", CityCommerce.IsReviewRun);
            Test("ten-display-cars-with-physical-bodies", shop.displayCars.Length == 10 && shop.displayCars.All(t => t && t.GetComponent<Collider>().enabled));
            Test("native-landscape-reserved-for-the-lot", ctx.Data.solids.Where(s => shop.reservedNaturalSolids.Contains(s.index)).All(s => !s.active));
            var cemetery = FindFirstObjectByType<CityCemetery>();
            Test("cemetery-preserved-on-opposite-side-of-street", cemetery && cemetery.graveCount == 48 && cemetery.seats.Length == 6 &&
                Vector3.Dot(CityPhysics.Flat(cemetery.transform.position - shop.transform.position).normalized, -shop.transform.forward) > .85f);
            var natural = new HashSet<string>(ctx.Data.natureTrees.Select(t => "Colisão-" + t.solidIndex));
            foreach (var rock in ctx.Data.rocks) { natural.Add("Colisão-" + rock.solidIndex); natural.Add("Rocha • " + rock.solidIndex); }
            var foreign = Physics.OverlapBox(shop.transform.position + Vector3.up * 3, new Vector3(31.4f, 3.1f, 30.4f), shop.transform.rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(shop.transform) && !natural.Contains(c.name)).ToArray();
            Test("lot-preserves-existing-walls-gardens-buildings-and-furniture", foreign.Length == 0, string.Join(", ", foreign.Select(c => c.name)));
            Test("signage-is-English", shop.GetComponentsInChildren<Text>().Any(t => t.text == "S U N   M O T O R S") &&
                shop.GetComponentsInChildren<Text>().Any(t => t.text == "NEW & PRE-OWNED") && shop.GetComponentsInChildren<Text>().Any(t => t.text == "SALES"));

            ctx.Combat.Holster(); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 0;
            ctx.Motor.Warp(Feet(shop.streetApproach)); ctx.Motor.SetInputLock(true); shop.SetDoorPose(1); Physics.SyncTransforms();
            int unsupported = 0, blocked = 0; var obstacles = new HashSet<string>();
            for (int i = 1; i < shop.walkRoute.Length; i++)
            {
                var a = shop.walkRoute[i - 1]; var b = shop.walkRoute[i]; int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .35f);
                for (int j = 0; j <= count; j++)
                {
                    var p = Vector3.Lerp(a, b, j / (float)Mathf.Max(1, count));
                    if (!Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var hit, 8, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    p.y = hit.point.y + .04f; report.routeSamples++;
                    foreach (var c in Physics.OverlapCapsule(p + Vector3.up * .43f, p + Vector3.up * 1.87f, .38f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                    { blocked++; obstacles.Add(c.name + " route=" + i); }
                }
            }
            Test("continuous-sidewalk-entrance-showroom-lounge-and-courtyard", unsupported == 0 && blocked == 0,
                "samples=" + report.routeSamples + " unsupported=" + unsupported + " blocked=" + blocked + " " + string.Join(", ", obstacles));
            shop.SetDoorPose(0); Physics.SyncTransforms();
            Capture("01-overview", new Vector3(70, 58, -92), new Vector3(0, 0, -26), 57);
            Capture("02-street-front", new Vector3(7, 3.7f, -40), new Vector3(-6, 2.6f, 7), 65);
            Capture("03-showroom", new Vector3(1.8f, 2.7f, 4.8f), new Vector3(-14, 1.4f, 19), 76);
            Capture("04-reception", new Vector3(-1, 2.6f, 15.6f), new Vector3(7, 1.5f, 23), 70);
            ctx.Camera.transform.rotation = Quaternion.identity; ctx.Motor.SetInputLock(false); ctx.Input.TouchRun = true;
            bool walked = true;
            for (int i = 0; i < shop.walkRoute.Length; i++)
            {
                yield return Walk(shop.walkRoute[i]);
                float gap = CityPhysics.Flat(shop.walkRoute[i] - ctx.Player.position).magnitude;
                Test("walk-real-controller-waypoint-" + i, gap < .9f, "gap=" + gap.ToString("F3"));
                if (gap >= .9f) { walked = false; break; }
            }
            move = Vector2.zero; ctx.Input.TouchRun = false; yield return new WaitForSeconds(.3f);
            Test("enter-explore-and-leave-with-real-controller", walked && ctx.Motor.Grounded);
            ctx.Motor.Warp(Feet(P(1, 1))); yield return new WaitForSeconds(.8f);
            Test("automatic-door-opens-for-player", shop.Occupied && shop.OpenAmount > .98f);
            ctx.UI.SetSettings(true); float open = shop.OpenAmount, clock = ctx.Clock;
            yield return new WaitForSecondsRealtime(.35f);
            Test("Esc-freezes-door-and-city", ctx.Paused && Mathf.Abs(shop.OpenAmount - open) < .0001f && Mathf.Abs(ctx.Clock - clock) < .0001f);
            ctx.UI.SetSettings(false); ctx.Motor.Warp(Feet(P(1, -10))); yield return new WaitForSeconds(2.7f);
            Test("door-closes-after-clearance-and-resume", !ctx.Paused && !shop.Occupied && shop.OpenAmount < .01f);

            ctx.Motor.Warp(Feet(P(9.5f, -2))); yield return new WaitForSeconds(.2f);
            var direction = shop.transform.forward; move = new Vector2(direction.x, direction.z); ctx.Input.TouchRun = true;
            yield return new WaitForSeconds(.8f); move = Vector2.zero; ctx.Input.TouchRun = false; yield return new WaitForSeconds(.15f);
            Test("glass-facade-blocks-player-outside-entrance", shop.transform.InverseTransformPoint(ctx.Player.position).z < .7f);
            ctx.Motor.Warp(Feet(P(1, -8))); yield return new WaitForSeconds(.2f);
            float ground = ctx.Player.position.y; ctx.Input.TouchJump = true; yield return new WaitForSeconds(.35f);
            bool airborne = ctx.Player.position.y > ground + .2f; yield return new WaitForSeconds(1.5f);
            Test("jump-and-land-in-courtyard", airborne && ctx.Motor.Grounded && Mathf.Abs(ctx.Player.position.y - ground) < .15f);

            ctx.UI.Map.SetOpen(true); yield return new WaitForSeconds(.2f);
            Test("Sun-Motors-is-on-Places-map", ctx.UI.Map.Places.Any(p => p.kind == "car-dealership" && p.name == CityCarDealership.DisplayName));
            ctx.UI.Map.SetOpen(false); ctx.Clock = 168; ctx.Motor.SetInputLock(true); yield return new WaitForSeconds(.7f);
            Test("night-lights-follow-city-cycle", ctx.Night > .9f && shop.lights.Length == 9 && shop.lights.All(l => l && l.enabled));
            Capture("05-night-front", new Vector3(29, 9, -34), new Vector3(-3, 2.5f, 5), 65);
            // Advance a complete day instead of rewinding service deadlines.
            ctx.Clock = 300; yield return new WaitForSeconds(.5f);
            // The city's clear-weather fog floor leaves 0.040425 intensity:
            // slightly above its 0.04 enable threshold. Match that shared policy.
            float expected = 2.1f * Mathf.Max(ctx.Night, ctx.Systems.Weather.Fog * .55f, ctx.Systems.Weather.Rain * .5f);
            Test("daylight-lights-follow-city-ambient-policy", ctx.Night < .01f && shop.lights.All(l => l &&
                Mathf.Abs(l.intensity - expected) < .002f && l.enabled == (l.intensity > .04f)),
                "night=" + ctx.Night + " fog=" + ctx.Systems.Weather.Fog + " rain=" + ctx.Systems.Weather.Rain + " expected=" + expected +
                " actual=" + string.Join(",", shop.lights.Select(l => l ? l.intensity.ToString("F6") : "missing")));
            ctx.Motor.SetInputLock(false); ctx.Follow.enabled = true;
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void Capture(string name, Vector3 localEye, Vector3 localTarget, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = shop.transform.TransformPoint(localEye);
            camera.transform.LookAt(shop.transform.TransformPoint(localTarget)); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>();
            if (manager) foreach (var item in manager.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 2 }; rt.Create();
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = old;
            rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish()
        {
            finished = true; File.WriteAllText(Path.Combine(dir, "dealership-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
