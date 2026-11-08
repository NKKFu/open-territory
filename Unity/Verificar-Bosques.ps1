$ErrorActionPreference = 'Stop'
$natureExe = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
$natureDir = Join-Path $PSScriptRoot 'Reports\nature\native'
New-Item -ItemType Directory -Path $natureDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $natureExe -ArgumentList @('-batchmode','--city-nature-qa','--city-report-dir',('"'+$natureDir+'"'),'-logFile',('"'+(Join-Path $natureDir 'nature-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('Vegetação: QA nativo iniciado, PID '+$process.Id)
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'O teste da vegetação excedeu dois minutos.' }
$reportPath = Join-Path $natureDir 'nature-qa.json'
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$passed = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $natureExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$natureExe; assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;passed=$passed;checks=@($report.checks).Count;report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Reports\nature\verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.detail) }
if (-not $passed) { throw ('QA da vegetação falhou. Consulte '+$reportPath) }
Write-Host ('Vegetação aprovada: '+@($report.checks).Count+' verificações. Capturas: '+$natureDir)
