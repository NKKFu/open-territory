using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // No scene component or normal-game installation. Explicit QA flag only.
    public sealed class CityLightingProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Sample { public string kind; public int node, place; public Vector3 position; public float nominalIntensity, range; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
            public List<Sample> fixtures = new List<Sample>();
        }
        readonly Report report = new Report();
        CityContext ctx;
        CityLighting lighting;
        string directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-lighting-qa") >= 0)
                new GameObject("QA iluminação • opt-in").AddComponent<CityLightingProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text);
        }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed;
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("../../Reports/lighting/placement");
            Directory.CreateDirectory(directory);
            float end = Time.realtimeSinceStartup + 60;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < end) yield return null;
            ctx = CityContext.Instance; lighting = FindFirstObjectByType<CityLighting>();
            CheckResult("native-lighting-ready", ctx && ctx.Ready && lighting);
            if (!ctx || !ctx.Ready || !lighting) { Finish(); yield break; }
            yield return new WaitForSecondsRealtime(.5f);
            Vector3 origin = ctx.Player.position;
            float clock = ctx.Clock, fov = ctx.Camera.fieldOfView; bool paused = ctx.Paused, follow = ctx.Follow.enabled;
            Vector3 cameraOrigin = ctx.Camera.transform.position; Quaternion cameraRotation = ctx.Camera.transform.rotation;
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false; ctx.Pause(true);
            int interiors = 0, street = 0, road = 0, pier = 0, unmounted = 0, blocked = 0, outside = 0, roof = 0;
            foreach (var fixture in lighting.fixtures)
            {
                if (!fixture.source || !fixture.lens || !fixture.anchor) { unmounted++; continue; }
                Vector3 position = fixture.source.transform.position;
                bool interior = fixture.place >= 0;
                if (interior) interiors++; else if (fixture.kind == "poste") street++; else if (fixture.kind == "poste viário") road++; else pier++;
                // Intersect the *rendered lamp mesh*, independently of the baked anchor data.
                if (!HitsLampMesh(fixture, interior || fixture.kind.StartsWith("poste") ? Vector3.up : Vector3.down)) unmounted++;
                if (Physics.CheckSphere(position, .035f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) blocked++;
                if (interior)
                {
                    var place = ctx.Data.places[fixture.place];
                    if (!CityLighting.Inside(place, position) || position.y - place.elevation < 4.5f) outside++;
                    if (!Physics.Raycast(position, Vector3.up, .4f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) roof++;
                }
                report.fixtures.Add(new Sample { kind = fixture.kind, node = fixture.node, place = fixture.place, position = position,
                    nominalIntensity = fixture.intensity, range = fixture.source.range });
            }
            CheckResult("all-interiors-use-authored-ceiling-panels", interiors == 434 && Enumerable.Range(0, ctx.Data.places.Length).All(i => lighting.fixtures.Any(f => f.place == i)), "sources=" + interiors);
            CheckResult("all-street-lights-use-physical-bulbs", street == ctx.Data.streetLamps.Length, "sources=" + street);
            CheckResult("streets-have-additional-fixed-lamp-coverage", road > 150, "sources=" + road);
            CheckResult("pier-bollards-have-local-sources", pier == 4, "sources=" + pier);
            CheckResult("every-source-touches-visible-lamp", unmounted == 0, "failures=" + unmounted);
            CheckResult("sources-clear-of-solid-walls-and-ceilings", blocked == 0, "failures=" + blocked);
            CheckResult("interior-sources-within-rotated-floorplan", outside == 0, "failures=" + outside);
            CheckResult("interior-sources-just-below-real-ceilings", roof == 0, "failures=" + roof);
            CheckResult("no-floating-runtime-cube-luminaires", !FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("Luminária • ")));
            CheckResult("source-power-balanced-for-overlapping-fixtures", lighting.fixtures.All(f => f.intensity > .5f && f.intensity <= 1.5f));

            int dayFailures = 0, approachFailures = 0;
            foreach (var group in lighting.fixtures.Where(f => f.place >= 0).GroupBy(f => f.place))
            {
                var place = ctx.Data.places[group.Key];
                ctx.Motor.Warp(new Vector3(place.x, place.elevation + .2f, place.z));
                ctx.Camera.transform.position = ctx.Player.position + Vector3.up * 1.8f;
                lighting.Tick(0);
                if (group.Any(f => !f.source.enabled)) dayFailures++;
                var outsidePoint = new Vector3((place.lotWidth > 0 ? place.lotWidth : place.w) * .5f + .5f, .2f, 0);
                ctx.Motor.Warp(new Vector3(place.x, place.elevation, place.z) + Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0) * outsidePoint);
                ctx.Camera.transform.position = ctx.Player.position + Vector3.up * 1.8f;
                lighting.Tick(0);
                if (group.Any(f => !f.source.enabled)) approachFailures++;
            }
            CheckResult("every-occupied-interior-lit-by-day", dayFailures == 0, "failures=" + dayFailures);
            CheckResult("nearby-unoccupied-houses-stay-lit-by-day", approachFailures == 0, "failures=" + approachFailures);
            int streetDayFailures = 0;
            foreach (var fixture in lighting.fixtures.Where(f => f.place < 0))
            {
                ctx.Motor.Warp(fixture.source.transform.position - Vector3.up * 2);
                ctx.Camera.transform.position = ctx.Player.position + Vector3.back * 6;
                lighting.Tick(0);
                if (!fixture.source.enabled) streetDayFailures++;
            }
            CheckResult("nearby-street-and-pier-lights-stay-lit-by-day", streetDayFailures == 0, "failures=" + streetDayFailures);
            var testPlace = ctx.Data.places[17];
            Vector3 testCenter = new Vector3(testPlace.x, testPlace.elevation + 1, testPlace.z);
            float previousLevel = 1; bool gradual = true;
            for (int step = 0; step <= 80; step++)
            {
                Vector3 point = testCenter + Quaternion.Euler(0, testPlace.yaw * Mathf.Rad2Deg, 0) * Vector3.right * (testPlace.lotWidth * .5f + step);
                float level = CityLighting.RoomLevel(testPlace, point, point, 0);
                gradual &= level <= previousLevel + .001f && previousLevel - level < .09f; previousLevel = level;
            }
            CheckResult("proximity-fades-smoothly-without-switching", gradual && previousLevel == 0);
            CheckResult("every-building-has-contained-indirect-light", lighting.rooms.Length == ctx.Data.places.Length && lighting.rooms.All(r => r.surfaces.Length > 0));

            var windows = FindFirstObjectByType<CityWindows>();
            CheckResult("every-building-has-glazed-windows", windows && windows.windows.Length > 1600 && Enumerable.Range(0, ctx.Data.places.Length).All(i => windows.windows.Count(w => w.place == i) >= 6));
            CheckResult("window-glazing-uses-transparent-native-shader", windows && windows.glazing.Length == ctx.Data.places.Length && windows.glazing.All(g => g.renderer && g.renderer.sharedMaterial.shader.name == "OpenTerritory/CityWindow"));
            int sealedWindows = 0;
            if (windows) foreach (var window in windows.windows)
            {
                var place = ctx.Data.places[window.place]; var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
                Vector3 sample = window.center + Vector3.Cross(Vector3.up, window.outward) * window.width * .25f + Vector3.up * window.height * .25f;
                if (lighting.rooms[window.place].surfaces.Any(r => HitsMesh(r, sample + window.outward * .35f, -window.outward * .7f))) sealedWindows++;
            }
            CheckResult("windows-are-openings-through-rendered-walls", sealedWindows == 0, "sealed=" + sealedWindows);
            var positions = lighting.fixtures.Select(f => f.source.transform.position).ToArray();
            ctx.Motor.Warp(ctx.Data.spawn + Vector3.one * 10000);
            ctx.Camera.transform.position = ctx.Data.spawn + Vector3.one * 10000; lighting.Tick(1);
            CheckResult("distant-sources-disabled", lighting.fixtures.All(f => !f.source.enabled));
            ctx.Camera.transform.position = ctx.Data.spawn; lighting.Tick(1);
            CheckResult("camera-never-repositions-sources", lighting.fixtures.Select((f, i) => Vector3.Distance(f.source.transform.position, positions[i]) < .001f).All(v => v));
            var fixtureToBreak = lighting.fixtures.First(f => f.kind == "poste");
            var root = ctx.Data.streetLamps.First(s => s.bulb == fixtureToBreak.node);
            var pole = ctx.Node(root.node); bool active = pole.gameObject.activeSelf;
            ctx.Camera.transform.position = fixtureToBreak.source.transform.position + Vector3.right * 3;
            pole.gameObject.SetActive(false); lighting.Tick(1);
            CheckResult("broken-pole-turns-off-its-own-light", !fixtureToBreak.source.enabled);
            pole.gameObject.SetActive(active);
            bool legacyOff = ctx.Data.streetLightIds.Concat(ctx.Data.shopLightIds).Append(ctx.Data.canopyLightId)
                .All(id => !ctx.Node(id) || !ctx.Node(id).GetComponent<Light>() || !ctx.Node(id).GetComponent<Light>().enabled);
            CheckResult("no-detached-legacy-pool-or-station-lights", legacyOff);

            ctx.Clock = 165; ctx.Systems.Weather.Request("clear", true);
            ctx.Systems.SendMessage("UpdateSky");
            int image = 0;
            foreach (int index in new[] { 10, 12, 13, 9, 8 })
            {
                var place = ctx.Data.places[index];
                var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
                Vector3 center = new Vector3(place.x, place.elevation, place.z);
                float depth = place.lotDepth > 0 ? place.lotDepth : place.d;
                ctx.Motor.Warp(center + rotation * new Vector3(0, .2f, depth * .5f + 2));
                ctx.Camera.transform.position = center + rotation * new Vector3(0, 1.9f, depth * .5f - 1.5f);
                ctx.Camera.transform.LookAt(center + rotation * new Vector3(0, 3.5f, -depth * .25f));
                ctx.Pause(false); lighting.Tick(1); yield return new WaitForSeconds(.35f); ctx.Pause(true);
                Capture((++image).ToString("00") + "-interior-" + index);
            }
            foreach (var fixture in new[] { lighting.fixtures.First(f => f.kind == "poste"), lighting.fixtures.First(f => f.kind == "balizador do cais") })
            {
                Vector3 center = fixture.source.transform.position;
                ctx.Camera.transform.position = center + new Vector3(6, 1, -8);
                ctx.Camera.transform.LookAt(center - Vector3.up * 1.2f);
                ctx.Pause(false); lighting.Tick(1); yield return new WaitForSeconds(.35f); ctx.Pause(true);
                Capture((++image).ToString("00") + "-exterior-" + fixture.node);
            }
            foreach (int index in new[] { 17, 12 })
            {
                var place = ctx.Data.places[index]; var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
                Vector3 center = new Vector3(place.x, place.elevation, place.z);
                ctx.Motor.Warp(center + rotation * new Vector3(0, .2f, place.lotDepth * .5f + 4));
                Vector3 view = center + rotation * new Vector3(place.lotWidth * .42f, 4.1f, place.lotDepth * .5f + 11);
                view.y = Mathf.Max(view.y, ctx.Ground(view.x, view.z) + 3);
                ctx.Camera.transform.position = view;
                ctx.Camera.transform.LookAt(center + Vector3.up * 2.6f);
                ctx.Pause(false); lighting.Tick(1); yield return new WaitForSeconds(.35f); ctx.Pause(true);
                Capture((++image).ToString("00") + "-windows-night-" + index);
            }
            var roadLamp = lighting.fixtures.Where(f => f.kind == "poste viário").OrderBy(f => Vector3.Distance(f.source.transform.position, ctx.Data.spawn)).First();
            var lampPoint = roadLamp.source.transform.position;
            ctx.Motor.Warp(lampPoint - Vector3.up * 4.8f + roadLamp.anchor.right * 7);
            ctx.Camera.transform.position = ctx.Player.position + Vector3.up * 1.7f - roadLamp.anchor.forward * 12;
            ctx.Camera.transform.LookAt(lampPoint - Vector3.up * 3);
            ctx.Pause(false); lighting.Tick(1); yield return new WaitForSeconds(.35f); ctx.Pause(true);
            Capture((++image).ToString("00") + "-street-night");
            ctx.Clock = 0; ctx.Systems.SendMessage("UpdateSky"); lighting.Tick(0); Capture((++image).ToString("00") + "-street-day");
            ctx.UI.SetSettings(true);
            CheckResult("settings-still-pause-simulation", ctx.UI.Open && ctx.Paused && Time.timeScale == 0);
            ctx.UI.SetSettings(false);
            CheckResult("settings-still-resume-simulation", !ctx.UI.Open && !ctx.Paused && Time.timeScale == 1);
            ctx.Motor.Warp(origin); ctx.Clock = clock; ctx.Camera.transform.SetPositionAndRotation(cameraOrigin, cameraRotation); ctx.Camera.fieldOfView = fov;
            ctx.Follow.enabled = follow; ctx.Motor.SetInputLock(false); ctx.Pause(paused);
            CheckResult("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }

        static bool HitsLampMesh(CityLighting.Fixture fixture, Vector3 direction)
        {
            return HitsMesh(fixture.lens, fixture.source.transform.position, direction * .3f);
        }
        static bool HitsMesh(Renderer renderer, Vector3 worldOrigin, Vector3 worldDelta)
        {
            var filter = renderer ? renderer.GetComponent<MeshFilter>() : null; if (!filter || !filter.sharedMesh) return false;
            var mesh = filter.sharedMesh;
            var lod = renderer.GetComponentInParent<CityObjectLod>();
            if (lod) foreach (var part in lod.parts) if (part.filter == filter) { mesh = part.full; break; }
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            Vector3 origin = filter.transform.InverseTransformPoint(worldOrigin);
            Vector3 delta = filter.transform.InverseTransformVector(worldDelta);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], e1 = vertices[triangles[i + 1]] - a, e2 = vertices[triangles[i + 2]] - a;
                Vector3 cross = Vector3.Cross(delta, e2); float determinant = Vector3.Dot(e1, cross);
                if (Mathf.Abs(determinant) < .0000001f) continue;
                Vector3 relative = origin - a; float u = Vector3.Dot(relative, cross) / determinant;
                // Centers can land on the diagonal shared by the panel's two triangles.
                // Keep float roundoff at that edge from rejecting both triangles.
                const float epsilon = .00001f;
                if (u < -epsilon || u > 1 + epsilon) continue;
                Vector3 q = Vector3.Cross(relative, e1); float v = Vector3.Dot(delta, q) / determinant;
                if (v < -epsilon || u + v > 1 + epsilon) continue;
                float t = Vector3.Dot(e2, q) / determinant;
                if (t > 0 && t <= 1) return true;
            }
            return false;
        }
        void Capture(string name)
        {
            var camera = ctx.Camera; camera.fieldOfView = 72;
            var target = new RenderTexture(1200, 800, 24); target.Create();
            var previous = RenderTexture.active; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(image);
        }
        void Finish()
        {
            File.WriteAllText(Path.Combine(directory, "lighting-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CITY_LIGHTING_QA " + (report.passed ? "PASS" : "FAIL") + " checks=" + report.checks.Count);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
