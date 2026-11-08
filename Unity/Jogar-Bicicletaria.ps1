$ErrorActionPreference = 'Stop'
$bikeGame = Join-Path $PSScriptRoot 'Deliveries\BikeShop-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $bikeGame)) { throw 'A entrega da bicicletaria não foi encontrada.' }
Start-Process -FilePath $bikeGame -WorkingDirectory (Split-Path -Parent $bikeGame)
