$ErrorActionPreference = 'Stop'
$game = Join-Path $PSScriptRoot 'Deliveries\ParkingLot-20261108-Final\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $game)) { throw 'A versão do estacionamento ainda não foi construída.' }
Start-Process -FilePath $game -WorkingDirectory (Split-Path -Parent $game)
