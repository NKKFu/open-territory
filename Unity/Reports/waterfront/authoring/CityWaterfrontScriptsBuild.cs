using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace OpenTerritory.Editor
{
    // Used only for the opt-in diagnostic on the already-built preview.
    public static class CityWaterfrontScriptsBuild
    {
        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();var output=args[Array.IndexOf(args,"-city-output")+1];
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},
                locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.BuildScriptsOnly });
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Diagnostic scripts build: "+report.summary.result);
        }
    }
}
