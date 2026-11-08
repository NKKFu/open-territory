param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\seat-camera\native'),
    [ValidateSet('default','d3d11')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assemblyPath = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$reportPath = Join-Path $ReportDirectory 'seat-camera-qa.json'
$logPath = Join-Path $ReportDirectory 'player.log'
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-seat-camera-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + $logPath + '"'))
if ($GraphicsApi -eq 'd3d11') { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null = $process.Handle
try {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação da câmera dos assentos excedeu três minutos.' }
    }
    $process.WaitForExit()
    if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + $logPath) }
    if ((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação; execute novamente sobre uma compilação estável.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $passed = $process.ExitCode -eq 0 -and $report.passed
    [ordered]@{
        verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
        executable = $Executable
        assemblySha256 = $assemblyHash
        exitCode = $process.ExitCode
        graphicsApi = $GraphicsApi
        arguments = $arguments
        passed = $passed
        checks = @($report.checks).Count
        report = $reportPath
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
    $report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
    if (-not $passed) { throw ('A verificação da câmera dos assentos falhou. Consulte ' + $reportPath) }
    Write-Output ('Câmera dos assentos: ' + @($report.checks).Count + ' verificações aprovadas. Relatório: ' + $reportPath)
}
finally { $process.Dispose() }
