param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\OutdoorDestruction-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\outdoor-destruction\native'),
    [ValidateSet('auto', 'd3d11', 'd3d12')][string]$GraphicsApi = 'auto',
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$launchArguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800',
    '--city-outdoor-destruction-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'),
    '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($NoGraphics) { $launchArguments += '-nographics' }
elseif ($GraphicsApi -ne 'auto') { $launchArguments += '-force-' + $GraphicsApi }
$process = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Output ('Destruição de cercas e árvores: QA nativo iniciado, PID ' + $process.Id)
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 250) { $process.Kill(); throw 'Outdoor destruction QA timed out.' }
}
$reportPath = Join-Path $ReportDirectory 'outdoor-destruction-qa.json'
$fresh = (Test-Path -LiteralPath $reportPath) -and (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$report = if ($fresh) { Get-Content -LiteralPath $reportPath -Raw -Encoding utf8 | ConvertFrom-Json } else { $null }
$sameAssembly = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $sameAssembly -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{ startedAtUtc=$started.ToString('o'); verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable;
    assemblySha256=$testedHash; assemblyUnchanged=$sameAssembly; processExitCode=$process.ExitCode; graphicsApi=$report.graphics;
    resolution='1280x800'; arguments=$launchArguments; fresh=[bool]$fresh; passed=[bool]$passed;
    fences=$report.fences; trees=$report.trees; checks=@($report.checks).Count; report=$reportPath
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Output ('FAIL: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('Outdoor destruction QA failed. See ' + $reportPath) }
Write-Output ('Destruição de cercas e árvores aprovada: ' + @($report.checks).Count + ' verificações. ' + $reportPath)
