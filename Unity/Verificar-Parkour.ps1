param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\parkour\native'),
    [switch]$NoGraphics,
    [switch]$CameraOnly,
    [switch]$CrouchOnly,
    [switch]$CarOnly
)
$ErrorActionPreference = 'Stop'
if (([int][bool]$CameraOnly + [int][bool]$CrouchOnly + [int][bool]$CarOnly) -gt 1) { throw 'Use CameraOnly, CrouchOnly ou CarOnly separadamente.' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Gere primeiro o executável Windows no Unity.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assemblyPath = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$log = Join-Path $ReportDirectory 'player.log'
$reportPath = Join-Path $ReportDirectory 'parkour-qa.json'
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '--city-parkour-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + $log + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
if ($CameraOnly) { $arguments += '--city-parkour-camera-qa' }
if ($CrouchOnly) { $arguments += '--city-crouch-qa' }
if ($CarOnly) { $arguments += '--city-parkour-car-qa' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $timeoutMinutes = if ($CameraOnly -or $CrouchOnly -or $CarOnly) { 3 } else { 4 }
    $deadline = [DateTime]::UtcNow.AddMinutes($timeoutMinutes)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw ('A verificação de salto e parkour excedeu ' + $timeoutMinutes + ' minutos.') }
    }
    if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + $log) }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação; execute novamente sobre uma compilação estável.' }
    if ($process.ExitCode -ne 0 -or -not $report.passed) {
        $report.checks | Where-Object { -not $_.passed } | Format-Table name, value
        throw ('A verificação de salto e parkour falhou. Consulte ' + $reportPath)
    }
    [ordered]@{
        verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
        executable = $Executable
        assemblySha256 = $assemblyHash
        checks = @($report.checks).Count
        exitCode = $process.ExitCode
        cameraOnly = [bool]$CameraOnly
        crouchOnly = [bool]$CrouchOnly
        carOnly = [bool]$CarOnly
        graphics = -not [bool]$NoGraphics
        arguments = $arguments
        passed = $true
        report = $reportPath
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding UTF8
    Write-Output ('Salto e parkour: ' + @($report.checks).Count + ' verificações aprovadas. Relatório: ' + $reportPath)
}
finally { $process.Dispose() }
