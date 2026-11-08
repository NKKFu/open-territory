using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Opt-in only: --city-interaction-qa, or explicitly attach during Play Mode.
    public sealed class CityInteractionProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        CityContext ctx; List<CityAction> savedActions; GameObject floor, wall, prop;
        Vector3 savedPosition, savedEye; Quaternion savedRotation, savedView; bool savedFollow;
        string dir; int objectUses, talkUses;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-interaction-qa") >= 0) new GameObject("Interaction QA opt-in").AddComponent<CityInteractionProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "") { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("INTERACTION_QA " + name + " " + passed + " " + detail); }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator Press(bool conversation)
        {
            if (conversation) { ctx.Input.TouchEmoteHeld = true; yield return Frame(); ctx.Input.TouchEmoteHeld = false; }
            else ctx.Input.TouchInteract = true;
            yield return Frame();
        }
        void Look(Vector3 point) { ctx.Camera.transform.LookAt(point); }
        Text KeyText() { return ctx.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Tecla" && t.transform.parent.name == "Tecla de interação"); }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            // Batch players have no capturable desktop backbuffer. Render the same
            // camera and its world labels into a temporary target, then restore them.
            var camera = ctx.Camera; var target = new RenderTexture(1280, 800, 24); target.Create();
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var canvases = ctx.GetComponentsInChildren<Canvas>();
            var modes = canvases.Select(c => c.renderMode).ToArray(); var cameras = canvases.Select(c => c.worldCamera).ToArray(); var distances = canvases.Select(c => c.planeDistance).ToArray();
            Texture2D image = null;
            try
            {
                camera.targetTexture = target;
                foreach (var canvas in canvases) if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .3f; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image = new Texture2D(1280, 800, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
                Canvas.ForceUpdateCanvases(); target.Release(); Destroy(target); if (image) Destroy(image);
            }
        }
        IEnumerator Start()
        {
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/interactions")); Directory.CreateDirectory(dir);
            savedPosition = ctx.Player.position; savedRotation = ctx.Player.rotation; savedEye = ctx.Camera.transform.position; savedView = ctx.Camera.transform.rotation; savedFollow = ctx.Follow.enabled;
            savedActions = ctx.Actions; ctx.Actions = new List<CityAction>(); ctx.Follow.enabled = false;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA isolated interaction floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(30, 2, 30);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0));
            ctx.Camera.transform.position = new Vector3(0, 202.4f, -3.4f);
            Vector3 person = new Vector3(-.9f, 201.4f, 1.7f), item = new Vector3(.9f, 201.2f, 1.2f);
            ctx.AddAction("qa-person", () => person, () => "Conversar", () => talkUses++, range: 3, conversation: true);
            ctx.AddAction("qa-object", () => item, () => "Usar objeto", () => objectUses++, range: 3, priority: 20);
            CheckResult("Q-talk-E-object-bindings", CityInput.TalkKey == KeyCode.Q && CityInput.InteractKey == KeyCode.E);
            Look(person); yield return Frame();
            CheckResult("gaze-selects-person-over-nearer-priority-object", ctx.CurrentAction?.id == "qa-person", ctx.CurrentAction?.id);
            CheckResult("person-world-label-shows-Q", KeyText() && KeyText().text == "Q");
            yield return Press(false); CheckResult("E-does-not-talk", talkUses == 0 && objectUses == 0);
            yield return Press(true); CheckResult("Q-talks-once", talkUses == 1 && objectUses == 0);
            yield return Frame(); CheckResult("Q-does-not-repeat", talkUses == 1);
            ctx.QueueCurrentAction(); yield return Frame(); CheckResult("person-prompt-click-routes-Q", talkUses == 2);
            var position = ctx.Player.position;
            Look(item); yield return Frame();
            CheckResult("camera-turn-switches-target-without-moving", ctx.CurrentAction?.id == "qa-object" && Vector3.Distance(position, ctx.Player.position) < .05f, ctx.CurrentAction?.id);
            CheckResult("object-world-label-shows-E", KeyText() && KeyText().text == "E");
            yield return Press(true); CheckResult("Q-does-not-use-object", objectUses == 0 && talkUses == 2);
            yield return Press(false); CheckResult("E-uses-object-once", objectUses == 1 && talkUses == 2);
            ctx.QueueCurrentAction(); yield return Frame(); CheckResult("object-prompt-click-routes-E", objectUses == 2);
            Look(ctx.Camera.transform.position + Vector3.back * 10); yield return Frame();
            CheckResult("looking-away-clears-target", ctx.CurrentAction == null);
            yield return Press(false); yield return Press(true); CheckResult("looking-away-executes-nothing", objectUses == 2 && talkUses == 2);
            Look(item); ctx.Motor.Warp(new Vector3(0, 200.04f, -8)); yield return Frame();
            CheckResult("camera-cannot-extend-character-reach", ctx.CurrentAction == null);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0));
            prop = GameObject.CreatePrimitive(PrimitiveType.Cube); prop.layer = CityContext.WorldLayer; prop.transform.position = item; prop.transform.localScale = Vector3.one * .6f;
            var objectAction = ctx.Actions.Find(a => a.id == "qa-object"); objectAction.Target = () => prop.transform;
            yield return Frame(); CheckResult("object-does-not-occlude-its-own-action", ctx.CurrentAction == objectAction);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer; wall.transform.position = new Vector3(0, 202, -.7f); wall.transform.localScale = new Vector3(6, 4, .3f);
            yield return Frame(); CheckResult("camera-wall-blocks-even-high-priority-object", ctx.CurrentAction == null);
            wall.layer = CityContext.ActorLayer; yield return Frame(); CheckResult("foreground-person-blocks-object", ctx.CurrentAction == null);
            wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(.45f, 201.2f, .6f); wall.transform.localScale = new Vector3(.5f, 2, .12f);
            ctx.Camera.transform.position = new Vector3(3, 202.4f, -1); Look(item); yield return Frame();
            CheckResult("character-cannot-reach-through-wall", ctx.CurrentAction != objectAction);
            Destroy(wall); wall = null; yield return Frame();
            objectAction.Enabled = () => false; yield return Frame(); CheckResult("disabled-object-is-not-selected", ctx.CurrentAction != objectAction);
            objectAction.Enabled = null;
            Look(person); yield return Frame(); var clock = ctx.Clock; ctx.UI.SetSettings(true);
            yield return Press(true); yield return Press(false);
            CheckResult("Esc-pauses-input-and-world", talkUses == 2 && objectUses == 2 && ctx.Clock == clock && Time.timeScale == 0);
            ctx.UI.SetSettings(false); yield return Frame(); CheckResult("resume-does-not-replay-input", talkUses == 2 && objectUses == 2 && Time.timeScale == 1);
            bool state = true;
            ctx.AddAction("qa-exit", () => ctx.Player.position + Vector3.up, () => "Sair", () => state = false, () => state, priority: 80, requiresLook: false);
            Look(ctx.Camera.transform.position + Vector3.back * 10); yield return Frame(); CheckResult("state-exit-survives-camera-orbit", ctx.CurrentAction?.id == "qa-exit");
            yield return Press(true); CheckResult("Q-does-not-trigger-E-state-action", state);
            yield return Press(false); CheckResult("E-triggers-state-action", !state);
            savedActions.AddRange(ctx.Actions.Where(a => !a.id.StartsWith("qa-") && !savedActions.Any(s => s.id == a.id)));
            ctx.Actions = savedActions; savedActions = null; Destroy(prop); prop = null; Destroy(floor); floor = null;
            ctx.Motor.SetInputLock(false); ctx.Motor.Warp(savedPosition); ctx.Camera.transform.position = savedEye; ctx.Camera.transform.rotation = savedView;
            yield return new WaitForSeconds(.7f);
            yield return RealPerson(); yield return RealBench(); yield return RealCar();
            CheckResult("all-resident-and-driver-dialogues-use-Q", ctx.Actions.Where(a => a.id.StartsWith("resident-") || a.id.StartsWith("driver-incident-") || a.id == "fish-0" || a.id.StartsWith("music-")).All(a => a.conversation));
            CheckResult("vehicles-seats-and-fish-objects-use-E", ctx.Actions.Where(a => a.id.StartsWith("vehicle-") || a.id.StartsWith("seat-") || a.id == "fish-1" || a.id == "fish-2" || a.id == "fish-3").All(a => !a.conversation));
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            File.WriteAllText(Path.Combine(dir, "interaction-qa.json"), JsonUtility.ToJson(report, true)); Debug.Log("INTERACTION_QA_DONE " + report.passed);
            bool quit = !Application.isEditor && Array.IndexOf(args, "--city-interaction-qa") >= 0;
            Destroy(this); if (quit) Application.Quit(report.passed ? 0 : 1);
        }
        IEnumerator RealPerson()
        {
            var candidates = ctx.Residents.People.Where(p => ctx.Residents.AvailableForTalk(p)).OrderBy(p => Vector3.Distance(p.Root.position, savedPosition)).ToArray();
            CityResidents.Resident person = null;
            foreach (var p in candidates)
            {
                foreach (var direction in new[] { p.Root.forward, -p.Root.forward, p.Root.right, -p.Root.right })
                {
                    var feet = p.Root.position + direction * 1.8f; feet.y = ctx.Ground(feet.x, feet.z) + .05f;
                    if (!ctx.Free(feet)) continue;
                    ctx.Motor.Warp(feet); ctx.Camera.transform.position = feet + Vector3.up * 2.4f + direction * 2.6f; Look(p.Root.position + Vector3.up * 1.4f);
                    ctx.RefreshAction(); if (ctx.CurrentAction?.id != "resident-" + p.Data.id) continue; person = p; break;
                }
                if (person != null) break;
            }
            CheckResult("real-resident-selected-by-gaze", person != null, ctx.CurrentAction?.id);
            if (person == null) yield break;
            yield return Frame(); ctx.Speech = ""; yield return Press(false); CheckResult("real-resident-ignores-E", ctx.Speech == "");
            yield return Press(true); CheckResult("real-resident-Q-conversation-without-pause", ctx.Speech.Contains(person.Data.name) && !ctx.Paused && person.TalkUntil > ctx.Clock, ctx.Speech);
            Capture("01-conversa-Q"); yield return Frame();
        }
        IEnumerator RealBench()
        {
            CityAction seat = null;
            foreach (var a in ctx.Actions.Where(a => a.id.StartsWith("seat-")).OrderBy(a => Vector3.Distance(a.Position, savedPosition)))
            {
                var feet = a.Position; feet.y = ctx.Ground(feet.x, feet.z) + .05f;
                if (!ctx.Free(feet)) continue; ctx.Motor.Warp(feet);
                var outward = CityPhysics.Flat(a.Position - a.LookPoint).normalized;
                ctx.Camera.transform.position = feet + Vector3.up * 2.4f + outward * 2.6f; Look(a.LookPoint);
                yield return new WaitForSeconds(.15f); ctx.RefreshAction(); if (ctx.CurrentAction != a) continue; seat = a; break;
            }
            CheckResult("real-bench-selected-by-gaze", seat != null, ctx.CurrentAction?.id); if (seat == null) yield break;
            Capture("02-objeto-E"); yield return Frame();
            yield return Press(true); CheckResult("real-bench-ignores-Q", !ctx.Motor.SeatHeight.HasValue);
            yield return Press(false); yield return new WaitForSeconds(.8f); CheckResult("real-bench-E-sits", ctx.Motor.SeatHeight.HasValue);
            Look(ctx.Camera.transform.position + Vector3.back * 10); yield return Frame(); CheckResult("real-stand-offered-looking-away", ctx.CurrentAction?.id == "stand");
            yield return Press(false); yield return new WaitForSeconds(.8f); CheckResult("real-stand-E-gets-up", !ctx.Motor.SeatHeight.HasValue);
        }
        IEnumerator RealCar()
        {
            var road = ReferenceMap.Layout.roads.First(a => a.id == "101-inland"); int k = road.points.Length / 2;
            var at = road.points[k] + Vector3.up * .06f; var direction = CityPhysics.Flat(road.points[k + 2] - road.points[k - 2]).normalized;
            var definition = ctx.Data.vehicles.First(a => !a.auto && a.kind == "car");
            var car = ctx.Vehicles.Fleet.FirstOrDefault(a => a.Data.id == definition.id) ?? ctx.Vehicles.Spawn(definition, at, Quaternion.LookRotation(direction));
            car.Body.position = at; car.Body.rotation = Quaternion.LookRotation(direction); car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero; car.LastUsed = ctx.Clock;
            Physics.SyncTransforms();
            var action = ctx.Actions.Find(a => a.id == "vehicle-" + car.Data.id);
            if (action == null) { CheckResult("real-car-selected-by-gaze", false, "missing action"); yield break; }
            var feet = action.Position - Vector3.up; feet.y = ctx.Ground(feet.x, feet.z) + .05f; ctx.Motor.Warp(feet);
            ctx.Camera.transform.position = feet + Vector3.up * 2.5f - car.Root.right * 2.6f; Look(action.LookPoint);
            yield return new WaitForSeconds(.5f); ctx.RefreshAction(); CheckResult("real-car-selected-by-gaze", ctx.CurrentAction == action, ctx.CurrentAction?.id);
            yield return Press(true); CheckResult("real-car-ignores-Q", !ctx.Motor.Riding);
            yield return Press(false); float entryUntil = Time.time + 8;
            while (ctx.Motor.Busy && Time.time < entryUntil) yield return null;
            yield return Frame(); CheckResult("real-car-E-enters", ctx.Motor.Riding == car.Root && !ctx.Motor.Busy);
            if (!ctx.Motor.Riding) yield break;
            Look(ctx.Camera.transform.position + Vector3.back * 10); yield return Frame(); CheckResult("real-car-exit-offered-looking-away", ctx.CurrentAction?.id == "vehicle-exit");
            yield return Press(false); yield return new WaitForSeconds(1.4f); CheckResult("real-car-E-exits", !ctx.Motor.Riding);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (ctx && ctx.Player)
            {
                if (savedActions != null) ctx.Actions = savedActions;
                ctx.Input.TouchInteract = ctx.Input.TouchTalk = ctx.Input.TouchEmoteHeld = false; ctx.UI.SetSettings(false); ctx.Motor.SetInputLock(false);
                ctx.Motor.Warp(savedPosition); ctx.Player.rotation = savedRotation; ctx.Camera.transform.SetPositionAndRotation(savedEye, savedView); ctx.Follow.enabled = savedFollow; ctx.Follow.ResetTracking();
            }
            if (floor) Destroy(floor); if (wall) Destroy(wall); if (prop) Destroy(prop);
        }
    }
}
