param(
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\map-revision\native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
if ($GraphicsApi -ne 'default' -and -not $PSBoundParameters.ContainsKey('ReportDirectory')) { $ReportDirectory = Join-Path $PSScriptRoot ('Reports\map-revision\native-'+$GraphicsApi) }
$reviewExe = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
$reviewAssembly = Join-Path (Split-Path -Parent $reviewExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$reviewReport = Join-Path $ReportDirectory 'street-review-qa.json'
$reviewStarted = [DateTime]::UtcNow
$reviewHash = (Get-FileHash -LiteralPath $reviewAssembly -Algorithm SHA256).Hash
$reviewArguments = @('-batchmode','--city-street-review-qa','--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($GraphicsApi -ne 'default') { $reviewArguments += '-force-'+$GraphicsApi }
$reviewProcess = Start-Process -FilePath $reviewExe -ArgumentList $reviewArguments -WindowStyle Hidden -PassThru
$null = $reviewProcess.Handle
Write-Host ('Revisão nativa de ruas e mobiliário, PID '+$reviewProcess.Id)
if (-not $reviewProcess.WaitForExit(360000)) { $reviewProcess.Kill(); throw 'A revisão nativa excedeu seis minutos.' }
$reviewProcess.WaitForExit()
if (-not (Test-Path -LiteralPath $reviewReport)) { throw 'O player não produziu o relatório da revisão.' }
$reviewData = Get-Content -LiteralPath $reviewReport -Raw | ConvertFrom-Json
$reviewFresh = (Get-Item -LiteralPath $reviewReport).LastWriteTimeUtc -ge $reviewStarted
$reviewPassed = $reviewFresh -and $reviewProcess.ExitCode -eq 0 -and $reviewData.passed
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); startedAtUtc=$reviewStarted.ToString('o'); executable=$reviewExe; assemblySha256=$reviewHash; sourceHash=$reviewData.sourceHash; requestedGraphicsApi=$GraphicsApi; graphicsApi=$reviewData.graphicsApi; exitCode=$reviewProcess.ExitCode; passed=$reviewPassed; checks=@($reviewData.checks).Count; report=$reviewReport } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$reviewData.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.detail) }
if (-not $reviewPassed) { throw ('A revisão do mapa falhou. Consulte '+$reviewReport) }
Write-Host ('Mapa revisado: '+@($reviewData.checks).Count+' verificações aprovadas. '+$reviewReport)
