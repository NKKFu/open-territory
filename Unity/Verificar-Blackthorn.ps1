param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries/BlackthornLowPolyShop-20261108/BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports/blackthorn-lowpoly-shop/native'),
    [ValidateSet('default','d3d11')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$blackthornExe = (Resolve-Path -LiteralPath $Executable).Path
$blackthornReport = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $blackthornReport -Force | Out-Null
$blackthornAssembly = Join-Path (Split-Path -Parent $blackthornExe) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
$blackthornHash = (Get-FileHash -LiteralPath $blackthornAssembly).Hash
$blackthornStarted = [DateTime]::UtcNow
$blackthornArgs = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-blackthorn-qa','--city-report-dir',('"'+$blackthornReport+'"'),'-logFile',('"'+(Join-Path $blackthornReport 'player.log')+'"'))
if ($GraphicsApi -eq 'd3d11') { $blackthornArgs += '-force-d3d11' }
$blackthornProcess = Start-Process -FilePath $blackthornExe -ArgumentList $blackthornArgs -WindowStyle Hidden -PassThru
Write-Output ('Blackthorn native QA PID '+$blackthornProcess.Id)
$blackthornDeadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $blackthornProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $blackthornDeadline) { $blackthornProcess.Kill(); throw 'Blackthorn native QA exceeded four minutes.' }
}
$blackthornReportPath = Join-Path $blackthornReport 'blackthorn-qa.json'
if (-not (Test-Path -LiteralPath $blackthornReportPath) -or (Get-Item -LiteralPath $blackthornReportPath).LastWriteTimeUtc -lt $blackthornStarted) { throw 'Current native report missing; inspect player.log.' }
$blackthornResult = Get-Content -LiteralPath $blackthornReportPath -Raw | ConvertFrom-Json
$blackthornChanged = (Get-FileHash -LiteralPath $blackthornAssembly).Hash -ne $blackthornHash
$blackthornExit = $blackthornProcess.ExitCode
[ordered]@{
    passed = [bool]$blackthornResult.passed -and -not $blackthornChanged -and $blackthornExit -eq 0
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $blackthornExe
    assemblySha256 = $blackthornHash
    assemblyChangedDuringRun = $blackthornChanged
    processExitCode = $blackthornExit
    graphics = $blackthornResult.graphics
    resolution = '1280x800; captures 1600x1000'
    checks = $blackthornResult.checks.Count
    routeSamples = $blackthornResult.routeSamples
    arguments = $blackthornArgs
    report = $blackthornReportPath
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $blackthornReport 'verification.json') -Encoding utf8
Write-Output ('Blackthorn: '+@($blackthornResult.checks | Where-Object passed).Count+'/'+$blackthornResult.checks.Count+' checks; exit '+$blackthornExit)
if (-not $blackthornResult.passed -or $blackthornChanged -or $blackthornExit -ne 0) {
    $blackthornResult.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Native acceptance failed. Inspect '+$blackthornReportPath)
}
