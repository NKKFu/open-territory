using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityLinearParkSnapshotBuild
    {
        [Serializable] sealed class Record { public bool passed;public string result,executable,completedAtUtc;public int errors,warnings; }
        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1];
            string record=args[Array.IndexOf(args,"-city-build-record")+1];
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
            CityLinearParkBuilder.Rebuild();
            var park=UnityEngine.Object.FindFirstObjectByType<CityLinearPark>();
            if(!park || park.paths.Length<8)throw new Exception("A cópia não contém os caminhos do parque.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var build=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
            var report=new Record{passed=build.summary.result==BuildResult.Succeeded,result=build.summary.result.ToString(),executable=output,
                completedAtUtc=DateTime.UtcNow.ToString("o"),errors=build.summary.totalErrors,warnings=build.summary.totalWarnings};
            File.WriteAllText(record,JsonUtility.ToJson(report,true));if(!report.passed)throw new Exception("Build do parque: "+report.result);
        }
    }
}
