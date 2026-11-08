param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\CarDealership-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\dealership\delivery'),
    [ValidateSet('default', 'd3d11', 'd3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$dealershipAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$dealershipHash = (Get-FileHash -LiteralPath $dealershipAssembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-dealership-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($GraphicsApi -eq 'd3d11') { $arguments += '-force-d3d11' }
if ($GraphicsApi -eq 'd3d12') { $arguments += '-force-d3d12' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host ('Loja de carros: QA nativo iniciado, PID ' + $process.Id)
$deadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação da loja excedeu quatro minutos.' }
}
$process.Refresh()
$reportPath = Join-Path $ReportDirectory 'dealership-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$dealershipLog = Get-Content -LiteralPath (Join-Path $ReportDirectory 'player.log') -Raw
$actualApi = if ($dealershipLog -match 'Direct3D\s*12|Direct3D 12|D3D12') { 'Direct3D 12' } elseif ($dealershipLog -match 'Direct3D\s*11|Direct3D 11|D3D11') { 'Direct3D 11' } else { 'See player.log' }
$changed = (Get-FileHash -LiteralPath $dealershipAssembly).Hash -ne $dealershipHash
$passed = [bool]$report.passed -and -not $changed -and $process.ExitCode -eq 0
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $Executable
    assemblySha256 = $dealershipHash
    assemblyChangedDuringRun = $changed
    passed = $passed
    exitCode = $process.ExitCode
    checks = $report.checks.Count
    routeSamples = $report.routeSamples
    report = $reportPath
    graphics = $actualApi
    requestedGraphicsApi = $GraphicsApi
    arguments = $arguments
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Loja de carros: ' + @($report.checks | Where-Object passed).Count + '/' + $report.checks.Count + ' verificações; ' + $report.routeSamples + ' amostras; saída ' + $process.ExitCode)
if (-not $passed) {
    $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $reportPath)
}
