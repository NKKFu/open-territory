using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit request only; waits for the shared Editor without saving its scene.
    [InitializeOnLoad]
    public static class CityOutdoorDestructionBuild
    {
        const string Request = "Library/city-outdoor-destruction-build.request";
        static double next;
        static CityOutdoorDestructionBuild() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene(); if (scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty) return;
            // Parallel weapon authoring may compile the consumer before its
            // local prefabs are imported. Such a player cannot initialize the city.
            if (typeof(CityDestruction).Assembly.GetType("OpenTerritory.CityWeaponAsset") != null)
                foreach (string id in new[] { "pulse", "sidearm", "carbine" })
                    if (!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/" + id + ".prefab")) return;
            DateTime runtime = File.GetLastWriteTimeUtc(typeof(CityDestruction).Assembly.Location);
            DateTime editor = File.GetLastWriteTimeUtc(typeof(CityOutdoorDestructionBuild).Assembly.Location);
            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)) if (File.GetLastWriteTimeUtc(file) > runtime) return;
            foreach (string file in Directory.GetFiles("Assets/Editor", "*.cs", SearchOption.AllDirectories)) if (File.GetLastWriteTimeUtc(file) > editor) return;
            string output = Path.GetFullPath(File.ReadAllText(Request).Trim());
            bool ownBuild = output.StartsWith(Path.GetFullPath("Builds/OutdoorDestruction-"), StringComparison.OrdinalIgnoreCase);
            bool ownDelivery = output.StartsWith(Path.GetFullPath("../Deliveries/OutdoorDestruction-"), StringComparison.OrdinalIgnoreCase);
            if ((!ownBuild && !ownDelivery) || Path.GetFileName(output) != "BaiaDoSol.exe")
                throw new InvalidOperationException("Outdoor destruction requires its exclusive build folder.");
            File.Move(Request, Request + ".started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene.path }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport });
            Directory.CreateDirectory("../Reports/outdoor-destruction");
            File.WriteAllText("../Reports/outdoor-destruction/build.json", JsonUtility.ToJson(new BuildRecord {
                executable = output, verifiedAtUtc = DateTime.UtcNow.ToString("o"), result = report.summary.result.ToString(),
                passed = report.summary.result == BuildResult.Succeeded, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings }, true));
        }
        [Serializable] sealed class BuildRecord { public string executable, verifiedAtUtc, result; public bool passed; public int errors, warnings; }
    }
}
