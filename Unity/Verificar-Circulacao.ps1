param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\living-city\native')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$reportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList @('-batchmode', '--city-living-qa', '--city-report-dir', ('"' + $reportDirectory + '"'), '-logFile', ('"' + (Join-Path $reportDirectory 'living-city-qa.log') + '"')) -WindowStyle Hidden -PassThru
Write-Host ('Circulação: QA nativo iniciado, PID ' + $process.Id)
if (-not $process.WaitForExit(360000)) { $process.Kill(); throw 'O teste de circulação não terminou em seis minutos.' }
$reportPath = Join-Path $reportDirectory 'living-city-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de circulação.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged; processExitCode=$process.ExitCode; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $reportDirectory) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de circulação falhou. Consulte ' + $reportPath) }
Write-Host ('Circulação aprovada: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
