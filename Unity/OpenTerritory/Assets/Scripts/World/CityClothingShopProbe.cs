using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityClothingShopProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool passed = true; public string graphicsApi, createdAtUtc; public int routeSamples;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityClothingShop shop; string dir; Vector2 move; bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-clothing-qa") >= 0)
                new GameObject("Clothing store QA • opt-in").AddComponent<CityClothingShopProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + trace); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("CLOTHING_QA " + name + " " + passed + " " + detail); }
        Vector3 P(float x, float z) => shop.transform.TransformPoint(new Vector3(x, shop.Floor(x) + .04f, z));
        Vector3 Feet(Vector3 p) { p.y = ctx.Ground(p.x, p.z) + .04f; return p; }
        IEnumerator Walk(Vector3 target)
        {
            float deadline = Time.realtimeSinceStartup + CityPhysics.Flat(target - ctx.Player.position).magnitude / 3 + 3;
            while (CityPhysics.Flat(target - ctx.Player.position).magnitude > .35f && Time.realtimeSinceStartup < deadline)
            { var d = CityPhysics.Flat(target - ctx.Player.position).normalized; move = new Vector2(d.x, d.z); yield return null; }
            move = Vector2.zero; yield return null;
        }
        void AimAt(Transform point)
        {
            ctx.Camera.transform.position = point.position + shop.transform.forward * 4.7f + Vector3.up * 1.2f;
            ctx.Camera.transform.LookAt(point.position);
        }
        void Click(Button button)
        { ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler); }
        void Capture(string name, Vector3 eye, Vector3 target, float fov = 65)
        {
            var camera = ctx.Camera; camera.transform.position = shop.transform.TransformPoint(eye); camera.transform.LookAt(shop.transform.TransformPoint(target)); camera.fieldOfView = fov;
            var manager = FindFirstObjectByType<CityLodManager>(); if (manager) foreach (var item in manager.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var old = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 2 }; rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = old; rt.Release(); Destroy(rt); Destroy(image);
        }
        IEnumerator CaptureUI(string name)
        {
            yield return new WaitForSecondsRealtime(.15f);
            yield return new WaitForEndOfFrame();
            // Hidden batch players can return a black desktop backbuffer. Render
            // the same uGUI canvas through its camera for an inspectable capture.
            var canvas = ctx.Clothing.Panel.GetComponentInParent<Canvas>(); var mode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ctx.Camera; canvas.planeDistance = .3f;
            var old = RenderTexture.active; var rt = new RenderTexture(Screen.width, Screen.height, 24); rt.Create(); ctx.Camera.targetTexture = rt; Canvas.ForceUpdateCanvases(); ctx.Camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
            Test("shopping-panel-visible-in-native-render", image.GetPixel(rt.width - 100, rt.height / 2).maxColorComponent > .2f);
            ctx.Camera.targetTexture = null; RenderTexture.active = old; canvas.renderMode = mode; canvas.worldCamera = oldCamera; rt.Release(); Destroy(rt); Destroy(image);
            yield return null;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "clothing-qa"); Directory.CreateDirectory(dir);
            report.graphicsApi = SystemInfo.graphicsDeviceType.ToString(); report.createdAtUtc = DateTime.UtcNow.ToString("o");
            float deadline = Time.realtimeSinceStartup + 40;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; shop = FindFirstObjectByType<CityClothingShop>();
            Test("native-clothing-store-ready", ctx && ctx.Ready && shop && ctx.Wardrobe && ctx.Clothing);
            if (!ctx || !ctx.Ready || !shop) { Finish(); yield break; }
            yield return new WaitForSeconds(.5f);
            Test("QA-wallet-is-isolated", CityCommerce.IsReviewRun && ctx.Commerce.SavePath.Contains("BaiaDoSol-QA"));
            Test("only-marked-buildings-replaced", shop.replacedObjects.Length > 0 && shop.replacedObjects.All(g => g && !g.activeInHierarchy) &&
                ctx.Data.places[142].name == "Tideline" && ctx.Data.places[142].kind == "clothing" && ctx.Data.places[145].kind == "retired");
            Test("old-physical-solids-disabled-in-navigation", shop.removedSolidSlots.All(i => !ctx.Data.solids[i].active));
            Test("English-signage-and-real-entrances", shop.GetComponentsInChildren<Text>().Any(t => t.text == "T I D E L I N E") && shop.doorLeaves.Length == 4 && shop.browsePoints.Length == 3);
            // Compare the shared floor outside the replacement against its frozen
            // original geometry, without advancing the simulation between variants.
            var perimeter = new List<Vector3>();
            for (float x = -21; x <= 21; x += .6f)
            {
                perimeter.Add(shop.transform.TransformPoint(new Vector3(x, 0, -7.7f)));
                if (Mathf.Abs(x - 11) > 2 && Mathf.Abs(x + 11) > 2) perimeter.Add(shop.transform.TransformPoint(new Vector3(x, 0, 7.7f)));
            }
            for (float z = -7.7f; z <= 7.7f; z += .6f) foreach (float x in new[] { -20.8f, 20.8f }) perimeter.Add(shop.transform.TransformPoint(new Vector3(x, 0, z)));
            float HeightAt(Vector3 p) => Physics.Raycast(p + Vector3.up * 100, Vector3.down, out var hit, 200, 1 << CityContext.GroundLayer) ? hit.point.y : -10000;
            var afterHeights = perimeter.Select(HeightAt).ToArray();
            for (int i = 0; i < shop.groundColliders.Length; i++) { shop.groundColliders[i].sharedMesh = shop.originalCollisionMeshes[i]; shop.groundColliders[i].enabled = shop.originalCollisionEnabled[i]; }
            Physics.SyncTransforms(); int perimeterChanges = 0;
            for (int i = 0; i < perimeter.Count; i++) if (Mathf.Abs(afterHeights[i] - HeightAt(perimeter[i])) > .015f) perimeterChanges++;
            for (int i = 0; i < shop.groundColliders.Length; i++) { shop.groundColliders[i].sharedMesh = shop.cutCollisionMeshes[i]; shop.groundColliders[i].enabled = shop.originalCollisionEnabled[i] && shop.cutCollisionMeshes[i]; }
            Physics.SyncTransforms(); Test("shared-floor-outside-store-preserved", perimeterChanges == 0, "samples=" + perimeter.Count + " changes=" + perimeterChanges);
            var foreign = Physics.OverlapBox(shop.transform.TransformPoint(new Vector3(0, 4, 0)), new Vector3(20.5f, 4, 7.16f), shop.transform.rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(shop.transform)).ToArray();
            Test("no-overlap-with-neighbours-or-public-furniture", foreign.Length == 0, string.Join(",", foreign.Select(c => c.name)));

            ctx.Combat.Holster(); ctx.Follow.enabled = false; ctx.Clock = 0; ctx.Systems.Weather.Request("clear", true);
            ctx.Motor.Warp(Feet(shop.streetApproach)); ctx.Motor.SetInputLock(true); shop.SetDoor(0, 1); shop.SetDoor(1, 1); Physics.SyncTransforms();
            int unsupported = 0, blocked = 0; var obstacles = new HashSet<string>();
            for (int i = 1; i < shop.walkRoute.Length; i++)
            {
                var a = shop.walkRoute[i - 1]; var b = shop.walkRoute[i]; int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .30f);
                for (int j = 0; j <= count; j++) foreach (float lateral in new[] { -.25f, 0, .25f })
                {
                    var p = Vector3.Lerp(a, b, j / (float)Mathf.Max(1, count)) + shop.transform.right * lateral;
                    if (!Physics.Raycast(p + Vector3.up * 3, Vector3.down, out var hit, 7, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    p.y = hit.point.y + .05f; report.routeSamples++;
                    if (i >= 4 && i <= 6)
                    {
                        var local = shop.transform.InverseTransformPoint(hit.point);
                        if (Mathf.Abs(local.y - shop.Floor(local.x)) > .08f) { blocked++; obstacles.Add("hidden-floor-step segment=" + i); }
                    }
                    foreach (var c in Physics.OverlapCapsule(p + Vector3.up * .43f, p + Vector3.up * 1.87f, .37f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                    { blocked++; obstacles.Add(c.name + " segment=" + i); }
                }
            }
            Test("continuous-sidewalk-both-sales-floors-ramp-and-fitting-room", unsupported == 0 && blocked == 0, "samples=" + report.routeSamples + " unsupported=" + unsupported + " blocked=" + blocked + " " + string.Join(",", obstacles));
            Capture("01-overview", new Vector3(39, 34, 46), new Vector3(0, 2.3f, 0), 57);
            Capture("02-street", new Vector3(5, 6.4f, 30), new Vector3(0, 4.1f, 0), 65);
            Capture("03-interior", new Vector3(12, 3.2f, 4.6f), new Vector3(-4.8f, 3.4f, -3.7f), 76);
            shop.SetDoor(0, 0); shop.SetDoor(1, 0); ctx.Camera.transform.rotation = Quaternion.identity; ctx.Motor.SetInputLock(false); ctx.Input.TouchRun = true;
            bool walked = true;
            for (int i = 0; i < shop.walkRoute.Length; i++)
            {
                yield return Walk(shop.walkRoute[i]); float gap = CityPhysics.Flat(shop.walkRoute[i] - ctx.Player.position).magnitude;
                Test("real-controller-route-" + i, gap < .9f, "gap=" + gap.ToString("F3") + " position=" + shop.transform.InverseTransformPoint(ctx.Player.position)); if (gap >= .9f) { walked = false; break; }
            }
            move = Vector2.zero; ctx.Input.TouchRun = false; yield return new WaitForSeconds(.25f);
            Test("enter-explore-fitting-room-and-exit", walked && ctx.Motor.Grounded);
            ctx.Motor.Warp(Feet(P(11, 7.8f))); yield return new WaitForSeconds(.7f);
            Test("automatic-entrance-opens-for-protagonist", shop.DoorAmounts[0] > .98f);
            ctx.UI.SetSettings(true); float clock = ctx.Clock, doorAmount = shop.DoorAmounts[0]; yield return new WaitForSecondsRealtime(.3f);
            Test("Esc-pauses-world-and-entrance", ctx.Paused && Mathf.Abs(ctx.Clock - clock) < .0001f && Mathf.Abs(shop.DoorAmounts[0] - doorAmount) < .0001f);
            ctx.UI.SetSettings(false); ctx.Motor.Warp(Feet(P(11, 11.5f))); yield return new WaitForSeconds(2);
            Test("entrance-closes-after-safe-clearance", shop.DoorAmounts[0] < .01f);
            ctx.Motor.Warp(Feet(P(17.7f, 8.9f))); ctx.Camera.transform.rotation = Quaternion.identity; yield return new WaitForSeconds(.2f);
            var throughGlass = -shop.transform.forward; move = new Vector2(throughGlass.x, throughGlass.z); yield return new WaitForSeconds(.65f); move = Vector2.zero; yield return new WaitForSeconds(.2f);
            Test("display-glazing-blocks-walking", shop.transform.InverseTransformPoint(ctx.Player.position).z > 7.15f);

            ctx.Motor.Warp(Feet(P(11.5f, .6f))); yield return new WaitForSeconds(.3f); AimAt(shop.browsePoints[0]); ctx.RefreshAction();
            Test("rack-selected-by-real-world-visibility-and-range", ctx.CurrentAction != null && ctx.CurrentAction.id == "clothing-0", ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id);
            ctx.Input.TouchInteract = true; yield return null; yield return null;
            Test("E-opens-compact-contextual-uGUI", ctx.Clothing.IsOpen && !ctx.Paused && ctx.Clothing.Panel.rect.width <= 352 && ctx.UI.PointerBlocked);
            if (!ctx.Clothing.IsOpen) { ctx.Clothing.Open(shop, 0); }
            int initial = ctx.Commerce.Balance; ctx.Clothing.Select(1); yield return null;
            var shirt = ctx.Node(3986).GetComponent<Renderer>(); var sleeve = ctx.Node(4017); var skin = ctx.Node(4004).GetComponent<Renderer>();
            Color skinBefore = skin.sharedMaterial.color; var property = new MaterialPropertyBlock(); shirt.GetPropertyBlock(property);
            Test("preview-changes-protagonist-without-charging", ctx.Wardrobe.PreviewId == "coast-tee" && !ctx.Wardrobe.Owns("coast-tee") && ctx.Commerce.Balance == initial &&
                Vector4.Distance(property.GetColor("_Color"), CityWardrobe.Find("coast-tee").color) < .001f && sleeve.localScale.y < .24f);
            Capture("04-fitting-preview", shop.transform.InverseTransformPoint(ctx.Player.position) + new Vector3(1.3f, 1.8f, -3.2f), shop.transform.InverseTransformPoint(ctx.Player.position) + Vector3.up * 1.1f, 50);
            yield return CaptureUI("05-shopping-panel");
            ctx.Clothing.Close(); yield return null; shirt.GetPropertyBlock(property);
            Test("cancel-restores-owned-outfit", ctx.Wardrobe.PreviewId == null && ctx.Wardrobe.Equipped(0) == "field-jacket" && ctx.Commerce.Balance == initial && sleeve.localScale.y > .4f);
            ctx.Clothing.Open(shop, 0); ctx.Clothing.Select(1); Click(ctx.Clothing.BuyButton); yield return null;
            Test("mouse-touch-button-buys-and-wears", ctx.Wardrobe.Owns("coast-tee") && ctx.Wardrobe.Equipped(0) == "coast-tee" && ctx.Commerce.Balance == initial - 65);
            Click(ctx.Clothing.BuyButton); yield return null;
            Test("owned-item-cannot-charge-twice", ctx.Commerce.Balance == initial - 65 && !ctx.Clothing.BuyButton.interactable);
            var trousersTab = ctx.Clothing.Panel.GetComponentsInChildren<Button>().First(b => b.name == "Trousers"); Click(trousersTab); ctx.Clothing.Select(1); Click(ctx.Clothing.BuyButton); yield return null;
            var sneakersTab = ctx.Clothing.Panel.GetComponentsInChildren<Button>().First(b => b.name == "Sneakers"); Click(sneakersTab); ctx.Clothing.Select(1); Click(ctx.Clothing.BuyButton); yield return null;
            Test("tops-trousers-and-sneakers-equip-independently", ctx.Wardrobe.Equipped(0) == "coast-tee" && ctx.Wardrobe.Equipped(1) == "indigo-denim" && ctx.Wardrobe.Equipped(2) == "cloud-sneakers" && ctx.Commerce.Balance == initial - 225);
            ctx.Clothing.SetCategory(0); ctx.Clothing.Select(3); int before = ctx.Commerce.Balance; bool tooCostly = ctx.Clothing.Purchase();
            Test("insufficient-funds-does-not-buy-or-charge", !tooCostly && ctx.Commerce.Balance == before && !ctx.Wardrobe.Owns("navy-overshirt") && !ctx.Clothing.BuyButton.interactable);
            ctx.Clothing.Select(0); Click(ctx.Clothing.BuyButton); yield return null;
            Test("original-clothes-remain-free-and-restore-sleeves", ctx.Wardrobe.Equipped(0) == "field-jacket" && ctx.Commerce.Balance == before && sleeve.localScale.y > .4f);
            ctx.Clothing.Select(1); Click(ctx.Clothing.BuyButton); yield return null;
            Test("owned-clothes-reusable-for-free", ctx.Wardrobe.Equipped(0) == "coast-tee" && ctx.Commerce.Balance == before);
            ctx.Clothing.Close(); ctx.Commerce.Checkpoint(); string savedWardrobe = ctx.Wardrobe.ExportJson(), walletFile = File.ReadAllText(ctx.Commerce.SavePath);
            ctx.Wardrobe.RestoreJson(null); ctx.Commerce.Fine(1);
            File.WriteAllText(ctx.Commerce.SavePath, walletFile); typeof(CityCommerce).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctx.Commerce, null);
            Test("wallet-and-wardrobe-restore-from-same-persisted-checkpoint", ctx.Wardrobe.ExportJson() == savedWardrobe && ctx.Commerce.Balance == before && walletFile.Contains("wardrobe"));
            Test("shared-NPC-materials-skin-and-face-preserved", shirt.sharedMaterial.color != CityWardrobe.Find("coast-tee").color && skin.sharedMaterial.color == skinBefore && skin.GetPropertyBlockColorForClothingProbe() == Color.clear);
            ctx.Wardrobe.RestoreJson("{\"version\":1,\"owned\":[\"invalid\"],\"equipped\":[\"invalid\",\"invalid\",\"invalid\"]}");
            Test("old-or-invalid-wardrobe-retains-default-clothes", ctx.Wardrobe.Equipped(0) == "field-jacket" && !ctx.Wardrobe.Owns("invalid")); ctx.Wardrobe.RestoreJson(savedWardrobe);

            ctx.Clothing.Open(shop, 0); clock = ctx.Clock; yield return new WaitForSeconds(.25f);
            Test("shopping-does-not-pause-city", ctx.Clothing.IsOpen && ctx.Clock > clock && !ctx.Paused);
            ctx.Input.Pause = true; ctx.Clothing.SampleInput(); Test("Esc-closes-shopping-and-restores-controls", !ctx.Clothing.IsOpen && !ctx.Paused && !ctx.Input.Pause);
            ctx.Clothing.Open(shop, 0); ctx.Input.TouchMove = Vector2.up; ctx.Clothing.SampleInput(); ctx.Input.TouchMove = Vector2.zero;
            Test("walking-closes-preview-without-blocking-controls", !ctx.Clothing.IsOpen && ctx.Wardrobe.PreviewId == null);
            ctx.Clothing.Open(shop, 0); ctx.Motor.Warp(Feet(P(11, 11.6f))); ctx.Clothing.SampleInput();
            Test("leaving-store-closes-preview", !ctx.Clothing.IsOpen);
            yield return new WaitForSeconds(.25f); float ground = ctx.Player.position.y; ctx.Input.TouchJump = true; yield return new WaitForSeconds(.35f); bool jumped = ctx.Player.position.y > ground + .2f; yield return new WaitForSeconds(1.5f);
            Test("jump-and-grounding-still-work", jumped && ctx.Motor.Grounded);
            ctx.UI.Map.SetOpen(true); yield return new WaitForSeconds(.2f);
            Test("Tideline-is-a-single-Places-destination", ctx.UI.Map.Places.Count(p => p.kind == "clothing" && p.name == "Tideline") == 1); ctx.UI.Map.SetOpen(false);
            ctx.Clock = 168; ctx.Motor.SetInputLock(true); yield return new WaitForSeconds(.8f);
            Test("store-lighting-follows-night-cycle", ctx.Night > .9f && shop.lights.Length == 10 && shop.lights.All(l => l && l.enabled));
            Capture("06-night-street", new Vector3(-20, 9, 30), new Vector3(0, 4, 0), 65);
            Test("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            ctx.Motor.SetInputLock(false); ctx.Follow.enabled = true; Finish();
        }
        void Finish()
        {
            finished = true; File.WriteAllText(Path.Combine(dir, "clothing-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
    static class ClothingProbeRenderer
    {
        public static Color GetPropertyBlockColorForClothingProbe(this Renderer renderer)
        { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetColor("_Color"); }
    }
}
