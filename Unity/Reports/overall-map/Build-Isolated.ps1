param([switch]$ReuseSnapshot, [switch]$SkipCacheCopy)
$ErrorActionPreference = 'Stop'
$mapSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$mapSnapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/OverallMap-20261108'))
$mapOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/OverallMap-20261108/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $mapSnapshot -Force | Out-Null
if (-not $ReuseSnapshot) {
    $mapSceneHash = (Get-FileHash -LiteralPath (Join-Path $mapSource 'Assets/Scenes/BaiaDoSol.unity')).Hash
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        & robocopy (Join-Path $mapSource $part) (Join-Path $mapSnapshot $part) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw ('Snapshot copy failed: ' + $part) }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $mapSource 'Assets/Scenes/BaiaDoSol.unity')).Hash -ne $mapSceneHash) {
        throw 'The saved scene changed while copying. Run the snapshot again.'
    }
    foreach ($part in @('Artifacts','PackageCache','ShaderCache')) {
        if ($SkipCacheCopy) { continue }
        $mapCache = Join-Path $mapSource ('Library/' + $part)
        if (-not (Test-Path -LiteralPath $mapCache)) { continue }
        & robocopy $mapCache (Join-Path $mapSnapshot ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw ('Snapshot cache copy failed: ' + $part) }
    }
}
$mapBuildSource = @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace OpenTerritory.Editor {
public static class CityOverallMapSnapshotBuild {
 [Serializable] public sealed class Record { public bool passed; public string result,executable,snapshot,completedAtUtc; public int errors,warnings; public ulong bytes; }
 [Serializable] public sealed class Audit {public bool passed;public string sourceHash;public int buildingFailures,paintFailures,parkingFailures,windowFailures,windows;public System.Collections.Generic.List<string> problems=new System.Collections.Generic.List<string>();}
 public static void Build() {
  var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1],record=args[Array.IndexOf(args,"-city-build-record")+1];
  EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
  var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
  var review=Resources.Load<TextAsset>("overall-map-review");
  if(!registry||!review||JsonUtility.FromJson<CityOverallMapProbe.Review>(review.text).sourceHash!=JsonUtility.FromJson<WorldData>(registry.manifest.text).sourceHash)throw new Exception("Snapshot review does not match its map.");
  var data=JsonUtility.FromJson<CityOverallMapProbe.Review>(review.text);var audit=new Audit{sourceHash=data.sourceHash};Physics.SyncTransforms();
  foreach(var p in data.buildingSamples){RaycastHit h;if(!Physics.Raycast(new Vector3(p.x,p.floor+7,p.z),Vector3.down,out h,20,1<<8)){audit.buildingFailures++;if(audit.problems.Count<12)audit.problems.Add("unsupported building "+p.place+" "+p.x+","+p.z);}}
  foreach(var p in data.paintSamples){RaycastHit h;if(!Physics.Raycast(p+Vector3.up*.25f,Vector3.down,out h,.6f,1<<8)||p.y-h.point.y<.003f||p.y-h.point.y>.04f){audit.paintFailures++;if(audit.problems.Count<24)audit.problems.Add("paint "+p+" hit="+h.point);}}
  foreach(var p in data.parkingSamples){RaycastHit h;if(!Physics.Raycast(p+Vector3.up*.4f,Vector3.down,out h,1,1<<8)||Mathf.Abs(h.point.y-p.y)>.06f){audit.parkingFailures++;if(audit.problems.Count<36)audit.problems.Add("parking "+p+" hit="+h.point);}}
  var windows=UnityEngine.Object.FindFirstObjectByType<CityWindows>();audit.windows=windows.windows.Length;foreach(var w in windows.windows)foreach(float across in new[]{-.48f,-.25f,0,.25f,.48f}){var p=w.center+w.outward*.18f+Vector3.Cross(Vector3.up,w.outward)*w.width*across;RaycastHit h;if(Physics.Raycast(new Vector3(p.x,350,p.z),Vector3.down,out h,500,1<<8)&&h.point.y>w.center.y-w.height*.5f-.06f)audit.windowFailures++;}
  audit.passed=audit.buildingFailures+audit.paintFailures+audit.parkingFailures+audit.windowFailures==0;File.WriteAllText(args[Array.IndexOf(args,"-city-editor-audit")+1],JsonUtility.ToJson(audit,true));if(!audit.passed)throw new Exception("Frozen geometry audit failed.");
  Directory.CreateDirectory(Path.GetDirectoryName(output));
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
  var result=new Record{passed=report.summary.result==BuildResult.Succeeded,result=report.summary.result.ToString(),executable=output,snapshot=Path.GetFullPath("."),completedAtUtc=DateTime.UtcNow.ToString("o"),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize};
  File.WriteAllText(record,JsonUtility.ToJson(result,true));if(!result.passed)throw new Exception("Overall map build failed: "+result.result);
 }
}}
'@
Set-Content -LiteralPath (Join-Path $mapSnapshot 'Assets/Editor/CityOverallMapSnapshotBuild.cs') -Value $mapBuildSource -Encoding utf8
$mapFingerprints = Get-ChildItem -LiteralPath (Join-Path $mapSnapshot 'Assets') -File -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($mapSnapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$mapSource;snapshot=$mapSnapshot;output=$mapOutput;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $mapSnapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash;assets=@($mapFingerprints)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$mapEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$mapArguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$mapSnapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityOverallMapSnapshotBuild.Build',
    '-city-output',('"'+$mapOutput+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),
    '-city-editor-audit',('"'+(Join-Path $PSScriptRoot 'editor-validation.json')+'"'),
    '-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$mapBuildProcess = Start-Process -FilePath $mapEditor -ArgumentList $mapArguments -PassThru -WindowStyle Hidden
Write-Output ('Overall map full snapshot build: PID ' + $mapBuildProcess.Id)
$mapBuildProcess.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$mapBuildProcess.ExitCode;output=$mapOutput} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if ($mapBuildProcess.ExitCode -ne 0) { throw ('Unity exited with code ' + $mapBuildProcess.ExitCode) }
Write-Output 'Overall map full build complete.'
