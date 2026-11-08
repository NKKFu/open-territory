param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\vehicle-impact\native'),
    [ValidateSet('auto', 'd3d11', 'd3d12')][string]$GraphicsApi = 'auto',
    [switch]$FullDriving,
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$qaFlag = if ($FullDriving) { '--city-driving-qa' } else { '--city-vehicle-impact-qa' }
$launchArguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', $qaFlag,
    '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'driving-qa.log') + '"'))
if ($NoGraphics) { $launchArguments += '-nographics' }
elseif ($GraphicsApi -ne 'auto') { $launchArguments += '-force-' + $GraphicsApi }
$process = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
$null = $process.Handle
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt 210) { $process.Kill(); throw 'Vehicle impact QA timed out.' }
}
$reportPath = Join-Path $ReportDirectory 'driving-qa.json'
$fresh = (Test-Path -LiteralPath $reportPath) -and (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$report = if ($fresh) { Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } else { $null }
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{
    startedAtUtc=$started.ToString('o'); verifiedAtUtc=[DateTime]::UtcNow.ToString('o')
    executable=$Executable; assemblySha256=$testedHash; assemblyUnchanged=$assemblyUnchanged
    exitCode=$process.ExitCode; graphicsApi=$GraphicsApi; noGraphics=[bool]$NoGraphics
    resolution='1280x720'; qaFlag=$qaFlag; passed=[bool]$passed; fresh=$fresh
    checks=@($report.checks).Count; report=$reportPath
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Output ('FAIL: ' + $_.name + ' ' + $_.value + ' ' + $_.detail) }
if (-not $passed) { throw ('Vehicle impact QA failed. See ' + $reportPath) }
Write-Output ('Vehicle impact QA passed: ' + @($report.checks).Count + ' checks. ' + $reportPath)
