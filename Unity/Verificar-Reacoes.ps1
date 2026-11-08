param([string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'), [switch]$NoGraphics, [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\reactions\native'))
$ErrorActionPreference = 'Stop'
$reportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$launchArguments = @('-batchmode', '--city-reactions-qa', '--city-report-dir', ('"' + $reportDirectory + '"'), '-logFile', ('"' + (Join-Path $reportDirectory 'reactions-qa.log') + '"'))
if ($NoGraphics) { $launchArguments += '-nographics' }
$process = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
Write-Host ('Reações: QA nativo iniciado, PID ' + $process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'O teste de reações não terminou em três minutos.' }
$reportPath = Join-Path $reportDirectory 'reactions-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de reações.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed -and $report.completed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged; exitCode=$process.ExitCode; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $reportDirectory) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de reações falhou. Consulte ' + $reportPath) }
Write-Host ('Reações aprovadas: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
