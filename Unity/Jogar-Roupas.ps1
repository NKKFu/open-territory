$clothingGame = Join-Path $PSScriptRoot 'Deliveries/ClothingShop-20261108/BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $clothingGame)) { throw 'Construa a entrega da Tideline antes de jogar.' }
Start-Process -FilePath $clothingGame -WorkingDirectory (Split-Path -Parent $clothingGame)
