$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/LinearWaterfrontPark-Preview'))
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/LinearWaterfrontPark-Preview/BaiaDoSol.exe'))
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

Write-Output ('Snapshot copied to '+$snapshot)
