using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    public static class CityLodBuilder
    {
        // Separate traversable surfaces before batching: never decimate continuous terrain,
        // road, sidewalk, pier or water meshes. Building shells and furnishings stay separate.
        public static string StaticKey(VisualNode node, MaterialData material, Vector3 point)
        {
            string name = node.name ?? "";
            bool building = name.StartsWith("Reused building-") || name.StartsWith("Reused residential interior ") || name == "Reused cinema" || name == "Reused fish";
            string category = building ? "building" : "scenery";
            if (!building && (material.sea != 0 || material.surface != 0 || name.Contains("terreno", StringComparison.OrdinalIgnoreCase)
                || name.Contains("calçada", StringComparison.OrdinalIgnoreCase) || name.Contains("asfalto", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Passarela") || name.Contains("Cais nivelado") || name.Contains("embarque"))) category = "surface";
            // One building owns its static pieces even when it crosses a 32 m region boundary.
            return category == "building" ? category + ":" + name : category + ":" + Mathf.FloorToInt(point.x / 24) + ":" + Mathf.FloorToInt(point.z / 24);
        }

        public static void Build(Transform root, Transform staticRoot, Dictionary<int, Transform> nodes, WorldData world, UnityEngine.Object library)
        {
            var cache = new Dictionary<Mesh, Mesh[]>();
            var owned = new HashSet<Renderer>();
            var objects = new List<CityObjectLod>();
            void Add(Transform owner, string category)
            {
                if (!owner) return;
                var renderers = owner.GetComponentsInChildren<MeshRenderer>(true).Where(r => !owned.Contains(r)).ToArray();
                if (renderers.Length == 0) return;
                PlaceData place = null;
                const string originalPrefix = "building:Reused building-", homePrefix = "building:Reused residential interior ";
                if (owner.name.StartsWith(originalPrefix) && int.TryParse(owner.name.Substring(originalPrefix.Length), out int buildingIndex) && buildingIndex < world.places.Length) place = world.places[buildingIndex];
                if (owner.name.StartsWith(homePrefix) && int.TryParse(owner.name.Substring(homePrefix.Length), out int homeIndex)) place = world.places.FirstOrDefault(p => p.name == "Casa da orla " + (homeIndex + 1));
                Bounds buildingBounds = renderers[0].bounds;
                foreach (var r in renderers) buildingBounds.Encapsulate(r.bounds);
                var parts = new List<CityLodPart>(); Bounds bounds = default; bool first = true;
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) continue;
                    var source = filter.sharedMesh;
                    if (!cache.TryGetValue(source, out var levels))
                    {
                        // Do not join shells or erase openings. Vertex clustering only collapses
                        // nearby vertices in the same normal/UV/color/surface bucket.
                        levels = new[] { source, Simplify(source, 1, category == "building", place, buildingBounds), Simplify(source, 2, category == "building", place, buildingBounds) };
                        if (levels[2].triangles.Length > levels[1].triangles.Length)
                        {
                            if (levels[2] != source) UnityEngine.Object.DestroyImmediate(levels[2]);
                            levels[2] = levels[1];
                        }
                        foreach (var mesh in levels.Distinct()) if (mesh != source) AssetDatabase.AddObjectToAsset(mesh, library);
                        cache[source] = levels;
                    }
                    var worldSize = renderer.bounds.size;
                    float extent = Mathf.Max(worldSize.x, worldSize.y, worldSize.z);
                    // Remove small trim, facial details and fasteners only beyond interaction range.
                    int last = extent < .075f ? 0 : extent < (category == "building" ? .42f : .22f) ? 1 : 2;
                    parts.Add(new CityLodPart { filter = filter, renderer = renderer, full = source, medium = levels[1], far = levels[2], shadows = renderer.shadowCastingMode, lastVisibleLevel = last });
                    owned.Add(renderer);
                    var meshBounds = source.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        Vector3 local = owner.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        if (first) { bounds = new Bounds(local, Vector3.zero); first = false; } else bounds.Encapsulate(local);
                    }
                }
                if (parts.Count == 0) return;
                // A whole low-poly entity must retain at least its largest piece at far LOD.
                if (parts.All(p => p.lastVisibleLevel < 2)) parts.OrderByDescending(p => p.renderer.bounds.size.sqrMagnitude).First().lastVisibleLevel = 2;
                var lod = owner.gameObject.AddComponent<CityObjectLod>(); lod.category = category; lod.parts = parts.ToArray(); lod.localBounds = bounds;
                lod.fullDistance = category == "building" ? 35 : category == "vehicle" || category == "boat" ? 22 : 12;
                lod.cullDistance = category == "building" ? 550 : category == "npc" || category == "animal" ? 240 : 420;
                lod.mediumHeight = category == "npc" || category == "animal" ? .055f : .10f;
                lod.farHeight = category == "npc" || category == "animal" ? .018f : .035f;
                objects.Add(lod);
            }
            // Specific entities first; passengers can belong to vehicle hierarchies.
            foreach (var resident in world.residents) if (nodes.TryGetValue(resident.id, out var t)) Add(t, "npc");
            foreach (var animal in world.animals) if (nodes.TryGetValue(animal.id, out var t)) Add(t, "animal");
            foreach (var vehicle in world.vehicles) if (nodes.TryGetValue(vehicle.id, out var t)) Add(t, "vehicle");
            foreach (var boat in world.boats) if (nodes.TryGetValue(boat.id, out var t)) Add(t, "boat");
            foreach (Transform child in staticRoot) if (!child.name.StartsWith("surface:")) Add(child, child.name.StartsWith("building:") ? "building" : "scenery");
            // Dynamic props retain their entire original hierarchy. Exclude the player and trees.
            Transform visualRoot = root.Find("Assets reaproveitados e articulações");
            if (visualRoot) foreach (Transform child in visualRoot)
            {
                if (nodes.TryGetValue(world.playerId, out var player) && (child == player || player.IsChildOf(child))) continue;
                Add(child, "prop");
            }
            var manager = root.gameObject.AddComponent<CityLodManager>(); manager.objects = objects.ToArray();
            var categories = objects.GroupBy(o => o.category).Select(g => new Category { name = g.Key, objects = g.Count(), renderers = g.Sum(o => o.parts.Length),
                triangles = Enumerable.Range(0, 3).Select(l => g.Sum(o => o.parts.Where(p => p.lastVisibleLevel >= l).Sum(p => (l == 0 ? p.full : l == 1 ? p.medium : p.far).triangles.Length / 3))).ToArray() }).ToArray();
            System.IO.Directory.CreateDirectory("../Reports/lod");
            System.IO.File.WriteAllText("../Reports/lod/import.json", JsonUtility.ToJson(new Report { categories = categories, sharedSources = cache.Count, meshes = cache.Values.SelectMany(a => a).Distinct().Count() }, true));
            Debug.Log("WORLD_LOD_IMPORT_OK objects=" + objects.Count + " renderers=" + owned.Count + " sharedSources=" + cache.Count);
        }

        [Serializable] sealed class Category { public string name; public int objects, renderers; public int[] triangles; }
        [Serializable] sealed class Report { public Category[] categories; public int sharedSources, meshes; }

        struct Bucket : IEquatable<Bucket>
        {
            public int x, y, z, normal, u, v, color, surface;
            public bool pinned;
            public bool Equals(Bucket b) => x == b.x && y == b.y && z == b.z && normal == b.normal && u == b.u && v == b.v && color == b.color && surface == b.surface && pinned == b.pinned;
            public override bool Equals(object o) => o is Bucket b && Equals(b);
            public override int GetHashCode() { unchecked { int h = x; h = h * 397 ^ y; h = h * 397 ^ z; h = h * 397 ^ normal; h = h * 397 ^ u; h = h * 397 ^ v; h = h * 397 ^ color; h = h * 397 ^ surface; return h * 397 ^ (pinned ? 1 : 0); } }
        }

        static bool[] RemovedComponents(Mesh source, int level, PlaceData place, Bounds buildingBounds)
        {
            var vertices = source.vertices; var indices = source.triangles;
            var parent = Enumerable.Range(0, vertices.Length).ToArray();
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            var positions = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i]; var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                if (positions.TryGetValue(key, out int previous)) Union(i, previous); else positions[key] = i;
            }
            for (int i = 0; i < indices.Length; i += 3) { Union(indices[i], indices[i+1]); Union(indices[i], indices[i+2]); }
            var bounds = new Dictionary<int, Bounds>();
            foreach (int index in indices)
            {
                int root = Find(index);
                if (!bounds.TryGetValue(root, out var box)) box = new Bounds(vertices[index], Vector3.zero);
                box.Encapsulate(vertices[index]); bounds[root] = box;
            }
            var removed = new HashSet<int>();
            foreach (var pair in bounds)
            {
                var box = pair.Value; float size = Mathf.Max(box.size.x, box.size.y, box.size.z);
                bool small = size < (level == 1 ? .16f : .55f);
                // Remove enclosed furniture/partitions only at far LOD. Preserve floors, the
                // exterior envelope, window openings, roofs and anything above the roof line.
                bool interior = level == 2 && place != null && box.min.y > place.elevation + .3f && box.max.y < buildingBounds.max.y - .65f;
                if (interior) for (int corner = 0; corner < 4; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x, 0, (corner & 2) == 0 ? box.min.z : box.max.z);
                    if (!place.Contains(point, .65f)) { interior = false; break; }
                }
                if (small || interior) removed.Add(pair.Key);
            }
            var result = new bool[vertices.Length]; for (int i = 0; i < result.Length; i++) result[i] = removed.Contains(Find(i));
            return result;
        }

        static Mesh Simplify(Mesh source, int level, bool building, PlaceData place, Bounds buildingBounds)
        {
            if (!building && source.triangles.Length <= 36) return source; // A box is already minimal.
            var vertices = source.vertices; var normals = source.normals; var uv = source.uv; var uv2 = source.uv2; var colors = source.colors;
            bool[] removed = building ? RemovedComponents(source, level, place, buildingBounds) : null;
            var size = source.bounds.size; float longest = Mathf.Max(size.x, size.y, size.z);
            float cell = Mathf.Min(level == 1 ? .18f : .45f, longest * (level == 1 ? .055f : .14f));
            if (cell < .0001f) return source;
            Vector3Int Exact(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));
            var pinnedPositions = new HashSet<Vector3Int>(); var originalIndices = source.triangles;
            float areaLimit = longest * longest * .01f;
            for (int i = 0; i < originalIndices.Length; i += 3)
            {
                Vector3 a = vertices[originalIndices[i]], b = vertices[originalIndices[i+1]], c = vertices[originalIndices[i+2]];
                // Large facade, roof and body panels must keep their corners. Only tessellation
                // and small trim may collapse; otherwise a thin roof strip can disappear.
                if (Vector3.Cross(b - a, c - a).magnitude * .5f <= areaLimit) continue;
                pinnedPositions.Add(Exact(a)); pinnedPositions.Add(Exact(b)); pinnedPositions.Add(Exact(c));
            }
            var buckets = new Dictionary<Bucket, int>(); var map = new int[vertices.Length];
            var spatialSums = new Dictionary<(int, int, int, bool), Vector3>(); var spatialCounts = new Dictionary<(int, int, int, bool), int>();
            var spatialKeys = new List<(int, int, int, bool)>();
            var points = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var vs = new List<Vector2>(); var cs = new List<Color>(); var counts = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i] - source.bounds.min, n = normals.Length == vertices.Length ? normals[i] : Vector3.up;
                Vector2 a = uv.Length == vertices.Length ? uv[i] : Vector2.zero, b = uv2.Length == vertices.Length ? uv2[i] : Vector2.zero;
                Color c = colors.Length == vertices.Length ? colors[i] : Color.white;
                // Keep opposite sides and texture seams separate, including very thin windows.
                int axis = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2) : Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2;
                var key = new Bucket { x = Mathf.RoundToInt(p.x / cell), y = Mathf.RoundToInt(p.y / cell), z = Mathf.RoundToInt(p.z / cell), normal = axis * 2 + (n[axis] < 0 ? 1 : 0),
                    u = Mathf.RoundToInt(a.x * 8), v = Mathf.RoundToInt(a.y * 8), surface = Mathf.RoundToInt(b.x * 100), color = Mathf.RoundToInt(c.r * 16) * 4096 + Mathf.RoundToInt(c.g * 16) * 64 + Mathf.RoundToInt(c.b * 16) };
                if (pinnedPositions.Contains(Exact(vertices[i])))
                {
                    var exact = Exact(p); key.x = exact.x; key.y = exact.y; key.z = exact.z; key.pinned = true;
                }
                var spatial = (key.x, key.y, key.z, key.pinned);
                if (removed == null || !removed[i])
                {
                    spatialSums.TryGetValue(spatial, out var sum); spatialCounts.TryGetValue(spatial, out int count);
                    spatialSums[spatial] = sum + vertices[i]; spatialCounts[spatial] = count + 1;
                }
                if (!buckets.TryGetValue(key, out int index))
                {
                    index = points.Count; buckets[key] = index; points.Add(vertices[i]); ns.Add(n); us.Add(a); vs.Add(b); cs.Add(c); counts.Add(1);
                    spatialKeys.Add(spatial);
                }
                else { points[index] += vertices[i]; ns[index] += n; us[index] += a; vs[index] += b; cs[index] += c; counts[index]++; }
                map[i] = index;
            }
            // Adjacent normal/UV/color buckets share the same collapsed position. Averaging
            // each attribute bucket independently opens cracks along hard edges and UV seams.
            for (int i = 0; i < points.Count; i++)
            {
                var key = spatialKeys[i];
                points[i] = spatialCounts.TryGetValue(key, out int count) ? spatialSums[key] / count : points[i] / counts[i];
                ns[i] = ns[i].normalized; us[i] /= counts[i]; vs[i] /= counts[i]; cs[i] /= counts[i];
            }
            var submeshes = new List<int[]>(); int triangleCount = 0;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var src = source.GetTriangles(s); var dst = new List<int>(); var seen = new HashSet<(int, int, int)>();
                for (int i = 0; i < src.Length; i += 3)
                {
                    if (removed != null && removed[src[i]]) continue;
                    int a = map[src[i]], b = map[src[i+1]], c = map[src[i+2]];
                    if (a == b || a == c || b == c || Vector3.Cross(points[b] - points[a], points[c] - points[a]).sqrMagnitude < 1e-14f) continue;
                    var key = a < b && a < c ? (a,b,c) : b < c ? (b,c,a) : (c,a,b);
                    if (!seen.Add(key)) continue;
                    dst.Add(a); dst.Add(b); dst.Add(c);
                }
                submeshes.Add(dst.ToArray()); triangleCount += dst.Count;
            }
            if (triangleCount >= source.triangles.Length * .98f) return source;
            var result = new Mesh { name = source.name + "-LOD" + level, indexFormat = points.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            result.SetVertices(points); result.SetNormals(ns); result.SetUVs(0, us); result.SetUVs(1, vs); result.SetColors(cs); result.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) result.SetTriangles(submeshes[s], s);
            result.RecalculateBounds(); return result;
        }
    }
}
