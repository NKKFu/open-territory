using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Installed only by --city-melee-qa. Uses the real controller, rigs,
    // colliders and sampled input, with the existing isolated QA save directory.
    [DefaultExecutionOrder(-900)]
    public sealed class CityMeleeProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        CityContext ctx; CityResidents.Resident person, second; GameObject floor, wall;
        string directory; bool holdFire;
        readonly Vector3 origin = new Vector3(0, 200.04f, 0);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-melee-qa") >= 0) new GameObject("Melee QA opt-in").AddComponent<CityMeleeProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + "\n" + stack); }
        void Update() { if (holdFire && ctx) ctx.Input.FireHeld = true; }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("MELEE_QA " + name + " " + passed + " " + detail); }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); yield return Frame(); }
        IEnumerator Press() { ctx.Input.TouchFire = true; yield return Frame(); }
        void Place(CityResidents.Resident p, Vector3 point)
        {
            p.Controller.enabled = false; p.Root.SetPositionAndRotation(point, Quaternion.Euler(0, 180, 0)); p.Controller.enabled = true;
            p.LastPosition = point; p.Velocity = Vector3.zero; p.MoveSpeed = 0; p.Controlled = true;
            p.TalkUntil = p.FearUntil = p.DodgeUntil = 0; p.Crossing = false;
        }
        IEnumerator Reset(float z = 1.1f, float x = 0)
        {
            ctx.Input.TouchAim = false; holdFire = false; ctx.Combat.Holster(); ctx.Police.Clear();
            ctx.Motor.SetInputLock(false); ctx.Motor.SetRemote(false); ctx.Motor.SetSeat(null); ctx.Motor.SetRide(null);
            ctx.Combat.HealAtClinic(); ctx.Motor.Warp(origin); ctx.Player.rotation = Quaternion.identity;
            ctx.Camera.transform.SetPositionAndRotation(origin + new Vector3(.98f, 2.80f, -2.47f), Quaternion.Euler(18, 0, 0));
            ctx.Residents.Interrupt(person); ctx.Combat.ForgetTemporaryActor(person.Root); person.Health = 100;
            Place(person, origin + new Vector3(x, 0, z)); Place(second, origin + new Vector3(5, 0, 4));
            Physics.SyncTransforms(); yield return Wait(.85f);
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var target = RenderTexture.GetTemporary(1280, 800, 24); var old = ctx.Camera.targetTexture; var active = RenderTexture.active;
            Texture2D picture = null;
            try
            {
                ctx.Camera.targetTexture = target; ctx.Camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(1280, 800, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), picture.EncodeToPNG());
            }
            finally { ctx.Camera.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture); }
        }
        IEnumerator Start()
        {
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/melee/native"));
            Directory.CreateDirectory(directory);
            ctx.Follow.enabled = ctx.Population.enabled = ctx.Police.enabled = false;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA isolated melee floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = origin - Vector3.up * 1.04f; floor.transform.localScale = new Vector3(40, 2, 40);
            ctx.Motor.Warp(origin);
            var candidates = ctx.Data.residents.Where(p => !p.child && !p.sitting && !p.stationary && !p.performance && p.role != "police" && !ctx.Residents.IsSpawned(p.id)).Take(2).ToArray();
            foreach (var data in candidates) ctx.Residents.Spawn(data.id, origin + Vector3.forward * 4, Quaternion.identity);
            if (candidates.Length < 2) { CheckResult("two-real-resident-rigs-available", false); Finish(); yield break; }
            person = ctx.Residents.Find(candidates[0].id); second = ctx.Residents.Find(candidates[1].id);
            CheckResult("two-real-resident-rigs-available", person.Rig != null && second.Rig != null);
            CheckResult("QA-storage-is-isolated", CityCommerce.IsReviewRun);
            CheckResult("probe-is-opt-in", FindObjectsByType<CityMeleeProbe>(FindObjectsSortMode.None).Length == 1);
            yield return Reset();
            string loadout = ctx.Combat.ExportLoadoutJson(); var arm = ctx.Actors.PlayerRig.Arms[0].localRotation;
            int hits = ctx.Combat.MeleeHits;
            yield return Press(); yield return Wait(.045f);
            CheckResult("press-starts-punch-without-firearm", ctx.Combat.Punching && ctx.Combat.Equipped == null && !ctx.Combat.Aiming);
            CheckResult("windup-does-not-deal-early-damage", ctx.Combat.HealthOf(person.Root) == 100);
            CheckResult("punch-articulates-real-arm", Quaternion.Angle(arm, ctx.Actors.PlayerRig.Arms[0].localRotation) > 5);
            yield return Wait(.16f);
            CheckResult("contact-damages-one-resident-once", ctx.Combat.MeleeHits == hits + 1 && ctx.Combat.HealthOf(person.Root) == 76, "health=" + ctx.Combat.HealthOf(person.Root));
            CheckResult("hit-has-stagger-and-crime-consequence", ctx.Combat.MeleeActing(person.Root) && ctx.Police.Level > 0);
            Capture("01-soco-e-reacao");
            yield return Wait(.12f); CheckResult("recovery-cannot-repeat-contact", ctx.Combat.MeleeHits == hits + 1);

            yield return Reset(4);
            int swings = ctx.Combat.MeleeSwings;
            yield return Press(); holdFire = true; yield return Wait(1.1f); holdFire = false;
            CheckResult("holding-fire-never-repeats-melee", ctx.Combat.MeleeSwings == swings + 1);
            CheckResult("miss-does-not-damage-or-alert-police", ctx.Combat.HealthOf(person.Root) == 100 && ctx.Police.Level == 0);

            yield return Reset(1.0f); hits = ctx.Combat.MeleeHits; swings = ctx.Combat.MeleeSwings;
            yield return Press(); yield return Wait(.24f); yield return Press(); yield return Wait(.40f); yield return Press(); yield return Wait(.51f);
            CheckResult("three-presses-chain-three-distinct-strikes", ctx.Combat.MeleeSwings == swings + 3 && ctx.Combat.ComboStep == 3, "swings=" + (ctx.Combat.MeleeSwings - swings));
            CheckResult("three-connected-hits-knock-down-same-target", ctx.Combat.MeleeHits == hits + 3 && ctx.Combat.IsDown(person.Root), "hits=" + (ctx.Combat.MeleeHits - hits));
            CheckResult("finisher-preserves-damage-and-physical-fall", ctx.Combat.HealthOf(person.Root) == 14 && !person.Controller.enabled && person.Root.GetComponent<Rigidbody>());
            CheckResult("finisher-rejects-fourth-buffered-attack", !ctx.Combat.TryPunch());
            Capture("02-sequencia-e-queda");
            yield return Wait(.75f); float fallenHealth = ctx.Combat.HealthOf(person.Root); hits = ctx.Combat.MeleeHits;
            yield return Press(); yield return Wait(.3f);
            CheckResult("fallen-actors-take-no-extra-melee-damage", ctx.Combat.HealthOf(person.Root) == fallenHealth && ctx.Combat.MeleeHits == hits);
            yield return Wait(2.2f);
            CheckResult("knockdown-recovers-controller-and-upright-body", !ctx.Combat.IsDown(person.Root) && person.Controller.enabled && Vector3.Dot(person.Root.up, Vector3.up) > .99f);

            yield return Reset();
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA melee occlusion"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = origin + new Vector3(0, 1.3f, .57f); wall.transform.localScale = new Vector3(3, 3, .12f); Physics.SyncTransforms();
            hits = ctx.Combat.MeleeHits; yield return Press(); yield return Wait(.28f);
            CheckResult("solid-wall-stops-damage-and-target-assistance", ctx.Combat.MeleeHits == hits && ctx.Combat.HealthOf(person.Root) == 100 && ctx.Police.Level == 0);
            CheckResult("wall-impact-stays-on-near-side", ctx.Combat.LastImpact.z < origin.z + .6f);
            yield return Wait(.65f); ctx.Motor.enabled = false; wall.transform.position = origin + Vector3.up * 1.45f; Physics.SyncTransforms();
            yield return Press(); yield return Wait(.28f);
            CheckResult("cast-origin-inside-cover-cannot-bypass-wall", ctx.Combat.MeleeHits == hits && ctx.Combat.HealthOf(person.Root) == 100);
            Destroy(wall); wall = null; ctx.Motor.enabled = true;

            yield return Reset(3); hits = ctx.Combat.MeleeHits; yield return Press(); yield return Wait(.29f);
            CheckResult("out-of-range-person-is-not-hit", ctx.Combat.MeleeHits == hits);
            yield return Reset(-1.1f); yield return Press(); yield return Wait(.29f);
            CheckResult("person-behind-player-is-not-targeted", ctx.Combat.MeleeHits == hits);
            yield return Reset(0, 2); yield return Press(); yield return Wait(.29f);
            CheckResult("person-outside-forward-cone-is-not-targeted", ctx.Combat.MeleeHits == hits);
            yield return Reset(); yield return Press(); yield return Wait(.04f); Place(person, origin + Vector3.forward * 3); Physics.SyncTransforms(); yield return Wait(.23f);
            CheckResult("moving-away-during-windup-evades-contact", ctx.Combat.MeleeHits == hits);
            yield return Reset(); Place(second, origin + Vector3.forward * 1.85f); Physics.SyncTransforms(); yield return Press(); yield return Wait(.28f);
            CheckResult("one-strike-hits-nearest-person-only", ctx.Combat.HealthOf(person.Root) == 76 && ctx.Combat.HealthOf(second.Root) == 100);

            yield return Reset(); int counters = ctx.Combat.CounterSwings, blocks = ctx.Combat.BlockedMeleeHits;
            yield return Press(); yield return Wait(.28f); ctx.Input.TouchAim = true; yield return Wait(.49f);
            CheckResult("counterattack-has-visible-windup-before-damage", ctx.Combat.CounterSwings == counters + 1 && ctx.Combat.MeleeActing(person.Root) && ctx.Combat.Health == 100);
            CheckResult("unarmed-aim-holds-guard-without-gun-reticle", ctx.Combat.Guarding && !ctx.Combat.Aiming);
            Capture("03-guarda-e-contra-ataque"); yield return Wait(.35f);
            CheckResult("frontal-guard-reduces-melee-damage", ctx.Combat.BlockedMeleeHits == blocks + 1 && Mathf.Abs(ctx.Combat.Health - 97) < .1f, "health=" + ctx.Combat.Health);
            ctx.Input.TouchAim = false; yield return Wait(.1f); CheckResult("releasing-guard-lowers-arms", !ctx.Combat.Guarding);
            yield return Reset(); yield return Press(); yield return Wait(1.12f);
            CheckResult("unblocked-counterattack-deals-full-damage", Mathf.Abs(ctx.Combat.Health - 85) < .1f, "health=" + ctx.Combat.Health);
            yield return Reset(); yield return Press(); yield return Wait(.52f); ctx.Camera.transform.rotation = Quaternion.Euler(18, 180, 0); ctx.Input.TouchAim = true; yield return Wait(.60f);
            CheckResult("guard-does-not-block-attacks-from-behind", Mathf.Abs(ctx.Combat.Health - 85) < .1f, "health=" + ctx.Combat.Health);

            yield return Reset(); swings = ctx.Combat.MeleeSwings; hits = ctx.Combat.MeleeHits;
            yield return Press(); yield return Wait(.035f); ctx.UI.SetSettings(true);
            float clock = ctx.Clock; var pose = ctx.Actors.PlayerRig.Arms[0].rotation; ctx.Input.TouchFire = true; yield return Wait(.4f);
            CheckResult("Esc-freezes-clock-pose-and-contact", ctx.Clock == clock && Quaternion.Angle(pose, ctx.Actors.PlayerRig.Arms[0].rotation) < .01f && ctx.Combat.MeleeHits == hits);
            CheckResult("paused-attack-is-unavailable", !ctx.Combat.TryPunch());
            ctx.UI.SetSettings(false); yield return Wait(.5f);
            CheckResult("resume-finishes-one-strike-without-replaying-input", ctx.Combat.MeleeSwings == swings + 1 && ctx.Combat.MeleeHits == hits + 1 && Time.timeScale == 1);

            yield return Reset(4); ctx.Motor.SetRide(floor.transform); CheckResult("vehicle-blocks-melee", !ctx.Combat.TryPunch()); ctx.Motor.SetRide(null);
            ctx.Motor.SetSeat(origin + Vector3.up * .6f); CheckResult("seated-player-cannot-punch", !ctx.Combat.TryPunch()); ctx.Motor.SetSeat(null);
            ctx.Motor.SetMarineState(true, "swim", 0, 0, 0); CheckResult("swimming-blocks-melee", !ctx.Combat.TryPunch()); ctx.Motor.SetMarineState(false, "idle", 0, 0, 0);
            ctx.Motor.SetRemote(true); CheckResult("remote-control-blocks-melee", !ctx.Combat.TryPunch()); ctx.Motor.SetRemote(false);
            ctx.Motor.SetInputLock(true); CheckResult("busy-transition-blocks-melee", !ctx.Combat.TryPunch()); ctx.Motor.SetInputLock(false);
            yield return Wait(.1f); ctx.Input.TouchJump = true; yield return Wait(.20f);
            CheckResult("airborne-player-cannot-punch", !ctx.Motor.Grounded && !ctx.Combat.TryPunch()); yield return Wait(1.6f);
            ctx.Input.TouchEmoteHeld = true; yield return Wait(.35f);
            CheckResult("emoji-wheel-blocks-melee", ctx.Emotes.WheelOpen && !ctx.Combat.TryPunch()); ctx.Input.TouchEmoteHeld = false; ctx.Emotes.CancelInput(); yield return Frame();
            ctx.Input.TouchAim = true; ctx.Input.TouchMove = Vector2.right; ctx.Input.TouchRun = true; yield return Wait(.9f);
            CheckResult("guard-keeps-controllable-slow-footwork", ctx.Combat.Guarding && ctx.Motor.Speed > .5f && ctx.Motor.Speed < 1.9f, "speed=" + ctx.Motor.Speed);
            ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchRun = ctx.Input.TouchAim = false;
            yield return Reset(4);
            CheckResult("melee-does-not-change-ammunition", ctx.Combat.ExportLoadoutJson() == loadout);
            swings = ctx.Combat.MeleeSwings; ctx.Input.TouchFire = true; yield return Frame(); ctx.Input.TouchWeapon = 1; yield return Wait(.22f);
            CheckResult("selecting-weapon-cancels-melee-windup", !ctx.Combat.Punching && ctx.Combat.Equipped == "pulse" && ctx.Combat.HealthOf(person.Root) == 100);
            CheckResult("armed-player-cannot-punch", !ctx.Combat.TryPunch());
            int shots = ctx.Combat.Shots; ctx.Input.TouchAim = true; yield return Frame(); yield return Press(); yield return Wait(.1f);
            CheckResult("armed-aim-and-fire-still-work", ctx.Combat.Aiming && ctx.Combat.Shots == shots + 1 && ctx.Combat.MeleeSwings == swings + 1);
            ctx.Input.TouchAim = false; ctx.Combat.Holster(); yield return Wait(.7f);
            CheckResult("firearm-still-consumes-exactly-one-round", ctx.Combat.Ammo("pulse") == 0 && ctx.Combat.Reserve("pulse") == 8);

            // Inspect and press the same touch components used by the player.
            var view = ctx.GetComponent<CityCanvasUI>();
            view.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).First(t => t.name == "Mostrar controles de toque").isOn = true;
            yield return Frame();
            var touchButtons = view.GetComponentsInChildren<CityPointerControl>(true);
            var attack = touchButtons.First(b => b.name == "Toque 7"); var guard = touchButtons.First(b => b.name == "Toque 6");
            CheckResult("touch-labels-describe-unarmed-actions", attack.GetComponentInChildren<UnityEngine.UI.Text>().text == "Punch" && guard.GetComponentInChildren<UnityEngine.UI.Text>().text == "Block");
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerId = 91 };
            swings = ctx.Combat.MeleeSwings; attack.OnPointerDown(pointer); yield return Frame(); attack.OnPointerUp(pointer); yield return Wait(.65f);
            CheckResult("touch-press-and-release-start-exactly-one-punch", ctx.Combat.MeleeSwings == swings + 1);
            guard.OnPointerDown(pointer); yield return Wait(.1f); CheckResult("touch-guard-button-raises-guard", ctx.Combat.Guarding); guard.OnPointerUp(pointer); yield return Wait(.1f);
            CheckResult("touch-guard-release-lowers-guard", !ctx.Combat.Guarding);
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            Finish();
        }
        void Finish()
        {
            File.WriteAllText(Path.Combine(directory, "melee-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("MELEE_QA_DONE " + report.passed);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
