$dealershipGame = Join-Path $PSScriptRoot 'Deliveries\CarDealership-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $dealershipGame)) { throw 'A entrega da Sun Motors não foi encontrada em Unity/Deliveries/CarDealership-20261108.' }
Start-Process -FilePath $dealershipGame -WorkingDirectory (Split-Path -Parent $dealershipGame)
