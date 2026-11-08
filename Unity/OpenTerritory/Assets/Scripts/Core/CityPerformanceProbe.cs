using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace OpenTerritory
{
    // Explicit standalone QA only. No recorders, fixtures or benchmark work in a normal game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityPerformanceProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Stats
        {
            public bool available; public int samples; public double mean, median, p95, p99, maximum, total;
        }
        [Serializable] public sealed class Phase
        {
            public string name; public int frames, residents, vehicles;
            public Stats frameMs, mainThreadMs, renderThreadMs, gpuMs, gcBytes, usedMemoryMiB, drawCalls;
        }
        [Serializable] public sealed class Operations
        {
            public string name; public int count, succeeded; public Stats milliseconds, allocatedBytes;
        }
        [Serializable] public sealed class RouteResult
        {
            public int start, goal, count; public bool found, valid; public string signature;
        }
        [Serializable] public sealed class Report
        {
            public bool passed = true, completed, development, optimizedExpected, allocationCounterAvailable, referenceImplementation;
            public string label, unity, layout, cpu, gpu, graphicsApi, operatingSystem, startedUtc;
            public int width, height, quality, msaa, vSync, targetFrameRate, navigationNodes, poolBefore, poolAfter;
            public List<Phase> phases = new List<Phase>();
            public List<Operations> operations = new List<Operations>();
            public List<RouteResult> routes = new List<RouteResult>();
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        struct Frame
        {
            public double wall, main, render, gpu, gc, memory, draws;
        }
        readonly Report report = new Report();
        readonly List<Frame> frames = new List<Frame>(8192);
        readonly List<double> operationMs = new List<double>(256), operationBytes = new List<double>(256);
        readonly List<GameObject> fixtures = new List<GameObject>();
        ProfilerRecorder main, render, gpu, gc, memory, draws;
        CityContext ctx; string directory; bool collecting, finished;
        int previousVSync, previousLimit; long lastFrame;
        Mesh fixtureMesh; Material fixtureMaterial;
        const int RouteCases = 48, BreakCases = 64;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-performance-qa") < 0) return;
            UnityEngine.Random.InitState(71939);
            new GameObject("QA de desempenho • opt-in").AddComponent<CityPerformanceProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }
        static ProfilerRecorder Counter(ProfilerCategory category, string name)
        { return ProfilerRecorder.StartNew(category, name, 1); }
        static double Read(ProfilerRecorder recorder, double factor = 1)
        { return recorder.Valid && recorder.Count > 0 ? recorder.LastValue * factor : -1; }
        void Update()
        {
            if (!collecting) return;
            long now = Stopwatch.GetTimestamp();
            frames.Add(new Frame { wall = (now - lastFrame) * 1000d / Stopwatch.Frequency,
                main = Read(main, .000001), render = Read(render, .000001), gpu = Read(gpu, .000001),
                gc = Read(gc), memory = Read(memory, 1d / (1024 * 1024)), draws = Read(draws) });
            lastFrame = now;
        }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("PERFORMANCE_QA " + name + " " + passed + " " + detail);
        }
        static Stats Summarize(List<double> values, bool allowZero = true)
        {
            var sorted = new List<double>(values.Count);
            foreach (double v in values) if (v >= 0 && (allowZero || v > 0)) sorted.Add(v);
            var result = new Stats { available = sorted.Count > 0, samples = sorted.Count };
            if (sorted.Count == 0) return result;
            sorted.Sort(); foreach (double v in sorted) result.total += v;
            result.mean = result.total / sorted.Count; result.median = sorted[(sorted.Count - 1) / 2];
            result.p95 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * .95) - 1)];
            result.p99 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * .99) - 1)];
            result.maximum = sorted[sorted.Count - 1]; return result;
        }
        Stats FrameStats(Func<Frame, double> selector, bool allowZero = true)
        {
            var values = new List<double>(frames.Count); foreach (var f in frames) values.Add(selector(f));
            return Summarize(values, allowZero);
        }
        void BeginFrames(string name)
        {
            Debug.Log("PERFORMANCE_PHASE_BEGIN " + name);
            frames.Clear(); lastFrame = Stopwatch.GetTimestamp(); collecting = true;
        }
        void EndFrames(string name)
        {
            collecting = false;
            report.phases.Add(new Phase { name = name, frames = frames.Count, residents = ctx.Residents.People.Count,
                vehicles = ctx.Vehicles.Fleet.Count, frameMs = FrameStats(f => f.wall),
                mainThreadMs = FrameStats(f => f.main, false), renderThreadMs = FrameStats(f => f.render, false),
                gpuMs = FrameStats(f => f.gpu, false), gcBytes = FrameStats(f => f.gc),
                usedMemoryMiB = FrameStats(f => f.memory, false), drawCalls = FrameStats(f => f.draws) });
            Debug.Log("PERFORMANCE_PHASE_END " + name + " frames=" + frames.Count);
            Save();
        }
        void Save() { File.WriteAllText(Path.Combine(directory, "performance-qa.json"), JsonUtility.ToJson(report, true)); }

        IEnumerator Start()
        {
            directory = Argument("--city-report-dir", Application.temporaryCachePath);
            Directory.CreateDirectory(directory);
            report.label = Argument("--city-performance-label", "native");
#if CITY_PERFORMANCE_REFERENCE
            report.referenceImplementation = true;
#endif
            report.optimizedExpected = Array.IndexOf(Environment.GetCommandLineArgs(), "--city-expect-optimized") >= 0;
            report.startedUtc = DateTime.UtcNow.ToString("o");
            report.unity = Application.unityVersion; report.development = Debug.isDebugBuild;
            report.cpu = SystemInfo.processorType; report.gpu = SystemInfo.graphicsDeviceName;
            report.graphicsApi = SystemInfo.graphicsDeviceType.ToString(); report.operatingSystem = SystemInfo.operatingSystem;
            float timeout = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { CheckResult("city-ready", false); Finish(); yield break; }
            CheckResult("isolated-QA-storage", CityCommerce.IsReviewRun);
            report.layout = ctx.Data.layoutId; report.navigationNodes = ctx.Data.nav.Length;
            previousVSync = QualitySettings.vSyncCount; previousLimit = Application.targetFrameRate;
            // Only this opted-in process is uncapped. Ordinary gameplay keeps its settings.
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
            report.width = Screen.width; report.height = Screen.height; report.quality = ctx.UI.Quality;
            report.msaa = QualitySettings.antiAliasing; report.vSync = QualitySettings.vSyncCount;
            report.targetFrameRate = Application.targetFrameRate;
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false;
            ctx.Population.enabled = ctx.Residents.enabled = ctx.Vehicles.enabled = false;
            long calibrationBefore = GC.GetAllocatedBytesForCurrentThread();
            var calibration = new byte[4096];
            report.allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() - calibrationBefore >= calibration.Length;
            GC.KeepAlive(calibration);
            MeasureRoutes();
            // The saved scene may contain an author's preview pose. Benchmark the
            // manifest's city spawn explicitly, without changing that saved pose.
            ctx.Motor.Warp(ctx.Data.spawn);
            ctx.Population.enabled = ctx.Residents.enabled = ctx.Vehicles.enabled = true;
            ctx.Systems.Weather.Request("clear", true);
            main = Counter(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            render = Counter(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
            gpu = Counter(ProfilerCategory.Internal, "GPU Frame Time");
            gc = Counter(ProfilerCategory.Memory, "GC Allocated In Frame");
            memory = Counter(ProfilerCategory.Memory, "Total Used Memory");
            draws = Counter(ProfilerCategory.Render, "Draw Calls Count");
            Vector3 focus = ctx.Player.position + Vector3.up * 1.3f;
            ctx.Camera.transform.position = focus + new Vector3(7, 5, -9); ctx.Camera.transform.LookAt(focus);
            yield return Wait(6);
            BeginFrames("city-street"); yield return Wait(10); EndFrames("city-street");
            CheckResult("city-population-is-running", ctx.Residents.People.Count > 0 && ctx.Vehicles.Fleet.Count > 0);
            Capture("01-city-street");
            yield return MeasureDestruction();
            CheckResult("only-explicit-performance-probe", OnlyThisProbe());
            CheckResult("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
        void MeasureRoutes()
        {
            var method = typeof(CityResidents).GetMethod("Route", BindingFlags.Instance | BindingFlags.NonPublic);
            var route = (Func<CityResidents.Resident, Vector3, bool>)Delegate.CreateDelegate(
                typeof(Func<CityResidents.Resident, Vector3, bool>), ctx.Residents, method);
            var root = new GameObject("QA • origem de rotas"); fixtures.Add(root);
            var person = new CityResidents.Resident { Root = root.transform, RouteSalt = 487.25f };
            person.Path.Capacity = ctx.Data.nav.Length;
            int n = ctx.Data.nav.Length; var starts = new int[RouteCases]; var goals = new int[RouteCases];
            for (int i = 0; i < RouteCases; i++)
            {
                starts[i] = (i * 997 + 17) % n; goals[i] = (i * 2719 + 503) % n;
                while (ctx.Data.nav[starts[i]].edges.Length == 0) starts[i] = (starts[i] + 1) % n;
                while (ctx.Data.nav[goals[i]].edges.Length == 0) goals[i] = (goals[i] + 1) % n;
            }
            for (int i = 0; i < RouteCases; i++) { root.transform.position = NodePoint(starts[i]); route(person, NodePoint(goals[i])); }
            operationMs.Clear(); operationBytes.Clear(); int succeeded = 0; bool valid = true;
            for (int round = 0; round < 3; round++) for (int i = 0; i < RouteCases; i++)
            {
                root.transform.position = NodePoint(starts[i]); Vector3 goal = NodePoint(goals[i]);
                long bytes = GC.GetAllocatedBytesForCurrentThread(), ticks = Stopwatch.GetTimestamp();
                bool found = route(person, goal);
                long elapsed = Stopwatch.GetTimestamp() - ticks, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                operationMs.Add(elapsed * 1000d / Stopwatch.Frequency); operationBytes.Add(report.allocationCounterAvailable ? allocated : -1);
                if (found) succeeded++;
                bool pathValid = !found ? person.Path.Count == 0 : ValidPath(person.Path);
                valid &= pathValid;
                if (round == 0)
                {
                    uint hash = 2166136261;
                    unchecked { foreach (int index in person.Path) hash = (hash ^ (uint)ctx.Data.nav[index].id) * 16777619; }
                    report.routes.Add(new RouteResult { start = starts[i], goal = goals[i], found = found,
                        count = person.Path.Count, valid = pathValid, signature = hash.ToString("X8") });
                }
            }
            report.operations.Add(new Operations { name = "resident-routes", count = operationMs.Count, succeeded = succeeded,
                milliseconds = Summarize(operationMs), allocatedBytes = Summarize(operationBytes) });
            CheckResult("routes-follow-unblocked-real-edges", valid && succeeded > 0, "successes=" + succeeded + "/" + operationMs.Count);
            route(person, new Vector3(50000, 0, 50000));
            CheckResult("unreachable-goal-clears-old-path", person.Path.Count == 0);
            root.transform.position = NodePoint(starts[0]);
            CheckResult("valid-search-recovers-after-failure", route(person, NodePoint(goals[0])) == report.routes[0].found);
            Save();
        }
        Vector3 NodePoint(int index)
        {
            var node = ctx.Data.nav[index]; return new Vector3(node.x, ctx.Ground(node.x, node.z) + .04f, node.z);
        }
        bool ValidPath(List<int> path)
        {
            if (path.Count == 0) return false;
            var visited = new HashSet<int>();
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] < 0 || path[i] >= ctx.Data.nav.Length || !visited.Add(path[i])) return false;
                if (i == 0) continue;
                var before = ctx.Data.nav[path[i - 1]]; int target = ctx.Data.nav[path[i]].id; bool edge = false;
                foreach (var e in before.edges) if (e.to == target && !ctx.LifeNavigation.Blocked(before.id, target)) { edge = true; break; }
                if (!edge) return false;
            }
            return true;
        }
        Rigidbody[] Debris()
        {
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var result = new List<Rigidbody>(); foreach (var b in all) if (b.name.StartsWith("Estilhaço •", StringComparison.Ordinal)) result.Add(b);
            return result.ToArray();
        }
        IEnumerator MeasureDestruction()
        {
            ctx.Population.enabled = false;
            Vector3 origin = new Vector3(5000, 300, 5000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(floor); floor.name = "QA • piso de destruição";
            floor.layer = CityContext.GroundLayer; floor.transform.position = origin - Vector3.up * .55f; floor.transform.localScale = new Vector3(50, 1, 50);
            fixtureMesh = Instantiate(floor.GetComponent<MeshFilter>().sharedMesh);
            var colors = new Color32[fixtureMesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(160, 190, 215, 255);
            fixtureMesh.colors32 = colors;
            fixtureMaterial = new Material(Shader.Find("OpenTerritory/CitySurface")); fixtureMaterial.SetColor("_Color", new Color(.8f, .6f, .4f));
            var props = new CityDestructible[BreakCases]; var shapes = new Collider[BreakCases];
            for (int i = 0; i < props.Length; i++)
            {
                var go = new GameObject("QA • objeto destrutível " + i); fixtures.Add(go); go.layer = CityContext.WorldLayer;
                go.transform.position = origin + new Vector3(i % 8 * 2.2f - 8, .65f, i / 8 * 2.2f - 8);
                go.transform.localScale = new Vector3(1.2f, 1.3f, 1.2f);
                go.AddComponent<MeshFilter>().sharedMesh = fixtureMesh; go.AddComponent<MeshRenderer>().sharedMaterial = fixtureMaterial;
                shapes[i] = go.AddComponent<BoxCollider>(); props[i] = go.AddComponent<CityDestructible>(); props[i].category = "QA";
                ctx.Systems.Destruction.Register(props[i]);
            }
            ctx.Motor.Warp(origin + new Vector3(0, .05f, -15));
            ctx.Camera.transform.position = origin + new Vector3(15, 12, -21); ctx.Camera.transform.LookAt(origin + new Vector3(0, 1, 1));
            Physics.SyncTransforms(); yield return Wait(2);
            report.poolBefore = Debris().Length;
            operationMs.Clear(); operationBytes.Clear(); int broken = 0; bool bounded = true;
            BeginFrames("destruction-stress");
            for (int i = 0; i < props.Length; i++)
            {
                Vector3 at = props[i].transform.position;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), ticks = Stopwatch.GetTimestamp();
                bool hit = ctx.Systems.Destruction.Hit(shapes[i], 1000, at, new Vector3(2, 2, 1), (CityPropDamageKind)(i % 3), false);
                long elapsed = Stopwatch.GetTimestamp() - ticks, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                operationMs.Add(elapsed * 1000d / Stopwatch.Frequency); operationBytes.Add(report.allocationCounterAvailable ? allocated : -1);
                if (hit && props[i].Broken && !shapes[i].enabled && !props[i].gameObject.activeSelf) broken++;
                bounded &= ctx.Systems.Destruction.FragmentCount <= CityDestruction.DebrisLimit;
                yield return null;
                if (i == 15)
                {
                    EndFrames("destruction-stress"); Capture("02-debris");
                    // FrameTiming counters lag; keep screenshot encoding out of the next sample window.
                    yield return Wait(.25f); BeginFrames("destruction-stress-tail");
                }
            }
            EndFrames("destruction-stress-tail");
            report.operations.Add(new Operations { name = "prop-destruction", count = operationMs.Count, succeeded = broken,
                milliseconds = Summarize(operationMs), allocatedBytes = Summarize(operationBytes) });
            CheckResult("bullet-melee-vehicle-damage-still-break-props", broken == props.Length, "broken=" + broken);
            CheckResult("debris-budget-is-bounded", bounded && ctx.Systems.Destruction.FragmentCount == CityDestruction.DebrisLimit);
            var live = Debris(); report.poolAfter = live.Length;
            bool masks = true, pigment = true;
            int expectedMask = (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer);
            foreach (var body in live) if (body.gameObject.activeInHierarchy)
            {
                masks &= body.GetComponent<Collider>().excludeLayers.value == expectedMask;
                var block = new MaterialPropertyBlock(); body.GetComponent<Renderer>().GetPropertyBlock(block);
                Color expected = fixtureMaterial.color * (Color)colors[0], actual = block.GetColor("_Color");
                pigment &= Mathf.Abs(expected.r - actual.r) < .001f && Mathf.Abs(expected.g - actual.g) < .001f && Mathf.Abs(expected.b - actual.b) < .001f;
            }
            CheckResult("debris-keeps-collision-filter-and-source-color", masks && pigment);
            if (report.optimizedExpected) CheckResult("fixed-pool-reused-under-saturation", report.poolBefore == CityDestruction.DebrisLimit && report.poolAfter == report.poolBefore,
                "before=" + report.poolBefore + " after=" + report.poolAfter);
            ctx.UI.SetSettings(true); float clock = ctx.Clock; int active = ctx.Systems.Destruction.FragmentCount;
            var positions = new Vector3[live.Length]; for (int i = 0; i < live.Length; i++) if (live[i]) positions[i] = live[i].position;
            yield return Wait(.6f); bool frozen = ctx.Clock == clock && ctx.Systems.Destruction.FragmentCount == active;
            for (int i = 0; i < live.Length; i++) if (live[i]) frozen &= Vector3.Distance(live[i].position, positions[i]) < .001f;
            CheckResult("Esc-freezes-debris-and-expiry", frozen); Capture("03-paused-debris"); ctx.UI.SetSettings(false);
            yield return Wait(8.5f);
            CheckResult("debris-expires-after-resume", ctx.Systems.Destruction.FragmentCount == 0);
            if (report.optimizedExpected)
            {
                // An inactive Rigidbody has no live physics actor; IsSleeping() is
                // not a reliable query for that state. Check actual participation.
                bool inactive = true; foreach (var body in Debris()) inactive &= !body.gameObject.activeSelf && !body.detectCollisions &&
                    !body.GetComponent<Collider>().enabled && body.linearVelocity.sqrMagnitude < .0001f && body.angularVelocity.sqrMagnitude < .0001f;
                CheckResult("returned-pool-has-no-live-physics", inactive && Debris().Length == CityDestruction.DebrisLimit);
            }
        }
        bool OnlyThisProbe()
        {
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (component != this && component.GetType().Name.EndsWith("Probe", StringComparison.Ordinal)) return false;
            return true;
        }
        void Capture(string name)
        {
            var rt = new RenderTexture(1280, 720, 24); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var cameraTarget = ctx.Camera.targetTexture; var previous = RenderTexture.active;
            try
            {
                rt.Create(); ctx.Camera.targetTexture = rt; ctx.Camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { ctx.Camera.targetTexture = cameraTarget; RenderTexture.active = previous; rt.Release(); Destroy(rt); Destroy(image); }
        }
        void Finish()
        {
            collecting = false; finished = true; report.completed = true; report.passed &= report.errors.Count == 0;
            Save(); Debug.Log("PERFORMANCE_QA_FINISHED " + report.passed); Application.Quit(report.passed ? 0 : 2);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            main.Dispose(); render.Dispose(); gpu.Dispose(); gc.Dispose(); memory.Dispose(); draws.Dispose();
            if (ctx) { ctx.Pause(false); QualitySettings.vSyncCount = previousVSync; Application.targetFrameRate = previousLimit; }
            foreach (var go in fixtures) if (go) Destroy(go);
            if (fixtureMesh) Destroy(fixtureMesh); if (fixtureMaterial) Destroy(fixtureMaterial);
            if (!finished && directory != null) { report.passed = false; Save(); }
        }
    }
}
