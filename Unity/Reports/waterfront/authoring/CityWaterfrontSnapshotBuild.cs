using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityWaterfrontSnapshotBuild
    {
        [Serializable] sealed class Record { public bool passed;public string result,executable,completedAtUtc;public int errors,warnings; }
        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1];
            string record=args[Array.IndexOf(args,"-city-build-record")+1];
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
            CityWaterfrontBuilder.Rebuild();
            var waterfront=UnityEngine.Object.FindFirstObjectByType<CityWaterfront>();
            if(!waterfront || waterfront.route.Length<20)throw new Exception("A cena congelada não contém a orla.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
            var report=new Record{passed=result.summary.result==BuildResult.Succeeded,result=result.summary.result.ToString(),executable=output,
                completedAtUtc=DateTime.UtcNow.ToString("o"),errors=result.summary.totalErrors,warnings=result.summary.totalWarnings};
            File.WriteAllText(record,JsonUtility.ToJson(report,true));if(!report.passed)throw new Exception("Build da orla: "+report.result);
        }
    }
}
