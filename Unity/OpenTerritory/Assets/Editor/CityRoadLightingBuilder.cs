using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    public static class CityRoadLightingBuilder
    {
        const string RootName = "Postes • iluminação viária", AssetPath = "Assets/Imported/CityRoadLighting.asset";
        public static void Build(WorldData world, List<CityLighting.Fixture> fixtures)
        {
            var previous = GameObject.Find(RootName); if (previous) UnityEngine.Object.DestroyImmediate(previous);
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var asset = Resources.Load<TextAsset>("reference-map"); if (!asset) return;
            var layout = JsonUtility.FromJson<ReferenceLayout>(asset.text);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, AssetPath);
            Material Material(string name, Color color, Color emission)
            {
                var m = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                m.SetColor("_Color", color); m.SetColor("_EmissionColor", emission); m.SetFloat("_Glossiness", .24f); m.SetFloat("_Cull", 2);
                AssetDatabase.AddObjectToAsset(m, library); return m;
            }
            var metal = Material("Postes • grafite", new Color(.22f, .27f, .28f), Color.black);
            var bulb = Material("Postes • difusor quente", new Color(.96f, .94f, .84f), new Color(1, .93f, .82f));
            var root = new GameObject(RootName).transform; var poles = new List<CityObjectLod>();
            var occupied = fixtures.Where(f => f.place < 0).Select(f => f.source.transform.position).ToList();
            var overlaps = new Collider[8]; int count = 0;
            Physics.SyncTransforms();
            foreach (var road in layout.roads.Where(r => r.kind == "street"))
            {
                float next = 12, length = 0; int sequence = 0;
                for (int i = 1; i < road.points.Length; i++)
                {
                    var a = road.points[i - 1]; var b = road.points[i]; float segment = Vector3.Distance(a, b);
                    while (next <= length + segment)
                    {
                        Vector3 center = Vector3.Lerp(a, b, (next - length) / Mathf.Max(.001f, segment));
                        Vector3 forward = (b - a).normalized; forward.y = 0; forward.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, forward) * ((sequence++ & 1) == 0 ? 1 : -1);
                        next += 30;
                        Vector3 point = center + side * (road.width * .5f + 2.45f);
                        if (occupied.Any(p => new Vector2(p.x - point.x, p.z - point.z).sqrMagnitude < 19 * 19)) continue;
                        if (world.places.Any(p => CityLighting.DistanceTo(p, new Vector3(point.x, p.elevation + 1, point.z)) < .8f)) continue;
                        if (!Physics.Raycast(point + Vector3.up * 300, Vector3.down, out var floor, 500, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) continue;
                        point.y = floor.point.y;
                        point = CityMapAccessBuilder.RoadLightPosition(point);
                        if (point.y < -.2f || Physics.OverlapBoxNonAlloc(point + Vector3.up * 2.7f, new Vector3(.27f, 2.7f, .27f), overlaps,
                            Quaternion.identity, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore) > 0) continue;
                        var pole = new GameObject("Poste viário • " + road.id + " • " + count).transform;
                        pole.SetParent(root, false); pole.position = point; pole.rotation = Quaternion.LookRotation(-side, Vector3.up);
                        MeshRenderer Box(string name, Vector3 position, Vector3 size, Material material, bool collider)
                        {
                            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(pole, false);
                            go.transform.localPosition = position; go.transform.localScale = size; go.layer = CityContext.WorldLayer;
                            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
                        }
                        var mast = Box("Haste", new Vector3(0, 2.7f, 0), new Vector3(.16f, 5.4f, .16f), metal, true);
                        var baseRenderer = Box("Base", new Vector3(0, .12f, 0), new Vector3(.34f, .24f, .34f), metal, true);
                        var arm = Box("Braço sobre a calçada", new Vector3(0, 5.35f, .65f), new Vector3(.15f, .15f, 1.4f), metal, false);
                        var housing = Box("Luminária", new Vector3(0, 5.29f, 1.3f), new Vector3(.66f, .18f, .92f), metal, false);
                        var lens = Box("Difusor", new Vector3(0, 5.185f, 1.3f), new Vector3(.54f, .05f, .78f), bulb, false);
                        lens.shadowCastingMode = ShadowCastingMode.Off;
                        var emitter = new GameObject("Luz • poste viário").transform; emitter.SetParent(pole, false);
                        emitter.localPosition = new Vector3(0, 5.10f, 1.3f); emitter.rotation = Quaternion.LookRotation(Vector3.down, pole.forward);
                        var source = emitter.gameObject.AddComponent<Light>(); source.type = LightType.Spot; source.spotAngle = 150; source.innerSpotAngle = 110;
                        source.range = 30; source.color = new Color(1, .93f, .82f); source.renderMode = LightRenderMode.ForcePixel;
                        source.shadows = LightShadows.Soft; source.shadowBias = .015f; source.shadowNormalBias = .04f;
                        source.shadowResolution = LightShadowResolution.Medium; source.enabled = false;
                        fixtures.Add(new CityLighting.Fixture { kind = "poste viário", node = -100000 - count, place = -1, anchor = pole,
                            localSurface = new Vector3(0, 5.16f, 1.3f), localEmitter = emitter.localPosition, lens = lens, source = source, intensity = 1.35f });
                        occupied.Add(point); count++;
                        var lod = pole.gameObject.AddComponent<CityObjectLod>(); lod.category = "street-light"; lod.fullDistance = 20; lod.cullDistance = 550;
                        lod.localBounds = new Bounds(new Vector3(0, 2.8f, .65f), new Vector3(.7f, 5.6f, 2.3f));
                        lod.parts = new[] { mast, baseRenderer, arm, housing, lens }.Select(r => new CityLodPart { renderer = r, filter = r.GetComponent<MeshFilter>(),
                            full = r.GetComponent<MeshFilter>().sharedMesh, medium = r.GetComponent<MeshFilter>().sharedMesh, far = r.GetComponent<MeshFilter>().sharedMesh,
                            shadows = r.shadowCastingMode }).ToArray(); poles.Add(lod);
                    }
                    length += segment;
                }
            }
            var manager = UnityEngine.Object.FindFirstObjectByType<CityLodManager>(); if (manager) manager.objects = manager.objects.Where(l => l).Concat(poles).ToArray();
            AssetDatabase.SaveAssets(); Debug.Log("CITY_ROAD_LIGHTS_BAKED poles=" + count);
        }
    }
}
