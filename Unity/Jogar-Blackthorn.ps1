$blackthornGame = Join-Path $PSScriptRoot 'Deliveries/BlackthornLowPolyShop-20261108/BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $blackthornGame)) { throw 'Build Blackthorn House in Unity before launching this delivery.' }
Start-Process -FilePath $blackthornGame -WorkingDirectory (Split-Path -Parent $blackthornGame)
