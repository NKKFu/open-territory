$ErrorActionPreference = 'Stop'
$weaponAssetsGame = Join-Path $PSScriptRoot 'Deliveries\WeaponAssets-20261108-Verified\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $weaponAssetsGame)) {
    throw 'O executável com os assets de armas não foi encontrado em Unity/Deliveries.'
}
Start-Process -FilePath $weaponAssetsGame -WorkingDirectory (Split-Path -Parent $weaponAssetsGame)
