using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in player test. It exercises the imported colliders, real controllers,
    // weapon raycasts and the same touch input sampled by the desktop controls.
    public sealed class CityLifeProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] sealed class Report { public string suite = "native-life", unity, startedUtc; public bool passed; public float seconds; public int residents, animals; public List<Check> checks = new List<Check>(); }
        sealed class SavedPerson { public CityResidents.Resident Person; public Vector3 Position; public Quaternion Rotation; public bool Controlled; }
        CityContext ctx; readonly Report report = new Report(); readonly List<SavedPerson> saved = new List<SavedPerson>();
        Vector3 playerHome; Quaternion playerRotation; string inventory; float began;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-life-qa") >= 0) new GameObject("Verificação nativa de moradores e combate").AddComponent<CityLifeProbe>(); }
        void CheckResult(string name, bool pass, string detail)
        { report.checks.Add(new Check { name = name, passed = pass, detail = detail }); Debug.Log("[CityLifeQA] " + (pass ? "PASS " : "FAIL ") + name + " · " + detail); }
        IEnumerator Start()
        {
            began = Time.realtimeSinceStartup; report.unity = Application.unityVersion; report.startedUtc = DateTime.UtcNow.ToString("o");
            IEnumerator run = Run();
            while (true)
            {
                object instruction = null; bool finished = false;
                try { if (!run.MoveNext()) finished = true; else instruction = run.Current; }
                catch (Exception error) { CheckResult("unexpected-exception", false, error.ToString()); finished = true; }
                if (finished) break; yield return instruction;
            }
            if (ctx)
            {
                ctx.Input.TouchAim = ctx.Input.TouchHack = false; ctx.Input.TouchMove = Vector2.zero; ctx.Combat.Holster(); ctx.Police.Clear();
                if (!string.IsNullOrEmpty(inventory)) ctx.Combat.RestoreLoadoutJson(inventory);
                foreach (var p in saved) if (p.Person.Root && !p.Person.Down) { Place(p.Person, p.Position, p.Rotation); ctx.Residents.Control(p.Person, p.Controlled); }
                if (!ctx.Combat.Down) { ctx.Motor.Warp(playerHome); ctx.Player.rotation = playerRotation; }
            }
            report.seconds = Time.realtimeSinceStartup - began; report.passed = report.checks.Count > 0 && report.checks.TrueForAll(c => c.passed);
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            string directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Reports"));
            try { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "life-qa.json"), JsonUtility.ToJson(report, true)); }
            catch (Exception error) { Debug.LogError("[CityLifeQA] Cannot write report: " + error); }
            Debug.Log("[CityLifeQA] Finished " + report.passed + " -> " + directory); Application.Quit(report.passed ? 0 : 2);
        }
        void Save(CityResidents.Resident p)
        { if (!saved.Exists(s => s.Person == p)) saved.Add(new SavedPerson { Person = p, Position = p.Root.position, Rotation = p.Root.rotation, Controlled = p.Controlled }); }
        void Place(CityResidents.Resident p, Vector3 point, Quaternion rotation)
        { p.Controller.enabled = false; p.Root.SetPositionAndRotation(point, rotation); p.Controller.enabled = true; p.LastPosition = point; p.Velocity = Vector3.zero; Physics.SyncTransforms(); }
        Vector3 Point(float x, float z) => new Vector3(x, ctx.Ground(x, z) + .04f, z);
        void PrepareAim(Vector3 target)
        { Vector3 eye = ctx.Player.position + Vector3.up * 2.0f + Vector3.forward * 3.5f; ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye)); Physics.SyncTransforms(); }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 40;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; if (!ctx || !ctx.Ready) { CheckResult("world-initialized", false, "CityContext did not become ready."); yield break; }
            report.residents = ctx.Residents.People.Count; report.animals = ctx.Data.animals.Length;
            playerHome = ctx.Player.position; playerRotation = ctx.Player.rotation; inventory = ctx.Combat.ExportLoadoutJson();
            bool rigs = ctx.Actors.PlayerRig != null && ctx.Actors.PlayerRig.Valid;
            foreach (var p in ctx.Residents.People) rigs &= p.Rig != null && p.Rig.Valid && p.Controller;
            CheckResult("native-rigs-and-capsules", rigs && report.residents > 20 && report.animals > 0, report.residents + " residents, " + report.animals + " animals; articulated transforms and CharacterControllers bound.");
            var positions = new Dictionary<CityResidents.Resident, Vector3>(); foreach (var p in ctx.Residents.People) positions[p] = p.Root.position;
            yield return new WaitForSeconds(2.2f);
            int moved = 0, activities = 0, invalid = 0;
            foreach (var p in ctx.Residents.People)
            { if (Vector3.Distance(p.Root.position, positions[p]) > .25f) moved++; if (p.State == "activity") activities++; if (float.IsNaN(p.Root.position.x) || float.IsInfinity(p.Root.position.y)) invalid++; }
            CheckResult("ordinary-routines-progress", moved >= 3 && activities >= 2 && invalid == 0, moved + " travelled; " + activities + " reached an activity; invalid roots " + invalid + ".");
            var civilians = ctx.Residents.People.FindAll(p => !p.Data.stationary && !p.Data.sitting && !p.Data.child && !p.Data.performance && string.IsNullOrEmpty(p.Data.role));
            if (civilians.Count < 3) { CheckResult("test-actors-available", false, "Three mobile adult civilians required."); yield break; }
            var a = civilians[0]; var b = civilians[1]; var caller = civilians[2]; Save(a); Save(b); Save(caller);
            Vector3 center = Point(-46, 36); bool found = false;
            for (float z = 20; z <= 55 && !found; z += 3) for (float x = -58; x <= -17 && !found; x += 3)
            { var candidate = Point(x, z); if (ctx.Free(candidate - Vector3.right * 1.5f) && ctx.Free(candidate) && ctx.Free(candidate + Vector3.right * 1.5f)) { center = candidate; found = true; } }
            CheckResult("collision-test-free-space", found, center.ToString("F2"));
            ctx.Residents.Control(a, true); ctx.Residents.Control(b, true);
            Vector3 startA = center - Vector3.right * 1.5f, startB = center + Vector3.right * 1.5f;
            Place(a, startA, Quaternion.Euler(0, 90, 0)); Place(b, startB, Quaternion.Euler(0, -90, 0)); ctx.Motor.Warp(center + Vector3.back * 5);
            float minDistance = float.MaxValue, elapsed = 0;
            while (elapsed < 4)
            {
                float dt = Time.deltaTime; elapsed += dt; ctx.Residents.MoveToward(a, startB, dt, 1.2f); ctx.Residents.MoveToward(b, startA, dt, 1.2f);
                minDistance = Mathf.Min(minDistance, Vector3.ProjectOnPlane(a.Root.position - b.Root.position, Vector3.up).magnitude); yield return null;
            }
            float progress = Vector3.Distance(startA, a.Root.position) + Vector3.Distance(startB, b.Root.position);
            CheckResult("opposing-pedestrians-collide-and-pass", minDistance > .68f && progress > 2.0f, "minimum separation " + minDistance.ToString("F3") + "m; total travelled " + progress.ToString("F2") + "m.");

            // The physical range wall must stop a real pulse before the civilian
            // behind it. The same ray aimed at a civilian in front must stun.
            ctx.Police.Clear(); ctx.Residents.Control(a, true); ctx.Residents.Control(caller, true);
            ctx.Motor.Warp(Point(54, 107.8f)); ctx.Player.rotation = Quaternion.Euler(0, 180, 0);
            Place(a, Point(54, 95.7f), Quaternion.identity); Place(caller, Point(56, 105.5f), Quaternion.Euler(0, 180, 0));
            ctx.Combat.Select("pulse"); ctx.Input.TouchAim = true; yield return new WaitForSeconds(.25f);
            int priorShots = ctx.Combat.Shots, priorPractice = ctx.Combat.PracticeRounds; PrepareAim(Point(54, 95.7f) + Vector3.up * 1.5f);
            bool wallShot = ctx.Combat.Fire(); yield return new WaitForSeconds(.12f);
            CheckResult("physical-cover-stops-shot", wallShot && ctx.Combat.Shots == priorShots + 1 && !a.Down && ctx.Combat.HealthOf(a.Root) == 100, "pulse reached cover; civilian behind wall down=" + a.Down);
            CheckResult("authorized-practice-no-police", ctx.Combat.PracticeRounds == priorPractice + 1 && ctx.Combat.PracticeHits > 0 && ctx.Police.Stage == "livre", "range rounds=" + ctx.Combat.PracticeRounds + "; plate hits=" + ctx.Combat.PracticeHits + "; police=" + ctx.Police.Stage);
            bool reload = ctx.Combat.Reload(); yield return new WaitForSeconds(1.3f); CheckResult("native-reload-transfers-round", reload && ctx.Combat.Ammo("pulse") == 1, "chamber=" + ctx.Combat.Ammo("pulse") + "; reserve=" + ctx.Combat.Reserve("pulse"));
            Place(a, Point(54, 100.3f), Quaternion.identity); PrepareAim(a.Root.position + Vector3.up * 1.3f); bool stunShot = ctx.Combat.Fire(); yield return new WaitForSeconds(.2f);
            CheckResult("nonlethal-physical-incapacitation", stunShot && a.Down && ctx.Combat.HealthOf(a.Root) == 100 && a.Root.GetComponent<Rigidbody>(), "actual pulse hit; down=" + a.Down + ", health=" + ctx.Combat.HealthOf(a.Root));
            bool callStarted = ctx.Police.Calling;
            CheckResult("eyewitness-starts-call", callStarted, "witness faces the assault; stage=" + ctx.Police.Stage);
            ctx.Input.TouchAim = false; ctx.Combat.Holster(); ctx.Input.TouchHack = true; yield return new WaitForSeconds(1.1f); ctx.Input.TouchHack = false;
            CheckResult("F-interrupts-untransmitted-call", callStarted && !ctx.Police.Calling && ctx.Police.Stage == "livre", "call observed=" + callStarted + "; F touch state; stage=" + ctx.Police.Stage);
            yield return new WaitForSeconds(3.8f);
            CheckResult("stun-recovers-on-city-ground", !a.Down && a.Controller.enabled && Mathf.Abs(a.Root.position.y - ctx.Ground(a.Root.position.x, a.Root.position.z)) < .25f, "down=" + a.Down + "; root=" + a.Root.position.ToString("F2"));

            var officer = ctx.Residents.People.Find(p => p.Data.role == "police" && !p.Data.indoor);
            if (officer != null)
            {
                Save(officer); ctx.Residents.Control(officer, true); Place(officer, Point(54, 110.5f), Quaternion.Euler(0, 180, 0)); ctx.Police.Clear();
                bool patrolTheft = ctx.Police.Report("furto-policial", ctx.Player.position, null, true); yield return new WaitForSeconds(.5f);
                CheckResult("witnessed-police-car-theft", patrolTheft && ctx.Police.Level == 3 && ctx.Police.Stage != "livre", "occupied patrol theft recognized; level=" + ctx.Police.Level + "; stage=" + ctx.Police.Stage);
                CheckResult("police-visible-approach", ctx.Police.Approaching, "stage=" + ctx.Police.Stage + "; distance=" + Vector3.Distance(officer.Root.position, ctx.Player.position).ToString("F2"));
                ctx.Input.TouchInteract = true; yield return new WaitForSeconds(.2f); bool custody = ctx.Police.Custody; yield return new WaitForSeconds(2.3f);
                CheckResult("E-surrender-and-playable-release", custody && ctx.Police.Stage == "livre" && !ctx.Motor.Busy, "custody observed=" + custody + "; fine=" + ctx.Police.LastFine + "; stage=" + ctx.Police.Stage);
                Place(officer, Point(54, 110.5f), Quaternion.Euler(0, 180, 0)); ctx.Police.Report("disparo", ctx.Player.position, null, true);
                Vector3 escape = Point(-180, 170); foreach (var node in ctx.Data.nav) { var q = Point(node.x, node.z); if (Vector3.Distance(q, ctx.Player.position) > 180 && ctx.Free(q)) { escape = q; break; } }
                ctx.Motor.Warp(escape); yield return new WaitForSeconds(4.2f);
                CheckResult("lost-sight-enters-search", ctx.Police.Stage == "busca", "player out of sight; stage=" + ctx.Police.Stage); ctx.Police.Clear();
            }
            else CheckResult("police-officer-available", false, "No exterior uniformed officer.");

            ctx.Combat.Damage(100, ctx.Player.position + Vector3.forward); yield return new WaitForSeconds(.2f);
            bool playerFell = ctx.Combat.Down && ctx.Player.GetComponent<Rigidbody>(); yield return new WaitForSeconds(7.5f);
            float healed = ctx.Combat.HealAtClinic();
            CheckResult("player-physical-fall-and-recovery", playerFell && !ctx.Combat.Down && ctx.Motor.Controller.enabled, "fallen=" + playerFell + "; recovered=" + !ctx.Combat.Down + "; controller=" + ctx.Motor.Controller.enabled);
            CheckResult("clinic-restores-health", healed > 0 && ctx.Combat.Health == 100, "restored=" + healed.ToString("F1") + "; health=" + ctx.Combat.Health);
        }
    }
}
