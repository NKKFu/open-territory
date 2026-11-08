param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$raysReportDir = Join-Path $PSScriptRoot ('Reports\sunrays\native-' + $GraphicsApi)
New-Item -ItemType Directory -Path $raysReportDir -Force | Out-Null
$raysAssemblyPath = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$raysAssemblyHash = (Get-FileHash -LiteralPath $raysAssemblyPath -Algorithm SHA256).Hash
$raysStarted = [DateTime]::UtcNow
$raysArgs = @('-batchmode','--city-sunrays-qa','--city-report-dir',('"'+$raysReportDir+'"'),'-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+(Join-Path $raysReportDir 'player.log')+'"'))
if ($GraphicsApi -ne 'default') { $raysArgs += '-force-' + $GraphicsApi }
$raysProcess = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -ArgumentList $raysArgs -WindowStyle Hidden -PassThru
Write-Host ('Raios solares: QA nativo iniciado, PID ' + $raysProcess.Id)
if (-not $raysProcess.WaitForExit(60000)) { $raysProcess.Kill(); throw 'O teste de raios solares excedeu um minuto.' }
$raysReportPath = Join-Path $raysReportDir 'sunrays-qa.json'
if (-not (Test-Path -LiteralPath $raysReportPath)) { throw ('Relatório não gerado. Consulte ' + (Join-Path $raysReportDir 'player.log')) }
$raysReport = Get-Content -LiteralPath $raysReportPath -Raw | ConvertFrom-Json
$raysFresh = (Get-Item -LiteralPath $raysReportPath).LastWriteTimeUtc -ge $raysStarted
$raysSameBuild = $raysAssemblyHash -eq (Get-FileHash -LiteralPath $raysAssemblyPath -Algorithm SHA256).Hash
$raysPassed = $raysFresh -and $raysSameBuild -and $raysProcess.ExitCode -eq 0 -and $raysReport.passed -and $raysReport.completed
[ordered]@{
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = $Executable
    assemblySha256 = $raysAssemblyHash
    assemblyUnchangedDuringTest = $raysSameBuild
    graphicsApi = $raysReport.graphicsApi
    passed = $raysPassed
    exitCode = $raysProcess.ExitCode
    checks = @($raysReport.checks).Count
    report = $raysReportPath
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $raysReportDir 'verification.json') -Encoding utf8
$raysReport.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if (-not $raysPassed) { throw ('QA de raios solares falhou. Consulte ' + $raysReportPath) }
Write-Host ('Raios solares aprovados: ' + @($raysReport.checks).Count + ' verificações. Capturas: ' + $raysReportDir)
