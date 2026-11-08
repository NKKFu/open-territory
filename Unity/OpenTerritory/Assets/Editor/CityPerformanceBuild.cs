using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Build-only defines keep the historical algorithms out of the delivered player.
    // Both comparison players use the current source and the same frozen content.
    public static class CityPerformanceBuild
    {
        [Serializable] sealed class Request { public bool reference; }
        [Serializable] sealed class Result { public bool passed, reference; public string output, completedUtc; public int errors; }
        [MenuItem("Open Territory/QA/Compilar comparação de desempenho")]
        public static void BuildComparison()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de compilar.");
            var request = JsonUtility.FromJson<Request>(File.ReadAllText("../Reports/performance/build-request.json"));
            string output = request.reference ? "Builds/PerformanceBefore/BaiaDoSol.exe" : "Builds/PerformanceAfter/BaiaDoSol.exe";
            if (!File.Exists(output)) throw new InvalidOperationException("Prepare uma cópia do player completo antes do build de scripts.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.BuildScriptsOnly | BuildOptions.StrictMode | BuildOptions.CompressWithLz4,
                extraScriptingDefines = request.reference ? new[] { "CITY_PERFORMANCE_REFERENCE" } : Array.Empty<string>()
            });
            var result = new Result { passed = report.summary.result == BuildResult.Succeeded, reference = request.reference,
                output = output, completedUtc = DateTime.UtcNow.ToString("o"), errors = (int)report.summary.totalErrors };
            File.WriteAllText("../Reports/performance/build-result.json", JsonUtility.ToJson(result, true));
            Debug.Log("PERFORMANCE_BUILD " + result.passed + " reference=" + request.reference);
            if (!result.passed) throw new InvalidOperationException("Falha ao compilar a variante de desempenho.");
        }
    }
}
