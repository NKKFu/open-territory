$quartelGame = Join-Path $PSScriptRoot 'Deliveries\FireStationWaterfront-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $quartelGame)) { throw 'O executavel do quartel ainda nao esta disponivel.' }
Start-Process -FilePath $quartelGame -WorkingDirectory (Split-Path -Parent $quartelGame)
