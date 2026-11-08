param(
    [string]$Executable = (Join-Path $PSScriptRoot '../../OpenTerritory/Builds/SteeringWheels-20261108-B/BaiaDoSol.exe'),
    [ValidateSet('default','d3d11')][string]$GraphicsApi = 'default',
    [switch]$ControlsOnly
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$steeringReportDirectory = Join-Path $PSScriptRoot ($(if ($ControlsOnly) { 'controls-' } else { 'native-' }) + $GraphicsApi)
New-Item -ItemType Directory -Path $steeringReportDirectory -Force | Out-Null
$steeringAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
$steeringAssemblyHash = (Get-FileHash -LiteralPath $steeringAssembly).Hash
$steeringStarted = [DateTime]::UtcNow
$steeringArguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    $(if ($ControlsOnly) { '--city-driving-controls-qa' } else { '--city-driving-qa' }),'--city-report-dir',('"' + $steeringReportDirectory + '"'),
    '-logFile',('"' + (Join-Path $steeringReportDirectory 'player.log') + '"'))
if ($GraphicsApi -eq 'd3d11') { $steeringArguments += '-force-d3d11' }
$steeringProcess = Start-Process -FilePath $Executable -ArgumentList $steeringArguments -WorkingDirectory (Split-Path -Parent $Executable) -WindowStyle Hidden -PassThru
Write-Host ('Native driving QA PID ' + $steeringProcess.Id)
while (-not $steeringProcess.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $steeringStarted).TotalSeconds -gt 210) {
        $steeringProcess.Kill()
        throw 'Driving QA exceeded 210 seconds.'
    }
}
$steeringReportPath = Join-Path $steeringReportDirectory 'driving-qa.json'
$steeringFresh = (Test-Path -LiteralPath $steeringReportPath) -and (Get-Item -LiteralPath $steeringReportPath).LastWriteTimeUtc -ge $steeringStarted
$steeringReport = if ($steeringFresh) { Get-Content -LiteralPath $steeringReportPath -Raw | ConvertFrom-Json } else { $null }
$steeringAssemblyUnchanged = (Get-FileHash -LiteralPath $steeringAssembly).Hash -eq $steeringAssemblyHash
$steeringScopeMatched = [bool]$steeringReport.controlsOnly -eq [bool]$ControlsOnly
$steeringPassed = $steeringFresh -and $steeringAssemblyUnchanged -and $steeringScopeMatched -and $steeringProcess.ExitCode -eq 0 -and $steeringReport.passed
$steeringVerification = [ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $Executable
    assemblySha256 = $steeringAssemblyHash
    assemblyUnchanged = $steeringAssemblyUnchanged
    startedUtc = $steeringStarted.ToString('o')
    exitCode = $steeringProcess.ExitCode
    graphicsApi = $GraphicsApi
    controlsOnly = [bool]$ControlsOnly
    scopeMatched = $steeringScopeMatched
    arguments = $steeringArguments
    reportFresh = $steeringFresh
    report = $steeringReportPath
    passed = [bool]$steeringPassed
    checks = @($steeringReport.checks).Count
    failures = @($steeringReport.checks | Where-Object { -not $_.passed })
    errors = @($steeringReport.errors)
}
$steeringVerification | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $steeringReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Passed: ' + $steeringPassed + '; checks: ' + $steeringVerification.checks + '; exit: ' + $steeringProcess.ExitCode)
$steeringVerification.failures | Format-Table name,value,detail -AutoSize
if (-not $steeringPassed) { throw ('Native driving QA failed: ' + $steeringReportPath) }
