param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\CrimeResponse\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\crime-response\native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default',
    [switch]$NoGraphics
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$testedHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$launchArguments = @('-batchmode', '--city-crime-response-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'crime-response-qa.log') + '"'))
if ($NoGraphics) { $launchArguments += '-nographics' }
if ($GraphicsApi -eq 'd3d11') { $launchArguments += '-force-d3d11' }
if ($GraphicsApi -eq 'd3d12') { $launchArguments += '-force-d3d12' }
$process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
Write-Host ('Crime response QA: PID ' + $process.Id + ', ' + $GraphicsApi)
if (-not $process.WaitForExit(240000)) { $process.Kill(); throw 'O teste de crimes não terminou em quatro minutos.' }
$process.Refresh()
$reportPath = Join-Path $ReportDirectory 'crime-response-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw ('O executável não gerou relatório. Exit: ' + $process.ExitCode) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$assemblyUnchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $testedHash
$passed = $fresh -and $assemblyUnchanged -and $process.ExitCode -eq 0 -and $report.passed -and $report.completed
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); executable = $Executable; assemblySha256 = $testedHash
    graphicsApi = $GraphicsApi; noGraphics = [bool]$NoGraphics; exitCode = $process.ExitCode
    fresh = $fresh; assemblyUnchanged = $assemblyUnchanged; completed = $report.completed
    passed = $passed; checks = @($report.checks).Count; report = $reportPath
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $passed) { throw ('QA de crimes falhou. Consulte ' + $reportPath) }
Write-Host ('Crimes aprovados: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
