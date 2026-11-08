param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\FireStationWaterfront-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\fire-station-red\native'),
    [ValidateSet('default','d3d11')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$quartelAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$quartelHash = (Get-FileHash -LiteralPath $quartelAssembly).Hash
$results = @()
foreach ($suite in @('fire-station','garage')) {
    $destination = Join-Path $ReportDirectory $suite
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $started = [DateTime]::UtcNow
    $arguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1440','-screen-height','1000',
        ('--city-' + $suite + '-qa'),'--city-report-dir',('"' + $destination + '"'),'-logFile',('"' + (Join-Path $destination 'player.log') + '"'))
    if ($GraphicsApi -eq 'd3d11') { $arguments += '-force-d3d11' }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host ('Quartel: ' + $suite + ', PID ' + $process.Id)
    $deadline = [DateTime]::UtcNow.AddSeconds(190)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw ('Timeout: ' + $suite) }
    }
    $reportPath = Join-Path $destination ($suite + '-qa.json')
    if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatorio atual ausente: ' + $reportPath) }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $sameAssembly = (Get-FileHash -LiteralPath $quartelAssembly).Hash -eq $quartelHash
    $entry = [ordered]@{ suite=$suite; passed=([bool]$report.passed -and $sameAssembly -and $process.ExitCode -eq 0);
        exitCode=$process.ExitCode; checks=$report.checks.Count; floorSamples=$report.floorSamples; report=$reportPath;
        arguments=$arguments; verifiedAtUtc=[DateTime]::UtcNow.ToString('o') }
    $results += $entry
    [ordered]@{ passed=(@($results | Where-Object { -not $_.passed }).Count -eq 0); executable=$Executable;
        assemblySha256=$quartelHash; graphicsApi=$GraphicsApi; suites=$results } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
    Write-Host ($suite + ': ' + @($report.checks | Where-Object passed).Count + '/' + $report.checks.Count + ', exit ' + $process.ExitCode)
    if (-not $entry.passed) { $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize; throw ('Falha: ' + $suite) }
}
