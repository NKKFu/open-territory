param([switch]$ReuseSnapshot)
$ErrorActionPreference = 'Stop'
$parkSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$parkSnapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/SunmeadowPark-20261108'))
$parkOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/SunmeadowPark-20261108-Verified/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $parkSnapshot -Force | Out-Null
if (-not $ReuseSnapshot) {
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        & robocopy (Join-Path $parkSource $part) (Join-Path $parkSnapshot $part) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot copy failed: $part" }
    }
    foreach ($part in @('Artifacts','PackageCache','ShaderCache')) {
        $parkCache = Join-Path $parkSource ('Library/' + $part)
        if (-not (Test-Path -LiteralPath $parkCache)) { continue }
        & robocopy $parkCache (Join-Path $parkSnapshot ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot cache copy failed: $part" }
    }
}
$parkBuildSource = @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace OpenTerritory.Editor {
public static class CityParkSnapshotBuild {
 [Serializable] public sealed class Record {public bool passed;public string result,executable,snapshot,completedAtUtc;public int errors,warnings;public ulong bytes;}
 public static void Build() {
  var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1],record=args[Array.IndexOf(args,"-city-build-record")+1];
  if(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/pulse.prefab")||!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/sidearm.prefab")||!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/carbine.prefab")){CityWeaponAssetBuilder.Build();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);}
  EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
  var park=UnityEngine.Object.FindFirstObjectByType<CityPark>();if(!park||park.seats.Length!=10)throw new Exception("The frozen scene must contain the authored park.");
  Directory.CreateDirectory(Path.GetDirectoryName(output));var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
  var result=new Record{passed=report.summary.result==BuildResult.Succeeded,result=report.summary.result.ToString(),executable=output,snapshot=Path.GetFullPath("."),completedAtUtc=DateTime.UtcNow.ToString("o"),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize};
  File.WriteAllText(record,JsonUtility.ToJson(result,true));if(!result.passed)throw new Exception("Park build failed: "+result.result);
 }
}}
'@
Set-Content -LiteralPath (Join-Path $parkSnapshot 'Assets/Editor/CityParkSnapshotBuild.cs') -Value $parkBuildSource -Encoding utf8
$parkFingerprints = Get-ChildItem -LiteralPath (Join-Path $parkSnapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($parkSnapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$parkSource;snapshot=$parkSnapshot;output=$parkOutput;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $parkSnapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash;scripts=@($parkFingerprints)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$parkEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$parkArguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$parkSnapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityParkSnapshotBuild.Build',
    '-city-output',('"'+$parkOutput+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$parkBuildProcess = Start-Process -FilePath $parkEditor -ArgumentList $parkArguments -PassThru -WindowStyle Hidden
Write-Output ('Park isolated full build: PID '+$parkBuildProcess.Id)
$parkBuildProcess.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$parkBuildProcess.ExitCode;output=$parkOutput} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if($parkBuildProcess.ExitCode -ne 0){throw ('Unity exited with code '+$parkBuildProcess.ExitCode)}
Write-Output 'Park isolated full build complete.'
