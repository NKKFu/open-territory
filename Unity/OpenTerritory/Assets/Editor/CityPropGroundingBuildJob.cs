using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit file request fallback for times when the shared Editor's MCP
    // transport is unavailable. Never runs without this task's request file.
    [InitializeOnLoad]
    public static class CityPropGroundingBuildJob
    {
        const string DirectoryPath = "../Reports/prop-grounding";
        static double next;
        [Serializable] sealed class Record { public bool passed; public string executable, verifiedAtUtc, result, error; public int errors, warnings; }
        static CityPropGroundingBuildJob() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            string request = DirectoryPath + "/build.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty) return;
            string output = Path.GetFullPath(File.ReadAllText(request).Trim());
            string allowed = Path.GetFullPath("Builds/PropsGrounded-");
            if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(output) != "BaiaDoSol.exe")
                throw new InvalidOperationException("A construção de props deve usar sua pasta exclusiva.");
            File.Move(request, DirectoryPath + "/build-started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            var record = new Record { executable = output };
            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene.path },
                    locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                record.result = result.summary.result.ToString(); record.passed = result.summary.result == BuildResult.Succeeded;
                record.errors = result.summary.totalErrors; record.warnings = result.summary.totalWarnings;
            }
            catch (Exception error) { record.error = error.ToString(); Debug.LogException(error); }
            record.verifiedAtUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(DirectoryPath + "/build-complete.json", JsonUtility.ToJson(record, true));
        }
    }
}
