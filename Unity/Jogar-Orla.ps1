$ErrorActionPreference='Stop'
$game=Join-Path $PSScriptRoot 'Deliveries\Bayfront-20261108-Current\BaiaDoSol.exe'
if(-not (Test-Path -LiteralPath $game)){throw 'A entrega da orla ainda não foi construída.'}
Start-Process -FilePath $game -WorkingDirectory (Split-Path -Parent $game)
