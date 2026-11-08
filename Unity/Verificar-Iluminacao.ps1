param([string]$Executable)
$ErrorActionPreference = 'Stop'
$lightingExe = if ($Executable) { [IO.Path]::GetFullPath($Executable) } else { Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe' }
$lightingReportDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Reports\lighting\placement'))
New-Item -ItemType Directory -Path $lightingReportDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $lightingExe -ArgumentList @('-batchmode', '--city-lighting-qa', '--city-report-dir', ('"' + $lightingReportDir + '"'), '-logFile', ('"' + (Join-Path $lightingReportDir 'player.log') + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'A verificação da iluminação excedeu dois minutos.' }
$reportPath = Join-Path $lightingReportDir 'lighting-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O player não produziu o relatório de iluminação.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $fresh -or $process.ExitCode -ne 0 -or -not $report.passed) { throw ('Falha na iluminação. Consulte ' + $reportPath) }
Write-Host ('Iluminação aprovada: ' + $report.checks.Count + ' verificações, ' + $report.fixtures.Count + ' fontes. Capturas: ' + $lightingReportDir)
