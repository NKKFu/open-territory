using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in integration QA. Never installed during a normal game.
    [DefaultExecutionOrder(-900)]
    public sealed class CityHomeProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true, captures;
            public int homes, apartmentBuildings, apartments, rooms, props;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }
        public string ReportDirectory;
        public bool Finished { get; private set; }
        readonly Report report = new Report();
        readonly List<GameObject> temporary = new List<GameObject>();
        CityContext ctx; CityHomeCatalog catalog; CityDestruction damage;
        string directory, loadout, equipped;
        Vector3 saved; Quaternion savedRotation;
        bool commerceEnabled, followEnabled, populationEnabled, policeEnabled, residentsEnabled, vehiclesEnabled;
        bool reached;
        readonly Vector3 origin = new Vector3(0, 200.04f, 0);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-interiors-qa") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "--city-home-style-qa") >= 0) new GameObject("Interiores QA opt-in").AddComponent<CityHomeProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + stack); }
        void CheckResult(string name, bool passed, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail }); Debug.Log("INTERIORS_QA " + name + " " + passed + " " + detail); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); yield return null; }
        IEnumerator PressFire() { ctx.Input.TouchFire = true; yield return Wait(.22f); }
        void View(Vector3 position, Vector3 target)
        { ctx.Follow.enabled = false; ctx.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position)); }
        void Capture(string name)
        {
            if (!report.captures) return;
            var texture = RenderTexture.GetTemporary(1280, 800, 24); var previous = ctx.Camera.targetTexture; var active = RenderTexture.active; Texture2D picture = null;
            try
            {
                ctx.Camera.targetTexture = texture; ctx.Camera.Render(); RenderTexture.active = texture;
                picture = new Texture2D(1280, 800, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); picture.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), picture.EncodeToPNG());
            }
            finally { ctx.Camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(texture); if (picture) Destroy(picture); }
        }
        IEnumerator WalkTo(Vector3 target, float timeout = 9)
        {
            reached = false; float until = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < until)
            {
                Vector3 delta = CityPhysics.Flat(target - ctx.Player.position);
                if (delta.magnitude < .18f && Mathf.Abs(ctx.Player.position.y - target.y) < .40f) { reached = true; break; }
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, ctx.Camera.transform.eulerAngles.y, 0)) * delta.normalized;
                ctx.Input.TouchMove = new Vector2(local.x, local.z) * (delta.magnitude < .5f ? .45f : .82f); ctx.Input.TouchRun = false;
                yield return null;
            }
            ctx.Input.TouchMove = Vector2.zero; yield return Wait(.08f);
        }
        CityDestructible Clone(string category, Vector3 position, Quaternion rotation)
        {
            var original = damage.Props.FirstOrDefault(p => p && !p.Broken && p.category == category && p.gameObject.activeInHierarchy);
            if (!original) { CheckResult("available-" + category, false); return null; }
            var clone = Instantiate(original.gameObject, position, rotation); clone.name = "QA • " + category; temporary.Add(clone);
            foreach (var body in clone.GetComponentsInChildren<Rigidbody>()) Destroy(body);
            foreach (var collider in clone.GetComponentsInChildren<Collider>()) collider.enabled = true;
            var prop = clone.GetComponent<CityDestructible>(); damage.Register(prop); Physics.SyncTransforms(); return prop;
        }
        void Clear(CityDestructible prop) { if (prop) prop.gameObject.SetActive(false); }
        IEnumerator Start()
        {
            var routine=Run();
            while(true)
            {
                bool more=false,failed=false;object current=null;
                try{more=routine.MoveNext();if(more)current=routine.Current;}
                catch(Exception ex){failed=true;CheckResult("probe-completed-without-exception",false,ex.ToString());}
                if(failed){Finish();yield break;}if(!more)yield break;yield return current;
            }
        }
        IEnumerator Run()
        {
            while (!CityContext.Instance || !CityContext.Instance.Ready) yield return null;
            ctx = CityContext.Instance; catalog = FindFirstObjectByType<CityHomeCatalog>(); damage = ctx.Systems.Destruction;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            bool styleOnly = Array.IndexOf(args, "--city-home-style-qa") >= 0;
            directory = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/interiors/native"));
            Directory.CreateDirectory(directory); report.captures = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            saved = ctx.Player.position; savedRotation = ctx.Player.rotation; loadout = ctx.Combat.ExportLoadoutJson(); equipped = ctx.Combat.Equipped;
            commerceEnabled = ctx.Commerce.enabled; ctx.Commerce.enabled = false;
            followEnabled = ctx.Follow.enabled; populationEnabled = ctx.Population.enabled; policeEnabled = ctx.Police.enabled; residentsEnabled = ctx.Residents.enabled; vehiclesEnabled = ctx.Vehicles.enabled;
            ctx.Population.enabled = ctx.Police.enabled = ctx.Residents.enabled = false; ctx.UI.SetSettings(false); ctx.Combat.Holster(); ctx.Police.Clear();
            CheckResult("catalog-is-baked", catalog && damage);
            if (!catalog || !damage) { Finish(); yield break; }
            report.homes = catalog.homes.Count(h => h.units == 1); report.apartmentBuildings = catalog.homes.Count(h => h.units > 1);
            report.apartments = catalog.homes.Where(h => h.units > 1).Sum(h => h.units); report.rooms = catalog.rooms.Length; report.props = damage.Props.Count;
            CheckResult("all-204-residential-buildings-furnished", catalog.homes.Length == 204);
            CheckResult("twelve-four-unit-apartment-buildings", report.apartmentBuildings == 12 && report.apartments == 48);
            CheckResult("physical-rooms-in-every-home", catalog.homes.All(h => catalog.rooms.Count(r => r.place == h.place) == (h.units == 1 ? 4 : 12)));
            CheckResult("independent-doors", catalog.doors.Length >= 900 && catalog.doors.All(d => d.leaf && d.leaf.GetComponent<CityDestructible>() && d.leaf.GetComponentsInChildren<Collider>().Length > 0));
            CheckResult("all-props-have-collisions-and-no-idle-bodies", damage.Props.All(p => p.shapes.Length > 0 && !p.GetComponent<Rigidbody>()),string.Join(", ",damage.Props.Where(p=>p.shapes.Length==0||p.GetComponent<Rigidbody>()).Take(12).Select(p=>p.category+"/"+p.nodeId)));
            var importedShapes=FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.name.StartsWith("Colisão-")).GroupBy(c=>c.name).ToDictionary(g=>g.Key,g=>g.ToArray());
            CheckResult("old-interior-solids-removed", catalog.removedSolids.Length > 1500 && catalog.removedSolids.All(slot => {
                if(slot<0||slot>=ctx.Data.solids.Length)return false;var solid=ctx.Data.solids[slot];
                Vector3 center=new Vector3(solid.x,(solid.bottom+solid.top)*.5f,solid.z);
                return !solid.active&&(!importedShapes.TryGetValue("Colisão-"+solid.index,out var shapes)||shapes.All(c=>Vector3.Distance(c.bounds.center,center)>.04f));}));
            foreach (var category in new[] { "tv", "bed", "radio", "phone", "hangers", "magazine-a", "magazine-b", "wardrobe", "counter", "sink" })
                CheckResult("every-house-has-" + category, catalog.homes.All(h => h.root.GetComponentsInChildren<CityDestructible>().Count(p => p.category == category) >= h.units));
            foreach (string category in new[] { "bench", "bin", "lamp", "signal", "planter", "door", "light" })
                CheckResult("existing-props-destructible-" + category, damage.Props.Any(p => p.category == category));
            var lighting = FindFirstObjectByType<CityLighting>();
            CheckResult("room-lights-belong-to-visible-fixtures", lighting && catalog.homes.All(h => lighting.fixtures.Count(f => f.place == h.place) >= (h.units == 1 ? 4 : 12)));
            CheckResult("upper-floors-do-not-replace-street-height", catalog.homes.Where(h => h.units > 1).All(h => {
                var p = h.root.TransformPoint(new Vector3(0, .22f, 3)); return Mathf.Abs(ctx.Ground(p.x, p.z) - p.y) < .35f; }));

            // Full-body doorway audit, including every flat on both floors.
            foreach (var leaf in catalog.doors) leaf.leaf.gameObject.SetActive(false);
            Physics.SyncTransforms(); int blocked = 0;var blockedRooms=new List<string>();
            foreach (var room in catalog.rooms)
            {
                var home = catalog.homes.First(h => h.place == room.place);
                Vector3 point = room.entrance + Vector3.up * .045f;
                if (!CityPhysics.StandClear(point, ctx.Player)) {blocked++;blockedRooms.Add(room.place+"/"+room.unit+"/"+room.name);}
            }
            foreach (var leaf in catalog.doors) leaf.leaf.gameObject.SetActive(true);
            Physics.SyncTransforms(); CheckResult("all-room-doorways-clear-for-player-capsule", blocked == 0, "blocked=" + blocked+" "+string.Join(", ",blockedRooms));

            if (styleOnly)
            {
                var styles = FindFirstObjectByType<CityHomeStyleCatalog>();
                CheckResult("all-240-dwellings-have-baked-decoration", styles && styles.units.Length == 240);
                if (!styles) { Finish(); yield break; }
                CheckResult("every-dwelling-has-a-distinct-combination", styles.units.Select(u => u.combination).Distinct().Count() == styles.units.Length);
                CheckResult("eight-palettes-six-textiles-five-art-themes", styles.units.Select(u => u.palette).Distinct().Count() == 8 && styles.units.Select(u => u.rugPattern).Distinct().Count() == 6 && styles.units.Select(u => u.art).Distinct().Count() == 5);
                CheckResult("plants-rugs-and-paintings-in-every-dwelling", styles.units.All(u => u.root &&
                    u.root.GetComponentsInChildren<CityDestructible>().Count(p => p.category == "plant") >= 2 &&
                    u.root.GetComponentsInChildren<CityDestructible>().Count(p => p.category == "painting") >= 2 &&
                    u.root.GetComponentsInChildren<MeshFilter>().Count(f => f.name.StartsWith("Rug ")) >= 2));
                int collisions = 0;
                foreach (var plant in styles.units.SelectMany(u => u.root.GetComponentsInChildren<CityDestructible>()).Where(p => p.category == "plant"))
                {
                    var box = (BoxCollider)plant.shapes[0];
                    var half = Vector3.Scale(box.size, box.transform.lossyScale) * .5f - Vector3.one * .002f;
                    if (Physics.OverlapBox(box.transform.TransformPoint(box.center), half, box.transform.rotation,
                        1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Any(c => !CityPhysics.Belongs(c, plant.transform))) collisions++;
                }
                CheckResult("new-plants-clear-of-walls-and-furniture", collisions == 0, "collisions=" + collisions);
                CheckResult("kitchen-cabinet-fronts-face-into-room", catalog.homes.All(h => h.root.GetComponentsInChildren<Transform>().Where(t => t.name == "counter").All(t => Vector3.Dot(t.forward, h.root.forward) < -.98f)));
                CheckResult("house-partitions-clear-full-front-door-assembly", catalog.homes.Where(h => h.units == 1).All(h =>
                    h.root.Cast<Transform>().Where(t => t.name == "Parede • passagens e portas").All(t => Mathf.Abs(t.localPosition.x) - .08f > 1.62f)));
            }

            // Use real movement and pushing doors in two rotated houses.
            foreach (var home in catalog.homes.Where(h => h.units == 1).OrderBy(h => styleOnly && h.place == 134 ? -1 : Vector3.Distance(h.root.position, saved)).Take(styleOnly ? 3 : 2))
            {
                var p = ctx.Data.places[home.place]; var root = home.root;
                ctx.Motor.Warp(root.TransformPoint(new Vector3(0, .23f, p.lotDepth * .5f + 1.5f)));
                View(root.TransformPoint(new Vector3(0, 2.4f, p.lotDepth * .5f + 4)), root.TransformPoint(new Vector3(0, 1.5f, 0)));
                yield return Wait(.2f);
                yield return WalkTo(root.TransformPoint(new Vector3(0, .23f, 3.25f)));
                CheckResult("walk-through-house-front-door-" + home.place, reached, ctx.Player.position.ToString());
                foreach (float z in new[] { 3.25f, -3.25f }) foreach (int side in new[] { -1, 1 })
                {
                    yield return WalkTo(root.TransformPoint(new Vector3(0, .23f, z)));
                    yield return WalkTo(root.TransformPoint(new Vector3(side * 2.45f, .23f, z)));
                    CheckResult("walk-house-room-" + home.place + "-" + side + "-" + z, reached, root.InverseTransformPoint(ctx.Player.position).ToString());
                    yield return WalkTo(root.TransformPoint(new Vector3(0, .23f, z)));
                }
                View(root.TransformPoint(new Vector3(-2.55f, 2.25f, 5.6f)), root.TransformPoint(new Vector3(-5.2f, 1.3f, 2.8f))); yield return Wait(.3f); Capture("house-living-" + home.place);
                View(root.TransformPoint(new Vector3(-2.6f, 2.25f, -1.4f)), root.TransformPoint(new Vector3(-5.2f, .8f, -4.5f))); yield return Wait(.2f); Capture("house-bedroom-" + home.place);
                if (styleOnly) { View(root.TransformPoint(new Vector3(6.9f, 3.1f, 2)), root.TransformPoint(new Vector3(1.5f, 1.8f, 5.8f))); yield return Wait(.2f); Capture("house-kitchen-" + home.place); }
            }
            if (styleOnly) { CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("; ", report.errors)); Finish(); yield break; }
            foreach (var home in catalog.homes.Where(h => h.units > 1).Take(2))
            {
                var root = home.root;
                ctx.Motor.Warp(root.TransformPoint(new Vector3(0, .23f, 6.0f))); View(root.TransformPoint(new Vector3(0, 2.6f, 7)), root.TransformPoint(new Vector3(0, 1.4f, -2)));
                bool stairs = true;
                foreach (var point in home.stairRoute) { yield return WalkTo(point, 4); if (!reached) { stairs = false; break; } }
                CheckResult("walk-up-real-stairs-" + home.place, stairs && root.InverseTransformPoint(ctx.Player.position).y > 4.25f, root.InverseTransformPoint(ctx.Player.position).ToString());
                View(root.TransformPoint(new Vector3(0, 6.6f, 5.8f)), root.TransformPoint(new Vector3(0, 5.6f, -2))); yield return Wait(.25f); Capture("apartments-upper-corridor-" + home.place);
                if (stairs)
                {
                    yield return WalkTo(root.TransformPoint(new Vector3(0, 4.55f, 3.3f)));
                    yield return WalkTo(root.TransformPoint(new Vector3(-3.1f, 4.55f, 3.3f)));
                    CheckResult("walk-into-upper-apartment-" + home.place, reached, root.InverseTransformPoint(ctx.Player.position).ToString());
                    View(root.TransformPoint(new Vector3(-2.8f, 6.6f, 6.2f)), root.TransformPoint(new Vector3(-5.0f, 5.5f, 2.4f))); yield return Wait(.25f); Capture("apartment-living-" + home.place);
                }
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "QA isolated prop floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = origin + Vector3.down * .54f; floor.transform.localScale = new Vector3(45, 1, 45); temporary.Add(floor);
            ctx.Motor.Warp(origin); ctx.Player.rotation = Quaternion.identity; ctx.Combat.Grant("sidearm"); ctx.Combat.Select("sidearm"); ctx.Input.TouchAim = true;
            var tv = Clone("tv", origin + new Vector3(0, .65f, 4), Quaternion.identity); yield return Wait(.35f);
            View(origin + new Vector3(0, 1.8f, -2), tv.transform.position + Vector3.up * .70f);
            int shots = ctx.Combat.Shots; yield return PressFire();
            CheckResult("real-fire-control-breaks-TV", tv.Broken && ctx.Combat.Shots == shots + 1 && tv.LastDamage == CityPropDamageKind.Bullet);
            CheckResult("broken-prop-removes-visual-and-solid", !tv.gameObject.activeInHierarchy && tv.shapes.All(c => !c.enabled));
            Capture("TV-destroyed-by-shot");
            var bed = Clone("bed", origin + Vector3.forward * 4, Quaternion.identity); View(origin + new Vector3(0, 1.8f, -2), bed.transform.position + Vector3.up * .58f);
            yield return Wait(.15f); yield return PressFire(); yield return Wait(.15f);
            CheckResult("solid-furniture-survives-first-shot", !bed.Broken && bed.Health < bed.durability && bed.Health > 0, "health=" + bed.Health);
            yield return PressFire(); yield return Wait(.15f); yield return PressFire();
            CheckResult("repeat-shots-break-bed", bed.Broken);
            ctx.Combat.Select("pulse");
            var pulseTV=Clone("tv",origin+new Vector3(0,.65f,4),Quaternion.identity);
            View(origin+new Vector3(0,1.8f,-2),pulseTV.transform.position+Vector3.up*.70f);yield return Wait(.25f);yield return PressFire();
            CheckResult("pulse-also-damages-props",pulseTV.Health<pulseTV.durability&&!pulseTV.Broken);
            ctx.Input.TouchReload=true;yield return Wait(1.35f);yield return PressFire();
            CheckResult("repeated-pulse-breaks-electronics",pulseTV.Broken);

            var radio = Clone("radio", origin + new Vector3(0, 1.20f, 1.0f), Quaternion.identity); ctx.Combat.Holster(); ctx.Input.TouchAim = false; ctx.Player.rotation = Quaternion.identity;
            View(origin + new Vector3(0, 2, -2), origin + new Vector3(0, 1.4f, 1)); yield return Wait(.5f);
            int swings = ctx.Combat.MeleeSwings; yield return PressFire(); yield return Wait(.4f); yield return PressFire(); yield return Wait(.45f);
            CheckResult("real-melee-control-breaks-radio", radio.Broken && ctx.Combat.MeleeSwings >= swings + 2 && radio.LastDamage == CityPropDamageKind.Melee);
            var magazine = Clone("magazine-a", origin + new Vector3(0, .62f, .72f), Quaternion.identity);
            View(origin + new Vector3(0, 2, -1), origin + new Vector3(0, .30f, .0f)); yield return Wait(.4f); yield return PressFire(); yield return Wait(.35f);
            CheckResult("descending-punch-reaches-low-magazine", magazine.Broken && magazine.LastDamage == CityPropDamageKind.Melee);

            // A non-destructible wall cannot redirect a hit to a nearby prop.
            var phone = Clone("phone", origin + new Vector3(0, 1.2f, 1.15f), Quaternion.identity);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "QA solid cover"; wall.layer = CityContext.WorldLayer; wall.transform.position = origin + new Vector3(0, 1.3f, .56f); wall.transform.localScale = new Vector3(2.4f, 2.6f, .12f); temporary.Add(wall); Physics.SyncTransforms();
            View(origin + new Vector3(0, 2, -2), origin + new Vector3(0, 1.4f, 1)); yield return Wait(.5f); yield return PressFire(); yield return Wait(.3f);
            CheckResult("wall-stops-punch-and-nearby-prop-is-safe", phone.Health == phone.durability);
            CheckResult("solid-hit-has-no-radius-fallback", !ctx.Systems.Hit(wall.GetComponent<Collider>(), Vector3.forward * 4, 200, phone.transform.position));
            wall.SetActive(false); Clear(phone);

            var lamp = Clone("light", origin + new Vector3(0, 2.5f, 2.5f), Quaternion.identity); var sources = lamp.GetComponentsInChildren<Light>();
            var fixture = new CityLighting.Fixture { kind = "QA", anchor = lamp.transform, lens = lamp.GetComponent<Renderer>(), source = sources.FirstOrDefault(), place = -1, intensity = 1 };
            lighting.fixtures = lighting.fixtures.Append(fixture).ToArray();
            lamp.Apply(100, lamp.transform.position, Vector3.down, CityPropDamageKind.Bullet); yield return Wait(.3f);
            CheckResult("broken-light-stays-off-after-lighting-tick", sources.All(l => !l.enabled) && !ctx.Systems.HackDevicePresent(new DeviceData { type = "light", target = lighting.fixtures.Length - 1 }));

            var seat = ctx.Data.seats.FirstOrDefault(s => !s.occupied && ctx.Node(s.id) && ctx.Node(s.id).gameObject.activeInHierarchy && ctx.Node(s.id).GetComponent<CityDestructible>());
            if (seat != null)
            {
                var prop = ctx.Node(seat.id).GetComponent<CityDestructible>(); prop.Apply(1000, prop.transform.position, Vector3.up, CityPropDamageKind.Melee);
                var action = ctx.Actions.First(a => a.id == "seat-" + seat.id); CheckResult("destroyed-bench-cannot-be-used", !action.Enabled());
            }
            var doorProp = damage.Props.First(p => p.category == "door" && p.gameObject.activeInHierarchy);
            doorProp.Apply(1000, doorProp.transform.position, Vector3.forward, CityPropDamageKind.Melee); yield return Wait(.6f);
            CheckResult("destroyed-door-stays-open-and-non-solid", !doorProp.gameObject.activeInHierarchy && doorProp.shapes.All(s => !s.enabled));
            var paused = Clone("bin", origin + new Vector3(5, 0, 2), Quaternion.identity); int fragments = damage.FragmentCount;
            ctx.UI.SetSettings(true); bool rejected = !paused.Apply(1000, paused.transform.position, Vector3.forward, CityPropDamageKind.Melee); yield return Wait(.35f);
            CheckResult("Esc-pauses-damage-and-debris-lifetime", rejected && !paused.Broken && paused.Health == paused.durability && damage.FragmentCount == fragments);
            ctx.UI.SetSettings(false); Clear(paused);

            // Real vehicle body + CityVehicleContact, not a simulated damage call.
            ctx.Motor.Warp(origin + new Vector3(9, 0, 1)); ctx.Vehicles.enabled = false;
            var data = ctx.Data.vehicles.First(v => !v.auto && v.kind == "car" && v.role != "police");
            var car = ctx.Vehicles.Spawn(data, origin + new Vector3(0, .2f, -5), Quaternion.identity);
            foreach (var category in new[] { "bench", "bin", "lamp", "door", "tv", "bed", "phone" })
            {
                var prop = Clone(category, origin + new Vector3(0, category=="phone"?.75f:0, 2.0f), Quaternion.identity);
                car.Body.position = origin + new Vector3(0, .08f, -5); car.Body.rotation = Quaternion.identity; car.Body.linearVelocity = Vector3.zero; car.Body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
                yield return Wait(.12f); car.Body.linearVelocity = Vector3.forward * 12;
                yield return Wait(1.35f);
                CheckResult("real-car-impact-breaks-" + category, prop.Broken && prop.LastDamage == CityPropDamageKind.Vehicle, "health=" + prop.Health + " car=" + car.Position);
                Clear(prop); car.Body.linearVelocity = Vector3.zero;
            }
            Capture("car-prop-debris"); ctx.Vehicles.Despawn(car); ctx.Vehicles.enabled = vehiclesEnabled;
            for (int i = 0; i < 20; i++)
            { var prop = Clone("radio", origin + new Vector3(-6 + i % 5, 1, 7 + i / 5), Quaternion.identity); prop.Apply(100, prop.transform.position, Vector3.up * 3, CityPropDamageKind.Bullet); }
            yield return null; CheckResult("debris-budget-is-global", damage.FragmentCount <= CityDestruction.DebrisLimit, "fragments=" + damage.FragmentCount);
            Capture("bounded-debris"); yield return Wait(8.2f); CheckResult("debris-is-cleaned-up", damage.FragmentCount == 0, "remaining=" + damage.FragmentCount);
            CheckResult("normal-scenes-have-no-other-interior-probe", FindObjectsByType<CityHomeProbe>(FindObjectsSortMode.None).Length == 1);
            CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
            Finish();
        }
        void Finish()
        {
            Finished = true; File.WriteAllText(Path.Combine(directory, "interiors-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("INTERIORS_QA_DONE " + report.passed); Restore();
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1); else Destroy(this);
        }
        void Restore()
        {
            if (!ctx) return; ctx.Input.TouchMove = Vector2.zero; ctx.Input.TouchRun = ctx.Input.TouchAim = ctx.Input.TouchFire = false;
            ctx.UI.SetSettings(false); ctx.Combat.CancelMelee(); ctx.Combat.RestoreLoadoutJson(loadout); if (!string.IsNullOrEmpty(equipped)) ctx.Combat.Select(equipped);
            ctx.Motor.SetSeat(null); ctx.Motor.Warp(saved); ctx.Player.rotation = savedRotation; ctx.Follow.enabled = followEnabled; ctx.Follow.ResetTracking();
            ctx.Population.enabled = populationEnabled; ctx.Police.enabled = policeEnabled; ctx.Residents.enabled = residentsEnabled; ctx.Vehicles.enabled = vehiclesEnabled; ctx.Commerce.enabled = commerceEnabled;
            foreach (var obj in temporary) if (obj) Destroy(obj);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (!Finished && ctx) Restore(); }
    }
}
