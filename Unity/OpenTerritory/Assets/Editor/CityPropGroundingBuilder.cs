using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityPropGroundingBuilder
    {
        const string AssetPath = "Assets/Imported/CityPropFootings.asset";
        const float Embed = .02f;

        [MenuItem("Open Territory/Assentar mobiliário no relevo")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty)
                throw new InvalidOperationException("Salve BaiaDoSol e saia do Play Mode antes de assentar o mobiliário.");
            Directory.CreateDirectory("../Reports/prop-grounding/backups");
            EditorSceneManager.SaveScene(scene, "../Reports/prop-grounding/backups/Before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            Build(JsonUtility.FromJson<WorldData>(registry.manifest.text), registry);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(WorldData world, WorldRegistry registry)
        {
            var baked = registry.GetComponent<CityPropGrounding>();
            if (baked) Restore(baked);
            else baked = registry.gameObject.AddComponent<CityPropGrounding>();
            var nodes = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            Physics.SyncTransforms();
            var plans = new List<CityPropGrounding.Prop>();
            // Plan and validate the complete placement before changing any asset.
            foreach (var data in world.destructibles.Where(d => d.type == "bin" || d.type == "planter" || d.type == "lamp"))
            {
                if (!nodes.TryGetValue(data.id, out var root) || !root.gameObject.activeInHierarchy) continue;
                var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<Renderer>()).ToArray();
                if (filters.Length == 0) continue;
                float bottom = filters.Min(f => Bounds(f).min.y);
                var bases = filters.Where(f => Bounds(f).min.y < bottom + .025f).ToArray();
                var plan = new CityPropGrounding.Prop { nodeId = data.id, category = data.type, root = root,
                    sourcePosition = data.position, originalPosition = root.position, position = root.position };
                var samples = Samples(bases);
                if (samples.Any(p => Floor(p) < -.3f))
                {
                    // An old harbor bin was beside the pier, above open water.
                    // Keep the complete footprint on the edge of the real deck.
                    plan.position = DockPosition(world, root, filters, samples);
                    var delta = plan.position - root.position;
                    samples = samples.Select(p => p + delta).ToArray();
                }
                var heights = samples.Select(Floor).ToArray();
                if (heights.Any(h => float.IsNaN(h) || h < -.3f))
                    throw new InvalidOperationException("Mobiliário sem piso: " + data.id);
                plan.minimumGround = heights.Min(); plan.maximumGround = heights.Max();
                plan.originalGap = bottom - plan.minimumGround;
                // Keep the prop upright and preserve its height at the uphill edge.
                // Only its existing lower body extends into the downhill ground.
                plan.position.y += plan.maximumGround - .005f - bottom;
                plan.groundSamples = samples.Select((p, i) => new Vector3(p.x, heights[i], p.z)).ToArray();
                plan.parts = bases.Select(f =>
                {
                    var lod = f.GetComponentInParent<CityObjectLod>();
                    var part = lod ? lod.parts.FirstOrDefault(p => p.filter == f) : null;
                    return new CityPropGrounding.Part { filter = f, original = f.sharedMesh,
                        medium = part != null ? part.medium : f.sharedMesh, far = part != null ? part.far : f.sharedMesh,
                        externalMesh = part != null && part.externalMesh };
                }).ToArray();
                plan.shapes = root.GetComponentsInChildren<BoxCollider>(true).Select(c =>
                {
                    int index;
                    return new CityPropGrounding.Shape { collider = c, center = c.center, size = c.size,
                        footing = c.bounds.min.y <= bottom + .05f,
                        solidIndex = c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out index) ? index : -1 };
                }).ToArray();
                if (plan.shapes.Length == 0) throw new InvalidOperationException("Mobiliário sem colisão: " + data.id);
                plans.Add(plan);
            }
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, AssetPath);
            foreach (var plan in plans)
            {
                plan.root.position = plan.position;
                foreach (var part in plan.parts)
                {
                    var vertices = part.original.vertices;
                    float bottom = vertices.Min(v => part.filter.transform.TransformPoint(v).y);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var point = part.filter.transform.TransformPoint(vertices[i]);
                        if (point.y > bottom + .002f) continue;
                        point.y = plan.minimumGround - Embed;
                        vertices[i] = part.filter.transform.InverseTransformPoint(point);
                    }
                    var mesh = Object.Instantiate(part.original); mesh.name = "Footing • " + plan.nodeId + " • " + part.filter.name;
                    mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    AssetDatabase.AddObjectToAsset(mesh, library); part.filter.sharedMesh = part.baked = mesh;
                    SetLod(part, false);
                }
                foreach (var shape in plan.shapes)
                {
                    if (!shape.footing) continue;
                    var box = shape.collider;
                    var low = box.transform.TransformPoint(box.center - Vector3.up * box.size.y * .5f);
                    var high = box.transform.TransformPoint(box.center + Vector3.up * box.size.y * .5f);
                    low.y = Mathf.Min(low.y, plan.minimumGround - Embed);
                    var a = box.transform.InverseTransformPoint(low); var b = box.transform.InverseTransformPoint(high);
                    box.center = (a + b) * .5f;
                    box.size = new Vector3(box.size.x, Mathf.Abs(b.y - a.y), box.size.z);
                }
                var lod = plan.root.GetComponent<CityObjectLod>();
                if (lod) UpdateBounds(lod);
            }
            baked.sourceHash = world.sourceHash; baked.props = plans.ToArray();
            Physics.SyncTransforms(); EditorUtility.SetDirty(baked); AssetDatabase.SaveAssets();
            Debug.Log("CITY_PROPS_GROUNDED count=" + plans.Count + " relocated=" + plans.Count(p => CityPhysics.Flat(p.position - p.originalPosition).magnitude > .01f));
        }

        static void Restore(CityPropGrounding baked)
        {
            foreach (var prop in baked.props)
            {
                if (prop.root && Vector3.Distance(prop.root.position, prop.position) < .001f) prop.root.position = prop.originalPosition;
                foreach (var part in prop.parts)
                {
                    if (!part.filter || !part.original) continue;
                    part.filter.sharedMesh = part.original; SetLod(part, true);
                }
                foreach (var shape in prop.shapes)
                    if (shape.collider) { shape.collider.center = shape.center; shape.collider.size = shape.size; }
            }
        }

        static void SetLod(CityPropGrounding.Part part, bool restore)
        {
            var lod = part.filter.GetComponentInParent<CityObjectLod>(); if (!lod) return;
            foreach (var item in lod.parts.Where(p => p.filter == part.filter))
            {
                item.full = restore ? part.original : part.baked;
                item.medium = restore ? part.medium : part.baked; item.far = restore ? part.far : part.baked;
                item.externalMesh = restore && part.externalMesh;
            }
        }

        static Bounds Bounds(MeshFilter filter)
        {
            var local = filter.sharedMesh.bounds;
            var result = new Bounds(filter.transform.TransformPoint(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(filter.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        static Vector3[] Samples(MeshFilter[] bases)
        {
            var points = new List<Vector3>();
            foreach (var filter in bases)
            {
                var b = filter.sharedMesh.bounds;
                int nx = Mathf.Max(2, Mathf.CeilToInt(filter.transform.TransformVector(Vector3.right * b.size.x).magnitude / .15f));
                int nz = Mathf.Max(2, Mathf.CeilToInt(filter.transform.TransformVector(Vector3.forward * b.size.z).magnitude / .15f));
                for (int x = 0; x <= nx; x++) for (int z = 0; z <= nz; z++)
                    points.Add(filter.transform.TransformPoint(new Vector3(Mathf.Lerp(b.min.x, b.max.x, x / (float)nx), b.min.y, Mathf.Lerp(b.min.z, b.max.z, z / (float)nz))));
            }
            return points.ToArray();
        }

        static float Floor(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 8, Vector3.down, 80, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore);
            return hits.Length > 0 ? hits.Max(h => h.point.y) : float.NaN;
        }

        static Vector3 DockPosition(WorldData world, Transform root, MeshFilter[] filters, Vector3[] samples)
        {
            var bounds = Bounds(filters[0]); foreach (var filter in filters) bounds.Encapsulate(Bounds(filter));
            float half = Mathf.Max(bounds.extents.x, bounds.extents.z) + .2f;
            foreach (var dock in world.docks.OrderBy(d => new Vector2(d.x - root.position.x, d.z - root.position.z).sqrMagnitude))
            {
                var center = new Vector3(dock.x, dock.top, dock.z); var rotation = Quaternion.Euler(0, dock.yaw * Mathf.Rad2Deg, 0);
                var local = Quaternion.Inverse(rotation) * (root.position - center);
                if (Mathf.Abs(local.x) > dock.w * .5f + 8 || Mathf.Abs(local.z) > dock.d * .5f + 8) continue;
                foreach (float side in new[] { Mathf.Sign(local.x), -Mathf.Sign(local.x) })
                {
                    if (dock.w * .5f <= half + .8f) continue;
                    var candidate = center + rotation * new Vector3(side * (dock.w * .5f - half), 0, Mathf.Clamp(local.z, -dock.d * .5f + half, dock.d * .5f - half));
                    candidate.y = root.position.y;
                    var delta = candidate - root.position;
                    if (samples.Any(p => float.IsNaN(Floor(p + delta)) || Mathf.Abs(Floor(p + delta) - dock.top) > .03f)) continue;
                    var body = bounds; body.center += delta; body.center = new Vector3(body.center.x, dock.top + bounds.extents.y + .04f, body.center.z);
                    if (Physics.OverlapBox(body.center, body.extents + new Vector3(.08f, 0, .08f), Quaternion.identity, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)
                        .Any(c => !c.transform.IsChildOf(root))) continue;
                    return candidate;
                }
            }
            throw new InvalidOperationException("Não há piso seco e livre para " + root.name);
        }

        static void UpdateBounds(CityObjectLod lod)
        {
            bool first = true; Bounds bounds = default;
            foreach (var part in lod.parts.Where(p => p.filter && p.filter.sharedMesh))
            {
                var b = Bounds(part.filter);
                for (int i = 0; i < 8; i++)
                {
                    var p = lod.transform.InverseTransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            lod.localBounds = bounds;
        }
    }
}
