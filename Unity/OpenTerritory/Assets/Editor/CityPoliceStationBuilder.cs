using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityPoliceStationBuilder
    {
        public const string RootName = "Baia do Sol Police • two-storey station";
        const string LibraryPath = "Assets/Imported/PoliceStation.asset";
        const string Reports = "../Reports/police-station";
        static readonly int[] OldShell = { 106, 107, 108, 109, 110, 111, 112, 113, 114, 812, 813, 814 };

        [Serializable] sealed class PlacementReport
        {
            public bool passed;
            public string createdAtUtc;
            public Vector3 center;
            public float yaw;
            public int retiredShellColliders, preservedColliders, newColliders;
            public string[] unexpectedChanges, blockers;
        }

        [MenuItem("Open Territory/Rebuild police station")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("BaiaDoSol must be saved and outside Play Mode and builds.");
            Directory.CreateDirectory(Reports + "/backups");
            File.Copy(scene.path, Reports + "/backups/Before-police-station-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            Build(Object.FindFirstObjectByType<CityLodManager>().transform, JsonUtility.FromJson<WorldData>(registry.manifest.text));
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the updated station scene.");
        }

        public static void Build(Transform city, WorldData world)
        {
            if (city.Find(RootName)) return;
            var place = world.places[CityPoliceStation.PlaceIndex];
            if (place.kind != "police" || place.door == null || place.door.id != 1576)
                throw new InvalidOperationException("The original station no longer matches its recorded site.");
            var existing = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var snapshot = existing.ToDictionary(c => c, c => new { c.enabled, active = c.gameObject.activeInHierarchy, center = c.bounds.center, size = c.bounds.size });
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var shell = city.Find("Novo mapa • geometria agrupada/building:Reused building-5");
            var glazing = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Janelas • 5");
            if (!shell || !glazing || !registry.nodes.Any(n => n.id == 1576 && n.transform))
                throw new InvalidOperationException("The original shell, glazing, or entrance could not be found.");
            var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
            var origin = new Vector3(place.x, place.elevation, place.z);
            var slots = Enumerable.Range(0, world.solids.Length).Where(i => {
                var s = world.solids[i]; var local = Quaternion.Inverse(rotation) * (new Vector3(s.x, place.elevation, s.z) - origin);
                return OldShell.Contains(s.index) && Mathf.Abs(local.x) < 12 && Mathf.Abs(local.z) < 12.4f;
            }).ToArray();
            if (slots.Length != OldShell.Length) throw new InvalidOperationException("The station shell collision records have changed.");
            var retired = new List<Collider>();
            foreach (int slot in slots)
            {
                var s = world.solids[slot]; var point = new Vector3(s.x, (s.bottom + s.top) * .5f, s.z);
                var collider = existing.FirstOrDefault(c => c.name == "Colisão-" + s.index && Vector3.Distance(c.bounds.center, point) < .2f);
                if (!collider) throw new InvalidOperationException("Missing station collision " + s.index);
                retired.Add(collider);
            }
            shell.gameObject.SetActive(false); glazing.gameObject.SetActive(false);
            foreach (var c in retired) c.enabled = false;
            Physics.SyncTransforms();
            // The old reception desk and waiting seats remain part of the station.
            var expected = new HashSet<int>(Enumerable.Range(608, 11).Concat(new[] { 16884, 16885 }));
            var blockers = Physics.OverlapBox(origin + Vector3.up * 4.6f, new Vector3(11.10f, 4.35f, 10.10f), rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => {
                    int id; return !(c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out id) && expected.Contains(id));
                }).Select(c => c.name).Distinct().ToArray();
            if (blockers.Length != 0)
            {
                shell.gameObject.SetActive(true); glazing.gameObject.SetActive(true); foreach (var c in retired) c.enabled = true;
                throw new InvalidOperationException("Objects from outside the station occupy its footprint: " + string.Join(", ", blockers));
            }
            if (AssetDatabase.LoadMainAssetAtPath(LibraryPath)) throw new InvalidOperationException("Station asset exists without its scene root.");
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash; AssetDatabase.CreateAsset(library, LibraryPath);
            var root = new GameObject(RootName).transform; root.SetParent(city, false); root.SetPositionAndRotation(origin, rotation);
            var station = root.gameObject.AddComponent<CityPoliceStation>();
            station.retiredObjects = new[] { shell.gameObject, glazing.gameObject };
            station.retiredColliders = retired.ToArray(); station.retiredSolidSlots = slots;
            var scenery = new Scenery(station, library); scenery.Build();

            var lighting = Object.FindFirstObjectByType<CityLighting>();
            if (lighting)
            {
                var old = lighting.fixtures.Where(f => f.place == CityPoliceStation.PlaceIndex).ToArray();
                foreach (var fixture in old) { if (fixture.anchor) fixture.anchor.gameObject.SetActive(false); if (fixture.source) fixture.source.gameObject.SetActive(false); }
                lighting.fixtures = lighting.fixtures.Where(f => f.place != CityPoliceStation.PlaceIndex).Concat(scenery.fixtures).ToArray();
                foreach (var room in lighting.rooms.Where(r => r.place == CityPoliceStation.PlaceIndex))
                { room.surfaces = root.GetComponentsInChildren<Renderer>(true); room.height = 9; }
                EditorUtility.SetDirty(lighting);
            }
            var windows = Object.FindFirstObjectByType<CityWindows>();
            if (windows)
            {
                windows.windows = windows.windows.Where(w => w.place != CityPoliceStation.PlaceIndex).ToArray();
                windows.glazing = windows.glazing.Where(w => w.place != CityPoliceStation.PlaceIndex).ToArray();
                EditorUtility.SetDirty(windows);
            }
            Physics.SyncTransforms();
            var changed = new List<string>();
            foreach (var entry in snapshot)
            {
                if (retired.Contains(entry.Key) || entry.Key.transform.IsChildOf(shell) || entry.Key.transform.IsChildOf(glazing)) continue;
                var c = entry.Key; var before = entry.Value;
                if (!c || c.enabled != before.enabled || c.gameObject.activeInHierarchy != before.active ||
                    (c.bounds.center - before.center).sqrMagnitude > .000001f || (c.bounds.size - before.size).sqrMagnitude > .000001f)
                    changed.Add(c ? c.name : "removed collider");
            }
            var report = new PlacementReport { passed = changed.Count == 0, createdAtUtc = DateTime.UtcNow.ToString("o"), center = origin,
                yaw = rotation.eulerAngles.y, retiredShellColliders = retired.Count, preservedColliders = snapshot.Count - retired.Count,
                newColliders = root.GetComponentsInChildren<Collider>().Length, unexpectedChanges = changed.ToArray(), blockers = blockers };
            Directory.CreateDirectory(Reports); File.WriteAllText(Reports + "/placement.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Unrelated scene colliders changed: " + string.Join(", ", changed));
            EditorUtility.SetDirty(station); AssetDatabase.SaveAssets();
            Debug.Log("POLICE_STATION_BUILD_OK preserved=" + report.preservedColliders + " stationColliders=" + report.newColliders);
        }

        // Refresh only this authored model, retaining the original entrance,
        // retired collision records, and every other part of the city.
        public static void RefreshScenery()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("BaiaDoSol must be saved and outside Play Mode and builds.");
            var station = Object.FindFirstObjectByType<CityPoliceStation>();
            var library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(LibraryPath);
            if (!station || !library) throw new InvalidOperationException("The authored station is missing.");
            Directory.CreateDirectory(Reports + "/backups");
            File.Copy(scene.path, Reports + "/backups/Before-refresh-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity");
            var lighting = Object.FindFirstObjectByType<CityLighting>();
            if (lighting) lighting.fixtures = lighting.fixtures.Where(f => f.place != CityPoliceStation.PlaceIndex).ToArray();
            foreach (Transform child in station.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(LibraryPath).Where(a => a && a != library).ToArray()) Object.DestroyImmediate(asset, true);
            var scenery = new Scenery(station, library); scenery.Build();
            if (lighting)
            {
                lighting.fixtures = lighting.fixtures.Concat(scenery.fixtures).ToArray();
                foreach (var room in lighting.rooms.Where(r => r.place == CityPoliceStation.PlaceIndex)) room.surfaces = station.GetComponentsInChildren<Renderer>(true);
                EditorUtility.SetDirty(lighting);
            }
            EditorUtility.SetDirty(station); Physics.SyncTransforms(); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the refreshed station.");
        }

        sealed class Scenery
        {
            readonly CityPoliceStation station; readonly Transform root; readonly Object library;
            readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
            readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
            public readonly List<CityLighting.Fixture> fixtures = new List<CityLighting.Fixture>();
            readonly List<Transform> gates = new List<Transform>(); readonly List<Vector3> anchors = new List<Vector3>();
            Mesh cube; Material plaster, blue, deep, trim, glass, metal, floor, wood, paper, glow, dark;
            const float Ground = .18f, Upper = 4.50f;
            public Scenery(CityPoliceStation station, Object library) { this.station = station; root = station.transform; this.library = library; }

            Material Mat(string name, string hex, bool transparent = false)
            {
                string key = hex + transparent; if (palette.TryGetValue(key, out var existing)) return existing;
                ColorUtility.TryParseHtmlString(hex, out var c); if (transparent) c.a = .30f;
                var m = new Material(Shader.Find(transparent ? "OpenTerritory/CityGlass" : "OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                m.SetColor("_Color", c); m.SetFloat("_Glossiness", transparent ? .55f : .14f); AssetDatabase.AddObjectToAsset(m, library); palette[key] = m; return m;
            }
            void Shape(Vector3 p, Vector3 size, Material material, Quaternion q)
            {
                if (!batches.TryGetValue(material, out var list)) batches[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(p, q, size) });
            }
            void Box(string name, Vector3 p, Vector3 size, Material material, bool solid = false, bool ground = false, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity; Shape(p, size, material, q);
                if (!solid) return;
                var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = p; go.transform.localRotation = q;
                go.layer = ground ? CityContext.GroundLayer : CityContext.WorldLayer; go.AddComponent<BoxCollider>().size = size; go.isStatic = true;
            }
            void ColliderOnly(string name, Vector3 p, Vector3 size)
            {
                var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = p;
                go.layer = CityContext.WorldLayer; go.AddComponent<BoxCollider>().size = size; go.isStatic = true;
            }
            void Label(string text, Vector3 p, Vector2 size, int fontSize, Quaternion rotation)
            {
                var go = new GameObject("Sign • " + text, typeof(RectTransform)); go.transform.SetParent(root, false); go.transform.localPosition = p;
                go.transform.localRotation = rotation; go.transform.localScale = Vector3.one * .01f;
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; ((RectTransform)go.transform).sizeDelta = size * 100;
                var label = new GameObject("Lettering", typeof(RectTransform)).AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = fontSize; label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter; label.color = new Color(.95f, .97f, .89f); label.text = text; label.raycastTarget = false;
            }
            void WindowWall(Vector3 point, Quaternion rot, float width, float height, float[] openings, float windowWidth = 2.2f, float sill = 1.25f, float head = 3.20f)
            {
                Vector3 P(float x, float y, float z = 0) => point + rot * new Vector3(x, y, z);
                void Part(string n, float x, float y, float z, float w, float h, float d, Material m, bool solid = false)
                    => Box(n, P(x, y, z), new Vector3(w, h, d), m, solid, false, rot);
                float start = -width * .5f;
                foreach (float at in openings.OrderBy(v => v))
                {
                    float end = at - windowWidth * .5f;
                    if (end > start) Part("Exterior wall", (start + end) * .5f, height * .5f, 0, end - start, height, .30f, plaster, true);
                    Part("Window sill wall", at, sill * .5f, 0, windowWidth, sill, .30f, plaster, true);
                    Part("Window lintel wall", at, (head + height) * .5f, 0, windowWidth, height - head, .30f, plaster, true);
                    Part("Window glass", at, (sill + head) * .5f, 0, windowWidth - .08f, head - sill - .08f, .09f, glass, true);
                    foreach (int side in new[] { -1, 1 }) Part("Window frame", at + side * windowWidth * .5f, (sill + head) * .5f, 0, .10f, head - sill + .18f, .38f, trim);
                    Part("Window frame", at, sill, 0, windowWidth + .12f, .10f, .38f, trim);
                    Part("Window frame", at, head, 0, windowWidth + .12f, .10f, .38f, trim);
                    Part("Window mullion", at, (sill + head) * .5f, .08f, .055f, head - sill, .12f, trim);
                    start = at + windowWidth * .5f;
                }
                if (start < width * .5f) Part("Exterior wall", (start + width * .5f) * .5f, height * .5f, 0, width * .5f - start, height, .30f, plaster, true);
            }
            void Lamp(float x, float y, float z)
            {
                Box("Ceiling light", new Vector3(x, y, z), new Vector3(1.5f, .10f, .55f), glow);
                var go = new GameObject("Station light"); go.transform.SetParent(root, false); go.transform.localPosition = new Vector3(x, y - .12f, z);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 10; light.color = new Color(1, .91f, .77f); light.intensity = 1.1f; light.shadows = LightShadows.None; light.enabled = false;
                fixtures.Add(new CityLighting.Fixture { place = CityPoliceStation.PlaceIndex, kind = "station", anchor = go.transform, source = light, intensity = 1.1f });
            }
            void Cell(int index, float center)
            {
                float front = -5.15f, back = -10.18f;
                if (index == 0) Box("Cell end wall", new Vector3(center - 1.58f, 2.08f, (front + back) * .5f), new Vector3(.18f, 3.8f, front - back), plaster, true);
                Box("Cell dividing wall", new Vector3(center + 1.58f, 2.08f, (front + back) * .5f), new Vector3(.18f, 3.8f, front - back), plaster, true);
                foreach (int side in new[] { -1, 1 })
                {
                    float x = center + side * 1.18f;
                    ColliderOnly("Fixed cell grille", new Vector3(x, 1.76f, front), new Vector3(.78f, 3.16f, .14f));
                    for (int j = -2; j <= 2; j++) Box("Cell bars", new Vector3(x + j * .17f, 1.76f, front), new Vector3(.065f, 3.05f, .10f), metal);
                    foreach (float y in new[] { .25f, 1.45f, 3.25f }) Box("Cell grille rail", new Vector3(x, y, front), new Vector3(.78f, .09f, .12f), metal);
                }
                Box("Cell lintel", new Vector3(center, 3.56f, front), new Vector3(3.0f, .45f, .22f), deep, true);
                var hinge = new GameObject("Holding cell " + (index + 1) + " • gate").transform; hinge.SetParent(root, false); hinge.localPosition = new Vector3(center - .70f, Ground, front);
                var collision = hinge.gameObject.AddComponent<BoxCollider>(); collision.center = new Vector3(.70f, 1.55f, 0); collision.size = new Vector3(1.38f, 3.1f, .14f); hinge.gameObject.layer = CityContext.WorldLayer;
                void GatePart(Vector3 p, Vector3 s, Material material)
                {
                    var go = new GameObject("Gate frame"); go.transform.SetParent(hinge, false); go.transform.localPosition = p; go.transform.localScale = s;
                    go.AddComponent<MeshFilter>().sharedMesh = cube; go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                foreach (float x in new[] { .04f, 1.36f }) GatePart(new Vector3(x, 1.55f, 0), new Vector3(.08f, 3.1f, .13f), metal);
                foreach (float y in new[] { .12f, 1.28f, 2.95f }) GatePart(new Vector3(.7f, y, 0), new Vector3(1.38f, .09f, .13f), metal);
                for (int i = 1; i <= 6; i++) GatePart(new Vector3(i * .20f, 1.55f, 0), new Vector3(.055f, 2.9f, .08f), metal);
                GatePart(new Vector3(1.17f, 1.35f, .12f), new Vector3(.15f, .22f, .12f), trim);
                gates.Add(hinge); anchors.Add(new Vector3(center, 1.45f, front));
                Box("Cell bunk", new Vector3(center, .56f, -9.00f), new Vector3(2.40f, .76f, 1.65f), deep, true);
                Box("Cell mattress", new Vector3(center, 1.01f, -9.00f), new Vector3(2.22f, .16f, 1.48f), paper);
                Box("Cell pillow", new Vector3(center - .72f, 1.13f, -9.00f), new Vector3(.50f, .15f, 1.20f), trim);
                Box("Cell number plate", new Vector3(center, 3.40f, front + .13f), new Vector3(.55f, .34f, .08f), blue);
                Label("0" + (index + 1), new Vector3(center, 3.40f, front + .18f), new Vector2(.50f, .30f), 28, Quaternion.Euler(0, 180, 0));
            }
            void Staircase()
            {
                var route = new List<Vector3> { new Vector3(0, Ground + .04f, 9.2f), new Vector3(4.8f, Ground + .04f, 8.0f), new Vector3(7.0f, Ground + .04f, 7.1f) };
                for (int i = 0; i < 12; i++)
                {
                    float h = (i + 1) * .18f, z = 6.62f - i * .34f;
                    Box("Stair • lower flight", new Vector3(7, Ground + h * .5f, z), new Vector3(1.65f, h, .35f), floor, true);
                }
                Box("Stair • turning landing", new Vector3(8, Ground + 2.16f - .10f, 2.18f), new Vector3(3.65f, .20f, 1.30f), floor, true);
                for (int i = 0; i < 12; i++)
                {
                    float h = 2.16f + (i + 1) * .18f, z = 2.73f + i * .34f;
                    Box("Stair • upper flight", new Vector3(9, Ground + h - .09f, z), new Vector3(1.65f, .18f, .35f), floor, true);
                }
                Box("Stair • top landing", new Vector3(9, Upper - .10f, 7.0f), new Vector3(1.65f, .20f, .55f), floor, true);
                foreach (float x in new[] { 6.05f, 10.05f })
                {
                    Box("Stairwell upper handrail", new Vector3(x, Upper + .58f, 4.32f), new Vector3(.08f, .08f, 5.5f), metal);
                    for (int i = 0; i < 7; i++) Box("Stairwell rail post", new Vector3(x, Upper + .48f, 1.85f + i * .80f), new Vector3(.07f, .96f, .07f), metal);
                }
                route.AddRange(new[] { new Vector3(7, Ground + 2.16f, 2.45f), new Vector3(8, Ground + 2.16f, 2.18f), new Vector3(9, Ground + 2.16f, 2.45f),
                    new Vector3(9, Upper + .04f, 7.10f), new Vector3(4.6f, Upper + .04f, 8.0f), new Vector3(0, Upper + .04f, 3.0f) });
                station.stairRoute = route.Select(root.TransformPoint).ToArray();
            }
            void Furniture()
            {
                // These two groups use the original, preserved collision and seating data.
                Box("Reception desk", new Vector3(4, .805f, -4.8f), new Vector3(7, 1.25f, 1.6f), blue);
                Box("Reception counter", new Vector3(4, 1.505f, -4.8f), new Vector3(7.2f, .15f, 1.8f), trim);
                Box("Reception inset", new Vector3(4, 1.0f, -3.96f), new Vector3(5.7f, .54f, .04f), deep);
                Box("Reception monitor", new Vector3(4, 1.92f, -4.9f), new Vector3(1.05f, .70f, .12f), dark);
                Box("Reception screen", new Vector3(4, 1.92f, -4.82f), new Vector3(.88f, .51f, .03f), glass);
                foreach (float x in new[] { -7f, -5.4f, -3.8f })
                {
                    Box("Waiting chair base", new Vector3(x, .42f, 3.96f), new Vector3(1.13f, .48f, 1.04f), deep);
                    Box("Waiting chair cushion", new Vector3(x, .72f, 4.07f), new Vector3(1.23f, .19f, 1.07f), blue);
                    Box("Waiting chair back", new Vector3(x, 1.14f, 3.52f), new Vector3(1.25f, 1.06f, .24f), blue);
                }
                Box("Notice board", new Vector3(-11.28f, 2.65f, -.6f), new Vector3(.10f, 2.0f, 3.2f), wood);
                Box("Notice papers", new Vector3(-11.20f, 2.65f, -.6f), new Vector3(.05f, 1.70f, 2.9f), paper);
                station.noticeAnchor = root.TransformPoint(new Vector3(-10.95f, 2.65f, -.6f));
                Label("RECEPTION", new Vector3(4, 3.25f, -9.95f), new Vector2(4.2f, .48f), 36, Quaternion.Euler(0, 180, 0));
                // Upstairs is an open workroom with circulation to the stair landing.
                foreach (float x in new[] { -6.7f, -2.6f, 1.5f })
                {
                    Box("Office desk", new Vector3(x, Upper + .82f, -3.0f), new Vector3(3.0f, .16f, 1.45f), wood, true);
                    foreach (int side in new[] { -1, 1 }) Box("Desk leg", new Vector3(x + side * 1.25f, Upper + .40f, -3.0f), new Vector3(.14f, .8f, 1.1f), metal, true);
                    Box("Office monitor", new Vector3(x, Upper + 1.32f, -3.25f), new Vector3(1.0f, .72f, .10f), dark);
                    Box("Office screen", new Vector3(x, Upper + 1.32f, -3.18f), new Vector3(.84f, .55f, .03f), glass);
                    Box("Office chair", new Vector3(x, Upper + .38f, -1.4f), new Vector3(.92f, .65f, .92f), blue, true);
                    Box("Office chair back", new Vector3(x, Upper + 1.08f, -1.02f), new Vector3(.92f, .84f, .15f), deep);
                }
                Box("Records cabinet", new Vector3(8.9f, Upper + 1.25f, -8.8f), new Vector3(3.1f, 2.5f, 1.2f), blue, true);
                for (int i = 0; i < 3; i++) Box("Cabinet drawer", new Vector3(8.9f, Upper + .54f + i * .68f, -8.17f), new Vector3(2.7f, .55f, .05f), deep);
                Box("Meeting table", new Vector3(-5.3f, Upper + .77f, 5.7f), new Vector3(4.6f, .15f, 2.0f), wood, true);
                foreach (float x in new[] { -6.9f, -3.7f }) Box("Meeting table leg", new Vector3(x, Upper + .38f, 5.7f), new Vector3(.14f, .76f, 1.5f), metal, true);
                Box("Office pinboard", new Vector3(-.2f, Upper + 2.4f, -10.28f), new Vector3(4.7f, 1.8f, .08f), wood);
                for (int i = 0; i < 4; i++) Box("Notice", new Vector3(-1.8f + i * 1.05f, Upper + 2.4f, -10.20f), new Vector3(.76f, 1.15f, .025f), paper);
            }
            public void Build()
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(primitive);
                cube.name = "Police station shared cube"; cube.colors = Enumerable.Repeat(Color.white, cube.vertexCount).ToArray(); AssetDatabase.AddObjectToAsset(cube, library);
                plaster = Mat("Civic limestone", "#D2D8C8"); blue = Mat("Station blue", "#416D82"); deep = Mat("Deep navy", "#294D60");
                trim = Mat("Ivory trim", "#F0E8D3"); glass = Mat("Blue green glass", "#82B2B2", true); metal = Mat("Dark metal", "#4A646A");
                floor = Mat("Interior tile", "#C8CDC0"); wood = Mat("Warm timber", "#A88C6F"); paper = Mat("Paper", "#E6E7D7");
                glow = Mat("Lamp diffuser", "#FFF1C7"); dark = Mat("Screens", "#273D42");
                Box("Ground floor", new Vector3(0, .10f, 0), new Vector3(22.6f, .16f, 20.6f), floor, true, true);
                Box("Upper floor main", new Vector3(-2.65f, Upper - .10f, 0), new Vector3(17.3f, .20f, 20.6f), floor, true);
                Box("Upper floor right edge", new Vector3(10.7f, Upper - .10f, 0), new Vector3(1.2f, .20f, 20.6f), floor, true);
                Box("Upper floor rear", new Vector3(8.1f, Upper - .10f, -4.55f), new Vector3(4.2f, .20f, 11.5f), floor, true);
                Box("Upper floor front", new Vector3(8.1f, Upper - .10f, 8.70f), new Vector3(4.2f, .20f, 3.2f), floor, true);
                for (int story = 0; story < 2; story++)
                {
                    float y = story == 0 ? Ground : Upper; float h = 4.14f;
                    WindowWall(new Vector3(0, y, -10.45f), Quaternion.identity, 22.8f, h, new[] { -8f, -4f, 0f, 4f, 8f }, story == 0 ? 1.45f : 2.2f, story == 0 ? 2.25f : 1.25f, 3.20f);
                    foreach (float side in new[] { -1f, 1f }) WindowWall(new Vector3(side * 11.35f, y, 0), Quaternion.Euler(0, 90, 0), 20.6f, h, new[] { -7.5f, -3.8f, 0f, 3.8f, 7.5f });
                    if (story == 0)
                    {
                        foreach (float side in new[] { -1f, 1f }) WindowWall(new Vector3(side * 6.45f, y, 10.45f), Quaternion.identity, 9.9f, h, new[] { -3.1f, 0f, 3.1f });
                        Box("Entrance lintel", new Vector3(0, y + 3.76f, 10.45f), new Vector3(3.0f, .76f, .3f), plaster, true);
                    }
                    else WindowWall(new Vector3(0, y, 10.45f), Quaternion.identity, 22.8f, h, new[] { -8f, -4f, 0f, 4f, 8f });
                }
                Box("Floor band front", new Vector3(0, 4.43f, 10.61f), new Vector3(23f, .30f, .18f), blue);
                Box("Floor band rear", new Vector3(0, 4.43f, -10.61f), new Vector3(23f, .30f, .18f), blue);
                foreach (float side in new[] { -1f, 1f }) Box("Floor band side", new Vector3(side * 11.51f, 4.43f, 0), new Vector3(.18f, .30f, 21f), blue);
                Box("Roof slab", new Vector3(0, 8.78f, 0), new Vector3(23.1f, .28f, 20.9f), deep, true);
                Box("Roof front parapet", new Vector3(0, 9.04f, 10.35f), new Vector3(23.0f, .46f, .25f), trim);
                Box("Roof rear parapet", new Vector3(0, 9.04f, -10.35f), new Vector3(23.0f, .46f, .25f), trim);
                foreach (float side in new[] { -1f, 1f }) Box("Roof side parapet", new Vector3(side * 11.35f, 9.04f, 0), new Vector3(.25f, .46f, 20.7f), trim);
                Box("Roof service housing", new Vector3(5, 9.20f, -4), new Vector3(3.2f, .65f, 2.1f), floor);
                for (int i = 0; i < 4; i++) Box("Roof vent", new Vector3(4.0f + i * .65f, 9.54f, -4), new Vector3(.22f, .08f, 1.55f), metal);
                Box("Entrance canopy", new Vector3(0, 3.85f, 11.15f), new Vector3(9.0f, .24f, 1.5f), deep);
                Box("Canopy trim", new Vector3(0, 3.83f, 11.90f), new Vector3(9.0f, .18f, .08f), trim);
                Box("Station sign", new Vector3(0, 5.05f, 10.68f), new Vector3(10.5f, 1.05f, .18f), deep);
                Label("POLICE STATION", new Vector3(0, 5.06f, 10.80f), new Vector2(10.0f, .9f), 65, Quaternion.Euler(0, 180, 0));
                Box("Badge backing", new Vector3(-7.9f, 3.35f, 10.65f), new Vector3(1.15f, 1.15f, .16f), blue);
                Label("★", new Vector3(-7.9f, 3.36f, 10.75f), new Vector2(1.0f, 1.0f), 55, Quaternion.Euler(0, 180, 0));
                Furniture(); Staircase();
                for (int i = 0; i < 3; i++) Cell(i, -9.05f + i * 3.2f);
                foreach (float x in new[] { -6f, 5f }) foreach (float z in new[] { -2f, 6f }) { Lamp(x, 4.20f, z); Lamp(x, 8.56f, z); }
                station.gates = gates.ToArray(); station.gateAnchors = anchors.ToArray(); station.streetApproach = root.TransformPoint(new Vector3(0, Ground + .04f, 13.0f));
                foreach (var pair in batches)
                {
                    var mesh = new Mesh { name = "Police station • " + pair.Key.name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(mesh.name); go.transform.SetParent(root, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key; go.isStatic = true;
                }
            }
        }
    }
}
