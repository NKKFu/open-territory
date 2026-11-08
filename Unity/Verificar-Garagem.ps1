param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\garage\native'),
    [ValidateSet('default', 'd3d11')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$garageAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$garageHash = (Get-FileHash -LiteralPath $garageAssembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-garage-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($GraphicsApi -eq 'd3d11') { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host ('Garagem: QA nativo iniciado, PID ' + $process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'A verificação da garagem excedeu três minutos.' }
$reportPath = Join-Path $ReportDirectory 'garage-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$assemblyChanged = (Get-FileHash -LiteralPath $garageAssembly).Hash -ne $garageHash
$passed = [bool]$report.passed -and -not $assemblyChanged -and $process.ExitCode -eq 0
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $Executable
    assemblySha256 = $garageHash
    assemblyChangedDuringRun = $assemblyChanged
    exitCode = $process.ExitCode
    graphicsApi = $GraphicsApi
    passed = $passed
    checks = $report.checks.Count
    floorSamples = $report.floorSamples
    report = $reportPath
    arguments = $arguments
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Garagem: ' + @($report.checks | Where-Object passed).Count + '/' + $report.checks.Count + ' verificações; ' + $report.floorSamples + ' amostras de piso; saída ' + $process.ExitCode)
if (-not $passed) {
    $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $reportPath)
}
