using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // A few dock bulbs came from a static material batch. Give each its own
    // geometry before play, retaining every surrounding part of that batch.
    public static class CityFixtureDestructionBuilder
    {
        public static void Bake()
        {
            var lighting = UnityEngine.Object.FindFirstObjectByType<CityLighting>(); if (!lighting) return;
            var library = AssetDatabase.LoadMainAssetAtPath("Assets/Imported/CityHomes.asset");
            var lods = UnityEngine.Object.FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None);
            var added = new List<CityObjectLod>(); int count = 0;
            foreach (var fixture in lighting.fixtures)
            {
                if (!fixture.anchor || !fixture.lens || fixture.lens.transform.IsChildOf(fixture.anchor)) continue;
                string name = "Luminária destrutível • " + fixture.node;
                var existing = fixture.anchor.Find(name);
                Vector3 surface = fixture.anchor.TransformPoint(fixture.localSurface), emitter = fixture.source.transform.position;
                if (existing)
                {
                    fixture.anchor = existing; fixture.lens = existing.GetComponent<Renderer>();
                    fixture.source.transform.SetParent(existing, true); fixture.localSurface = existing.InverseTransformPoint(surface); fixture.localEmitter = existing.InverseTransformPoint(emitter); continue;
                }
                var part = lods.SelectMany(l => l.parts).FirstOrDefault(p => p.renderer == fixture.lens);
                if (part == null || !part.full) throw new InvalidOperationException("Luminária sem malha de origem: " + fixture.node);
                var region = new Bounds(surface, new Vector3(.68f, .24f, .68f));
                var matrix = part.filter.transform.localToWorldMatrix;
                var source = part.full; var vertices = source.vertices; var selected = new List<int>(); var kept = new List<int>();
                bool Inside(int index) => region.Contains(matrix.MultiplyPoint3x4(vertices[index]));
                var indices = source.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                    (Inside(indices[i]) && Inside(indices[i + 1]) && Inside(indices[i + 2]) ? selected : kept).AddRange(new[] { indices[i], indices[i + 1], indices[i + 2] });
                if (selected.Count == 0) throw new InvalidOperationException("Geometria luminosa não encontrada: " + fixture.node + " em " + surface);
                var root = new GameObject(name).transform; root.SetParent(fixture.anchor, true); root.position = surface; root.rotation = Quaternion.identity; root.gameObject.layer = CityContext.WorldLayer;
                var map = new Dictionary<int, int>(); var points = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var colors = new List<Color>(); var ownIndices = new List<int>();
                var sourceNormals = source.normals; var sourceUv = source.uv; var sourceColors = source.colors;
                foreach (int index in selected)
                {
                    if (!map.TryGetValue(index, out int own))
                    {
                        own = points.Count; map[index] = own; points.Add(root.InverseTransformPoint(matrix.MultiplyPoint3x4(vertices[index])));
                        normals.Add(sourceNormals.Length == vertices.Length ? root.InverseTransformDirection(matrix.inverse.transpose.MultiplyVector(sourceNormals[index])).normalized : Vector3.up);
                        uv.Add(sourceUv.Length == vertices.Length ? sourceUv[index] : Vector2.zero); colors.Add(sourceColors.Length == vertices.Length ? sourceColors[index] : Color.white);
                    }
                    ownIndices.Add(own);
                }
                var mesh = new Mesh { name = name }; mesh.SetVertices(points); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(ownIndices, 0); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = root.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = fixture.lens.sharedMaterial;
                var box = root.gameObject.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; var size = mesh.bounds.size; size.y = Mathf.Max(.09f, size.y); box.size = size;
                var prop = root.gameObject.AddComponent<CityDestructible>(); prop.category = "light"; prop.nodeId = fixture.node; prop.durability = 18; prop.visuals = new Renderer[] { renderer }; prop.shapes = new Collider[] { box };
                Mesh Remove(Mesh original)
                {
                    if (!original) return original;
                    var p = original.vertices; var triangles = original.triangles; var remaining = new List<int>();
                    for (int j = 0; j < triangles.Length; j += 3)
                    {
                        bool remove = region.Contains(matrix.MultiplyPoint3x4(p[triangles[j]])) && region.Contains(matrix.MultiplyPoint3x4(p[triangles[j + 1]])) && region.Contains(matrix.MultiplyPoint3x4(p[triangles[j + 2]]));
                        if (!remove) remaining.AddRange(new[] { triangles[j], triangles[j + 1], triangles[j + 2] });
                    }
                    if (remaining.Count == triangles.Length) return original;
                    var result = UnityEngine.Object.Instantiate(original); result.name = original.name + " • luminária separada"; result.SetTriangles(remaining, 0); AssetDatabase.AddObjectToAsset(result, library); return result;
                }
                part.full = Remove(part.full); part.medium = Remove(part.medium); part.far = Remove(part.far); part.filter.sharedMesh = part.full; part.externalMesh = false;
                fixture.anchor = root; fixture.lens = renderer; fixture.source.transform.SetParent(root, true); fixture.localSurface = root.InverseTransformPoint(surface); fixture.localEmitter = root.InverseTransformPoint(emitter);
                var lod = root.gameObject.AddComponent<CityObjectLod>(); lod.category = "prop"; lod.localBounds = mesh.bounds; lod.fullDistance = 20; lod.cullDistance = 550;
                lod.parts = new[] { new CityLodPart { renderer = renderer, filter = root.GetComponent<MeshFilter>(), full = mesh, medium = mesh, far = mesh, shadows = renderer.shadowCastingMode } }; added.Add(lod); count++;
            }
            var manager = UnityEngine.Object.FindFirstObjectByType<CityLodManager>(); if (manager) manager.objects = manager.objects.Where(l => l).Concat(added).ToArray();
            // Clustering can move a distant bulb's vertices outside its exact
            // full-detail bounds. Clear that same bulb from the source LODs too.
            foreach(var fixture in lighting.fixtures.Where(f=>f.kind=="balizador do cais"&&f.anchor&&f.lens))
            {
                Vector3 point=fixture.anchor.TransformPoint(fixture.localSurface);var region=new Bounds(point,new Vector3(.96f,.90f,.96f));
                foreach(var part in lods.SelectMany(l=>l.parts).Where(p=>p.renderer&&p.renderer!=fixture.lens&&p.renderer.sharedMaterial==fixture.lens.sharedMaterial&&p.renderer.bounds.SqrDistance(point)<1))
                {
                    Mesh Clear(Mesh original)
                    {
                        if(!original)return original;var vertices=original.vertices;var indices=original.triangles;var remaining=new List<int>();var matrix=part.filter.transform.localToWorldMatrix;
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            if(region.Contains(matrix.MultiplyPoint3x4(vertices[indices[i]]))&&region.Contains(matrix.MultiplyPoint3x4(vertices[indices[i+1]]))&&region.Contains(matrix.MultiplyPoint3x4(vertices[indices[i+2]])))continue;
                            remaining.Add(indices[i]);remaining.Add(indices[i+1]);remaining.Add(indices[i+2]);
                        }
                        if(remaining.Count==indices.Length)return original;var mesh=UnityEngine.Object.Instantiate(original);mesh.name=original.name+" • sem bulbo agrupado";mesh.SetTriangles(remaining,0);AssetDatabase.AddObjectToAsset(mesh,library);return mesh;
                    }
                    part.full=Clear(part.full);part.medium=Clear(part.medium);part.far=Clear(part.far);part.filter.sharedMesh=part.full;
                }
            }
            var homes=UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            if(homes)foreach(var home in homes.homes.Where(h=>h.units>1)){var lod=home.root.GetComponent<CityObjectLod>();if(lod)lod.category="building";}
            foreach(var lod in UnityEngine.Object.FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None))
            {
                if(lod.category=="light-prop")lod.category="prop";
                foreach(var part in lod.parts)
                {
                    if(!part.full||!part.medium||!part.far)continue;
                    // An exact cut may be smaller than a previously simplified
                    // mesh. Keeping the exact version is the conservative LOD.
                    if(part.medium.triangles.Length>part.full.triangles.Length)part.medium=part.full;
                    if(part.far.triangles.Length>part.medium.triangles.Length)part.far=part.medium;
                    if(part.filter)part.filter.sharedMesh=part.full;
                }
            }
            EditorUtility.SetDirty(lighting); AssetDatabase.SaveAssets(); Debug.Log("CITY_FIXTURES_DESTRUCTIBLE_BAKED isolated=" + count);
        }
        // Batch-mode audit uses the exact player capsule with the leaves open.
        public static void BakeAndAudit()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity"); Bake();CityInteriorGroundBuilder.Bake();CityHomeBuilder.RebindWindows();
            var homes = UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            foreach (var door in homes.doors) door.leaf.gameObject.SetActive(false); Physics.SyncTransforms();
            var blocked = new List<string>();
            foreach (var room in homes.rooms)
            {
                Vector3 feet = room.entrance + Vector3.up * .045f;
                var collisions = Physics.OverlapCapsule(feet + Vector3.up * .41f, feet + Vector3.up * 1.98f, .37f, ~0, QueryTriggerInteraction.Ignore);
                if (collisions.Length > 0) blocked.Add(room.place + "/" + room.unit + "/" + room.name + " " + feet + " => " + string.Join("; ", collisions.Select(c => c.name + " parent=" + (c.transform.parent ? c.transform.parent.name : "") + " center=" + c.bounds.center + " size=" + c.bounds.size)));
            }
            foreach (var door in homes.doors) door.leaf.gameObject.SetActive(true);
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-interiors-audit");
            string path = at >= 0 && at + 1 < args.Length ? args[at + 1] : "../Reports/interiors-doorway-audit.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, blocked); Debug.Log("CITY_INTERIOR_DOORWAY_AUDIT blocked=" + blocked.Count);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        public static void BakeAuditAndBuild(){BakeAndAudit();CityBuilder.Build();}
    }
}
