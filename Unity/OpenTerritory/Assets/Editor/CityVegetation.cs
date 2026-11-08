using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    // Generated at import time, never on a gameplay frame. One shared mesh per variant/LOD.
    public static class CityVegetation
    {
        public static readonly string[] Forms = { "Carvalho", "Acácia", "Álamo", "Pinheiro", "Cipreste", "Ipê", "Costeira", "Árvore jovem" };
        public static bool IsTree(VisualNode node) => node.name == "Reused donor tree";

        [MenuItem("Open Territory/Reconstruir somente as árvores")]
        public static void Rebuild()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty)
                throw new InvalidOperationException("Abra a cena salva da cidade fora do Play Mode antes de reconstruir as árvores.");
            var old = GameObject.Find("Árvores • 24 variações • LOD");
            if (!old) throw new InvalidOperationException("A floresta atual não foi encontrada.");
            var parent = old.transform.parent;
            var previousAssets = old.GetComponentsInChildren<MeshFilter>().Select(f => AssetDatabase.GetAssetPath(f.sharedMesh))
                .Where(p => p.StartsWith("Assets/Imported/TreeAssets") && p.EndsWith(".asset")).Distinct().ToArray();
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Imported/TreeAssets.asset");
            AssetDatabase.CreateAsset(library, path);
            var world = JsonUtility.FromJson<WorldData>(System.IO.File.ReadAllText("Assets/Resources/world.json"));
            var visual = JsonUtility.FromJson<VisualData>(System.IO.File.ReadAllText("Assets/Imported/visual.json"));
            library.sourceHash = world.sourceHash;
            Build(parent, visual, world, library);
            UnityEngine.Object.DestroyImmediate(old);
            foreach (string previous in previousAssets) AssetDatabase.DeleteAsset(previous);
            AssetDatabase.SaveAssets(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform parent, VisualData visual, WorldData world, UnityEngine.Object library)
        {
            var root = new GameObject("Árvores • 24 variações • LOD").transform;
            root.SetParent(parent, false);
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Folhagem e casca • cores por vértice", enableInstancing = true };
            material.SetColor("_Color", Color.white); material.SetFloat("_Glossiness", .08f); material.SetFloat("_Foliage", 1);
            AssetDatabase.AddObjectToAsset(material, library);
            var meshes = new Mesh[24, 3];
            for (int v = 0; v < 24; v++) for (int l = 0; l < 3; l++)
            {
                meshes[v, l] = CreateMesh(v, l);
                AssetDatabase.AddObjectToAsset(meshes[v, l], library);
            }
            // Match the exported trunk to its original physical footprint. Keep the existing
            // collider independent of LOD, including when the renderer is culled in the fog.
            var trunks = world.solids.Where(s => s.active && Mathf.Abs(s.w - .7f) < .001f && Mathf.Abs(s.d - .7f) < .001f)
                .GroupBy(s => Key(s.x, s.z)).ToDictionary(g => g.Key, g => g.First());
            int index = 0;
            var nature = world.natureTrees.ToDictionary(t => t.node);
            foreach (var node in visual.nodes.Where(IsTree))
            {
                if (node.instances.Length == 0) throw new InvalidOperationException("Tree has no trunk transform");
                var m = node.instances[0].matrix;
                nature.TryGetValue(node.id, out var planned);
                if (!trunks.TryGetValue(Key(m[12], m[14]), out var trunk) && planned == null) throw new InvalidOperationException("Tree has no matching physical trunk: " + node.id);
                int variant = planned != null ? planned.variant : index % 24;
                var random = new System.Random(node.id * 31);
                var tree = new GameObject(Forms[variant % 8] + " • " + index);
                tree.transform.SetParent(root, false);
                tree.transform.position = planned != null ? planned.position : new Vector3(trunk.x, trunk.bottom, trunk.z);
                if (CityCemeteryBuilder.Reserved(tree.transform.position.x, tree.transform.position.z, 2)) tree.SetActive(false);
                if (CityCarDealershipBuilder.Reserved(tree.transform.position.x, tree.transform.position.z, 3)) tree.SetActive(false);
                tree.transform.rotation = Quaternion.Euler(0, planned != null ? planned.yaw : (float)random.NextDouble() * 360, 0);
                // Trunk width stays inside its original 70 cm collision footprint.
                tree.transform.localScale = planned != null ? planned.scale : new Vector3(.94f + (float)random.NextDouble() * .12f, .88f + (float)random.NextDouble() * .26f, .94f + (float)random.NextDouble() * .12f);
                var lods = new LOD[3];
                float[] thresholds = { .12f, .045f, .003f };
                for (int l = 0; l < 3; l++)
                {
                    var go = new GameObject("LOD" + l); go.transform.SetParent(tree.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[variant, l];
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = l < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    lods[l] = new LOD(thresholds[l], new Renderer[] { renderer });
                }
                var group = tree.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
                index++;
            }
            Debug.Log("VEGETATION_IMPORT_OK trees=" + index + " variants=24 sharedMeshes=72");
        }

        static string Key(float x, float z) => Mathf.RoundToInt(x * 100) + ":" + Mathf.RoundToInt(z * 100);

        public static Mesh CreateMesh(int variant, int lod) => CityTreeModels.Create(variant, lod);
    }
}
