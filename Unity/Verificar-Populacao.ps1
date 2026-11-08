param([string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'))
$ErrorActionPreference = 'Stop'
$populationDir = Join-Path $PSScriptRoot 'Reports\population\native'
New-Item -ItemType Directory -Path $populationDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList @('-batchmode', '--city-population-qa', '--city-report-dir', ('"' + $populationDir + '"'), '-logFile', ('"' + (Join-Path $populationDir 'population-qa.log') + '"')) -WindowStyle Hidden -PassThru
Write-Host ('População: QA nativo iniciado, PID ' + $process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'O teste de população não terminou em três minutos.' }
$reportPath = Join-Path $populationDir 'population-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de população.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $populationDir) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de população falhou. Consulte ' + $reportPath) }
Write-Host ('População aprovada: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
