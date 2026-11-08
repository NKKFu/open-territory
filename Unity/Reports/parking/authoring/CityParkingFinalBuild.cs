using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityParkingFinalBuild
    {
        [Serializable] sealed class Record { public bool passed; public string result, executable, completedAtUtc; public int errors, warnings; public ulong bytes; }
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-city-output") + 1];
            string record = args[Array.IndexOf(args, "-city-build-record") + 1];
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity", OpenSceneMode.Single);
            var lot = UnityEngine.Object.FindFirstObjectByType<CityParkingLot>();
            if (!lot || lot.bayCount != 9 || !lot.surface || lot.movedStops.Length != 6 || lot.newStops.Length != 3)
                throw new Exception("A cena congelada não contém o estacionamento completo.");
            var mesh = lot.transform.Find("Parking bay markings")?.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh || mesh.normals.Length == 0 || mesh.normals.Average(n => n.y) < .9f)
                throw new Exception("As marcações do estacionamento não estão visíveis.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" },
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport });
            var result = new Record { passed = build.summary.result == BuildResult.Succeeded, result = build.summary.result.ToString(), executable = output,
                completedAtUtc = DateTime.UtcNow.ToString("o"), errors = build.summary.totalErrors, warnings = build.summary.totalWarnings, bytes = build.summary.totalSize };
            File.WriteAllText(record, JsonUtility.ToJson(result, true));
            if (!result.passed) throw new Exception("Build do estacionamento: " + result.result);
        }
    }
}
