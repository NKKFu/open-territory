using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit native acceptance only; never attached in an ordinary game.
    public sealed class CityOutdoorDestructionProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string unity, graphics;
            public int fences, trees;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        readonly List<GameObject> fixtures = new List<GameObject>();
        CityContext ctx; CityDestruction damage; CityDestructible treeTemplate;
        string directory; bool finished; float began;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-outdoor-destruction-qa") >= 0)
                new GameObject("Outdoor destruction QA opt-in").AddComponent<CityOutdoorDestructionProbe>();
        }
        void Awake() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("OUTDOOR_DESTRUCTION_QA " + name + " " + passed + " " + detail);
        }
        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - began > 220)
            { CheckResult("timeout", false); StopAllCoroutines(); Finish(); }
        }
        static int MeshHash(Mesh mesh)
        { unchecked { int hash = 17; foreach (int i in mesh.triangles) hash = hash * 31 + i; return hash; } }
        static bool Hidden(CityDestruction.BatchedSection section) => section.pieces.All(p => p.offsets.All(i =>
            p.batch.indices[i] == p.batch.indices[i + 1] && p.batch.indices[i] == p.batch.indices[i + 2]));
        static Rigidbody[] Pool() => FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(b => b.name.StartsWith("Estilhaço • ")).ToArray();
        IEnumerator Capture(string name, Vector3 eye, Vector3 target)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            yield return new WaitForEndOfFrame();
            var previous = RenderTexture.active; var oldTarget = ctx.Camera.targetTexture;
            var texture = new RenderTexture(1280, 800, 24); var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                ctx.Camera.targetTexture = texture; ctx.Camera.Render(); RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally { ctx.Camera.targetTexture = oldTarget; RenderTexture.active = previous; Destroy(texture); Destroy(pixels); }
            yield return null;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "OutdoorDestructionQA");
            Directory.CreateDirectory(directory);
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup - began < 90) yield return null;
            ctx = CityContext.Instance; CheckResult("native-scene-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            damage = ctx.Systems.Destruction; report.unity = Application.unityVersion; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            report.fences = damage.FenceCount; report.trees = damage.TreeCount;
            ctx.Population.enabled = false; ctx.Follow.enabled = false; ctx.Systems.Weather.Request("clear", true);
            var fences = damage.Props.Where(p => p.category == "fence").ToArray();
            var trees = damage.Props.Where(p => p.category == "tree").ToArray();
            var authoredFences = JsonUtility.FromJson<CityNeighborhoodProbe.Layout>(Resources.Load<TextAsset>("neighborhood-spaces").text).walls.Length;
            // Two original coincident boundary records can share a registered
            // section. The inventory follows the authored map, including open
            // hillside gardens, instead of requiring a former wall count.
            CheckResult("authored-fences-registered", fences.Length >= authoredFences - 2, fences.Length + "/" + authoredFences);
            CheckResult("all-fence-body-and-coping-faces-owned-together", fences.All(p => p.batchSection.TriangleCount == 24),
                string.Join(",", fences.GroupBy(p => p.batchSection.TriangleCount).Select(g => g.Key + ":" + g.Count())));
            CheckResult("forest-yard-and-cemetery-trees-registered", trees.Length > 7100 && trees.Count(p => p.name == "Cipreste do cemitério") == 8, trees.Length.ToString());
            CheckResult("trees-have-solid-trunks-and-prepared-fall-meshes", trees.All(p => p.shapes.Length > 0 && p.fallingMesh && p.fallingMaterial));
            var legacy = trees.Where(p => p.batchSection != null).ToArray();
            CheckResult("reused-tree-boxes-stay-whole", legacy.Length >= 120 && legacy.All(p => p.batchSection.TriangleCount >= 84 && p.batchSection.TriangleCount % 12 == 0),
                string.Join(",", legacy.GroupBy(p => p.batchSection.TriangleCount).Select(g => g.Key + ":" + g.Count())));
            var batches = fences.Concat(legacy).SelectMany(p => p.batchSection.pieces).Select(p => p.batch).Distinct().ToArray();
            var hashes = batches.ToDictionary(b => b.source, b => MeshHash(b.source));
            treeTemplate = trees.First(p => p.batchSection == null && p.visuals.Length > 0 && p.fallingMesh.bounds.size.y > 4);
            int poolCount = Pool().Length; CheckResult("fragment-pool-prepared-before-impact", poolCount == CityDestruction.DebrisLimit);

            // Real character controller, against an actual authored city fence.
            var walkFence = fences.FirstOrDefault(p =>
            {
                var box = p.shapes[0] as BoxCollider; if (!box || box.size.z < 2.3f) return false;
                var across = p.transform.right; var a = p.transform.position - across * 1.25f; var b = p.transform.position + across * 1.25f;
                a.y = ctx.Ground(a.x, a.z) + .04f; b.y = ctx.Ground(b.x, b.z) + .04f;
                return Mathf.Abs(a.y - b.y) < .15f && ctx.Free(a, .35f) && ctx.Free(b, .35f);
            });
            CheckResult("authored-fence-has-testable-approach", walkFence, walkFence ? walkFence.name + " " + walkFence.transform.position : "none");
            if (walkFence)
            {
                var across = walkFence.transform.right; var center = walkFence.transform.position;
                var start = center - across * 1.25f; start.y = ctx.Ground(start.x, start.z) + .04f;
                ctx.Motor.SetInputLock(true); ctx.Motor.Warp(start); Physics.SyncTransforms();
                ctx.Motor.Controller.Move(across * 2.5f); float before = Vector3.Dot(ctx.Player.position - center, across);
                CheckResult("intact-fence-blocks-character", before < 0, before.ToString());
                ctx.Motor.Warp(start);
                yield return Capture("01-fence-intact", center - across * 7 + Vector3.up * 3.5f, center);
                var peers = damage.FencePeers(walkFence);
                var neighbour = fences.Where(p => p != walkFence && !peers.Contains(p)).OrderBy(p => (p.transform.position - center).sqrMagnitude).First();
                int neighbourHash = neighbour.batchSection.pieces.Sum(p => p.offsets.Sum(i => p.batch.indices[i + 1]));
                ctx.UI.SetSettings(true); float clock = ctx.Clock, health = walkFence.Health;
                bool denied = !damage.Hit(walkFence.shapes[0], 1000, center, across * 4, CityPropDamageKind.Bullet);
                yield return new WaitForSecondsRealtime(.25f);
                CheckResult("Esc-rejects-damage-and-freezes-clock", denied && walkFence.Health == health && ctx.Clock == clock);
                ctx.UI.SetSettings(false);
                damage.Hit(walkFence.shapes[0], 18, center, across * 3, CityPropDamageKind.Bullet, false);
                CheckResult("fence-takes-progressive-damage", !walkFence.Broken && walkFence.Health == 54);
                damage.Hit(walkFence.shapes[0], 1000, center, across * 4, CityPropDamageKind.Bullet, false); Physics.SyncTransforms();
                CheckResult("broken-fence-removes-all-authored-faces-and-collisions", walkFence.Broken && Hidden(walkFence.batchSection) && walkFence.shapes.All(s => !s.enabled));
                CheckResult("overlapping-lot-boundaries-break-together", peers.Count > 0 && peers.All(p => p.Broken && Hidden(p.batchSection)), "overlaps=" + peers.Count);
                CheckResult("adjacent-fence-remains-intact", !neighbour.Broken && neighbour.shapes.All(s => s.enabled) &&
                    neighbourHash == neighbour.batchSection.pieces.Sum(p => p.offsets.Sum(i => p.batch.indices[i + 1])));
                yield return new WaitForFixedUpdate(); Physics.SyncTransforms();
                ctx.Motor.Controller.Move(across * 2.5f); float after = Vector3.Dot(ctx.Player.position - center, across);
                var blockers = Physics.CapsuleCastAll(start + Vector3.up * .55f, start + Vector3.up * 1.8f, .42f, across, 2.5f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                CheckResult("character-walks-through-destroyed-fence", after > .7f, after + " blockers=" +
                    string.Join(",", blockers.Select(h => h.collider.name + "@" + h.distance + " layer=" + h.collider.gameObject.layer)));
                yield return Capture("02-fence-open", center - across * 7 + Vector3.up * 3.5f, center);
            }
            foreach (var prop in legacy.Take(2))
            {
                var point = prop.shapes[0].bounds.center;
                damage.Hit(prop.shapes[0], 1000, point, Vector3.forward * 4, CityPropDamageKind.Melee, false);
                CheckResult("reused-tree-clears-trunk-crown-and-batch-" + prop.name, prop.Broken && Hidden(prop.batchSection) && prop.shapes.All(s => !s.enabled));
            }
            // All 24 shared native tree forms must fall without editing a shared mesh.
            foreach (var tree in trees.Where(p => p.batchSection == null && !p.Broken).GroupBy(p => p.fallingMesh).Take(24).Select(g => g.First()))
            {
                damage.Hit(tree.shapes[0], 1000, tree.shapes[0].bounds.center, Vector3.forward * 4, CityPropDamageKind.Bullet, false);
                CheckResult("native-tree-variant-falls-" + tree.fallingMesh.name, tree.Broken && !tree.gameObject.activeInHierarchy && tree.shapes.All(s => !s.enabled));
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(floor); floor.layer = CityContext.GroundLayer;
            floor.name = "Outdoor destruction QA floor"; floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(80, 2, 100);
            ctx.Motor.SetInputLock(false); ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); Physics.SyncTransforms(); yield return new WaitForSeconds(.2f);
            foreach (string category in new[] { "fence", "tree" })
            {
                var target = Fixture(category, new Vector3(0, 200, 7));
                ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); ctx.Player.rotation = Quaternion.identity;
                ctx.Combat.Grant("sidearm"); ctx.Combat.Select("sidearm"); ctx.Input.TouchAim = true;
                var aim = target.transform.position + Vector3.up * (category == "fence" ? .7f : 1.6f);
                ctx.Camera.transform.SetPositionAndRotation(new Vector3(.7f, 202.6f, -2), Quaternion.LookRotation(aim - new Vector3(.7f, 202.6f, -2)));
                yield return new WaitForSeconds(.25f);
                for (int i = 0; i < 8 && !target.Broken; i++) { ctx.Combat.Fire(); yield return new WaitForSeconds(.34f); }
                CheckResult("actual-firearm-destroys-" + category, target.Broken && target.LastDamage == CityPropDamageKind.Bullet, "health=" + target.Health);
                target.gameObject.SetActive(false); ctx.Input.TouchAim = false; ctx.Combat.Holster();
                target = Fixture(category, new Vector3(0, 200, 7));
                ctx.Motor.Warp(new Vector3(0, 200.04f, 5.8f)); ctx.Player.rotation = Quaternion.identity;
                ctx.Camera.transform.SetPositionAndRotation(new Vector3(0, 203, 4.4f), Quaternion.LookRotation(target.transform.position + Vector3.up * .6f - new Vector3(0, 203, 4.4f)));
                yield return new WaitForSeconds(.15f);
                for (int i = 0; i < 10 && !target.Broken; i++) { ctx.Combat.TryPunch(); yield return new WaitForSeconds(.73f); }
                CheckResult("actual-melee-destroys-" + category, target.Broken && target.LastDamage == CityPropDamageKind.Melee, "health=" + target.Health);
                if (category == "tree" && target.Broken)
                {
                    yield return new WaitForFixedUpdate(); Physics.SyncTransforms();
                    ctx.Motor.Controller.Move(Vector3.forward * 2.5f);
                    CheckResult("character-passes-through-felled-tree-and-debris", ctx.Player.position.z > 7.7f, ctx.Player.position.ToString());
                }
                target.gameObject.SetActive(false);
            }
            ctx.Motor.SetInputLock(true); ctx.Motor.Warp(new Vector3(12, 200.04f, 0));
            var definition = ctx.Data.vehicles.First(v => !v.auto && v.kind == "car");
            var car = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == definition.id) ?? ctx.Vehicles.Spawn(definition, new Vector3(0, 200.1f, 0), Quaternion.identity);
            foreach (string category in new[] { "fence", "tree" })
            {
                var target = Fixture(category, new Vector3(0, 200, 6));
                car.Data.auto = car.Occupied = car.Commanded = car.ExitRequested = false; car.RemoteUntil = car.JamUntil = 0;
                car.Body.position = new Vector3(0, 200.1f, 1.5f); car.Body.rotation = Quaternion.identity;
                car.Root.SetPositionAndRotation(car.Body.position, car.Body.rotation); car.Body.linearVelocity = Vector3.forward * 12; car.Body.angularVelocity = Vector3.zero;
                car.Motion = new CityVehicleMath.State { Speed = 12, Direction = 1, Gear = 2 }; car.Body.WakeUp(); Physics.SyncTransforms();
                yield return new WaitForSeconds(.65f);
                CheckResult("actual-vehicle-contact-destroys-" + category, target.Broken && target.LastDamage == CityPropDamageKind.Vehicle && car.Speed > 1,
                    "health=" + target.Health + " speed=" + car.Speed);
                yield return Capture("03-impact-" + category, new Vector3(7, 204, -1), new Vector3(0, 201, 6));
                target.gameObject.SetActive(false);
            }
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero; car.Body.Sleep();
            CheckResult("imported-shared-mesh-assets-remain-immutable", hashes.All(pair => MeshHash(pair.Key) == pair.Value));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(wall); wall.layer = CityContext.WorldLayer; wall.transform.position = new Vector3(15, 201, 10);
            CheckResult("ordinary-walls-stay-solid", !damage.Hit(wall.GetComponent<Collider>(), 1000, wall.transform.position, Vector3.forward, CityPropDamageKind.Bullet) && wall.GetComponent<Collider>().enabled);
            foreach (var fence in fences.Where(p => !p.Broken).Take(20)) damage.Hit(fence.shapes[0], 1000, fence.transform.position, Vector3.forward * 4, CityPropDamageKind.Vehicle, false);
            CheckResult("saturation-reuses-fixed-pool", damage.FragmentCount == CityDestruction.DebrisLimit && Pool().Length == poolCount);
            var lastTree = Fixture("tree", new Vector3(0, 200, 14));
            damage.Hit(lastTree.shapes[0], 1000, lastTree.transform.position, Vector3.forward * 4, CityPropDamageKind.Vehicle, false);
            yield return new WaitForSeconds(.3f);
            var fallen = Pool().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponent<MeshFilter>().sharedMesh == lastTree.fallingMesh);
            CheckResult("tree-reuses-a-body-with-the-original-mesh", fallen && fallen.mass == 55 && fallen.angularVelocity.sqrMagnitude > .01f);
            ctx.UI.SetSettings(true); float pausedClock = ctx.Clock; int pausedCount = damage.FragmentCount;
            Vector3 pausedPosition = fallen ? fallen.position : Vector3.zero;
            yield return new WaitForSecondsRealtime(.35f);
            CheckResult("Esc-freezes-falling-tree-and-expiry", ctx.Clock == pausedClock && damage.FragmentCount == pausedCount && fallen && Vector3.Distance(fallen.position, pausedPosition) < .0001f);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(8.3f);
            CheckResult("all-debris-returns-after-resume", damage.FragmentCount == 0 && Pool().All(b => !b.gameObject.activeSelf && !b.detectCollisions && b.linearVelocity == Vector3.zero && b.angularVelocity == Vector3.zero && !b.GetComponent<Collider>().enabled));
            var reuse = Fixture("fence", new Vector3(0, 200, 16));
            damage.Hit(reuse.shapes[0], 1000, reuse.transform.position, Vector3.forward, CityPropDamageKind.Melee, false); yield return new WaitForSeconds(.15f);
            CheckResult("reused-debris-resets-tree-mass-mesh-and-collider", Pool().Where(b => b.gameObject.activeSelf).All(b => Mathf.Abs(b.mass - .35f) < .001f && b.GetComponent<BoxCollider>().center == Vector3.zero && b.GetComponent<BoxCollider>().size == Vector3.one));
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors)); Finish();
        }
        CityDestructible Fixture(string category, Vector3 position)
        {
            CityDestructible prop;
            if (category == "tree")
            {
                var root = Instantiate(treeTemplate.gameObject); fixtures.Add(root); root.name = "QA tree";
                root.SetActive(true); root.transform.SetPositionAndRotation(position, Quaternion.identity); root.transform.localScale = Vector3.one;
                prop = root.GetComponent<CityDestructible>(); prop.fallingMesh = treeTemplate.fallingMesh; prop.fallingMaterial = treeTemplate.fallingMaterial; prop.durability = 105;
                foreach (var shape in root.GetComponentsInChildren<Collider>(true)) shape.enabled = true;
            }
            else
            {
                var root = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(root); root.name = "QA fence"; root.layer = CityContext.WorldLayer;
                root.transform.position = position + Vector3.up * .525f; root.transform.localScale = new Vector3(2.6f, 1.05f, .22f);
                prop = root.AddComponent<CityDestructible>(); prop.category = "fence"; prop.durability = 72;
            }
            damage.Register(prop); Physics.SyncTransforms(); return prop;
        }
        void Finish()
        {
            if (finished) return; finished = true;
            if (ctx) { ctx.Input.TouchAim = false; ctx.UI.SetSettings(false); ctx.Motor.SetInputLock(false); }
            foreach (var go in fixtures) if (go) go.SetActive(false);
            File.WriteAllText(Path.Combine(directory, "outdoor-destruction-qa.json"), JsonUtility.ToJson(report, true));
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
