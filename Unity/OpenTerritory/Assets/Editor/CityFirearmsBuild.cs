using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // A durable request survives other builds and domain reloads. It never
    // saves a dirty scene or changes project/player graphics preferences.
    [InitializeOnLoad]
    public static class CityFirearmsBuild
    {
        [Serializable] sealed class Request { public string output; }
        [Serializable] sealed class Result { public bool passed; public string output, completedUtc, assemblySha256; public int errors; }
        const string RequestPath="../Reports/firearms/build-request.json";
        const string ResultPath="../Reports/firearms/build-result.json";
        static double next;
        static bool running;
        static CityFirearmsBuild() { EditorApplication.update+=Tick; }
        static void Tick()
        {
            if(running||EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+2;
            if(!File.Exists(RequestPath)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer)return;
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.path!="Assets/Scenes/BaiaDoSol.unity"||scene.isDirty)return;
            var assembly=typeof(CityCombat).Assembly;
            DateTime newest=File.GetLastWriteTimeUtc("Assets/Scripts/Life/CityFirearmInjuryProbe.cs");
            foreach(var path in new[]{"Assets/Scripts/Life/CityCombat.cs","Assets/Scripts/Life/CityCombat.Firearms.cs","Assets/Scripts/Life/CityWeaponVisual.cs","Assets/Scripts/Life/CityWeaponHands.cs","Assets/Scripts/Life/CityGunEffects.cs","Assets/Scripts/World/CityAudio.cs"})
                if(File.GetLastWriteTimeUtc(path)>newest)newest=File.GetLastWriteTimeUtc(path);
            if(File.GetLastWriteTimeUtc(assembly.Location)<newest||typeof(CityCombat).GetProperty("PlayerBulletHits")==null||assembly.GetType("OpenTerritory.CityFirearmInjuryProbe")==null)
            {
                next=EditorApplication.timeSinceStartup+20;
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                CompilationPipeline.RequestScriptCompilation();return;
            }
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            if(request==null||string.IsNullOrWhiteSpace(request.output))return;
            running=true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.output));
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path},locationPathName=request.output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.CompressWithLz4|BuildOptions.StrictMode});
                var result=new Result{passed=report.summary.result==BuildResult.Succeeded,output=request.output,completedUtc=DateTime.UtcNow.ToString("o"),errors=(int)report.summary.totalErrors};
                string playerAssembly=Path.Combine(Path.GetDirectoryName(request.output),"BaiaDoSol_Data/Managed/Assembly-CSharp.dll");
                if(File.Exists(playerAssembly))using(var sha=System.Security.Cryptography.SHA256.Create())using(var stream=File.OpenRead(playerAssembly))result.assemblySha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                File.WriteAllText(ResultPath,JsonUtility.ToJson(result,true));
                File.Move(RequestPath,RequestPath+".completed-"+DateTime.UtcNow.ToString("HHmmss"));
                Debug.Log("FIREARMS_BUILD "+result.passed+" "+result.output);
            }
            catch(Exception exception){Debug.LogException(exception);File.WriteAllText(ResultPath,JsonUtility.ToJson(new Result{passed=false,output=request.output,completedUtc=DateTime.UtcNow.ToString("o"),errors=1},true));File.Move(RequestPath,RequestPath+".failed-"+DateTime.UtcNow.ToString("HHmmss"));}
            finally{running=false;}
        }
    }
}
