$firearmsGame = Join-Path $PSScriptRoot 'OpenTerritory\Builds\Firearms-20261108-0800\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $firearmsGame)) { throw 'O build isolado de armas ainda não está disponível.' }
Start-Process -FilePath $firearmsGame -WorkingDirectory (Split-Path -Parent $firearmsGame)
