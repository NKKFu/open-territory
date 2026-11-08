using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit native integration run; it never installs in a normal session.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityFrontageProbe : MonoBehaviour
    {
        [Serializable] public class Entrance { public int door; public string name; public float gap; public Vector3 sidewalk, inside; public Vector3[] samples; }
        [Serializable] public class Layout { public int revision; public Entrance[] entrances; }
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(), obstacles = new List<string>(); }
        CityContext ctx; Layout layout; Vector2 move; string dir; bool finished; readonly Report report = new Report();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-frontage-qa") >= 0) new GameObject("QA de fachadas e acessos • opt-in").AddComponent<CityFrontageProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, float value = 0, string detail = "") { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("FRONTAGE_QA " + name + " " + passed + " " + value + " " + detail); }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir"); dir = at >= 0 ? args[at + 1] : Application.persistentDataPath; Directory.CreateDirectory(dir);
            float timeout = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            var asset = Resources.Load<TextAsset>("building-frontages"); layout = asset ? JsonUtility.FromJson<Layout>(asset.text) : null;
            // Native compounds own their new entrances; the exported routes
            // belong only to doors that still occupy their original frontage.
            if (layout != null) layout.entrances = layout.entrances.Where(e => ctx.Data.places.Any(p => p.door != null && p.door.id == e.door &&
                CityPhysics.Flat(new Vector3(p.door.x,0,p.door.z)-e.samples.Last()).magnitude < 3)).ToArray();
            Test("current-exported-doors-have-reviewed-access", layout != null && layout.entrances.Length >= 200, layout == null ? 0 : layout.entrances.Length);
            if (layout == null) { Finish(); yield break; }
            yield return new WaitForSeconds(1.5f);
            int unsupported = 0, blocked = 0, checkedPoints = 0, navMissing = 0; float maxError = 0;
            foreach (var entry in layout.entrances)
            {
                var door = ctx.Node(entry.door);
                foreach (var p in entry.samples)
                {
                    checkedPoints++;
                    if (!Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var hit, 8, 1 << CityContext.GroundLayer)) { unsupported++; continue; }
                    float error = Mathf.Abs(hit.point.y - p.y); maxError = Mathf.Max(maxError, error);
                    if (error > .16f) { unsupported++; if (report.obstacles.Count < 30) report.obstacles.Add("height " + entry.name + " " + p + " error=" + error); }
                    foreach (var collider in Physics.OverlapCapsule(hit.point + Vector3.up * .55f, hit.point + Vector3.up * 1.8f, .26f, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                    {
                        if (CityPhysics.Belongs(collider, door)) continue;
                        blocked++; if (report.obstacles.Count < 30) report.obstacles.Add("blocked " + entry.name + " " + p + " " + collider.name); break;
                    }
                }
                if (!ctx.Data.nav.Any(n => Mathf.Pow(n.x - entry.sidewalk.x, 2) + Mathf.Pow(n.z - entry.sidewalk.z, 2) < 4 && n.edges.Length > 0)) { navMissing++; report.obstacles.Add("navigation " + entry.name + " " + entry.sidewalk); }
            }
            Test("all-entrances-have-continuous-physical-paving", unsupported == 0, unsupported, "samples=" + checkedPoints + " maxHeightError=" + maxError);
            Test("entrance-routes-clear-of-walls-and-furniture", blocked == 0, blocked);
            Test("all-entrances-connected-to-pedestrian-navigation", navMissing == 0, navMissing);
            Test("most-door-setbacks-under-three-metres", layout.entrances.Count(e => e.gap <= 3) >= layout.entrances.Length * .95f, layout.entrances.Count(e => e.gap <= 3));
            var choices = layout.entrances.Where(e => ctx.Free(e.inside + Vector3.up * .04f) && ctx.Free(e.sidewalk + Vector3.up * .04f)).ToArray();
            Test("entrances-have-clear-interior-and-sidewalk-approaches", choices.Length >= layout.entrances.Length * .9f, choices.Length);
            for (int i = 0; i < Mathf.Min(6, choices.Length); i++) yield return Walk(choices[i * (choices.Length / 6)], i);
            ctx.Motor.SetInputLock(true); ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true);
            var center = ReferenceMap.FromPixel(990, 355); center.y = ctx.Ground(center.x, center.z);
            Capture("01-bairro-compacto", center + new Vector3(35, 65, -65), center + Vector3.up * 2);
            var selected = layout.entrances[layout.entrances.Length / 3]; var place = ctx.Data.places.First(p => p.door != null && p.door.id == selected.door);
            var front = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, front); var doorPoint = new Vector3(place.door.x, place.elevation + 1.5f, place.door.z);
            Capture("02-porta-ligada-a-calcada", doorPoint + front * 10 + right * 10 + Vector3.up * 6, doorPoint);
            var row = layout.entrances[Mathf.Min(25, layout.entrances.Length - 1)];
            Capture("03-frentes-pavimentadas", row.sidewalk + new Vector3(-23, 25, -28), row.sidewalk + Vector3.up * 2);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish();
        }
        IEnumerator Walk(Entrance entry, int serial)
        {
            var place = ctx.Data.places.First(p => p.door != null && p.door.id == entry.door); var door = ctx.Node(entry.door); var closed = Quaternion.Euler(0, place.door.yaw * Mathf.Rad2Deg, 0);
            ctx.Motor.SetInputLock(false); ctx.Motor.SetRide(null); ctx.Motor.Warp(entry.sidewalk + Vector3.up * .05f);
            var delta = CityPhysics.Flat(entry.inside - entry.sidewalk); var direction = delta.normalized;
            // Use a known camera basis for the held W input. The normal follow
            // camera can move around nearby facades; its collision offset is
            // not an instruction to steer this test away from the entrance.
            ctx.Follow.enabled = false; ctx.Camera.transform.SetPositionAndRotation(entry.sidewalk + Vector3.up * 4 - direction * 7, Quaternion.LookRotation(direction));
            yield return new WaitForSeconds(.35f); float end = ctx.Clock + delta.magnitude / 2.5f + 2.5f; move = Vector2.up; float openedAmount = 0;
            while (ctx.Clock < end && Vector3.Dot(CityPhysics.Flat(ctx.Player.position - entry.sidewalk), direction) < delta.magnitude - .15f)
            {
                openedAmount = Mathf.Max(openedAmount, place.door.automatic ? Vector3.Distance(door.position, place.door.position) : Quaternion.Angle(closed, door.rotation));
                yield return null;
            }
            move = Vector2.zero; yield return new WaitForSeconds(.25f);
            bool arrived = place.Contains(ctx.Player.position, .05f) && ctx.Motor.Grounded && CityPhysics.Flat(ctx.Player.position - entry.inside).magnitude < 1.4f;
            Test("walk-from-sidewalk-through-door-" + serial, arrived, CityPhysics.Flat(ctx.Player.position - entry.inside).magnitude, entry.name + " player=" + ctx.Player.position + " door=" + new Vector3(place.door.x,place.elevation,place.door.z) + " grounded=" + ctx.Motor.Grounded);
            Test("door-still-operates-" + serial, openedAmount > (place.door.automatic ? .2f : 8), openedAmount, entry.name);
            if (!arrived)
            {
                foreach (var collider in Physics.OverlapSphere(ctx.Player.position + Vector3.up, .7f, ctx.SolidMask, QueryTriggerInteraction.Ignore)) report.obstacles.Add("walk " + entry.name + " " + collider.name + " owner=" + collider.transform.parent?.name);
                var front = Quaternion.Euler(0,place.yaw*Mathf.Rad2Deg,0)*Vector3.forward;
                var at = new Vector3(place.door.x,place.elevation+1.5f,place.door.z);
                Capture("failed-entry-"+serial,at+front*5+Vector3.up*2,ctx.Player.position+Vector3.up);
            }
        }
        void Capture(string name, Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = 65; camera.farClipPlane = 4000;
            bool fog = RenderSettings.fog; RenderSettings.fog = false; var active = RenderTexture.active; var rt = new RenderTexture(1600, 1000, 24); rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = active; RenderSettings.fog = fog; rt.Release(); Destroy(rt); Destroy(texture);
        }
        void Finish() { finished = true; File.WriteAllText(Path.Combine(dir, "frontage-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2); }
    }
}
