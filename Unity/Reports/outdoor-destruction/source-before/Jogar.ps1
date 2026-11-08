$unityGame = Join-Path $PSScriptRoot 'OpenTerritory\Builds\DriverSeat-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) { throw 'Abra o projeto no Unity e use Open Territory > Construir Windows.' }
Start-Process -FilePath $unityGame -WorkingDirectory (Split-Path -Parent $unityGame)
