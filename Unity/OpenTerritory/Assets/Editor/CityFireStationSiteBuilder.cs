using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityFireStationSiteBuilder
    {
        public const string RootName = "Fire Station • waterfront site";
        const string LibraryPath = "Assets/Imported/FireStationSite.asset";
        public static readonly Vector3 Site = new Vector3(-110.14345f, 9.023f, 452.90174f);
        public static readonly Quaternion Rotation = Quaternion.Euler(0, 73.1465f, 0);
        static readonly int[] EntranceIds = { 5297, 5298, 5299, 5300 };
        static double nextRequestCheck;
        [InitializeOnLoadMethod]
        static void WatchRequest() { EditorApplication.update += CheckRequest; }
        static void CheckRequest()
        {
            if (EditorApplication.timeSinceStartup < nextRequestCheck) return;
            nextRequestCheck = EditorApplication.timeSinceStartup + 1;
            const string request = "../Reports/fire-station-red/apply.request";
            if (!File.Exists(request) || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            File.Move(request, "../Reports/fire-station-red/apply-consumed-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            try { Rebuild(); File.WriteAllText("../Reports/fire-station-red/apply-complete.txt", DateTime.UtcNow.ToString("o")); }
            catch (Exception e) { File.WriteAllText("../Reports/fire-station-red/apply-error.txt", e.ToString()); Debug.LogException(e); }
        }
        static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        public static bool Reserved(float x, float z, float margin = 0)
        {
            var p = Quaternion.Inverse(Rotation) * (new Vector3(x, Site.y, z) - Site);
            return Mathf.Abs(p.x) < 13.4f + margin && Mathf.Abs(p.z) < 13.4f + margin ||
                p.x > -11 - margin && p.x < 3 + margin && p.z > 11 && p.z < 29.2f + margin;
        }
        static bool OriginalLot(PlaceData p, Vector3 point)
        {
            var q = Quaternion.Euler(0, -p.yaw * Mathf.Rad2Deg, 0) * (point - new Vector3(p.x, p.elevation, p.z));
            return Mathf.Abs(q.x) < 11.96f && q.z > -10.96f && q.z < 12.76f;
        }

        [MenuItem("Open Territory/Mover quartel vermelho para a orla")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Salve BaiaDoSol e saia do Play Mode/build.");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            Directory.CreateDirectory("../Reports/fire-station-red/backups");
            File.Copy(scene.path, "../Reports/fire-station-red/backups/Before-move-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity");
            Build(Object.FindFirstObjectByType<CityLodManager>().transform, JsonUtility.FromJson<WorldData>(registry.manifest.text));
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform city, WorldData world)
        {
            if (Object.FindFirstObjectByType<CityFireStationSite>()) return;
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var source = world.places[CityFireStationSite.PlaceIndex];
            if (source.kind != "fire") throw new InvalidOperationException("Original fire station record missing.");
            Physics.SyncTransforms();
            var blockers = Physics.OverlapBox(Site + Vector3.up * 8, new Vector3(13.5f, 8, 13.5f), Rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            var access = Physics.OverlapBox(Site + Rotation * new Vector3(-4, 1.2f, 18.5f), new Vector3(6.5f, 1.2f, 5.5f), Rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            if (blockers.Length + access.Length > 0) throw new InvalidOperationException("Occupied site: " + string.Join(", ", blockers.Concat(access).Select(c => c.name)));
            if (AssetDatabase.LoadMainAssetAtPath(LibraryPath)) throw new InvalidOperationException("Relocation library already exists without its scene root.");
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath);
            var original = Matrix4x4.TRS(new Vector3(source.x, source.elevation, source.z), Quaternion.Euler(0, source.yaw * Mathf.Rad2Deg, 0), Vector3.one);
            var destination = Matrix4x4.TRS(Site, Rotation, Vector3.one);
            var move = destination * original.inverse;
            var yawDelta = Rotation.eulerAngles.y * Mathf.Deg2Rad - source.yaw;
            var root = new GameObject(RootName); root.transform.SetParent(city, false);
            root.transform.SetPositionAndRotation(original.GetColumn(3), original.rotation);
            var info = root.AddComponent<CityFireStationSite>();
            var bike = Object.FindFirstObjectByType<CityBikeShop>();
            var allLods = Object.FindObjectsByType<CityObjectLod>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var shell = allLods.First(l => l.name == "building:Reused building-3");
            var glazing = allLods.First(l => l.name == "Janelas • 3");
            var garage = Object.FindFirstObjectByType<CityFireGarage>(FindObjectsInactive.Include);
            var allColliders = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var shapes = allColliders.Where(c => c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out _))
                .ToLookup(c => int.Parse(c.name.Substring(8)));
            var selected = world.solids.Where(s => OriginalLot(source, new Vector3(s.x, 0, s.z)) && shapes.Contains(s.index)).ToArray();
            var nodeMap = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            // Legacy natural objects and later door panels can share a numeric
            // solid ID. Resolve the owner and pose rather than taking the first.
            var bySolid = selected.ToDictionary(s => s.index, s => shapes[s.index].OrderBy(c => {
                if (s.owner != 0 && nodeMap.TryGetValue(s.owner, out var owner) && (c.transform == owner || c.transform.IsChildOf(owner))) return -1f;
                return (c.transform.TransformPoint(c.center) - new Vector3(s.x, (s.bottom + s.top) * .5f, s.z)).sqrMagnitude;
            }).First());
            var selectedIds = new HashSet<int>(selected.Select(s => s.index));
            var targets = new HashSet<Transform> { shell.transform, glazing.transform, garage.transform };
            foreach (var solid in selected) targets.Add(bySolid[solid.index].transform);
            var foundation = city.Find("Building foundations/Foundation • 3"); if (foundation) targets.Add(foundation);
            var door = registry.nodes.First(n => n.id == source.door.id).transform; targets.Add(door);
            foreach (var node in registry.nodes)
                if (node.transform && (node.transform.name.StartsWith("Doorway sidelight-" + source.door.id + "-") || node.transform.name == "Doorway canopy-" + source.door.id)) targets.Add(node.transform);
            var lighting = Object.FindFirstObjectByType<CityLighting>();
            foreach (var fixture in lighting.fixtures.Where(f => f.place == CityFireStationSite.PlaceIndex))
            {
                if (fixture.anchor) targets.Add(fixture.anchor);
                if (fixture.source) targets.Add(fixture.source.transform);
            }
            bool Moving(Transform t) => t && targets.Any(p => t == p || t.IsChildOf(p));
            if (bike)
            {
                foreach (var retired in bike.retiredObjects) if (retired.target && Moving(retired.target.transform)) retired.target.SetActive(retired.active);
                foreach (var retired in bike.retiredColliders)
                    if (retired.target && Moving(retired.target.transform)) retired.target.enabled = retired.enabled;
                bike.retiredObjects = bike.retiredObjects.Where(r => !r.target || !Moving(r.target.transform)).ToArray();
                bike.retiredColliders = bike.retiredColliders.Where(r => !r.target || !Moving(r.target.transform)).ToArray();
                bike.retiredSolids = bike.retiredSolids.Where(id => !selectedIds.Contains(id)).ToArray();
                EditorUtility.SetDirty(bike);
            }
            // Parents are moved once, including their dynamic doors and attached lights.
            foreach (var target in targets.Where(t => !targets.Any(other => other != t && t.IsChildOf(other))).ToArray())
                target.SetParent(root.transform, true);
            shell.gameObject.SetActive(true); glazing.gameObject.SetActive(true); garage.gameObject.SetActive(true); door.gameObject.SetActive(true);
            if (foundation) foundation.gameObject.SetActive(true);
            BakeFloors(root.transform, source, original.inverse, bike, library, info);
            root.transform.SetPositionAndRotation(Site, Rotation);
            Physics.SyncTransforms();
            foreach (var change in garage.colliders) change.position = move.MultiplyPoint3x4(change.position);
            if (garage.parkedTruck) garage.parkedTruck.SetPositionAndRotation(garage.TruckPosition, Rotation);
            info.place = Copy(source); info.place.name = "Fire Station";
            info.place.x = Site.x; info.place.z = Site.z; info.place.elevation = Site.y; info.place.yaw = Rotation.eulerAngles.y * Mathf.Deg2Rad;
            info.place.w = Mathf.Abs(Mathf.Cos(info.place.yaw)) * source.lotWidth + Mathf.Abs(Mathf.Sin(info.place.yaw)) * source.lotDepth;
            info.place.d = Mathf.Abs(Mathf.Sin(info.place.yaw)) * source.lotWidth + Mathf.Abs(Mathf.Cos(info.place.yaw)) * source.lotDepth;
            var doorPoint = move.MultiplyPoint3x4(new Vector3(source.door.x, source.elevation, source.door.z));
            info.place.door.x = doorPoint.x; info.place.door.z = doorPoint.z; info.place.door.position = move.MultiplyPoint3x4(source.door.position); info.place.door.yaw += yawDelta;
            info.originalSolidIds = selected.Select(s => s.index).ToArray(); info.originalSolidOwners = selected.Select(s => s.owner).ToArray();
            int nextSolid = Mathf.Max(world.solids.Max(s => s.index), shapes.Max(g => g.Key)) + 1;
            info.solids = selected.Select(s => {
                var copy = Copy(s); var box = bySolid[s.index]; var center = box.transform.TransformPoint(box.center);
                if (world.solids.Count(other => other.index == s.index) > 1) { copy.index = nextSolid++; box.name = "Colisão-" + copy.index; }
                copy.x = center.x; copy.z = center.z; copy.bottom = box.bounds.min.y; copy.top = box.bounds.max.y;
                copy.yaw = box.transform.eulerAngles.y * Mathf.Deg2Rad; copy.active = box.enabled && box.gameObject.activeInHierarchy; return copy;
            }).ToArray();
            info.movedColliders = info.solids.Length;
            info.service = Copy(world.services.First(s => s.id == "service-12"));
            info.service.position = move.MultiplyPoint3x4(info.service.position); info.service.anchor = move.MultiplyPoint3x4(info.service.anchor); info.service.label = "Water training";
            info.device = Copy(world.devices.First(d => d.id == "device-98"));
            info.device.position = move.MultiplyPoint3x4(info.device.position); info.device.lookAt = move.MultiplyPoint3x4(info.device.lookAt); info.device.yaw += yawDelta; info.device.label = "Water panel";
            var windows = Object.FindFirstObjectByType<CityWindows>();
            foreach (var window in windows.windows.Where(w => w.place == CityFireStationSite.PlaceIndex))
            { window.center = move.MultiplyPoint3x4(window.center); window.outward = move.MultiplyVector(window.outward); }
            CloneFirefighter(info, registry, world, move, root.transform);
            Navigation(info, world, move);
            Approaches(info, world.ground, library);
            ClearCover(info, library);
            CityFireStationPaint.Apply(world);
            EditorUtility.SetDirty(windows); EditorUtility.SetDirty(lighting); EditorUtility.SetDirty(registry); EditorUtility.SetDirty(info);
            Physics.SyncTransforms();
            Directory.CreateDirectory("../Reports/fire-station-red");
            File.WriteAllText("../Reports/fire-station-red/relocation.json", JsonUtility.ToJson(info, true));
            Debug.Log("FIRE_STATION_RELOCATED colliders=" + info.movedColliders + " floors=" + info.floorTriangles + " site=" + Site);
        }

        // Upgrade the first authored relocation without rebuilding any scenery.
        // Its duplicate IDs included one remote tree collider; restore that
        // exact collider and assign exclusive IDs to the station's two panels.
        public static void RepairSolidIdentity()
        {
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Editor must be idle.");
            var site = Object.FindFirstObjectByType<CityFireStationSite>();
            if (!site || site.originalSolidIds.Length == site.solids.Length) return;
            var registry = Object.FindFirstObjectByType<WorldRegistry>(); var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var source = world.places[CityFireStationSite.PlaceIndex];
            var original = Matrix4x4.TRS(new Vector3(source.x, source.elevation, source.z), Quaternion.Euler(0, source.yaw * Mathf.Rad2Deg, 0), Vector3.one);
            var restore = original * site.transform.worldToLocalMatrix;
            var physicsRoot = Object.FindFirstObjectByType<CityLodManager>().transform.Find("Colisões e relevo");
            var all = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int restoredTrees = 0;
            foreach (var box in site.GetComponentsInChildren<BoxCollider>(true).ToArray())
            {
                if (!box.name.StartsWith("Colisão-") || !int.TryParse(box.name.Substring(8), out int id)) continue;
                var natural = world.solids.FirstOrDefault(s => s.index == id && s.owner == 0 && !OriginalLot(source, new Vector3(s.x, 0, s.z)));
                if (natural == null || Mathf.Abs(box.size.y - (natural.top - natural.bottom)) > .01f || Mathf.Abs(box.size.x - natural.w) > .01f) continue;
                var position = restore.MultiplyPoint3x4(box.transform.position); var rotation = restore.rotation * box.transform.rotation;
                box.transform.SetParent(physicsRoot, true); box.transform.SetPositionAndRotation(position, rotation); restoredTrees++;
            }
            var selected = world.solids.Where(s => OriginalLot(source, new Vector3(s.x, 0, s.z)) && site.solids.Any(m => m.owner == s.owner && (m.index == s.index))).ToArray();
            var mapping = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            int next = Mathf.Max(world.solids.Max(s => s.index), all.Where(c => c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out _)).Max(c => int.Parse(c.name.Substring(8)))) + 1;
            site.originalSolidIds = selected.Select(s => s.index).ToArray(); site.originalSolidOwners = selected.Select(s => s.owner).ToArray();
            site.solids = selected.Select(s => {
                var candidates = all.Where(c => c.name == "Colisão-" + s.index && c.transform.IsChildOf(site.transform));
                var box = s.owner != 0 && mapping.TryGetValue(s.owner, out var owner) ? candidates.First(c => c.transform == owner || c.transform.IsChildOf(owner)) : candidates.First();
                var copy = Copy(s); if (world.solids.Count(other => other.index == s.index) > 1) { copy.index = next++; box.name = "Colisão-" + copy.index; }
                var center = box.transform.TransformPoint(box.center); copy.x = center.x; copy.z = center.z; copy.yaw = box.transform.eulerAngles.y * Mathf.Deg2Rad;
                copy.bottom = center.y - box.size.y * .5f; copy.top = center.y + box.size.y * .5f; copy.active = box.enabled && box.gameObject.activeInHierarchy; return copy;
            }).ToArray();
            site.movedColliders = site.solids.Length; Physics.SyncTransforms(); EditorUtility.SetDirty(site);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(site.gameObject.scene); EditorSceneManager.SaveScene(site.gameObject.scene);
            File.WriteAllText("../Reports/fire-station-red/relocation.json", JsonUtility.ToJson(site, true));
            File.WriteAllText("../Reports/fire-station-red/solid-identity-repair.json", "{\"restoredRemoteTreeColliders\":" + restoredTrees + ",\"exclusivePanelIds\":true}");
        }

        static void BakeFloors(Transform root, PlaceData source, Matrix4x4 toLocal, CityBikeShop bike, Object library, CityFireStationSite info)
        {
            var vertices = new List<Vector3>();
            foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!collider.name.StartsWith("Piso-")) continue;
                var mesh = bike ? bike.floorChanges.FirstOrDefault(f => f.target == collider)?.original : null;
                if (!mesh) mesh = collider.sharedMesh;
                if (!mesh) continue;
                var positions = mesh.vertices; var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = collider.transform.TransformPoint(positions[triangles[i]]); var b = collider.transform.TransformPoint(positions[triangles[i + 1]]); var c = collider.transform.TransformPoint(positions[triangles[i + 2]]);
                    if (Mathf.Min(a.y, b.y, c.y) < source.elevation - .05f || !OriginalLot(source, a) || !OriginalLot(source, b) || !OriginalLot(source, c)) continue;
                    vertices.Add(toLocal.MultiplyPoint3x4(a)); vertices.Add(toLocal.MultiplyPoint3x4(b)); vertices.Add(toLocal.MultiplyPoint3x4(c));
                }
            }
            if (vertices.Count == 0) throw new InvalidOperationException("Station floor collision not found.");
            var meshFloor = new Mesh { name = "Relocated fire station • original floor collision", indexFormat = IndexFormat.UInt32 };
            meshFloor.SetVertices(vertices); meshFloor.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); meshFloor.RecalculateNormals(); meshFloor.RecalculateBounds(); AssetDatabase.AddObjectToAsset(meshFloor, library);
            var floor = new GameObject("Fire station • preserved interior floor"); floor.transform.SetParent(root, false); floor.layer = CityContext.GroundLayer; floor.AddComponent<MeshCollider>().sharedMesh = meshFloor;
            info.floorTriangles = vertices.Count / 3;
        }

        static void CloneFirefighter(CityFireStationSite site, WorldRegistry registry, WorldData world, Matrix4x4 move, Transform root)
        {
            var resident = Copy(world.residents.First(r => r.id == 7971));
            var original = registry.nodes.First(n => n.id == resident.id).transform;
            var clone = Object.Instantiate(original.gameObject, root).transform; clone.name = "Henry • fire station attendant";
            clone.SetPositionAndRotation(move.MultiplyPoint3x4(resident.position), move.rotation * original.rotation);
            clone.gameObject.SetActive(true);
            var references = new List<NodeReference>(registry.nodes); var mapping = new Dictionary<int, int>(); int next = registry.nodes.Max(n => n.id) + 100;
            foreach (var node in registry.nodes.Where(n => n.transform && (n.transform == original || n.transform.IsChildOf(original))))
            {
                string path = AnimationUtility.CalculateTransformPath(node.transform, original); var target = path.Length == 0 ? clone : clone.Find(path);
                if (!target) throw new InvalidOperationException("Firefighter rig clone incomplete: " + path);
                mapping.Add(node.id, next); references.Add(new NodeReference { id = next++, transform = target });
            }
            int Id(int id) => id != 0 && mapping.TryGetValue(id, out int result) ? result : id;
            resident.id = Id(resident.id); resident.position = clone.position; resident.name = "Henry • firefighter"; resident.place = "Fire Station";
            resident.line = "The equipment is ready. The training panel is by the wall."; resident.lines = new[] { resident.line }; resident.stationary = true; resident.activity = "work";
            var rig = resident.rig; rig.root = Id(rig.root); rig.hips = Id(rig.hips); rig.chest = Id(rig.chest); rig.head = Id(rig.head);
            rig.limbs = rig.limbs.Select(Id).ToArray(); rig.knees = rig.knees.Select(Id).ToArray(); rig.elbows = rig.elbows.Select(Id).ToArray(); rig.hands = rig.hands.Select(Id).ToArray(); rig.ankles = rig.ankles.Select(Id).ToArray();
            site.firefighter = resident; registry.nodes = references.ToArray();
            var manager = Object.FindFirstObjectByType<CityLodManager>();
            manager.objects = manager.objects.Concat(clone.GetComponentsInChildren<CityObjectLod>(true)).ToArray();
        }

        static void Navigation(CityFireStationSite site, WorldData world, Matrix4x4 move)
        {
            var nodes = world.nav.Where(n => EntranceIds.Contains(n.id)).OrderBy(n => n.id).Select(n => {
                var p = move.MultiplyPoint3x4(new Vector3(n.x, 0, n.z)); return new NavNodeData { id = n.id, x = p.x, z = p.z };
            }).ToList();
            int next = world.nav.Max(n => n.id) + 1;
            foreach (float z in new[] { 19.5f, 26f })
            { var p = site.transform.TransformPoint(new Vector3(0, 0, z)); nodes.Add(new NavNodeData { id = next++, x = p.x, z = p.z }); }
            var end = nodes.Last(); var sidewalk = world.nav.Where(n => !EntranceIds.Contains(n.id))
                .OrderBy(n => (new Vector2(n.x - end.x, n.z - end.z)).sqrMagnitude).First();
            site.connection = sidewalk.id;
            for (int i = 0; i < nodes.Count; i++)
            {
                var adjacent = new List<NavNodeData>(); if (i > 0) adjacent.Add(nodes[i - 1]); if (i < nodes.Count - 1) adjacent.Add(nodes[i + 1]); else adjacent.Add(sidewalk);
                var n = nodes[i]; n.edges = adjacent.Select(a => new NavEdgeData { to = a.id, cost = Vector2.Distance(new Vector2(n.x, n.z), new Vector2(a.x, a.z)) }).ToArray();
            }
            site.entrance = nodes.ToArray();
        }

        static void Approaches(CityFireStationSite site, GroundData ground, Object library)
        {
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Fire station • warm paved forecourt", enableInstancing = true };
            material.SetColor("_Color", new Color(.57f, .59f, .55f)); material.SetFloat("_Glossiness", .15f); AssetDatabase.AddObjectToAsset(material, library);
            // Independent bands keep the rest of this open grass verge intact.
            void Band(string name, float left, float right, float start, float end, float startHeight)
            {
                var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
                int strips = Mathf.CeilToInt((end - start) / .5f);
                for (int j = 0; j <= strips; j++) for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? left : right, z = Mathf.Lerp(start, end, j / (float)strips);
                    var wp = site.transform.TransformPoint(new Vector3(x, 0, z));
                    float y = CityGroundCoverBuilder.Height(ground, wp.x, wp.z) - Site.y + .025f;
                    if (z < 25f) y = Mathf.Lerp(startHeight, .19f, Mathf.InverseLerp(start, 25f, z));
                    else if (Physics.Raycast(wp + Vector3.up * 3, Vector3.down, out var hit, 6, 1 << CityContext.GroundLayer)) y = hit.point.y - Site.y + .009f;
                    vertices.Add(new Vector3(x, y, z)); uv.Add(new Vector2(x, z));
                }
                for (int j = 0; j < strips; j++) { int a = j * 2; triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
                var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                var go = new GameObject(name); go.transform.SetParent(site.transform, false); go.layer = CityContext.GroundLayer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material; go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            Band("Fire station • driveway to sidewalk and road", -9.8f, -2.6f, 12.6f, 29.2f, .19f);
            Band("Fire station • continuous pedestrian approach", -1.8f, 1.8f, 12.1f, 26.5f, .19f);
        }

        static void ClearCover(CityFireStationSite site, Object library)
        {
            var cover = Object.FindFirstObjectByType<CityGroundCoverInfo>(); if (!cover) return;
            foreach (var filter in cover.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>(); if (!renderer || renderer.bounds.SqrDistance(Site) > 60 * 60) continue;
                var original = filter.sharedMesh; var vertices = original.vertices; var triangles = original.triangles; var keep = new List<int>();
                bool Clear(int index) { var p = filter.transform.TransformPoint(vertices[index]); return Reserved(p.x, p.z, .8f); }
                for (int i = 0; i < triangles.Length; i += 3)
                    if (!Clear(triangles[i]) && !Clear(triangles[i + 1]) && !Clear(triangles[i + 2])) keep.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
                if (keep.Count == triangles.Length) continue;
                var mesh = Object.Instantiate(original); mesh.name = original.name + " • fire station clearing"; mesh.SetTriangles(keep, 0); mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, library); filter.sharedMesh = mesh; site.clearedCoverTriangles += (triangles.Length - keep.Count) / 3; EditorUtility.SetDirty(filter);
            }
        }
    }
}
