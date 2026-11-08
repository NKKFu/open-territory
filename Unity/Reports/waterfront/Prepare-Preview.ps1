$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/Bayfront-Preview-20261108'))
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/Bayfront-Preview-20261108/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
function Get-SourceManifest {
    $result=@{}
    foreach($part in @('Assets','Packages','ProjectSettings')) {
        $base=Join-Path $source $part
        foreach($item in Get-ChildItem -LiteralPath $base -File -Recurse) {
            $relative=$item.FullName.Substring($source.Length+1)
            if($relative -like 'Assets\Scenes\Backups\*') { continue }
            $result[$relative]=@($item.Length,$item.LastWriteTimeUtc.Ticks)
        }
    }
    return $result
}
function Test-Manifest($first,$second) {
    if($first.Count -ne $second.Count) { return $false }
    foreach($key in $first.Keys) {
        if(-not $second.ContainsKey($key) -or $first[$key][0] -ne $second[$key][0] -or $first[$key][1] -ne $second[$key][1]) { return $false }
    }
    return $true
}
$coherent=$false
for($attempt=1;$attempt -le 5 -and -not $coherent;$attempt++) {
    $before=Get-SourceManifest
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        $robocopyArgs=@((Join-Path $source $part),(Join-Path $snapshot $part),'/E','/COPY:DAT','/DCOPY:DAT','/R:2','/W:1','/NFL','/NDL','/NJH','/NJS','/NP')
        if($part -eq 'Assets') { $robocopyArgs+=@('/XD',(Join-Path $source 'Assets/Scenes/Backups')) }
        & robocopy @robocopyArgs | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot copy failed: $part ($LASTEXITCODE)" }
    }
    $after=Get-SourceManifest
    Start-Sleep -Seconds 3
    $quiet=Get-SourceManifest
    $coherent=(Test-Manifest $before $after) -and (Test-Manifest $after $quiet)
    if($coherent) {
        foreach($key in $quiet.Keys) {
            $copy=Join-Path $snapshot $key
            if(-not (Test-Path -LiteralPath $copy)) { $coherent=$false;break }
            $item=Get-Item -LiteralPath $copy
            if($item.Length -ne $quiet[$key][0] -or $item.LastWriteTimeUtc.Ticks -ne $quiet[$key][1]) { $coherent=$false;break }
        }
    }
    if(-not $coherent) { Write-Output "Source changed during copy; retrying ($attempt/5)." }
}
if(-not $coherent) { throw 'Não foi possível congelar uma cópia consistente dos assets.' }
$sceneSourceHash=(Get-FileHash -LiteralPath (Join-Path $source 'Assets/Scenes/BaiaDoSol.unity')).Hash
$sceneCopyHash=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash
$terrainSourceHash=(Get-FileHash -LiteralPath (Join-Path $source 'Assets/Imported/ClothingShop.asset')).Hash
$terrainCopyHash=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Imported/ClothingShop.asset')).Hash
if($sceneSourceHash -ne $sceneCopyHash -or $terrainSourceHash -ne $terrainCopyHash) { throw 'Hashes de cena ou terreno divergiram durante a cópia.' }
foreach ($part in @('Artifacts','PackageCache','ShaderCache')) {
    $cache = Join-Path $source ('Library/' + $part)
    if (Test-Path -LiteralPath $cache) {
        & robocopy $cache (Join-Path $snapshot ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot cache copy failed: $part ($LASTEXITCODE)" }
    }
}
$authoring = Join-Path $PSScriptRoot 'authoring'
Copy-Item -LiteralPath (Join-Path $authoring 'CityWaterfront.cs') -Destination (Join-Path $snapshot 'Assets/Scripts/World/CityWaterfront.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityWaterfrontProbe.cs') -Destination (Join-Path $snapshot 'Assets/Scripts/World/CityWaterfrontProbe.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityWaterfrontBuilder.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityWaterfrontBuilder.cs') -Force
Copy-Item -LiteralPath (Join-Path $authoring 'CityWaterfrontSnapshotBuild.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityWaterfrontSnapshotBuild.cs') -Force
$utf8 = [Text.UTF8Encoding]::new($false)
$context = Join-Path $snapshot 'Assets/Scripts/Core/CityContext.cs'
$value = [IO.File]::ReadAllText($context)
if (-not $value.Contains('CityPark.Bind(this);')) { throw 'CityContext binding anchor absent' }
$value = $value.Replace('CityPark.Bind(this);', "CityPark.Bind(this);`n  CityWaterfront.Bind(this);")
[IO.File]::WriteAllText($context,$value,$utf8)
$cover = Join-Path $snapshot 'Assets/Editor/CityGroundCoverBuilder.cs'
$value = [IO.File]::ReadAllText($cover)
if (-not $value.Contains('if (CityParkBuilder.Reserved(x, z, radius)) return false;')) { throw 'Ground-cover anchor absent' }
$value = $value.Replace('if (CityParkBuilder.Reserved(x, z, radius)) return false;', "if (CityParkBuilder.Reserved(x, z, radius)) return false;`n            if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;")
[IO.File]::WriteAllText($cover,$value,$utf8)
$builder = Join-Path $snapshot 'Assets/Editor/CityBuilder.cs'
$value = [IO.File]::ReadAllText($builder)
if (-not $value.Contains('CityFireStationSiteBuilder.Build(root.transform,world);')) { throw 'City import anchor absent' }
$value = $value.Replace('CityFireStationSiteBuilder.Build(root.transform,world);', "CityFireStationSiteBuilder.Build(root.transform,world);`n  CityWaterfrontBuilder.Build(root.transform,world);")
[IO.File]::WriteAllText($builder,$value,$utf8)
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$source;snapshot=$snapshot;executable=$output;
    sourceFiles=$quiet.Count;sceneSha256=$sceneCopyHash;terrainAssetSha256=$terrainCopyHash;consistent=$coherent} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'preview-snapshot.json') -Encoding utf8
$editor='C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$snapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityWaterfrontSnapshotBuild.Build',
    '-city-output',('"'+$output+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'preview-build.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'preview-build.log')+'"'),'-job-worker-count','4')
$process=Start-Process -FilePath $editor -ArgumentList $arguments -PassThru -WindowStyle Hidden
Write-Output ('Isolated preview Unity PID: '+$process.Id)
$process.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$process.ExitCode;executable=$output} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'preview-process.json') -Encoding utf8
if($process.ExitCode -ne 0){throw ('Unity preview failed with exit code '+$process.ExitCode)}
