param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\cemetery\native')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$cemeteryAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$cemeteryAssemblyHash = (Get-FileHash -LiteralPath $cemeteryAssembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-cemetery-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host ('Cemitério: QA nativo iniciado, PID ' + $process.Id)
$deadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação do cemitério excedeu quatro minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'cemetery-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$cemeteryAssemblyChanged = (Get-FileHash -LiteralPath $cemeteryAssembly).Hash -ne $cemeteryAssemblyHash
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $Executable
    assemblySha256 = $cemeteryAssemblyHash
    assemblyChangedDuringRun = $cemeteryAssemblyChanged
    passed = [bool]$report.passed -and -not $cemeteryAssemblyChanged
    checks = $report.checks.Count
    routeSamples = $report.routeSamples
    report = $reportPath
    graphics = 'D3D11; player 1280x800; captures 1600x1000'
    arguments = $arguments
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Cemitério: ' + @($report.checks | Where-Object passed).Count + '/' + $report.checks.Count + ' verificações; ' + $report.routeSamples + ' amostras de piso e passagem.')
if ($process.ExitCode -ne 0 -or -not $report.passed -or $cemeteryAssemblyChanged) {
    $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $reportPath)
}
