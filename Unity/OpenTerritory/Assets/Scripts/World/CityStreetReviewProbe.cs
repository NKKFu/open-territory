using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit QA only. A normal game never adds this component or moves its camera.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityStreetReviewProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report { public bool passed = true; public string sourceHash, graphicsApi; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        CityContext ctx; CityMapProbe.Map map; string dir; Vector2 move; bool interact, finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-street-review-qa") >= 0)
                new GameObject("Street review QA opt-in").AddComponent<CityStreetReviewProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update()
        {
            if (!ctx || finished) return;
            ctx.Input.TouchMove = move;
            if (interact) { ctx.Input.TouchInteract = true; interact = false; }
        }
        void Test(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("STREET_REVIEW_QA " + name + " " + passed + " " + detail);
        }
        float Floor(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 1.8f, Vector3.down, 5, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore);
            return hits.Length > 0 ? hits.Max(h => h.point.y) : -999;
        }
        bool Clear(Vector3 p, Transform ignore = null)
        {
            return !Physics.OverlapCapsule(p + Vector3.up * .45f, p + Vector3.up * 1.8f, .37f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Any(c => !ignore || !c.transform.IsChildOf(ignore));
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/map-revision/editor")); Directory.CreateDirectory(dir);
            float until = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < until) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            report.sourceHash = ctx.Data.sourceHash; report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            map = JsonUtility.FromJson<CityMapProbe.Map>(Resources.Load<TextAsset>("street-network").text);
            yield return new WaitForSeconds(2);
            ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 62;

            foreach (var signal in ctx.Data.signals)
            {
                int index = Array.IndexOf(ctx.Data.signals, signal);
                Test("junction-has-real-approaches-" + index, signal.approaches.Length >= 3 && signal.lamps.Length == signal.approaches.Length * 3);
                foreach (var arm in signal.approaches)
                {
                    var pole = ctx.Node(arm.node);
                    bool oriented = pole && pole.gameObject.activeInHierarchy && Vector3.Dot(CityPhysics.Flat(pole.forward).normalized, arm.heading) > .99f;
                    float gap; SignalApproachData selected;
                    var right = Vector3.Cross(Vector3.up, arm.heading);
                    var incoming = arm.stop - arm.heading * 16 + right * arm.width * .25f;
                    bool incomingMatch = CitySignalRules.Approach(signal, incoming, arm.heading, 2, out selected, out gap) && selected == arm && gap > 12;
                    bool nearLineHold = CitySignalRules.Approach(signal, arm.stop - arm.heading * 2.1f + right * arm.width * .25f, arm.heading, 2, out selected, out gap) && selected == arm;
                    bool outgoingIgnored = !CitySignalRules.Approach(signal, arm.stop - arm.heading * 12, -arm.heading, 2, out selected, out gap);
                    bool committedIgnored = !CitySignalRules.Approach(signal, arm.stop + arm.heading * 8, arm.heading, 2, out selected, out gap);
                    Test("signal-facing-and-stop-line-" + arm.node, oriented && incomingMatch && nearLineHold && outgoingIgnored && committedIgnored,
                        "incoming=" + incomingMatch + " nearLine=" + nearLineHold + " outgoing=" + outgoingIgnored + " committed=" + committedIgnored);
                    Test("signal-on-supported-verge-" + arm.node, pole && Mathf.Abs(pole.position.y - Floor(pole.position)) < .14f);
                    Test("approach-has-green-amber-red-" + arm.node,
                        CitySignalRules.State(signal, arm.axis * 12 + 2 - signal.phase, arm.axis) == 0 &&
                        CitySignalRules.State(signal, arm.axis * 12 + 10 - signal.phase, arm.axis) == 1 &&
                        CitySignalRules.State(signal, arm.axis * 12 + 11.5f - signal.phase, arm.axis) == 2);
                }
                bool exclusive = true;
                for (float time = 0; time < signal.phaseCount * 12; time += .25f)
                    if (Enumerable.Range(0, signal.phaseCount).Count(axis => CitySignalRules.State(signal, time, axis) == 0) > 1) exclusive = false;
                Test("conflicting-streets-never-green-together-" + index, exclusive);
            }
            var devices = ctx.Data.devices.Where(d => d.type == "signal").ToArray();
            Test("no-orphan-signal-devices", devices.All(d => d.target >= 0 && d.target < ctx.Data.signals.Length && ctx.Node(d.node) && ctx.Node(d.node).gameObject.activeInHierarchy));
            foreach (var signal in ctx.Data.signals)
            {
                var d = devices.First(x => ctx.Data.signals[x.target] == signal);
                ctx.Systems.ExecuteHack(d, "signal-stop");
                Test("hack-stops-every-arm-" + d.target, signal.approaches.All(a => ctx.Systems.SignalState(signal, a.axis) == 2));
                ctx.Systems.ExecuteHack(d, "signal-restore");
            }

            var benches = ctx.Data.seats.Where(b => Mathf.Abs(b.seatTop - .82f) < .001f).ToArray();
            foreach (var bench in benches)
            {
                var root = ctx.Node(bench.id); var front = Quaternion.Euler(0, bench.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                var approach = root.position + front * (bench.depth * .5f + .95f); approach.y = Floor(approach) + .035f;
                Test("bench-visible-and-reachable-" + bench.id, root.gameObject.activeInHierarchy && root.GetComponentsInChildren<Renderer>().Length >= 10 && Vector3.Distance(root.position, bench.position) < .035f && Clear(approach, root));
                var feet = root.GetComponentsInChildren<MeshFilter>().Where(f => Mathf.Abs(Mathf.Abs(f.transform.localPosition.x) - 1.1f) < .01f && Mathf.Abs(f.transform.localPosition.z) < .01f).ToArray();
                bool supported = feet.Length == 2;
                foreach (var foot in feet)
                {
                    var bottom = foot.transform.TransformPoint(foot.sharedMesh.bounds.center - Vector3.up * foot.sharedMesh.bounds.extents.y);
                    supported &= bottom.y <= Floor(bottom) + .06f;
                }
                Test("bench-feet-reach-local-ground-" + bench.id, supported);
            }
            foreach (var crossing in map.crossings)
            {
                bool supported = true, clear = true;
                for (int i = 0; i <= 12; i++)
                {
                    var p = Vector3.Lerp(crossing.ends[0], crossing.ends[1], i / 12f); float y = Floor(p);
                    supported &= y > -900 && Mathf.Abs(y - p.y) < .25f;
                    clear &= Clear(new Vector3(p.x, y + .035f, p.z));
                }
                Test("crossing-has-two-clear-supported-sides-" + crossing.road + "-" + Array.IndexOf(map.crossings, crossing), supported && clear, "support=" + supported + " clear=" + clear);
            }
            yield return Traffic();
            var walks = map.crossings.Where(c => Clear(c.ends[0]) && Clear(c.ends[1])).ToArray();
            for (int i = 0; i < Mathf.Min(6, walks.Length); i++)
            {
                var crossing = walks[i * walks.Length / 6];
                yield return Walk("walk-across-zebra-" + i, crossing.ends[0], crossing.ends[1]);
            }
            var seating = benches.Where(b => !b.occupied && b.id < 4000).Take(5)
                .Concat(benches.Where(b => !b.occupied && new[] { 2300, 2315, 2330, 2345, 2409, 2442 }.Contains(b.id)))
                .Concat(benches.Where(b => !b.occupied && b.id > 4000).OrderByDescending(b => b.position.y - Floor(b.position)).Take(1)).Distinct();
            foreach (var bench in seating)
            {
                var front = Quaternion.Euler(0, bench.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                var p = bench.position + front * (bench.depth * .5f + .9f); p.y = Floor(p) + .05f;
                ctx.Motor.Warp(p);
                // Keep the test view inside the clear approach. A fixed camera
                // several metres behind it can end up beyond a garden wall.
                var eye = p + front * .35f + Vector3.up * 2.1f;
                var focus = bench.position + Vector3.up * (bench.seatTop + .15f);
                ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                yield return new WaitForSeconds(.3f); ctx.RefreshAction();
                bool selected = ctx.CurrentAction != null && ctx.CurrentAction.id == "seat-" + bench.id;
                interact = true; yield return new WaitForSeconds(.8f);
                bool sat = ctx.Motor.SeatHeight.HasValue;
                bool height = sat && Mathf.Abs(ctx.Player.position.y + ctx.Motor.SeatHeight.Value - bench.position.y - bench.seatTop) < .08f;
                interact = true; yield return new WaitForSeconds(.8f);
                Test("E-sits-at-visible-seat-and-stands-" + bench.id, selected && sat && height && !ctx.Motor.SeatHeight.HasValue && ctx.Motor.Grounded, "selected=" + selected + " sat=" + sat + " height=" + height);
            }
            var first = ctx.Data.signals[0]; var center = new Vector3(first.x, ReferenceMap.Height(first.x, first.z), first.z);
            Capture("01-cruzamento-revisado", center + new Vector3(26, 45, -40), center);
            var arm0 = first.approaches[0]; Capture("02-semaforo-e-faixa", arm0.stop - arm0.heading * 16 + Vector3.up * 5, arm0.stop + Vector3.up * 2);
            var seat = benches.First(b => b.id == 2162); Capture("03-banco-e-passagem", seat.position + new Vector3(6, 4, -6), seat.position + Vector3.up * .8f);
            ctx.UI.SetSettings(true); yield return null; float clock = ctx.Clock; yield return new WaitForSecondsRealtime(.2f);
            Test("Esc-still-pauses-the-city", ctx.Paused && ctx.Clock == clock); ctx.UI.SetSettings(false); yield return null;
            Test("no-runtime-errors", report.errors.Count == 0, string.Join("; ", report.errors)); Finish();
        }
        IEnumerator Walk(string name, Vector3 from, Vector3 to)
        {
            var direction = CityPhysics.Flat(to - from).normalized;
            ctx.Motor.Warp(from + Vector3.up * .05f); ctx.Motor.SetInputLock(false);
            ctx.Camera.transform.SetPositionAndRotation(from - direction * 7 + Vector3.up * 4, Quaternion.LookRotation(direction));
            yield return new WaitForSeconds(.3f);
            // Enter after moving traffic/people clear the band. Warping directly
            // in front of an already committed car does not test curb geometry.
            float wait = ctx.Clock + 25;
            var center = Vector3.Lerp(from, to, .5f) + Vector3.up * 1.1f;
            var half = new Vector3(.85f, 1.2f, CityPhysics.Flat(to - from).magnitude * .5f + .25f);
            int dynamicMask = (1 << CityContext.VehicleLayer) | (1 << CityContext.ActorLayer);
            while (ctx.Clock < wait && Physics.CheckBox(center, half, Quaternion.LookRotation(direction), dynamicMask, QueryTriggerInteraction.Ignore)) yield return null;
            float until = ctx.Clock + 7; move = Vector2.up;
            while (ctx.Clock < until && Vector3.Dot(CityPhysics.Flat(ctx.Player.position - from), direction) < CityPhysics.Flat(to - from).magnitude - .25f) yield return null;
            move = Vector2.zero; yield return new WaitForSeconds(.2f);
            Test(name, CityPhysics.Flat(ctx.Player.position - to).magnitude < 1.25f && ctx.Motor.Grounded && !ctx.Motor.Swimming,
                "position=" + ctx.Player.position + " from=" + from + " to=" + to + " grounded=" + ctx.Motor.Grounded + " mode=" + ctx.Motor.Mode);
        }
        IEnumerator Traffic()
        {
            const int signalIndex = 4; // The authored circuit follows town-spine here.
            var signal = ctx.Data.signals[signalIndex]; var focus = signal.approaches[0].stop;
            ctx.Motor.Warp(focus + Vector3.right * 18 + Vector3.up * .1f); yield return new WaitForSeconds(.8f);
            // Ambient cars choose local circuits on spawn. Use a fixed authored
            // circuit for this probe so population choices cannot skip the signal.
            var template = ctx.Data.vehicles.First(d => d.auto && d.kind == "car" && d.role != "police" && d.route.Length > 5);
            var fixture = ctx.Vehicles.Fleet.FirstOrDefault(c => c.Data.id == template.id) ?? ctx.Vehicles.Spawn(template, template.route[0], Quaternion.Euler(0, template.yaw * Mathf.Rad2Deg, 0));
            fixture.Route = template.route;
            typeof(CityVehicles).GetMethod("PrepareRoute", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(ctx.Vehicles, new object[] { fixture });
            fixture.Traffic = new CityTrafficPolicy.State();
            ((CityTrafficPolicy)typeof(CityVehicles).GetField("traffic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ctx.Vehicles)).Prepare(fixture);
            CityVehicles.Vehicle selected = null; SignalApproachData arm = null; float progress = 0;
            foreach (var car in new[] { fixture })
            {
                foreach (var approach in signal.approaches)
                {
                    float at = ctx.Vehicles.RouteDistance(car, approach.stop);
                    var p = ctx.Vehicles.RoutePoint(car, at);
                    var direction = CityPhysics.Flat(ctx.Vehicles.RoutePoint(car, at + 1) - ctx.Vehicles.RoutePoint(car, at - 1)).normalized;
                    if (CityPhysics.Flat(p - approach.stop).magnitude > 4 || Vector3.Dot(direction, approach.heading) < .9f) continue;
                    selected = car; arm = approach; progress = at; break;
                }
                if (selected != null) break;
            }
            Test("traffic-route-reaches-a-reviewed-stop-line", selected != null);
            if (selected == null) yield break;
            var start = ctx.Vehicles.RoutePoint(selected, progress - 17);
            var heading = CityPhysics.Flat(ctx.Vehicles.RoutePoint(selected, progress - 15) - start).normalized;
            selected.Root.SetPositionAndRotation(start, Quaternion.LookRotation(heading)); selected.Body.position = start; selected.Body.rotation = selected.Root.rotation;
            selected.Body.linearVelocity = selected.Body.angularVelocity = Vector3.zero; selected.Progress = progress - 17;
            selected.Traffic = new CityTrafficPolicy.State(); selected.TrafficHold = false; selected.Commanded = false;
            Physics.SyncTransforms();
            var device = ctx.Data.devices.First(d => d.type == "signal" && d.target == signalIndex);
            ctx.Systems.ExecuteHack(device, "signal-stop");
            yield return new WaitForSeconds(5.8f);
            float nose = selected.Data.length * .5f + selected.Data.centerOffset;
            float gap = Vector3.Dot(CityPhysics.Flat(arm.stop - selected.Position), arm.heading) - nose;
            Test("native-car-stops-before-the-zebra", selected.StoppedReason == "semaforo" && Mathf.Abs(selected.Speed) < .65f && gap > -.2f && gap < 4,
                "speed=" + selected.Speed + " gap=" + gap + " reason=" + selected.StoppedReason);
            Capture("04-carro-na-linha-de-parada", selected.Position - selected.Root.forward * 9 + Vector3.up * 5, arm.stop);
            ctx.Systems.ExecuteHack(device, "signal-restore");
            float cycle = Mathf.Max(2, signal.phaseCount) * 12;
            ctx.Clock += Mathf.Repeat(arm.axis * 12 + 2 - Mathf.Repeat(ctx.Clock + signal.phase, cycle), cycle);
            var stopped = selected.Position; yield return new WaitForSeconds(4);
            Test("native-car-resumes-on-its-own-green", Vector3.Dot(CityPhysics.Flat(selected.Position - stopped), arm.heading) > 4,
                "distance=" + Vector3.Distance(stopped, selected.Position) + " reason=" + selected.StoppedReason);
        }
        void Capture(string name, Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var lods = FindFirstObjectByType<CityLodManager>();
            if (lods) foreach (var item in lods.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            var previous = RenderTexture.active; var rt = new RenderTexture(1440, 900, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; RenderSettings.fog = fog; rt.Release(); Destroy(rt); Destroy(texture);
        }
        void Finish()
        {
            finished = true; File.WriteAllText(Path.Combine(dir, "street-review-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
