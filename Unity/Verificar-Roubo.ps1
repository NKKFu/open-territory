param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\carjacking\native'),
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-carjacking-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host ('Roubo: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 210) { $process.Kill(); throw 'A verificação de roubo excedeu três minutos e meio.' }
}
$reportPath = Join-Path $ReportDirectory 'carjacking-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$sameAssembly = (Get-FileHash -LiteralPath $assembly).Hash -eq $testedHash
$passed = $sameAssembly -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$sameAssembly; processExitCode=$process.ExitCode; captures=(-not $NoGraphics); arguments=$arguments; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de roubo falhou. Consulte ' + $reportPath) }
Write-Host ('Roubo aprovado: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
