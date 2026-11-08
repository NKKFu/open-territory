param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\GrassFluffy-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\terrain-fluffy\native'),
    [ValidateSet('Default', 'D3D11', 'D3D12')][string]$GraphicsApi = 'Default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800',
    '--city-terrain-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'),
    '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($GraphicsApi -eq 'D3D11') { $arguments += '-force-d3d11' }
if ($GraphicsApi -eq 'D3D12') { $arguments += '-force-d3d12' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(6)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação do terreno excedeu seis minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'terrain-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) {
    throw "Relatório novo ausente; saída do player: $($process.ExitCode)."
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$unchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $hash
$passed = [bool]$report.passed -and $unchanged -and $process.ExitCode -eq 0
[ordered]@{ verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); executable = $Executable; assemblySha256 = $hash;
    assemblyUnchanged = $unchanged; processExitCode = $process.ExitCode; passed = $passed; graphicsApi = $report.graphicsApi;
    requestedGraphicsApi = $GraphicsApi; arguments = $arguments; checks = @($report.checks).Count; report = $reportPath } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Terreno: ' + @($report.checks | Where-Object passed).Count + '/' + @($report.checks).Count + ' verificações; saída ' + $process.ExitCode)
if (-not $passed) {
    $report.checks | Where-Object { -not $_.passed } | Format-Table name, detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $reportPath)
}
