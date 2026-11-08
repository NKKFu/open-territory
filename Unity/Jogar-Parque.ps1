$ErrorActionPreference = 'Stop'
$parkExecutable = Join-Path $PSScriptRoot 'Deliveries\SunmeadowPark-20261108-Verified\BaiaDoSol.exe'
if (-not (Test-Path -LiteralPath $parkExecutable)) { throw 'A entrega Sunmeadow Park não foi encontrada. Execute Reports/park/Build-Isolated.ps1.' }
Start-Process -FilePath $parkExecutable -WorkingDirectory (Split-Path -Parent $parkExecutable)
