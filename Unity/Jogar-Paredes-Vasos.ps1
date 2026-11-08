$wallPlanterGame = Join-Path $PSScriptRoot 'Deliveries/WallPlanters-20261108/BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $wallPlanterGame)) { throw 'A entrega de paredes e vasos ainda não foi construída.' }
Start-Process -FilePath $wallPlanterGame -WorkingDirectory (Split-Path -Parent $wallPlanterGame)
