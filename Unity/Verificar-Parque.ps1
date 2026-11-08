param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\SunmeadowPark-20261108-Verified\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\park\native')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$parkAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$parkHash = (Get-FileHash -LiteralPath $parkAssembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-park-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(5)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação do parque excedeu cinco minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'park-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw 'Relatório novo do parque ausente.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$changed = (Get-FileHash -LiteralPath $parkAssembly).Hash -ne $parkHash
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); executable = $Executable
    assemblySha256 = $parkHash; assemblyChangedDuringRun = $changed
    processExitCode = $process.ExitCode; passed = [bool]$report.passed -and -not $changed -and $process.ExitCode -eq 0
    checks = $report.checks.Count; routeSamples = $report.routeSamples; graphics = $report.graphicsApi
    arguments = $arguments; report = $reportPath
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Parque: ' + @($report.checks | Where-Object passed).Count + '/' + $report.checks.Count + ' verificações; ' + $report.routeSamples + ' amostras; saída ' + $process.ExitCode)
if ($process.ExitCode -ne 0 -or -not $report.passed -or $changed) {
    $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $reportPath)
}
