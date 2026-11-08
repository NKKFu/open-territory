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
    public static class CityBikeShopBuilder
    {
        public const string RootName = "Sunspoke Cycles • showroom and workshop";
        const string LibraryPath = "Assets/Imported/CityBikeShop.asset", Reports = "../Reports/bike-shop";
        static readonly int[] Places = { 0, 3 };
        [Serializable] sealed class Preservation
        {
            public bool passed; public string verifiedAtUtc; public int preservedColliders, retiredColliders, retiredObjects, modifiedFloorChunks;
            public List<string> unexpected = new List<string>();
        }
        static string Signature(Collider c)
        {
            string value = c.name + "|" + c.enabled + "|" + c.gameObject.activeSelf + "|" + c.transform.localToWorldMatrix.ToString("R");
            if (c is BoxCollider box) value += "|" + box.center.ToString("R") + "|" + box.size.ToString("R");
            if (c is MeshCollider mesh) value += "|" + (mesh.sharedMesh ? mesh.sharedMesh.GetInstanceID() : 0);
            return value;
        }
        static Vector3 Local(PlaceData p, Vector3 point) => Quaternion.Euler(0, -p.yaw * Mathf.Rad2Deg, 0) * (point - new Vector3(p.x, p.elevation, p.z));
        static bool OldLot(PlaceData p, Vector3 point) { var q = Local(p, point); return Mathf.Abs(q.x) < 11.95f && q.z > -10.95f && q.z < 12.75f; }

        [MenuItem("Open Territory/Substituir área marcada por bicicletaria")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("BaiaDoSol deve estar aberta e fora de Play Mode/build.");
            Directory.CreateDirectory(Reports + "/backups");
            EditorSceneManager.SaveScene(scene, Reports + "/backups/Before-bike-shop-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            Build(Object.FindFirstObjectByType<CityLodManager>().transform, JsonUtility.FromJson<WorldData>(registry.manifest.text));
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        public static void RefreshScenery()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("A cena deve estar salva e livre para atualizar a bicicletaria.");
            var shop = Object.FindFirstObjectByType<CityBikeShop>();
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            if (!shop || !registry) throw new InvalidOperationException("Bicicletaria ausente.");
            var library = AssetDatabase.LoadMainAssetAtPath(LibraryPath);
            // Remove references to this shop's two bicycle clones, preserving every donor.
            registry.nodes = registry.nodes.Where(n => !(n.transform && n.transform.IsChildOf(shop.transform))).ToArray();
            foreach (Transform child in shop.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            shop.displayBikes = 0;
            new Scenery(shop, library, JsonUtility.FromJson<WorldData>(registry.manifest.text)).Build();
            EditorUtility.SetDirty(registry); EditorUtility.SetDirty(shop); AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform city, WorldData world)
        {
            // Re-running the authoring command is intentionally idempotent.
            if (city.Find(RootName)) return;
            Directory.CreateDirectory(Reports);
            var before = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToDictionary(c => c, Signature);
            var root = new GameObject(RootName); root.transform.SetParent(city, false);
            root.transform.position = new Vector3(587, 9.65f, 704);
            var info = root.AddComponent<CityBikeShop>();
            var retired = new List<CityBikeShop.RetiredObject>();
            void Retire(GameObject go)
            {
                if (!go || retired.Any(r => r.target == go)) return;
                retired.Add(new CityBikeShop.RetiredObject { target = go, active = go.activeSelf }); go.SetActive(false);
            }
            foreach (int index in Places)
            {
                Retire(city.Find("Novo mapa • geometria agrupada/building:Reused building-" + index)?.gameObject);
                Retire(city.Find("Building foundations/Foundation • " + index)?.gameObject);
                foreach (var window in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name == "Janelas • " + index)) Retire(window.gameObject);
            }
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            foreach (int id in new[] { 1267, 1448 }) Retire(registry.nodes.FirstOrDefault(n => n.id == id)?.transform?.gameObject);
            var garage = Object.FindFirstObjectByType<CityFireGarage>();
            if (garage)
            {
                if (garage.parkedTruck) garage.parkedTruck.SetPositionAndRotation(garage.originalTruckPosition, garage.originalTruckRotation);
                Retire(garage.gameObject);
            }
            var lighting = Object.FindFirstObjectByType<CityLighting>();
            if (lighting) foreach (var fixture in lighting.fixtures.Where(f => Places.Contains(f.place)))
            { if (fixture.lens) Retire(fixture.lens.gameObject); if (fixture.source) Retire(fixture.source.gameObject); }

            var approved = new HashSet<int>(world.solids.Where(s => Places.Any(i => OldLot(world.places[i], new Vector3(s.x, 0, s.z)))).Select(s => s.index));
            info.retiredSolids = approved.OrderBy(i => i).ToArray();
            var collisions = new List<CityBikeShop.RetiredCollider>();
            foreach (var c in before.Keys)
                if (c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out int id) && approved.Contains(id)
                    && Places.Any(i => OldLot(world.places[i], c.transform.TransformPoint(c is BoxCollider box ? box.center : Vector3.zero))))
                { collisions.Add(new CityBikeShop.RetiredCollider { target = c, enabled = c.enabled }); c.enabled = false; }
            info.retiredColliders = collisions.ToArray(); info.retiredObjects = retired.ToArray();
            info.retiredSeats = world.seats.Where(s => Places.Any(i => OldLot(world.places[i], s.position))).Select(s => s.id).ToArray();

            var library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(LibraryPath);
            if (!library) { library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath); }
            library.sourceHash = world.sourceHash;
            CutOldFloors(info, world, library);
            Physics.SyncTransforms();
            // Check the actual remaining scene, including walls, gardens and small furniture.
            foreach (int i in Places)
            {
                var p = world.places[i]; var rotation = Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0);
                var blockers = Physics.OverlapBox(new Vector3(p.x, p.elevation + 3.1f, p.z), new Vector3(11.35f, 2.8f, 9.8f), rotation,
                    1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
                if (blockers.Length > 0) throw new InvalidOperationException("O novo prédio encontra objetos preservados: " + string.Join(", ", blockers.Select(c => c.name)));
            }
            new Scenery(info, library, world).Build();
            var changedFloors = new HashSet<Collider>(info.floorChanges.Select(f => (Collider)f.target));
            var retiredColliders = new HashSet<Collider>(collisions.Select(c => c.target));
            var report = new Preservation { verifiedAtUtc = DateTime.UtcNow.ToString("o"), retiredColliders = collisions.Count,
                retiredObjects = retired.Count, modifiedFloorChunks = changedFloors.Count };
            foreach (var pair in before)
            {
                if (retiredColliders.Contains(pair.Key) || changedFloors.Contains(pair.Key) || retired.Any(r => pair.Key.transform.IsChildOf(r.target.transform))) continue;
                report.preservedColliders++;
                if (!pair.Key || Signature(pair.Key) != pair.Value) report.unexpected.Add(pair.Key ? pair.Key.name : "Removed collider");
            }
            report.passed = report.unexpected.Count == 0;
            File.WriteAllText(Reports + "/preservation.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Colisões externas alteradas: " + string.Join(", ", report.unexpected));
            EditorUtility.SetDirty(registry); AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            Debug.Log("BIKE_SHOP_BUILD_OK preserved=" + report.preservedColliders + " displayBikes=" + info.displayBikes + " rideableBikes=" + info.bicycles.Length);
        }

        static void CutOldFloors(CityBikeShop info, WorldData world, Object library)
        {
            var changes = new List<CityBikeShop.FloorChange>();
            foreach (var floor in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!floor.name.StartsWith("Piso-") || !floor.sharedMesh || floor.bounds.max.x < 548 || floor.bounds.min.x > 623 || floor.bounds.max.z < 683 || floor.bounds.min.z > 730) continue;
                var mesh = floor.sharedMesh; var vertices = mesh.vertices; var triangles = mesh.triangles; var kept = new List<int>();
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    var a = floor.transform.TransformPoint(vertices[triangles[t]]); var b = floor.transform.TransformPoint(vertices[triangles[t + 1]]); var c = floor.transform.TransformPoint(vertices[triangles[t + 2]]);
                    bool remove = Places.Any(i => { var p = world.places[i]; return OldLot(p, a) && OldLot(p, b) && OldLot(p, c) && Mathf.Min(a.y, b.y, c.y) > p.elevation + .24f; });
                    if (!remove) { kept.Add(triangles[t]); kept.Add(triangles[t + 1]); kept.Add(triangles[t + 2]); }
                }
                if (kept.Count == triangles.Length) continue;
                var patch = Object.Instantiate(mesh); patch.name = mesh.name + " • bike shop retired floors"; patch.triangles = kept.ToArray(); patch.RecalculateBounds(); AssetDatabase.AddObjectToAsset(patch, library);
                changes.Add(new CityBikeShop.FloorChange { target = floor, original = mesh }); floor.sharedMesh = patch;
            }
            info.floorChanges = changes.ToArray();
        }

        sealed class Scenery
        {
            readonly CityBikeShop info; readonly Transform root; readonly Object library; readonly WorldData world;
            readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
            readonly List<Light> lamps = new List<Light>(); readonly List<Transform> doors = new List<Transform>();
            readonly List<Vector3> closed = new List<Vector3>();
            Transform group; Mesh cube, ring; Material cream, green, orange, dark, grey, white, glass, wood, rubber, metal;
            public Scenery(CityBikeShop info, Object library, WorldData world) { this.info = info; root = info.transform; this.library = library; this.world = world; }
            Material Mat(string name, Color color, bool transparent = false)
            {
                var m = new Material(Shader.Find(transparent ? "OpenTerritory/CityGlass" : "OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                m.SetColor("_Color", color); m.SetFloat("_Glossiness", transparent ? .7f : .18f); AssetDatabase.AddObjectToAsset(m, library); return m;
            }
            void Shape(Mesh mesh, Vector3 p, Vector3 size, Material material, Quaternion? rotation = null)
            {
                if (!batches.TryGetValue(material, out var list)) batches[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(p, rotation ?? Quaternion.identity, size) });
            }
            GameObject Box(string name, Vector3 p, Vector3 size, Material material, bool solid = false, bool floor = false, bool independent = false)
            {
                if (!independent) Shape(cube, p, size, material);
                if (!solid && !independent) return null;
                var go = new GameObject(name); go.transform.SetParent(group, false); go.transform.localPosition = p;
                go.layer = floor ? CityContext.GroundLayer : CityContext.WorldLayer;
                if (solid) go.AddComponent<BoxCollider>().size = size;
                if (independent) { go.transform.localScale = size; go.AddComponent<MeshFilter>().sharedMesh = cube; go.AddComponent<MeshRenderer>().sharedMaterial = material; if (solid) go.GetComponent<BoxCollider>().size = Vector3.one; }
                return go;
            }
            void CollisionBox(string name, Vector3 p, Vector3 size)
            {
                var go = new GameObject(name); go.transform.SetParent(group, false); go.transform.localPosition = p;
                go.layer = CityContext.WorldLayer; go.AddComponent<BoxCollider>().size = size;
            }
            void Bar(Vector3 a, Vector3 b, float width, Material material) => Shape(cube, (a + b) * .5f, new Vector3(width, Vector3.Distance(a, b), width), material, Quaternion.FromToRotation(Vector3.up, b - a));
            void Label(string text, Vector3 p, Vector2 size, int fontSize, Color color, Quaternion? rotation = null)
            {
                var go = new GameObject(text, typeof(RectTransform)); go.transform.SetParent(group, false); go.transform.localPosition = p;
                go.transform.localRotation = rotation ?? Quaternion.Euler(0, 180, 0); go.transform.localScale = Vector3.one * .01f;
                go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; ((RectTransform)go.transform).sizeDelta = size * 100;
                var label = new GameObject("Sign lettering", typeof(RectTransform)).AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = fontSize; label.fontStyle = FontStyle.Bold;
                label.color = color; label.alignment = TextAnchor.MiddleCenter; label.text = text; label.raycastTarget = false;
            }
            Mesh Ring()
            {
                var p = new List<Vector3>(); var n = new List<Vector3>(); var triangles = new List<int>();
                const int segments = 20, sides = 4;
                for (int i = 0; i < segments; i++) for (int j = 0; j < sides; j++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = j * Mathf.PI * 2 / sides;
                    var normal = new Vector3(Mathf.Sin(b), Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b));
                    p.Add(new Vector3(0, Mathf.Cos(a) * .54f, Mathf.Sin(a) * .54f) + normal * .065f); n.Add(normal);
                }
                for (int i = 0; i < segments; i++) for (int j = 0; j < sides; j++)
                { int a = i * sides + j, b = ((i + 1) % segments) * sides + j, c = ((i + 1) % segments) * sides + (j + 1) % sides, d = i * sides + (j + 1) % sides; triangles.AddRange(new[] { a, b, c, a, c, d }); }
                var mesh = new Mesh { name = "Faceted bicycle wheel • shared" }; mesh.SetVertices(p); mesh.SetNormals(n); mesh.SetTriangles(triangles, 0);
                mesh.colors = Enumerable.Repeat(Color.white, p.Count).ToArray(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
            void Bike(Vector3 offset, float yaw, Material paint, bool hanging = false)
            {
                var q = Quaternion.Euler(0, yaw, 0); Vector3 P(float x, float y, float z) => offset + q * new Vector3(x, y, z);
                foreach (float z in new[] { -.88f, .88f })
                {
                    Shape(ring, P(0, .61f, z), Vector3.one, rubber, q); Shape(ring, P(0, .61f, z), Vector3.one * .82f, metal, q);
                    for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; Bar(P(0, .61f, z), P(0, .61f + Mathf.Cos(a) * .42f, z + Mathf.Sin(a) * .42f), .018f, metal); }
                }
                var rear = P(0, .61f, -.88f); var crank = P(0, .54f, -.15f); var saddle = P(0, 1.30f, -.34f); var front = P(0, .61f, .88f); var head = P(0, 1.35f, .62f);
                foreach (var pair in new[] { new[] { rear, crank }, new[] { rear, saddle }, new[] { crank, saddle }, new[] { saddle, head }, new[] { crank, head }, new[] { front, head } }) Bar(pair[0], pair[1], .075f, paint);
                Bar(saddle, P(0, 1.52f, -.37f), .065f, metal); Shape(cube, P(0, 1.56f, -.4f), new Vector3(.35f, .10f, .49f), dark, q);
                Bar(head, P(0, 1.58f, .70f), .065f, metal); Bar(P(-.40f, 1.58f, .70f), P(.40f, 1.58f, .70f), .08f, dark);
                Bar(P(-.30f, .54f, -.15f), P(.30f, .54f, -.15f), .07f, metal);
                foreach (float side in new[] { -.35f, .35f }) Shape(cube, P(side, .54f, -.15f), new Vector3(.18f, .09f, .27f), dark, q);
                if (!hanging) CollisionBox("Bicycle display body", offset + Vector3.up * .78f, new Vector3(.85f, 1.55f, 2.9f));
                info.displayBikes++;
            }
            void Flush()
            {
                foreach (var pair in batches)
                {
                    var mesh = new Mesh { name = group.name + " • " + pair.Key.name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(mesh.name); go.transform.SetParent(group, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key; go.isStatic = true;
                }
                batches.Clear();
            }
            void Lamp(float x, float z)
            {
                Box("Ceiling light", new Vector3(x, 4.93f, z), new Vector3(2.5f, .12f, .55f), white);
                var go = new GameObject("Warm shop lighting"); go.transform.SetParent(group, false); go.transform.localPosition = new Vector3(x, 4.4f, z);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 15; light.intensity = 1.7f; light.color = new Color(1, .87f, .67f); light.shadows = LightShadows.None; lamps.Add(light);
            }
            void Ramp(Transform hall)
            {
                var end = hall.TransformPoint(new Vector3(0, 0, 15)); float y = Physics.Raycast(end + Vector3.up * 15, Vector3.down, out var hit, 30, 1 << CityContext.GroundLayer) ? hit.point.y - hall.position.y + .025f : 0;
                var mesh = new Mesh { name = hall.name + " sidewalk ramp" };
                mesh.vertices = new[] { new Vector3(-2.5f, .20f, 9.15f), new Vector3(-2.5f, y, 15), new Vector3(2.5f, y, 15), new Vector3(2.5f, .20f, 9.15f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                var go = new GameObject("Continuous sidewalk access"); go.transform.SetParent(hall, false); go.layer = CityContext.GroundLayer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = grey; go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            void Hall(int wing)
            {
                var p = world.places[Places[wing]]; group = new GameObject(wing == 0 ? "Bicycle showroom" : "Bicycle repair workshop").transform;
                group.SetParent(root, false); group.SetPositionAndRotation(new Vector3(p.x, p.elevation, p.z), Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0)); info.halls[wing] = group;
                Box("Level shop floor", new Vector3(0, .04f, 0), new Vector3(22, .32f, 19), grey, true, true);
                Box("Rear wall", new Vector3(0, 2.7f, -9.4f), new Vector3(22, 5.4f, .28f), cream, true);
                foreach (float x in new[] { -10.9f, 10.9f })
                {
                    Box("Side wall base", new Vector3(x, .70f, 0), new Vector3(.25f, 1.4f, 19), cream, true);
                    Box("Side glazing", new Vector3(x, 2.70f, 0), new Vector3(.16f, 2.6f, 18.7f), glass, true);
                    Box("Side wall lintel", new Vector3(x, 4.7f, 0), new Vector3(.28f, 1.4f, 19), cream, true);
                    foreach (float z in new[] { -9.3f, -4.6f, 0, 4.6f, 9.3f }) Box("Side window mullion", new Vector3(x, 2.5f, z), new Vector3(.32f, 4.8f, .13f), green);
                }
                foreach (float x in new[] { -6.5f, 6.5f })
                {
                    Box("Front window plinth", new Vector3(x, .40f, 9.4f), new Vector3(8.8f, .8f, .26f), cream, true);
                    Box("Front display glazing", new Vector3(x, 2.35f, 9.4f), new Vector3(8.8f, 3.1f, .15f), glass, true);
                    foreach (float dx in new[] { -4.35f, 0, 4.35f }) Box("Front glazing frame", new Vector3(x + dx, 2.25f, 9.45f), new Vector3(.12f, 4.5f, .28f), green);
                }
                Box("Entrance header", new Vector3(0, 4.5f, 9.4f), new Vector3(22.3f, 1.15f, .5f), green, true);
                Box("Roof slab", new Vector3(0, 5.38f, 0), new Vector3(22.7f, .40f, 19.7f), wing == 0 ? green : orange, true);
                foreach (float x in new[] { -11.3f, 11.3f }) Box("Roof edge trim", new Vector3(x, 5.65f, 0), new Vector3(.18f, .28f, 19.8f), cream);
                foreach (float z in new[] { -9.8f, 9.8f }) Box("Roof edge trim", new Vector3(0, 5.65f, z), new Vector3(22.8f, .28f, .18f), cream);
                foreach (float x in new[] { -5f, 5f })
                {
                    Box("Raised skylight curb", new Vector3(x, 5.64f, -1), new Vector3(3.5f, .25f, 5.2f), cream);
                    Box("Skylight", new Vector3(x, 5.80f, -1), new Vector3(3.2f, .12f, 4.9f), glass);
                    Box("Skylight central rail", new Vector3(x, 5.88f, -1), new Vector3(.09f, .08f, 5), green);
                }
                for (int side = 0; side < 2; side++)
                {
                    var door = Box("Automatic glass entrance", new Vector3(side == 0 ? -1.05f : 1.05f, 2.06f, 9.40f), new Vector3(2.04f, 3.70f, .15f), glass, true, independent: true);
                    // Separate moving frame stays attached to the leaf's scaled transform.
                    var handle = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(handle.GetComponent<Collider>()); handle.name = "Door pull"; handle.transform.SetParent(door.transform, false);
                    handle.transform.localPosition = new Vector3(side == 0 ? .37f : -.37f, 0, .65f); handle.transform.localScale = new Vector3(.025f, .22f, .85f); handle.GetComponent<Renderer>().sharedMaterial = orange;
                    doors.Add(door.transform); closed.Add(door.transform.localPosition);
                }
                Label(wing == 0 ? "SUNSPOKE CYCLES" : "REPAIRS & SERVICE", new Vector3(0, 4.62f, 9.68f), new Vector2(20, .72f), 58, new Color(.97f, .94f, .82f));
                Label(wing == 0 ? "CITY  /  TRAIL  /  EVERYDAY" : "TUNE-UPS  /  PARTS  /  CARE", new Vector3(0, 4.07f, 9.72f), new Vector2(19, .25f), 22, Color.white);
                if (wing == 0)
                {
                    foreach (float x in new[] { -6.6f, 6.6f }) foreach (float z in new[] { 3.8f, -.7f })
                    { Box("Bicycle display platform", new Vector3(x, .30f, z), new Vector3(3.6f, .20f, 3.8f), wood, true, true); Bike(new Vector3(x, .42f, z), 0, x < 0 ? orange : green); }
                    Box("Customer counter", new Vector3(-7, .83f, -5.5f), new Vector3(5.3f, 1.25f, 1.45f), green, true);
                    Box("Counter top", new Vector3(-7, 1.5f, -5.5f), new Vector3(5.5f, .15f, 1.6f), wood);
                    Box("Accessories shelf", new Vector3(6.5f, 1.8f, -8.7f), new Vector3(6, 3.2f, .65f), green, true);
                    for (int i = 0; i < 8; i++) Box("Helmet and kit boxes", new Vector3(4.2f + (i % 4) * 1.5f, 1.2f + (i / 4) * 1.1f, -8.15f), new Vector3(.9f, .55f, .65f), i % 2 == 0 ? orange : cream);
                    Label("FIND YOUR NEXT RIDE", new Vector3(0, 3.4f, -9.18f), new Vector2(18, 1.1f), 70, new Color(.14f, .34f, .29f));
                }
                else
                {
                    Box("Workshop bench", new Vector3(3.5f, .85f, -7.7f), new Vector3(12, 1.3f, 1.9f), green, true);
                    Box("Timber worktop", new Vector3(3.5f, 1.55f, -7.7f), new Vector3(12.2f, .17f, 2.1f), wood);
                    Box("Tool pegboard", new Vector3(3.5f, 2.8f, -9.15f), new Vector3(12.5f, 1.9f, .15f), dark);
                    for (int i = 0; i < 11; i++) { var a = new Vector3(-1.5f + i, 2.4f, -9); Bar(a, a + new Vector3(.15f, .6f, 0), .07f, i % 3 == 0 ? orange : metal); }
                    foreach (float x in new[] { -6.5f, 6.5f })
                    { Box("Repair stand base", new Vector3(x, .28f, .5f), new Vector3(2.2f, .16f, 2.4f), dark, true, true); Bar(new Vector3(x + .4f, .3f, .5f), new Vector3(x + .4f, 2, .5f), .12f, metal); Bike(new Vector3(x, .65f, .5f), 0, x < 0 ? orange : green, true); CollisionBox("Repair bay", new Vector3(x, 1.3f, .5f), new Vector3(2.7f, 2.2f, 3.4f)); }
                    for (int i = 0; i < 3; i++) Shape(ring, new Vector3(-7.5f + i * 1.6f, 2.8f, -9.15f), Vector3.one, rubber, Quaternion.Euler(0, 90, 0));
                    Label("KEEP THE GOOD TIMES ROLLING", new Vector3(0, 4.3f, -9.18f), new Vector2(20, .7f), 45, new Color(.14f, .34f, .29f));
                }
                foreach (float x in new[] { -5f, 5f }) { Lamp(x, 4); Lamp(x, -4); }
                Ramp(group); info.approaches[wing] = group.TransformPoint(new Vector3(0, .20f, 15)); Flush();
            }
            void RideableBikes()
            {
                var registry = Object.FindFirstObjectByType<WorldRegistry>(); var refs = registry.nodes.ToDictionary(n => n.id); var donor = world.vehicles.First(v => v.kind == "bike");
                var original = refs[donor.id].transform; var oldNodes = original.GetComponentsInChildren<Transform>(true); var nodeIds = registry.nodes.Where(n => n.transform && (n.transform == original || n.transform.IsChildOf(original))).ToDictionary(n => n.transform, n => n.id);
                var additions = new List<NodeReference>(); var vehicles = new List<VehicleData>();
                for (int bike = 0; bike < 2; bike++)
                {
                    int next = 81000 + bike * 300; var clone = Object.Instantiate(original.gameObject, root).transform; clone.name = "Sunspoke • rideable city bike " + (bike + 1);
                    var fresh = clone.GetComponentsInChildren<Transform>(true); var ids = new Dictionary<int, int>();
                    for (int j = 0; j < oldNodes.Length; j++) if (nodeIds.TryGetValue(oldNodes[j], out int id))
                    { if (refs.ContainsKey(next)) throw new InvalidOperationException("Bicycle node ID already allocated: " + next); ids[id] = next; additions.Add(new NodeReference { id = next++, transform = fresh[j] }); }
                    int Id(int id) => id != 0 && ids.TryGetValue(id, out int remapped) ? remapped : 0;
                    int[] Ids(int[] input) => input.Select(Id).ToArray();
                    var data = JsonUtility.FromJson<VehicleData>(JsonUtility.ToJson(donor));
                    data.id = Id(donor.id); data.rider = Id(donor.rider); data.driver = Id(donor.driver); data.steeringWheel = Id(donor.steeringWheel);
                    data.brakeLights = Ids(data.brakeLights); data.headlights = Ids(data.headlights); data.leftIndicators = Ids(data.leftIndicators); data.rightIndicators = Ids(data.rightIndicators); data.reverseLights = Ids(data.reverseLights);
                    data.riderLegs = Ids(data.riderLegs); data.driverHands = Ids(data.driverHands); data.driverArms = Ids(data.driverArms); data.doors = Ids(data.doors);
                    foreach (var wheel in data.wheels) { wheel.pivot = Id(wheel.pivot); wheel.spin = Id(wheel.spin); }
                    data.position = info.halls[bike].TransformPoint(new Vector3(7, .08f, 12)); data.yaw = (info.halls[bike].eulerAngles.y + 90) * Mathf.Deg2Rad; data.model = "Sunspoke city bike";
                    if (Physics.Raycast(data.position + Vector3.up * 10, Vector3.down, out var hit, 20, 1 << CityContext.GroundLayer)) data.position.y = hit.point.y + .08f;
                    clone.SetPositionAndRotation(data.position, Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0)); clone.gameObject.SetActive(true);
                    var rider = additions.FirstOrDefault(n => n.id == data.rider); if (rider != null) rider.transform.gameObject.SetActive(false);
                    vehicles.Add(data);
                }
                info.bicycles = vehicles.ToArray(); registry.nodes = registry.nodes.Concat(additions).ToArray();
            }
            public void Build()
            {
                var sample = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = Object.Instantiate(sample.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(sample);
                cube.name = "Sunspoke shared cube"; cube.colors = Enumerable.Repeat(Color.white, cube.vertexCount).ToArray(); AssetDatabase.AddObjectToAsset(cube, library); ring = Ring();
                cream = Mat("Sunspoke warm ivory", new Color(.86f, .83f, .69f)); green = Mat("Sunspoke pine green", new Color(.10f, .29f, .24f)); orange = Mat("Sunspoke burnt orange", new Color(.82f, .33f, .17f));
                dark = Mat("Sunspoke charcoal", new Color(.11f, .15f, .15f)); grey = Mat("Sunspoke pale concrete", new Color(.64f, .66f, .60f)); white = Mat("Sunspoke lighting", new Color(.98f, .93f, .75f));
                glass = Mat("Sunspoke clear glazing", new Color(.60f, .83f, .79f, .16f), true); wood = Mat("Sunspoke honey timber", new Color(.65f, .43f, .23f)); rubber = Mat("Sunspoke tyre rubber", new Color(.07f, .09f, .09f)); metal = Mat("Sunspoke brushed metal", new Color(.57f, .63f, .59f));
                info.halls = new Transform[2]; info.approaches = new Vector3[2]; Hall(0); Hall(1); info.doorLeaves = doors.ToArray(); info.closedDoors = closed.ToArray(); info.lights = lamps.ToArray();
                RideableBikes();
                var route = new List<Vector3>();
                for (int wing = 0; wing < 2; wing++) foreach (var local in new[] { new Vector3(0, .25f, 15), new Vector3(0, .25f, 11.7f), new Vector3(0, .25f, 7), new Vector3(0, .25f, -4), new Vector3(0, .25f, 7), new Vector3(0, .25f, 15) }) route.Add(info.halls[wing].TransformPoint(local));
                info.walkRoute = route.ToArray();
            }
        }
    }
}
