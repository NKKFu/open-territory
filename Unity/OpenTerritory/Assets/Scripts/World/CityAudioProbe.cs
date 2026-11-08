using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Opt-in integration test, with the real native DSP device enabled.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityAudioProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public int sampleRate, captureFrames; public float outputPeak; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        readonly float[] output = new float[2048];
        CityContext ctx; CityAudio audio; CityAudioCapture capture; Vector2 move; bool brake, finished; string dir;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-audio-qa") >= 0) new GameObject("QA de áudio • opt-in").AddComponent<CityAudioProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update()
        {
            if (!ctx || finished) return;
            ctx.Input.TouchMove = move; ctx.Input.TouchBrake = brake;
            AudioListener.GetOutputData(output, 0);
            foreach (float sample in output) report.outputPeak = Mathf.Max(report.outputPeak, Mathf.Abs(sample));
        }
        void Test(string name, bool passed, float value = 0, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("AUDIO_QA " + name + " " + passed + " " + value + " " + detail); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.persistentDataPath; Directory.CreateDirectory(dir);
            float end = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < end) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            audio = ctx.Systems.Audio; report.sampleRate = AudioSettings.outputSampleRate;
            Test("bank-loaded-with-no-missing-assets", audio && audio.Manifest != null && audio.Manifest.version == 5 && audio.Manifest.clips.Length == 129 && audio.MissingClips == 0, audio && audio.Manifest != null ? audio.Manifest.clips.Length : 0);
            if (!audio || audio.Manifest == null) { Finish(); yield break; }
            Test("every-clip-has-licensed-source-provenance", audio.Manifest.clips.All(e => !string.IsNullOrEmpty(e.source) && e.license == "CC0-1.0"), audio.Manifest.clips.Length);
            int invalid = 0, mismatched = 0; float peak = 0;
            foreach (var entry in audio.Manifest.clips)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + entry.id);
                if (!clip || clip.frequency != 44100 || clip.loadState != AudioDataLoadState.Loaded) { invalid++; continue; }
                var samples = new float[clip.samples * clip.channels]; clip.GetData(samples, 0);
                double energy = 0; float clipPeak = 0;
                foreach (float sample in samples) { if (float.IsNaN(sample) || float.IsInfinity(sample)) invalid++; energy += sample * (double)sample; clipPeak = Mathf.Max(clipPeak, Mathf.Abs(sample)); peak = Mathf.Max(peak, Mathf.Abs(sample)); }
                if (Math.Abs(Math.Sqrt(energy / samples.Length) - entry.rms) > .00003 || Mathf.Abs(clipPeak - entry.peak) > .00005f) mismatched++;
            }
            Test("all-clips-native-44khz-finite-with-headroom", invalid == 0 && peak <= .87f, peak);
            Test("built-audio-content-matches-imported-source-levels", mismatched == 0, mismatched);
            Test("stereo-long-ambience-loops", Resources.Load<AudioClip>("Audio/sea").channels == 2 && Resources.Load<AudioClip>("Audio/sea").length >= 18);
            var beds = audio.Manifest.clips.Where(e => e.category == "ambience" && e.loop).ToArray();
            Test("six-recorded-ambience-loops-with-smooth-joins", beds.Length == 6 && beds.All(e => e.seconds >= 20 && e.seam < .0001f), beds.Length);
            var legacy = new CityUI.Options(); JsonUtility.FromJsonOverwrite("{\"volume\":0.4,\"quality\":2}", legacy);
            Test("older-settings-keep-new-audio-defaults", legacy.effects == 1 && legacy.ambience == 1 && legacy.music > .7f && legacy.volume == .4f);
            ctx.UI.SetAudioMix(1, 1, 0, 0); yield return new WaitForSeconds(.8f);
            capture = ctx.Camera.gameObject.AddComponent<CityAudioCapture>(); capture.Begin(report.sampleRate, 24);
            ctx.Motor.Warp(ctx.Data.spawn); ctx.Follow.Face(90); ctx.Motor.SetInputLock(false);
            int startSteps = audio.PlayerSteps; move = Vector2.up; yield return new WaitForSeconds(2.1f); move = Vector2.zero;
            Test("walking-emits-distance-driven-footsteps", audio.PlayerSteps - startSteps >= 3, audio.PlayerSteps - startSteps);
            yield return new WaitForSeconds(.4f); int stoppedSteps = audio.PlayerSteps; yield return new WaitForSeconds(.7f);
            Test("idle-does-not-emit-footsteps", audio.PlayerSteps == stoppedSteps);
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false;
            var eye = ctx.Player.position + Vector3.up * 1.7f; ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.identity);
            var ids = new HashSet<string>(); int recordedSurfaces = 0;
            foreach (string surface in new[] { "concrete", "gravel", "grass", "wood" })
            {
                ids.Clear();
                for (int i = 0; i < 4; i++)
                {
                    audio.Play("footstep-" + surface, eye + Vector3.forward, .7f); ids.Add(audio.LastSource.clip.name);
                    yield return new WaitForSeconds(.13f);
                }
                if (ids.Count == 4) recordedSurfaces++;
            }
            Test("four-surfaces-use-sixteen-distinct-recorded-steps", recordedSurfaces == 4, recordedSurfaces);
            ids.Clear();
            for (int i = 0; i < 3; i++)
            {
                audio.Play("hurt", eye + Vector3.forward * 3, .5f); ids.Add(audio.LastSource.clip.name);
                yield return new WaitForSeconds(.6f);
            }
            Test("human-pain-rotates-three-recorded-performances", ids.Count == 3 && ids.All(n => n.StartsWith("hurt-")), ids.Count);
            var street = ReferenceMap.Layout.roads.First(r => r.kind != "trail");
            var trail = ReferenceMap.Layout.roads.First(r => r.kind == "trail");
            var dock = ctx.Data.docks[0];
            Test("surface-follows-road-trail-and-pier", audio.Surface(street.points[street.points.Length / 2]) == "concrete" && audio.Surface(trail.points[trail.points.Length / 2]) == "gravel" && audio.Surface(new Vector3(dock.x, dock.top, dock.z)) == "wood");
            Test("distant-one-shots-are-inaudible-and-culled", !audio.Play("horn", eye + Vector3.forward * 400));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Parede temporária • QA de oclusão"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = eye + Vector3.forward * 4; wall.transform.localScale = new Vector3(8, 6, .35f); Physics.SyncTransforms();
            audio.Play("phone", eye + Vector3.forward * 8); var blocked = audio.LastSource;
            Test("wall-muffles-and-attenuates-source", blocked.GetComponent<AudioLowPassFilter>().cutoffFrequency < 2000 && blocked.volume < .3f, blocked.GetComponent<AudioLowPassFilter>().cutoffFrequency);
            Destroy(wall); yield return new WaitForSeconds(.35f);
            Test("occlusion-recovers-after-wall-is-removed", blocked.GetComponent<AudioLowPassFilter>().cutoffFrequency > 10000, blocked.GetComponent<AudioLowPassFilter>().cutoffFrequency);
            yield return Vehicle();
            capture.End(Path.Combine(dir, "previa-no-jogo.wav")); report.captureFrames = capture.Frames;
            Test("recorded-real-native-audio-output", capture.Frames > report.sampleRate * 5 && capture.Peak > .001f, capture.Peak, "frames=" + capture.Frames);
            ctx.UI.SetAudioMix(1, 0, 1, 0); yield return new WaitForSeconds(.3f);
            Test("effects-can-be-muted-independently", audio.BusGain("effects") == 0 && audio.BusGain("ambience") == 1 && ctx.Vehicles.Fleet.All(c => c.Engine.volume < .001f));
            capture.Begin(report.sampleRate, 12);
            Place(ReferenceMap.FromPixel(250, 515)); yield return new WaitForSeconds(1);
            float coastal = audio.LoopSource("sea").volume;
            Place(ReferenceMap.FromPixel(710, 115)); yield return new WaitForSeconds(1.3f);
            float inland = audio.LoopSource("sea").volume;
            Test("sea-follows-new-coast-not-old-z-coordinate", coastal > inland * 1.5f, coastal, "inland=" + inland);
            ctx.Systems.Weather.Request("squall", true); yield return new WaitForSeconds(.8f);
            float outdoorRain = audio.LoopSource("rain").volume;
            var place = ctx.Data.places.First(p => p.kind == "market"); Place(new Vector3(place.x, place.elevation + .2f, place.z)); yield return new WaitForSeconds(1);
            Test("interiors-shelter-and-filter-the-rain", audio.Indoors && audio.LoopSource("rain").volume < outdoorRain * .5f && audio.LoopSource("rain").GetComponent<AudioLowPassFilter>().cutoffFrequency < 2500, audio.LoopSource("rain").volume, "outside=" + outdoorRain);
            capture.End(Path.Combine(dir, "ambientes-no-jogo.wav"));
            Test("downloaded-ambience-recorded-from-native-dsp", capture.Frames > report.sampleRate * 3 && capture.Peak > .001f, capture.Peak, "frames=" + capture.Frames);
            ctx.UI.SetAudioMix(1, 1, 0, 1); ctx.Systems.Audio.TuneRadio(2); ctx.Camera.transform.position = ReferenceMap.Layout.harborRadio + Vector3.up;
            yield return new WaitForSeconds(.7f);
            var radio = audio.LoopSource("radio-2");
            Test("radio-plays-continuous-licensed-performance", radio && radio.isPlaying && radio.loop && radio.clip.length > 16);
            ctx.Systems.Weather.Request("clear", true); ctx.UI.SetAudioMix(1, 1, 1, .8f);
            Place(ctx.Data.spawn); yield return new WaitForSeconds(.5f);
            var wind = audio.LoopSource("wind"); int sampleBeforePause = wind.timeSamples;
            var theme = ctx.UI.GetComponent<CityMenuMusic>();
            Test("menu-theme-is-silent-during-exploration", theme && theme.Source && !theme.Source.isPlaying);
            ctx.UI.SetSettings(true);
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (button.name == "Som") button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.35f);
            Test("settings-pause-freezes-audio-playhead", AudioListener.pause && Mathf.Abs(wind.timeSamples - sampleBeforePause) < 2205, wind.timeSamples - sampleBeforePause);
            var sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Test("four-accessible-sound-sliders", new[] { "Volume", "Efeitos", "Ambiente", "Música e rádio" }.All(n => sliders.Any(s => s.name == n)));
            yield return MenuMusic(sliders);
            CaptureSettings(); yield return new WaitForSecondsRealtime(.1f);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(.3f);
            Test("audio-resumes-after-settings", !AudioListener.pause && wind.timeSamples != sampleBeforePause);
            Test("menu-theme-is-paused-after-closing-settings", theme && !theme.Source.isPlaying && theme.Source.volume == 0);
            ctx.UI.SetAudioMix(0, 1, 1, .8f); Test("master-volume-controls-listener", AudioListener.volume == 0);
            ctx.UI.SetAudioMix(1, 1, 1, .8f); yield return new WaitForSeconds(.15f);
            Test("native-output-is-audible-and-unclipped", report.outputPeak > .001f && report.outputPeak < .98f, report.outputPeak);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish();
        }
        IEnumerator MenuMusic(Slider[] sliders)
        {
            var theme = ctx.UI.GetComponent<CityMenuMusic>();
            var source = theme ? theme.Source : null;
            Test("licensed-menu-theme-is-loaded-and-streamed", source && source.clip && source.clip.name == "bossa-antigua" && source.clip.length > 270 && source.clip.channels == 2 && source.clip.loadType == AudioClipLoadType.Streaming);
            if (!source || !source.clip) yield break;
            Test("menu-theme-plays-in-2d-while-city-audio-is-paused", AudioListener.pause && Time.timeScale == 0 && source.isPlaying && source.ignoreListenerPause && source.loop && source.spatialBlend == 0);
            int before = source.timeSamples; float clock = ctx.Clock;
            capture.Begin(report.sampleRate, 3);
            yield return new WaitForSecondsRealtime(1.2f);
            capture.End(Path.Combine(dir, "menu-bossa-antigua.wav"));
            Test("menu-theme-advances-with-the-world-frozen", source.timeSamples > before && ctx.Clock == clock);
            Test("menu-theme-is-audible-in-native-dsp", capture.Frames > report.sampleRate / 2 && capture.Peak > .001f && capture.Peak < .98f, capture.Peak);

            var musicSlider = sliders.First(s => s.name == "Música e rádio");
            musicSlider.value = .2f; yield return new WaitForSecondsRealtime(.4f);
            Test("music-slider-controls-menu-theme-during-pause", Mathf.Abs(source.volume - .11f) < .005f, source.volume);
            musicSlider.value = 0; yield return new WaitForSecondsRealtime(.1f);
            Test("music-slider-mutes-menu-theme", source.volume == 0);
            musicSlider.value = .8f;
            sliders.First(s => s.name == "Volume").value = 0;
            Test("master-slider-mutes-menu-theme", AudioListener.volume == 0);
            ctx.UI.SetAudioMix(1, 0, 0, .8f); yield return new WaitForSecondsRealtime(.4f);
            Test("menu-theme-is-independent-of-effects-and-ambience", Mathf.Abs(source.volume - .44f) < .005f, source.volume);
            ctx.UI.SetAudioMix(1, 1, 1, .8f);

            source.timeSamples = source.clip.samples - source.clip.frequency / 3;
            yield return new WaitForSecondsRealtime(.75f);
            Test("menu-theme-loops-at-the-end-of-the-track", source.isPlaying && source.time < 2, source.time);
            ctx.UI.SetSettings(false);
            int pausedAt = source.timeSamples;
            yield return new WaitForSecondsRealtime(.15f);
            Test("closing-menu-freezes-theme-playhead", !source.isPlaying && source.timeSamples == pausedAt);
            ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.2f);
            Test("reopening-menu-resumes-the-same-music-voice", source.isPlaying && source.timeSamples > pausedAt && source.timeSamples < pausedAt + source.clip.frequency && FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(s => s.clip == source.clip) == 1);

            var credit = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "Crédito da música");
            var license = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "Licença da música");
            Test("menu-shows-title-artist-and-license-links", credit && credit.interactable && credit.GetComponentInChildren<Text>().text.Contains("Bossa Antigua · Kevin MacLeod") && license && license.interactable && license.GetComponentInChildren<Text>().text.Contains("CC BY 4.0"));
            var credits = Resources.Load<TextAsset>("Music/CREDITS");
            Test("offline-credits-include-source-license-and-adaptations", credits && credits.text.Contains(CityMenuMusic.SourceUrl) && credits.text.Contains(CityMenuMusic.LicenseUrl) && credits.text.Contains("without cuts or edits") && File.Exists(Path.Combine(Application.streamingAssetsPath, "Credits/MENU-MUSIC.txt")));
        }
        void Place(Vector3 point)
        {
            point.y = Mathf.Max(point.y, ctx.Ground(point.x, point.z) + .05f);
            ctx.Motor.Warp(point); ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false;
            ctx.Camera.transform.SetPositionAndRotation(point + Vector3.up * 1.7f, Quaternion.identity);
        }
        void CaptureSettings()
        {
            // Hidden native windows have no back buffer for ScreenCapture. Render
            // the same uGUI canvas through the camera into an explicit target.
            var canvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.renderMode == RenderMode.ScreenSpaceOverlay);
            var mode = canvas.renderMode; var worldCamera = canvas.worldCamera; float plane = canvas.planeDistance;
            var active = RenderTexture.active;
            var cameraObject = new GameObject("Câmera de captura das configurações • QA");
            cameraObject.transform.position = Vector3.one * 20000;
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .065f, .08f);
            var target = new RenderTexture(960, 600, 24); target.Create();
            var texture = new Texture2D(960, 600, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(dir, "configuracoes-de-som.png"), texture.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = worldCamera; canvas.planeDistance = plane;
                camera.targetTexture = null; RenderTexture.active = active; target.Release(); Destroy(target); Destroy(texture); Destroy(cameraObject);
            }
        }
        IEnumerator Vehicle()
        {
            var road = ReferenceMap.Layout.roads.First(r => r.id == "101-inland"); int k = road.points.Length / 2;
            var definition = ctx.Data.vehicles.First(c => !c.auto && c.kind == "car");
            var heading = Quaternion.LookRotation(CityPhysics.Flat(road.points[k + 2] - road.points[k - 2]).normalized);
            var car = ctx.Vehicles.Fleet.FirstOrDefault(c => c.Data.id == definition.id) ?? ctx.Vehicles.Spawn(definition, road.points[k] + Vector3.up * .06f, heading);
            car.LastUsed = ctx.Clock;
            car.Body.position = road.points[k] + Vector3.up * .06f; car.Body.rotation = Quaternion.LookRotation(CityPhysics.Flat(road.points[k + 2] - road.points[k - 2]).normalized);
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
            var entrance = car.Root.TransformPoint(new Vector3(-car.Data.width * .5f - .75f, 0, car.Data.seatMount.z)); entrance.y = ctx.Ground(entrance.x, entrance.z) + .05f;
            ctx.Motor.SetInputLock(false); ctx.Motor.Warp(entrance); ctx.Follow.enabled = true; ctx.Follow.Face(car.Root.eulerAngles.y);
            // Freshly streamed cars have no pre-test settling time. Establish
            // a stationary idle before comparing it with acceleration.
            brake = true;
            yield return new WaitForSeconds(.7f); ctx.Actions.First(a => a.id == "vehicle-" + car.Data.id).Execute(); yield return new WaitForSeconds(2.8f);
            float idle = car.Engine.pitch; Test("native-car-entry-starts-engine-loop", ctx.Vehicles.Active == car && car.Engine.isPlaying && car.Engine.loop);
            float idleSpeed = car.Speed; brake = false; move = Vector2.up; yield return new WaitForSeconds(2); move = Vector2.zero;
            Test("engine-pitch-follows-actual-rpm", car.Engine.pitch > idle + .08f, car.Engine.pitch, "idle=" + idle + " idle-speed=" + idleSpeed + " rpm=" + car.Motion.Rpm);
            brake = true; yield return new WaitForSeconds(2); brake = false;
            ctx.Actions.First(a => a.id == "vehicle-exit").Execute(); yield return new WaitForSeconds(1.3f);
            var patrolData = ctx.Data.vehicles.First(c => c.role == "police");
            var patrol = ctx.Vehicles.Fleet.FirstOrDefault(c => c.Data.id == patrolData.id) ?? ctx.Vehicles.Spawn(patrolData, road.points[k] + Vector3.right * 30 + Vector3.up * .06f, Quaternion.identity);
            patrol.LastUsed = ctx.Clock;
            ctx.Follow.enabled = false; ctx.Camera.transform.position = patrol.Position + Vector3.up * 2; patrol.Siren = true; yield return new WaitForSeconds(.6f);
            Test("siren-is-continuous-not-repeated-bursts", patrol.SirenSource.isPlaying && patrol.SirenSource.loop && patrol.SirenSource.clip.length >= 6);
            patrol.Siren = false;
        }
        void Finish()
        {
            finished = true; if (ctx) ctx.UI.SetSettings(false);
            File.WriteAllText(Path.Combine(dir, "audio-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2);
        }
    }
    // Real listener DSP capture. No file IO, allocations or Unity APIs in callback.
    public sealed class CityAudioCapture : MonoBehaviour
    {
        float[] samples; volatile bool recording; int count, channels, rate;
        public int Frames => channels > 0 ? count / channels : 0;
        public float Peak { get; private set; }
        public void Begin(int sampleRate, int seconds) { recording = false; rate = sampleRate; samples = new float[sampleRate * seconds * 8]; count = 0; channels = 0; Peak = 0; recording = true; }
        void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (!recording || samples == null) return;
            channels = channelCount; int n = Math.Min(data.Length, samples.Length - count);
            Array.Copy(data, 0, samples, count, n); System.Threading.Volatile.Write(ref count, count + n);
        }
        public void End(string path)
        {
            recording = false;
            if (channels < 1 || count < 1) return;
            using (var file = new BinaryWriter(File.Create(path)))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); file.Write(36 + count * 2); file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                file.Write(16); file.Write((short)1); file.Write((short)channels); file.Write(rate); file.Write(rate * channels * 2); file.Write((short)(channels * 2)); file.Write((short)16);
                file.Write(System.Text.Encoding.ASCII.GetBytes("data")); file.Write(count * 2);
                for (int i = 0; i < count; i++) { Peak = Mathf.Max(Peak, Mathf.Abs(samples[i])); file.Write((short)(Mathf.Clamp(samples[i], -1, 1) * 32767)); }
            }
        }
    }
}
