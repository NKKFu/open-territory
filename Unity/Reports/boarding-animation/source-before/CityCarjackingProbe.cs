using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Paired-animation review: opt in explicitly; no normal-game installation.
    public sealed class CityCarjackingProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Sample
        {
            public string scenario, phase; public float time, door, handGap; public Vector3 player, driver, head;
            public bool visible, riding, locked;
        }
        [Serializable] public class Report
        {
            public bool passed = true; public List<Check> checks = new List<Check>();
            public List<Sample> samples = new List<Sample>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report(); CityContext ctx; CityVehicles.Vehicle car;
        string dir; bool finished; int crimes, videoFrame; float began;
        GameObject wall;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-carjacking-qa") >= 0) new GameObject("Carjacking animation QA opt-in").AddComponent<CityCarjackingProbe>(); }
        void Awake() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("CARJACK_QA " + name + " " + passed + " " + detail); }
        void Update()
        { if (!finished && Time.realtimeSinceStartup - began > 180) { CheckResult("timeout", false); StopAllCoroutines(); Finish(); } }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator PressE() { ctx.Input.TouchInteract = true; yield return Frame(); }
        void Crime(string kind, Vector3 at) { if (kind == "furto" || kind == "furto-policial") crimes++; }
        static bool Visible(Transform root) => root && root.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff);
        Transform DriverHead() => car.Driver.GetComponentsInChildren<Transform>(true).First(t => t.name.IndexOf("driver-head", StringComparison.OrdinalIgnoreCase) >= 0);
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/carjacking/native"));
            Directory.CreateDirectory(dir); Directory.CreateDirectory(Path.Combine(dir, "frames"));
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; ctx.Population.enabled = false; ctx.Residents.enabled = false; ctx.Follow.enabled = false;
            ctx.Combat.Holster(); ctx.Pause(false); ctx.Vehicles.Crime += Crime;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA carjacking floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(100, 2, 70);
            var models = new[] { "sedã", "picape", "furgão" };
            foreach (string model in models)
            {
                var data = ctx.Data.vehicles.First(v => v.auto && v.kind == "car" && v.model == model && string.IsNullOrEmpty(v.role));
                car = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == data.id) ?? ctx.Vehicles.Spawn(data, new Vector3(0, 200.1f, 0), Quaternion.identity);
                foreach (int side in new[] { -1, 1 }) yield return ReviewRemoval(model, side, model == "sedã" && side == -1, model == "furgão" && side == 1);
                car.Root.position += Vector3.right * 30; car.Body.position = car.Root.position;
            }
            yield return BlockedAndInterrupted();
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors)); Finish();
        }
        void Place()
        {
            ctx.Motor.SetRide(null); ctx.Motor.SetInputLock(false); ctx.Motor.SetVisible(true);
            car.Root.SetPositionAndRotation(new Vector3(0, 200.08f, 0), Quaternion.identity); car.Body.position = car.Root.position; car.Body.rotation = Quaternion.identity;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.Motion = new CityVehicleMath.State { Direction = 1, Gear = 1 }; car.Occupied = true; car.Data.auto = false;
            car.Controlled = car.Commanded = car.ExitRequested = false; car.RemoteUntil = car.JamUntil = car.TargetSpeed = 0;
            car.LastImpact = -1000; car.LastUsed = ctx.Clock; car.Driver.gameObject.SetActive(true); car.Body.WakeUp(); Physics.SyncTransforms();
            // Keep earlier owners outside this fixture without deleting anyone.
            foreach (var p in ctx.Residents.People.Where(p => p.Data.id < 0))
            { bool on = p.Controller.enabled; p.Controller.enabled = false; p.Root.position = new Vector3(20, 200.04f, 15 + (p.Data.id % 5)); p.Controller.enabled = on; }
        }
        void Approach(int side)
        {
            var feet = new Vector3(side * (car.Data.width * .5f + .9f), 200.04f, car.Data.seatMount.z - .2f);
            ctx.Motor.Warp(feet); ctx.Player.rotation = Quaternion.Euler(0, -side * 90, 0);
            ctx.Camera.transform.position = car.Position + new Vector3(side * 4.8f, 3.0f, 5.8f);
            ctx.Camera.transform.LookAt(car.Position + new Vector3(side * 1.0f, 1.10f, -.3f));
            ctx.RefreshAction();
        }
        IEnumerator ReviewRemoval(string model, int side, bool record, bool normalCamera)
        {
            Place(); Approach(side); yield return new WaitForSeconds(.25f);
            if (normalCamera) { ctx.Follow.enabled = true; ctx.Follow.Face(-side * 90); ctx.Follow.ResetTracking(); yield return new WaitForSeconds(.5f); }
            string name = model + "-" + side; int population = ctx.Residents.People.Count, before = crimes;
            var head = DriverHead(); Vector3 previousHead = head.position; float previousTime = Time.time;
            CheckResult(name + "-E-target", ctx.CurrentAction?.id == "vehicle-" + car.Data.id);
            Capture(name + "-01-before"); yield return PressE();
            CheckResult(name + "-starts-with-occupant-still-visible", ctx.Vehicles.RemovingDriver && Visible(head) && !ctx.Motor.Riding && ctx.Motor.Busy);
            bool visible = true, earlyRide = false, continuous = true, paused = false, grip = false, braked = true;
            var phases = new HashSet<string>(); CityResidents.Resident driver = null;
            float start = Time.time, nextVideo = Time.time, nextSample = 0; int captures = 0, framed = 0; float headSeparation = 1;
            while ((ctx.Vehicles.RemovingDriver || ctx.Motor.Busy) && Time.time - start < 9)
            {
                if (ctx.Vehicles.RemovedDriver != null) driver = ctx.Vehicles.RemovedDriver;
                string phase = ctx.Vehicles.CarjackingPhase;
                if (phase.Length > 0) phases.Add(phase);
                visible &= ctx.Vehicles.RemovingDriver ? Visible(head) : driver == null || Visible(driver.Rig.Head);
                earlyRide |= ctx.Vehicles.RemovingDriver && ctx.Motor.Riding;
                if (ctx.Vehicles.RemovingDriver)
                {
                    float dt = Mathf.Max(.001f, Time.time - previousTime);
                    continuous &= Vector3.Distance(head.position, previousHead) < 8 * dt + .18f;
                    previousHead = head.position; previousTime = Time.time;
                    ctx.Input.TouchMove = Vector2.up; ctx.Input.TouchInteract = true;
                    braked &= Mathf.Abs(car.Speed) < .2f;
                }
                else ctx.Input.TouchMove = Vector2.zero;
                float handGap = driver == null ? -1 : Vector3.Distance(ctx.Actors.PlayerRig.Hands[1].position, driver.Rig.Arms[side < 0 ? 0 : 1].position + Vector3.down * .1f);
                if (phase == "pulling" && handGap < .28f) grip = true;
                if (normalCamera && phase == "recovering" && driver != null)
                {
                    var a = ctx.Camera.WorldToViewportPoint(ctx.Actors.PlayerRig.Head.position);
                    var b = ctx.Camera.WorldToViewportPoint(driver.Rig.Head.position);
                    if (a.z > 0 && b.z > 0 && a.x > .04f && a.x < .96f && a.y > .04f && a.y < .96f && b.x > .04f && b.x < .96f && b.y > .04f && b.y < .96f) framed++;
                    headSeparation = Mathf.Min(headSeparation, Vector2.Distance(a, b));
                }
                if (Time.time >= nextSample)
                {
                    nextSample = Time.time + .06f;
                    report.samples.Add(new Sample { scenario = name, phase = phase, time = Time.time - start, door = car.DoorOpen, handGap = handGap,
                        player = ctx.Player.position, driver = driver != null ? driver.Root.position : Vector3.zero, head = head.position,
                        visible = Visible(head) || driver != null && Visible(driver.Rig.Head), riding = ctx.Motor.Riding, locked = ctx.Motor.Busy });
                }
                int desired = phase == "opening" ? 1 : phase == "grabbing" ? 2 : phase == "pulling" ? 3 : phase == "recovering" ? 4 : 0;
                if (desired > captures) { captures = desired; Capture(name + "-0" + (desired + 1) + "-" + phase); }
                if (record && Time.time >= nextVideo) { nextVideo = Time.time + .1f; Capture("frames/frame-" + (videoFrame++).ToString("D4")); }
                if (phase == "pulling" && !paused && side == -1)
                {
                    paused = true; var actorPosition = driver.Root.position; var playerPosition = ctx.Player.position; float clock = ctx.Clock;
                    ctx.Input.TouchMove = Vector2.zero; ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.22f);
                    CheckResult(name + "-Esc-freezes-both-actors", ctx.Clock == clock && Vector3.Distance(actorPosition, driver.Root.position) < .005f && Vector3.Distance(playerPosition, ctx.Player.position) < .005f);
                    ctx.UI.SetSettings(false); previousTime = Time.time; previousHead = head.position;
                }
                yield return Frame();
            }
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchInteract = false;
            CheckResult(name + "-visible-continuous-removal", visible && continuous, "continuous=" + continuous + "; visible=" + visible);
            CheckResult(name + "-door-grab-pull-recovery", new[] { "opening", "grabbing", "pulling", "recovering" }.All(phases.Contains), string.Join(",", phases));
            CheckResult(name + "-hands-contact-driver", grip);
            CheckResult(name + "-no-driving-during-removal", !earlyRide && braked);
            CheckResult(name + "-same-person-stays-outside", driver != null && Visible(driver.Rig.Head) && driver.Controller.enabled && ctx.Residents.People.Contains(driver) && ctx.Residents.People.Count == population + 1 && Mathf.Abs(car.Root.InverseTransformPoint(driver.Root.position).x) > car.Data.width * .5f + .5f);
            CheckResult(name + "-player-visible-in-seat", ctx.Vehicles.Active == car && ctx.Motor.Riding == car.Root && !ctx.Motor.Busy && Visible(ctx.Actors.PlayerRig.Head) && !car.Driver.gameObject.activeInHierarchy);
            CheckResult(name + "-one-theft-for-one-press", crimes == before + 1);
            if (normalCamera) CheckResult("paired-action-fits-normal-game-camera", framed > 2 && headSeparation > .045f, "frames=" + framed + "; head separation=" + headSeparation.ToString("F3"));
            Capture(name + "-06-seated");
            ctx.Input.TouchMove = Vector2.up; yield return new WaitForSeconds(.6f); ctx.Input.TouchMove = Vector2.zero;
            CheckResult(name + "-drives-after-removal", car.Speed > .4f, car.Speed.ToString("F2"));
            yield return PressE(); float until = Time.time + 4;
            while ((ctx.Motor.Riding || ctx.Motor.Busy) && Time.time < until) yield return null;
            CheckResult(name + "-exits-with-controls-restored", !ctx.Motor.Riding && !ctx.Motor.Busy && ctx.Motor.Controller.enabled);
            ctx.Follow.enabled = false;
        }
        IEnumerator BlockedAndInterrupted()
        {
            Place(); Approach(-1); yield return Frame();
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(-(car.Data.width * .5f + 1.88f), 201, car.Data.seatMount.z + .72f);
            wall.transform.localScale = new Vector3(1, 2, 1); Physics.SyncTransforms();
            var head = DriverHead(); yield return PressE();
            CheckResult("blocked-extraction-keeps-driver-and-input", !ctx.Vehicles.RemovingDriver && !ctx.Motor.Busy && ctx.Vehicles.Active == null && Visible(head));
            Destroy(wall); wall = null; yield return Frame(); Approach(-1); yield return PressE();
            float until = Time.time + 3;
            while (ctx.Vehicles.CarjackingPhase != "grabbing" && Time.time < until) yield return Frame();
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(-(car.Data.width * .5f + 1.02f), 201, car.Data.seatMount.z - .24f);
            wall.transform.localScale = new Vector3(.8f, 2, .9f); Physics.SyncTransforms();
            until = Time.time + 4; bool returning = false;
            while (ctx.Vehicles.RemovingDriver && Time.time < until) { returning |= ctx.Vehicles.CarjackingPhase == "returning"; yield return Frame(); }
            CheckResult("new-obstacle-rolls-back-with-visible-driver", returning && car.Occupied && Visible(DriverHead()) && ctx.Vehicles.Active == null && !ctx.Motor.Busy && ctx.Motor.Controller.enabled);
            Destroy(wall); wall = null;
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = ctx.Camera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var modes = overlays.Select(c => c.renderMode).ToArray(); var cameras = overlays.Select(c => c.worldCamera).ToArray(); var distances = overlays.Select(c => c.planeDistance).ToArray();
            var target = RenderTexture.GetTemporary(1280, 800, 24); Texture2D picture = null;
            try
            {
                camera.targetTexture = target;
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = camera; c.planeDistance = .3f; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(1280, 800, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = modes[i]; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = distances[i]; }
                RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture);
            }
        }
        void Finish()
        {
            finished = true; if (wall) Destroy(wall);
            if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchInteract = false; ctx.UI.SetSettings(false); ctx.Vehicles.Crime -= Crime; }
            report.passed &= report.errors.Count == 0; File.WriteAllText(Path.Combine(dir, "carjacking-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CARJACK_QA_DONE " + report.passed);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1); else if (ctx) ctx.Pause(true);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (ctx && ctx.Vehicles) ctx.Vehicles.Crime -= Crime; }
    }
}
