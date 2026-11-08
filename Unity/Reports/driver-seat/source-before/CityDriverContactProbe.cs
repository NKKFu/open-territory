using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit acceptance scenario. Never installed during normal play.
    public sealed class CityDriverContactProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report
        {
            public bool passed = true; public string unity, graphics;
            public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; CityVehicles.Vehicle car; CityResidents.Resident moving;
        Vector3 pedestrianVelocity; Transform originalParent; Vector3 originalScale;
        Transform contactTarget; float contactSpeed;
        string dir; float started; bool finished;
        public string OutputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-driver-contact-qa") >= 0) new GameObject("Driver and pedestrian contact QA opt-in").AddComponent<CityDriverContactProbe>(); }
        void Awake() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("DRIVER_CONTACT_QA " + name + " " + passed + " " + detail); }
        void Contact(CityVehicles.Vehicle vehicle, float speed, Collider other)
        { if (ctx.Residents.FromCollider(other)?.Root == contactTarget) contactSpeed = speed; }
        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - started > 230) { CheckResult("timeout", false); StopAllCoroutines(); Finish(); }
            if (moving != null && moving.Controller.enabled && !ctx.Paused)
            { moving.Velocity = pedestrianVelocity; moving.Controller.Move(pedestrianVelocity * Time.deltaTime + Vector3.down * Time.deltaTime * 2); }
        }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator PressE() { ctx.Input.TouchInteract = true; yield return Frame(); }
        static bool Visible(Transform root) => root.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.forceRenderingOff);
        void PlaceCar(float speed = 0)
        {
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = false;
            car.Data.auto = car.Occupied = car.Commanded = car.ExitRequested = car.TrafficHold = false;
            car.RemoteUntil = car.JamUntil = 0; car.LastImpact = -1000;
            car.Body.position = new Vector3(0, 200.10f, 0); car.Body.rotation = Quaternion.identity;
            car.Root.SetPositionAndRotation(car.Body.position, car.Body.rotation);
            car.Body.linearVelocity = Vector3.forward * speed; car.Body.angularVelocity = Vector3.zero;
            car.Motion = new CityVehicleMath.State { Speed = speed, Direction = speed < 0 ? -1 : 1, Gear = 1 };
            car.Body.WakeUp(); if (car.Driver) car.Driver.gameObject.SetActive(false); Physics.SyncTransforms();
        }
        IEnumerator Enter(int side)
        {
            var entrance = car.Root.TransformPoint(new Vector3(side * (car.Data.width * .5f + .75f), 0, car.Data.seatMount.z));
            entrance.y = 200.04f; ctx.Motor.Warp(entrance);
            var eye = entrance + Vector3.up * 1.8f;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(car.Root.TransformPoint(new Vector3(0, 1, car.Data.seatMount.z)) - eye));
            yield return new WaitForSeconds(.2f); ctx.RefreshAction();
            CheckResult(car.Data.model + "-E-targets-car-" + side, ctx.CurrentAction?.id == "vehicle-" + car.Data.id,
                "action=" + ctx.CurrentAction?.id + " player=" + ctx.Player.position + " car=" + car.Position + " busy=" + ctx.Motor.Busy + " speed=" + car.Speed);
            yield return PressE(); float until = Time.time + 7;
            while ((ctx.Motor.Busy || ctx.Vehicles.RemovingDriver) && Time.time < until) yield return Frame();
            yield return Frame();
            CheckResult(car.Data.model + "-boards-visible-driver", ctx.Vehicles.Active == car && ctx.Motor.Riding == car.Root && Visible(ctx.Actors.PlayerRig.Head) && !car.Driver.gameObject.activeSelf);
        }
        IEnumerator Exit()
        {
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = true;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero; yield return new WaitForSeconds(.2f);
            yield return PressE(); float until = Time.time + 3;
            while ((ctx.Motor.Riding || ctx.Motor.Busy) && Time.time < until) yield return Frame();
            ctx.Input.TouchBrake = false;
            CheckResult(car.Data.model + "-exit-restores-parent-capsule-and-scale", !ctx.Motor.Riding && ctx.Vehicles.Active == null &&
                ctx.Player.parent == originalParent && ctx.Motor.Controller.enabled && Vector3.Distance(ctx.Player.localScale, originalScale) < .001f);
            var from = ctx.Player.position; ctx.Input.TouchMove = Vector2.up;
            yield return new WaitForSeconds(.35f); ctx.Input.TouchMove = Vector2.zero;
            CheckResult(car.Data.model + "-walks-after-exit", Vector3.Distance(from, ctx.Player.position) > .25f);
        }
        IEnumerator Cabin(string model, int side)
        {
            var data = ctx.Data.vehicles.First(v => v.kind == "car" && v.model == model);
            car = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == data.id) ?? ctx.Vehicles.Spawn(data, new Vector3(0, 200.1f, 0), Quaternion.identity);
            PlaceCar(); yield return new WaitForSeconds(.3f); yield return Enter(side);
            if (ctx.Vehicles.Active != car) yield break;
            var rig = ctx.Actors.PlayerRig;
            CheckResult(model + "-driver-inherits-vehicle-render-pose", ctx.Player.parent == car.Root && !ctx.Motor.Controller.enabled);
            var wheel = car.Steering; var before = wheel.localPosition; CitySteeringWheel.Fit(car.Data, wheel);
            CheckResult(model + "-wheel-fit-is-idempotent", Vector3.Distance(before, wheel.localPosition) < .00001f);
            float rimBack = float.PositiveInfinity, rimFront = float.NegativeInfinity, rimTop = float.NegativeInfinity;
            foreach (var mesh in wheel.GetComponentsInChildren<MeshFilter>()) foreach (var vertex in mesh.sharedMesh.vertices)
            {
                var local = car.Root.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                rimBack = Mathf.Min(rimBack, local.z); rimFront = Mathf.Max(rimFront, local.z); rimTop = Mathf.Max(rimTop, local.y);
            }
            CheckResult(model + "-rim-clear-of-face-and-windscreen", rimBack > car.Data.seatMount.z + .56f && rimFront < car.Data.wheelMount.z - .03f &&
                rimTop < car.Data.wheelMount.y + .20f, "back=" + rimBack + " front=" + rimFront + " top=" + rimTop);
            Capture(model + "-seated");
            float maxGap = 0, maxAngle = 0, maxHandGap = 0;
            foreach (float steering in new[] { 0f, .65f, -.65f })
            {
                PlaceCar(28); ctx.Input.TouchMove = new Vector2(steering, 1);
                float until = Time.time + 1.1f;
                while (Time.time < until)
                {
                    yield return Frame();
                    maxGap = Mathf.Max(maxGap, Vector3.Distance(car.Root.InverseTransformPoint(ctx.Player.position), car.Data.seatMount));
                    maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(ctx.Player.rotation, car.Root.rotation));
                    for (int i = 0; i < 2; i++) maxHandGap = Mathf.Max(maxHandGap, Vector3.Distance(rig.Hands[i].position, car.Hands[i].position));
                }
                Capture(model + (steering > 0 ? "-right" : steering < 0 ? "-left" : "-moving"));
            }
            CheckResult(model + "-driver-stays-inside-at-speed-and-in-turns", maxGap < .003f && maxAngle < .1f, "seatGap=" + maxGap + " angle=" + maxAngle);
            CheckResult(model + "-both-hands-follow-the-wheel", maxHandGap < .13f, "maxGap=" + maxHandGap);
            PlaceCar(); yield return new WaitForSeconds(.2f);
            var frozen = ctx.Player.position; var clock = ctx.Clock;
            ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.2f);
            CheckResult(model + "-Esc-freezes-driver-and-vehicle", ctx.Clock == clock && Vector3.Distance(frozen, ctx.Player.position) < .001f);
            ctx.UI.SetSettings(false); yield return Exit();
            // The next model uses this lane too; keep previous test vehicles
            // out of its doorway and physical footprint.
            car.Body.position = new Vector3(55, 200.1f, side * 40 + car.Data.width * 3);
            car.Root.position = car.Body.position; car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
        CityResidents.Resident Person(int index, Vector3 position)
        {
            var data = ctx.Data.residents.Where(p => !p.child && !p.sitting && !p.stationary && !p.performance && string.IsNullOrEmpty(p.role)).ElementAt(index);
            var p = ctx.Residents.Find(data.id);
            if (!ctx.Residents.IsSpawned(data.id)) ctx.Residents.Spawn(data.id, position, Quaternion.identity);
            ctx.Residents.PrepareBody(p); p.Controlled = true;
            p.Controller.enabled = false; p.Root.SetPositionAndRotation(position, Quaternion.identity); p.Controller.enabled = true;
            p.LastPosition = position; p.Velocity = Vector3.zero; Physics.SyncTransforms(); return p;
        }
        IEnumerator ImpactCase(string name, int index, float speed, bool crossing = false)
        {
            PlaceCar(); yield return new WaitForSeconds(.15f);
            float sign = Mathf.Sign(speed), ahead = sign * (car.Data.length * .5f + 3);
            var p = Person(index, new Vector3(crossing ? -1.1f : 0, 200.04f, ahead));
            contactTarget = p.Root; contactSpeed = -1;
            float health = ctx.Combat.HealthOf(p.Root);
            if (crossing) { moving = p; pedestrianVelocity = Vector3.right * 2; }
            PlaceCar(speed);
            float until = Time.time + 2.5f;
            while (!p.Down && Time.time < until) yield return Frame();
            moving = null; pedestrianVelocity = Vector3.zero;
            CheckResult(name + "-actual-contact-damages-and-knocks-down", p.Down && ctx.Combat.HealthOf(p.Root) < health,
                "health=" + ctx.Combat.HealthOf(p.Root) + " contactSpeed=" + contactSpeed + " car=" + car.Position + " person=" + p.Root.position);
            if (name == "slow-contact") CheckResult("slow-contact-stays-between-2-and-4-metres-per-second", contactSpeed >= 2 && contactSpeed < 4, contactSpeed.ToString());
            var body = p.Root.GetComponent<Rigidbody>();
            CheckResult(name + "-physical-body-receives-impact-momentum", body && !p.Controller.enabled && Vector3.Dot(body.linearVelocity, Vector3.forward * sign) > .5f);
            Capture(name);
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = true;
            if (name == "forward")
            {
                var position = p.Root.position; float clock = ctx.Clock;
                ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.25f);
                CheckResult("Esc-freezes-fallen-NPC", ctx.Clock == clock && Vector3.Distance(position, p.Root.position) < .001f && p.Down);
                ctx.UI.SetSettings(false);
                float contactHealth = ctx.Combat.HealthOf(p.Root); yield return new WaitForSeconds(.25f);
                CheckResult("one-impact-does-not-repeat-damage-per-body-shape", ctx.Combat.HealthOf(p.Root) == contactHealth);
            }
            // Keep the car away from the recovery capsule.
            car.Body.position += Vector3.right * 20; car.Root.position = car.Body.position;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
            until = Time.time + 9;
            while (p.Down && Time.time < until) yield return Frame();
            CheckResult(name + "-NPC-recovers-with-controller", !p.Down && p.Controller.enabled && !p.Root.GetComponent<Rigidbody>() && Visible(p.Rig.Head));
            p.Controller.enabled = false; p.Root.position = new Vector3(35 + index, 200.04f, 60); p.Controller.enabled = true;
        }
        IEnumerator NonContacts()
        {
            PlaceCar(); yield return new WaitForSeconds(.15f);
            var p = Person(5, new Vector3(car.Data.width * .5f + .9f, 200.04f, car.Data.length * .5f + 2));
            float health = ctx.Combat.HealthOf(p.Root); PlaceCar(18); ctx.Input.TouchMove = Vector2.up;
            yield return new WaitForSeconds(.9f);
            CheckResult("near-miss-does-not-damage-NPC", !p.Down && ctx.Combat.HealthOf(p.Root) == health);
            PlaceCar(); p = Person(5, new Vector3(0, 200.04f, car.Data.length * .5f + .36f));
            yield return new WaitForSeconds(.5f);
            CheckResult("parked-car-does-not-knock-down-a-touching-NPC", !p.Down && ctx.Combat.HealthOf(p.Root) == health);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.SetPositionAndRotation(new Vector3(0, 201.5f, 3.2f), Quaternion.identity); wall.transform.localScale = new Vector3(8, 3, .4f);
            p = Person(5, new Vector3(0, 200.04f, 4.7f)); PlaceCar(25); ctx.Input.TouchMove = Vector2.up;
            yield return new WaitForSeconds(.6f);
            CheckResult("solid-wall-prevents-predicted-actor-damage", !p.Down && ctx.Combat.HealthOf(p.Root) == health && car.Position.z < 1.1f);
            Destroy(wall); yield return Frame();
            p.Controller.enabled = false; p.Root.position = new Vector3(45, 200.04f, 60); p.Controller.enabled = true;
        }
        IEnumerator MultipleContacts()
        {
            PlaceCar(); yield return new WaitForSeconds(.2f); float z = car.Data.length * .5f + 2;
            var left = Person(6, new Vector3(-.62f, 200.04f, z)); var right = Person(7, new Vector3(.62f, 200.04f, z));
            PlaceCar(15); ctx.Input.TouchMove = Vector2.up; float until = Time.time + 2;
            while ((!left.Down || !right.Down) && Time.time < until) yield return Frame();
            CheckResult("same-step-contact-hits-both-NPCs", left.Down && right.Down);
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = true;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = !string.IsNullOrEmpty(OutputDirectory) ? OutputDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("../Reports/driver-contact/editor");
            Directory.CreateDirectory(dir);
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; report.unity = Application.unityVersion; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            ctx.Vehicles.Impact += Contact;
            ctx.Population.enabled = ctx.Residents.enabled = ctx.Follow.enabled = ctx.Police.enabled = false;
            ctx.Combat.Holster(); ctx.UI.SetSettings(false); originalParent = ctx.Player.parent; originalScale = ctx.Player.localScale;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Driver contact QA platform"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(160, 2, 650);
            int side = -1;
            foreach (var model in new[] { "sedã", "compacto", "perua", "picape", "furgão" }) { yield return Cabin(model, side); side = -side; }
            var data = ctx.Data.vehicles.First(v => v.kind == "car" && v.model == "sedã");
            car = ctx.Vehicles.Fleet.First(v => v.Data.id == data.id); PlaceCar(); yield return new WaitForSeconds(.25f); yield return Enter(-1);
            if (ctx.Vehicles.Active == car)
            {
                // Crime reports require an actual witness. The isolated
                // platform intentionally has no ambient population.
                var witness = Person(8, new Vector3(3, 200.04f, 6)); witness.Controlled = false;
                witness.Root.rotation = Quaternion.LookRotation(new Vector3(-3, 0, -1));
                yield return ImpactCase("forward", 0, 12);
                CheckResult("player-run-over-reports-to-police", ctx.Police.Level > 0);
                witness.Controller.enabled = false; witness.Root.position = new Vector3(60, 200.04f, 60); witness.Controller.enabled = true;
                yield return ImpactCase("slow-contact", 1, 2.8f);
                yield return ImpactCase("fast-contact", 2, 35);
                yield return ImpactCase("reverse", 3, -6);
                yield return ImpactCase("crossing", 4, 10, true);
                yield return NonContacts(); yield return MultipleContacts();
            }
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors)); Finish();
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = ctx.Camera; camera.transform.position = car.Root.TransformPoint(new Vector3(-3, 2.7f, 3.3f));
            camera.transform.LookAt(car.Root.TransformPoint(new Vector3(-.2f, 1.1f, .1f)));
            var previous = camera.targetTexture; var active = RenderTexture.active; var target = RenderTexture.GetTemporary(1280, 800, 24); Texture2D picture = null;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(1280, 800, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture); }
        }
        void Finish()
        {
            finished = true; moving = null;
            if (ctx) { ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = false; ctx.UI.SetSettings(false); }
            report.passed &= report.errors.Count == 0;
            File.WriteAllText(Path.Combine(dir, "driver-contact-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("DRIVER_CONTACT_QA_DONE " + report.passed);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1); else if (ctx) ctx.Pause(true);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (ctx && ctx.Vehicles) ctx.Vehicles.Impact -= Contact; }
    }
}
