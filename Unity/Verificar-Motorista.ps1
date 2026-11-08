param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\DriverSeat-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\driver-seat\native'),
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-driver-contact-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host ('Motorista e atropelamento: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 260) { $process.Kill(); throw 'A verificação do motorista excedeu o prazo.' }
}
$reportPath = Join-Path $ReportDirectory 'driver-contact-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding utf8 | ConvertFrom-Json
$sameAssembly = (Get-FileHash -LiteralPath $assembly).Hash -eq $testedHash
$passed = $sameAssembly -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$sameAssembly; processExitCode=$process.ExitCode; captures=(-not $NoGraphics); graphics=$report.graphics; arguments=$arguments; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA do motorista falhou. Consulte ' + $reportPath) }
Write-Host ('Motorista e atropelamento aprovados: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
