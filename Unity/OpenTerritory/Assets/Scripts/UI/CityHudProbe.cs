using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Explicit opt-in only. The --city-hud-qa flag also isolates wallet, equipment and options storage.
    [DefaultExecutionOrder(-900)]
    public sealed class CityHudProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        CityContext ctx; CityCanvasUI view; string dir; bool left, right, hazard; float started;
        public bool Finished { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-hud-qa") >= 0) new GameObject("QA HUD · opt-in").AddComponent<CityHudProbe>(); }
        void Awake() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("HUD_QA " + passed + " " + name + " " + detail); }
        void Update()
        {
            if (Finished) return;
            if (Time.realtimeSinceStartup - started > 100) { CheckResult("probe-timeout", false); Finish(); return; }
            if (!ctx) return;
            if (left) { ctx.Input.LeftSignal = true; left = false; }
            if (right) { ctx.Input.RightSignal = true; right = false; }
            if (hazard) { ctx.Input.Emergency = true; hazard = false; }
        }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); Canvas.ForceUpdateCanvases(); }
        IEnumerator Wait(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; yield return Frame(); }
        Text Caption(string name) => view.GetComponentsInChildren<Text>(true).First(t => t.name == name);
        CityHudGraphic Icon(string name) => view.GetComponentsInChildren<CityHudGraphic>(true).First(t => t.name == name);
        RectTransform Card(string name) => (RectTransform)Icon(name).transform;
        bool Fits(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return corners.All(p => p.x >= Screen.safeArea.xMin && p.x <= Screen.safeArea.xMax && p.y >= Screen.safeArea.yMin && p.y <= Screen.safeArea.yMax);
        }
        void Capture(string name)
        {
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            var previousTarget = ctx.Camera.targetTexture; var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24); Texture2D picture = null;
            try
            {
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ctx.Camera; c.planeDistance = 1; }
                ctx.Camera.targetTexture = target; Canvas.ForceUpdateCanvases(); ctx.Camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
            }
            finally
            {
                ctx.Camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i]; }
                Canvas.ForceUpdateCanvases(); RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture);
            }
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/hud/native")); Directory.CreateDirectory(dir);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; view = ctx.GetComponent<CityCanvasUI>(); yield return Wait(.6f);
            ctx.Combat.Holster(); yield return Frame();
            CheckResult("empty-hands-hide-weapon-and-vehicle-hud", !view.WeaponHudVisible && !view.VehicleHudVisible);
            var vignette = view.GetComponentInChildren<CityVignetteGraphic>(true);
            CheckResult("subtle-vignette-behind-ui-does-not-block-pointer", view.VignetteVisible && !vignette.raycastTarget && vignette.transform.GetSiblingIndex() == 0 && vignette.color.a <= .25f);
            CheckResult("vignette-preserves-center-and-darkens-corners", CityVignetteGraphic.EdgeOpacity(new Vector2(.5f, .5f)) == 0 && CityVignetteGraphic.EdgeOpacity(Vector2.zero) > .95f);
            CheckResult("all-instruments-are-passive", view.GetComponentsInChildren<CityHudGraphic>(true).All(g => !g.raycastTarget));
            Capture("01-exploracao");
            for (int i = 0; i < CityCombat.Weapons.Length; i++)
            {
                var weapon = CityCombat.Weapons[i]; ctx.Combat.Grant(weapon.Id); ctx.Input.TouchWeapon = i + 1; yield return Frame();
                CheckResult("weapon-selection-" + weapon.Id, view.WeaponHudVisible && !view.VehicleHudVisible && Caption("Arma equipada").text == CityEnglish.Text(weapon.Name) && Fits(Card("Arma na mão")));
                Capture("02-arma-" + weapon.Id);
            }
            ctx.Input.TouchWeapon = 1; yield return Frame(); ctx.Input.TouchAim = true; ctx.Input.TouchFire = true; yield return Frame(); ctx.Input.TouchAim = false;
            CheckResult("firing-updates-real-ammunition", ctx.Combat.Ammo("pulse") == 0 && Caption("Munição").text == "0 / 8");
            ctx.Input.TouchReload = true; yield return Frame(); CheckResult("reload-state-is-visible", ctx.Combat.Reloading && Caption("Munição").text == "Reloading…");
            yield return Wait(1.4f); CheckResult("reload-completion-updates-ammunition", !ctx.Combat.Reloading && Caption("Munição").text == "1 / 7");
            ctx.Input.TouchHolster = true; yield return Frame(); CheckResult("X-holsters-and-clears-hud", ctx.Combat.Equipped == null && !view.WeaponHudVisible);
            ctx.UI.SetSettings(true); yield return Frame(); var toggle = view.GetComponentsInChildren<Toggle>(true).First(t => t.name == "Vinheta suave");
            toggle.isOn = false; yield return Frame(); CheckResult("settings-can-disable-vignette", !view.VignetteVisible); toggle.isOn = true; ctx.UI.SetSettings(false); yield return Frame();
            ctx.Input.TouchMap = true; yield return Frame(); CheckResult("map-hides-game-instruments-and-vignette", ctx.UI.MapOpen && !view.WeaponHudVisible && !view.VehicleHudVisible && !view.VignetteVisible);
            ctx.Input.TouchMap = true; yield return Frame();

            ctx.Population.enabled = false;
            var carData = ctx.Data.vehicles.First(c => !c.auto && c.kind == "car");
            var carRoot = ctx.Node(carData.id);
            var car = ctx.Vehicles.Fleet.FirstOrDefault(c => c.Data.id == carData.id) ?? ctx.Vehicles.Spawn(carData, carRoot.position, carRoot.rotation);
            var home = car.Position; var rotation = car.Root.rotation;
            var action = ctx.Actions.First(a => a.id == "vehicle-" + car.Data.id);
            var entry = car.Root.TransformPoint(new Vector3(-(car.Data.width * .5f + .75f), 0, car.Data.seatMount.z)); entry.y = ctx.Ground(entry.x, entry.z) + .045f;
            ctx.Motor.Warp(entry); yield return Wait(.3f); action.Execute();
            float entryUntil = Time.time + 8;
            while (ctx.Motor.Busy && Time.time < entryUntil) yield return null;
            yield return Frame();
            CheckResult("entering-real-car-switches-to-speedometer", ctx.Vehicles.Active == car && view.VehicleHudVisible && !view.WeaponHudVisible);
            if (ctx.Vehicles.Active != car) { Finish(); yield break; }
            CheckResult("stationary-speed-is-zero", Caption("Velocidade").text == "0");
            CheckResult("headlight-indicator-matches-physical-lights", car.HeadlightsOn && car.Headlights.Any(l => l && l.isActiveAndEnabled && l.intensity > 0) && Icon("Faróis").color.g > .7f);
            left = true; yield return Frame(); CheckResult("Q-toggles-left-signal", car.Signal == -1);
            bool on = false, off = false, synchronized = true; float until = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < until) { yield return Frame(); bool lit = Icon("Seta esquerda").color.r > .9f; on |= lit; off |= !lit; synchronized &= lit == car.LeftIndicatorLit; }
            CheckResult("left-icon-synchronized-with-exterior", synchronized);
            CheckResult("indicator-has-both-blink-phases", on && off);
            right = true; yield return Frame(); CheckResult("C-switches-to-right-signal", car.Signal == 1);
            hazard = true; yield return Frame(); CheckResult("L-toggles-hazards", car.Hazards);
            until = Time.unscaledTime + 1.1f;
            while (Time.unscaledTime < until) { yield return Frame(); if (car.LeftIndicatorLit) break; }
            CheckResult("hazards-illuminate-both-hud-arrows", Icon("Seta esquerda").color.r > .9f && Icon("Seta direita").color.r > .9f); Capture("03-carro");

            // Hold physics only inside this isolated QA session to measure known ground velocities.
            ctx.Vehicles.enabled = false; car.Body.useGravity = false;
            foreach (var velocity in new[] { car.Root.forward * 10, -car.Root.forward * 5, car.Root.right * 4 })
            {
                car.Body.linearVelocity = velocity; yield return Frame();
                int expected = Mathf.RoundToInt(CityPhysics.Flat(car.Body.linearVelocity).magnitude * 3.6f);
                CheckResult("speedometer-measures-forward-reverse-and-slide", Caption("Velocidade").text == expected.ToString(), Caption("Velocidade").text + " expected " + expected);
            }
            car.Body.linearVelocity = Vector3.zero; car.Body.position = home; car.Body.rotation = rotation; car.Body.useGravity = true; ctx.Vehicles.enabled = true; yield return Frame();
            ctx.UI.SetSettings(true); yield return Frame(); CheckResult("Esc-hides-instruments-and-pauses", ctx.Paused && !view.VehicleHudVisible); ctx.UI.SetSettings(false); yield return Frame();
            var touch = view.GetComponentsInChildren<Toggle>(true).First(t => t.name == "Mostrar controles de toque"); touch.isOn = true; yield return Frame();
            CheckResult("touch-hud-stays-above-action-buttons", Fits(Card("Instrumentos do carro")) && Card("Instrumentos do carro").anchoredPosition.y >= 240); Capture("04-toque"); touch.isOn = false;
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1024, 768), new Vector2Int(800, 800), new Vector2Int(540, 960) })
            { Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed); yield return Wait(.35f); CheckResult("safe-layout-" + size.x + "x" + size.y, Fits(Card("Instrumentos do carro"))); Capture("05-carro-" + size.x + "x" + size.y); }
            ctx.Input.TouchInteract = true; yield return Wait(1.2f); CheckResult("leaving-car-clears-speedometer", ctx.Vehicles.Active == null && !view.VehicleHudVisible);
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors)); Finish();
        }
        void Finish()
        {
            if (Finished) return; Finished = true; StopAllCoroutines();
            if (ctx) { ctx.Input.TouchAim = ctx.Input.TouchFire = false; ctx.Pause(false); }
            File.WriteAllText(Path.Combine(dir, "hud-qa.json"), JsonUtility.ToJson(report, true));
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-hud-qa") >= 0) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
