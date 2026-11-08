using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OpenTerritory
{
    // Native, opt-in validation of the visible rig, not a parallel animation.
    [DefaultExecutionOrder(500)]
    public sealed class CityGaitProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name; public bool passed; public float value; }
        [Serializable] public class Motion
        {
            public string name;
            public int frames, leftContacts, rightContacts, sounds;
            public float seconds, speed, footError, plantedSlip, hipMin = 100, hipMax = -100, hipJump, kneeFlexion, clearance;
            public float contactsPerSecond, hipSpeed, armOpposition, anatomy, rootScale;
            public int residentId;
        }
        [Serializable] public class Report
        {
            public bool passed = true;
            public bool captures;
            public List<Check> checks = new List<Check>();
            public List<Motion> motions = new List<Motion>();
            public List<string> errors = new List<string>();
        }
        public string ReportDirectory;
        readonly Report report = new Report();
        readonly Vector3[] previousFeet = new Vector3[2];
        readonly bool[] previousStance = new bool[2];
        readonly bool[] captured = new bool[8];
        readonly StringBuilder trace = new StringBuilder();
        readonly List<CityResidents.Resident> testResidents = new List<CityResidents.Resident>();
        readonly Dictionary<Transform, Vector3> residentHomes = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Quaternion> residentRotations = new Dictionary<Transform, Quaternion>();
        CityContext ctx; GameObject floor, ramp, wall; Camera preview; Material floorMaterial;
        CityResidents.Resident measuredResident;
        CityRig Subject => measuredResident == null ? ctx.Actors.PlayerRig : measuredResident.Rig;
        int StepSounds => measuredResident == null ? ctx.Systems.Audio.PlayerSteps : ctx.Systems.Audio.ResidentSteps;
        float residentSpeed, legEnergy, armEnergy, opposition;
        Vector3 residentDirection = Vector3.forward;
        Vector3 sampleRoot;
        Vector3 saved; Quaternion savedRotation; Motion motion; string dir, loadout, equipped;
        float lastHip; bool initialized; int slipSamples, audioStart, contactStart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-gait-qa") >= 0)
                new GameObject("QA de passadas • opt-in").AddComponent<CityGaitProbe>();
        }
        void Awake() { Application.logMessageReceived += OnLog; }
        void OnLog(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void Test(string name, bool passed, float value = 0)
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value });
            Debug.Log("GAIT_QA " + name + " " + passed + " " + value);
        }
        IEnumerator Hold(Vector2 input, float seconds, bool run = false)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            { ctx.Input.TouchMove = input; ctx.Input.TouchRun = run; yield return null; }
        }
        void Update()
        {
            if (!ctx || ctx.Paused || measuredResident == null || residentSpeed <= 0) return;
            // Exercise the resident's real controller and normal animation
            // update independently of navigation changes and route selection.
            float dt = Time.deltaTime;
            measuredResident.Controller.Move(residentDirection * residentSpeed * dt + Vector3.down * (2.5f * dt));
            measuredResident.Root.rotation = Quaternion.Slerp(measuredResident.Root.rotation, Quaternion.LookRotation(residentDirection), 1 - Mathf.Exp(-5 * dt));
        }
        IEnumerator Measure(string name, Vector2 input, bool run, float warmup = .9f)
        {
            yield return Hold(input, warmup, run);
            var subject = Subject;
            motion = new Motion { name = name, anatomy = (subject.Thigh + subject.Shin) / .89f, rootScale = subject.Root.lossyScale.y, residentId = measuredResident == null ? 0 : measuredResident.Data.id };
            initialized = false; slipSamples = 0; legEnergy = armEnergy = opposition = 0;
            audioStart = StepSounds; contactStart = subject.FootContacts;
            trace.Clear(); trace.AppendLine("time,phase,hip,leftStance,rightStance,leftZ,leftY,rightZ,rightY");
            Array.Clear(captured, 0, captured.Length);
            yield return Hold(input, 3.1f, run);
            var result = motion; motion = null;
            result.speed /= Mathf.Max(1, result.frames - (measuredResident == null ? 0 : 1));
            result.plantedSlip /= Mathf.Max(1, slipSamples);
            result.contactsPerSecond = (result.leftContacts + result.rightContacts) / Mathf.Max(.001f, result.seconds);
            result.sounds = StepSounds - audioStart;
            result.armOpposition = -opposition / Mathf.Sqrt(Mathf.Max(.000001f, legEnergy * armEnergy));
            report.motions.Add(result);
            File.WriteAllText(Path.Combine(dir, name + "-trace.csv"), trace.ToString());
            Test(name + "-actual-foot-reaches-target", result.footError < .025f, result.footError);
            Test(name + "-planted-foot-stays-in-world", result.plantedSlip < .08f, result.plantedSlip);
            Test(name + "-balanced-contacts", Math.Abs(result.leftContacts - result.rightContacts) <= 1, result.leftContacts - result.rightContacts);
            Test(name + "-pelvis-remains-upright", result.hipMin > .68f * result.anatomy && result.hipMax - result.hipMin < .24f * result.anatomy, result.hipMax - result.hipMin);
            Test(name + "-pelvis-does-not-snap", result.hipSpeed < 3.0f * result.anatomy, result.hipSpeed);
            Test(name + "-bends-knees", result.kneeFlexion > (run ? 65 : 22), result.kneeFlexion);
            Test(name + "-sounds-match-foot-contacts", Math.Abs(result.sounds - (subject.FootContacts - contactStart)) <= 1 && result.sounds >= 2, result.sounds);
            if (name != "aim-strafe") Test(name + "-arms-oppose-legs", result.armOpposition > .35f, result.armOpposition);
            if (name == "walk" || name == "sprint")
            {
                Test(name + "-cadence", result.contactsPerSecond > (run ? 3.3f : 2.4f) && result.contactsPerSecond < (run ? 4.7f : 3.6f), result.contactsPerSecond);
                Test(name + "-speed-preserved", Mathf.Abs(result.speed - (run ? CityPlayer.SprintSpeed : CityPlayer.WalkSpeed)) < .15f, result.speed);
                if (run)
                {
                    float cycleDistance = result.speed * 2 / result.contactsPerSecond;
                    Test("sprint-faster-than-previous-version", result.speed > 8.8f, result.speed);
                    Test("sprint-travel-matches-longer-steps", cycleDistance > 3.8f && cycleDistance < 4.8f, cycleDistance);
                }
            }
            if (measuredResident != null)
            {
                Test(name + "-real-controller-speed", Mathf.Abs(result.speed - residentSpeed) < .15f, result.speed);
                Test(name + "-cadence", result.contactsPerSecond > 1.1f && result.contactsPerSecond < 4.5f, result.contactsPerSecond);
            }
        }
        void LateUpdate()
        {
            if (motion == null || !ctx || ctx.Paused) return;
            var r = Subject; float dt = Time.deltaTime;
            if (dt <= 0) return;
            float hip = r.Hips.localPosition.y;
            if (initialized)
            {
                motion.hipJump = Mathf.Max(motion.hipJump, Mathf.Abs(hip - lastHip));
                motion.hipSpeed = Mathf.Max(motion.hipSpeed, Mathf.Abs(hip - lastHip) / dt);
            }
            lastHip = hip; motion.frames++; motion.seconds += dt;
            if (measuredResident == null) motion.speed += ctx.Motor.Speed;
            else if (initialized) motion.speed += CityPhysics.Flat(r.Root.position - sampleRoot).magnitude / dt;
            sampleRoot = r.Root.position;
            float legs = r.Root.InverseTransformPoint(r.Feet[0].position).z - r.Root.InverseTransformPoint(r.Feet[1].position).z;
            float arms = r.Root.InverseTransformPoint(r.Hands[0].position).z - r.Root.InverseTransformPoint(r.Hands[1].position).z;
            opposition += legs * arms; legEnergy += legs * legs; armEnergy += arms * arms;
            if (motion.seconds < 1.3f)
            {
                Vector3 left = r.Root.InverseTransformPoint(r.Feet[0].position), right = r.Root.InverseTransformPoint(r.Feet[1].position);
                trace.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4},{3},{4},{5:F4},{6:F4},{7:F4},{8:F4}", motion.seconds, r.Phase, hip, r.Stance[0], r.Stance[1], left.z, left.y, right.z, right.y));
            }
            motion.hipMin = Mathf.Min(motion.hipMin, hip); motion.hipMax = Mathf.Max(motion.hipMax, hip);
            for (int i = 0; i < 2; i++)
            {
                Vector3 foot = r.Feet[i].position;
                motion.footError = Mathf.Max(motion.footError, Vector3.Distance(foot, r.FootPoint[i]));
                motion.clearance = Mathf.Max(motion.clearance, r.Root.InverseTransformPoint(foot).y - r.Ankle);
                motion.kneeFlexion = Mathf.Max(motion.kneeFlexion, 180 - Vector3.Angle(r.Legs[i].position - r.Knees[i].position, foot - r.Knees[i].position));
                if (initialized && r.Stance[i] && previousStance[i])
                {
                    motion.plantedSlip += CityPhysics.Flat(foot - previousFeet[i]).magnitude / dt; slipSamples++;
                }
                if (initialized && r.Stance[i] && !previousStance[i])
                { if (i == 0) motion.leftContacts++; else motion.rightContacts++; }
                previousFeet[i] = foot; previousStance[i] = r.Stance[i];
            }
            initialized = true;
            if (motion.name == "walk" || motion.name == "sprint" || motion.name.StartsWith("npc-"))
            {
                int phase = Mathf.FloorToInt(Mathf.Repeat(r.Phase / (Mathf.PI * 2), 1) * 8);
                if (!captured[phase]) { captured[phase] = true; Capture(motion.name + "-" + phase); }
            }
        }
        void Capture(string name)
        {
            if (!preview || !report.captures) return;
            var root = Subject.Root;
            preview.transform.position = root.position + root.right * 4.4f + root.up * 1.35f - root.forward * 1.25f;
            preview.transform.LookAt(root.position + Vector3.up * 1.1f);
            var target = RenderTexture.GetTemporary(640, 480, 24, RenderTextureFormat.ARGB32);
            var active = RenderTexture.active;
            try
            {
                preview.targetTexture = target; preview.Render(); RenderTexture.active = target;
                var texture = new Texture2D(640, 480, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG()); Destroy(texture);
            }
            finally { preview.targetTexture = null; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); }
        }
        CityResidents.Resident PrepareResident(bool tallest, bool seated)
        {
            ResidentData chosen = null; float size = tallest ? -1 : float.MaxValue;
            foreach (var data in ctx.Data.residents)
            {
                var candidate = ctx.Residents.Find(data.id); var root = ctx.Node(data.id);
                if (data.rig == null || !root || candidate == null || data.sitting != seated || ctx.Residents.IsSpawned(data.id) || testResidents.Contains(candidate)) continue;
                if (!seated && (data.stationary || data.performance)) continue;
                float height = root.lossyScale.y;
                if (tallest ? height > size : height < size) { chosen = data; size = height; }
            }
            if (chosen == null) return null;
            var node = ctx.Node(chosen.id); residentHomes[node] = node.position; residentRotations[node] = node.rotation;
            if (!ctx.Residents.Spawn(chosen.id, new Vector3(8, 200.04f, -10), Quaternion.identity)) return null;
            var person = ctx.Residents.Find(chosen.id); ctx.Residents.Control(person, true);
            testResidents.Add(person); measuredResident = person; residentSpeed = 0;
            return person;
        }
        IEnumerator MeasureResident(string name, float speed, Vector3 direction, float warmup = .9f)
        {
            ctx.Motor.Warp(measuredResident.Root.position + Vector3.left * 7);
            residentDirection = direction; residentSpeed = speed;
            yield return Measure(name, Vector2.zero, speed > 2, warmup);
            residentSpeed = 0;
            yield return Hold(Vector2.zero, .35f);
        }
        IEnumerator CheckResidents()
        {
            for (int size = 0; size < 2; size++)
            {
                var person = PrepareResident(size == 1, false);
                Test("npc-" + size + "-real-rig-spawned", person != null && person.Rig != null && person.Rig.Valid);
                if (person == null || person.Rig == null) continue;
                ctx.Motor.Warp(person.Root.position + Vector3.left * 7);
                yield return Hold(Vector2.zero, .5f);
                if (size == 0) yield return MeasureResident("npc-small-slow-walk", .65f, Vector3.forward);
                yield return MeasureResident(size == 0 ? "npc-small-walk" : "npc-tall-walk", 1.15f, Vector3.forward);
                yield return MeasureResident(size == 0 ? "npc-small-jog" : "npc-tall-jog", 2.8f, Vector3.forward);
                if (size == 0)
                {
                    // Include the reversal itself, before the body has settled.
                    residentSpeed = 2.8f; yield return Hold(Vector2.zero, .4f);
                    yield return MeasureResident("npc-run-turn", 3.1f, Vector3.right, 0);
                }
                float phase = person.Rig.Phase;
                yield return Hold(Vector2.zero, .35f);
                Test("npc-" + size + "-idle-stops-gait", Mathf.Abs(person.Rig.Phase - phase) < .0001f);
                residentSpeed = 2.8f; yield return Hold(Vector2.zero, .35f);
                ctx.UI.SetSettings(true); phase = person.Rig.Phase; Vector3 hip = person.Rig.Hips.position;
                yield return Hold(Vector2.zero, .3f);
                Test("npc-" + size + "-settings-freeze-animation", person.Rig.Phase == phase && (person.Rig.Hips.position - hip).sqrMagnitude < .000001f);
                ctx.UI.SetSettings(false); residentSpeed = 0;
                ctx.Residents.Despawn(person); person.Controlled = false; measuredResident = null;
            }
            for (int kind = 0; kind < 2; kind++)
            {
                var person = PrepareResident(kind == 1, true);
                Test("npc-seated-" + kind + "-real-rig-spawned", person != null && person.Rig != null && person.Rig.Valid);
                if (person == null || person.Rig == null) continue;
                yield return Hold(Vector2.zero, .5f);
                var r = person.Rig;
                Test("npc-seated-" + kind + "-pose-preserved", !r.GroundGait && Mathf.Abs(r.Hips.localPosition.y - r.HipHeight) < .04f && float.IsFinite(r.Head.position.y), r.Hips.localPosition.y);
                Capture("npc-seated-" + kind);
                ctx.Residents.Despawn(person); person.Controlled = false; measuredResident = null;
            }
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/locomotion/gait"));
            Directory.CreateDirectory(dir);
            report.captures = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            float timeout = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            saved = ctx.Player.position; savedRotation = ctx.Player.rotation;
            loadout = ctx.Combat.ExportLoadoutJson(); equipped = ctx.Combat.Equipped; ctx.Combat.Holster();
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA gait floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(180, 2, 180);
            floorMaterial = new Material(Shader.Find("Standard")); floorMaterial.color = new Color(.34f, .37f, .34f); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            preview = new GameObject("QA gait camera").AddComponent<Camera>(); preview.enabled = false;
            preview.orthographic = true; preview.orthographicSize = 1.4f; preview.nearClipPlane = .1f; preview.farClipPlane = 40;
            preview.clearFlags = CameraClearFlags.SolidColor; preview.backgroundColor = new Color(.49f, .62f, .65f);
            // The visual children retain the imported Default layer.
            preview.cullingMask = ~0;
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); ctx.Player.rotation = Quaternion.identity; ctx.Follow.Face(0);
            yield return Hold(Vector2.zero, .7f);
            yield return Measure("walk", Vector2.up, false);
            yield return Measure("sprint", Vector2.up, true);
            yield return Measure("sprint-turn", Vector2.right, true, 0);
            yield return Hold(Vector2.down, .7f, true);
            yield return Hold(Vector2.zero, .6f);
            var rig = ctx.Actors.PlayerRig; float phase = rig.Phase; Vector3 idleHips = rig.Hips.position;
            yield return Hold(Vector2.zero, .3f);
            Test("idle-stops-gait", Mathf.Abs(rig.Phase - phase) < .0001f && ctx.Motor.Speed < .05f, ctx.Motor.Speed);
            ctx.Input.TouchJump = true; yield return Hold(Vector2.up, .13f); Test("jump-preserved", !ctx.Motor.Grounded);
            yield return Hold(Vector2.zero, .9f); Test("landing-preserved", ctx.Motor.Grounded && rig.Valid);
            ctx.Combat.Select("pulse"); ctx.Input.TouchAim = true;
            yield return Measure("aim-strafe", Vector2.right, false);
            ctx.Input.TouchAim = false; ctx.Combat.Holster(); yield return Hold(Vector2.zero, .4f);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); ctx.Follow.Face(0); yield return Hold(Vector2.zero, .5f);
            var forward = CityPhysics.Flat(ctx.Camera.transform.forward).normalized;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.position = ctx.Player.position + forward * 2 + Vector3.up * 1.5f; wall.transform.localScale = new Vector3(8, 3, .3f);
            wall.transform.rotation = Quaternion.LookRotation(forward);
            yield return Hold(Vector2.up, 1.5f, true); phase = rig.Phase; yield return Hold(Vector2.up, .4f, true);
            Test("blocked-does-not-run-in-place", ctx.Motor.Speed < .05f && Mathf.Abs(rig.Phase - phase) < .002f, ctx.Motor.Speed); Destroy(wall);
            ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.layer = CityContext.GroundLayer;
            ramp.transform.position = new Vector3(40, 200, 0); ramp.transform.localScale = new Vector3(12, .4f, 25); ramp.transform.rotation = Quaternion.Euler(-8, 0, 0);
            ctx.Motor.Warp(new Vector3(40, 200.4f, -1)); ctx.Follow.Face(0); yield return Hold(Vector2.zero, .5f);
            yield return Measure("slope-walk", Vector2.up, false);
            yield return Hold(Vector2.zero, .5f); ctx.UI.SetSettings(true);
            phase = rig.Phase; idleHips = rig.Hips.position; yield return Hold(Vector2.up, .35f, true);
            Test("settings-freeze-animation", rig.Phase == phase && (rig.Hips.position - idleHips).sqrMagnitude < .000001f);
            ctx.UI.SetSettings(false); ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchRun = false;
            yield return CheckResidents();
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count);
            Finish();
        }
        void Finish()
        {
            File.WriteAllText(Path.Combine(dir, "gait-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("GAIT_QA_DONE " + report.passed);
            if (!Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), "--city-gait-qa") >= 0) Application.Quit(report.passed ? 0 : 1);
            else Destroy(gameObject);
        }
        bool quitting;
        void OnApplicationQuit() { quitting = true; }
        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (quitting) return;
            if (ctx && !string.IsNullOrEmpty(loadout))
            {
                residentSpeed = 0;
                foreach (var person in testResidents)
                {
                    if (!person.Root) continue;
                    ctx.Residents.Despawn(person); person.Controlled = false;
                    person.Root.SetPositionAndRotation(residentHomes[person.Root], residentRotations[person.Root]);
                }
                ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchRun = ctx.Input.TouchAim = false; ctx.UI.SetSettings(false);
                ctx.Combat.RestoreLoadoutJson(loadout); if (!string.IsNullOrEmpty(equipped)) ctx.Combat.Select(equipped);
                ctx.Motor.Warp(saved); ctx.Player.rotation = savedRotation;
            }
            if (floor) Destroy(floor); if (ramp) Destroy(ramp); if (wall) Destroy(wall); if (preview) Destroy(preview.gameObject); if (floorMaterial) Destroy(floorMaterial);
        }
    }
}
