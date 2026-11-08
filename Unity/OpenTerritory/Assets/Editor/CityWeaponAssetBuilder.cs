using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenTerritory.Editor
{
    // Builds only weapon assets in a preview scene. The city is never rebuilt.
    [InitializeOnLoad]
    public static class CityWeaponAssetBuilder
    {
        const string ImportRequest = "../Reports/weapon-assets/import.request";
        static double nextRequestCheck;
        static CityWeaponAssetBuilder() { EditorApplication.update += ImportWhenIdle; }
        static void ImportWhenIdle()
        {
            if (EditorApplication.timeSinceStartup < nextRequestCheck) return;
            nextRequestCheck = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(ImportRequest) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            File.Move(ImportRequest, ImportRequest + ".started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            try
            {
                Build();
                File.WriteAllText("../Reports/weapon-assets/import-result.json", "{\"passed\":true,\"completedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
            }
            catch (Exception error)
            {
                File.WriteAllText("../Reports/weapon-assets/import-result.json", JsonUtility.ToJson(new ImportFailure { error = error.ToString() }, true));
                Debug.LogException(error);
            }
        }
        [Serializable] sealed class ImportFailure { public bool passed = false; public string error; }
        [Serializable] sealed class Part
        { public string name, material; public float[] vertices, normals, uv; public int[] triangles; }
        [Serializable] sealed class Finish { public string name; public float[] rgb; }
        [Serializable] sealed class Model
        {
            public string id, source, author, sourceUrl, sourceSha256, license;
            public int sourceTriangles;
            public Part[] parts; public Finish[] colors;
            public float[] muzzle, ejection, rightGrip, supportGrip;
            public bool textured;
        }
        const string Art = "Assets/Art/Weapons";
        const string Prefabs = "Assets/Resources/Weapons";

        [MenuItem("Open Territory/Import free weapon models")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Wait for the Editor to be idle before importing weapon assets.");
            Directory.CreateDirectory(Art + "/Meshes"); Directory.CreateDirectory(Art + "/Materials"); Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string id in new[] { "sidearm", "carbine", "pulse" }) BuildModel(id, scene);
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Debug.Log("WEAPON_ASSETS_READY: Quaternius Pistol_4, AssaultRifle2_2 and Kenney blaster-b (CC0).");
        }
        static Vector3 V(float[] values) => new Vector3(values[0], values[1], values[2]);
        static Transform Socket(Transform root, string name, Vector3 position)
        {
            var node = new GameObject(name).transform; node.SetParent(root, false); node.localPosition = position;
            node.gameObject.layer = CityContext.PlayerLayer; return node;
        }
        static void BuildModel(string id, Scene scene)
        {
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(Art + "/Prepared/" + id + ".json"));
            var root = new GameObject(id + " · " + model.author); SceneManager.MoveGameObjectToScene(root, scene);
            root.layer = CityContext.PlayerLayer;
            try
            {
                var asset = root.AddComponent<CityWeaponAsset>();
                asset.SourceModel = model.source; asset.Author = model.author; asset.SourceUrl = model.sourceUrl;
                asset.License = model.license; asset.SourceSha256 = model.sourceSha256; asset.SourceTriangles = model.sourceTriangles;
                var body = Socket(root.transform, "Body", Vector3.zero);
                asset.Action = Socket(root.transform, "Action", Vector3.zero);
                asset.Magazine = Socket(root.transform, "Magazine", Vector3.zero);
                asset.Muzzle = Socket(root.transform, "Muzzle", V(model.muzzle));
                asset.Ejection = Socket(root.transform, "Ejection", V(model.ejection));
                asset.RightGrip = Socket(root.transform, "RightGrip", V(model.rightGrip));
                asset.SupportGrip = Socket(root.transform, "SupportGrip", V(model.supportGrip));
                foreach (var part in model.parts)
                {
                    string label = id + "-" + part.name + "-" + part.material;
                    string path = Art + "/Meshes/" + label + ".asset";
                    var vertices = new Vector3[part.vertices.Length / 3]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = new Vector3(part.vertices[i*3], part.vertices[i*3+1], part.vertices[i*3+2]);
                        normals[i] = new Vector3(part.normals[i*3], part.normals[i*3+1], part.normals[i*3+2]);
                        uv[i] = new Vector2(part.uv[i*2], part.uv[i*2+1]);
                    }
                    var mesh = new Mesh { name = label, vertices = vertices, normals = normals, uv = uv, triangles = part.triangles };
                    mesh.RecalculateBounds();
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
                    else AssetDatabase.CreateAsset(mesh, path);
                    var parent = part.name == "Action" ? asset.Action : part.name == "Magazine" ? asset.Magazine : body;
                    var node = Socket(parent, part.material, Vector3.zero);
                    node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = node.gameObject.AddComponent<MeshRenderer>();
                    bool emitter = model.id == "pulse" && part.name == "Action";
                    renderer.sharedMaterial = Material(model, part.material, emitter);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    if (emitter) asset.EmissionRenderer = renderer;
                }
                // Rotate the removable piece around its own centre on reload.
                var magazineBounds = new Bounds(); bool haveBounds = false;
                foreach (var filter in asset.Magazine.GetComponentsInChildren<MeshFilter>())
                {
                    if (!haveBounds) { magazineBounds = filter.sharedMesh.bounds; haveBounds = true; }
                    else magazineBounds.Encapsulate(filter.sharedMesh.bounds);
                }
                asset.Magazine.localPosition = magazineBounds.center;
                foreach (Transform child in asset.Magazine) child.localPosition = -magazineBounds.center;
                bool success; PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + id + ".prefab", out success);
                if (!success) throw new InvalidOperationException("Could not save weapon prefab: " + id);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static Material Material(Model model, string name, bool emitter)
        {
            string path = Art + "/Materials/" + model.id + "-" + name + (emitter ? "-emitter" : "") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.name = model.author + " · " + model.id + " · " + name;
            material.enableInstancing = true;
            material.color = Color.white;
            if (model.textured && name == "colormap")
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/kenney-colormap.png");
            else
            {
                material.mainTexture = null;
                foreach (var finish in model.colors) if (finish.name == name)
                    material.color = new Color(finish.rgb[0], finish.rgb[1], finish.rgb[2]).gamma;
            }
            material.SetFloat("_Metallic", model.textured ? .12f : name == "Black" || name == "ReloadInsert" ? .03f : .48f);
            material.SetFloat("_Glossiness", .28f);
            if (emitter)
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", new Color(.06f, .75f, .65f) * 1.3f);
                material.EnableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material); return material;
        }
    }
}
