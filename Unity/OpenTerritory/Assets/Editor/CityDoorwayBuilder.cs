using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Apply only the transformed door nodes. This preserves the current scene's
    // other authored meshes, lighting and LOD assets; a full import uses the
    // same visual.json and world.json through CityBuilder as usual.
    public static class CityDoorwayBuilder
    {
        [MenuItem("Open Territory/Atualizar largura das portas")]
        public static void BakeActiveScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de atualizar portas.");
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("A cena da cidade não está carregada.");
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var map = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            var doors = new HashSet<int>(world.places.Where(p => !p.door.automatic).Select(p => p.door.id));
            var panels = new HashSet<int>(visual.nodes.Where(n => n.name.StartsWith("Doorway sidelight-")).Select(n => n.id));
            var parent = map[doors.First()].parent;
            Undo.RecordObject(registry, "Atualizar portas");
            int changed = 0;
            foreach (var node in visual.nodes)
            {
                if (!doors.Contains(node.id) && !doors.Contains(node.parent) && !panels.Contains(node.id) && !panels.Contains(node.parent)) continue;
                if (!map.TryGetValue(node.id, out var t))
                {
                    var go = new GameObject(node.name); Undo.RegisterCreatedObjectUndo(go, "Painel lateral fixo");
                    t = go.transform; t.SetParent(node.parent >= 0 ? map[node.parent] : parent, false); map.Add(node.id, t);
                    if (node.geometry >= 0)
                    {
                        int sourceId = int.Parse(node.name.Substring(node.name.LastIndexOf('-') + 1));
                        var source = map[sourceId];
                        go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                        var renderer = go.AddComponent<MeshRenderer>(); var original = source.GetComponent<MeshRenderer>();
                        renderer.sharedMaterials = original.sharedMaterials; renderer.shadowCastingMode = original.shadowCastingMode;
                    }
                }
                Undo.RecordObject(t, "Ajustar folha da porta");
                t.localPosition = node.position; t.localRotation = node.rotation; t.localScale = node.scale; changed++;
            }
            foreach (var solid in world.solids.Where(s => panels.Contains(s.owner)))
            {
                var t = map[solid.owner]; t.gameObject.layer = CityContext.WorldLayer;
                var box = t.GetComponent<BoxCollider>(); if (!box) box = Undo.AddComponent<BoxCollider>(t.gameObject);
                Undo.RecordObject(box, "Colisão do painel fixo");
                box.center = t.InverseTransformPoint(new Vector3(solid.x, (solid.bottom + solid.top) / 2, solid.z));
                box.size = new Vector3(solid.w, solid.top - solid.bottom, solid.d);
            }
            registry.nodes = map.Select(n => new NodeReference { id = n.Key, transform = n.Value }).ToArray();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("DOORWAY_GEOMETRY_UPDATED nodes=" + changed + " fixedPanels=" + panels.Count);
        }
    }
}
