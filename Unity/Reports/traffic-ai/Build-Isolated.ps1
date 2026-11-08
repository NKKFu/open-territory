$ErrorActionPreference = 'Stop'
$projectSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshotProject = Join-Path $env:LOCALAPPDATA 'Temp/OpenTerritory-TrafficAI-20261108-0735'
$outputExecutable = Join-Path $projectSource 'Builds/TrafficAI-20261108-0735/BaiaDoSol.exe'
New-Item -ItemType Directory -Path $snapshotProject -Force | Out-Null
foreach ($part in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectSource $part) (Join-Path $snapshotProject $part) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Copy failed: $part ($LASTEXITCODE)" }
}
foreach ($part in @('Artifacts', 'PackageCache', 'ShaderCache')) {
    $cacheSource = Join-Path $projectSource ('Library/' + $part)
    if (-not (Test-Path -LiteralPath $cacheSource)) { continue }
    & robocopy $cacheSource (Join-Path $snapshotProject ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Cache copy failed: $part ($LASTEXITCODE)" }
}
$helper = @'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
public static class TrafficIsolatedBuild
{
    public static void Build()
    {
        var args=Environment.GetCommandLineArgs();
        string output=args[Array.IndexOf(args,"-traffic-output")+1];
        string evidence=args[Array.IndexOf(args,"-traffic-evidence")+1];
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.CompressWithLz4
        });
        File.WriteAllText(evidence,"result="+report.summary.result+"\nerrors="+report.summary.totalErrors+"\nbytes="+report.summary.totalSize+"\nutc="+DateTime.UtcNow.ToString("o"));
        EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
    }
}
'@
Set-Content -LiteralPath (Join-Path $snapshotProject 'Assets/Editor/TrafficIsolatedBuild.cs') -Value $helper -Encoding utf8
$fingerprints = Get-ChildItem -LiteralPath (Join-Path $snapshotProject 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($snapshotProject.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
[ordered]@{ createdAtUtc=[DateTime]::UtcNow.ToString('o'); source=$projectSource; snapshot=$snapshotProject; output=$outputExecutable;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $snapshotProject 'Assets/Scenes/BaiaDoSol.unity')).Hash; scripts=@($fingerprints) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$unityEditor='C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$arguments=@('-batchmode','-nographics','-projectPath',('"'+$snapshotProject+'"'),'-buildTarget','Win64',
    '-executeMethod','TrafficIsolatedBuild.Build','-traffic-output',('"'+$outputExecutable+'"'),
    '-traffic-evidence',('"'+(Join-Path $PSScriptRoot 'build-result.txt')+'"'),
    '-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$buildProcess=Start-Process -FilePath $unityEditor -ArgumentList $arguments -PassThru -WindowStyle Hidden
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-id.txt') -Value $buildProcess.Id
Write-Output ('Isolated full build started: PID '+$buildProcess.Id)
$buildProcess.WaitForExit()
[ordered]@{ completedAtUtc=[DateTime]::UtcNow.ToString('o'); processExitCode=$buildProcess.ExitCode; output=$outputExecutable } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if($buildProcess.ExitCode -ne 0){throw ('Unity exited with code '+$buildProcess.ExitCode)}
Write-Output 'Isolated full build completed.'
