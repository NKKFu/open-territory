param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\locomotion\gait'),
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Gere primeiro o executável Windows no Unity.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assemblyPath = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$log = Join-Path $ReportDirectory 'player.log'
$reportPath = Join-Path $ReportDirectory 'gait-qa.json'
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '--city-gait-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + $log + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação de passadas excedeu três minutos.' }
    }
    if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + $log) }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação; execute novamente sobre uma compilação estável.' }
    if ($process.ExitCode -ne 0 -or -not $report.passed) {
        $report.checks | Where-Object { -not $_.passed } | Format-Table name, value
        throw ('A verificação de passadas falhou. Consulte ' + $reportPath)
    }
    [ordered]@{
        verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
        executable = $Executable
        assemblySha256 = $assemblyHash
        checks = @($report.checks).Count
        passed = $true
        report = $reportPath
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding UTF8
    Write-Output ('Passadas: ' + @($report.checks).Count + ' verificações aprovadas. Relatório: ' + $reportPath)
}
finally { $process.Dispose() }
