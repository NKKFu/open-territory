param([string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'))
$ErrorActionPreference = 'Stop'
$atmosphereDir = Join-Path $PSScriptRoot 'Reports\atmosphere\native'
New-Item -ItemType Directory -Path $atmosphereDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList @('-batchmode', '--city-atmosphere-qa', '--city-report-dir', ('"' + $atmosphereDir + '"'), '-logFile', ('"' + (Join-Path $atmosphereDir 'atmosphere-qa.log') + '"')) -WindowStyle Hidden -PassThru
Write-Host ('Vento e nuvens: QA nativo iniciado, PID ' + $process.Id)
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'O teste de clima não terminou em um minuto.' }
$reportPath = Join-Path $atmosphereDir 'atmosphere-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de clima.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $atmosphereDir) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de vento e nuvens falhou. Consulte ' + $reportPath) }
Write-Host ('Vento e nuvens aprovados: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
