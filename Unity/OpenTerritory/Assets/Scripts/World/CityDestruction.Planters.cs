using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    public sealed partial class CityDestruction
    {
        public int EntrancePlanterCount { get; private set; }

        void RegisterEntrancePlanters(Dictionary<int, List<Collider>> imported)
        {
            var sections = new List<BatchedSection>();
            var catalog = FindFirstObjectByType<CityHomeCatalog>();
            // Apartment replacements have their own entrances and no donor pots.
            var apartments = new HashSet<int>(catalog ? catalog.homes.Where(h => h.units > 1).Select(h => h.place) : Enumerable.Empty<int>());
            var originalCenters = new Dictionary<int, Vector3>();
            var garage = FindFirstObjectByType<CityFireGarage>();
            if (garage) foreach (var change in garage.colliders)
            {
                var shape = change.collider;
                if (shape && shape.name.StartsWith("Colisão-") && int.TryParse(shape.name.Substring(8), out int id))
                    originalCenters[id] = change.position + shape.transform.TransformVector(change.center);
            }
            foreach (var solid in ctx.Data.solids)
            {
                if (!solid.active || Mathf.Abs(solid.w - .7f) > .001f || Mathf.Abs(solid.d - .7f) > .001f) continue;
                // A garage refit has moved one pot clear of the driveway. Use
                // its authored identity, then bind the actual current geometry.
                Vector3 authored = originalCenters.TryGetValue(solid.index, out var original) ? original : new Vector3(solid.x, solid.bottom, solid.z);
                int place = -1;
                for (int i = 0; i < ctx.Data.places.Length; i++)
                {
                    if (apartments.Contains(i)) continue;
                    var home = ctx.Data.places[i];
                    var local = CityLighting.LocalPoint(home, authored);
                    if (Mathf.Abs(Mathf.Abs(local.x) - 3.5f) > .02f || Mathf.Abs(local.z - home.lotDepth * .5f - 1.05f) > .02f) continue;
                    place = i; break;
                }
                if (place < 0 || !imported.TryGetValue(solid.index, out var shapes)) continue;
                // Imported IDs can repeat. Resolve the real collider by its pose.
                var box = shapes.OfType<BoxCollider>().FirstOrDefault(s => s && s.enabled && s.gameObject.activeInHierarchy &&
                    CityPhysics.Flat(s.bounds.center - new Vector3(solid.x, 0, solid.z)).sqrMagnitude < .001f);
                if (!box || box.GetComponent<CityDestructible>()) continue;
                var prop = box.gameObject.AddComponent<CityDestructible>();
                prop.category = "entrance-planter"; prop.durability = 30; prop.place = place;
                var section = new BatchedSection { prop = prop };
                // Two original boxes: 70 cm terracotta pot and 85 cm crown.
                // The donor's 3.2 m collider includes empty air above the plant.
                var matrix = Matrix4x4.TRS(new Vector3(solid.x, solid.bottom + .7375f, solid.z),
                    Quaternion.Euler(0, solid.yaw * Mathf.Rad2Deg, 0), Vector3.one);
                section.AddVolume(matrix, new Vector3(.44f, .7525f, .44f));
                sections.Add(section);
            }
            AttachEntrancePlanterBatches(sections);
            foreach (var section in sections)
            {
                var prop = section.prop;
                if (section.TriangleCount != 24)
                {
                    Debug.LogWarning("Entrance planter: expected both boxes for " + prop.name + ", got " + section.TriangleCount);
                    Destroy(prop); continue;
                }
                prop.batchSection = section; outdoorSections.Add(section); Register(prop); EntrancePlanterCount++;
            }
        }

        void AttachEntrancePlanterBatches(List<BatchedSection> sections)
        {
            var grid = SectionGrid(sections);
            foreach (var owner in FindObjectsByType<CityObjectLod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (owner.category != "building") continue;
                foreach (var part in owner.parts)
                {
                    if (!part.full || !part.filter || !part.renderer || !part.renderer.sharedMaterial) continue;
                    var binding = new OutdoorLodBinding { part = part, full = part.full, medium = part.medium, far = part.far };
                    var clones = new Dictionary<Mesh, Mesh>();
                    Mesh Connect(Mesh source, bool full)
                    {
                        if (!source) return source;
                        if (clones.TryGetValue(source, out var previous)) return previous;
                        var points = source.vertices; var indices = source.triangles;
                        var matrix = part.filter.transform.localToWorldMatrix;
                        var assignments = new Dictionary<BatchedSection, List<int>>();
                        // Window cutting has turned these batches into triangle
                        // soup. Keep the authored building LODs and split only
                        // triangles wholly inside each plant, never the facade.
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            var a = matrix.MultiplyPoint3x4(points[indices[i]]);
                            var b = matrix.MultiplyPoint3x4(points[indices[i + 1]]);
                            var c = matrix.MultiplyPoint3x4(points[indices[i + 2]]);
                            if (!grid.TryGetValue(Cell((a + b + c) / 3), out var candidates)) continue;
                            foreach (var section in candidates)
                            {
                                if (!section.Contains(a) || !section.Contains(b) || !section.Contains(c)) continue;
                                if (!assignments.TryGetValue(section, out var list)) assignments[section] = list = new List<int>();
                                list.Add(i);
                                if (full) { section.Include(a); section.Include(b); section.Include(c); }
                                break;
                            }
                        }
                        if (assignments.Count == 0) { clones[source] = source; return source; }
                        var mesh = Instantiate(source); mesh.name = source.name + " · breakable entrance plants";
                        mesh.indexFormat = IndexFormat.UInt32; mesh.SetTriangles(indices, 0, false); mesh.MarkDynamic();
                        var colors = source.colors32;
                        if (colors.Length != points.Length) { colors = new Color32[points.Length]; for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255); }
                        var batch = new OutdoorBatch { source = source, mesh = mesh, points = points, normals = source.normals,
                            indices = indices, originals = colors, colors = (Color32[])colors.Clone(), matrix = matrix, material = part.renderer.sharedMaterial };
                        mesh.SetColors(batch.colors); outdoorMeshes.Add(mesh); clones[source] = mesh;
                        foreach (var pair in assignments)
                        {
                            var vertices = new HashSet<int>();
                            foreach (int offset in pair.Value) for (int n = 0; n < 3; n++) vertices.Add(indices[offset + n]);
                            pair.Key.pieces.Add(new BatchPiece { batch = batch, offsets = pair.Value.ToArray(), vertices = vertices.ToArray(), full = full });
                        }
                        return mesh;
                    }
                    part.full = Connect(binding.full, true);
                    part.medium = Connect(binding.medium, false); part.far = Connect(binding.far, false);
                    var current = part.filter.sharedMesh;
                    if (current == binding.full) part.filter.sharedMesh = part.full;
                    else if (current == binding.medium) part.filter.sharedMesh = part.medium;
                    else if (current == binding.far) part.filter.sharedMesh = part.far;
                    if (part.full != binding.full || part.medium != binding.medium || part.far != binding.far) outdoorBindings.Add(binding);
                }
            }
        }
    }
}
