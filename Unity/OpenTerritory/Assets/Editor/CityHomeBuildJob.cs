using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit file request survives script reloads. Never starts from normal play.
    [InitializeOnLoad]
    public static class CityHomeBuildJob
    {
        const string DirectoryPath = "../Reports/interiors";
        static double next;
        static CityHomeBuildJob() { EditorApplication.update += Tick; }
        [MenuItem("Open Territory/Reconstruir cômodos e apartamentos")]
        public static void Queue()
        { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(DirectoryPath + "/rebuild.request", DateTime.UtcNow.ToString("o")); }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
            string request = DirectoryPath + "/rebuild.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            File.Delete(request);
            File.WriteAllText(DirectoryPath + "/import-started.txt", DateTime.UtcNow.ToString("o"));
            EditorApplication.LockReloadAssemblies(); AssetDatabase.DisallowAutoRefresh();
            try { CityBuilder.Import(); File.WriteAllText(DirectoryPath + "/import-complete.txt", DateTime.UtcNow.ToString("o")); }
            catch (Exception ex) { Debug.LogException(ex); File.WriteAllText(DirectoryPath + "/import-error.txt", ex.ToString()); }
            finally { AssetDatabase.AllowAutoRefresh(); EditorApplication.UnlockReloadAssemblies(); }
        }
    }
}
