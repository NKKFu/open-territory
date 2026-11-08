param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\OverallMap-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\overall-map\native'),
    [ValidateSet('overall','map','neighborhood','frontage','reference','prop-grounding','outdoor-destruction')]
    [string]$Probe = 'overall',
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$mapAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$mapHash = (Get-FileHash -LiteralPath $mapAssembly).Hash
$mapStarted = [DateTime]::UtcNow
$mapFlag = if ($Probe -eq 'overall') { '--city-overall-map-qa' } else { '--city-' + $Probe + '-qa' }
$mapFile = if ($Probe -eq 'overall') { 'overall-map-qa.json' } else { $Probe + '-qa.json' }
$mapArguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800', $mapFlag,
    '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($GraphicsApi -ne 'default') { $mapArguments += ('-force-' + $GraphicsApi) }
$mapProcess = Start-Process -FilePath $Executable -ArgumentList $mapArguments -WindowStyle Hidden -PassThru
$mapDeadline = [DateTime]::UtcNow.AddMinutes(9)
while (-not $mapProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $mapDeadline) { $mapProcess.Kill(); throw 'A verificação do mapa excedeu nove minutos.' }
}
$mapReportPath = Join-Path $ReportDirectory $mapFile
if (-not (Test-Path -LiteralPath $mapReportPath) -or (Get-Item -LiteralPath $mapReportPath).LastWriteTimeUtc -lt $mapStarted) {
    throw ('Relatório novo ausente. Saída: ' + $mapProcess.ExitCode + '. Consulte ' + (Join-Path $ReportDirectory 'player.log'))
}
$mapReport = Get-Content -LiteralPath $mapReportPath -Raw | ConvertFrom-Json
$mapChanged = (Get-FileHash -LiteralPath $mapAssembly).Hash -ne $mapHash
$mapGraphics = $mapReport.graphicsApi
if (-not $mapGraphics) { $mapGraphics = $mapReport.graphics }
if (-not $mapGraphics) {
    $mapGraphicsLine = Select-String -LiteralPath (Join-Path $ReportDirectory 'player.log') -Pattern 'Version:.*Direct3D' | Select-Object -First 1
    if ($mapGraphicsLine) { $mapGraphics = $mapGraphicsLine.Line.Trim() }
}
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o'); executable = $Executable; assemblySha256 = $mapHash
    assemblyChangedDuringRun = $mapChanged; processExitCode = $mapProcess.ExitCode
    passed = [bool]$mapReport.passed -and -not $mapChanged -and $mapProcess.ExitCode -eq 0
    checks = $mapReport.checks.Count; graphics = $mapGraphics
    arguments = $mapArguments; report = $mapReportPath
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Output ($Probe + ': ' + @($mapReport.checks | Where-Object passed).Count + '/' + $mapReport.checks.Count + '; saída ' + $mapProcess.ExitCode)
if ($mapProcess.ExitCode -ne 0 -or -not $mapReport.passed -or $mapChanged) {
    $mapReport.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte ' + $mapReportPath)
}
