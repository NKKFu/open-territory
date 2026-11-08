using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityLightingBuilder
    {
        [MenuItem("Open Territory/Reconstruir iluminação nas luminárias")]
        public static void BakeActiveScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de reconstruir a iluminação.");
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("A cena da cidade não está carregada.");
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            CityHomeBuilder.PrepareImport(visual,world);
            Bake(visual, world, registry.nodes.ToDictionary(n => n.id, n => n.transform));
            CityHomeBuilder.RebindLighting();CityFixtureDestructionBuilder.Bake();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void Bake(VisualData visual, WorldData world, Dictionary<int, Transform> nodes)
        {
            foreach (var previous in UnityEngine.Object.FindObjectsByType<CityLighting>(FindObjectsSortMode.None))
            {
                foreach (var fixture in previous.fixtures) if (fixture.source) UnityEngine.Object.DestroyImmediate(fixture.source.gameObject);
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }
            var lighting = new GameObject("Iluminação • fontes nas luminárias").AddComponent<CityLighting>();
            var fixtures = new List<CityLighting.Fixture>();
            var lookup = visual.nodes.ToDictionary(n => n.id);
            var lampMaterials = new HashSet<int>(world.streetLamps.Where(l => lookup.ContainsKey(l.bulb)).SelectMany(l => lookup[l.bulb].materials));
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var staticRoots = renderers.GroupBy(r => r.transform.parent ? r.transform.parent.name : "").ToDictionary(g => g.Key, g => g.ToArray());
            var bounds = visual.geometries.Select(BoundsOf).ToArray();

            Renderer Lens(VisualNode node, Vector3 point)
            {
                var anchor = nodes[node.id];
                if (node.dynamic) return anchor.GetComponentInChildren<Renderer>(true);
                string key = CityLodBuilder.StaticKey(node, visual.materials[node.materials[0]], point);
                if (!staticRoots.TryGetValue(key, out var candidates)) return null;
                string material = visual.materials[node.materials[0]].name;
                return candidates.FirstOrDefault(r => r.sharedMaterials.Any(m => m && m.name == material));
            }

            void Add(VisualNode node, Matrix4x4 matrix, Bounds shape, string kind, int place, Vector3 direction, float offset)
            {
                var anchor = nodes[node.id];
                Vector3 center = matrix.MultiplyPoint3x4(shape.center);
                // Use the real mesh's lower face for ceiling lamps and street-lamp bulbs.
                Vector3 surface = direction == Vector3.down ? matrix.MultiplyPoint3x4(shape.center - Vector3.up * shape.extents.y) : center;
                Vector3 point = surface + direction * offset;
                var sourceObject = new GameObject("Luz • " + kind + " • " + node.id + " • " + fixtures.Count);
                sourceObject.transform.SetParent(anchor, false);
                sourceObject.transform.position = point;
                var source = sourceObject.AddComponent<Light>();
                source.type = kind == "balizador do cais" ? LightType.Point : LightType.Spot; source.enabled = false;
                if (source.type == LightType.Spot)
                {
                    // A ceiling panel emits into the room, not into its own housing/slab.
                    sourceObject.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    source.spotAngle = place >= 0 ? 165 : 155; source.innerSpotAngle = place >= 0 ? 125 : 110;
                }
                source.color = place >= 0 && world.places[place].kind == "hospital" ? new Color(.95f, .98f, 1) : new Color(1, .93f, .82f);
                float size = place >= 0 ? Mathf.Max(world.places[place].lotWidth, world.places[place].lotDepth) : 0;
                source.range = place >= 0 ? Mathf.Clamp(size * .82f, 16, 20) : kind == "poste" ? 24 : 6;
                source.renderMode = LightRenderMode.ForcePixel;
                source.shadows = LightShadows.Soft; source.shadowBias = .015f; source.shadowNormalBias = .04f;
                source.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Medium;
                var lens = Lens(node, center);
                if (place >= 0 && lens) lens.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fixtures.Add(new CityLighting.Fixture { kind = kind, node = node.id, place = place, anchor = anchor,
                    localSurface = anchor.InverseTransformPoint(surface), localEmitter = anchor.InverseTransformPoint(point),
                    lens = lens, source = source, intensity = place >= 0 ? 1.05f : kind == "poste" ? 1.2f : .8f });
            }

            foreach (var entry in world.streetLamps)
            {
                if (!lookup.TryGetValue(entry.bulb, out var node) || !nodes.ContainsKey(entry.bulb) || node.geometry < 0) continue;
                Add(node, nodes[node.id].localToWorldMatrix, bounds[node.geometry], "poste", -1, Vector3.down, .06f);
            }
            foreach (var node in visual.nodes)
            {
                if (node.geometry < 0 || node.instances.Length == 0 || !nodes.ContainsKey(node.id)) continue;
                bool ceilingMaterial = node.materials.Any(lampMaterials.Contains);
                bool pierMaterial = node.name == "Reused harbor" && node.materials.Any(i => visual.materials[i].emission.maxColorComponent > .1f);
                if (!ceilingMaterial && !pierMaterial) continue;
                foreach (var instance in node.instances)
                {
                    Matrix4x4 matrix = nodes[node.id].localToWorldMatrix * Matrix(instance.matrix);
                    var shape = bounds[node.geometry];
                    Vector3 size = new Vector3(matrix.MultiplyVector(Vector3.right * shape.size.x).magnitude,
                        matrix.MultiplyVector(Vector3.up * shape.size.y).magnitude, matrix.MultiplyVector(Vector3.forward * shape.size.z).magnitude);
                    Vector3 point = matrix.MultiplyPoint3x4(shape.center);
                    if (ceilingMaterial && size.y < .2f && size.x > 1 && size.z > .4f)
                    {
                        int place = Array.FindIndex(world.places, p => CityLighting.Inside(p, point - Vector3.up * .3f)
                            && point.y - p.elevation > 4.5f && point.y - p.elevation < 5.2f);
                        if (place >= 0) Add(node, matrix, shape, "teto", place, Vector3.down, .08f);
                    }
                    // Four low bollard lamps at the pier; ignore emissive crane instruments.
                    if (pierMaterial && size.x < .4f && size.z < .4f && size.y < .2f)
                        Add(node, matrix, shape, "balizador do cais", -1, Vector3.up, .10f);
                }
            }
            CityRoadLightingBuilder.Build(world, fixtures);
            lighting.fixtures = fixtures.ToArray();
            lighting.rooms = Enumerable.Range(0, world.places.Length).Select(i => {
                string key = i < 17 ? "building:Reused building-" + i : "building:Reused residential interior " + (i - 17);
                return new CityLighting.Room { place = i, surfaces = staticRoots.TryGetValue(key, out var surfaces) ? surfaces.Cast<Renderer>().ToArray() : Array.Empty<Renderer>() };
            }).ToArray();
            foreach (int id in world.streetLightIds.Concat(world.shopLightIds).Append(world.canopyLightId))
                if (nodes.TryGetValue(id, out var node) && node.TryGetComponent<Light>(out var legacy)) legacy.enabled = false;
            int interiors = fixtures.Count(f => f.place >= 0);
            if (Enumerable.Range(0, world.places.Length).Any(i => !CityHomeBuilder.Housing(world.places[i]) && !fixtures.Any(f => f.place == i)) || fixtures.Any(f => !f.lens))
                throw new InvalidOperationException("Iluminação incompleta: interiores=" + interiors + ", sem lente=" + fixtures.Count(f => !f.lens));
            Debug.Log("CITY_LIGHTING_BAKED interiors=" + interiors + " streets=" + world.streetLamps.Length + " fixtures=" + fixtures.Count);
        }

        static Matrix4x4 Matrix(float[] values)
        {
            var matrix = new Matrix4x4();
            for (int c = 0; c < 4; c++) matrix.SetColumn(c, new Vector4(values[c * 4], values[c * 4 + 1], values[c * 4 + 2], values[c * 4 + 3]));
            return matrix;
        }

        static Bounds BoundsOf(GeometryData geometry)
        {
            if (geometry.positions.Length < 3) return default;
            var bounds = new Bounds(new Vector3(geometry.positions[0], geometry.positions[1], geometry.positions[2]), Vector3.zero);
            for (int i = 3; i < geometry.positions.Length; i += 3)
                bounds.Encapsulate(new Vector3(geometry.positions[i], geometry.positions[i + 1], geometry.positions[i + 2]));
            return bounds;
        }
    }
}
