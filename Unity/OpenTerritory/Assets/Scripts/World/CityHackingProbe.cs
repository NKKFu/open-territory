using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Installed only with --city-hacking-qa, or explicitly attached in Play Mode.
    // Exercises the real input poller, target selection, simulation and physics.
    public sealed class CityHackingProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string unity, sourceHash;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        readonly List<CityHackOption> options = new List<CityHackOption>();
        CityContext ctx; string directory; float started; bool aimed, finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-hacking-qa") >= 0)
                new GameObject("QA hacking · opt-in").AddComponent<CityHackingProbe>();
        }
        void Awake() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool pass, string detail = "")
        { report.passed &= pass; report.checks.Add(new Check { name = name, passed = pass, detail = detail }); Debug.Log("HACK QA " + (pass ? "PASS " : "FAIL ") + name + " " + detail); }
        void Update()
        { if (!finished && Time.realtimeSinceStartup - started > 240) { report.errors.Add("Hacking review exceeded 240 seconds"); StopAllCoroutines(); Finish(); } }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/hacking/native"));
            Directory.CreateDirectory(directory);
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup - started < 60) yield return null;
            ctx = CityContext.Instance; CheckResult("native-context-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            report.unity = Application.unityVersion; report.sourceHash = ctx.Data.sourceHash;
            ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.Motor.SetInputLock(true); ctx.Motor.SetVisible(false); ctx.Combat.Holster();
            yield return null;
            var run = Review();
            while (true)
            {
                bool next = false;
                try { next = run.MoveNext(); } catch (Exception error) { report.errors.Add(error.ToString()); }
                if (!next) break; yield return run.Current;
            }
            Finish();
        }
        IEnumerator PressF(float held = .06f)
        { ctx.Input.TouchHack = true; yield return new WaitForSecondsRealtime(held); ctx.Input.TouchHack = false; yield return null; yield return null; }
        IEnumerator Toggle()
        { ctx.Input.TouchHackerToggle = true; yield return null; yield return null; }
        IEnumerator SelectAction(string id)
        {
            for (int i = 0; i < 8 && ctx.Remote.SelectedHack != id; i++)
            { ctx.Input.TouchHackNext = true; yield return null; yield return null; }
        }
        IEnumerator Aim(DeviceData device)
        {
            aimed = false; ctx.Remote.SetHackerMode(true);
            Vector3 anchor = ctx.Remote.TargetPoint(device, ctx.Player.position + Vector3.up * 1.68f);
            foreach (float radius in new[] { 7f, 11f, 17f })
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    Vector3 feet = anchor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    feet.y = ctx.Ground(feet.x, feet.z) + .05f;
                    if (!ctx.Free(feet)) continue;
                    Vector3 eye = feet + Vector3.up * 1.68f, target = ctx.Remote.TargetPoint(device, eye);
                    if (!ctx.Visible(eye, target)) continue;
                    ctx.Motor.Warp(feet); ctx.Motor.SetInputLock(true); ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    Physics.SyncTransforms(); yield return null; yield return null;
                    if (ctx.Remote.SelectedDevice == device) { aimed = true; yield break; }
                }
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            // Render to a texture as batch players have no capturable desktop surface.
            var target = new RenderTexture(1280, 720, 24); target.Create();
            var previous = RenderTexture.active; var previousTarget = ctx.Camera.targetTexture;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            ctx.Camera.targetTexture = target;
            foreach (var overlay in overlays) { overlay.renderMode = RenderMode.ScreenSpaceCamera; overlay.worldCamera = ctx.Camera; overlay.planeDistance = .5f; }
            Canvas.ForceUpdateCanvases(); ctx.GetComponent<CityCanvasUI>().RefreshHackingUI(); ctx.Camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i]; }
            ctx.Camera.targetTexture = previousTarget; RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(image);
            yield return null;
        }
        DeviceData Device(string type) => ctx.Systems.HackDevices.FirstOrDefault(d => d.type == type);
        IEnumerator Review()
        {
            CheckResult("Z-F-Tab-and-signal-bindings", CityInput.HackerKey == KeyCode.Z && CityInput.HackKey == KeyCode.F && CityInput.HackNextKey == KeyCode.Tab && CityInput.RightSignalKey == KeyCode.C);
            CheckResult("scanner-starts-off", !ctx.Remote.HackerMode && !ctx.Remote.PreviewVisible);
            var phone = Device("phone"); CheckResult("public-phone-exists", phone != null); if (phone == null) yield break;
            yield return Aim(phone); CheckResult("real-phone-can-be-targeted", aimed, ctx.Remote.SelectedDevice?.id); if (!aimed) yield break;
            int hacks = ctx.Remote.SuccessfulHacks, rings = ctx.Systems.Audio.EventCount("phone");
            yield return new WaitForSecondsRealtime(.9f);
            CheckResult("preview-only-never-executes", hacks == ctx.Remote.SuccessfulHacks && rings == ctx.Systems.Audio.EventCount("phone"));
            CheckResult("preview-identifies-target-and-action", ctx.Remote.HackPreview.Contains("TELEFONE") && ctx.Remote.HackPreview.Contains("[F]") && ctx.Remote.HackPreview.Contains("Tocar"), ctx.Remote.HackPreview);
            CheckResult("custom-hacking-card-is-visible", ctx.GetComponent<CityCanvasUI>().HackingCardVisible);
            yield return Capture("01-alvo-e-efeito");
            yield return Toggle(); CheckResult("Z-hides-preview-without-hacking", !ctx.Remote.HackerMode && !ctx.Remote.PreviewVisible && hacks == ctx.Remote.SuccessfulHacks);
            yield return PressF(1.0f);
            CheckResult("F-outside-mode-executes-once-while-held", ctx.Remote.SuccessfulHacks == hacks + 1 && !ctx.Remote.HackerMode && !ctx.Remote.PreviewVisible);
            yield return new WaitForSecondsRealtime(.6f);
            CheckResult("release-does-not-hack", ctx.Remote.SuccessfulHacks == hacks + 1);
            yield return Toggle(); yield return SelectAction("phone-silence");
            CheckResult("Tab-changes-predicted-action", ctx.Remote.SelectedHack == "phone-silence" && ctx.Remote.HackPreview.Contains("Silenciar"));
            hacks = ctx.Remote.SuccessfulHacks;
            ctx.GetComponent<CityCanvasUI>().HackExecuteButton.onClick.Invoke(); yield return null; yield return null;
            CheckResult("card-button-executes-exactly-one-hack", ctx.Remote.SuccessfulHacks == hacks + 1);
            rings = ctx.Systems.Audio.EventCount("phone");
            yield return new WaitForSecondsRealtime(2.6f);
            CheckResult("silence-stops-this-phone", ctx.Systems.Audio.EventCount("phone") == rings);
            ctx.GetComponent<CityCanvasUI>().HackCycleButton.onClick.Invoke(); yield return null; yield return null;
            CheckResult("card-button-cycles-actions", ctx.Remote.SelectedHack == "phone-ring"); hacks = ctx.Remote.SuccessfulHacks;
            ctx.UI.SetSettings(true); float clock = ctx.Clock; ctx.Input.TouchHack = true; ctx.Input.TouchHackerToggle = true;
            yield return new WaitForSecondsRealtime(.3f);
            CheckResult("Esc-pauses-effects-and-rejects-input", ctx.Paused && Mathf.Abs(ctx.Clock - clock) < .001f && hacks == ctx.Remote.SuccessfulHacks && !ctx.Remote.PreviewVisible);
            ctx.UI.SetSettings(false); ctx.Input.TouchHack = false; yield return null; yield return null;
            CheckResult("resume-does-not-execute-buffered-hack", hacks == ctx.Remote.SuccessfulHacks && ctx.Remote.HackerMode);
            Vector3 camera = ctx.Camera.transform.position; Quaternion rotation = ctx.Camera.transform.rotation;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA temporary occluder"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = Vector3.Lerp(camera, phone.position, .5f); wall.transform.rotation = rotation; wall.transform.localScale = new Vector3(5, 5, .4f);
            Physics.SyncTransforms(); yield return null; yield return null;
            CheckResult("walls-block-target-selection", ctx.Remote.SelectedDevice != phone);
            Destroy(wall); yield return null;
            ctx.Camera.transform.rotation = Quaternion.Euler(-85, 0, 0); yield return null; yield return null;
            hacks = ctx.Remote.SuccessfulHacks; yield return PressF();
            CheckResult("empty-sky-cannot-be-hacked", ctx.Remote.SelectedDevice == null && ctx.Remote.SuccessfulHacks == hacks);
            Vector3 far = phone.position + Vector3.forward * 45; far.y = ctx.Ground(far.x, far.z) + .05f;
            ctx.Motor.Warp(far); ctx.Camera.transform.position = far + Vector3.up * 1.68f; ctx.Camera.transform.LookAt(phone.position);
            yield return null; yield return null;
            CheckResult("range-is-measured-from-character", ctx.Remote.SelectedDevice != phone);

            var light = ctx.Systems.HackDevices.Where(d => d.type == "light").OrderBy(d => (d.position - phone.position).sqrMagnitude).FirstOrDefault();
            CheckResult("existing-luminaires-are-hackable", light != null);
            if (light != null)
            {
                yield return Aim(light); CheckResult("luminaire-can-be-targeted", aimed, ctx.Remote.SelectedDevice?.id);
                if (aimed)
                {
                    var lighting = FindFirstObjectByType<CityLighting>();
                    yield return SelectAction("light-off"); yield return PressF();
                    CheckResult("blackout-disables-real-light", lighting.CircuitOff(light.target) && lighting.fixtures[light.target].source.intensity < .001f);
                    ctx.Night = 1; CheckResult("blackout-reduces-night-visibility", ctx.Systems.HackVisibility(ctx.Player.position) < 1);
                    ctx.Night = 0; CheckResult("daylight-still-preserves-visibility", Mathf.Approximately(ctx.Systems.HackVisibility(ctx.Player.position), 1));
                    yield return Capture("02-apagao-local");
                    yield return SelectAction("light-on"); yield return PressF();
                    CheckResult("F-restores-the-selected-circuit", !lighting.CircuitOff(light.target) && lighting.fixtures[light.target].source.intensity > .01f);
                    yield return new WaitForSecondsRealtime(2.1f); yield return SelectAction("light-off"); yield return PressF();
                    yield return new WaitForSecondsRealtime(14.3f);
                    CheckResult("blackout-recovers-automatically", !lighting.CircuitOff(light.target) && lighting.fixtures[light.target].source.intensity > .01f);
                }
            }
            var door = Device("door");
            if (door != null)
            {
                var leaf = ctx.Node(door.node); Vector3 closed = leaf.position;
                yield return Aim(door); CheckResult("electronic-door-can-be-targeted", aimed, ctx.Remote.SelectedDevice?.id);
                if (aimed)
                {
                    yield return SelectAction("door-open"); yield return PressF(); yield return new WaitForSecondsRealtime(.9f);
                    CheckResult("F-opens-real-door", Vector3.Distance(leaf.position, closed) > .8f);
                    var data = ctx.Data.places.First(p => p.door != null && p.door.id == door.node).door;
                    var blocker = GameObject.CreatePrimitive(PrimitiveType.Capsule); blocker.layer = CityContext.ActorLayer;
                    blocker.transform.position = closed + Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0) * Vector3.left * data.width * .5f + Vector3.up * 1.3f;
                    blocker.transform.localScale = new Vector3(.6f, 1.2f, .6f); Physics.SyncTransforms();
                    yield return SelectAction("door-lock"); yield return PressF(); yield return new WaitForSecondsRealtime(.6f);
                    CheckResult("locked-door-waits-for-occupied-threshold", Vector3.Distance(leaf.position, closed) > .8f);
                    ctx.Systems.GetHackOptions(door, options);
                    CheckResult("locked-door-predicts-blocked-open", options.Any(o => o.Id == "door-open" && !o.Available) && options.Any(o => o.Id == "door-lock" && o.Label.Contains("Destrancar")));
                    Destroy(blocker); yield return new WaitForSecondsRealtime(1.1f);
                    CheckResult("locked-door-closes-after-threshold-clears", Vector3.Distance(leaf.position, closed) < .08f);
                    yield return SelectAction("door-lock"); yield return PressF();
                    ctx.Systems.GetHackOptions(door, options); CheckResult("unlock-restores-automatic-access", options.Any(o => o.Id == "door-open" && o.Available));
                }
            }
            var personData = ctx.Data.residents.First(p => !p.child && !p.stationary && !p.sitting && !p.performance && string.IsNullOrEmpty(p.role));
            var node = ctx.Data.nav.OrderBy(n => new Vector2(n.x - phone.position.x, n.z - phone.position.z).sqrMagnitude).First(n => n.edges.Length > 0);
            Vector3 spawn = new Vector3(node.x, ctx.Ground(node.x, node.z) + .05f, node.z);
            bool spawned = ctx.Residents.Spawn(personData.id, spawn, Quaternion.identity);
            var person = ctx.Residents.Find(personData.id);
            CheckResult("resident-with-real-controller-is-present", spawned && person != null);
            if (spawned && person != null)
            {
                var mobile = ctx.Systems.HackDevices.First(d => d.type == "resident-phone" && d.node == personData.id);
                yield return Aim(mobile); CheckResult("resident-phone-can-be-targeted", aimed);
                if (aimed)
                {
                    yield return PressF(); yield return new WaitForSecondsRealtime(.25f);
                    CheckResult("phone-hack-interrupts-routine-and-shows-prop", ctx.Residents.IsReactingToHack(person) && person.Phone && person.Phone.gameObject.activeSelf);
                    yield return Capture("03-reacao-do-morador");
                    yield return new WaitForSecondsRealtime(5.2f);
                    CheckResult("resident-resumes-after-distraction", !ctx.Residents.IsReactingToHack(person) && !person.Controlled);
                }
                ctx.Residents.Control(person, false); Vector3 before = person.Root.position;
                int reacting = ctx.Residents.ReactToHack(before + person.Root.forward * 3 + Vector3.up, "Vou conferir esse som.", true, 12);
                yield return new WaitForSecondsRealtime(1.4f);
                CheckResult("city-investigation-uses-real-movement", reacting > 0 && ctx.Residents.IsReactingToHack(person) && Vector3.Distance(before, person.Root.position) > .12f, "reacting=" + reacting + " moved=" + Vector3.Distance(before, person.Root.position));
                ctx.Residents.Control(person, false); ctx.Police.Clear();
                bool called = ctx.Police.Report("dano", person.Root.position, person);
                CheckResult("witness-starts-a-real-police-call", called && ctx.Police.Calling);
                if (ctx.Police.Calling)
                {
                    var callingPhone = ctx.Systems.HackDevices.First(d => d.type == "resident-phone" && d.node == person.Data.id);
                    yield return Aim(callingPhone); yield return SelectAction("phone-jam");
                    CheckResult("caller-predicts-interrupting-the-report", aimed && ctx.Remote.SelectedHack == "phone-jam");
                    yield return PressF(); CheckResult("F-cancels-call-before-dispatch", !ctx.Police.Calling && !person.Calling && ctx.Police.Stage == "livre");
                }
            }
            var carData = ctx.Data.vehicles.First(d => !d.auto && d.kind != "bike" && d.kind != "bus" && string.IsNullOrEmpty(d.role));
            var car = ctx.Vehicles.Spawn(carData, carData.position, Quaternion.Euler(0, carData.yaw * Mathf.Rad2Deg, 0));
            CheckResult("parked-car-has-real-physics", car != null && car.Body);
            if (car != null)
            {
                var vehicle = ctx.Systems.HackDevices.First(d => d.node == carData.id && (d.type == "vehicle" || d.type == "car"));
                yield return Aim(vehicle); CheckResult("vehicle-can-be-targeted", aimed);
                if (aimed)
                {
                    CheckResult("vehicle-offers-six-distinct-actions", ctx.Remote.ActionCount == 6);
                    yield return SelectAction("vehicle-horn"); int horns = ctx.Systems.Audio.EventCount("horn"); yield return PressF();
                    CheckResult("remote-horn-is-spatial-audio", ctx.Systems.Audio.EventCount("horn") > horns);
                    yield return SelectAction("vehicle-alarm"); yield return PressF();
                    CheckResult("alarm-is-temporary-and-flashes-vehicle", car.AlarmUntil > ctx.Clock);
                    yield return Capture("04-acoes-do-carro");
                    Vector3 before = car.Position; yield return SelectAction("vehicle-forward"); yield return PressF();
                    CheckResult("F-starts-the-selected-remote-manoeuvre", car.RemoteUntil > ctx.Clock, "action=" + ctx.Remote.SelectedHack + " until=" + car.RemoteUntil + " clock=" + ctx.Clock);
                    yield return new WaitForSecondsRealtime(1.4f);
                    CheckResult("remote-manoeuvre-uses-rigidbody", Vector3.Distance(before, car.Position) > .12f && Mathf.Abs(car.Speed) < 3.6f, "moved=" + Vector3.Distance(before, car.Position) + " speed=" + car.Speed);
                    var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.layer = CityContext.ActorLayer;
                    obstacle.transform.position = car.Position + car.Root.forward * (car.Data.length * .5f + 1.8f) + Vector3.up; obstacle.transform.localScale = new Vector3(2, 2, .4f);
                    Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(.7f);
                    CheckResult("remote-manoeuvre-brakes-for-obstacle", car.StoppedReason == "hack-obstaculo" && Mathf.Abs(car.Speed) < 1.2f, "reason=" + car.StoppedReason + " speed=" + car.Speed);
                    Destroy(obstacle); yield return new WaitForSecondsRealtime(1.2f);
                    CheckResult("remote-manoeuvre-expires", car.RemoteUntil == 0);
                    yield return new WaitForSecondsRealtime(4.2f); CheckResult("alarm-expires", car.AlarmUntil <= ctx.Clock);
                    yield return Aim(vehicle); yield return SelectAction("vehicle-drive");
                    Vector3 body = ctx.Player.position;
                    yield return PressF(); ctx.Follow.enabled = true;
                    CheckResult("F-assumes-full-remote-vehicle-control", ctx.Remote.VehicleActive && ctx.Vehicles.RemoteVehicle == car && ctx.Motor.Remote && ctx.Vehicles.Active == null);
                    if (ctx.Remote.VehicleActive)
                    {
                        yield return Toggle(); yield return new WaitForSecondsRealtime(.2f);
                        CheckResult("remote-driving-has-a-custom-connection-card", ctx.GetComponent<CityCanvasUI>().RemoteDriveCardVisible);
                        yield return Capture("05-controle-remoto");
                        Vector3 start = car.Position; ctx.Input.TouchMove = Vector2.up;
                        yield return new WaitForSecondsRealtime(1.1f);
                        CheckResult("remote-W-drives-car-and-keeps-body-in-place", Vector3.Distance(start, car.Position) > .2f && Vector3.Distance(body, ctx.Player.position) < .05f, "moved=" + Vector3.Distance(start, car.Position));
                        CheckResult("remote-camera-follows-car", Vector3.Distance(ctx.Camera.transform.position, car.Position) < 20);
                        float heading = car.Root.eulerAngles.y; ctx.Input.TouchMove = new Vector2(.8f, 1);
                        yield return new WaitForSecondsRealtime(.55f);
                        CheckResult("remote-A-D-steer-real-wheels", Mathf.Abs(Mathf.DeltaAngle(heading, car.Root.eulerAngles.y)) > .2f);
                        ctx.UI.SetSettings(true); start = car.Position; float pausedClock = ctx.Clock;
                        yield return new WaitForSecondsRealtime(.3f);
                        CheckResult("Esc-pauses-remotely-driven-car", Vector3.Distance(start, car.Position) < .01f && Mathf.Abs(ctx.Clock - pausedClock) < .001f);
                        ctx.UI.SetSettings(false); ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = true;
                        yield return new WaitForSecondsRealtime(.7f); ctx.Input.TouchBrake = false;
                        ctx.Input.TouchInteract = true; yield return null; yield return null;
                        CheckResult("E-restores-body-control-and-brakes-car", !ctx.Remote.Active && ctx.Vehicles.RemoteVehicle == null && !ctx.Motor.Remote && car.JamUntil > ctx.Clock);
                        CheckResult("remote-control-never-boards-the-player", ctx.Motor.Riding == null && ctx.Vehicles.Active == null && Vector3.Distance(body, ctx.Player.position) < .05f);
                        yield return new WaitForSecondsRealtime(.5f);
                        if (ctx.Remote.ConnectVehicle(car.Root))
                        {
                            Vector3 farBody = car.Position + Vector3.right * 125; farBody.y = ctx.Ground(farBody.x, farBody.z) + .05f;
                            ctx.Motor.Warp(farBody); yield return null; yield return null;
                            CheckResult("signal-loss-disconnects-and-brakes-car", !ctx.Remote.VehicleActive && car.JamUntil > ctx.Clock);
                            ctx.Motor.Warp(body);
                        }
                    }
                    ctx.Input.TouchMove = Vector2.zero; ctx.Follow.enabled = false;
                }
            }
            var signal = Device("signal");
            if (signal != null)
            {
                CheckResult("signal-hack-accepts-stop", ctx.Systems.ExecuteHack(signal, "signal-stop"));
                CheckResult("signal-stops-both-directions", ctx.Systems.SignalState(ctx.Data.signals[signal.target], true) == 2 && ctx.Systems.SignalState(ctx.Data.signals[signal.target], false) == 2);
                CheckResult("signal-cycle-can-be-restored", ctx.Systems.ExecuteHack(signal, "signal-restore"));
            }
            var fountain = Device("fountain");
            if (fountain != null)
            {
                bool on = ctx.Systems.FountainOn;
                CheckResult("fountain-switch-changes-water-state", ctx.Systems.ExecuteHack(fountain, "fountain-toggle") && ctx.Systems.FountainOn != on);
                CheckResult("fountain-pump-attracts-attention", ctx.Systems.ExecuteHack(fountain, "fountain-lure"));
            }
            var radio = Device("harbor-radio");
            if (radio != null) CheckResult("radio-has-a-real-interference-action", ctx.Systems.ExecuteHack(radio, "radio-lure"));
            var cinema = Device("cinema");
            if (cinema != null)
            {
                bool on = ctx.Systems.CinemaPlaying;
                CheckResult("cinema-switch-changes-projection", ctx.Systems.ExecuteHack(cinema, "cinema-toggle") && ctx.Systems.CinemaPlaying != on);
                CheckResult("cinema-mechanism-attracts-attention", ctx.Systems.ExecuteHack(cinema, "cinema-lure"));
            }
            var water = Device("fire-water");
            if (water != null) CheckResult("water-panel-offers-on-and-off", ctx.Systems.ExecuteHack(water, "water-on") && ctx.Systems.ExecuteHack(water, "water-off"));
            // Existing remote tools use the same F selector and action catalogue.
            ctx.Remote.SetHackerMode(false); ctx.Motor.SetInputLock(false);
            yield return Aim(Device("camera")); CheckResult("security-camera-can-be-targeted", aimed);
            if (aimed) { yield return PressF(); CheckResult("F-enters-camera-without-held-scan", ctx.Remote.Active); ctx.Remote.RecallOrExit(); }
            ctx.Motor.SetInputLock(false); ctx.Remote.SetHackerMode(false);
            yield return Toggle(); ctx.Input.TouchDrone = true; yield return null; yield return null;
            CheckResult("drone-preserves-hacker-mode", ctx.Remote.DroneActive && ctx.Remote.HackerMode);
            if (ctx.Remote.DroneActive) ctx.Remote.RecallOrExit();
        }
        void Finish()
        {
            if (finished) return; finished = true;
            if (ctx) { ctx.Input.TouchHack = false; ctx.UI.SetSettings(false); ctx.Remote.SetHackerMode(false); ctx.Motor.SetVisible(true); }
            report.passed &= report.errors.Count == 0;
            if (string.IsNullOrEmpty(directory)) directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/hacking/native"));
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "hacking-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("HACK_QA_" + (report.passed ? "PASS" : "FAIL") + " checks=" + report.checks.Count);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
