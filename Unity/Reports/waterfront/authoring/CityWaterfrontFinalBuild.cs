using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Builds the already-authored scene from an isolated, frozen copy.
    public static class CityWaterfrontFinalBuild
    {
        [Serializable] sealed class Record { public bool passed;public string result,executable,completedAtUtc;public int errors,warnings;public ulong bytes; }
        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1];
            string record=args[Array.IndexOf(args,"-city-build-record")+1];
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
            var waterfront=UnityEngine.Object.FindFirstObjectByType<CityWaterfront>();
            var terrain=UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).FirstOrDefault(f=>f.name=="s-surface:0:0-843");
            if(!waterfront || waterfront.route.Length<20 || waterfront.seats.Length<5)throw new Exception("A cena congelada não contém a orla completa.");
            if(!terrain || !terrain.sharedMesh || terrain.sharedMesh.vertexCount<100000)throw new Exception("O terreno original está ausente na cópia congelada.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
            var report=new Record{passed=result.summary.result==BuildResult.Succeeded,result=result.summary.result.ToString(),executable=output,
                completedAtUtc=DateTime.UtcNow.ToString("o"),errors=result.summary.totalErrors,warnings=result.summary.totalWarnings,bytes=result.summary.totalSize};
            File.WriteAllText(record,JsonUtility.ToJson(report,true));if(!report.passed)throw new Exception("Build da orla: "+report.result);
        }
    }
}
