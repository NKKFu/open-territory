param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$reportDirectory = Join-Path $PSScriptRoot 'Reports\vehicles\native'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$launchArguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '--city-driving-qa', '--city-report-dir', ('"' + $reportDirectory + '"'), '-logFile', ('"' + (Join-Path $reportDirectory 'driving-qa.log') + '"'))
if ($NoGraphics) { $launchArguments += '-nographics' }
$process = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
Write-Host ('Direção: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 210) { $process.Kill(); throw 'O teste de direção excedeu três minutos e meio.' }
}
$reportPath = Join-Path $reportDirectory 'driving-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de direção.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $reportDirectory) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.value + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de direção falhou. Consulte ' + $reportPath) }
Write-Host ('Direção aprovada: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
