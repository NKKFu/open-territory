using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Explicit native/Editor QA only. Never installed in a normal game.
    public sealed class CitySunRaysProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true, completed;
            public string unity, graphicsApi, startedUtc;
            public int width = 1280, height = 720, quality, msaa;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        public string ReportDirectory;
        public bool QuitWhenFinished = true;
        CityContext ctx;
        CitySunRays rays;
        string directory;
        GameObject fixture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-sunrays-qa") >= 0)
                new GameObject("QA de raios solares • opt-in").AddComponent<CitySunRaysProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("SUNRAYS_QA " + name + " " + passed + " " + detail);
        }
        Texture2D Capture(string name)
        {
            var camera = ctx.Camera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(report.width, report.height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(report.width, report.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, report.width, report.height), 0, 0); texture.Apply();
                if (!string.IsNullOrEmpty(name)) File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
                return texture;
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); }
        }
        int Pair(string name)
        {
            rays.enabled = false; var off = Capture(name == null ? null : name + "-off");
            rays.enabled = true; var on = Capture(name == null ? null : name + "-on");
            var a = off.GetPixels32(); var b = on.GetPixels32(); int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 12) changed++;
            Destroy(off); Destroy(on); return changed;
        }
        void CaptureSettings()
        {
            var canvas = ctx.UI.GetComponentsInChildren<Canvas>(true).First(c => c.name == "Interface da Baía");
            var mode = canvas.renderMode; var camera = canvas.worldCamera; float plane = canvas.planeDistance;
            bool active = rays.enabled;
            try
            {
                // Camera.Render does not include overlay canvases. Render the
                // same uGUI layout through the camera for this QA image only.
                rays.enabled = false; canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = ctx.Camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); var picture = Capture("08-settings"); Destroy(picture);
            }
            finally { canvas.renderMode = mode; canvas.worldCamera = camera; canvas.planeDistance = plane; rays.enabled = active; }
        }
        void TimeAndWeather(float clock, string weather = "clear")
        {
            ctx.Clock = clock; ctx.Systems.Weather.Request(weather, true); ctx.Systems.SendMessage("UpdateSky");
        }
        void FaceSun(Vector3 eye)
        {
            var sun = ctx.Systems.Sky.SunDirection;
            ctx.Camera.transform.SetPositionAndRotation(eye,
                Quaternion.LookRotation(new Vector3(sun.x, 0, sun.z).normalized + Vector3.up * .12f));
        }
        void MakeFixture(Vector3 eye)
        {
            var sun = ctx.Systems.Sky.SunDirection; var flat = new Vector3(sun.x, 0, sun.z).normalized;
            fixture = new GameObject("QA • pilares temporários");
            fixture.transform.SetPositionAndRotation(eye + flat * 14, Quaternion.LookRotation(flat));
            for (int i = -2; i <= 2; i++) Cube(new Vector3(i * 2.5f, 5, 0), new Vector3(.65f, 14, 1));
            Cube(new Vector3(0, 11, 0), new Vector3(13, .8f, 1));
        }
        GameObject Cube(Vector3 position, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(fixture.transform, false);
            cube.transform.localPosition = position; cube.transform.localScale = scale; return cube;
        }
        IEnumerator Start()
        {
            report.unity = Application.unityVersion; report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            report.startedUtc = DateTime.UtcNow.ToString("o");
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory :
                at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "sunrays-qa");
            Directory.CreateDirectory(directory);
            float end = Time.realtimeSinceStartup + 60;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < end) yield return null;
            ctx = CityContext.Instance;
            CheckResult("native-city-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            rays = ctx.Camera.GetComponent<CitySunRays>();
            CheckResult("effect-and-shader-in-native-player", rays && rays.enabled && Resources.Load<Shader>("Shaders/CitySunRays").isSupported);
            if (!rays) { Finish(); yield break; }
            yield return new WaitForSecondsRealtime(.5f);
            report.quality = ctx.UI.Quality; report.msaa = QualitySettings.antiAliasing;
            var camera = ctx.Camera; var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
            var oldClock = ctx.Clock; string oldWeather = ctx.Systems.Weather.Id; bool oldFollow = ctx.Follow.enabled, oldPaused = ctx.Paused;
            var atmosphere = FindFirstObjectByType<CityAtmosphere>(); bool oldAtmosphere = atmosphere.enabled;
            var clouds = atmosphere.GetComponentsInChildren<MeshRenderer>().Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == "OpenTerritory/CityCloud").ToArray();
            var cloudStates = clouds.Select(c => c.enabled).ToArray();
            var cloud = clouds[0].transform; var cloudPosition = cloud.position; var cloudRotation = cloud.rotation; var cloudScale = cloud.localScale;
            float cloudCover = Shader.GetGlobalFloat("_CityCloudCover");
            var optionsField = typeof(CityUI).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic);
            var reviewField = typeof(CityUI).GetField("reviewRun", BindingFlags.Instance | BindingFlags.NonPublic);
            var options = (CityUI.Options)optionsField.GetValue(ctx.UI);
            string oldOptions = JsonUtility.ToJson(options); bool oldReview = (bool)reviewField.GetValue(ctx.UI);
            reviewField.SetValue(ctx.UI, true); // Explicit Editor probes also preserve PlayerPrefs.
            try
            {
                ctx.Pause(true); ctx.Follow.enabled = false; atmosphere.enabled = false;
                foreach (var renderer in clouds) renderer.enabled = false;
                TimeAndWeather(65); var eye = ctx.Player.position + Vector3.up * 480; FaceSun(eye); MakeFixture(eye);
                int changed = Pair("01-pillars");
                CheckResult("visible-shafts-around-occluding-pillars", changed > 10000, "changed pixels=" + changed);
                CheckResult("effect-preserves-most-of-the-image", changed < report.width * report.height * .45f, "changed pixels=" + changed);
                CheckResult("bounded-low-resolution-buffers", rays.BufferWidth > 0 && rays.BufferWidth <= 480, "width=" + rays.BufferWidth);
                foreach (int msaa in new[] { 0, 2, 4 })
                {
                    QualitySettings.antiAliasing = msaa; changed = Pair("02-msaa-" + msaa);
                    CheckResult("occlusion-alignment-with-msaa-" + msaa, changed > 10000 && changed < report.width * report.height * .45f, "changed pixels=" + changed);
                }
                QualitySettings.antiAliasing = report.msaa;
                var wall = Cube(new Vector3(0, 4, -6), new Vector3(80, 80, 1));
                changed = Pair("03-covered-view");
                CheckResult("solid-covered-view-has-no-light-leak", changed == 0, "changed pixels=" + changed); DestroyImmediate(wall);
                fixture.SetActive(false);
                foreach (var renderer in clouds) renderer.enabled = false;
                clouds[0].enabled = true; cloud.position = eye + ctx.Systems.Sky.SunDirection * 150;
                cloud.rotation = Quaternion.identity; cloud.localScale = new Vector3(3, 4, 3);
                Shader.SetGlobalFloat("_CityCloudCover", 1);
                changed = Pair("04-cloud-occlusion");
                CheckResult("transparent-cloud-volume-blocks-rays", changed < 30 && rays.CloudDraws > 0, "changed pixels=" + changed + " cloud draws=" + rays.CloudDraws);
                clouds[0].enabled = false; Shader.SetGlobalFloat("_CityCloudCover", cloudCover);
                TimeAndWeather(65); FaceSun(eye); fixture.SetActive(true); Pair(null); float clearStrength = rays.ActiveStrength;
                TimeAndWeather(65, "squall"); Pair("05-squall");
                CheckResult("squall-reduces-sun-rays", rays.ActiveStrength < clearStrength * .1f, "clear=" + clearStrength + " squall=" + rays.ActiveStrength);
                TimeAndWeather(168); FaceSun(eye); changed = Pair("06-night");
                CheckResult("night-and-moon-do-not-emit-sun-rays", changed == 0 && rays.ActiveStrength == 0, "changed pixels=" + changed);
                TimeAndWeather(65); camera.transform.rotation = Quaternion.LookRotation(-ctx.Systems.Sky.SunDirection);
                changed = Pair(null); CheckResult("sun-behind-camera-is-a-clean-bypass", changed == 0 && rays.ActiveStrength == 0);
                FaceSun(eye); fixture.SetActive(false);
                for (int i = 0; i < clouds.Length; i++) clouds[i].enabled = cloudStates[i];
                cloud.SetPositionAndRotation(cloudPosition, cloudRotation); cloud.localScale = cloudScale;
                camera.transform.position = oldPosition; FaceSun(oldPosition);
                Pair("07-city");

                ctx.UI.SetSettings(true);
                var toggles = FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var rayToggle = toggles.First(t => t.name == "Raios solares");
                var aoToggle = toggles.First(t => t.name == "Oclusão ambiente");
                rayToggle.isOn = false; CheckResult("settings-toggle-disables-rays", !rays.enabled);
                rayToggle.isOn = true; CheckResult("settings-toggle-restores-rays", rays.enabled);
                aoToggle.isOn = false;
                CheckResult("disabling-AO-keeps-ray-depth", (camera.depthTextureMode & DepthTextureMode.Depth) != 0);
                rayToggle.isOn = false;
                CheckResult("both-disabled-release-owned-depth", (camera.depthTextureMode & DepthTextureMode.Depth) == 0);
                aoToggle.isOn = true;
                CheckResult("AO-keeps-depth-with-rays-disabled", (camera.depthTextureMode & DepthTextureMode.Depth) != 0);
                rayToggle.isOn = true;
                var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                buttons.First(b => b.name == "Qualidade Leve").onClick.Invoke();
                CheckResult("low-quality-disables-rays-and-toggle", !rays.enabled && !rayToggle.interactable);
                buttons.First(b => b.name == "Qualidade Equilibrada").onClick.Invoke();
                CheckResult("balanced-quality-restores-rays", rays.enabled && rayToggle.interactable);
                buttons.First(b => b.name == "Qualidade Alta").onClick.Invoke();
                var high = Capture(null); Destroy(high);
                CheckResult("high-quality-keeps-buffer-bounded", rays.BufferWidth <= 640 && rays.BufferWidth > 0, "width=" + rays.BufferWidth);
                buttons.First(b => b.name == "Qualidade Equilibrada").onClick.Invoke();
                var corners = new Vector3[4]; ((RectTransform)rayToggle.transform).GetWorldCorners(corners);
                var resume = buttons.First(b => b.name == "Voltar ao jogo"); var footer = new Vector3[4]; ((RectTransform)resume.transform).GetWorldCorners(footer);
                CheckResult("new-control-does-not-overlap-footer", corners[0].y >= footer[1].y, "toggle bottom=" + corners[0].y + " footer top=" + footer[1].y);
                CaptureSettings();
                float frozenClock = ctx.Clock; Vector3 frozenSun = ctx.Systems.Sky.SunDirection;
                yield return new WaitForSecondsRealtime(.4f);
                CheckResult("Esc-freezes-sun-source-and-world-clock", ctx.Paused && ctx.Clock == frozenClock && ctx.Systems.Sky.SunDirection == frozenSun);
                ctx.UI.SetSettings(false); yield return new WaitForSecondsRealtime(.3f);
                CheckResult("closing-settings-resumes-sun-motion", !ctx.Paused && ctx.Clock > frozenClock && ctx.Systems.Sky.SunDirection != frozenSun);
                CheckResult("only-explicit-QA-probe-installed", FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(p => !p.GetType().Name.EndsWith("Probe") || p == this));
                CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("; ", report.errors));
            }
            finally
            {
                if (fixture) Destroy(fixture);
                for (int i = 0; i < clouds.Length; i++) if (clouds[i]) clouds[i].enabled = cloudStates[i];
                cloud.SetPositionAndRotation(cloudPosition, cloudRotation); cloud.localScale = cloudScale;
                Shader.SetGlobalFloat("_CityCloudCover", cloudCover); atmosphere.enabled = oldAtmosphere;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation); ctx.Follow.enabled = oldFollow;
                ctx.Clock = oldClock; ctx.Systems.Weather.Request(oldWeather, true); ctx.UI.SetSettings(false); ctx.Pause(oldPaused);
                JsonUtility.FromJsonOverwrite(oldOptions, options); ctx.UI.Save();
                ctx.UI.GetComponent<CityCanvasUI>().Bind(options); reviewField.SetValue(ctx.UI, oldReview);
                QualitySettings.antiAliasing = report.msaa;
            }
            Finish();
        }
        void Finish()
        {
            report.completed = true;
            File.WriteAllText(Path.Combine(directory, "sunrays-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("SUNRAYS_QA_FINISHED " + report.passed);
            if (QuitWhenFinished) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
