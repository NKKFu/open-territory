param([string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'))
$ErrorActionPreference = 'Stop'
$reportDirectory = Join-Path $PSScriptRoot 'Reports\hacking\native'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '--city-hacking-qa', '--city-report-dir', ('"' + $reportDirectory + '"'), '-logFile', ('"' + (Join-Path $reportDirectory 'hacking-qa.log') + '"')) -WindowStyle Hidden -PassThru
Write-Host ('Hacking: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 270) { $process.Kill(); throw 'O teste de hacking excedeu quatro minutos e meio.' }
}
$reportPath = Join-Path $reportDirectory 'hacking-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório de hacking.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged; passed=$passed; checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $reportDirectory) 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de hacking falhou. Consulte ' + $reportPath) }
Write-Host ('Hacking aprovado: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
