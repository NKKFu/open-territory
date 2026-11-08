param([switch]$ReuseSnapshot)
$ErrorActionPreference = 'Stop'
$clothingSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$clothingSnapshot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/ClothingShop-20261108'))
$clothingOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Deliveries/ClothingShop-20261108/BaiaDoSol.exe'))
New-Item -ItemType Directory -Path $clothingSnapshot -Force | Out-Null
if (-not $ReuseSnapshot) {
    foreach ($part in @('Assets','Packages','ProjectSettings')) {
        & robocopy (Join-Path $clothingSource $part) (Join-Path $clothingSnapshot $part) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot copy failed: $part" }
    }
    foreach ($part in @('Artifacts','PackageCache','ShaderCache')) {
        $clothingCache = Join-Path $clothingSource ('Library/' + $part)
        if (-not (Test-Path -LiteralPath $clothingCache)) { continue }
        & robocopy $clothingCache (Join-Path $clothingSnapshot ('Library/' + $part)) /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Snapshot cache copy failed: $part" }
    }
}
$clothingFingerprints = Get-ChildItem -LiteralPath (Join-Path $clothingSnapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($clothingSnapshot.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
}
[ordered]@{ createdAtUtc=[DateTime]::UtcNow.ToString('o'); source=$clothingSource; snapshot=$clothingSnapshot; output=$clothingOutput;
    sceneSha256=(Get-FileHash -LiteralPath (Join-Path $clothingSnapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash; scripts=@($clothingFingerprints) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$clothingEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$clothingArguments = @('-batchmode','-nographics','-quit','-projectPath',('"'+$clothingSnapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityClothingBuildJob.BuildSnapshot',
    '-city-output',('"'+$clothingOutput+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$clothingProcess = Start-Process -FilePath $clothingEditor -ArgumentList $clothingArguments -PassThru -WindowStyle Hidden
Write-Output ('Clothing isolated full build: PID '+$clothingProcess.Id)
$clothingProcess.WaitForExit()
[ordered]@{ completedAtUtc=[DateTime]::UtcNow.ToString('o'); processExitCode=$clothingProcess.ExitCode; output=$clothingOutput } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if ($clothingProcess.ExitCode -ne 0) { throw ('Unity exited with code '+$clothingProcess.ExitCode) }
Write-Output 'Clothing isolated full build complete.'
