using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Opt-in: --city-interactive-map-qa, or explicitly attach during Play Mode.
    public sealed class CityInteractiveMapProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        CityContext ctx; CityMapUI map; string dir;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-interactive-map-qa") >= 0)
                new GameObject("Mapa interativo QA opt-in").AddComponent<CityInteractiveMapProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("INTERACTIVE_MAP_QA " + name + " " + passed + " " + detail); }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); yield return Frame(); }
        Vector2 ScreenPoint(Vector2 local) => RectTransformUtility.WorldToScreenPoint(null, map.Viewport.TransformPoint(local));
        PointerEventData Pointer(Vector2 local, int id = -1)
        { return new PointerEventData(EventSystem.current) { pointerId = id, button = PointerEventData.InputButton.Left, position = ScreenPoint(local), eligibleForClick = true }; }
        void Click(Vector2 local, int id = -1)
        {
            var e = Pointer(local, id); var target = map.Viewport.gameObject;
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(target, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, e, ExecuteEvents.pointerClickHandler);
        }
        Button Button(string name) => map.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        ScrollRect GuideScroll => map.GetComponentsInChildren<ScrollRect>(true).First(s => s.name == "Lista de locais");
        void Reveal(Button button)
        {
            var scroll = GuideScroll; scroll.StopMovement();
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            scroll.verticalNormalizedPosition = overflow <= 0 ? 1 : Mathf.Clamp01(1 - (-((RectTransform)button.transform).anchoredPosition.y - 8) / overflow);
            Canvas.ForceUpdateCanvases();
        }
        bool ClickButton(Button button)
        {
            var rect = (RectTransform)button.transform;
            var e = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), eligibleForClick = true };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(e, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button) return false;
            ExecuteEvents.Execute(button.gameObject, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, e, ExecuteEvents.pointerClickHandler);
            return true;
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            var previousTarget = ctx.Camera.targetTexture; var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24); Texture2D picture = null;
            try
            {
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ctx.Camera; c.planeDistance = 1; }
                ctx.Camera.targetTexture = target; Canvas.ForceUpdateCanvases(); ctx.Camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); picture.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
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
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; map = ctx.UI.Map;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/interactive-map/editor"));
            Directory.CreateDirectory(dir);
            yield return Wait(.4f);
            CheckResult("closed-in-normal-play-and-M-binding", !map.IsOpen && CityInput.MapKey == KeyCode.M);
            CheckResult("probe-is-explicit-only", FindObjectsByType<CityInteractiveMapProbe>(FindObjectsSortMode.None).Length == 1);
            ctx.Input.TouchMap = true; yield return Frame();
            CheckResult("sampled-map-key-opens", map.IsOpen && ctx.UI.PointerBlocked && !ctx.Paused && Time.timeScale == 1);
            CheckResult("map-releases-visible-cursor", Cursor.lockState == CursorLockMode.None && Cursor.visible);
            CheckResult("chart-has-native-roads-and-building-footprints", map.GetComponentInChildren<CityMapGraphic>() && ctx.Data.places.Length >= 217 && ReferenceMap.Active);
            CheckResult("projection-roundtrip-and-north-up", Vector2.Distance(map.LocalToWorld(map.WorldToLocal(map.PlayerPoint)), map.PlayerPoint) < .01f &&
                map.WorldToLocal(map.PlayerPoint + Vector2.up * 10).y > map.WorldToLocal(map.PlayerPoint).y);

            var publicBuildings = ctx.Data.places.Where(p => p.kind != "home" && p.kind != "residence" && !p.name.StartsWith("#")).ToArray();
            CheckResult("all-public-buildings-have-map-destinations", publicBuildings.Length >= 13 && publicBuildings.All(p =>
                map.Places.Any(m => !m.bus && m.kind == p.kind && Vector2.Distance(m.point, new Vector2(p.x, p.z)) < .01f)), publicBuildings.Length + " public buildings");
            var serviceKinds = new[] { "bank", "range-card", "street-music", "harbor-crane", "harbor-drone", "harbor-weather", "district-guide" };
            CheckResult("public-service-points-have-map-destinations", ctx.Data.services.Where(s => serviceKinds.Contains(s.kind)).All(s =>
                map.Places.Any(p => p.kind == s.kind && Vector2.Distance(p.point, new Vector2(s.position.x, s.position.z)) < 4)));
            CheckResult("all-destinations-have-guide-buttons", map.Places.Where(p => !p.bus).All(p =>
                map.GetComponentsInChildren<Button>(true).Any(b => b.name == "Visitar no mapa · " + p.name)), map.Places.Count(p => !p.bus) + " destinations");
            CheckResult("homes-stay-as-footprints-without-directory-noise", !map.Places.Any(p => p.kind == "home" || p.kind == "residence" || p.name.StartsWith("#")));
            var marina = map.Places.First(p => p.kind == "marina"); var marinaStop = ctx.Data.stops.First(p => p.name == "Marina");
            CheckResult("marina-marks-physical-pier-separately-from-bus-stop", Vector2.Distance(marina.point, new Vector2(ctx.Data.docks[0].x, ctx.Data.docks[0].z)) < .01f &&
                Vector2.Distance(marina.point, new Vector2(marinaStop.x, marinaStop.z)) > 20 && map.Places.Any(p => p.bus && p.name == "Parada · Marina"));

            var guideScroll = GuideScroll;
            CheckResult("guide-list-clips-and-scrolls-above-fixed-actions", guideScroll.viewport.GetComponent<RectMask2D>() &&
                guideScroll.content.rect.height > guideScroll.viewport.rect.height && guideScroll.vertical && !guideScroll.horizontal && guideScroll.verticalScrollbar);
            float chartZoom = map.Zoom;
            var guideWheel = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 40 };
            ExecuteEvents.ExecuteHierarchy(guideScroll.content.GetChild(0).gameObject, guideWheel, ExecuteEvents.scrollHandler);
            yield return Frame();
            CheckResult("guide-wheel-scrolls-without-zooming-chart", guideScroll.content.anchoredPosition.y > 20 && map.Zoom == chartZoom);
            guideScroll.verticalNormalizedPosition = 0; yield return Frame();
            var lastDestination = guideScroll.content.GetChild(guideScroll.content.childCount - 1).GetComponent<Button>();
            CheckResult("last-destination-is-visible-and-clickable", ClickButton(lastDestination) && map.MarkerName == lastDestination.GetComponentInChildren<Text>().text);
            guideScroll.verticalNormalizedPosition = 1; yield return Frame();
            float beforeTouch = guideScroll.content.anchoredPosition.y; var guideMarker = map.Marker;
            var guideDrag = new PointerEventData(EventSystem.current) { pointerId = 51, button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, guideScroll.viewport.TransformPoint(guideScroll.viewport.rect.center)) };
            ExecuteEvents.Execute(guideScroll.gameObject, guideDrag, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(guideScroll.gameObject, guideDrag, ExecuteEvents.beginDragHandler);
            guideDrag.position += Vector2.up * 140;
            ExecuteEvents.Execute(guideScroll.gameObject, guideDrag, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(guideScroll.gameObject, guideDrag, ExecuteEvents.endDragHandler);
            guideScroll.StopMovement(); yield return Frame();
            CheckResult("guide-touch-drag-scrolls-without-changing-marker", guideScroll.content.anchoredPosition.y > beforeTouch + 10 && map.Marker == guideMarker);

            var armory = map.Places.First(p => p.kind == "armory"); var armoryButton = Button("Visitar no mapa · " + armory.name);
            Reveal(armoryButton); yield return Frame();
            CheckResult("armory-guide-click-selects-real-shop", ClickButton(armoryButton) && map.MarkerName == "Oficina Farol" && Vector2.Distance(map.Marker.Value, armory.point) < .01f);
            CheckResult("armory-guide-identifies-weapons-and-ammunition", armoryButton.GetComponentsInChildren<Text>().Any(t => t.text.Contains("Gun shop") && t.text.Contains("ammo")));
            yield return Frame(); Capture("07-armaria-no-mapa");
            map.FitCity(); yield return Frame();
            var armoryLabel = map.GetComponentsInChildren<Text>(true).First(t => t.name == "Local · Oficina Farol");
            CheckResult("selected-armory-label-survives-overview-crowding", armoryLabel.gameObject.activeInHierarchy && armoryLabel.text.Contains("Gun shop"));
            Capture("08-guia-completo");
            map.ClearMarker(); map.FitCity();
            Capture("01-toda-a-baia");

            float clock = ctx.Clock; var cars = ctx.Vehicles.Fleet.ToArray(); var positions = cars.Select(c => c.Position).ToArray();
            ctx.Input.TouchMove = Vector2.right; ctx.Input.TouchFire = ctx.Input.TouchHack = ctx.Input.TouchJump = ctx.Input.TouchInteract = ctx.Input.TouchDrone = true;
            ctx.Input.TouchRun = ctx.Input.TouchAim = ctx.Input.TouchEmoteHeld = true;
            yield return Wait(.5f);
            CheckResult("map-consumes-gameplay-and-emotes", ctx.Input.Move == Vector2.zero && ctx.Input.Look == Vector2.zero && ctx.Input.Zoom == 0 &&
                !ctx.Input.Fire && !ctx.Input.FireHeld && !ctx.Input.HackPressed && !ctx.Input.Interact && !ctx.Input.Jump && !ctx.Input.Drone && !ctx.Input.AimHeld && !ctx.Emotes.WheelOpen);
            ctx.UI.ClearTouch();
            CheckResult("city-clock-and-traffic-continue", ctx.Clock > clock + .1f && cars.Where((c, i) => Vector3.Distance(c.Position, positions[i]) > .02f).Any());
            CheckResult("world-actions-not-selected-through-map", ctx.CurrentAction == null);

            map.Focus(map.WorldBounds.center, 3);
            var local = new Vector2(80, 20); var anchor = map.LocalToWorld(local); float beforeZoom = map.Zoom;
            var wheel = Pointer(local); wheel.scrollDelta = Vector2.up;
            ExecuteEvents.Execute(map.Viewport.gameObject, wheel, ExecuteEvents.scrollHandler);
            CheckResult("wheel-zooms-at-cursor", map.Zoom > beforeZoom && Vector2.Distance(anchor, map.LocalToWorld(local)) < .05f);
            Click(new Vector2(0, 80));
            CheckResult("click-creates-marker", map.Marker.HasValue);
            Vector2 marker = map.Marker.Value, beforePan = map.Center;
            var drag = Pointer(new Vector2(-100, 0)); var surface = map.Viewport.gameObject;
            ExecuteEvents.Execute(surface, drag, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(surface, drag, ExecuteEvents.beginDragHandler);
            drag.position = ScreenPoint(new Vector2(-20, 0)); ExecuteEvents.Execute(surface, drag, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(surface, drag, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(surface, drag, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(surface, drag, ExecuteEvents.pointerClickHandler);
            CheckResult("drag-pans-without-replacing-marker", Vector2.Distance(map.Center, beforePan) > 1 && map.Marker == marker);
            Click(new Vector2(30, 50), 41); CheckResult("touch-pointer-also-marks", map.Marker.HasValue && map.Marker != marker);
            var outside = Pointer(Vector2.zero, 42); ExecuteEvents.Execute(surface, outside, ExecuteEvents.pointerDownHandler);
            outside.position = ScreenPoint(new Vector2(-9999, 0)); ExecuteEvents.Execute(surface, outside, ExecuteEvents.pointerUpHandler);
            Click(new Vector2(50, 20), 43);
            CheckResult("outside-release-recovers-pointer", map.Marker.HasValue && Vector2.Distance(map.Marker.Value, map.LocalToWorld(new Vector2(50, 20))) < 40);

            Button("Visitar no mapa · Galpão da Maré").onClick.Invoke();
            CheckResult("guide-selects-real-place", map.MarkerName == "Galpão da Maré" && Vector2.Distance(map.Marker.Value, new Vector2(ctx.Data.fish.position.x, ctx.Data.fish.position.z)) < .01f);
            CheckResult("distance-uses-live-player-position", Mathf.Abs(map.MarkerDistance - Vector2.Distance(map.PlayerPoint, map.Marker.Value)) < .01f);
            Capture("02-local-marcado");
            Button("Mostrar paradas").onClick.Invoke(); CheckResult("bus-filter-hides-stops", !map.ShowStops);
            Button("Mostrar paradas").onClick.Invoke(); CheckResult("bus-filter-restores-stops", map.ShowStops);
            Button("Remover marcador").onClick.Invoke(); CheckResult("remove-clears-marker-and-disables-action", !map.Marker.HasValue && !Button("Remover marcador").interactable);
            Button("Minha posição").onClick.Invoke(); CheckResult("locate-centers-real-character", Vector2.Distance(map.Center, map.PlayerPoint) < .1f);
            Capture("03-minha-posicao");
            map.ZoomAt(1000, Vector2.zero); CheckResult("maximum-zoom-and-disabled-button", map.Zoom == 10 && !Button("Aproximar mapa").interactable);
            map.ZoomAt(.00001f, Vector2.zero); CheckResult("minimum-zoom-and-disabled-button", map.Zoom == 1 && !Button("Afastar mapa").interactable);
            map.Focus(map.WorldBounds.center, 4); map.Pan(new Vector2(1e7f, -1e7f)); CheckResult("pan-is-bounded", map.WorldBounds.Contains(map.Center));
            Button("Ver toda a baía").onClick.Invoke(); CheckResult("fit-restores-whole-chart", map.Zoom == 1 && map.Center == map.WorldBounds.center);

            ctx.Input.Pause = true; map.SampleInput(); CheckResult("escape-closes-map-without-pausing", !map.IsOpen && !ctx.Input.Pause && !ctx.Paused);
            yield return Frame();
            ctx.UI.SetSettings(true); CheckResult("settings-retain-pause", ctx.Paused && Time.timeScale == 0);
            map.SetOpen(true); CheckResult("map-cannot-cover-settings", !map.IsOpen);
            ctx.UI.SetSettings(false); yield return Frame();
            ctx.Input.TouchMap = true; yield return Frame(); ctx.Input.TouchMap = true; yield return Frame();
            CheckResult("M-toggles-closed-again", !map.IsOpen && !ctx.Paused);
            yield return Frame(); yield return Frame();
            ctx.Input.TouchMove = Vector2.right; ctx.Input.Poll(); CheckResult("gameplay-input-restored-after-closing", ctx.Input.Move.x > .9f && !ctx.UI.MapOpen);
            ctx.UI.ClearTouch(); ctx.Input.Poll();

            if (!Application.isEditor)
            {
                Screen.SetResolution(1920, 1080, false); yield return Wait(.4f); map.SetOpen(true); map.FitCity(); yield return Frame();
                CheckResult("layout-at-1080p", map.Viewport.rect.width > 800 && map.Viewport.rect.height > 400); Capture("04-1920x1080");
                Screen.SetResolution(1024, 768, false); yield return Wait(.4f);
                CheckResult("layout-at-4-by-3", map.Viewport.rect.width > 700 && map.Viewport.rect.height > 400); Capture("05-1024x768");
                Screen.SetResolution(800, 800, false); yield return Wait(.4f);
                CheckResult("compact-layout-hides-guide", !map.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Guia da cidade").gameObject.activeSelf);
                CheckResult("compact-layout-offers-places-button", Button("Abrir locais").gameObject.activeInHierarchy);
                Button("Abrir locais").onClick.Invoke(); yield return Frame();
                CheckResult("compact-places-button-opens-scrollable-guide", guideScroll.gameObject.activeInHierarchy && guideScroll.viewport.rect.height > 100);
                Reveal(armoryButton); yield return Frame();
                Capture("11-guia-tela-compacta");
                CheckResult("compact-guide-can-select-armory-and-return-to-chart", ClickButton(armoryButton) && map.MarkerName == "Oficina Farol" && !guideScroll.gameObject.activeInHierarchy);
                Capture("09-armaria-tela-compacta");
                map.Mark(map.PlayerPoint + Vector2.right * 20, "Ponto marcado");
                CheckResult("compact-marker-retains-distance-and-touch-removal", map.GetComponentsInChildren<Text>(true).First(t => t.name == "Legenda").text.Contains("20 m") && Button("Limpar marca no mapa compacto").gameObject.activeInHierarchy);
                Button("Limpar marca no mapa compacto").onClick.Invoke(); CheckResult("compact-touch-removes-marker", !map.Marker.HasValue);
                Capture("06-800x800");
                Screen.SetResolution(540, 960, false); yield return Wait(.4f);
                Button("Abrir locais").onClick.Invoke(); yield return Frame(); Reveal(lastDestination); yield return Frame();
                Capture("12-guia-vertical");
                CheckResult("portrait-guide-last-destination-stays-reachable", guideScroll.viewport.rect.height > 100 && ClickButton(lastDestination) &&
                    map.MarkerName == lastDestination.GetComponentInChildren<Text>().text && !guideScroll.gameObject.activeInHierarchy);
                Capture("10-mapa-vertical"); map.SetOpen(false);
            }
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            File.WriteAllText(Path.Combine(dir, "interactive-map-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("INTERACTIVE_MAP_QA_DONE passed=" + report.passed + " checks=" + report.checks.Count + " dir=" + dir);
            if (Array.IndexOf(args, "--city-interactive-map-qa") >= 0) Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (ctx) { ctx.UI.ClearTouch(); ctx.UI.SetSettings(false); if (map) map.SetOpen(false); } }
    }
}
