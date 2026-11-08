using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    // Authored materials are local to the station; the imported palette is shared
    // with other buildings and must never be recolored in place.
    public static class CityFireStationPaint
    {
        const string AssetPath = "Assets/Imported/FireStationPaint.asset";
        const string Prefix = "Fire station red • ";
        static readonly Color Wall = new Color(.74f, .105f, .075f);
        static readonly Color Trim = new Color(.86f, .16f, .11f);
        static readonly Color Roof = new Color(.57f, .07f, .05f);
        static readonly HashSet<int> Shell = new HashSet<int> {
            31, 32, 34, 36, 39, 41, 42, 44, 45, 46, 47, 48, 49, 61, 62, 63, 64, 795
        };

        [MenuItem("Open Territory/Pintar quartel dos bombeiros de vermelho")]
        public static void Repaint()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode.");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("Registro da cidade ausente.");
            Apply(JsonUtility.FromJson<WorldData>(registry.manifest.text));
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Apply(WorldData world)
        {
            int index = Array.FindIndex(world.places, p => p.kind == "fire");
            if (index < 0) return;
            var lods = Object.FindObjectsByType<CityObjectLod>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var building = lods.FirstOrDefault(l => l.name == "building:Reused building-" + index);
            if (!building) throw new InvalidOperationException("Quartel dos bombeiros ausente.");
            var library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(AssetPath);
            if (!library)
            {
                library = ScriptableObject.CreateInstance<CityAssetLibrary>();
                AssetDatabase.CreateAsset(library, AssetPath);
            }
            var paints = AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Material>().ToDictionary(m => m.name);
            void Paint(Renderer renderer, Color color)
            {
                var source = renderer.sharedMaterial;
                if (!source) return;
                string name = source.name.StartsWith(Prefix) ? source.name : Prefix + source.name;
                if (!paints.TryGetValue(name, out var material))
                {
                    material = new Material(source) { name = name, enableInstancing = true };
                    AssetDatabase.AddObjectToAsset(material, library);
                    paints.Add(name, material);
                }
                material.SetColor("_Color", color);
                EditorUtility.SetDirty(material);
                Undo.RecordObject(renderer, "Paint fire station red");
                renderer.sharedMaterial = material;
                EditorUtility.SetDirty(renderer);
            }
            foreach (var renderer in building.GetComponentsInChildren<MeshRenderer>(true))
            {
                string suffix = renderer.name.Substring(renderer.name.LastIndexOf('-') + 1);
                if (!int.TryParse(suffix, out int id) || !Shell.Contains(id)) continue;
                Paint(renderer, id == 61 || id == 795 ? Wall : id == 46 || id == 48 ? Roof : Trim);
            }
            var windows = lods.FirstOrDefault(l => l.name == "Janelas • " + index);
            if (windows) foreach (var renderer in windows.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "Caixilhos") Paint(renderer, Trim);
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var entry = registry ? registry.nodes.FirstOrDefault(n => n.id == world.places[index].door.id)?.transform : null;
            if (entry) foreach (var renderer in entry.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = renderer.sharedMaterial ? renderer.sharedMaterial.name : "";
                if (name.EndsWith("material-31") || name.EndsWith("material-32") || name.EndsWith("material-34")) Paint(renderer, Trim);
            }
            var garage = Object.FindFirstObjectByType<CityFireGarage>(FindObjectsInactive.Include);
            if (garage) foreach (var renderer in garage.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = renderer.sharedMaterial ? renderer.sharedMaterial.name : "";
                if (name.Contains("reboco quente")) Paint(renderer, Wall);
                else if (name.Contains("vermelho óxido")) Paint(renderer, Roof);
            }
            var foundation = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(r => r.name == "Foundation • " + index);
            if (foundation) Paint(foundation, Wall);
        }
    }
}
