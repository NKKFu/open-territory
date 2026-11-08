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
    public static class CityClothingShopBuilder
    {
        public const string RootName = "Tideline • clothing store";
        const string LibraryPath = "Assets/Imported/ClothingShop.asset";
        static readonly int[] TargetPlaces = { 142, 145 };
        [Serializable] sealed class PlacementReport
        {
            public bool passed; public string createdAtUtc; public Vector3 center; public float yaw, upperFloor;
            public string[] replaced; public int originalColliders, authorizedReplacedColliders, authorizedFloorCuts, preservedColliders, preservationFailures, newColliders;
            public string[] blockers;
        }
        [MenuItem("Open Territory/Construir loja de roupas Tideline")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity") throw new InvalidOperationException("Open BaiaDoSol outside Play Mode.");
            Directory.CreateDirectory("../Reports/clothing-shop/backups");
            string backup = "../Reports/clothing-shop/backups/Before-clothing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (scene.isDirty)
            {
                Directory.CreateDirectory("Assets/Scenes/Backups");
                string copy = "Assets/Scenes/Backups/Before-clothing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
                if (!EditorSceneManager.SaveScene(scene, copy, true)) throw new IOException("Unable to preserve unsaved scene work.");
                File.Copy(copy, backup);
            }
            else File.Copy(scene.path, backup);
            var registry = Object.FindFirstObjectByType<WorldRegistry>(); var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            Build(Object.FindFirstObjectByType<CityLodManager>().transform, world);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        public static void Build(Transform city, WorldData world)
        {
            var previous = city.Find(RootName);
            if (previous)
            {
                CityClothingGroundCut.Restore(previous.GetComponent<CityClothingShop>());
                foreach (var go in previous.GetComponent<CityClothingShop>().replacedObjects) if (go) go.SetActive(true);
                Object.DestroyImmediate(previous.gameObject);
            }
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var all = city.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var replaced = new HashSet<GameObject>(); var slots = new List<int>();
            var catalog = Object.FindFirstObjectByType<CityHomeCatalog>();
            foreach (int index in TargetPlaces)
            {
                var p = world.places[index];
                if (p.name != (index == 142 ? "#639A60" : "#366604")) throw new InvalidOperationException("The marked target no longer matches the manifest.");
                foreach (var t in all)
                    if (t.name == "building:Reused residential interior " + (index - 17) || t.name == "Casa • " + p.name || t.name == "Foundation • " + index || t.name == "Janelas • " + index ||
                        t.name.StartsWith("Doorway sidelight-" + p.door.id + "-") || t.name == "Doorway canopy-" + p.door.id) replaced.Add(t.gameObject);
                var door = registry.nodes.FirstOrDefault(n => n.id == p.door.id)?.transform; if (door) replaced.Add(door.gameObject);
                for (int i = 0; i < world.solids.Length; i++)
                {
                    var solid = world.solids[i];
                    var point = new Vector3(solid.x, (solid.bottom + solid.top) * .5f, solid.z);
                    if (p.Contains(point, -.30f) && solid.top > p.elevation - .7f && solid.bottom < p.elevation + 20)
                    {
                        slots.Add(i);
                        foreach (var t in all) if (t.name == "Colisão-" + solid.index && Vector3.Distance(t.position, point) < .2f) replaced.Add(t.gameObject);
                    }
                }
                foreach (var t in all)
                {
                    var r = t.GetComponent<Renderer>();
                    if (!r || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || !p.Contains(r.bounds.center, -.12f)) continue;
                    var local = CityLighting.LocalPoint(p, r.bounds.center);
                    // Dedicated target batches own the shell; catch only detached original props.
                    if (local.y >= -.25f && local.y < 18 && r.bounds.size.x < 20 && r.bounds.size.z < 18 && !t.GetComponentInParent<Light>())
                        replaced.Add(t.gameObject);
                }
            }
            // Keep only the highest selected ancestor; this also preserves the exact prior active state.
            replaced.RemoveWhere(g => !g.activeSelf || replaced.Any(other => other != g && g.transform.IsChildOf(other.transform)));
            var before = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            bool Replaced(Collider c) => replaced.Any(g => c.transform.IsChildOf(g.transform));
            var preserved = before.Where(c => !Replaced(c)).Select(c => new { collider = c, bounds = c.bounds, enabled = c.enabled }).ToArray();
            int removedCount = before.Count(Replaced);
            foreach (var go in replaced) go.SetActive(false);

            var a = world.places[142]; var b = world.places[145];
            var root = new GameObject(RootName).transform; root.SetParent(city, false);
            root.SetPositionAndRotation(new Vector3((a.x + b.x) * .5f, a.elevation + .18f, (a.z + b.z) * .5f), Quaternion.Euler(0, a.yaw * Mathf.Rad2Deg, 0));
            var info = root.gameObject.AddComponent<CityClothingShop>(); info.upperFloor = b.elevation - a.elevation;
            info.replacedObjects = replaced.ToArray(); info.removedSolidSlots = slots.Distinct().ToArray();
            Physics.SyncTransforms();
            var foreign = Physics.OverlapBox(root.TransformPoint(new Vector3(0, 4, 0)), new Vector3(20.5f, 4, 7.16f), root.rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(root)).Select(c => c.name).Distinct().ToArray();
            if (foreign.Length != 0)
            {
                foreach (var go in replaced) go.SetActive(true); Object.DestroyImmediate(root.gameObject);
                throw new InvalidOperationException("Clothing store footprint is obstructed: " + string.Join(", ", foreign));
            }
            if (AssetDatabase.LoadMainAssetAtPath(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash; AssetDatabase.CreateAsset(library, LibraryPath);
            var groundCuts = CityClothingGroundCut.Bake(info, library);
            new Scenery(info, library).Build();
            if (catalog)
            {
                catalog.homes = catalog.homes.Where(h => !TargetPlaces.Contains(h.place)).ToArray();
                catalog.rooms = catalog.rooms.Where(r => !TargetPlaces.Contains(r.place)).ToArray();
                catalog.doors = catalog.doors.Where(d => d.leaf && !replaced.Any(g => d.leaf.IsChildOf(g.transform))).ToArray(); EditorUtility.SetDirty(catalog);
            }
            Physics.SyncTransforms();
            preserved = preserved.Where(p => !groundCuts.Contains(p.collider)).ToArray();
            int failures = preserved.Count(p => !p.collider || p.collider.enabled != p.enabled || !p.collider.gameObject.activeInHierarchy ||
                (p.collider.bounds.center - p.bounds.center).sqrMagnitude > .000001f || (p.collider.bounds.size - p.bounds.size).sqrMagnitude > .000001f);
            var report = new PlacementReport { passed = failures == 0, createdAtUtc = DateTime.UtcNow.ToString("o"), center = root.position, yaw = root.eulerAngles.y, upperFloor = info.upperFloor,
                originalColliders = before.Length, authorizedReplacedColliders = removedCount, authorizedFloorCuts = groundCuts.Count, preservedColliders = preserved.Length, preservationFailures = failures,
                newColliders = root.GetComponentsInChildren<Collider>().Length, replaced = replaced.Select(g => g.name).ToArray(), blockers = foreign };
            Directory.CreateDirectory("../Reports/clothing-shop"); File.WriteAllText("../Reports/clothing-shop/placement.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("An unrelated collider changed during replacement.");
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("CLOTHING_SHOP_BUILD_OK preserved=" + preserved.Length + " replaced=" + removedCount + " new=" + report.newColliders);
        }

        sealed class Scenery
        {
            readonly CityClothingShop info; readonly Transform root; readonly Object library;
            readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
            readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
            readonly List<Light> lights = new List<Light>(); readonly List<Transform> leaves = new List<Transform>(); readonly List<Vector3> closed = new List<Vector3>();
            Mesh cube; Material plaster, blue, coral, tile, wood, metal, white, glass, glow, skin;
            Transform group;
            public Scenery(CityClothingShop info, Object library) { this.info = info; root = info.transform; this.library = library; }
            Material Mat(string name, string hex, bool transparent = false)
            {
                string key = hex + ":" + transparent; if (palette.TryGetValue(key, out var shared)) return shared;
                ColorUtility.TryParseHtmlString(hex, out var color); var m = new Material(Shader.Find(transparent ? "OpenTerritory/CityGlass" : "OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                if (transparent) color.a = .16f; m.SetColor("_Color", color); m.SetFloat("_Glossiness", transparent ? .64f : .14f); AssetDatabase.AddObjectToAsset(m, library); palette[key] = m; return m;
            }
            void Shape(Mesh mesh, Matrix4x4 m, Material material) { if (!batches.TryGetValue(material, out var list)) batches[material] = list = new List<CombineInstance>(); list.Add(new CombineInstance { mesh = mesh, transform = m }); }
            GameObject Box(string name, Vector3 p, Vector3 size, Material material, bool solid = false, bool ground = false, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity; Shape(cube, Matrix4x4.TRS(p, q, size), material);
                if (!solid) return null; var go = new GameObject(name); go.transform.SetParent(group, false); go.transform.localPosition = p; go.transform.localRotation = q;
                go.layer = ground ? CityContext.GroundLayer : CityContext.WorldLayer; go.AddComponent<BoxCollider>().size = size; go.isStatic = true; return go;
            }
            void Label(string name, string text, Vector3 p, Vector2 size, int fontSize, Color color, bool front = true)
            {
                var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(group, false); go.transform.localPosition = p;
                go.transform.localRotation = Quaternion.Euler(0, front ? 180 : 0, 0); go.transform.localScale = Vector3.one * .01f;
                go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; ((RectTransform)go.transform).sizeDelta = size * 100;
                var t = new GameObject("Lettering", typeof(RectTransform)).AddComponent<Text>(); t.transform.SetParent(go.transform, false); t.rectTransform.anchorMin = Vector2.zero;
                t.rectTransform.anchorMax = Vector2.one; t.rectTransform.sizeDelta = Vector2.zero; t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = fontSize;
                t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter; t.color = color; t.text = text; t.raycastTarget = false;
            }
            Mesh Plane(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var mesh = new Mesh { name = name }; mesh.vertices = new[] { a, b, c, d }; mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
            void FloorPlane(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material material)
            {
                var mesh = Plane(name, a, b, c, d); Shape(mesh, Matrix4x4.identity, material); var go = new GameObject(name); go.transform.SetParent(group, false); go.layer = CityContext.GroundLayer; go.AddComponent<MeshCollider>().sharedMesh = mesh; go.isStatic = true;
            }
            void Access(float x, int index)
            {
                float h = info.Floor(x) + .02f; float w = 1.8f;
                float Street(float atX)
                {
                    var p = root.TransformPoint(new Vector3(atX, 0, 10.5f));
                    return Physics.Raycast(p + Vector3.up * 60, Vector3.down, out var hit, 100, 1 << CityContext.GroundLayer) ? hit.point.y - root.position.y + .015f : h;
                }
                FloorPlane("Sidewalk access " + index, new Vector3(x - w, Street(x - w), 10.5f), new Vector3(x - w, h, 6.8f), new Vector3(x + w, h, 6.8f), new Vector3(x + w, Street(x + w), 10.5f), tile);
                if (index == 0) info.streetApproach = root.TransformPoint(new Vector3(x, (Street(x - w) + Street(x + w)) * .5f + .04f, 10.4f));
                for (int side = 0; side < 2; side++)
                {
                    var leaf = new GameObject("Automatic entrance " + index + " leaf " + side).transform; leaf.SetParent(root, false); leaf.localPosition = new Vector3(x + (side == 0 ? -.67f : .67f), h + 1.65f, 7.04f);
                    leaf.gameObject.layer = CityContext.WorldLayer; leaf.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.33f, 3.3f, .10f);
                    var pane = new GameObject("Clear glass"); pane.transform.SetParent(leaf, false); pane.transform.localScale = new Vector3(1.3f, 3.25f, .06f); pane.AddComponent<MeshFilter>().sharedMesh = cube; pane.AddComponent<MeshRenderer>().sharedMaterial = glass;
                    foreach (float px in new[] { -.65f, .65f })
                    {
                        var frame = new GameObject("Blue door frame"); frame.transform.SetParent(leaf, false); frame.transform.localPosition = new Vector3(px, 0, 0); frame.transform.localScale = new Vector3(.055f, 3.3f, .12f);
                        frame.AddComponent<MeshFilter>().sharedMesh = cube; frame.AddComponent<MeshRenderer>().sharedMaterial = blue;
                    }
                    leaves.Add(leaf); closed.Add(leaf.localPosition);
                }
            }
            void Mannequin(float x, float z, CityWardrobe.Garment outfit, bool trousers = false)
            {
                float y = info.Floor(x); var cloth = Mat("Display " + outfit.id, "#" + ColorUtility.ToHtmlStringRGB(outfit.color));
                Box("Display plinth", new Vector3(x, y + .13f, z), new Vector3(1.4f, .26f, 1.15f), white, true);
                foreach (float side in new[] { -.19f, .19f })
                {
                    Box("Mannequin leg", new Vector3(x + side, y + .74f, z), new Vector3(.22f, .92f, .26f), trousers ? cloth : blue);
                    Box("Display shoe", new Vector3(x + side, y + .31f, z + .09f), new Vector3(.27f, .15f, .47f), white);
                    Box("Sleeve", new Vector3(x + side * 2.5f, y + 1.64f, z), new Vector3(.23f, outfit.cut == 1 ? .33f : .70f, .3f), cloth, rotation: Quaternion.Euler(0, 0, side * 35));
                }
                Box("Mannequin torso", new Vector3(x, y + 1.53f, z), new Vector3(.70f, .75f, .37f), trousers ? white : cloth);
                Box("Mannequin head", new Vector3(x, y + 2.13f, z), new Vector3(.38f, .43f, .37f), skin);
            }
            void Rack(float x, float z, int category)
            {
                float y = info.Floor(x); Box("Rack feet", new Vector3(x, y + .14f, z), new Vector3(4, .28f, 1.05f), wood, true);
                foreach (float side in new[] { -1.85f, 1.85f }) Box("Rack uprights", new Vector3(x + side, y + 1.1f, z), new Vector3(.08f, 2.2f, .09f), metal);
                Box("Clothes rail", new Vector3(x, y + 2.15f, z), new Vector3(3.8f, .07f, .07f), metal);
                for (int i = 0; i < 7; i++)
                {
                    var garment = CityWardrobe.Catalog.First(g => g.category == category && g.price > 0 && (i % 2 == 0 ? g.cut != 2 : true));
                    string hex = i % 3 == 0 ? "#E87F6C" : i % 3 == 1 ? "#2DACA7" : "#344B72"; var material = Mat("Folded fabric " + category + "-" + i, category == 1 ? "#6E8299" : hex);
                    float px = x - 1.5f + i * .5f;
                    Box("Hanger", new Vector3(px, y + 1.98f, z), new Vector3(.035f, .07f, .7f), wood);
                    Box("Hanging garment", new Vector3(px, y + 1.47f, z), new Vector3(.13f, category == 1 ? 1.05f : .84f, .65f), material);
                    if (category == 0) foreach (float side in new[] { -.36f, .36f }) Box("Hanging sleeve", new Vector3(px, y + 1.62f, z + side), new Vector3(.14f, .38f, .20f), material);
                }
                var collision = new GameObject("Rack garment collision"); collision.transform.SetParent(group, false); collision.transform.localPosition = new Vector3(x, y + 1.24f, z);
                collision.layer = CityContext.WorldLayer; collision.AddComponent<BoxCollider>().size = new Vector3(3.6f, 2.2f, .72f);
            }
            void BrowsePoint(string name, int category, Vector3 p)
            {
                var point = new GameObject(name).transform; point.SetParent(root, false); point.localPosition = p; info.browsePoints[category] = point;
            }
            void Lamp(float x, float z)
            {
                Box("Ceiling light", new Vector3(x, 7.52f, z), new Vector3(2, .10f, .34f), glow);
                var go = new GameObject("Tideline warm ceiling light"); go.transform.SetParent(root, false); go.transform.localPosition = new Vector3(x, 6.8f, z);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 13; light.intensity = 2; light.color = new Color(1, .91f, .78f); lights.Add(light);
            }
            public void Build()
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(primitive);
                cube.name = "Tideline shared box"; cube.colors = Enumerable.Repeat(Color.white, cube.vertexCount).ToArray(); AssetDatabase.AddObjectToAsset(cube, library);
                plaster = Mat("Cloud plaster", "#DEE4D7"); blue = Mat("Atlantic blue", "#244E67"); coral = Mat("Sun washed coral", "#E87F6C");
                tile = Mat("Mineral floor", "#BDC8BF"); wood = Mat("Ash wood", "#B5916B"); metal = Mat("Brushed rail", "#617779"); white = Mat("Cotton white", "#EFEFE4");
                glass = Mat("Clear shop glazing", "#9DD3CE", true); glow = Mat("Warm luminaire", "#FFF0CD"); glow.SetColor("_EmissionColor", new Color(.75f, .60f, .35f)); skin = Mat("Display mannequin", "#E5CDB0");
                group = new GameObject("Architecture and merchandise colliders").transform; group.SetParent(root, false);
                float rise = info.upperFloor;
                Box("Lower sales floor", new Vector3(12.65f, -.12f, 0), new Vector3(15.5f, .24f, 14.2f), tile, true, true);
                Box("Upper sales floor", new Vector3(-12.65f, rise - .12f, 0), new Vector3(15.5f, .24f, 14.2f), tile, true, true);
                FloorPlane("Wide interior connecting ramp", new Vector3(-5, rise, 7.1f), new Vector3(-5, rise, -7.1f), new Vector3(5, 0, -7.1f), new Vector3(5, 0, 7.1f), tile);
                Box("Lower foundation", new Vector3(12.65f, -2.1f, 0), new Vector3(15.5f, 4, 14.1f), plaster);
                Box("Upper foundation", new Vector3(-12.65f, rise - 2.1f, 0), new Vector3(15.5f, 4, 14.1f), plaster);
                foreach (float x in new[] { -20.2f, 20.2f })
                {
                    float floor = info.Floor(x); Box("Side wall", new Vector3(x, (8 + floor) * .5f, 0), new Vector3(.36f, 8 - floor, 14.2f), plaster, true);
                }
                for (int i = 0; i < 20; i++)
                {
                    float x = -19.38f + i * 2.04f, h = info.Floor(x);
                    Box("Rear wall", new Vector3(x, (8 + h) * .5f, -6.96f), new Vector3(2.06f, 8 - h, .28f), plaster, true);
                }
                Box("Roof", new Vector3(0, 8, 0), new Vector3(41.2f, .28f, 14.65f), blue, true);
                Box("Front fascia", new Vector3(0, 7.0f, 7.12f), new Vector3(41.2f, 1.8f, .42f), blue, true);
                Box("Coral seam", new Vector3(0, 6.06f, 7.35f), new Vector3(41.1f, .12f, .11f), coral);
                // The ground follows two existing lots. Each bay starts at its local floor.
                float[] divisions = { -20.2f, -16, -12.4f, -9.6f, -5, 0, 5, 9.6f, 12.4f, 16, 20.2f };
                for (int i = 1; i < divisions.Length; i++)
                {
                    float left = divisions[i - 1], right = divisions[i], x = (left + right) * .5f, h = info.Floor(x);
                    bool doorway = Mathf.Abs(x + 11) < .1f || Mathf.Abs(x - 11) < .1f;
                    if (!doorway) Box("Solid display glazing", new Vector3(x, (6 + h) * .5f, 7.02f), new Vector3(right - left - .07f, 6 - h, .08f), glass, true);
                    else Box("Entrance transom", new Vector3(x, (6 + h + 3.3f) * .5f, 7.02f), new Vector3(right - left, 6 - h - 3.3f, .08f), glass, true);
                    Box("Glazing mullion", new Vector3(left, (6 + h) * .5f, 7.04f), new Vector3(.07f, 6 - h, .16f), blue, true);
                }
                Label("Tideline facade", "T I D E L I N E", new Vector3(0, 7.22f, 7.37f), new Vector2(16, .86f), 74, Color.white);
                Label("Facade subtitle", "CLOTHING & EVERYDAY GOODS", new Vector3(0, 6.49f, 7.37f), new Vector2(13, .38f), 32, new Color(.81f, .92f, .89f));
                // Oversized stitched tab is the shop's single exterior signature.
                Box("Coral woven label", new Vector3(17.3f, 7.25f, 7.42f), new Vector3(2.5f, 1.9f, .12f), coral);
                Label("Woven label", "T / L", new Vector3(17.3f, 7.25f, 7.5f), new Vector2(2.2f, 1), 52, Color.white);
                info.entrances = new[] { new Vector3(11, 0, 7), new Vector3(-11, rise, 7) }; Access(11, 0); Access(-11, 1);
                info.doorLeaves = leaves.ToArray(); info.doorClosed = closed.ToArray();
                foreach (float x in new[] { 17.2f, 6.8f }) Mannequin(x, 5.45f, CityWardrobe.Catalog[x > 10 ? 1 : 3]);
                Mannequin(-6.8f, 5.45f, CityWardrobe.Catalog[6], true); Mannequin(-17.2f, 5.45f, CityWardrobe.Catalog[2]);
                Rack(15.5f, -2.2f, 0); Rack(8, -2.2f, 0); Rack(-9, -2.2f, 1);
                // Shoe wall and folded stock are physical shelving, with clear circulation in front.
                Box("Shoe display backing", new Vector3(-18.8f, rise + 1.65f, -3.2f), new Vector3(.26f, 3.3f, 5.3f), blue, true);
                for (int shelf = 0; shelf < 3; shelf++)
                {
                    Box("Shoe shelf", new Vector3(-18.1f, rise + .6f + shelf * .9f, -3.2f), new Vector3(1.4f, .10f, 5.3f), wood, true);
                    for (int n = 0; n < 4; n++) for (int side = 0; side < 2; side++)
                        Box("Displayed sneaker", new Vector3(-18.1f + side * .33f, rise + .77f + shelf * .9f, -5.15f + n * 1.28f), new Vector3(.24f, .24f, .55f), shelf == 0 ? white : shelf == 1 ? coral : blue);
                }
                Box("Folded stock table", new Vector3(17.2f, .55f, 1.2f), new Vector3(3.2f, 1.1f, 1.5f), wood, true);
                for (int i = 0; i < 4; i++) for (int n = 0; n < 3; n++) Box("Folded tees", new Vector3(16 + i * .8f, 1.18f + n * .11f, 1.2f), new Vector3(.67f, .10f, 1.1f), i % 2 == 0 ? coral : blue);
                Box("Cash desk", new Vector3(8, .65f, -5.7f), new Vector3(4.8f, 1.3f, 1.3f), wood, true);
                Box("Counter top", new Vector3(8, 1.33f, -5.7f), new Vector3(5, .12f, 1.5f), white);
                Box("Payment terminal", new Vector3(7.2f, 1.53f, -5.5f), new Vector3(.45f, .34f, .4f), blue);
                // Fitting room is accessible from the upper gallery; the open curtain leaves a real doorway.
                foreach (float x in new[] { -16.7f, -13.0f }) Box("Fitting room side", new Vector3(x, rise + 1.6f, -4.65f), new Vector3(.16f, 3.2f, 4.3f), plaster, true);
                Box("Gathered fitting curtain", new Vector3(-16.05f, rise + 1.5f, -2.5f), new Vector3(1.2f, 3, .12f), coral, true);
                Box("Fitting mirror", new Vector3(-14.8f, rise + 1.7f, -6.77f), new Vector3(1.65f, 2.6f, .08f), glass);
                Box("Mirror backing", new Vector3(-14.8f, rise + 1.7f, -6.85f), new Vector3(1.8f, 2.75f, .08f), metal);
                Box("Fitting bench", new Vector3(-15.4f, rise + .30f, -5.8f), new Vector3(1.4f, .6f, .65f), wood, true);
                Label("Fitting sign", "FITTING", new Vector3(-14.9f, rise + 3.45f, -2.38f), new Vector2(3.2f, .35f), 32, new Color(.14f, .31f, .41f));
                Label("Welcome", "FIND YOUR EVERYDAY", new Vector3(1.5f, 4.5f, -6.77f), new Vector2(9, .7f), 52, new Color(.14f, .31f, .41f));
                info.browsePoints = new Transform[3];
                BrowsePoint("TOPS", 0, new Vector3(11.5f, 1.65f, -1.5f));
                BrowsePoint("TROUSERS", 1, new Vector3(-9, rise + 1.65f, -1.5f));
                BrowsePoint("SNEAKERS", 2, new Vector3(-17.3f, rise + 1.65f, .1f));
                foreach (float x in new[] { -16f, -8f, 0, 8, 16 }) foreach (float z in new[] { -3.7f, 3.7f }) Lamp(x, z);
                info.lights = lights.ToArray();
                var batchRoot = new GameObject("Shared geometry by material").transform; batchRoot.SetParent(root, false);
                foreach (var pair in batches)
                {
                    var mesh = new Mesh { name = "Tideline • " + pair.Key.name, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(mesh.name); go.transform.SetParent(batchRoot, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key; go.isStatic = true;
                }
                Vector3 Route(float x, float z) => root.TransformPoint(new Vector3(x, info.Floor(x) + .05f, z));
                info.walkRoute = new[] { info.streetApproach, Route(11, 8.7f), Route(11, 5), Route(11, .6f), Route(4.5f, .6f), Route(-5.5f, .6f), Route(-9, .6f), Route(-16, 1), Route(-14.3f, -3.4f), Route(-14.3f, -4.9f), Route(-14.3f, -1.4f), Route(-11, 4.5f), Route(-11, 8.5f), Route(-11, 10.3f) };
            }
        }
    }
}
