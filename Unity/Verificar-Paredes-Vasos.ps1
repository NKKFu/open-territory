param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries/WallPlanters-20261108/BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports/wall-planters/native'),
    [ValidateSet('default','d3d11')][string]$GraphicsApi = 'default',
    [ValidateSet('walls','driving','impacts')][string]$Scenario = 'walls'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$flag = @{walls='--city-wall-planter-qa';driving='--city-driving-controls-qa';impacts='--city-vehicle-impact-qa'}[$Scenario]
$launchArguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    $flag,'--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($GraphicsApi -eq 'd3d11') { $launchArguments += '-force-d3d11' }
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Output ('Native '+$Scenario+' QA started: PID '+$process.Id)
try {
    while (-not $process.WaitForExit(1000)) {
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 210) { $process.Kill(); throw 'Native vehicle QA exceeded 210 seconds.' }
    }
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $reportPath = Join-Path $ReportDirectory 'driving-qa.json'
    $fresh = (Test-Path -LiteralPath $reportPath) -and (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
    $report = if ($fresh) { Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } else { $null }
    $unchanged = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $hash
    $passed = $fresh -and $unchanged -and $exitCode -eq 0 -and $report.passed
    [ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');startedAtUtc=$started.ToString('o');
        executable=$Executable;assemblySha256=$hash;assemblyUnchanged=$unchanged;exitCode=$exitCode;graphicsApi=$GraphicsApi;
        arguments=$launchArguments;freshReport=$fresh;passed=$passed;checks=@($report.checks).Count;report=$reportPath} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
    $report.checks | Where-Object {-not $_.passed} | ForEach-Object {Write-Output ('FAIL '+$_.name+' value='+$_.value+' '+$_.detail)}
    if (-not $passed) { throw ('Native '+$Scenario+' QA failed: exit='+$exitCode+'; report='+$reportPath) }
    Write-Output ('Native '+$Scenario+' QA passed: '+@($report.checks).Count+' checks, exit='+$exitCode)
}
finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
