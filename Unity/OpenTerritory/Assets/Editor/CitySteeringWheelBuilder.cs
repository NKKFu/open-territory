using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CitySteeringWheelBuilder
    {
        [MenuItem("Open Territory/Ajustar volantes dos carros")]
        public static void BakeActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode para ajustar os volantes.");
            if (scene.isDirty)
                throw new InvalidOperationException("Salve as alterações da cena antes de ajustar os volantes.");
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("Registro da cidade ausente.");
            Directory.CreateDirectory("../Reports/steering/backups");
            EditorSceneManager.SaveScene(scene, "../Reports/steering/backups/Before-steering-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            if (File.Exists("Assets/Imported/CarDealership.asset")) File.Copy("Assets/Imported/CarDealership.asset",
                "../Reports/steering/backups/Before-column-dealership-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".asset");
            Fit(JsonUtility.FromJson<WorldData>(registry.manifest.text), registry);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Fit(WorldData world, WorldRegistry registry)
        {
            EnsureColumnMesh();
            var nodes = registry.nodes.Where(n => n.transform).ToDictionary(n => n.id, n => n.transform);
            foreach (var vehicle in world.vehicles)
            {
                if (vehicle.kind != "car" || !nodes.TryGetValue(vehicle.steeringWheel, out var wheel)) continue;
                Undo.RecordObject(wheel, "Ajustar volante");
                CitySteeringWheel.Fit(vehicle, wheel);
            }
            Undo.FlushUndoRecordObjects();
            CityCarDealershipBuilder.RefreshDisplayCars(world, registry);
        }

        public static void EnsureColumnMesh()
        {
            const string path = "Assets/Resources/Vehicles/SteeringColumn.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) return;
            Directory.CreateDirectory("Assets/Resources/Vehicles");
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            { int at = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2); }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
                var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); var q = new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0);
                Triangle(p, q, q + Vector3.forward); Triangle(p, q + Vector3.forward, p + Vector3.forward);
                Triangle(Vector3.zero, q, p); Triangle(Vector3.forward, p + Vector3.forward, q + Vector3.forward);
            }
            var mesh = new Mesh { name = "Shared eight-sided steering column", vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
        }
    }
}
