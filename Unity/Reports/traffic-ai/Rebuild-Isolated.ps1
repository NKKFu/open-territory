param([switch]$ScriptsOnly,[string]$Label='final')
$ErrorActionPreference='Stop'
$projectSource=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshotProject=Join-Path $env:LOCALAPPDATA 'Temp/OpenTerritory-TrafficAI-20261108-0735'
$outputExecutable=Join-Path $projectSource 'Builds/TrafficAI-20261108-0735/BaiaDoSol.exe'
if(Get-CimInstance Win32_Process -Filter "Name='BaiaDoSol.exe'" | Where-Object {$_.ExecutablePath -eq $outputExecutable}){throw 'The validation player is still running.'}
foreach($name in @('CityTrafficPolicy.cs','CityVehicles.cs','CityVehicleSafetyProbe.cs','CityVehicleSafetyProbe.Traffic.cs')){
    Copy-Item -LiteralPath (Join-Path $projectSource ('Assets/Scripts/Gameplay/'+$name)) -Destination (Join-Path $snapshotProject ('Assets/Scripts/Gameplay/'+$name))
}
$helper=@'
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
public static class TrafficIsolatedBuild
{
    public static void Build()
    {
        var args=Environment.GetCommandLineArgs();
        string output=args[Array.IndexOf(args,"-traffic-output")+1],evidence=args[Array.IndexOf(args,"-traffic-evidence")+1];
        var options=BuildOptions.StrictMode|BuildOptions.CompressWithLz4;
        if(Array.IndexOf(args,"-traffic-scripts-only")>=0)options|=BuildOptions.BuildScriptsOnly;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{"Assets/Scenes/BaiaDoSol.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=options});
        File.WriteAllText(evidence,"result="+report.summary.result+"\nerrors="+report.summary.totalErrors+"\noptions="+options+"\nbytes="+report.summary.totalSize+"\nutc="+DateTime.UtcNow.ToString("o"));
        EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
    }
}
'@
Set-Content -LiteralPath (Join-Path $snapshotProject 'Assets/Editor/TrafficIsolatedBuild.cs') -Value $helper -Encoding utf8
$fingerprints=Get-ChildItem -LiteralPath (Join-Path $snapshotProject 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($snapshotProject.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$projectSource;snapshot=$snapshotProject;output=$outputExecutable;scriptsOnly=[bool]$ScriptsOnly;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $snapshotProject 'Assets/Scenes/BaiaDoSol.unity')).Hash;scripts=@($fingerprints)}|
    ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot ('snapshot-'+$Label+'.json')) -Encoding utf8
$arguments=@('-batchmode','-nographics','-projectPath',('"'+$snapshotProject+'"'),'-buildTarget','Win64','-executeMethod','TrafficIsolatedBuild.Build',
    '-traffic-output',('"'+$outputExecutable+'"'),'-traffic-evidence',('"'+(Join-Path $PSScriptRoot ('build-'+$Label+'.txt'))+'"'),
    '-logFile',('"'+(Join-Path $PSScriptRoot ('build-'+$Label+'.log'))+'"'),'-job-worker-count','4')
if($ScriptsOnly){$arguments+='-traffic-scripts-only'}
$buildProcess=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Unity.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Isolated '+$Label+' build started: PID '+$buildProcess.Id)
$buildProcess.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$buildProcess.ExitCode;output=$outputExecutable}|ConvertTo-Json|
    Set-Content -LiteralPath (Join-Path $PSScriptRoot ('build-'+$Label+'-exit.json'))
if($buildProcess.ExitCode -ne 0){throw 'Isolated build failed'}
