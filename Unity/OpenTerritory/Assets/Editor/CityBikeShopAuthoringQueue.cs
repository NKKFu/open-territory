using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit file requests survive MCP reconnects while another build owns the Editor.
    [InitializeOnLoad]
    public static class CityBikeShopAuthoringQueue
    {
        const string Request = "../Reports/bike-shop/placement.request";
        static double next;
        static CityBikeShopAuthoringQueue() { if (!Application.isBatchMode) EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty) return;
            string command = File.ReadAllText(Request).Trim();
            if (command != "position-bicycles" && command != "refresh-scenery") return;
            try
            {
                if (command == "refresh-scenery")
                {
                    CityBikeShopBuilder.RefreshScenery();
                    File.WriteAllText("../Reports/bike-shop/scenery-complete.json", "{\"passed\":true,\"completedAtUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
                    File.Delete(Request); return;
                }
                var shop = UnityEngine.Object.FindFirstObjectByType<CityBikeShop>();
                var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
                if (!shop || !registry) throw new InvalidOperationException("Authored bicycle shop missing.");
                for (int i = 0; i < shop.bicycles.Length; i++)
                {
                    var hall = shop.halls[i]; var data = shop.bicycles[i];
                    var p = hall.TransformPoint(new Vector3(7, .08f, 12));
                    if (Physics.Raycast(p + Vector3.up * 10, Vector3.down, out var hit, 20, 1 << CityContext.GroundLayer)) p.y = hit.point.y + .08f;
                    data.position = p; data.yaw = (hall.eulerAngles.y + 90) * Mathf.Deg2Rad;
                    registry.nodes.First(n => n.id == data.id).transform.SetPositionAndRotation(p, Quaternion.Euler(0, data.yaw * Mathf.Rad2Deg, 0));
                }
                EditorUtility.SetDirty(shop); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save bicycle placement.");
                File.WriteAllText("../Reports/bike-shop/placement-complete.json", "{\"passed\":true,\"completedAtUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
                File.Delete(Request);
            }
            catch (Exception error) { File.WriteAllText("../Reports/bike-shop/placement-error.txt", error.ToString()); File.Delete(Request); Debug.LogException(error); }
        }
    }
}
