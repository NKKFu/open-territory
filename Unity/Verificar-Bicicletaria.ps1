param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\BikeShop-20261108\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\bike-shop\native'),
    [ValidateSet('default', 'd3d11', 'd3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$bikeAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$bikeHash = (Get-FileHash -LiteralPath $bikeAssembly).Hash
$bikeStarted = [DateTime]::UtcNow
$bikeArguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-bike-shop-qa','--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($GraphicsApi -eq 'd3d11') { $bikeArguments += '-force-d3d11' }
if ($GraphicsApi -eq 'd3d12') { $bikeArguments += '-force-d3d12' }
$bikeProcess = Start-Process -FilePath $Executable -ArgumentList $bikeArguments -WindowStyle Hidden -PassThru
Write-Output ('Bicycle shop native QA: PID '+$bikeProcess.Id)
$bikeDeadline = [DateTime]::UtcNow.AddMinutes(5)
while (-not $bikeProcess.WaitForExit(1000)) { if ([DateTime]::UtcNow -gt $bikeDeadline) { $bikeProcess.Kill(); throw 'Bicycle shop verification timed out.' } }
$bikeProcess.Refresh()
$bikeReportPath = Join-Path $ReportDirectory 'bike-shop-qa.json'
if (-not (Test-Path -LiteralPath $bikeReportPath) -or (Get-Item -LiteralPath $bikeReportPath).LastWriteTimeUtc -lt $bikeStarted) { throw 'Current bicycle shop report missing. See player.log.' }
$bikeReport = Get-Content -LiteralPath $bikeReportPath -Raw | ConvertFrom-Json
$bikeLog = Get-Content -LiteralPath (Join-Path $ReportDirectory 'player.log') -Raw
$bikeApi = if ($bikeLog -match 'Direct3D\s*12|D3D12') { 'Direct3D 12' } elseif ($bikeLog -match 'Direct3D\s*11|D3D11') { 'Direct3D 11' } else { 'See player.log' }
$bikeChanged = (Get-FileHash -LiteralPath $bikeAssembly).Hash -ne $bikeHash
$bikePassed = [bool]$bikeReport.passed -and -not $bikeChanged -and $bikeProcess.ExitCode -eq 0
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$Executable;assemblySha256=$bikeHash;assemblyChangedDuringRun=$bikeChanged;
    passed=$bikePassed;exitCode=$bikeProcess.ExitCode;checks=$bikeReport.checks.Count;routeSamples=$bikeReport.routeSamples;report=$bikeReportPath;
    graphics=$bikeApi;requestedGraphicsApi=$GraphicsApi;arguments=$bikeArguments} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Output ('Bicycle shop: '+@($bikeReport.checks | Where-Object passed).Count+'/'+$bikeReport.checks.Count+' checks; exit '+$bikeProcess.ExitCode)
if (-not $bikePassed) { $bikeReport.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize; throw 'Bicycle shop verification failed.' }
