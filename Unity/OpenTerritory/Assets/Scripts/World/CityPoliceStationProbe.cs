using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityPoliceStationProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool passed = true; public string createdAtUtc, graphicsApi, assemblySha256, executable, resolution, quality;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityPoliceStation station; Vector2 move; string dir; bool interact, finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-police-station-qa") >= 0)
                new GameObject("Police station acceptance • opt-in").AddComponent<CityPoliceStationProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + trace); }
        void Update() { if (ctx && !finished) { ctx.Input.TouchMove = move; if (interact) { ctx.Input.TouchInteract = true; interact = false; } } }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("POLICE_STATION_QA " + name + " " + passed + " " + detail); }
        Vector3 P(float x, float y, float z) => station.transform.TransformPoint(new Vector3(x, y, z));
        IEnumerator Walk(Vector3 target)
        {
            float deadline = Time.realtimeSinceStartup + CityPhysics.Flat(target - ctx.Player.position).magnitude / 2.4f + 4;
            ctx.Camera.transform.rotation = Quaternion.identity;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .32f && Time.realtimeSinceStartup < deadline)
            {
                var direction = CityPhysics.Flat(target - ctx.Player.position).normalized; move = new Vector2(direction.x, direction.z); yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.15f);
        }
        bool Reached(Vector3 target, float horizontal = .85f, float vertical = .45f)
            => CityPhysics.Flat(target - ctx.Player.position).magnitude < horizontal && Mathf.Abs(target.y - ctx.Player.position.y) < vertical;
        void AimAtGate(int i)
        {
            var point = station.transform.TransformPoint(station.gateAnchors[i]);
            ctx.Camera.transform.position = point + station.transform.forward * 2.2f + Vector3.up * .3f;
            ctx.Camera.transform.LookAt(point); ctx.RefreshAction();
        }
        void Capture(string name, Vector3 eye, Vector3 target, float fov = 65)
        {
            var camera = ctx.Camera; camera.transform.position = P(eye.x, eye.y, eye.z); camera.transform.LookAt(P(target.x, target.y, target.z)); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>();
            if (manager) foreach (var lod in manager.objects) if (lod && lod.isActiveAndEnabled) lod.ApplyLevel(lod.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 2 }; rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = old; rt.Release(); Destroy(rt); Destroy(image);
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "police-station-qa"); Directory.CreateDirectory(dir);
            report.createdAtUtc = DateTime.UtcNow.ToString("o"); report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            report.resolution = Screen.width + "x" + Screen.height; report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            report.executable = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            var assembly = typeof(CityPoliceStation).Assembly.Location;
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(assembly)) report.assemblySha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; station = FindFirstObjectByType<CityPoliceStation>();
            Test("native-station-ready", ctx && ctx.Ready && station);
            if (!ctx || !ctx.Ready || !station) { Finish(); yield break; }
            yield return new WaitForSeconds(.6f);
            Test("isolated-qa-storage", CityCommerce.IsReviewRun);
            Test("same-site-and-original-entrance", ctx.Data.places[5].kind == "police" && ctx.Data.places[5].door.id == 1576 && ctx.Node(1576));
            Test("old-solid-upper-volume-retired", station.retiredSolidSlots.Length == 12 && station.retiredSolidSlots.All(i => !ctx.Data.solids[i].active) && station.retiredColliders.All(c => c && !c.enabled));
            Test("three-solid-cell-gates", station.gates.Length == 3 && station.gates.All(g => g && g.GetComponent<BoxCollider>() && g.GetComponent<BoxCollider>().enabled));
            Test("reception-officer-and-information-registered", ctx.Node(8109) && ctx.Actions.Any(a => a.id == "notice-service-13"));
            Test("station-sign-is-english", station.GetComponentsInChildren<Text>().Any(t => t.text == "POLICE STATION"));
            float ground = ctx.Ground(station.transform.position.x, station.transform.position.z);
            Test("ground-height-remains-on-lower-storey", Mathf.Abs(ground - (station.transform.position.y + .18f)) < .25f, "local height=" + (ground - station.transform.position.y).ToString("F2"));
            for (int i = 0; i < 3; i++)
            {
                var a = station.transform.TransformPoint(station.gateAnchors[i]) + station.transform.forward * 1.2f;
                bool blocked = Physics.Raycast(a, -station.transform.forward, out var hit, 2.4f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(station.gates[i]);
                Test("cell-" + (i + 1) + "-closed-gate-blocks-passage", blocked, blocked ? hit.collider.name : "no gate hit");
            }
            ctx.Combat.Holster(); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 0;
            var approach = station.streetApproach; approach.y = ctx.Ground(approach.x, approach.z) + .04f;
            ctx.Motor.Warp(approach); ctx.Motor.SetInputLock(true); yield return new WaitForSeconds(.4f);
            Capture("01-overview", new Vector3(26, 23, 33), new Vector3(0, 4, 0), 55);
            Capture("02-street", new Vector3(0, 4.3f, 21.5f), new Vector3(0, 3.4f, 0), 62);
            Capture("03-reception-and-cells", new Vector3(0, 2.4f, 8.5f), new Vector3(-3.8f, 1.9f, -5.4f), 76);
            Capture("04-upper-office", new Vector3(4.2f, 6.9f, 6.5f), new Vector3(-3.5f, 6.2f, -4.5f), 74);
            Capture("05-cell-interior", new Vector3(-5.85f, 2.3f, -6.1f), new Vector3(-5.85f, 1.4f, -9.5f), 68);
            ctx.Motor.SetInputLock(false); ctx.Camera.transform.rotation = Quaternion.identity;
            var inside = P(0, .22f, 8.6f); yield return Walk(inside);
            Test("walk-through-original-street-entrance", Reached(inside), "local=" + station.transform.InverseTransformPoint(ctx.Player.position));
            var aisle = P(0, .22f, -3.35f); yield return Walk(aisle);
            Test("walk-through-reception-to-cell-corridor", Reached(aisle), "local=" + station.transform.InverseTransformPoint(ctx.Player.position));
            yield return new WaitForSeconds(1.0f);
            Test("reception-officer-appears-near-player", ctx.Node(8109) && ctx.Node(8109).gameObject.activeInHierarchy,
                ctx.Node(8109) ? "local=" + station.transform.InverseTransformPoint(ctx.Node(8109).position) : "officer missing");
            Capture("07-reception-staffed", new Vector3(0, 2.4f, 8.5f), new Vector3(3, 1.8f, -5.4f), 72);
            var noticeApproach = P(-9.25f, .22f, -.6f); yield return Walk(noticeApproach);
            var notice = ctx.Actions.First(a => a.id == "notice-service-13");
            ctx.Camera.transform.position = P(-8.9f, 1.9f, -.6f); ctx.Camera.transform.LookAt(notice.Position); ctx.RefreshAction();
            Test("relocated-information-board-selectable", Reached(noticeApproach) && ctx.CurrentAction != null && ctx.CurrentAction.id == notice.id,
                "action=" + (ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id));
            interact = true; yield return new WaitForSeconds(.2f);
            Test("information-board-action-works", !string.IsNullOrEmpty(ctx.Speech) && !ctx.Paused, ctx.Speech);
            for (int i = 0; i < 3; i++)
            {
                float x = station.gateAnchors[i].x; var outside = P(x, .22f, -3.45f); yield return Walk(outside);
                bool reached = Reached(outside); AimAtGate(i);
                bool selected = ctx.CurrentAction != null && ctx.CurrentAction.id == "police-cell-" + i;
                Test("cell-" + (i + 1) + "-contextual-open-action", reached && selected, "action=" + (ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id));
                interact = true; yield return new WaitForSeconds(.25f);
                if (i == 0)
                {
                    float amount = station.gateAmounts[i], clock = ctx.Clock; ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.3f);
                    Test("Esc-freezes-opening-cell-gate", ctx.Paused && Mathf.Abs(station.gateAmounts[i] - amount) < .001f && Mathf.Abs(ctx.Clock - clock) < .001f);
                    ctx.UI.SetSettings(false);
                }
                yield return new WaitForSeconds(1.0f); Test("cell-" + (i + 1) + "-gate-opens", station.gateAmounts[i] > .98f, station.gateAmounts[i].ToString("F2"));
                var cell = P(x, .22f, -6.65f); yield return Walk(cell); bool entered = Reached(cell);
                yield return Walk(outside); bool exited = Reached(outside);
                Test("cell-" + (i + 1) + "-walk-in-and-out", entered && exited, "local=" + station.transform.InverseTransformPoint(ctx.Player.position));
                AimAtGate(i); interact = true; yield return new WaitForSeconds(1.2f);
                Test("cell-" + (i + 1) + "-gate-closes", station.gateAmounts[i] < .02f, station.gateAmounts[i].ToString("F2"));
            }
            yield return Walk(aisle); yield return Walk(P(0, .22f, 8.6f));
            bool climbed = true;
            foreach (var target in station.stairRoute)
            {
                yield return Walk(target);
                if (!Reached(target, .9f, .55f)) { climbed = false; Test("stair-route-blocker", false, "target=" + station.transform.InverseTransformPoint(target) + " actual=" + station.transform.InverseTransformPoint(ctx.Player.position)); break; }
            }
            Test("walk-up-both-flights-to-second-floor", climbed && station.transform.InverseTransformPoint(ctx.Player.position).y > 4.35f);
            if (climbed)
            {
                bool descended = true;
                foreach (var target in station.stairRoute.Reverse())
                {
                    yield return Walk(target);
                    if (!Reached(target, .9f, .55f)) { descended = false; Test("descent-route-blocker", false, "target=" + station.transform.InverseTransformPoint(target) + " actual=" + station.transform.InverseTransformPoint(ctx.Player.position)); break; }
                }
                Test("walk-down-stairs-and-return-to-entrance", descended && station.transform.InverseTransformPoint(ctx.Player.position).y < .65f);
            }
            var outsideAgain = P(0, .22f, 13); yield return Walk(outsideAgain);
            Test("walk-back-out-to-sidewalk", Reached(outsideAgain, 1.1f, .7f), "local=" + station.transform.InverseTransformPoint(ctx.Player.position));
            ctx.Clock = 168; ctx.Motor.SetInputLock(true); yield return new WaitForSeconds(.8f);
            var lighting = FindFirstObjectByType<CityLighting>(); var lamps = lighting.fixtures.Where(f => f.place == 5).ToArray();
            Test("both-storeys-lit-at-night", ctx.Night > .9f && lamps.Length == 8 && lamps.All(f => f.source && f.source.enabled));
            Capture("06-night-front", new Vector3(-9, 5, 24), new Vector3(0, 4, 0), 62);
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            ctx.Motor.SetInputLock(false); ctx.Follow.enabled = true; Finish();
        }
        void Finish()
        {
            finished = true; move = Vector2.zero; File.WriteAllText(Path.Combine(dir, "police-station-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
