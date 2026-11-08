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
    public static class CityParkingLotBuilder
    {
        public const string RootName = "Waterfront • neighborhood parking";
        const string LibraryPath = "Assets/Imported/WaterfrontParking.asset";
        const float UMin = -10.1f, UMax = 18.05f, VMin = -6.85f, VMax = 7.15f;
        const float EntryMin = -5.4f, EntryMax = .25f;
        static readonly int[] StopIds = { 9291, 9292, 9293, 9294, 9295, 9296 };
        static readonly float[] StopU = { 7.02f, 10.07f, 13.12f, -2.13f, -5.18f, -8.23f };
        static readonly int[] VehicleIds = { 10045, 10241 };
        static readonly float[] VehicleU = { 7.02f, 13.12f };
        static Quaternion Rotation => Quaternion.Euler(0, CityParkingLot.Yaw, 0);

        [Serializable] sealed class Placement
        {
            public bool passed; public string completedAtUtc, scene;
            public int bays, preservedColliders, unexpectedColliderChanges, movedStops;
            public Vector3[] vehiclePositions;
        }

        public static bool Reserved(float x, float z, float margin = 0)
        {
            var q = CityParkingLot.Local(new Vector3(x, 0, z));
            return q.x >= UMin - margin && q.x <= UMax + margin && q.z >= VMin - margin && q.z <= VMax + margin
                || q.x >= EntryMin - margin && q.x <= EntryMax + margin && q.z >= VMax - margin && q.z <= 12.3f + margin;
        }

        [MenuItem("Open Territory/Construir estacionamento da orla")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol salva e fora do Play Mode.");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var parent = scene.GetRootGameObjects().First(g => g.name.StartsWith("Marin •")).transform;
            if (parent.Find(RootName)) throw new InvalidOperationException("O estacionamento já está na cena.");
            Directory.CreateDirectory("../Reports/parking/backups");
            if (!EditorSceneManager.SaveScene(scene, "../Reports/parking/backups/Before-parking-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Não foi possível preservar a cena anterior.");
            Build(parent, JsonUtility.FromJson<WorldData>(registry.manifest.text), registry);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Não foi possível salvar o estacionamento.");
        }

        static string Signature(Collider c)
        {
            string result = c.enabled + "|" + c.gameObject.activeSelf + "|" + c.gameObject.layer + "|" + c.transform.localToWorldMatrix.ToString("R");
            if (c is BoxCollider box) result += "|" + box.center.ToString("R") + "|" + box.size.ToString("R");
            if (c is MeshCollider mesh) result += "|" + (mesh.sharedMesh ? mesh.sharedMesh.GetInstanceID() : 0);
            return result;
        }

        public static void Build(Transform parent, WorldData world, WorldRegistry registry)
        {
            if (parent.Find(RootName)) return;
            Physics.SyncTransforms();
            var map = parent.Find("Novo mapa • geometria agrupada");
            var physics = parent.Find("Colisões e relevo");
            var meshes = map.GetComponentsInChildren<MeshFilter>(true);
            var stopFilter = meshes.First(f => f.name == "s-scenery:0:0-858");
            var lod = stopFilter.GetComponentInParent<CityObjectLod>();
            int lodPart = Array.FindIndex(lod.parts, p => p.filter == stopFilter);
            if (lodPart < 0 || !lod.parts[lodPart].full)
                throw new InvalidOperationException("A malha dos limitadores não está no agrupamento esperado.");
            var asphalt = meshes.First(f => f.name == "s-surface:0:0-845").GetComponent<Renderer>().sharedMaterial;
            var cap = stopFilter.GetComponent<Renderer>().sharedMaterial;
            var stops = StopIds.Select(id => physics.Find("Colisão-" + id)?.GetComponent<BoxCollider>()).ToArray();
            var cars = VehicleIds.Select(id => registry.nodes.First(n => n.id == id).transform).ToArray();
            if (stops.Any(c => !c) || cars.Any(t => !t)) throw new InvalidOperationException("Carros ou limitadores originais ausentes.");

            // Refuse an unexpected wall, lamp, building or other fixture in the court.
            var hits = Physics.OverlapBox(CityParkingLot.Point(3.95f, .1f) + Vector3.up * 10.5f,
                new Vector3(13.9f, .9f, 6.7f), Rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            var blockers = hits.Where(c => !stops.Contains(c) && !cars.Any(t => c.transform.IsChildOf(t))).Select(c => c.name).Distinct().ToArray();
            if (blockers.Length > 0) throw new InvalidOperationException("O terreno contém obstáculos: " + string.Join(", ", blockers.Take(12)));
            var protectedColliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => !stops.Contains(c) && !cars.Any(t => c.transform.IsChildOf(t))).ToDictionary(c => c, Signature);

            // Identify the six original stop boxes in the shared mesh. Require exact
            // matches so no adjacent wall cap or other building can be changed.
            var originalMesh = lod.parts[lodPart].full;
            var vertices = originalMesh.vertices; var triangles = originalMesh.triangles;
            var owner = new Dictionary<int, int>(); var matches = new int[stops.Length];
            for (int i = 0; i < triangles.Length; i += 3)
                for (int s = 0; s < stops.Length; s++)
                {
                    bool inside = true;
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 p = stops[s].transform.InverseTransformPoint(stopFilter.transform.TransformPoint(vertices[triangles[i + j]])) - stops[s].center;
                        Vector3 half = stops[s].size * .5f + Vector3.one * .04f;
                        if (Mathf.Abs(p.x) > half.x || Mathf.Abs(p.y) > half.y || Mathf.Abs(p.z) > half.z) { inside = false; break; }
                    }
                    if (!inside) continue;
                    matches[s]++;
                    for (int j = 0; j < 3; j++)
                    {
                        int index = triangles[i + j];
                        if (owner.TryGetValue(index, out int existing) && existing != s) throw new InvalidOperationException("Malha de limitadores compartilhada.");
                        owner[index] = s;
                    }
                    break;
                }
            if (matches.Any(n => n != 12)) throw new InvalidOperationException("Os seis limitadores originais não foram identificados com precisão: " + string.Join(",", matches));
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int n = (owner.ContainsKey(triangles[i]) ? 1 : 0) + (owner.ContainsKey(triangles[i + 1]) ? 1 : 0) + (owner.ContainsKey(triangles[i + 2]) ? 1 : 0);
                if (n != 0 && n != 3) throw new InvalidOperationException("Um limitador compartilha vértices com outra geometria.");
            }
            if (AssetDatabase.LoadMainAssetAtPath(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, LibraryPath);
            var go = new GameObject(RootName); go.transform.SetParent(parent, false);
            var lot = go.AddComponent<CityParkingLot>();
            lot.stopFilter = stopFilter; lot.stopLod = lod; lot.stopLodPart = lodPart; lot.originalStopMesh = originalMesh;

            // The pavement follows the existing physical ground. Its entrance is
            // gently raised to meet the existing sidewalk, with walking access kept open.
            float Base(float u, float v)
            {
                var p = CityParkingLot.Point(u, v) + Vector3.up * 15;
                var hit = Physics.RaycastAll(p, Vector3.down, 10, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.transform.IsChildOf(go.transform)).OrderByDescending(h => h.point.y).FirstOrDefault();
                if (!hit.collider) throw new InvalidOperationException("Terreno sem apoio em " + u + ", " + v);
                return hit.point.y;
            }
            float Height(float u, float v)
            {
                float h = Base(u, v) + .045f;
                if (u >= EntryMin && u <= EntryMax && v > 10.7f)
                {
                    float edge = Mathf.Clamp01(Mathf.Min(u - EntryMin, EntryMax - u) / .6f);
                    float ramp = Mathf.Lerp(Base(u, 10.7f) + .045f, Base(u, 12.3f) + .01f, Mathf.InverseLerp(10.7f, 12.3f, v));
                    return Mathf.Max(Base(u, v) + .01f, Mathf.Lerp(Base(u, v) + .01f, ramp, edge));
                }
                if (u >= EntryMin && u <= EntryMax && v > 4.5f && v < 8.7f)
                {
                    float edge = Mathf.Clamp01(Mathf.Min(u - EntryMin, EntryMax - u) / .6f);
                    float start = Base(u, 4.5f) + .045f, end = Base(u, 8.5f) + .045f;
                    float ramp = Mathf.Lerp(start, end, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4.5f, 8.5f, v)));
                    h = Mathf.Max(h, Mathf.Lerp(h, ramp, edge));
                }
                return h;
            }
            var surfaceMesh = Grid(UMin, UMax, VMin, VMax, .38f, Height, "Parking court");
            AssetDatabase.AddObjectToAsset(surfaceMesh, library);
            var paving = Surface(go.transform, "Parking court", surfaceMesh, asphalt, true);
            lot.surface = paving.GetComponent<MeshCollider>();
            var entryMesh = Grid(EntryMin, EntryMax, VMax, 12.3f, .25f, Height, "Parking entrance");
            AssetDatabase.AddObjectToAsset(entryMesh, library);
            Surface(go.transform, "Parking entrance", entryMesh, asphalt, true);
            Physics.SyncTransforms();

            float CurrentGround(float u, float v)
            {
                var p = CityParkingLot.Point(u, v) + Vector3.up * 15;
                if (!Physics.Raycast(p, Vector3.down, out var hit, 10, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("Piso do estacionamento ausente.");
                return hit.point.y;
            }

            var patched = Object.Instantiate(originalMesh); patched.name = "Parking • relocated existing wheel stops";
            var destination = new Vector3[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                destination[i] = CityParkingLot.Point(StopU[i], -6.05f);
                destination[i].y = CurrentGround(StopU[i], -6.05f) + stops[i].size.y * .5f + .008f;
            }
            foreach (var item in owner)
            {
                var box = stops[item.Value];
                Vector3 worldVertex = stopFilter.transform.TransformPoint(vertices[item.Key]);
                Vector3 relative = Quaternion.Inverse(box.transform.rotation) * (worldVertex - box.transform.position);
                vertices[item.Key] = stopFilter.transform.InverseTransformPoint(destination[item.Value] + Rotation * relative);
            }
            patched.vertices = vertices; patched.RecalculateBounds(); AssetDatabase.AddObjectToAsset(patched, library);
            var part = lod.parts[lodPart]; part.full = part.medium = part.far = patched; part.externalMesh = false;
            stopFilter.sharedMesh = patched;
            for (int i = 0; i < stops.Length; i++) stops[i].transform.SetPositionAndRotation(destination[i], Rotation);
            lot.movedStops = stops;

            var added = new List<BoxCollider>();
            foreach (float u in new[] { .92f, 3.97f, 16.17f })
            {
                var stop = GameObject.CreatePrimitive(PrimitiveType.Cube); stop.name = "Parking wheel stop";
                stop.transform.SetParent(go.transform, false); stop.layer = CityContext.WorldLayer;
                Vector3 at = CityParkingLot.Point(u, -6.05f); at.y = CurrentGround(u, -6.05f) + .068f;
                stop.transform.SetPositionAndRotation(at, Rotation); stop.transform.localScale = new Vector3(2.15f, .12f, .22f);
                stop.GetComponent<Renderer>().sharedMaterial = cap; stop.isStatic = true;
                added.Add(stop.GetComponent<BoxCollider>());
            }
            lot.newStops = added.ToArray();

            var markingMaterial = new Material(asphalt) { name = "Parking • warm white lines", color = new Color(.93f, .91f, .86f) };
            markingMaterial.SetFloat("_Surface", 0); markingMaterial.SetFloat("_Glossiness", .02f);
            AssetDatabase.AddObjectToAsset(markingMaterial, library);
            var paintVertices = new List<Vector3>(); var paintTriangles = new List<int>();
            void Line(float u1, float v1, float u2, float v2, float width)
            {
                var direction = new Vector2(u2 - u1, v2 - v1); int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / .35f));
                var side = new Vector2(-direction.y, direction.x).normalized * width * .5f;
                for (int i = 0; i < steps; i++)
                {
                    Vector2 a = Vector2.Lerp(new Vector2(u1, v1), new Vector2(u2, v2), i / (float)steps);
                    Vector2 b = Vector2.Lerp(new Vector2(u1, v1), new Vector2(u2, v2), (i + 1) / (float)steps);
                    Vector2[] points = { a - side, a + side, b - side, b + side }; int at = paintVertices.Count;
                    foreach (var uv in points) { var p = CityParkingLot.Point(uv.x, uv.y); p.y = CurrentGround(uv.x, uv.y) + .018f; paintVertices.Add(p); }
                    paintTriangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
                }
            }
            for (int i = 0; i <= CityParkingLot.BayCenters.Length; i++) Line(-9.755f + i * 3.05f, -6.65f, -9.755f + i * 3.05f, -.4f, .10f);
            foreach (float u in CityParkingLot.BayCenters) Line(u - 1.12f, -6.63f, u + 1.12f, -6.63f, .10f);
            var paint = new Mesh { name = "Parking • bay markings", indexFormat = IndexFormat.UInt32 };
            paint.SetVertices(paintVertices); paint.SetTriangles(paintTriangles, 0); paint.SetColors(Enumerable.Repeat(Color.white, paintVertices.Count).ToList()); paint.RecalculateNormals(); paint.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(paint, library); Surface(go.transform, "Parking bay markings", paint, markingMaterial, false);

            lot.vehicleIds = VehicleIds; lot.parkedVehicles = cars; lot.bayCount = CityParkingLot.BayCenters.Length;
            for (int i = 0; i < cars.Length; i++)
            {
                var at = CityParkingLot.Point(VehicleU[i], -3.3f); at.y = CurrentGround(VehicleU[i], -3.3f) + .04f;
                cars[i].SetPositionAndRotation(at, Rotation);
            }
            Physics.SyncTransforms();
            int changes = protectedColliders.Count(p => !p.Key || Signature(p.Key) != p.Value);
            var report = new Placement { passed = changes == 0, completedAtUtc = DateTime.UtcNow.ToString("o"), scene = parent.gameObject.scene.path,
                bays = lot.bayCount, preservedColliders = protectedColliders.Count, unexpectedColliderChanges = changes, movedStops = stops.Length,
                vehiclePositions = cars.Select(c => c.position).ToArray() };
            Directory.CreateDirectory("../Reports/parking"); File.WriteAllText("../Reports/parking/placement.json", JsonUtility.ToJson(report, true));
            if (changes != 0) throw new InvalidOperationException("Uma colisão fora do estacionamento foi alterada.");
            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        }

        static Mesh Grid(float minU, float maxU, float minV, float maxV, float step, Func<float, float, float> height, string name)
        {
            int nx = Mathf.CeilToInt((maxU - minU) / step), nz = Mathf.CeilToInt((maxV - minV) / step);
            var points = new List<Vector3>((nx + 1) * (nz + 1)); var triangles = new List<int>(nx * nz * 6);
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float u = Mathf.Lerp(minU, maxU, x / (float)nx), v = Mathf.Lerp(minV, maxV, z / (float)nz);
                var p = CityParkingLot.Point(u, v); p.y = height(u, v); points.Add(p);
            }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(points); mesh.SetTriangles(triangles, 0); mesh.SetColors(Enumerable.Repeat(Color.white, points.Count).ToList());
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        // Update only the entrance mesh in a scene already authored by the
        // first revision. The original sidewalk and road meshes remain intact.
        public static void SmoothEntrance()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol salva e fora do Play Mode.");
            var lot = Object.FindFirstObjectByType<CityParkingLot>();
            if (!lot) throw new InvalidOperationException("Estacionamento ausente.");
            var entrance = lot.transform.Find("Parking entrance");
            var filter = entrance.GetComponent<MeshFilter>(); var collider = entrance.GetComponent<MeshCollider>();
            float Base(float u, float v)
            {
                var p = CityParkingLot.Point(u, v) + Vector3.up * 15;
                var hit = Physics.RaycastAll(p, Vector3.down, 10, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.transform.IsChildOf(lot.transform)).OrderByDescending(h => h.point.y).FirstOrDefault();
                if (!hit.collider) throw new InvalidOperationException("Terreno sem apoio no acesso.");
                return hit.point.y;
            }
            float Height(float u, float v)
            {
                if (v <= 10.7f)
                {
                    float h = Base(u, v) + .045f;
                    if (v < 8.7f)
                    {
                        float side = Mathf.Clamp01(Mathf.Min(u - EntryMin, EntryMax - u) / .6f);
                        float start = Base(u, 4.5f) + .045f, end = Base(u, 8.5f) + .045f;
                        float rise = Mathf.Lerp(start, end, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4.5f, 8.5f, v)));
                        h = Mathf.Max(h, Mathf.Lerp(h, rise, side));
                    }
                    return h;
                }
                float edge = Mathf.Clamp01(Mathf.Min(u - EntryMin, EntryMax - u) / .6f);
                float ramp = Mathf.Lerp(Base(u, 10.7f) + .045f, Base(u, 12.3f) + .01f, Mathf.InverseLerp(10.7f, 12.3f, v));
                return Mathf.Max(Base(u, v) + .01f, Mathf.Lerp(Base(u, v) + .01f, ramp, edge));
            }
            var mesh = Grid(EntryMin, EntryMax, VMax, 12.3f, .25f, Height, "Parking entrance");
            var target = filter.sharedMesh;
            EditorUtility.CopySerialized(mesh, target); Object.DestroyImmediate(mesh);
            collider.sharedMesh = null; collider.sharedMesh = target;
            EditorUtility.SetDirty(target); EditorUtility.SetDirty(collider); AssetDatabase.SaveAssets();
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Falha ao salvar o acesso.");
            Physics.SyncTransforms();
        }

        static GameObject Surface(Transform parent, string name, Mesh mesh, Material material, bool collision)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
            if (collision) { go.layer = CityContext.GroundLayer; go.AddComponent<MeshCollider>().sharedMesh = mesh; }
            go.isStatic = true; return go;
        }
    }
}
