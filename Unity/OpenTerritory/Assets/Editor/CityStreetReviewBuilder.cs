using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    // Applies an offline street patch to the current scene. In particular, this
    // does not invoke CityBuilder.Import or recreate buildings and their grounds.
    public static class CityStreetReviewBuilder
    {
        [Serializable] sealed class NewNode { public int id, template; }
        [Serializable] sealed class Patch
        {
            public int revision;
            public string sourceHash, previousSourceHash;
            public int[] nodes, solids, materials;
            public string[] floorKeys;
            public string[] batchKeys;
            public NewNode[] newNodes;
        }
        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>(), surface = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> triangles = new List<int>();
            public void Add(GeometryData geometry, GeometryGroup group, Matrix4x4 matrix, Color tint, float code)
            {
                int start = vertices.Count, count = geometry.positions.Length / 3;
                var normal = matrix.inverse.transpose;
                for (int i = 0; i < count; i++)
                {
                    vertices.Add(matrix.MultiplyPoint3x4(new Vector3(geometry.positions[i * 3], geometry.positions[i * 3 + 1], geometry.positions[i * 3 + 2])));
                    normals.Add(geometry.normals.Length >= i * 3 + 3 ? normal.MultiplyVector(new Vector3(geometry.normals[i * 3], geometry.normals[i * 3 + 1], geometry.normals[i * 3 + 2])).normalized : Vector3.up);
                    uv.Add(geometry.uv.Length >= i * 2 + 2 ? new Vector2(geometry.uv[i * 2], geometry.uv[i * 2 + 1]) : Vector2.zero);
                    colors.Add(geometry.colors.Length >= i * 3 + 3 ? new Color(geometry.colors[i * 3], geometry.colors[i * 3 + 1], geometry.colors[i * 3 + 2]) * tint : tint);
                    surface.Add(new Vector2(code, 0));
                }
                bool mirrored = matrix.determinant < 0;
                for (int i = group.start; i + 2 < Mathf.Min(geometry.indices.Length, group.start + group.count); i += 3)
                {
                    triangles.Add(start + geometry.indices[i]);
                    triangles.Add(start + geometry.indices[i + (mirrored ? 2 : 1)]);
                    triangles.Add(start + geometry.indices[i + (mirrored ? 1 : 2)]);
                }
            }
            public void Write(Mesh mesh)
            {
                mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetUVs(1, surface);
                mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            }
        }

        [MenuItem("Open Territory/Aplicar revisão de ruas, semáforos e bancos")]
        public static void Apply()
        {
            ApplyPatch("Assets/Imported/street-review.json", "Assets/Imported/CityStreetReview.asset");
        }

        public static void ApplyPatch(string patchPath, string assetPath)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty)
                throw new InvalidOperationException("A cena BaiaDoSol deve estar salva e fora de Play Mode.");
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry) throw new InvalidOperationException("Registro da cidade ausente.");
            var patch = JsonUtility.FromJson<Patch>(File.ReadAllText(patchPath));
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            if (patch.revision != 1 || world.sourceHash != patch.sourceHash) throw new InvalidOperationException("Patch e manifesto não correspondem.");
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var nodes = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            var source = visual.nodes.ToDictionary(n => n.id);
            // Validate the entire allocation before touching any scene object.
            // A supplementary Editor fixture may own an ID absent from visual.json.
            foreach (var added in patch.newNodes)
                if (nodes.TryGetValue(added.id, out var existing) && existing.parent != nodes[source[added.id].parent])
                    throw new InvalidOperationException("ID do banco já pertence a outro conjunto: " + added.id);
            var addedNodes = new List<NodeReference>();
            foreach (var added in patch.newNodes)
            {
                if (nodes.ContainsKey(added.id)) continue;
                var donor = nodes[added.template]; var data = source[added.id];
                var target = new GameObject(data.name).transform; target.SetParent(nodes[data.parent], false);
                target.gameObject.layer = donor.gameObject.layer;
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = donor.GetComponent<MeshFilter>().sharedMesh;
                var renderer = target.gameObject.AddComponent<MeshRenderer>(); var original = donor.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials; renderer.shadowCastingMode = original.shadowCastingMode; renderer.receiveShadows = original.receiveShadows;
                nodes[added.id] = target; addedNodes.Add(new NodeReference { id = added.id, transform = target });
            }
            registry.nodes = registry.nodes.Concat(addedNodes).ToArray();
            foreach (int id in patch.nodes)
            {
                if (!nodes.TryGetValue(id, out var target) || !source.ContainsKey(id)) throw new InvalidOperationException("Nó do patch ausente: " + id);
                var node = source[id];
                target.localPosition = node.position; target.localRotation = node.rotation; target.localScale = node.scale;
                target.name = node.name; target.gameObject.SetActive(node.visible);
            }
            var solidIds = new HashSet<int>(patch.solids);
            var solids = world.solids.Where(s => solidIds.Contains(s.index)).ToDictionary(s => s.index);
            foreach (var collider in UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!collider.name.StartsWith("Colisão-") || !int.TryParse(collider.name.Substring(8), out int id) || !solids.TryGetValue(id, out var data)) continue;
                collider.transform.SetPositionAndRotation(new Vector3(data.x, (data.bottom + data.top) / 2, data.z), Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0));
                collider.transform.localScale = Vector3.one; collider.center = Vector3.zero;
                collider.size = new Vector3(data.w, data.top - data.bottom, data.d); collider.enabled = data.active;
            }

            var library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(assetPath);
            if (!library) { library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, assetPath); }
            library.sourceHash = world.sourceHash;
            // The library also stores authored shop cuts. Their descriptive
            // names can repeat across revisions and are never patch targets.
            var floorNames = new HashSet<string>(patch.floorKeys.Select(k => "Piso-" + k));
            var meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>()
                .Where(m => m.name.StartsWith("s-", StringComparison.Ordinal) || floorNames.Contains(m.name)).ToDictionary(m => m.name);
            Mesh MeshFor(string name)
            {
                if (meshes.TryGetValue(name, out var mesh)) return mesh;
                mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; AssetDatabase.AddObjectToAsset(mesh, library); meshes[name] = mesh; return mesh;
            }
            var root = UnityEngine.Object.FindFirstObjectByType<CityLodManager>().transform;
            var staticRoot = root.Find("Novo mapa • geometria agrupada");
            var physicsRoot = root.Find("Colisões e relevo");
            var selected = new HashSet<int>(patch.materials);
            var dirtyKeys = new HashSet<string>(patch.batchKeys);
            var batches = new Dictionary<string, Batch>();
            foreach (var node in visual.nodes.Where(n => !n.dynamic && n.visible && n.geometry >= 0))
            {
                var geometry = visual.geometries[node.geometry];
                var groups = node.materials.Length > 1 && geometry.groups.Length > 0 ? geometry.groups : new[] { new GeometryGroup { count = geometry.indices.Length } };
                var instances = node.instances.Length > 0 ? node.instances : new[] { new InstanceData { matrix = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 } } };
                foreach (var instance in instances)
                {
                    var matrix = new Matrix4x4();
                    for (int c = 0; c < 4; c++) matrix.SetColumn(c, new Vector4(instance.matrix[c * 4], instance.matrix[c * 4 + 1], instance.matrix[c * 4 + 2], instance.matrix[c * 4 + 3]));
                    matrix = nodes[node.id].localToWorldMatrix * matrix;
                    foreach (var group in groups)
                    {
                        int material = node.materials[Mathf.Min(node.materials.Length - 1, group.materialIndex)];
                        string key = "s-" + CityLodBuilder.StaticKey(node, visual.materials[material], matrix.MultiplyPoint3x4(Vector3.zero)) + "-" + material;
                        if (!selected.Contains(material) && !dirtyKeys.Contains(key)) continue;
                        if (!batches.TryGetValue(key, out var batch)) { batch = new Batch(); batches[key] = batch; }
                        batch.Add(geometry, group, matrix, instance.color, instance.surface);
                    }
                }
            }
            var filters = staticRoot.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
            foreach (string key in dirtyKeys) if (!batches.ContainsKey(key) && filters.TryGetValue(key, out var empty)) empty.GetComponent<MeshRenderer>().enabled = false;
            foreach (var pair in batches)
            {
                if (!filters.TryGetValue(pair.Key, out var filter)) throw new InvalidOperationException("Agrupamento original ausente: " + pair.Key);
                var mesh = MeshFor(pair.Key); pair.Value.Write(mesh); filter.sharedMesh = mesh;
                var lod = filter.GetComponentInParent<CityObjectLod>();
                if (lod) foreach (var part in lod.parts.Where(p => p.filter == filter)) { part.full = part.medium = part.far = mesh; part.externalMesh = false; }
            }
            var keys = new HashSet<string>(patch.floorKeys);
            var floors = new Dictionary<string, List<Vector3>>();
            foreach (var floor in visual.floors)
            {
                var v = floor.vertices; if (v.Length < 9) continue;
                string key = Mathf.FloorToInt(v[0] / 32) + "," + Mathf.FloorToInt(v[2] / 32); if (!keys.Contains(key)) continue;
                if (!floors.TryGetValue(key, out var points)) { points = new List<Vector3>(); floors[key] = points; }
                for (int i = 0; i + 8 < v.Length; i += 9)
                {
                    var a = new Vector3(v[i], v[i + 1], v[i + 2]); var b = new Vector3(v[i + 3], v[i + 4], v[i + 5]); var c = new Vector3(v[i + 6], v[i + 7], v[i + 8]);
                    points.Add(a); if (Vector3.Cross(b - a, c - a).y >= 0) { points.Add(b); points.Add(c); } else { points.Add(c); points.Add(b); }
                }
            }
            foreach (string key in keys)
            {
                var floor = physicsRoot.Find("Piso-" + key);
                if (!floors.TryGetValue(key, out var points)) { if (floor) floor.GetComponent<MeshCollider>().enabled = false; continue; }
                if (!floor) { floor = new GameObject("Piso-" + key).transform; floor.SetParent(physicsRoot, false); floor.gameObject.layer = CityContext.GroundLayer; floor.gameObject.isStatic = true; floor.gameObject.AddComponent<MeshCollider>(); }
                var mesh = MeshFor("Piso-" + key); mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32; mesh.SetVertices(points); mesh.SetTriangles(Enumerable.Range(0, points.Count).ToArray(), 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                var collider = floor.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.sharedMesh = mesh; collider.enabled = true;
            }
            var manager = root.GetComponent<CityLodManager>(); var newLods = new List<CityObjectLod>();
            foreach (int id in patch.newNodes.Select(n => source[n.id].parent).Distinct())
            {
                var owner = nodes[id]; var lod = owner.GetComponent<CityObjectLod>();
                if (!lod) { lod = owner.gameObject.AddComponent<CityObjectLod>(); newLods.Add(lod); }
                lod.category = "prop";
                lod.parts = owner.GetComponentsInChildren<MeshFilter>(true).Select(f => new CityLodPart { filter = f, renderer = f.GetComponent<Renderer>(), full = f.sharedMesh, medium = f.sharedMesh, far = f.sharedMesh, shadows = f.GetComponent<Renderer>().shadowCastingMode }).ToArray();
            }
            manager.objects = manager.objects.Concat(newLods).ToArray();
            foreach (int id in patch.nodes)
            {
                var lod = nodes[id].GetComponent<CityObjectLod>(); if (!lod) continue;
                var renderers = nodes[id].GetComponentsInChildren<Renderer>(true); bool first = true; Bounds bounds = default;
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) continue;
                    var b = filter.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var point = nodes[id].InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                }
                lod.localBounds = bounds;
            }
            Physics.SyncTransforms(); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("CITY_STREET_REVIEW_APPLIED nodes=" + patch.nodes.Length + " meshes=" + batches.Count + " floors=" + floors.Count);
        }
    }
}
