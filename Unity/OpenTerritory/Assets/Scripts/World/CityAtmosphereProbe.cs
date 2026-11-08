using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // No automatic work in normal games. Explicitly attach in Play Mode or
    // launch the native player with --city-atmosphere-qa.
    public sealed class CityAtmosphereProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report
        {
            public bool passed = true;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        public string ReportDirectory;
        public bool QuitWhenFinished = true;
        CityContext ctx; string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-atmosphere-qa") >= 0)
                new GameObject("QA de vento e nuvens • opt-in").AddComponent<CityAtmosphereProbe>();
        }
        void Awake() { Application.logMessageReceived += OnLog; }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("ATMOSPHERE_QA " + name + " " + passed + " " + detail);
        }
        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
        Texture2D Capture(string name)
        {
            var camera = ctx.Camera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG()); return texture;
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); }
        }
        static int Changed(Texture2D a, Texture2D b)
        {
            var first = a.GetPixels32(); var second = b.GetPixels32(); int changed = 0;
            for (int i = 0; i < first.Length; i++)
                if (Mathf.Abs(first[i].r - second[i].r) + Mathf.Abs(first[i].g - second[i].g) + Mathf.Abs(first[i].b - second[i].b) > 24) changed++;
            return changed;
        }
        void CheckGroundCover(CityAtmosphere atmosphere, Vector3 player)
        {
            var cover = FindFirstObjectByType<CityGroundCoverInfo>();
            CheckResult("wind-covers-grass-and-shrub-lods", cover && atmosphere.GroundCoverRendererCount == cover.grassChunks * 2 + cover.shrubChunks * 3,
                "renderers=" + atmosphere.GroundCoverRendererCount);
            if (!cover) return;
            var groups = cover.GetComponentsInChildren<LODGroup>();
            var grass = groups.Where(g => g.name.StartsWith("Grama ", StringComparison.Ordinal)).OrderBy(g => (g.transform.position - player).sqrMagnitude).First();
            var meshes = grass.GetComponentsInChildren<MeshFilter>();
            var vertices = meshes[1].sharedMesh.vertices;
            Vector3 root = Vector3.zero; float nearest = float.MaxValue;
            for (int i = 0; i < vertices.Length; i += 3)
            {
                Vector3 p = meshes[1].transform.TransformPoint((vertices[i] + vertices[i + 1]) * .5f);
                float distance = (p - player).sqrMagnitude;
                if (distance < nearest) { root = p; nearest = distance; }
            }
            var data = new List<Vector2>(); bool anchored = true;
            foreach (var filter in meshes)
            {
                filter.sharedMesh.GetUVs(2, data); anchored &= data.Count == filter.sharedMesh.vertexCount;
                for (int i = 0; i + 2 < data.Count; i += 3) anchored &= data[i] == Vector2.zero && data[i + 1] == Vector2.zero && data[i + 2].x > .1f && data[i + 2].y == 1;
            }
            CheckResult("grass-roots-pinned-in-both-lods", anchored);
            CheckResult("ground-cover-keeps-no-colliders", cover.GetComponentsInChildren<Collider>().Length == 0);
            atmosphere.Wind.gameObject.SetActive(false);
            ctx.Camera.transform.position = root + new Vector3(1.3f, 1.1f, -1.8f); ctx.Camera.transform.LookAt(root + Vector3.up * .28f);
            Vector3 wind = ctx.Systems.Weather.Wind; float time = ctx.Clock;
            for (int level = 0; level < 2; level++)
            {
                grass.ForceLOD(level); Shader.SetGlobalVector("_CityWind", Vector3.zero); Shader.SetGlobalFloat("_CityTime", time);
                var calm = Capture("10-grass-LOD" + level + "-calm");
                Shader.SetGlobalVector("_CityWind", wind); var breeze = Capture("11-grass-LOD" + level + "-breeze");
                int pixels = Changed(calm, breeze);
                CheckResult("grass-LOD" + level + "-moves-in-clear-weather", pixels > 100, "changed pixels=" + pixels);
                Shader.SetGlobalFloat("_CityTime", time + 2.2f); var next = Capture("12-grass-LOD" + level + "-next-gust");
                int moving = Changed(breeze, next);
                CheckResult("grass-LOD" + level + "-gusts-move-over-time", moving > 50, "changed pixels=" + moving);
                Destroy(calm); Destroy(breeze); Destroy(next);
            }
            grass.ForceLOD(-1);
            var shrub = groups.Where(g => g.name.StartsWith("Arbustos ", StringComparison.Ordinal)).OrderBy(g => (g.transform.position - player).sqrMagnitude).First();
            var shrubFilter = shrub.GetComponentsInChildren<MeshFilter>()[0]; var shrubVertices = shrubFilter.sharedMesh.vertices;
            shrubFilter.sharedMesh.GetUVs(2, data); int tip = data.FindIndex(v => v.y > .99f);
            Vector3 shrubTop = shrubFilter.transform.TransformPoint(shrubVertices[Mathf.Max(0, tip)]);
            ctx.Camera.transform.position = shrubTop + new Vector3(2, 1.4f, -3);
            ctx.Camera.transform.LookAt(shrubTop - Vector3.up * .2f); shrub.ForceLOD(0);
            Shader.SetGlobalVector("_CityWind", Vector3.zero); var still = Capture("13-shrubs-calm");
            Shader.SetGlobalVector("_CityWind", wind); var bending = Capture("14-shrubs-breeze");
            int shrubPixels = Changed(still, bending);
            CheckResult("shrubs-follow-the-same-breeze", shrubPixels > 80, "changed pixels=" + shrubPixels);
            Destroy(still); Destroy(bending); shrub.ForceLOD(-1);
            atmosphere.Wind.gameObject.SetActive(true); Shader.SetGlobalFloat("_CityTime", time); Shader.SetGlobalVector("_CityWind", wind);
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "atmosphere-qa");
            Directory.CreateDirectory(dir);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance;
            var atmosphere = FindFirstObjectByType<CityAtmosphere>();
            var player = ctx.Player.position; var rotation = ctx.Player.rotation;
            float clock = ctx.Clock; string weather = ctx.Systems.Weather.Id; bool follow = ctx.Follow.enabled;
            CheckResult("cloud-field-has-no-gameplay-colliders", atmosphere && atmosphere.CloudCount == 64 && atmosphere.GetComponentsInChildren<Collider>().Length == 0);
            CheckResult("wind-covers-all-three-tree-lods", atmosphere.WindRendererCount > 0 && atmosphere.WindRendererCount % 3 == 0, "renderers=" + atmosphere.WindRendererCount);
            CheckResult("cloud-shader-included-in-native-player", Resources.Load<Shader>("Shaders/CityCloud") && Resources.Load<Shader>("Shaders/CityCloud").isSupported);

            var streetWind = atmosphere.Wind;
            ctx.Clock = 25; ctx.Systems.Weather.Request("clear", true);
            yield return Wait(2.5f);
            CheckResult("street-wind-active-in-clear-weather", streetWind.ActiveLeaves > 0 && streetWind.ActiveGusts > 0 && streetWind.ActiveLeaves <= 32 && streetWind.ActiveGusts <= 10,
                "leaves=" + streetWind.ActiveLeaves + " gusts=" + streetWind.ActiveGusts);
            ctx.Pause(true); var withWind = Capture("08-player-view-wind");
            streetWind.gameObject.SetActive(false); var withoutWind = Capture("09-player-view-without-wind"); streetWind.gameObject.SetActive(true);
            int streetPixels = Changed(withWind, withoutWind);
            CheckResult("wind-visible-from-normal-player-camera", streetPixels > 120, "changed pixels=" + streetPixels);
            Destroy(withWind); Destroy(withoutWind);
            ctx.Pause(false);
            var roof = new GameObject("QA roof • wind shelter"); roof.layer = CityContext.WorldLayer;
            roof.transform.position = ctx.Player.position + Vector3.up * 5; roof.AddComponent<BoxCollider>().size = new Vector3(50,.2f,50);
            Physics.SyncTransforms(); yield return Wait(.25f);
            int shelteredEmissions = streetWind.EmittedLeaves + streetWind.EmittedGusts;
            yield return Wait(.7f);
            CheckResult("roof-blocks-new-wind-emission", streetWind.EmittedLeaves + streetWind.EmittedGusts == shelteredEmissions);
            Destroy(roof); yield return Wait(.2f);

            ctx.Systems.Weather.Request("clear", true); ctx.Follow.enabled = false;
            ctx.Camera.transform.position = player + Vector3.up * 12;
            ctx.Camera.transform.rotation = Quaternion.Euler(-9, 35, 0);
            yield return Wait(.3f); ctx.Pause(true);
            var clear = Capture("01-clear-clouds");
            atmosphere.gameObject.SetActive(false); var empty = Capture("02-without-clouds"); atmosphere.gameObject.SetActive(true);
            int cloudPixels = Changed(clear, empty);
            CheckResult("clouds-actually-render-in-sky", cloudPixels > 1500, "changed pixels=" + cloudPixels);
            Destroy(clear); Destroy(empty);
            float clearLight = Shader.GetGlobalColor("_CityCloudLit").grayscale;

            ctx.Pause(false); ctx.Systems.Weather.Request("squall", true); yield return Wait(.3f);
            Vector3 drift = atmosphere.Drift, wind = ctx.Systems.Weather.Wind;
            yield return Wait(.65f);
            var travel = atmosphere.Drift - drift;
            CheckResult("clouds-drift-with-weather-wind", travel.magnitude > 2 && Vector3.Dot(travel.normalized, wind.normalized) > .99f, "travel=" + travel);
            CheckResult("squall-darkens-clouds-and-strengthens-wind", Shader.GetGlobalColor("_CityCloudLit").grayscale < clearLight * .8f && Shader.GetGlobalVector("_CityWind").magnitude > 6);
            ctx.Pause(true); var storm = Capture("03-squall-clouds"); Destroy(storm);

            ctx.Pause(false); ctx.UI.SetSettings(true);
            yield return null; // Sample the first completed paused frame, after LateUpdate.
            drift = atmosphere.Drift; float pausedClock = ctx.Clock, shaderClock = Shader.GetGlobalFloat("_CityTime");
            var cloud = atmosphere.transform.Find("Nuvem • 0"); Vector3 cloudPosition = cloud.position;
            var windTransforms = streetWind.GetComponentsInChildren<Transform>();
            var windPositions = windTransforms.Select(t => t.position).ToArray();
            int pausedEmissions = streetWind.EmittedLeaves + streetWind.EmittedGusts;
            yield return Wait(.65f);
            CheckResult("Esc-freezes-clouds-wind-and-clock", ctx.Paused && ctx.Clock == pausedClock && Shader.GetGlobalFloat("_CityTime") == shaderClock &&
                atmosphere.Drift == drift && cloud.position == cloudPosition,
                "clock delta=" + (ctx.Clock - pausedClock) + " shader delta=" + (Shader.GetGlobalFloat("_CityTime") - shaderClock) + " drift=" + Vector3.Distance(atmosphere.Drift, drift));
            CheckResult("Esc-freezes-street-gusts-and-leaves", pausedEmissions == streetWind.EmittedLeaves + streetWind.EmittedGusts && windTransforms.Select((t,i) => t.position == windPositions[i]).All(same => same));
            ctx.UI.SetSettings(false); yield return Wait(.3f);
            CheckResult("closing-settings-resumes-atmosphere", !ctx.Paused && Vector3.Distance(drift, atmosphere.Drift) > .5f && ctx.Clock > pausedClock);

            ctx.Systems.Weather.Request("clear", true); yield return Wait(1.6f); ctx.Pause(true);
            var tree = FindObjectsByType<LODGroup>(FindObjectsSortMode.None)
                .Where(t => t.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterial && r.sharedMaterial.HasProperty("_Foliage") && r.sharedMaterial.GetFloat("_Foliage") > .5f))
                .OrderBy(t => Vector3.SqrMagnitude(t.transform.position - player)).First();
            tree.ForceLOD(0);
            ctx.Camera.transform.position = tree.transform.position + new Vector3(9, 5, -16);
            ctx.Camera.transform.LookAt(tree.transform.position + Vector3.up * 4.2f);
            var ray = new Ray(tree.transform.position + new Vector3(-3, 1.2f, 0), Vector3.right);
            bool hitBefore = Physics.Raycast(ray, out var before, 6, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            Shader.SetGlobalVector("_CityWind", Vector3.zero); var still = Capture("04-tree-calm");
            Shader.SetGlobalVector("_CityWind", new Vector3(8, 0, 6)); var bent = Capture("05-tree-wind");
            int windPixels = Changed(still, bent);
            CheckResult("GPU-wind-visibly-bends-foliage", windPixels > 100, "changed pixels=" + windPixels);
            Shader.SetGlobalFloat("_CityTime", ctx.Clock + 1.7f); var gust = Capture("06-tree-gust");
            int gustPixels = Changed(bent, gust);
            CheckResult("GPU-wind-sways-over-time", gustPixels > 60, "changed pixels=" + gustPixels);
            bool hitAfter = Physics.Raycast(ray, out var after, 6, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            CheckResult("tree-trunk-collision-stays-fixed", hitBefore && hitAfter && before.collider == after.collider && Mathf.Abs(before.distance - after.distance) < .0001f);
            Destroy(still); Destroy(bent); Destroy(gust); tree.ForceLOD(-1);
            Shader.SetGlobalFloat("_CityTime", ctx.Clock); Shader.SetGlobalVector("_CityWind", ctx.Systems.Weather.Wind);

            CheckGroundCover(atmosphere, player);

            ctx.Camera.transform.position = player + Vector3.up * 12;
            ctx.Camera.transform.rotation = Quaternion.Euler(-9, 35, 0);
            ctx.Clock = 210; ctx.Pause(false); yield return Wait(.25f);
            CheckResult("clouds-follow-night-lighting", ctx.Night > .95f && Shader.GetGlobalColor("_CityCloudLit").grayscale < clearLight * .3f);
            ctx.Pause(true); var night = Capture("07-night-clouds"); Destroy(night);

            ctx.Pause(false); ctx.Follow.enabled = true; ctx.Motor.Warp(player); ctx.Follow.Face(0);
            yield return Wait(.3f); float yaw = ctx.Camera.transform.eulerAngles.y;
            ctx.Input.TouchLook = new Vector2(20, 0); yield return null; yield return Wait(.25f);
            CheckResult("camera-orbit-remains-responsive", Mathf.Abs(Mathf.DeltaAngle(yaw, ctx.Camera.transform.eulerAngles.y)) > 20);
            CheckResult("only-explicit-QA-probe-installed", FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(p => !p.GetType().Name.EndsWith("Probe", StringComparison.Ordinal) || p == this));
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("; ", report.errors));
            ctx.Motor.Warp(player); ctx.Player.rotation = rotation; ctx.Follow.enabled = follow;
            ctx.Clock = clock; ctx.Systems.Weather.Request(weather, true);
            File.WriteAllText(Path.Combine(dir, "atmosphere-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("ATMOSPHERE_QA_FINISHED " + report.passed);
            if (QuitWhenFinished) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
