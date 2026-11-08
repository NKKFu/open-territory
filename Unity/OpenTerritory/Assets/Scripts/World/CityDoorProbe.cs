using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in integration run against the actual doors and player controller.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityDoorProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report(); CityContext ctx; Vector2 move; bool running, finished; string directory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-door-qa") >= 0) new GameObject("Door QA opt-in").AddComponent<CityDoorProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update() { if (ctx && !finished) { ctx.Input.TouchMove = move; ctx.Input.TouchRun = running; } }
        void Test(string name, bool passed, float value = 0, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("DOOR_QA " + name + " " + passed + " " + value + " " + detail); }
        Vector3 At(DoorData door, float distance, float lateral = 0)
        {
            var p = door.Approach(0, distance) + Quaternion.Euler(0, door.yaw * Mathf.Rad2Deg, 0) * Vector3.right * lateral;
            p.y = ctx.Ground(p.x, p.z) + .045f; return p;
        }
        Vector3 Normal(DoorData door) => Quaternion.Euler(0, door.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
        float Amount(DoorData door) => door.automatic ? Vector3.Distance(ctx.Node(door.id).position, door.position) : Quaternion.Angle(Quaternion.Euler(0, door.yaw * Mathf.Rad2Deg, 0), ctx.Node(door.id).rotation);
        IEnumerator Wait(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
        void Warp(DoorData door, float distance, float lateral = 0)
        {
            move = Vector2.zero; running = false; ctx.Motor.Warp(At(door, distance, lateral));
            var direction = -Mathf.Sign(distance) * Normal(door);
            ctx.Camera.transform.SetPositionAndRotation(ctx.Player.position + Vector3.up * 3 - direction * 6, Quaternion.LookRotation(direction));
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/doors/native")); Directory.CreateDirectory(directory);
            float timeout = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            // Isolate contacts from unrelated traffic; these changes exist only
            // in this explicitly requested QA process.
            ctx.Population.enabled = ctx.Residents.enabled = ctx.Vehicles.enabled = false;
            foreach (var resident in ctx.Data.residents) { var t = ctx.Node(resident.id); if (t) t.gameObject.SetActive(false); }
            ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true);
            yield return Wait(.25f);
            int manual = 0, badWidths = 0, badLeaf = 0, blockedPanels = 0, panels = 0;
            foreach (var place in ctx.Data.places)
            {
                var d = place.door; var root = ctx.Node(d.id);
                if (d.automatic) continue; manual++;
                float expected = place.kind == "home" || place.kind == "residence" ? 1.4f : 1.6f;
                if (Mathf.Abs(d.width - expected) > .001f) badWidths++;
                var box = root.GetComponentInChildren<BoxCollider>();
                if (!box || Mathf.Abs(box.size.x - d.width) > .001f || Vector3.Distance(root.position, d.position) > .005f) badLeaf++;
            }
            foreach (var solid in ctx.Data.solids)
            {
                var t = ctx.Node(solid.owner); if (!t || !t.name.StartsWith("Doorway sidelight-")) continue; panels++;
                var center = new Vector3(solid.x, solid.bottom + 1.6f, solid.z);
                var normal = Quaternion.Euler(0, solid.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                if (!Physics.Raycast(center + normal, -normal, out var hit, 2, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore) || !CityPhysics.Belongs(hit.collider, t)) blockedPanels++;
            }
            Test("all-manual-leaves-have-human-scale-width", manual == 215 && badWidths == 0, badWidths, "manual=" + manual);
            Test("all-leaf-colliders-match-visual-width", badLeaf == 0, badLeaf);
            Test("all-fixed-sidelights-have-solid-contact", panels == 430 && blockedPanels == 0, blockedPanels, "panels=" + panels);
            var homes = FindFirstObjectByType<CityHomeCatalog>();
            var apartments = homes.homes.Where(h => h.units > 1).Select(h => ctx.Data.places[h.place]).ToArray();
            Test("all-apartment-entrances-present", apartments.Length == 12, apartments.Length);
            foreach (var place in apartments)
            {
                var d = place.door; var normal = Normal(d);
                // A ray just behind a fixed pane must reach the lobby, rather
                // than the obsolete narrow facade hidden behind its glass.
                int blocked = 0, fixedPanes = 0;
                foreach (var solid in ctx.Data.solids)
                {
                    var panel = ctx.Node(solid.owner);
                    if (!panel || !panel.name.StartsWith("Doorway sidelight-" + d.id + "-")) continue;
                    fixedPanes++;
                    var behind = new Vector3(solid.x, d.position.y + 2.1f, solid.z) - normal * .13f;
                    if (!solid.active || Physics.Raycast(behind, -normal, .45f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) blocked++;
                }
                Test("apartment-sidelights-open-onto-lobby-" + d.id, fixedPanes == 2 && blocked == 0, blocked);
                var above = new Vector3(d.x, d.position.y + 3.36f, d.z);
                Test("apartment-header-closes-top-gap-" + d.id,
                    Physics.Raycast(above + normal * .8f, -normal, 1.5f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore));
                var home = homes.homes.First(h => ctx.Data.places[h.place] == place);
                var threshold = home.root.Find("Entrada • moldura alinhada/Threshold");
                var shape = threshold ? threshold.GetComponent<BoxCollider>() : null;
                var surface = threshold ? threshold.GetComponent<Renderer>() : null;
                float floorError = shape && surface ? Mathf.Max(Mathf.Abs(shape.bounds.max.y - (place.elevation + .18f)),
                    Mathf.Abs(surface.bounds.max.y - shape.bounds.max.y)) : float.PositiveInfinity;
                Test("apartment-threshold-meets-floor-" + d.id, floorError < .025f, floorError);
                // The imported apron bevel is 7.1 cm above the interior slab.
                // Measure it separately: Ground() returns that upper surface,
                // not the threshold's own visual and collision bounds.
                float entryStep = shape ? Mathf.Abs(ctx.Ground(d.x, d.z) - shape.bounds.max.y) : float.PositiveInfinity;
                Test("apartment-entry-step-under-ten-centimetres-" + d.id, entryStep < .10f, entryStep);
            }
            if (Array.IndexOf(args, "--city-apartment-doors-only") >= 0)
            {
                for (int i = 0; i < apartments.Length; i++)
                {
                    var place = apartments[i];
                    if (i == 0) { Warp(place.door, 5); yield return Wait(4.3f); Capture("04-apartment-closed", place.door); }
                    yield return Walk(place, 1, false, -.12f);
                    if (i == 0) Capture("05-apartment-open", place.door);
                    yield return Walk(place, -1, false, .12f);
                    if (i % 4 == 0) { Warp(place.door, 5); yield return Wait(4.3f); yield return Walk(place, 1, true, 0); }
                }
                if (apartments.Length > 0) yield return Safety(apartments[0].door);
                Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish(); yield break;
            }
            var choices = new List<PlaceData>();
            foreach (string kind in new[] { "home", "residence", "bakery", "gallery", "hospital", "station" })
            {
                var place = ctx.Data.places.FirstOrDefault(p => p.kind == kind && ctx.Free(At(p.door, 2.5f)) && ctx.Free(At(p.door, -1.8f)));
                Test("clear-test-approaches-" + kind, place != null);
                if (place != null) choices.Add(place);
            }
            int serial = 0;
            foreach (var place in choices)
            {
                var d = place.door;
                Warp(d, 5); yield return Wait(d.automatic ? 2.2f : 4.3f);
                Test("closes-when-clear-" + place.kind, Amount(d) < .5f, Amount(d));
                if (serial == 0) Capture("01-casa-porta-fechada", d);
                yield return Walk(place, 1, false, -.12f);
                if (serial == 0) Capture("02-casa-porta-aberta", d);
                yield return Walk(place, -1, false, .12f);
                if (!d.automatic) { Warp(d, 5); yield return Wait(4.3f); yield return Walk(place, 1, true, 0); }
                else Capture("03-automatica-" + place.kind, d);
                serial++;
            }
            var manualPlace = choices.FirstOrDefault(p => !p.door.automatic);
            if (manualPlace != null) yield return Safety(manualPlace.door);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count);
            Finish();
        }
        IEnumerator Walk(PlaceData place, int side, bool sprint, float lateral)
        {
            var d = place.door; float start = side > 0 ? (sprint ? 3.7f : 2.5f) : 1.8f;
            Warp(d, side * start, lateral); yield return Wait(.2f);
            float began = ctx.Clock, until = began + (start + 1.55f) / CityPlayer.WalkSpeed + 1.3f;
            float opened = Amount(d), stalled = 0; Vector3 previous = ctx.Player.position;
            running = sprint; move = Vector2.up;
            while (ctx.Clock < until)
            {
                float normal = Vector3.Dot(ctx.Player.position - new Vector3(d.x, ctx.Player.position.y, d.z), Normal(d));
                if (normal * side < -1.55f) break;
                opened = Mathf.Max(opened, Amount(d));
                if (Mathf.Abs(normal) < 1.1f && CityPhysics.Flat(ctx.Player.position - previous).magnitude < .2f * Time.deltaTime) stalled += Time.deltaTime;
                previous = ctx.Player.position; yield return null;
            }
            move = Vector2.zero; running = false; yield return Wait(.12f);
            float signed = Vector3.Dot(ctx.Player.position - new Vector3(d.x, ctx.Player.position.y, d.z), Normal(d));
            string label = place.kind + (side > 0 ? "-in" : "-out") + (sprint ? "-run" : "-walk") + "-" + d.id;
            Test("crosses-" + label, signed * side < -1.4f && ctx.Motor.Grounded, signed, "yaw=" + d.yaw + " elapsed=" + (ctx.Clock - began));
            Test("opens-" + label, opened > (d.automatic ? 2.5f : 85), opened);
            Test("no-sticking-" + label, stalled < .2f, stalled);
        }
        IEnumerator Safety(DoorData d)
        {
            Warp(d, -2.5f); yield return Wait(4.3f);
            Test("can-close-from-inside", Amount(d) < .5f, Amount(d));
            Warp(d, -.85f); yield return Wait(.55f);
            float angle = Mathf.DeltaAngle(d.yaw * Mathf.Rad2Deg, ctx.Node(d.id).eulerAngles.y);
            Test("cold-push-from-inside-opens-away", angle > 85, angle);
            Warp(d, 0); yield return Wait(2.5f);
            Test("holds-open-with-player-in-threshold", Amount(d) > 85, Amount(d));
            var frozen = ctx.Node(d.id).rotation; ctx.Pause(true); yield return Wait(.25f);
            Test("settings-pause-freezes-door", Quaternion.Angle(frozen, ctx.Node(d.id).rotation) < .01f);
            ctx.Pause(false); Warp(d, 5); yield return Wait(4.3f);
            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule); npc.name = "QA doorway pedestrian"; npc.layer = CityContext.ActorLayer;
            npc.transform.position = At(d, .85f) + Vector3.up * 1.1f; npc.transform.localScale = new Vector3(.72f, 1.1f, .72f);
            yield return Wait(.55f);
            angle = Mathf.DeltaAngle(d.yaw * Mathf.Rad2Deg, ctx.Node(d.id).eulerAngles.y);
            Test("npc-opens-away-from-outside", angle < -85, angle);
            npc.transform.position = At(d, 0) + Vector3.up * 1.1f; yield return Wait(2.5f);
            Test("holds-open-with-npc-in-threshold", Amount(d) > 85, Amount(d));
            Destroy(npc); yield return Wait(4.3f);
            Test("closes-after-pedestrian-leaves", Amount(d) < .5f, Amount(d));
            bool activated = ctx.Systems.ActivateDevice(new DeviceData { type = "door", node = d.id, target = d.id, position = d.position });
            yield return Wait(.55f); Test("remote-activation-opens-without-pusher", activated && Amount(d) > 85, Amount(d));
        }
        void Capture(string name, DoorData d)
        {
            var camera = ctx.Camera; var position = camera.transform.position; var rotation = camera.transform.rotation;
            var at = new Vector3(d.x, d.position.y + 1.7f, d.z); camera.transform.position = at + Normal(d) * 6 + Vector3.up * 1.1f; camera.transform.LookAt(at);
            var previous = RenderTexture.active; var output = new RenderTexture(1200, 900, 24); output.Create(); camera.targetTexture = output; camera.Render(); RenderTexture.active = output;
            var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; output.Release(); Destroy(output); Destroy(texture); camera.transform.SetPositionAndRotation(position, rotation);
        }
        void Finish()
        {
            finished = true; if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchRun = false; }
            File.WriteAllText(Path.Combine(directory, "door-qa.json"), JsonUtility.ToJson(report, true)); Debug.Log("DOOR_QA_FINISHED " + report.passed + " checks=" + report.checks.Count);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
