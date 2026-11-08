using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in only: --city-emote-qa, or explicitly attach in Play Mode.
    public sealed class CityEmoteProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        readonly List<CityResidents.Resident> spawned = new List<CityResidents.Resident>();
        CityContext ctx; List<CityAction> actions; GameObject floor, wall;
        Vector3 savedPlayer, savedEye; Quaternion savedRotation, savedView;
        bool savedFollow, savedPopulation, quitting; string dir; int talks;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-emote-qa") >= 0) new GameObject("Emote QA opt-in").AddComponent<CityEmoteProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("EMOTE_QA " + name + " " + passed + " " + detail); }
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); yield return Frame(); }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            // A hidden batch player has no capturable swap chain on some graphics drivers.
            // Render the same camera and transient overlay into a native texture instead.
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            var previousTarget = ctx.Camera.targetTexture; var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24); Texture2D picture = null;
            try
            {
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ctx.Camera; c.planeDistance = 1; }
                ctx.Camera.targetTexture = target; Canvas.ForceUpdateCanvases(); ctx.Camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); picture.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), picture.EncodeToPNG());
            }
            finally
            {
                ctx.Camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i]; }
                Canvas.ForceUpdateCanvases(); RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture);
            }
        }
        Vector2 Center => new Vector2(Screen.width, Screen.height) * .5f;
        Vector2 Direction(int index)
        {
            float angle = (90 - index * 60) * Mathf.Deg2Rad;
            return Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Screen.height / 800f * 160);
        }
        IEnumerator Open()
        {
            ctx.Input.TouchEmotePointer = Center; ctx.Input.TouchEmoteHeld = true;
            yield return Wait(CityEmotes.HoldDelay + .10f);
        }
        IEnumerator Release() { ctx.Input.TouchEmoteHeld = false; yield return Frame(); }
        void QuietResidents()
        {
            foreach (var p in spawned)
            {
                ctx.Residents.Interrupt(p); p.TalkUntil = p.FearUntil = p.DodgeUntil = 0;
                p.State = "activity"; p.Activity = "rest"; p.Age = 0; p.Duration = 600;
                p.NextSocial = ctx.Clock + 600; p.NextPause = ctx.Clock + 600;
                p.Path.Clear(); p.MoveSpeed = 0; p.Crossing = false;
            }
        }
        IEnumerator Start()
        {
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/emotes/native")); Directory.CreateDirectory(dir);
            savedPlayer = ctx.Player.position; savedRotation = ctx.Player.rotation;
            savedEye = ctx.Camera.transform.position; savedView = ctx.Camera.transform.rotation;
            savedFollow = ctx.Follow.enabled; savedPopulation = ctx.Population.enabled;
            ctx.Follow.enabled = ctx.Population.enabled = false;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA emote floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(24, 2, 24);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); ctx.Player.rotation = Quaternion.identity;
            ctx.Camera.transform.position = new Vector3(0, 202.6f, -4.3f); ctx.Camera.transform.LookAt(new Vector3(0, 201.6f, 1.3f));
            actions = ctx.Actions; ctx.Actions = new List<CityAction>();
            ctx.AddAction("qa-emote-talk", () => new Vector3(0, 201.5f, 2), () => "Conversar", () => talks++, conversation: true,
                target: () => spawned.Count > 1 ? spawned[1].Root : null);
            foreach (var data in ctx.Data.residents.Where(d => !d.stationary && !d.sitting && !d.performance && !ctx.Residents.IsSpawned(d.id)).Take(4))
            {
                int index = spawned.Count;
                Vector3 point = index == 3 ? new Vector3(0, 200.04f, 10) : new Vector3((index - 1) * 1.65f, 200.04f, 2.1f);
                if (ctx.Residents.Spawn(data.id, point, Quaternion.Euler(0, 180, 0))) spawned.Add(ctx.Residents.Find(data.id));
            }
            QuietResidents(); yield return Wait(1);
            CheckResult("test-uses-real-resident-rigs", spawned.Count == 4 && spawned.All(p => p.Rig != null));
            CheckResult("talk-target-visible-before-Q", ctx.CurrentAction?.id == "qa-emote-talk", ctx.CurrentAction?.id);
            CheckResult("normal-play-has-no-other-emote-probe", FindObjectsByType<CityEmoteProbe>(FindObjectsSortMode.None).Length == 1);
            // Exercise the very same sampled input as the keyboard, including tap vs hold.
            ctx.Input.TouchEmoteHeld = true; yield return Frame();
            CheckResult("Q-down-does-not-start-conversation", talks == 0 && !ctx.Emotes.WheelOpen);
            yield return Release(); CheckResult("quick-Q-release-talks-once", talks == 1 && !ctx.Emotes.Performing, "talks=" + talks + " action=" + ctx.CurrentAction?.id);
            yield return Wait(.1f); CheckResult("Q-tap-never-repeats", talks == 1);
            float clock = ctx.Clock; yield return Open();
            CheckResult("hold-Q-opens-without-talking-or-pausing", ctx.Emotes.WheelOpen && talks == 1 && !ctx.Paused && ctx.Clock > clock && Time.timeScale == 1);
            CheckResult("wheel-blocks-pointer-from-gameplay", ctx.UI.PointerBlocked);
            CheckResult("wheel-starts-with-no-selection", ctx.Emotes.Selected == CityEmote.None);
            Capture("01-roda-emojis"); yield return Frame();
            for (int i = 0; i < 6; i++)
            {
                ctx.Input.TouchEmotePointer = Direction(i); yield return Frame();
                CheckResult("mouse-selects-" + CityEmotes.Names[i], ctx.Emotes.Selected == (CityEmote)i, ctx.Emotes.Selected.ToString());
            }
            ctx.Input.TouchEmotePointer = Center; yield return Release();
            CheckResult("release-in-center-cancels", !ctx.Emotes.WheelOpen && !ctx.Emotes.Performing && talks == 1);
            for (int i = 0; i < 6; i++)
            {
                QuietResidents(); yield return Open(); ctx.Input.TouchEmotePointer = Direction(i); yield return Frame();
                if (i == 2) { Capture("02-dancar-selecionado"); yield return Frame(); }
                yield return Release(); yield return Wait(.36f);
                CheckResult("Q-release-performs-" + CityEmotes.Names[i], !ctx.Emotes.WheelOpen && ctx.Emotes.Current == (CityEmote)i && ctx.Emotes.Performing && talks == 1);
                CheckResult("NPCs-react-to-" + CityEmotes.Names[i], ctx.Emotes.LastReactionCount == 3 && spawned.Take(3).All(p => p.EmoteUntil > ctx.Clock), "responders=" + ctx.Emotes.LastReactionCount);
                CheckResult("far-NPC-ignores-" + CityEmotes.Names[i], spawned.Count < 4 || spawned[3].EmoteUntil <= ctx.Clock);
                if (i == 2)
                {
                    var rig = ctx.Actors.PlayerRig; var before = rig.Arms[1].localRotation; var position = ctx.Player.position;
                    yield return Wait(.22f);
                    CheckResult("dance-animates-body-without-moving-capsule", Quaternion.Angle(before, rig.Arms[1].localRotation) > 2 && Vector3.Distance(position, ctx.Player.position) < .05f);
                    Capture("03-danca-e-reacao"); yield return Frame();
                }
                if (i == 1) { CheckResult("sadness-receives-comfort", ctx.Speech == CityEnglish.Text("Ei, estou por aqui se quiser conversar.")); Capture("04-triste-e-acolhimento"); yield return Frame(); }
                ctx.Emotes.CancelPerformance();
            }
            QuietResidents(); wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA emote occlusion"; wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(0, 201.5f, 1); wall.transform.localScale = new Vector3(8, 3, .15f); Physics.SyncTransforms();
            ctx.Emotes.Perform(CityEmote.Happy); CheckResult("NPCs-behind-wall-ignore-emote", ctx.Emotes.LastReactionCount == 0);
            Destroy(wall); wall = null; ctx.Emotes.CancelPerformance(); yield return Frame();
            foreach (var p in spawned.Take(3)) p.Crossing = true;
            ctx.Emotes.Perform(CityEmote.Wave); CheckResult("crossing-pedestrians-keep-crossing", ctx.Emotes.LastReactionCount == 0);
            QuietResidents(); ctx.Emotes.CancelPerformance(); yield return Open();
            ctx.Input.TouchEmotePointer = Direction(0); yield return Frame(); ctx.UI.SetSettings(true); yield return Release();
            CheckResult("Esc-cancels-wheel-without-gesture-or-talk", ctx.Paused && !ctx.Emotes.WheelOpen && !ctx.Emotes.Performing && talks == 1);
            ctx.UI.SetSettings(false); yield return Frame(); CheckResult("resume-does-not-replay-Q", !ctx.Emotes.WheelOpen && !ctx.Emotes.Performing && talks == 1);
            ctx.Emotes.Perform(CityEmote.Dance); yield return Wait(.3f);
            float pausedClock = ctx.Clock; var poseBefore = ctx.Actors.PlayerRig.Arms[1].localRotation; ctx.UI.SetSettings(true); yield return Wait(.2f);
            CheckResult("Esc-pauses-gesture-and-clock", ctx.Clock == pausedClock && Quaternion.Angle(poseBefore, ctx.Actors.PlayerRig.Arms[1].localRotation) < .1f);
            ctx.UI.SetSettings(false); ctx.Input.TouchMove = Vector2.right; yield return Frame(); ctx.Input.TouchMove = Vector2.zero;
            CheckResult("movement-cancels-gesture-immediately", !ctx.Emotes.Performing);
            yield return Wait(.12f); yield return Open(); ctx.Emotes.CancelInput(); yield return Release();
            CheckResult("focus-cancel-does-not-replay-Q", !ctx.Emotes.WheelOpen && !ctx.Emotes.Performing && talks == 1);
            ctx.Motor.SetRide(floor.transform); ctx.Input.TouchEmoteHeld = true; yield return Wait(.35f);
            CheckResult("vehicle-Q-does-not-open-wheel", !ctx.Emotes.WheelOpen);
            yield return Release(); ctx.Motor.SetRide(null); yield return Wait(.15f);
            ctx.Emotes.Perform(CityEmote.Happy); yield return Wait(3.8f);
            CheckResult("emote-and-NPC-reactions-expire", !ctx.Emotes.Performing && spawned.All(p => p.EmoteUntil <= ctx.Clock));
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            File.WriteAllText(Path.Combine(dir, "emote-qa.json"), JsonUtility.ToJson(report, true)); Debug.Log("EMOTE_QA_DONE " + report.passed);
            bool quit = !Application.isEditor && Array.IndexOf(args, "--city-emote-qa") >= 0;
            if (quit) { quitting = true; Application.Quit(report.passed ? 0 : 1); }
            else Destroy(this);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (!ctx || quitting) return;
            ctx.Input.TouchEmoteHeld = false; ctx.Input.TouchEmotePointer = null; ctx.Input.TouchMove = Vector2.zero;
            ctx.Emotes.CancelInput(); ctx.Emotes.CancelPerformance(); ctx.UI.SetSettings(false);
            if (actions != null) ctx.Actions = actions;
            foreach (var p in spawned) if (p.Root) ctx.Residents.Despawn(p);
            ctx.Motor.SetRide(null); ctx.Motor.Warp(savedPlayer); ctx.Player.rotation = savedRotation;
            ctx.Camera.transform.SetPositionAndRotation(savedEye, savedView); ctx.Follow.enabled = savedFollow; ctx.Population.enabled = savedPopulation; ctx.Follow.ResetTracking();
            if (floor) Destroy(floor); if (wall) Destroy(wall);
        }
    }
}
