param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe')
)
$ErrorActionPreference = 'Stop'
$reportDirectory = Join-Path $PSScriptRoot 'Reports\hud\native'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$launchArguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-hud-qa', '--city-report-dir', ('"' + $reportDirectory + '"'), '-logFile', ('"' + (Join-Path $reportDirectory 'hud-qa.log') + '"'))
$process = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
Write-Host ('HUD: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 150) { $process.Kill(); throw 'O teste do HUD excedeu dois minutos e meio.' }
}
$reportPath = Join-Path $reportDirectory 'hud-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório do HUD.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $reportDirectory) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA do HUD falhou. Consulte ' + $reportPath) }
Write-Host ('HUD aprovado: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
