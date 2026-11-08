// Isolated acceptance runner. Copied into the temporary project's Editor folder only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTerritory;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CameraMountsBatch
{
    [Serializable] public class Check { public string name, detail; public bool passed; }
    [Serializable] public class Report { public bool passed = true; public string unity, sourceHash; public List<Check> checks = new List<Check>(); }
    const string Phase = "SecurityCameraValidationPhase";
    static IEnumerator review;
    static Report report;
    static int lastFrame = -1;
    static DateTime started;
    static string Arg(string key) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, key); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static string DirectoryPath => Arg("--camera-report");
    static CameraMountsBatch() { if (Environment.GetCommandLineArgs().Contains("--security-camera-isolated-validation")) EditorApplication.update += Tick; }
    public static void Run()
    {
        Directory.CreateDirectory(DirectoryPath);
        EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity");
        SessionState.SetInt(Phase, 1);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 1 && EditorApplication.isPlaying)
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (review == null) { started = DateTime.UtcNow; report = new Report { unity = Application.unityVersion }; review = Review(); }
            try
            {
                if ((DateTime.UtcNow - started).TotalSeconds > 90) throw new TimeoutException("Camera acceptance exceeded 90 seconds");
                if (review.MoveNext()) return;
            }
            catch (Exception e) { Add("acceptance-completed", false, e.ToString()); }
            File.WriteAllText(Path.Combine(DirectoryPath, "camera-qa.json"), JsonUtility.ToJson(report, true));
            SessionState.SetInt(Phase, 2);
            EditorApplication.isPlaying = false;
        }
        else if (phase == 2 && !EditorApplication.isPlaying)
        {
            SessionState.SetInt(Phase, 3);
            try
            {
                var qa = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(DirectoryPath, "camera-qa.json")));
                var output = Arg("--camera-build-path"); Directory.CreateDirectory(Path.GetDirectoryName(output));
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" }, locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                File.WriteAllText(Path.Combine(DirectoryPath, "build-result.txt"), DateTime.UtcNow.ToString("o") + "\n" + build.summary.result + "\nerrors=" + build.summary.totalErrors + "\nbytes=" + build.summary.totalSize + "\noutput=" + output);
                EditorApplication.Exit(qa.passed && build.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(DirectoryPath, "build-error.txt"), e.ToString()); EditorApplication.Exit(2); }
        }
    }
    static void Add(string name, bool passed, string detail = "") { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("CAMERA_MOUNT_QA " + (passed ? "PASS " : "FAIL ") + name + " " + detail); }
    static IEnumerator Review()
    {
        while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
        var ctx = CityContext.Instance; report.sourceHash = ctx.Data.sourceHash;
        ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.Motor.SetVisible(false); ctx.Motor.SetInputLock(true); ctx.Combat.Holster();
        var cameras = ctx.Data.devices.Where(d => d.type == "camera").ToArray();
        Add("both-security-cameras-registered", cameras.Length == 2);
        Add("F-and-E-bindings-preserved", CityInput.HackKey == KeyCode.F && CityInput.InteractKey == KeyCode.E);
        var post = ctx.Node(3354); var head = ctx.Node(3359);
        Add("restored-head-belongs-to-mast", head.parent == post);
        Add("four-fixed-mount-parts-render", Enumerable.Range(3355, 4).All(id => ctx.Node(id).GetComponent<Renderer>() && ctx.Node(id).GetComponent<Renderer>().enabled));
        var baseBounds = ctx.Node(3355).GetComponent<Renderer>().bounds;
        var gaps = new List<float>();
        foreach (float x in new[] { -.30f, .30f }) foreach (float z in new[] { -.30f, .30f }) gaps.Add(baseBounds.min.y - ctx.Ground(post.position.x + x, post.position.z + z));
        Add("entire-base-supported-by-physical-floor", gaps.All(g => g >= -.08f && g <= .04f), string.Join(",", gaps.Select(g => g.ToString("F4"))));
        var overlaps = Physics.OverlapBox(post.position + Vector3.up * 2.6f, new Vector3(.42f, 2.5f, .42f), post.rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => c.name != "Colisão-23658" && c.name != "Colisão-23659").ToArray();
        Add("mast-clear-of-walls-and-props", overlaps.Length == 0, string.Join(",", overlaps.Select(c => c.name)));
        foreach (var device in cameras)
        {
            ctx.Follow.enabled = false;
            var cameraHead = ctx.Node(device.node); var fixedPost = ctx.Node(device.node == 3359 ? 3354 : 3341);
            var postPosition = fixedPost.position; var postRotation = fixedPost.rotation; var pivotPosition = cameraHead.position; var homeRotation = cameraHead.rotation;
            bool aimed = false;
            foreach (float radius in new[] { 7f, 11f, 17f })
            {
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    Vector3 feet = pivotPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius; feet.y = ctx.Ground(feet.x, feet.z) + .05f;
                    Vector3 eye = feet + Vector3.up * 1.68f, target = ctx.Remote.TargetPoint(device, eye);
                    if (!ctx.Free(feet) || !ctx.Visible(eye, target)) continue;
                    ctx.Motor.Warp(feet); ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    ctx.Remote.SetHackerMode(true); Physics.SyncTransforms();
                    yield return null; yield return null; yield return null;
                    if (ctx.Remote.SelectedDevice == device) { aimed = true; break; }
                }
                if (aimed) break;
            }
            Add(device.id + "-targeted-through-real-selector", aimed);
            if (!aimed) continue;
            int hacks = ctx.Remote.SuccessfulHacks;
            ctx.Input.TouchHack = true; yield return null; yield return null;
            ctx.Input.TouchHack = false; ctx.Follow.enabled = true; yield return null; yield return null;
            Add(device.id + "-F-connects-once", ctx.Remote.OverridesCamera && ctx.Remote.SuccessfulHacks == hacks + 1);
            Add(device.id + "-view-origin-is-physical-lens", Vector3.Distance(ctx.Camera.transform.position, ctx.Node(device.target).position) < .025f);
            var before = cameraHead.rotation;
            for (int f = 0; f < 5; f++) { ctx.Input.TouchLook = new Vector2(12, -4); yield return null; }
            ctx.Input.TouchLook = Vector2.zero; yield return null;
            Add(device.id + "-pan-and-tilt-operate", Quaternion.Angle(before, cameraHead.rotation) > 5);
            Add(device.id + "-mount-and-pivot-stay-fixed", Vector3.Distance(fixedPost.position, postPosition) < .001f && Quaternion.Angle(fixedPost.rotation, postRotation) < .01f && Vector3.Distance(cameraHead.position, pivotPosition) < .001f);
            ctx.UI.SetSettings(true); yield return null;
            before = cameraHead.rotation;
            for (int f = 0; f < 3; f++) { ctx.Input.TouchLook = new Vector2(30, 12); yield return null; }
            Add(device.id + "-Esc-pauses-camera", ctx.Paused && Quaternion.Angle(before, cameraHead.rotation) < .01f);
            ctx.Input.TouchLook = Vector2.zero; ctx.UI.SetSettings(false); yield return null; yield return null;
            ctx.Input.TouchInteract = true; yield return null; yield return null; yield return null;
            Add(device.id + "-E-disconnects", !ctx.Remote.OverridesCamera && !ctx.Motor.Remote);
            if (ctx.Remote.Active) ctx.Remote.RecallOrExit();
            Add(device.id + "-head-restores-home-pose", Quaternion.Angle(homeRotation, cameraHead.rotation) < .01f);
        }
        ctx.Follow.enabled = false;
        ctx.Remote.SetHackerMode(false); ctx.Input.TouchMove = Vector2.zero;
        Vector3 approach = post.position + Vector3.left * 1.3f; approach.y = ctx.Ground(approach.x, approach.z) + .05f;
        Add("approach-to-new-mast-is-walkable", ctx.Free(approach));
        ctx.Motor.Warp(approach); Physics.SyncTransforms();
        var controller = ctx.Player.GetComponent<CharacterController>(); var flags = controller.Move(Vector3.right * 2.6f);
        Add("real-character-blocked-by-new-mast", (flags & CollisionFlags.Sides) != 0 && ctx.Player.position.x < post.position.x - .2f, ctx.Player.position.ToString("F3"));
        Capture(ctx, head.position + new Vector3(5, .7f, 7), head.position - Vector3.up * 1.8f, "camera-fixed-overview.png");
        Capture(ctx, new Vector3(head.position.x + 4, ctx.Ground(head.position.x + 4, head.position.z + 8) + 1.7f, head.position.z + 8), head.position - Vector3.up * 1.8f, "camera-fixed-street.png");
    }
    static void Capture(CityContext ctx, Vector3 eye, Vector3 target, string file)
    {
        var g = new GameObject("Temporary camera acceptance view"); var camera = g.AddComponent<Camera>(); camera.CopyFrom(ctx.Camera); camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye)); camera.fieldOfView = 55;
        var rt = new RenderTexture(1280, 900, 24); rt.Create(); camera.targetTexture = rt; var previous = RenderTexture.active; camera.Render(); RenderTexture.active = rt;
        var image = new Texture2D(1280, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(DirectoryPath, file), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(g);
    }
}
