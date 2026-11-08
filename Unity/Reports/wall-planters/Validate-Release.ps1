param([switch]$SummarizeOnly)
$ErrorActionPreference = 'Stop'
$wallPlanterUnity = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$wallPlanterExe = Join-Path $wallPlanterUnity 'Deliveries/WallPlanters-20261108/BaiaDoSol.exe'
if (-not $SummarizeOnly) {
foreach ($wallPlanterCase in @(
    @{scenario='walls';folder='release/walls-1'},
    @{scenario='walls';folder='release/walls-2'},
    @{scenario='impacts';folder='release/impacts'},
    @{scenario='driving';folder='release/driving'}
)) {
    & (Join-Path $wallPlanterUnity 'Verificar-Paredes-Vasos.ps1') -Executable $wallPlanterExe -Scenario $wallPlanterCase.scenario -ReportDirectory (Join-Path $PSScriptRoot $wallPlanterCase.folder)
}
& (Join-Path $wallPlanterUnity 'Verificar-Executavel-Padrao.ps1') -Executable $wallPlanterExe -ReportDirectory (Join-Path $PSScriptRoot 'release/normal')
}
$wallPlanterResults = foreach ($wallPlanterFolder in @('walls-1','walls-2','impacts','driving','normal')) {
    $wallPlanterFile = Join-Path $PSScriptRoot ('release/'+$wallPlanterFolder+'/verification.json')
    $wallPlanterResult = Get-Content -LiteralPath $wallPlanterFile -Raw | ConvertFrom-Json
    [ordered]@{scenario=$wallPlanterFolder;passed=$wallPlanterResult.passed;exitCode=$wallPlanterResult.exitCode;
        assemblySha256=$wallPlanterResult.assemblySha256;report=$wallPlanterFile}
}
$wallPlanterInput = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-input.json') -Raw | ConvertFrom-Json
$wallPlanterHash = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $wallPlanterExe) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll')).Hash
$wallPlanterSourceDrift = @($wallPlanterInput.scripts | Where-Object {
    (Get-FileHash -LiteralPath (Join-Path $wallPlanterInput.source $_.path)).Hash -ne $_.sha256
})
$wallPlanterSceneUnchanged = (Get-FileHash -LiteralPath (Join-Path $wallPlanterInput.source 'Assets/Scenes/BaiaDoSol.unity')).Hash -eq $wallPlanterInput.sceneSha256
$wallPlanterFeatureFiles = @('CityVehicles.cs','CityVehicles.WallContacts.cs','CityVehicleMath.cs','CityDrivingProbe.cs','CityDrivingProbe.Impacts.cs','CityDrivingProbe.Sidewalls.cs','CityDestructible.cs','CityDestruction.cs','CityDestruction.Outdoor.cs','CityDestruction.Planters.cs','CityDestruction.Fragments.cs')
$wallPlanterFeatureDrift = @($wallPlanterSourceDrift | Where-Object {[IO.Path]::GetFileName($_.path) -in $wallPlanterFeatureFiles})
$wallPlanterPassed = @($wallPlanterResults | Where-Object {-not $_.passed -or $_.exitCode -ne 0 -or $_.assemblySha256 -ne $wallPlanterHash}).Count -eq 0 -and $wallPlanterFeatureDrift.Count -eq 0
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$wallPlanterPassed;executable=$wallPlanterExe;
    assemblySha256=$wallPlanterHash;graphicsApi='Direct3D12 (player default)';sceneSha256=$wallPlanterInput.sceneSha256;
    sourceSceneUnchanged=$wallPlanterSceneUnchanged;featureSourcesUnchanged=($wallPlanterFeatureDrift.Count -eq 0);
    currentSourceSceneSha256=(Get-FileHash -LiteralPath (Join-Path $wallPlanterInput.source 'Assets/Scenes/BaiaDoSol.unity')).Hash;
    playerSceneSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $wallPlanterExe) 'BaiaDoSol_Data/level0')).Hash;
    changedSourceFiles=@($wallPlanterSourceDrift | ForEach-Object {$_.path});
    buildReport=(Join-Path $PSScriptRoot 'build-complete.json');validations=@($wallPlanterResults)} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
if (-not $wallPlanterPassed) { throw 'Release verification failed; inspect verification.json.' }
Write-Output 'Release verified: both wall runs, impacts, driving and ordinary startup passed with the same player assembly.'
