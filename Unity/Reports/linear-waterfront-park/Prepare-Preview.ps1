$ErrorActionPreference='Stop'
$snapshot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/LinearWaterfrontPark-Preview'))
$out=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/LinearWaterfrontPark-Preview/BaiaDoSol.exe'))
$authoring=Join-Path $PSScriptRoot 'authoring'
Copy-Item -LiteralPath (Join-Path $authoring 'CityLinearPark.cs') -Destination (Join-Path $snapshot 'Assets/Scripts/World/CityLinearPark.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityLinearParkProbe.cs') -Destination (Join-Path $snapshot 'Assets/Scripts/World/CityLinearParkProbe.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityLinearParkBuilder.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityLinearParkBuilder.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityLinearParkSnapshotBuild.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityLinearParkSnapshotBuild.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'LinearParkYards.json') -Destination (Join-Path $snapshot 'Assets/Editor/LinearParkYards.json') -Force
$utf8=[Text.UTF8Encoding]::new($false)
$context=Join-Path $snapshot 'Assets/Scripts/Core/CityContext.cs';$value=[IO.File]::ReadAllText($context)
if(-not $value.Contains('CityLinearPark.Bind(this);')){
 if(-not $value.Contains('CityWaterfront.Bind(this);')){throw 'CityContext binding anchor absent'}
 $value=$value.Replace('CityWaterfront.Bind(this);',"CityWaterfront.Bind(this);`n  CityLinearPark.Bind(this);")
 [IO.File]::WriteAllText($context,$value,$utf8)
}
$cover=Join-Path $snapshot 'Assets/Editor/CityGroundCoverBuilder.cs';$value=[IO.File]::ReadAllText($cover)
if(-not $value.Contains('CityLinearParkBuilder.Reserved(x, z, radius)')){
 if(-not $value.Contains('if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;')){throw 'Ground cover anchor absent'}
 $value=$value.Replace('if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;',"if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;`n            if (CityLinearParkBuilder.Reserved(x, z, radius)) return false;")
 [IO.File]::WriteAllText($cover,$value,$utf8)
}
$builder=Join-Path $snapshot 'Assets/Editor/CityBuilder.cs';$value=[IO.File]::ReadAllText($builder)
if(-not $value.Contains('CityLinearParkBuilder.Build(root.transform,world);')){
 if(-not $value.Contains('CityWaterfrontBuilder.Build(root.transform,world);')){throw 'City import anchor absent'}
 $value=$value.Replace('CityWaterfrontBuilder.Build(root.transform,world);',"CityWaterfrontBuilder.Build(root.transform,world);`n  CityLinearParkBuilder.Build(root.transform,world);")
 $value=$value.Replace('CityWaterfrontBuilder.Build(root,world);','CityWaterfrontBuilder.Build(root,world);CityLinearParkBuilder.Build(root,world);')
 [IO.File]::WriteAllText($builder,$value,$utf8)
}
$editor='C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$snapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityLinearParkSnapshotBuild.Build',
 '-city-output',('"'+$out+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'preview-build.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'preview-build.log')+'"'),'-job-worker-count','4')
$process=Start-Process -FilePath $editor -ArgumentList $arguments -PassThru -WindowStyle Hidden
Write-Output ('Isolated preview Unity PID: '+$process.Id)
$process.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$process.ExitCode;executable=$out} |
 ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'preview-process.json') -Encoding utf8
if($process.ExitCode -ne 0){throw ('Unity preview failed with exit code '+$process.ExitCode)}
