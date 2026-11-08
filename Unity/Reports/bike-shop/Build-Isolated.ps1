param([switch]$ReuseSnapshot)
$ErrorActionPreference = 'Stop'
$bikeSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$bikeSnapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/BikeShop-20261108'))
$bikeOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/BikeShop-20261108/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $bikeSnapshot -Force | Out-Null
if (-not $ReuseSnapshot) {
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        & robocopy (Join-Path $bikeSource $part) (Join-Path $bikeSnapshot $part) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot copy failed: $part" }
    }
    foreach ($part in @('Artifacts','PackageCache','ShaderCache')) {
        $bikeCache = Join-Path $bikeSource ('Library/' + $part)
        if (-not (Test-Path -LiteralPath $bikeCache)) { continue }
        & robocopy $bikeCache (Join-Path $bikeSnapshot ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot cache copy failed: $part" }
    }
}
$bikeBuildSource = @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace OpenTerritory.Editor {
public static class CityBikeShopSnapshotBuild {
 [Serializable] public sealed class Record {public bool passed;public string result,executable,snapshot,completedAtUtc;public int errors,warnings;public ulong bytes;}
 public static void Build() {
  var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1],record=args[Array.IndexOf(args,"-city-build-record")+1];
  if(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/pulse.prefab")||!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/sidearm.prefab")||!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/carbine.prefab")){CityWeaponAssetBuilder.Build();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);}
  EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
  var park=UnityEngine.Object.FindFirstObjectByType<CityBikeShop>();if(!park||park.bicycles.Length!=2)throw new Exception("The frozen scene must contain the authored bicycle shop.");
  Directory.CreateDirectory(Path.GetDirectoryName(output));var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
  var result=new Record{passed=report.summary.result==BuildResult.Succeeded,result=report.summary.result.ToString(),executable=output,snapshot=Path.GetFullPath("."),completedAtUtc=DateTime.UtcNow.ToString("o"),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize};
  File.WriteAllText(record,JsonUtility.ToJson(result,true));if(!result.passed)throw new Exception("Bicycle shop build failed: "+result.result);
 }
}}
'@
Set-Content -LiteralPath (Join-Path $bikeSnapshot 'Assets/Editor/CityBikeShopSnapshotBuild.cs') -Value $bikeBuildSource -Encoding utf8
$bikeFingerprints = Get-ChildItem -LiteralPath (Join-Path $bikeSnapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($bikeSnapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$bikeSource;snapshot=$bikeSnapshot;output=$bikeOutput;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $bikeSnapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash;scripts=@($bikeFingerprints)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$bikeEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$bikeArguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$bikeSnapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityBikeShopSnapshotBuild.Build',
    '-city-output',('"'+$bikeOutput+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$bikeBuildProcess = Start-Process -FilePath $bikeEditor -ArgumentList $bikeArguments -PassThru -WindowStyle Hidden
Write-Output ('Bicycle shop isolated full build: PID '+$bikeBuildProcess.Id)
$bikeBuildProcess.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$bikeBuildProcess.ExitCode;output=$bikeOutput} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if($bikeBuildProcess.ExitCode -ne 0){throw ('Unity exited with code '+$bikeBuildProcess.ExitCode)}
Write-Output 'Bicycle shop isolated full build complete.'

