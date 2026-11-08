using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in executable review. This component is created only by --city-world-qa.
    // It drives the same touch commands and world actions as a native player.
    public sealed class CityWorldProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool pass; }
        [Serializable] public class Report
        {
            public string unity, sourceHash, savedAt, savePath;
            public bool passed;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        CityContext ctx;
        readonly Report report = new Report();
        string output;
        bool finished;
        float started;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (Array.Exists(Environment.GetCommandLineArgs(), a => a == "--city-world-qa")) new GameObject("Verificação nativa do mundo").AddComponent<CityWorldProbe>();
        }
        static string ReportPath()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--city-report-dir");
            string directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.dataPath, "..", "Reports");
            return Path.GetFullPath(Path.Combine(directory, "world-qa.json"));
        }
        IEnumerator Start()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(), a => a == "--city-world-qa")) { Destroy(this); yield break; }
            started = Time.realtimeSinceStartup; output = ReportPath(); report.unity = Application.unityVersion;
            Application.logMessageReceived += Log;
            while (!(ctx = CityContext.Instance) || !ctx.Ready)
            {
                if (Time.realtimeSinceStartup - started > 30) { report.errors.Add("Native context did not become ready within 30 seconds"); Finish(); yield break; }
                yield return null;
            }
            report.unity = Application.unityVersion; report.sourceHash = ctx.Data.sourceHash; report.savePath = ctx.Commerce.SavePath;
            var runner = Run();
            while (true)
            {
                bool next = false;
                try { next = runner.MoveNext(); }
                catch (Exception error) { report.errors.Add(error.ToString()); Finish(); }
                if (!next) break;
                yield return runner.Current;
            }
            Finish();
        }
        void Update()
        {
            if (!finished && started > 0 && Time.realtimeSinceStartup - started > 150)
            { report.errors.Add("World review exceeded its 150 second limit"); StopAllCoroutines(); Finish(); }
        }
        void Log(string condition, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(condition + "\n" + trace); }
        void Test(string name, bool pass, string detail = "") { report.checks.Add(new Check { name = name, pass = pass, detail = detail }); Debug.Log("WORLD QA " + (pass ? "PASS " : "FAIL ") + name + " " + detail); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); }
        IEnumerator PressE() { ctx.Input.TouchInteract = true; yield return null; yield return new WaitForSecondsRealtime(.10f); }
        IEnumerator CycleEquipment() { ctx.UI.CycleTouchEquipment(); yield return null; yield return new WaitForSecondsRealtime(.08f); }
        void Warp(Vector3 at) { ctx.Motor.Warp(new Vector3(at.x, ctx.Ground(at.x, at.z) + .04f, at.z)); ctx.Motor.ClearMotion(); ctx.Follow.ResetTracking(); Physics.SyncTransforms(); }
        ServiceData Service(string kind, string mode = null, string item = null)
        { foreach (var s in ctx.Data.services) if (s.kind == kind && (mode == null || s.mode == mode) && (item == null || s.item == item)) return s; return null; }
        void Approach(ServiceData s) { Warp(s.position + Vector3.forward * .85f); }
        IEnumerator ReviewRangeCard()
        {
            var card = Service("range-card"); Test("range-card-metadata", card != null); if (card == null) yield break;
            Approach(card); yield return Wait(.20f); yield return PressE();
            Test("range-empty-card-E", ctx.CurrentAction != null && ctx.CurrentAction.id == "commerce-" + card.id && ctx.Speech.Contains("yellow shooting lanes"));
            Warp(new Vector3(54, 0, 107.8f)); ctx.Player.rotation = Quaternion.Euler(0, 180, 0);
            ctx.Follow.enabled = false; ctx.Combat.Select("pulse"); ctx.Input.TouchAim = true;
            Vector3 eye = ctx.Player.position + Vector3.up * 2 + Vector3.forward * 3.5f;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(54, ctx.Ground(54, 95.7f) + 1.5f, 95.7f) - eye));
            yield return Wait(.3f); int shots = ctx.Combat.PracticeRounds, hits = ctx.Combat.PracticeHits;
            bool fired = ctx.Combat.Fire(); yield return Wait(.12f);
            Test("range-real-round-for-scorecard", fired && ctx.Combat.PracticeRounds == shots + 1 && ctx.Combat.PracticeHits == hits + 1,
                "rounds=" + ctx.Combat.PracticeRounds + " hits=" + ctx.Combat.PracticeHits);
            ctx.Input.TouchAim = false; ctx.Combat.Holster(); ctx.Follow.enabled = true; ctx.Follow.ResetTracking();
            Approach(card); yield return Wait(.20f); yield return PressE();
            Test("range-scorecard-E-shows-live-counts", ctx.CurrentAction != null && ctx.CurrentAction.id == "commerce-" + card.id &&
                ctx.Speech.Contains(ctx.Combat.PracticeHits + " hits") && ctx.Speech.Contains(ctx.Combat.PracticeRounds + " shots"), ctx.Speech);
        }
        IEnumerator ReviewPhone()
        {
            DeviceData phone = null; foreach (var device in ctx.Data.devices) if (device.type == "phone") { phone = device; break; }
            var person = ctx.Residents.People.Find(p => !p.Down && !p.Controlled && p.Bus == null && !p.Data.stationary && !p.Data.sitting && !p.Data.child &&
                !p.Data.performance && string.IsNullOrEmpty(p.Data.role) && p.Partner == null && !p.Crossing && !p.Calling && p.FearUntil <= ctx.Clock && p.TalkUntil <= ctx.Clock);
            Test("phone-fixture-available", phone != null && person != null); if (phone == null || person == null) yield break;
            var node = ctx.Node(phone.node); Vector3 front = node ? node.forward : Vector3.forward, right = Vector3.Cross(Vector3.up, front);
            Vector3 spot = Vector3.zero; bool found = false;
            foreach (float side in new[] { 1.5f, -1.5f, 2.5f, -2.5f })
            {
                spot = phone.position + front * 2.8f + right * side; spot.y = ctx.Ground(spot.x, spot.z) + .04f;
                if (ctx.Free(spot) && ctx.Visible(spot + Vector3.up * 1.8f, phone.position + Vector3.up)) { found = true; break; }
            }
            Test("phone-listener-clear-space", found); if (!found) yield break;
            Vector3 oldPosition = person.Root.position, oldThreat = person.Threat; Quaternion oldRotation = person.Root.rotation;
            bool stationary = person.Data.stationary; float talk = person.TalkUntil, look = person.NextLook, social = person.NextSocial, until = person.SocialUntil;
            string gesture = person.Gesture; bool controllerEnabled = person.Controller.enabled;
            try
            {
                person.Controller.enabled = false; person.Root.SetPositionAndRotation(spot, Quaternion.LookRotation(Vector3.ProjectOnPlane(phone.position - spot, Vector3.up)));
                person.Controller.enabled = controllerEnabled; person.LastPosition = spot; person.Velocity = Vector3.zero;
                person.Data.stationary = true; person.NextSocial = ctx.Clock + 10; person.NextLook = 0; person.SocialUntil = 0; person.Gesture = null; person.TalkUntil = ctx.Clock + 5;
                Warp(phone.position + front * 5.5f); ctx.Follow.enabled = false;
                Vector3 eye = ctx.Player.position + Vector3.up * 1.8f; ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(phone.position - eye)); Physics.SyncTransforms();
                ctx.Input.TouchHack = true; yield return Wait(.65f); ctx.Input.TouchHack = false; yield return Wait(.12f);
                Test("phone-F-preserves-conversation", ctx.Speech == "Telefone tocando" && person.SocialUntil <= ctx.Clock && person.Gesture != "look", "speech=" + ctx.Speech);
                person.TalkUntil = 0; person.NextLook = 0;
                ctx.Input.TouchHack = true; yield return Wait(.65f); ctx.Input.TouchHack = false; yield return Wait(.08f);
                Test("phone-F-causes-listener-reaction", person.Gesture == "look" && person.SocialUntil > ctx.Clock && Vector3.Distance(person.Threat, phone.position) < .05f,
                    "gesture=" + person.Gesture + " reactionSeconds=" + (person.SocialUntil - ctx.Clock));
            }
            finally
            {
                ctx.Input.TouchHack = false; ctx.Follow.enabled = true; ctx.Follow.ResetTracking();
                person.Controller.enabled = false; person.Root.SetPositionAndRotation(oldPosition, oldRotation); person.Controller.enabled = controllerEnabled; person.LastPosition = oldPosition; person.Velocity = Vector3.zero;
                person.Data.stationary = stationary; person.TalkUntil = talk; person.NextLook = look; person.NextSocial = social; person.SocialUntil = until; person.Gesture = gesture; person.Threat = oldThreat;
                Physics.SyncTransforms();
            }
        }
        IEnumerator Run()
        {
            Test("native-context", ctx.Data.residents.Length > 60 && ctx.Data.vehicles.Length > 20 && ctx.Player && ctx.Camera);
            Test("qa-wallet-isolation", report.savePath.Contains("BaiaDoSol-QA") && !report.savePath.StartsWith(Application.persistentDataPath + Path.DirectorySeparatorChar));
            ctx.Systems.Weather.Request("clear", true); yield return Wait(.20f);
            float clearRain = ctx.Rain, clearFar = RenderSettings.fogEndDistance;
            ctx.Systems.Weather.Request("squall"); yield return Wait(.30f);
            float transitionRain = ctx.Rain;
            Test("weather-smooth-front", clearRain == 0 && transitionRain > 0 && transitionRain < .1f, "rain=" + transitionRain);
            float time = ctx.Clock, rain = ctx.Systems.Weather.Rain;
            ctx.UI.SetSettings(true); yield return Wait(.25f);
            Test("settings-pause", ctx.Paused && Mathf.Abs(ctx.Clock - time) < .001f && Mathf.Abs(ctx.Systems.Weather.Rain - rain) < .0001f && AudioListener.pause);
            ctx.UI.SetSettings(false); yield return Wait(.15f);
            Test("settings-resume", !ctx.Paused && Time.timeScale == 1 && ctx.Clock > time && !AudioListener.pause);
            ctx.Systems.Weather.Request("squall", true); yield return Wait(.22f);
            Test("weather-light-fog", ctx.Rain > .99f && RenderSettings.fogEndDistance < clearFar - 100 && RenderSettings.fog);
            var lighting = FindFirstObjectByType<CityLighting>();
            bool streetLightsMounted = lighting && ctx.Data.streetLamps.Length > 10;
            int streetSources = 0;
            if (lighting) foreach (var fixture in lighting.fixtures)
            {
                if (fixture.kind != "poste") continue;
                streetSources++;
                streetLightsMounted &= fixture.source && fixture.anchor && fixture.lens
                    && Vector3.Distance(fixture.source.transform.position, fixture.anchor.TransformPoint(fixture.localEmitter)) < .001f;
            }
            Test("street-lights-at-physical-lamps", streetLightsMounted && streetSources == ctx.Data.streetLamps.Length, "fixtures=" + streetSources);
            Vector3 book = new Vector3(36, ctx.Ground(36, -36) + 1.7f, -36), street = new Vector3(31, ctx.Ground(31, 3.3f) + .1f, 3.3f);
            Test("rain-roof-and-grip", ctx.Systems.Sheltered(book) && !ctx.Systems.Sheltered(street) && ctx.Systems.SurfaceGrip(book) > ctx.Systems.SurfaceGrip(street) + .1f,
                "indoor=" + ctx.Systems.SurfaceGrip(book) + " street=" + ctx.Systems.SurfaceGrip(street));
            bool wetAxle = false; float axleGrip = 1;
            foreach (var car in ctx.Vehicles.Fleet)
            {
                Vector3 axle = car.Root.TransformPoint(new Vector3(0, 0, -car.Data.rearOffset));
                if (Physics.Raycast(axle + Vector3.up * 3.6f, Vector3.up, 40, (1 << CityContext.GroundLayer) | (1 << CityContext.WorldLayer))) continue;
                axleGrip = ctx.Systems.SurfaceGrip(axle); wetAxle = axleGrip < .85f; break;
            }
            Test("rain-vehicle-does-not-shelter-own-grip", wetAxle, "axleGrip=" + axleGrip);
            ctx.Systems.Weather.Request("clear", true);
            ctx.Combat.Holster(); yield return CycleEquipment(); Test("touch-equipment-initial-pulse", ctx.Combat.Equipped == "pulse");
            yield return CycleEquipment(); Test("touch-equipment-skips-unowned-to-empty-hands", string.IsNullOrEmpty(ctx.Combat.Equipped));

            var buy = Service("commerce", "buy", "sidearm");
            Test("shop-metadata", buy != null);
            if (buy != null)
            {
                Approach(buy); yield return Wait(.25f);
                Test("shop-real-action", ctx.CurrentAction != null && ctx.CurrentAction.id == "commerce-" + buy.id, ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id);
                int balance = ctx.Commerce.Balance; yield return PressE();
                Test("shop-buy-E", ctx.Combat.Owns("sidearm") && ctx.Commerce.Balance == balance - 180, "cash=" + ctx.Commerce.Balance);
                ctx.Combat.Holster(); yield return CycleEquipment(); yield return CycleEquipment();
                Test("touch-equipment-includes-purchased-sidearm", ctx.Combat.Equipped == "sidearm");
                yield return CycleEquipment(); Test("touch-equipment-returns-to-empty-hands", string.IsNullOrEmpty(ctx.Combat.Equipped));
                int ammo = ctx.Combat.Reserve("sidearm"); balance = ctx.Commerce.Balance; yield return PressE();
                Test("shop-ammo-E", ctx.Combat.Reserve("sidearm") == ammo + 12 && ctx.Commerce.Balance == balance - 24);
                var sell = Service("commerce", "sell", "sidearm");
                if (sell != null)
                {
                    Approach(sell); yield return Wait(.2f); balance = ctx.Commerce.Balance; yield return PressE();
                    Test("shop-resale-E", !ctx.Combat.Owns("sidearm") && ctx.Commerce.Balance == balance + 105);
                }
                balance = ctx.Commerce.Balance; bool canBuy = ctx.Commerce.Trade("buy", "carbine", out string denied);
                Test("shop-insufficient-funds", !canBuy && ctx.Commerce.Balance == balance && !ctx.Combat.Owns("carbine"), denied);
            }
            yield return ReviewRangeCard();
            var fish = ctx.Data.fish;
            if (fish.actions.Length >= 4)
            {
                Warp(fish.actions[2] + Vector3.back * 1.25f); yield return Wait(.2f);
                Test("fish-weight-action", ctx.CurrentAction != null && ctx.CurrentAction.id == "fish-2", ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id);
                yield return PressE(); yield return PressE(); yield return Wait(.4f);
                Test("fish-physical-balance", ctx.Commerce.FishUnits == 2 && ctx.Commerce.WeightUnits == 2 && Vector3.Distance(ctx.Node(fish.leftPan).position, ctx.Node(fish.rightPan).position) < 2.25f &&
                    Mathf.Abs(ctx.Node(fish.leftPan).position.y - ctx.Node(fish.rightPan).position.y) < .09f);
                Warp(fish.actions[3] + Vector3.back * 1.15f); yield return Wait(.2f); int balance = ctx.Commerce.Balance;
                Test("fish-dispatch-action", ctx.CurrentAction != null && ctx.CurrentAction.id == "fish-3", ctx.CurrentAction == null ? "none" : ctx.CurrentAction.id);
                yield return PressE();
                Test("fish-real-payout", ctx.Commerce.Balance == balance + 56 && ctx.Commerce.Shipments == 1 && ctx.Commerce.NextWeightUnits == 3, "cash=" + ctx.Commerce.Balance);
                balance = ctx.Commerce.Balance; yield return PressE(); Test("fish-no-repeat-payout", ctx.Commerce.Balance == balance);
            }
            ctx.Commerce.Checkpoint();
            bool stored = File.Exists(ctx.Commerce.SavePath); string saved = stored ? File.ReadAllText(ctx.Commerce.SavePath) : "";
            Test("wallet-complete-checkpoint", stored && saved.Contains("\"balance\":" + ctx.Commerce.Balance) && saved.Contains("loadout") && saved.Contains("shipments"));

            foreach (string kind in new[] { "home", "bookstore", "hospital" })
            {
                PlaceData place = null; foreach (var p in ctx.Data.places) if (p.kind == kind || kind == "bookstore" && p.kind == "book") { place = p; break; }
                if (place == null) { Test("door-" + kind, false, "metadata missing"); continue; }
                var door = place.door; Warp(new Vector3(door.x, 0, door.z + .72f)); yield return Wait(1.15f);
                float floor = ctx.Ground(door.x, door.z + .35f);
                bool blocked = Physics.Raycast(new Vector3(door.x, floor + 1.5f, door.z + .45f), Vector3.back, .9f, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                var root = ctx.Node(door.id); Test("door-" + kind, root && root.gameObject.activeInHierarchy && root.GetComponentsInChildren<Collider>().Length > 0 && !blocked, "openingBlocked=" + blocked);
            }

            Warp(new Vector3(-26, 0, 32)); yield return Wait(.25f);
            ctx.Input.TouchDrone = true; yield return null; yield return Wait(.20f);
            Test("drone-V", ctx.Remote.DroneActive && ctx.Motor.Remote && ctx.Remote.Drone.gameObject.activeInHierarchy);
            Vector3 start = ctx.Remote.DronePosition; ctx.Input.TouchBrake = true; yield return Wait(.45f); ctx.Input.TouchBrake = false;
            Test("drone-lift", ctx.Remote.DronePosition.y > start.y + .3f, "lift=" + (ctx.Remote.DronePosition.y - start.y));
            ctx.Input.TouchMove = Vector2.up; yield return Wait(.28f); ctx.Input.TouchMove = Vector2.zero;
            Test("drone-continuous-move", new Vector2(ctx.Remote.DronePosition.x - start.x, ctx.Remote.DronePosition.z - start.z).magnitude > .2f);
            yield return PressE(); Test("drone-E-return", !ctx.Remote.OverridesCamera && !ctx.Motor.Remote);
            yield return Wait(1.8f);

            DeviceData camera = null; foreach (var d in ctx.Data.devices) if (d.type == "camera") { camera = d; break; }
            if (camera != null)
            {
                Vector3 flat = camera.lookAt - camera.position; flat.y = 0; if (flat.sqrMagnitude < .1f) flat = Vector3.back;
                Warp(camera.position + flat.normalized * 6); yield return Wait(.12f);
                ctx.Follow.enabled = false; ctx.Camera.transform.position = ctx.Player.position + Vector3.up * 1.8f; ctx.Camera.transform.LookAt(camera.position);
                ctx.Input.TouchHack = true; yield return Wait(.65f); ctx.Input.TouchHack = false; yield return Wait(.10f);
                Test("CCTV-F-live-camera", ctx.Remote.Active && !ctx.Remote.DroneActive && ctx.Remote.OverridesCamera, "node=" + camera.node + " lens=" + camera.target);
                ctx.Follow.enabled = true; yield return PressE(); Test("CCTV-E-exit", !ctx.Remote.Active && !ctx.Motor.Remote);
            }
            else Test("CCTV-F-live-camera", false, "metadata missing");
            yield return ReviewPhone();

            Warp(new Vector3(170, 0, -150)); ctx.Systems.ToggleCrane(); yield return Wait(3.2f);
            Test("crane-lift", ctx.Systems.CraneProgress > .98f, "progress=" + ctx.Systems.CraneProgress);
            Warp(ctx.Data.harbor.loadPosition); ctx.Systems.ToggleCrane(); yield return Wait(2.1f);
            Test("crane-blocks-occupied-sweep", ctx.Systems.CraneWaiting && ctx.Systems.CraneProgress > .3f, "progress=" + ctx.Systems.CraneProgress);
            Warp(new Vector3(170, 0, -150)); yield return Wait(2.4f);
            Test("crane-resumes-clear-path", ctx.Systems.CraneProgress < .02f);
            ctx.Systems.ToggleRadio(); ctx.Systems.ToggleCinema(); yield return Wait(.3f);
            Test("cinema-native-playback", ctx.Systems.CinemaPlaying && ctx.Node(ctx.Data.cinema.screen).GetComponent<Renderer>().material.mainTexture is Texture2D);
            ctx.Systems.ToggleCinema(); ctx.Systems.Weather.Request("clear", true);
            Warp(new Vector3(-26, 0, 24)); yield return Wait(.3f); Test("music-native-playing", ctx.Systems.MusicPlaying && ctx.Systems.MusicBeat > 0);
            Test("audio-native-sources", FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length >= 3);
        }
        void Finish()
        {
            if (finished) return; finished = true;
            if (ctx && ctx.Ready)
            {
                ctx.UI.SetSettings(false); ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchBrake = ctx.Input.TouchHack = false; ctx.Follow.enabled = true;
                if (ctx.Remote.Active) ctx.Remote.RecallOrExit();
            }
            report.passed = report.errors.Count == 0 && report.checks.Count > 20 && report.checks.TrueForAll(c => c.pass);
            report.savedAt = DateTime.UtcNow.ToString("O");
            if (string.IsNullOrEmpty(output)) output = ReportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("WORLD QA COMPLETE: " + (report.passed ? "PASS" : "FAIL") + " " + output);
            Application.logMessageReceived -= Log;
            Application.Quit(report.passed ? 0 : 2);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; }
    }
}
