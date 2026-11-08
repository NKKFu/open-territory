$destructionGame = Join-Path $PSScriptRoot 'Deliveries\OutdoorDestruction-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $destructionGame)) { throw 'A entrega de cercas e árvores ainda não está disponível.' }
Start-Process -FilePath $destructionGame -WorkingDirectory (Split-Path -Parent $destructionGame)
