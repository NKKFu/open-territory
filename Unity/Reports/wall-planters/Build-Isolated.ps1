param([switch]$ReuseSnapshot)
$ErrorActionPreference = 'Stop'
$wallPlanterSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$wallPlanterSnapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/WallPlanters-20261108'))
$wallPlanterOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/WallPlanters-20261108/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $wallPlanterSnapshot -Force | Out-Null
if (-not $ReuseSnapshot) {
foreach ($wallPlanterPart in @('Assets','Packages','ProjectSettings')) {
    & robocopy (Join-Path $wallPlanterSource $wallPlanterPart) (Join-Path $wallPlanterSnapshot $wallPlanterPart) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw ('Snapshot copy failed: '+$wallPlanterPart) }
}
foreach ($wallPlanterPart in @('Artifacts','PackageCache','ShaderCache')) {
    $wallPlanterCache = Join-Path $wallPlanterSource ('Library/'+$wallPlanterPart)
    if (-not (Test-Path -LiteralPath $wallPlanterCache)) { continue }
    & robocopy $wallPlanterCache (Join-Path $wallPlanterSnapshot ('Library/'+$wallPlanterPart)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw ('Snapshot cache copy failed: '+$wallPlanterPart) }
}
}
$wallPlanterBuildCode = @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace OpenTerritory.Editor {
public static class CityWallPlanterSnapshotBuild {
 [Serializable] public sealed class Record { public bool passed; public string result,executable,snapshot,completedAtUtc; public int errors,warnings; public ulong bytes; }
 public static void Build() {
  var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-city-output")+1],record=args[Array.IndexOf(args,"-city-build-record")+1];
  EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
  if(!UnityEngine.Object.FindFirstObjectByType<WorldRegistry>()||!UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>())throw new Exception("Frozen city scene is missing its authored registry/catalog.");
  Directory.CreateDirectory(Path.GetDirectoryName(output));
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
  var result=new Record{passed=report.summary.result==BuildResult.Succeeded,result=report.summary.result.ToString(),executable=output,snapshot=Path.GetFullPath("."),completedAtUtc=DateTime.UtcNow.ToString("o"),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize};
  File.WriteAllText(record,JsonUtility.ToJson(result,true));if(!result.passed)throw new Exception("Frozen full build failed: "+result.result);
 }
}}
'@
Set-Content -LiteralPath (Join-Path $wallPlanterSnapshot 'Assets/Editor/CityWallPlanterSnapshotBuild.cs') -Value $wallPlanterBuildCode -Encoding utf8
$wallPlanterHashes=Get-ChildItem -LiteralPath (Join-Path $wallPlanterSnapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {[ordered]@{path=$_.FullName.Substring($wallPlanterSnapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$wallPlanterSnapshot;originalSource=$wallPlanterSource;sceneSha256=(Get-FileHash -LiteralPath (Join-Path $wallPlanterSnapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash;scripts=@($wallPlanterHashes)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-input.json') -Encoding utf8
$wallPlanterEditor='C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Unity.exe'
$wallPlanterArgs=@('-batchmode','-nographics','-quit','-projectPath',('"'+$wallPlanterSnapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityWallPlanterSnapshotBuild.Build','-city-output',('"'+$wallPlanterOutput+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$wallPlanterProcess=Start-Process -FilePath $wallPlanterEditor -ArgumentList $wallPlanterArgs -WindowStyle Hidden -PassThru
$null=$wallPlanterProcess.Handle
Write-Output ('Frozen full build started: PID '+$wallPlanterProcess.Id)
$wallPlanterProcess.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');exitCode=$wallPlanterProcess.ExitCode;executable=$wallPlanterOutput;snapshot=$wallPlanterSnapshot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if($wallPlanterProcess.ExitCode -ne 0){throw ('Frozen Unity build exited with '+$wallPlanterProcess.ExitCode)}
Write-Output 'Frozen full build complete.'
