using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in only: --city-seat-camera-qa, or explicitly attach in Play Mode.
    public sealed class CitySeatCameraProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report
        {
            public bool passed = true;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; SeatData occupied; GameObject wall;
        Vector3 savedPosition; Quaternion savedRotation;
        float savedDistance, savedSensitivity; bool savedInvert;
        string savedLoadout, savedEquipped, dir;
        public string ReportDirectory;
        float minDistance, maxStep; bool lensClear, framed; Vector3 previousEye;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-seat-camera-qa") >= 0)
                new GameObject("Seated camera QA opt-in").AddComponent<CitySeatCameraProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace);
        }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed;
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("SEAT_CAMERA_QA " + name + " " + passed + " " + detail);
        }
        bool HeadFits()
        {
            var renderers = ctx.Actors.PlayerRig.Head.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var screen = ctx.Camera.WorldToViewportPoint(corner);
                    if (screen.z <= ctx.Camera.nearClipPlane || screen.x < .01f || screen.x > .99f || screen.y < .01f || screen.y > .95f) return false;
                }
            }
            return renderers.Length > 0;
        }
        IEnumerator Observe(float seconds)
        {
            minDistance = float.PositiveInfinity; maxStep = 0; lensClear = framed = true;
            previousEye = ctx.Camera.transform.position;
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
                var eye = ctx.Camera.transform.position;
                minDistance = Mathf.Min(minDistance, ctx.Follow.ObstructionDistance);
                maxStep = Mathf.Max(maxStep, Vector3.Distance(previousEye, eye)); previousEye = eye;
                lensClear &= float.IsFinite(eye.x) && float.IsFinite(eye.y) && float.IsFinite(eye.z)
                    && !Physics.CheckSphere(eye, .12f, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                framed &= HeadFits();
            }
        }
        IEnumerator Orbit(float degrees, float pitchDelta = 0)
        {
            ctx.Input.TouchLook = new Vector2(degrees / 2.6f, -pitchDelta / 2f);
            yield return null;
            yield return new WaitForSeconds(.7f);
        }
        // Select real furniture with space for the tested orbit, so a nearby
        // building cannot masquerade as the bench-camera regression.
        bool OpenBench(SeatData b)
        {
            if (b.occupied) return false;
            var rotation = Quaternion.Euler(0, b.yaw * Mathf.Rad2Deg, 0);
            var at = new Vector3(b.x, ctx.Ground(b.x, b.z), b.z) + rotation * Vector3.forward * (b.depth * .5f - .30f);
            var approach = new Vector3(b.x, 0, b.z) + rotation * Vector3.forward * (b.depth * .5f + .45f);
            approach.y = ctx.Ground(approach.x, approach.z) + .04f;
            if (!ctx.Free(approach)) return false;
            for (int i = 0; i < 4; i++)
            {
                float yaw = b.yaw * Mathf.Rad2Deg + i * 90;
                var origin = at + Vector3.up * (b.seatTop + 1.11f) + Quaternion.Euler(0, yaw, 0) * Vector3.right * CityFollowCamera.ShoulderOffset;
                var direction = Quaternion.Euler(-CityFollowCamera.DefaultPitch, yaw + 180, 0) * Vector3.forward;
                if (Physics.SphereCastAll(origin, .22f, direction, 5.5f, ctx.SolidMask, QueryTriggerInteraction.Ignore).Length > 0) return false;
            }
            return true;
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = ctx.Camera; var target = new RenderTexture(1280, 800, 24); target.Create();
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; Texture2D image = null;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Destroy(target); if (image) Destroy(image);
            }
        }
        IEnumerator Bench(SeatData b, int index)
        {
            string prefix = "bench-" + b.id + "-";
            var action = ctx.Actions.Find(a => a.id == "seat-" + b.id);
            var feet = action.Position; feet.y = ctx.Ground(feet.x, feet.z) + .04f;
            ctx.Motor.Warp(feet); ctx.Player.rotation = Quaternion.Euler(0, b.yaw * Mathf.Rad2Deg + 180, 0);
            ctx.Follow.Face(ctx.Player.eulerAngles.y); ctx.Follow.Distance = CityFollowCamera.DefaultDistance;
            yield return new WaitForSeconds(.7f); ctx.RefreshAction();
            CheckResult(prefix + "E-selects-real-seat", ctx.CurrentAction == action, ctx.CurrentAction?.id);
            if (ctx.CurrentAction != action) yield break;
            occupied = b; ctx.Input.TouchInteract = true;
            yield return Observe(.9f);
            CheckResult(prefix + "E-sits", ctx.Motor.SeatHeight.HasValue && b.occupied);
            if (!ctx.Motor.SeatHeight.HasValue) { occupied = null; yield break; }
            CheckResult(prefix + "sitting-transition-keeps-view", lensClear && framed && minDistance > 1.8f && maxStep < .6f,
                "min distance=" + minDistance + "; max frame step=" + maxStep);
            for (int i = 0; i < 4; i++)
            {
                yield return Orbit(90); yield return Observe(.2f);
                CheckResult(prefix + "orbit-" + (i + 1) * 90, lensClear && framed && minDistance > 2.3f, "distance=" + minDistance);
                if (i == 1 && index == 0) Capture("01-sentado-encosto");
            }
            yield return Orbit(180); // Back of the bench: the original failure.
            float beforePitch = ctx.Camera.transform.eulerAngles.x;
            ctx.Follow.Distance = 2.4f; yield return new WaitForSeconds(.6f); yield return Observe(.2f);
            CheckResult(prefix + "close-zoom", lensClear && framed && minDistance > 2.2f);
            ctx.Follow.Distance = 5.5f; yield return new WaitForSeconds(1.4f); yield return Observe(.2f);
            CheckResult(prefix + "far-zoom", lensClear && framed && minDistance > 5.2f);
            CheckResult(prefix + "zoom-preserves-pitch", Mathf.Abs(Mathf.DeltaAngle(beforePitch, ctx.Camera.transform.eulerAngles.x)) < .25f);
            ctx.Follow.Distance = CityFollowCamera.DefaultDistance;
            yield return Orbit(0, -30); yield return Observe(.2f);
            CheckResult(prefix + "low-orbit-clears-backrest", lensClear && framed && minDistance > 2.2f, "distance=" + minDistance);
            yield return Orbit(0, 60); yield return Observe(.2f);
            CheckResult(prefix + "high-orbit-keeps-head", lensClear && framed && minDistance > 2.2f);
            yield return Orbit(0, -30);
            if (index == 0)
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA seated camera wall"; wall.layer = CityContext.WorldLayer;
                wall.transform.position = ctx.Player.position + Vector3.up * 2 - ctx.Camera.transform.forward * 1.6f;
                wall.transform.rotation = Quaternion.Euler(0, ctx.Camera.transform.eulerAngles.y, 0); wall.transform.localScale = new Vector3(8, 5, .22f);
                yield return new WaitForSeconds(.6f); yield return Observe(.2f);
                CheckResult(prefix + "wall-still-retracts-camera", lensClear && minDistance < 1.6f && minDistance > .4f, "distance=" + minDistance);
                Destroy(wall); wall = null; yield return new WaitForSeconds(1.4f); yield return Observe(.2f);
                CheckResult(prefix + "wall-removal-restores-view", lensClear && framed && minDistance > 2.3f);
                var eye = ctx.Camera.transform.position; var clock = ctx.Clock;
                ctx.UI.SetSettings(true); yield return new WaitForSecondsRealtime(.2f);
                CheckResult(prefix + "Esc-pauses-seated-camera", ctx.Motor.SeatHeight.HasValue && ctx.Clock == clock && Vector3.Distance(eye, ctx.Camera.transform.position) < .001f);
                ctx.UI.SetSettings(false); yield return new WaitForSeconds(.2f);
                Capture("02-sentado-retomada");
            }
            ctx.Input.TouchInteract = true; yield return Observe(.9f);
            CheckResult(prefix + "E-stands", !ctx.Motor.SeatHeight.HasValue && !b.occupied && ctx.Motor.Controller.enabled);
            CheckResult(prefix + "standing-transition-keeps-view", lensClear && framed && minDistance > 1.8f && maxStep < .6f,
                "min distance=" + minDistance + "; max frame step=" + maxStep);
            occupied = null;
            var start = ctx.Player.position; ctx.Input.TouchMove = Vector2.up; yield return new WaitForSeconds(.6f); ctx.Input.TouchMove = Vector2.zero;
            CheckResult(prefix + "walking-resumes", CityPhysics.Flat(ctx.Player.position - start).magnitude > 1 && !ctx.Motor.SeatHeight.HasValue);
            yield return new WaitForSeconds(.3f);
        }
        IEnumerator Start()
        {
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; savedPosition = ctx.Player.position; savedRotation = ctx.Player.rotation;
            savedDistance = ctx.Follow.Distance; savedSensitivity = ctx.Follow.Sensitivity; savedInvert = ctx.Follow.InvertY;
            savedLoadout = ctx.Combat.ExportLoadoutJson(); savedEquipped = ctx.Combat.Equipped;
            ctx.Combat.Holster(); ctx.Input.TouchMove = ctx.Input.TouchLook = Vector2.zero; ctx.Input.TouchRun = ctx.Input.TouchAim = false;
            ctx.Follow.Sensitivity = 1; ctx.Follow.InvertY = false;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            dir = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/seat-camera/editor"));
            Directory.CreateDirectory(dir);
            var benches = ctx.Data.seats.Where(OpenBench).OrderBy(b => Vector3.Distance(savedPosition, new Vector3(b.x, 0, b.z))).Take(3).ToArray();
            CheckResult("three-open-real-benches-found", benches.Length == 3, string.Join(", ", benches.Select(b => b.id)));
            for (int i = 0; i < benches.Length; i++) yield return Bench(benches[i], i);
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            File.WriteAllText(Path.Combine(dir, "seat-camera-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("SEAT_CAMERA_QA_DONE " + report.passed); Destroy(this);
            if (!Application.isEditor && Array.IndexOf(args, "--city-seat-camera-qa") >= 0) Application.Quit(report.passed ? 0 : 1);
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (wall) Destroy(wall);
            if (!ctx || !ctx.Player || !ctx.Motor || !ctx.UI || !ctx.Combat || !ctx.Follow) return;
            if (occupied != null) occupied.occupied = false;
            ctx.Input.TouchMove = ctx.Input.TouchLook = Vector2.zero; ctx.Input.TouchRun = ctx.Input.TouchAim = ctx.Input.TouchInteract = false;
            ctx.UI.SetSettings(false); ctx.Motor.SetSeat(null); ctx.Motor.Warp(savedPosition); ctx.Player.rotation = savedRotation;
            ctx.Combat.RestoreLoadoutJson(savedLoadout); if (!string.IsNullOrEmpty(savedEquipped)) ctx.Combat.Select(savedEquipped);
            ctx.Follow.Distance = savedDistance; ctx.Follow.Sensitivity = savedSensitivity; ctx.Follow.InvertY = savedInvert; ctx.Follow.ResetTracking();
        }
    }
}
