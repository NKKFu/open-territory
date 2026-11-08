param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$Label = 'optimized',
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\performance\optimized'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default',
    [switch]$ExpectOptimized,
    [ValidateRange(1,10)][int]$Runs = 1
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Performance-Content.ps1')
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$playerRoot = Split-Path -Parent $Executable
$contentHash = Get-PerformanceContentHash $playerRoot
$engineHash = (Get-FileHash -LiteralPath (Join-Path $playerRoot 'UnityPlayer.dll') -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
for ($run = 1; $run -le $Runs; $run++) {
    $runDirectory = Join-Path $ReportDirectory ('run-' + $run)
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $started = [DateTime]::UtcNow
    $playerArgs = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '--city-performance-qa', '--city-performance-label', $Label, '--city-report-dir', ('"' + $runDirectory + '"'),
        '-logFile', ('"' + (Join-Path $runDirectory 'player.log') + '"'))
    if ($ExpectOptimized) { $playerArgs += '--city-expect-optimized' }
    if ($GraphicsApi -ne 'default') { $playerArgs += '-force-' + $GraphicsApi }
    $process = Start-Process -FilePath $Executable -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
    # Keep the native handle while the process is alive; Windows PowerShell can
    # otherwise lose ExitCode after a normally launched GUI player exits.
    $null = $process.Handle
    Write-Host ('Desempenho: ' + $Label + ', rodada ' + $run + ', PID ' + $process.Id)
    while (-not $process.WaitForExit(1000)) {
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 180) { $process.Kill(); throw 'QA de desempenho excedeu três minutos.' }
    }
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $reportPath = Join-Path $runDirectory 'performance-qa.json'
    if (-not (Test-Path -LiteralPath $reportPath)) { throw ('Relatório ausente: ' + $reportPath) }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $unchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $hash
    $fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
    $passed = $exitCode -eq 0 -and $report.passed -and $report.completed -and $unchanged -and $fresh
    [ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$hash;
        contentSha256=$contentHash; engineSha256=$engineHash;
        assemblyUnchanged=$unchanged; passed=$passed; exitCode=$exitCode; fresh=$fresh; startedAtUtc=$started.ToString('o'); checks=@($report.checks).Count; report=$reportPath;
        width=$report.width; height=$report.height; label=$Label } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $runDirectory 'verification.json') -Encoding utf8
    $report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
    if (-not $passed) { throw ('QA de desempenho falhou: ' + $reportPath) }
    Write-Host ('Aprovado: ' + @($report.checks).Count + ' verificações. ' + $reportPath)
}
