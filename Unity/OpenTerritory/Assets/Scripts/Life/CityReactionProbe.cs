using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit native integration session. Never installed in an ordinary game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityReactionProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public bool completed;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        readonly List<CityResidents.Resident> subjects = new List<CityResidents.Resident>();
        readonly Dictionary<CityResidents.Resident, bool> controls = new Dictionary<CityResidents.Resident, bool>();
        CityContext ctx; GameObject floor, wall; string directory, loadout, equipped;
        Vector3 savedPosition, savedEye; Quaternion savedRotation, savedView;
        bool populationEnabled, followEnabled, cleaned;
        public bool Finished { get; private set; }
        public string ReportDirectory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-reactions-qa") >= 0)
                new GameObject("QA de reações • opt-in").AddComponent<CityReactionProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; Cleanup(); }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("REACTIONS_QA " + name + " " + passed + " " + detail);
            File.WriteAllText(Path.Combine(directory, "reactions-qa.json"), JsonUtility.ToJson(report, true));
        }
        Vector3 Point(float x, float z) => new Vector3(5000 + x, 300.04f, 5000 + z);
        CityResidents.Resident Subject(Func<ResidentData, bool> predicate, float x, float z)
        {
            var data = ctx.Data.residents.FirstOrDefault(d => predicate(d) && !ctx.Residents.IsSpawned(d.id) && !subjects.Any(p => p.Data.id == d.id));
            if (data == null) throw new InvalidOperationException("No unspawned resident for reaction QA");
            ctx.Residents.Spawn(data.id, Point(x, z), Quaternion.Euler(0, 180, 0));
            var p = ctx.Residents.Find(data.id); subjects.Add(p); Place(p, x, z); return p;
        }
        void Place(CityResidents.Resident p, float x, float z, bool reset = true)
        {
            ctx.Residents.Interrupt(p); p.Controller.enabled = false; p.Root.SetPositionAndRotation(Point(x, z), Quaternion.Euler(0, 180, 0)); p.Controller.enabled = true;
            p.Controlled = false; p.LastPosition = p.Root.position; p.Velocity = p.Steering = Vector3.zero; p.MoveSpeed = 0; p.Pace = 1;
            p.Path.Clear(); p.PathIndex = 0; p.Doorway = null; p.Crossing = false; p.DodgeUntil = 0;
            p.State = "activity"; p.Activity = "rest"; p.Duration = 1000; p.Age = 0; p.NextSocial = ctx.Clock + 1000;
            p.FearUntil = 0; p.Reaction.Kind = CityResidents.ReactionKind.None; p.Reaction.Until = p.Reaction.NextScan = 0;
            p.Reaction.WeaponConcern = false; p.Reaction.PoseWeight = 0;
            if (reset) { p.Reaction.MemoryUntil = p.Reaction.NextLine = p.Reaction.NextReport = 0; }
        }
        void Only(params CityResidents.Resident[] people)
        {
            foreach (var p in subjects)
            {
                bool active = people.Contains(p); p.Controlled = !active; p.Root.gameObject.SetActive(active);
            }
        }
        void CameraAt(CityResidents.Resident p)
        { ctx.Camera.transform.position = Point(0, -3) + Vector3.up * 2; ctx.Camera.transform.LookAt(p.Root.position + Vector3.up * 1.4f); }
        IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); }
        IEnumerator Talk(CityResidents.Resident p)
        {
            CameraAt(p); ctx.Input.TouchEmoteHeld = true; yield return Wait(.06f);
            ctx.Input.TouchEmoteHeld = false; yield return Wait(.18f);
        }
        IEnumerator Holster()
        { ctx.Input.TouchAim = false; ctx.Input.TouchHolster = true; yield return Wait(2.1f); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "reactions-qa");
            Directory.CreateDirectory(directory);
            float timeout = Time.realtimeSinceStartup + 100;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance; if (!ctx || !ctx.Ready) { CheckResult("scene-ready", false); Finish(); yield break; }
            savedPosition = ctx.Player.position; savedRotation = ctx.Player.rotation; savedEye = ctx.Camera.transform.position; savedView = ctx.Camera.transform.rotation;
            loadout = ctx.Combat.ExportLoadoutJson(); equipped = ctx.Combat.Equipped; ctx.Combat.Holster();
            populationEnabled = ctx.Population.enabled; ctx.Population.enabled = false; followEnabled = ctx.Follow.enabled; ctx.Follow.enabled = false;
            foreach (var p in ctx.Residents.People) { controls[p] = p.Controlled; p.Controlled = true; }
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA reações • piso isolado"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(5000, 299, 5000); floor.transform.localScale = new Vector3(100, 2, 100);
            ctx.Motor.Warp(Point(0, 0)); ctx.Player.rotation = Quaternion.identity; ctx.Combat.Grant("sidearm");
            Func<ResidentData, bool> mobile = d => !d.stationary && !d.sitting && !d.performance && !d.child && string.IsNullOrEmpty(d.role);
            var person = Subject(d => mobile(d) && d.id % 3 == 0, 0, 2);
            var runner = Subject(d => mobile(d) && d.id % 3 != 0, 4, 5);
            var bystander = Subject(mobile, -4, 5);
            var worker = Subject(d => d.stationary && !d.sitting && !d.performance && d.role != "police", 10, 5);
            var officer = Subject(d => d.role == "police", 12, 5);
            Only(person); yield return Wait(.35f);
            CameraAt(person); ctx.RefreshAction();
            CheckResult("Q-selects-real-resident", ctx.CurrentAction?.id == "resident-" + person.Data.id, ctx.CurrentAction?.id);
            float clock = ctx.Clock; Vector3 before = person.Root.position;
            yield return Talk(person);
            CheckResult("Q-starts-world-conversation", person.Reaction.Kind == CityResidents.ReactionKind.Talking && ctx.Speech.Contains(person.Data.name), ctx.Speech);
            yield return Wait(.45f);
            CheckResult("conversation-faces-player-and-gestures", Vector3.Dot(person.Root.forward, (ctx.Player.position - person.Root.position).normalized) > .8f && person.Rig.Hands.Any(h => person.Root.InverseTransformPoint(h.position).y > 1.1f));
            CheckResult("conversation-stops-only-speaker", !ctx.Paused && ctx.Clock > clock + .3f && Vector3.Distance(before, person.Root.position) < .05f);
            Capture("01-conversa", person);
            ctx.Motor.Warp(Point(0, -6)); yield return Wait(.2f);
            CheckResult("walking-away-ends-conversation", person.TalkUntil <= ctx.Clock && person.Reaction.Kind != CityResidents.ReactionKind.Talking);
            ctx.Motor.Warp(Point(0, 0)); Place(person, 0, 2); yield return Talk(person);
            CheckResult("repeat-conversation-remembers-greeting", ctx.Speech.Contains("Hi again"), ctx.Speech);
            ctx.Input.TouchWeapon = 2; yield return Wait(.5f);
            CheckResult("drawing-weapon-interrupts-conversation", ctx.Combat.WeaponVisible && person.TalkUntil <= ctx.Clock && person.Reaction.Kind == CityResidents.ReactionKind.Cautious);
            CheckResult("armed-player-cannot-start-friendly-chat", !ctx.Residents.AvailableForTalk(person));
            CheckResult("visible-weapon-does-not-invent-crime", ctx.Police.Stage == "livre", ctx.Police.Stage);
            yield return Holster();
            CheckResult("X-holsters-and-reassures-NPC", !ctx.Combat.WeaponVisible && person.Reaction.Kind == CityResidents.ReactionKind.None && ctx.Residents.AvailableForTalk(person));
            Place(person, 0, 5); CameraAt(person); ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.65f);
            CheckResult("aim-ray-identifies-real-NPC", ctx.Combat.AimTarget == person, ctx.Combat.AimTarget?.Data.name);
            CheckResult("aimed-at-NPC-raises-both-hands", person.Reaction.Kind == CityResidents.ReactionKind.Surrender && person.Rig.Hands.All(h => person.Root.InverseTransformPoint(h.position).y > 1.8f), person.Reaction.Kind.ToString());
            Capture("02-maos-ao-alto", person);
            clock = ctx.Clock; before = person.Root.position; Quaternion hand = person.Rig.Arms[0].localRotation;
            ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.4f);
            CheckResult("Esc-freezes-reaction-movement-and-pose", ctx.Clock == clock && before == person.Root.position && Quaternion.Angle(hand, person.Rig.Arms[0].localRotation) < .01f);
            ctx.UI.SetSettings(false); yield return Holster();
            Place(runner, 0, 5); Only(runner); CameraAt(runner); ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.6f);
            float fleeDeadline = ctx.Clock + 3;
            while (runner.Reaction.Kind != CityResidents.ReactionKind.Flee && ctx.Clock < fleeDeadline) yield return null;
            before = runner.Root.position; yield return Wait(1.2f);
            CheckResult("other-personality-flees-after-startle", runner.Reaction.Kind == CityResidents.ReactionKind.Flee && CityPhysics.Flat(runner.Root.position - before).magnitude > .7f, runner.Reaction.Kind + " moved=" + Vector3.Distance(before, runner.Root.position));
            CheckResult("fleeing-keeps-physical-ground-support", Mathf.Abs(runner.Root.position.y - 300.04f) < .12f);
            Capture("03-fuga", runner); yield return Holster();
            Place(person, 0, 6); Only(person);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA obstrução de percepção"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(5000, 302, 5003); wall.transform.localScale = new Vector3(12, 4, .4f);
            CameraAt(person); ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.6f);
            CheckResult("walls-block-weapon-perception-and-aim", person.Reaction.Kind == CityResidents.ReactionKind.None && ctx.Combat.AimTarget == null);
            ctx.Input.TouchAim = false; ctx.Combat.Holster();
            bool reported = ctx.Police.Report("furto", ctx.Player.position);
            CheckResult("unseen-quiet-crime-has-no-omniscient-witness", !reported && person.Reaction.Kind == CityResidents.ReactionKind.None);
            ctx.Residents.ReactToCrime("disparo", ctx.Player.position, null, true); yield return Wait(.3f);
            CheckResult("nearby-hidden-shot-is-heard-without-identification", person.Reaction.Kind == CityResidents.ReactionKind.Flee && person.Reaction.MemoryUntil <= ctx.Clock);
            Destroy(wall); wall = null; yield return Wait(.1f);
            Place(person, 0, 30); CameraAt(person); ctx.Input.TouchWeapon = 2; yield return Wait(.4f);
            CheckResult("weapon-perception-respects-distance", person.Reaction.Kind == CityResidents.ReactionKind.None);
            yield return Holster();
            Place(person, 0, 5); Place(runner, 4, 5); Place(bystander, -4, 5); Only(person, runner, bystander);
            ctx.Camera.transform.position = Point(0, -3) + Vector3.up * 2; ctx.Camera.transform.LookAt(Point(30, 5) + Vector3.up * 1.6f);
            ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.3f); int shots = ctx.Combat.Shots;
            ctx.Input.TouchFire = true; yield return Wait(.4f);
            CheckResult("actual-fire-input-produces-shot", ctx.Combat.Shots == shots + 1);
            CheckResult("crime-selects-only-one-phone-witness", ctx.Police.Calling && subjects.Count(p => p.Calling) == 1, ctx.Police.Stage);
            var caller = subjects.FirstOrDefault(p => p.Calling);
            CheckResult("caller-keeps-phone-to-ear", caller != null && caller.Phone && caller.Phone.gameObject.activeSelf && caller.Reaction.Kind != CityResidents.ReactionKind.Talking);
            CheckResult("gunshot-reacts-across-nearby-crowd", new[] { person, runner, bystander }.All(p => p.Calling || p.Reaction.Kind == CityResidents.ReactionKind.Flee));
            CheckResult("witness-dialogue-survives-ammo-message", ctx.Speech.Contains("Police?"), ctx.Speech);
            var scared = new[] { person, runner, bystander }.First(p => !p.Calling);
            CheckResult("visible-crime-leaves-short-term-memory", scared.Reaction.MemoryUntil > ctx.Clock + 30);
            ctx.Police.Clear(); yield return Holster(); yield return Wait(6.5f);
            Place(scared, 0, 2, false); Only(scared); yield return Talk(scared);
            CheckResult("later-chat-reflects-witnessed-crime", ctx.Speech.Contains("I'm still shaken"), ctx.Speech);
            ctx.Residents.Interrupt(scared);
            Place(person, 0, 5); Place(runner, 4, 5); Only(person, runner);
            ctx.Police.Report("furto", ctx.Player.position);
            CheckResult("theft-prompts-protest-and-report", ctx.Police.Calling && new[] { person, runner }.Any(p => !p.Calling && p.Reaction.Kind == CityResidents.ReactionKind.Cautious));
            ctx.Police.Clear(); Place(person, 0, 5); Only(person);
            ctx.Residents.ReactToCrime("dano", ctx.Player.position, null, true);
            CheckResult("property-damage-provokes-protest", person.Reaction.Kind == CityResidents.ReactionKind.Cautious);
            Place(person, 0, 5); ctx.Residents.ReactToCrime("atropelamento", ctx.Player.position, null, true);
            CheckResult("hit-and-run-causes-alarm", person.Reaction.Kind == CityResidents.ReactionKind.Flee);
            Place(worker, 0, 5); Only(worker); CameraAt(worker); before = worker.Root.position;
            ctx.Input.TouchWeapon = 2; ctx.Input.TouchAim = true; yield return Wait(.7f);
            CheckResult("stationary-worker-cowers-without-sliding", Unsafe(worker) && CityPhysics.Flat(worker.Root.position - before).magnitude < .05f && worker.Rig.Hands.All(h => worker.Root.InverseTransformPoint(h.position).y > 1.8f), worker.Reaction.Kind.ToString());
            yield return Holster(); Place(officer, 0, 5); Only(officer); CameraAt(officer);
            ctx.Input.TouchWeapon = 2; yield return Wait(.5f);
            CheckResult("officer-warns-about-drawn-weapon", officer.Reaction.Kind == CityResidents.ReactionKind.Cautious && ctx.Police.Stage == "livre");
            ctx.Input.TouchAim = true; yield return Wait(.6f);
            CheckResult("threatening-officer-triggers-police-response", ctx.Police.Level >= 1 && ctx.Police.Stage != "livre" && officer.Reaction.Kind != CityResidents.ReactionKind.Flee, ctx.Police.Stage);
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            Finish();
        }
        bool Unsafe(CityResidents.Resident p) => p.Reaction.Kind == CityResidents.ReactionKind.Surrender || p.Reaction.Kind == CityResidents.ReactionKind.Flee;
        void Capture(string name, CityResidents.Resident p)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = ctx.Camera; Vector3 eye = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            camera.transform.position = p.Root.position + new Vector3(4, 2.8f, -5); camera.transform.LookAt(p.Root.position + Vector3.up * 1.2f);
            var previous = RenderTexture.active; var target = RenderTexture.GetTemporary(1200, 800, 24); var oldTarget = camera.targetTexture;
            var texture = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(texture); camera.transform.SetPositionAndRotation(eye, rotation); }
        }
        void Finish()
        {
            Finished = report.completed = true; File.WriteAllText(Path.Combine(directory, "reactions-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("REACTIONS_QA_FINISHED " + report.passed + " checks=" + report.checks.Count); Cleanup(); enabled = false;
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
        void Cleanup()
        {
            if (cleaned || !ctx || string.IsNullOrEmpty(loadout)) return; cleaned = true;
            ctx.Input.TouchAim = ctx.Input.TouchFire = ctx.Input.TouchEmoteHeld = false; ctx.Input.TouchMove = Vector2.zero;
            ctx.UI.SetSettings(false); ctx.Police.Clear();
            foreach (var p in subjects) if (p.Root) ctx.Residents.Despawn(p);
            foreach (var pair in controls) if (pair.Key.Root) pair.Key.Controlled = pair.Value;
            ctx.Combat.RestoreLoadoutJson(loadout); if (!string.IsNullOrEmpty(equipped)) ctx.Combat.Select(equipped);
            ctx.Motor.Warp(savedPosition); ctx.Player.rotation = savedRotation; ctx.Population.enabled = populationEnabled; ctx.Follow.enabled = followEnabled;
            ctx.Camera.transform.SetPositionAndRotation(savedEye, savedView); ctx.Follow.ResetTracking();
            if (floor) Destroy(floor); if (wall) Destroy(wall);
        }
    }
}
