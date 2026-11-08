$unityGame = Join-Path $PSScriptRoot 'Deliveries\BlackthornLowPolyShop-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\WallPlanters-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
$clothingGame = Join-Path $PSScriptRoot 'Deliveries\ClothingShop-20261108\BaiaDoSol.exe'
if (Test-Path -LiteralPath $clothingGame) {
    Start-Process -FilePath $clothingGame -WorkingDirectory (Split-Path -Parent $clothingGame)
    return
}
$unityGame = Join-Path $PSScriptRoot 'Deliveries\FireStationWaterfront-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\BlackthornLowPolyShop-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\BlackthornHouse-20261108\BaiaDoSol.exe'
}
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\SteeringColumn-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\CarDealership-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\SunmeadowPark-20261108-Verified\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'Deliveries\OutdoorDestruction-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'OpenTerritory\Builds\DriverSeat-20261108\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) {
    $unityGame = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
}
if (-not (Test-Path -LiteralPath $unityGame)) { throw 'Abra o projeto no Unity e use Open Territory > Construir Windows.' }
Start-Process -FilePath $unityGame -WorkingDirectory (Split-Path -Parent $unityGame)
