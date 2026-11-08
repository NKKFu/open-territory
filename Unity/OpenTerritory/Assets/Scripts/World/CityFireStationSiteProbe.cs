using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in acceptance of the relocated building and both street approaches.
    public sealed class CityFireStationSiteProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool passed = true; public int floorSamples;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityFireStationSite site; CityFireGarage bay;
        Vector2 move; string directory; bool finished; float started;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-fire-station-qa") >= 0)
                new GameObject("Fire station QA • opt-in").AddComponent<CityFireStationSiteProbe>();
        }
        void Awake() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - started > 145) { Test("timeout", false); StopAllCoroutines(); Finish(); return; }
            if (ctx) ctx.Input.TouchMove = move;
        }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("FIRE_STATION_QA " + name + " " + passed + " " + detail); }
        Vector3 Point(float x, float z)
        { var p = site.transform.TransformPoint(new Vector3(x, .2f, z)); p.y = ctx.Ground(p.x, p.z) + .04f; return p; }
        IEnumerator Walk(float x, float z)
        {
            var goal = Point(x, z); float deadline = Time.realtimeSinceStartup + Vector3.Distance(ctx.Player.position, goal) / 2 + 5;
            ctx.Camera.transform.rotation = Quaternion.identity;
            while (CityPhysics.Flat(goal - ctx.Player.position).magnitude > .2f && Time.realtimeSinceStartup < deadline)
            { var d = CityPhysics.Flat(goal - ctx.Player.position).normalized; move = new Vector2(d.x, d.z); yield return null; }
            move = Vector2.zero; yield return new WaitForSeconds(.25f);
            Test("walk-" + x + "-" + z, CityPhysics.Flat(goal - ctx.Player.position).magnitude < .6f && ctx.Motor.Grounded,
                "actual=" + site.transform.InverseTransformPoint(ctx.Player.position));
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/fire-station-red/editor"));
            Directory.CreateDirectory(directory);
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup - started < 70) yield return null;
            ctx = CityContext.Instance; site = FindFirstObjectByType<CityFireStationSite>(); bay = FindFirstObjectByType<CityFireGarage>();
            Test("relocated-station-ready", ctx && ctx.Ready && site && bay);
            if (!ctx || !site || !bay) { Finish(); yield break; }
            ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.UI.SetSettings(false); ctx.Combat.Holster(); ctx.Systems.Weather.Request("clear", true); ctx.Clock = 35;
            var place = ctx.Data.places[CityFireStationSite.PlaceIndex];
            Test("map-and-place-use-new-fire-station", place.kind == "fire" && place.name == "Fire Station" && Vector2.Distance(new Vector2(place.x, place.z), new Vector2(site.transform.position.x, site.transform.position.z)) < .01f && place.door != null);
            var bike = FindFirstObjectByType<CityBikeShop>();
            Test("bike-shop-preserved-at-original-site", bike && bike.halls.Length == 2 && Vector3.Distance(bike.transform.position, new Vector3(587, 9.65f, 704)) < .1f && bike.halls.All(h => h.gameObject.activeInHierarchy));
            var foreign = Physics.OverlapBox(site.transform.position + Vector3.up * 7, new Vector3(12.6f, 7, 12.6f), site.transform.rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(site.transform)).ToArray();
            Test("no-neighbour-tree-wall-or-furniture-overlap", foreign.Length == 0, string.Join(",", foreign.Select(c => c.name)));
            var paints = site.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial && r.sharedMaterial.name.StartsWith("Fire station red")).ToArray();
            Test("walls-roof-trim-and-garage-are-red", paints.Length >= 38 && paints.All(r => r.sharedMaterial.color.r > r.sharedMaterial.color.g * 3 && r.sharedMaterial.color.r > r.sharedMaterial.color.b * 3), "paintedRenderers=" + paints.Length);
            Test("water-interaction-and-hack-moved", ctx.Data.services.Any(s => s.id == "service-12" && place.Contains(s.position)) && ctx.Data.devices.Any(d => d.id == "device-98" && place.Contains(d.position)));
            Test("firefighter-and-bike-mechanic-both-present", ctx.Data.residents.Any(r => r.id == site.firefighter.id && r.role == "fire" && place.Contains(r.position)) && ctx.Data.residents.Any(r => r.id == 7971 && r.place == CityBikeShop.DisplayName));
            Test("navigation-reconnected-to-existing-sidewalk", site.entrance.Length == 6 && ctx.Data.nav.First(n => n.id == site.connection).edges.Any(e => e.to == site.entrance.Last().id));
            int missing = 0, blocked = 0;
            for (float x = -10; x <= 10; x += 2) for (float z = -9; z <= 10; z += 1)
            {
                var p = site.transform.TransformPoint(new Vector3(x, 1.2f, z)); report.floorSamples++;
                if (!Physics.Raycast(p, Vector3.down, 2, 1 << CityContext.GroundLayer)) missing++;
            }
            foreach (float x in new[] { -9f, -6.2f, -3.3f, -1.1f, 0f, 1.1f }) for (float z = 13; z <= (x < -2 ? 29 : 26); z += .25f)
            {
                var p = Point(x, z); report.floorSamples++;
                if (!Physics.Raycast(p + Vector3.up * .5f, Vector3.down, .8f, 1 << CityContext.GroundLayer)) missing++;
                if (Physics.CheckCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.8f, .35f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) blocked++;
            }
            Test("interior-and-both-street-approaches-have-continuous-floor", missing == 0, "samples=" + report.floorSamples + " missing=" + missing);
            Test("driveway-and-pedestrian-access-unobstructed", blocked == 0, "blocked=" + blocked);
            Capture("01-red-station-waterfront", new Vector3(37, 25, 43), new Vector3(0, 6.8f, 0), 53);
            Capture("02-rear-and-roof", new Vector3(-30, 26, -32), new Vector3(0, 6, 0), 52);
            ctx.Motor.Warp(Point(0, 26)); yield return new WaitForSeconds(.4f);
            yield return Walk(0, 12.4f); yield return Walk(0, 9.7f);
            Capture("03-personnel-entry", new Vector3(4, 2, 19), new Vector3(0, 2, 10), 62);
            ctx.Motor.Warp(Point(-8.5f, 5.5f)); yield return new WaitForSeconds(.4f);
            yield return Walk(-8.5f, 26.5f);
            Test("new-fire-truck-parking-record", ctx.Data.vehicles.Any(v => v.id == bay.vehicle && Vector3.Distance(v.position, bay.TruckPosition) < .01f));
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void Capture(string name, Vector3 eye, Vector3 target, float fov)
        {
            var camera = ctx.Camera; camera.transform.position = site.transform.TransformPoint(eye); camera.transform.LookAt(site.transform.TransformPoint(target)); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>(); if (manager) foreach (var item in manager.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var previous = RenderTexture.active; var rt = new RenderTexture(1440, 1000, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1440, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1440, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Destroy(rt); Destroy(image);
        }
        void Finish()
        {
            finished = true; if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.UI.SetSettings(false); }
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "fire-station-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
