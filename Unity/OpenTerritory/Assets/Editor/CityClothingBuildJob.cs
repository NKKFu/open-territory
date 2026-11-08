using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit file request fallback while the MCP transport reconnects.
    [InitializeOnLoad]
    public static class CityClothingBuildJob
    {
        const string Reports = "../Reports/clothing-shop";
        static double nextCheck;
        static CityClothingBuildJob() { EditorApplication.update += CheckRequest; }
        static void CheckRequest()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return; nextCheck = EditorApplication.timeSinceStartup + 1;
            string request = Reports + "/apply.request";
            if (!File.Exists(request) || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene(); if (scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            Directory.CreateDirectory(Reports);
            File.Move(request, Reports + "/apply-started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            try { CityClothingShopBuilder.Rebuild(); File.WriteAllText(Reports + "/apply-complete.txt", DateTime.UtcNow.ToString("o")); }
            catch (Exception e) { File.WriteAllText(Reports + "/apply-error.txt", e.ToString()); Debug.LogException(e); }
        }
        [Serializable] sealed class BuildRecord
        { public bool passed; public string result, executable, completedAtUtc; public int errors, warnings; public ulong bytes; }
        public static void BuildSnapshot()
        {
            var args = Environment.GetCommandLineArgs(); string output = args[Array.IndexOf(args, "-city-output") + 1], record = args[Array.IndexOf(args, "-city-build-record") + 1];
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity", OpenSceneMode.Single);
            if (!UnityEngine.Object.FindFirstObjectByType<CityClothingShop>()) throw new InvalidOperationException("The frozen scene does not contain Tideline.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport });
            var result = new BuildRecord { passed = report.summary.result == BuildResult.Succeeded, result = report.summary.result.ToString(), executable = output,
                completedAtUtc = DateTime.UtcNow.ToString("o"), errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, bytes = report.summary.totalSize };
            File.WriteAllText(record, JsonUtility.ToJson(result, true)); if (!result.passed) throw new Exception("Clothing store build failed: " + result.result);
        }
    }
}
