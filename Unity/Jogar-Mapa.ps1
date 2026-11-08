param([ValidateSet('default','d3d11')][string]$GraphicsApi = 'default')
$mapGame = Join-Path $PSScriptRoot 'Deliveries\OverallMap-20261108\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $mapGame)) { throw 'A entrega da revisão geral ainda não está disponível.' }
if ($GraphicsApi -eq 'd3d11') { Start-Process -FilePath $mapGame -ArgumentList '-force-d3d11' -WorkingDirectory (Split-Path -Parent $mapGame) }
else { Start-Process -FilePath $mapGame -WorkingDirectory (Split-Path -Parent $mapGame) }
