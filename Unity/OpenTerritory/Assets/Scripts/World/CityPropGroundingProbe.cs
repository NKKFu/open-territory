using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Installs only in explicitly requested QA runs.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityPropGroundingProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public int groundingRevision = 2;
            public bool passed = true;
            public string verifiedAtUtc, sourceHash, bakedSourceHash, graphicsApi;
            public int props, samples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; string directory; Vector2 movement; bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-prop-grounding-qa") >= 0)
                new GameObject("Prop grounding QA opt-in").AddComponent<CityPropGroundingProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message);
        }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = movement; }
        void Test(string name, bool passed, string detail = "")
        {
            report.passed &= passed;
            report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            if (!passed) Debug.LogWarning("PROP_GROUNDING_QA " + name + " " + detail);
        }
        float Floor(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 8, Vector3.down, 80, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore);
            return hits.Length > 0 ? hits.Max(h => h.point.y) : -999;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("../Reports/prop-grounding/editor");
            Directory.CreateDirectory(directory);
            float deadline = Time.realtimeSinceStartup + 100;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance;
            Test("scene-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            report.sourceHash = ctx.Data.sourceHash; report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            yield return new WaitForSeconds(1);
            ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true); ctx.Clock = 62;
            var baked = ctx.GetComponent<CityPropGrounding>();
            Test("baked-footings-reference-the-current-props", baked && baked.props.All(p => p.root && ctx.Node(p.nodeId) == p.root &&
                ctx.Data.destructibles.Any(d => d.id == p.nodeId && d.type == p.category)));
            if (!baked) { Finish(); yield break; }
            report.bakedSourceHash = baked.sourceHash;
            var solids = ctx.Data.solids.GroupBy(s => s.index).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var prop in baked.props)
            {
                report.props++; report.samples += prop.groundSamples.Length;
                float bottom = prop.parts.Min(p => p.filter.transform.TransformPoint(p.filter.sharedMesh.bounds.min).y);
                float lowestFloor = prop.groundSamples.Min(Floor);
                Test("supported-footprint-" + prop.nodeId, prop.groundSamples.Length >= 9 && lowestFloor > -.3f &&
                    bottom <= lowestFloor + .025f && bottom >= lowestFloor - .04f,
                    "bottom=" + bottom + " floor=" + lowestFloor + " samples=" + prop.groundSamples.Length);
                Test("complete-prop-and-lods-" + prop.nodeId, prop.root.gameObject.activeInHierarchy && !prop.root.GetComponent<Rigidbody>() &&
                    prop.parts.All(p => p.filter.sharedMesh == p.baked && p.baked.vertexCount == p.original.vertexCount &&
                        (!p.filter.GetComponentInParent<CityObjectLod>() || p.filter.GetComponentInParent<CityObjectLod>().parts
                            .Where(l => l.filter == p.filter).All(l => l.full == p.baked && l.medium == p.baked && l.far == p.baked))));
                Test("collisions-and-manifest-follow-the-prop-" + prop.nodeId, prop.shapes.Length > 0 && prop.shapes.All(s =>
                {
                    if (!s.collider || !s.collider.enabled || s.footing && s.collider.bounds.min.y > lowestFloor + .03f) return false;
                    if (s.solidIndex < 0) return true;
                    if (!solids.TryGetValue(s.solidIndex, out var records)) return false;
                    return records.Any(data => ctx.Node(data.owner) && s.collider.transform.IsChildOf(ctx.Node(data.owner)) &&
                        Mathf.Abs(data.bottom - s.collider.bounds.min.y) < .005f && Mathf.Abs(data.top - s.collider.bounds.max.y) < .005f &&
                        Vector2.Distance(new Vector2(data.x, data.z), new Vector2(s.collider.bounds.center.x, s.collider.bounds.center.z)) < .005f);
                }));
            }
            Test("all-street-bins-planters-and-lamps-covered", report.props == ctx.Data.destructibles.Count(d =>
                (d.type == "bin" || d.type == "planter" || d.type == "lamp") && ctx.Node(d.id) && ctx.Node(d.id).gameObject.activeInHierarchy));
            foreach (int id in new[] { 2690, 2914, 29278, 3549 })
            {
                var root = ctx.Node(id); var target = root.position + Vector3.up * .6f;
                var staging = root.position + new Vector3(-4, 0, 4); staging.y = Floor(staging) + .04f;
                ctx.Motor.Warp(staging);
                Capture("prop-" + id, Eye(root, target), target);
            }
            var bin = ctx.Node(2690); var center = bin.position;
            var from = center + new Vector3(-1.8f, 0, 1.6f); var to = center + new Vector3(1.8f, 0, 1.6f);
            from.y = Floor(from) + .04f; to.y = Floor(to) + .04f;
            ctx.Motor.Warp(from); ctx.Motor.SetInputLock(false);
            ctx.Camera.transform.SetPositionAndRotation(from + new Vector3(-4, 3, 0), Quaternion.LookRotation(Vector3.right));
            yield return new WaitForSeconds(.3f);
            movement = Vector2.up; deadline = ctx.Clock + 3;
            while (ctx.Clock < deadline && ctx.Player.position.x < to.x - .2f) yield return null;
            movement = Vector2.zero; yield return new WaitForSeconds(.2f);
            Test("walk-past-the-corrected-bin", ctx.Player.position.x > to.x - .5f && ctx.Motor.Grounded && !ctx.Motor.Swimming, ctx.Player.position.ToString());
            var start = center + Vector3.forward * 1.6f; start.y = Floor(start) + .04f;
            ctx.Motor.Warp(start); ctx.Camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            yield return new WaitForSeconds(.3f); movement = Vector2.up;
            yield return new WaitForSeconds(1); movement = Vector2.zero; yield return new WaitForSeconds(.2f);
            float gap = CityPhysics.Flat(ctx.Player.position - center).magnitude;
            Test("corrected-bin-still-blocks-the-character", gap > .65f && gap < 1.3f, "distance=" + gap);
            ctx.UI.SetSettings(true); yield return null;
            float clock = ctx.Clock; yield return new WaitForSecondsRealtime(.2f);
            Test("Esc-still-pauses-the-city", ctx.Paused && ctx.Clock == clock);
            ctx.UI.SetSettings(false); yield return null;
            var destructible = bin.GetComponent<CityDestructible>();
            bool damaged = destructible && destructible.Apply(2, bin.position + Vector3.up, Vector3.zero, CityPropDamageKind.Melee, false);
            Test("damage-still-resolves-the-corrected-prop", damaged && !destructible.Broken);
            Test("no-runtime-errors", report.errors.Count == 0, string.Join("; ", report.errors));
            Finish();
        }
        Vector3 Eye(Transform prop, Vector3 target)
        {
            int mask = (1 << CityContext.GroundLayer) | (1 << CityContext.WorldLayer);
            foreach (float distance in new[] { 4f, 2.5f, 1.8f })
                foreach (float height in new[] { 1.2f, 2.4f })
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = (i * 45 + 37) * Mathf.Deg2Rad;
                        var eye = target + new Vector3(Mathf.Sin(angle) * distance, height, Mathf.Cos(angle) * distance);
                        if (Physics.CheckSphere(eye, .15f, mask, QueryTriggerInteraction.Ignore)) continue;
                        var ray = target - eye;
                        if (Physics.RaycastAll(eye, ray.normalized, ray.magnitude, mask, QueryTriggerInteraction.Ignore)
                            .Any(h => !h.collider.transform.IsChildOf(prop))) continue;
                        return eye;
                    }
            return target + new Vector3(3, 5, 4);
        }
        void Capture(string name, Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var manager = FindFirstObjectByType<CityLodManager>();
            if (manager) foreach (var item in manager.objects) if (item && item.isActiveAndEnabled) item.ApplyLevel(item.SelectLevel(camera));
            var previous = RenderTexture.active; var rt = new RenderTexture(1280, 800, 24); rt.Create(); camera.targetTexture = rt;
            camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Destroy(rt); Destroy(texture);
        }
        void Finish()
        {
            finished = true; movement = Vector2.zero; if (ctx) ctx.Input.TouchMove = Vector2.zero;
            report.verifiedAtUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(directory, "prop-grounding-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("PROP_GROUNDING_QA_FINISHED passed=" + report.passed + " props=" + report.props + " samples=" + report.samples);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
