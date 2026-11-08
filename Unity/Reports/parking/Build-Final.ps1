param([string]$DeliveryName = 'ParkingLot-20261108-Final')
$ErrorActionPreference = 'Stop'
if ($DeliveryName -notmatch '^[A-Za-z0-9_-]+$') { throw 'Nome de entrega inválido.' }
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/ParkingLot-20261108-1038'))
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../Deliveries/' + $DeliveryName + '/BaiaDoSol.exe')))
if (-not (Test-Path -LiteralPath (Join-Path $snapshot 'ProjectSettings/ProjectVersion.txt'))) { throw 'Cópia isolada original ausente.' }
if (Test-Path -LiteralPath $output) { throw ('A entrega já existe: ' + $output) }
function Get-Manifest($root) {
    $result = @{}
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        foreach ($item in Get-ChildItem -LiteralPath (Join-Path $root $part) -File -Recurse) {
            $relative = $item.FullName.Substring($root.Length + 1)
            if ($relative -like 'Assets\Scenes\Backups\*' -or $relative -in @('Assets\Editor\CityParkingFinalBuild.cs','Assets\Editor\CityParkingFinalBuild.cs.meta')) { continue }
            $result[$relative] = @($item.Length, $item.LastWriteTimeUtc.Ticks)
        }
    }
    return $result
}
function Same($a,$b) {
    if ($a.Count -ne $b.Count) { return $false }
    foreach ($k in $a.Keys) { if (-not $b.ContainsKey($k) -or $a[$k][0] -ne $b[$k][0] -or $a[$k][1] -ne $b[$k][1]) { return $false } }
    return $true
}
$coherent = $false
for ($attempt = 1; $attempt -le 5 -and -not $coherent; $attempt++) {
    $before = Get-Manifest $source
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        $copyArgs = @((Join-Path $source $part),(Join-Path $snapshot $part),'/E','/COPY:DAT','/DCOPY:DAT','/R:2','/W:1','/NFL','/NDL','/NJH','/NJS','/NP')
        if ($part -eq 'Assets') { $copyArgs += @('/XD',(Join-Path $source 'Assets/Scenes/Backups')) }
        & robocopy @copyArgs | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Falha ao copiar $part ($LASTEXITCODE)." }
    }
    $after = Get-Manifest $source
    Start-Sleep -Seconds 3
    $quiet = Get-Manifest $source
    $copied = Get-Manifest $snapshot
    $coherent = (Same $before $after) -and (Same $after $quiet) -and (Same $quiet $copied)
    if (-not $coherent) { Write-Output "Projeto mudou durante a cópia; repetindo ($attempt/5)." }
}
if (-not $coherent) { throw 'Não foi possível congelar uma cópia consistente do projeto.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'authoring/CityParkingFinalBuild.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityParkingFinalBuild.cs') -Force
$sceneHash = (Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash
$assetHash = (Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Imported/WaterfrontParking.asset')).Hash
$scriptHashes = Get-ChildItem -LiteralPath (Join-Path $snapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($snapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$source;snapshot=$snapshot;executable=$output;sourceFiles=$quiet.Count;
    consistent=$coherent;sceneSha256=$sceneHash;parkingAssetSha256=$assetHash;scripts=@($scriptHashes)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot-final.json') -Encoding utf8
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$arguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$snapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityParkingFinalBuild.Build',
    '-city-output',('"'+$output+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-final.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'build-final.log')+'"'),'-job-worker-count','4')
$process = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Build final isolado: PID ' + $process.Id)
$process.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$process.ExitCode;executable=$output} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-final-process.json') -Encoding utf8
if ($process.ExitCode -ne 0) { throw ('Unity encerrou com código ' + $process.ExitCode) }
