using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // An explicit request queues this validation build while the shared Editor
    // is importing or building other work. It never saves or rebuilds the scene.
    [InitializeOnLoad]
    public static class CityCrouchValidationBuild
    {
        const string RequestPath = "Library/city-crouch-validation-build.request";
        static double nextAttempt;
        [Serializable] sealed class Result
        {
            public string verifiedAtUtc, executable, result, error;
            public bool passed;
            public bool scriptsOnly;
            public int errors, warnings;
        }
        static CityCrouchValidationBuild()
        {
            if (!Application.isBatchMode && !AssetDatabase.IsAssetImportWorkerProcess()) EditorApplication.update += Poll;
        }
        static void Poll()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed || BuildPipeline.isBuildingPlayer) return;
            if (EditorApplication.timeSinceStartup < nextAttempt) return;
            nextAttempt = EditorApplication.timeSinceStartup + .5;
            // A pending asset refresh can leave an idle Editor on old types.
            // Do not let the player compile a different serialized class layout.
            DateTime runtimeTime = File.GetLastWriteTimeUtc(typeof(CityPlayer).Assembly.Location);
            DateTime editorTime = File.GetLastWriteTimeUtc(typeof(CityCrouchValidationBuild).Assembly.Location);
            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(file) > runtimeTime) return;
            foreach (string file in Directory.GetFiles("Assets/Editor", "*.cs", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(file) > editorTime) return;
            var carField = typeof(CityParkourProbe).GetField("CarOnly");
            if (carField == null || !carField.IsNotSerialized) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty) return;
            string output = Path.GetFullPath("Builds/CrouchParkour-Final-20261108/BaiaDoSol.exe");
            string evidence = Path.GetFullPath("../Reports/crouch-parkour/final-build.json");
            bool scriptsOnly = File.ReadAllText(RequestPath).Trim() == "scripts";
            File.Move(RequestPath, RequestPath + ".started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var record = new Result { executable = output, scriptsOnly = scriptsOnly };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scene.path }, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport | (scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None)
                });
                record.result = report.summary.result.ToString(); record.passed = report.summary.result == BuildResult.Succeeded;
                record.errors = report.summary.totalErrors; record.warnings = report.summary.totalWarnings;
            }
            catch (Exception error) { record.error = error.ToString(); Debug.LogException(error); }
            record.verifiedAtUtc = DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(Path.GetDirectoryName(evidence));
            File.WriteAllText(evidence, JsonUtility.ToJson(record, true));
            Debug.Log("CROUCH_VALIDATION_BUILD " + record.passed + " " + output);
        }
    }
}
