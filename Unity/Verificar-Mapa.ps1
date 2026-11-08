param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\map-audit\native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default',
    [ValidateRange(60,900)][int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$mapExe = (Resolve-Path -LiteralPath $Executable).Path
$mapDir = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $mapDir -Force | Out-Null
$started = [DateTime]::UtcNow
$mapArgs = @('-batchmode','--city-map-qa','--city-report-dir',('"'+$mapDir+'"'),'-logFile',('"'+(Join-Path $mapDir 'map-qa.log')+'"'))
if ($GraphicsApi -ne 'default') { $mapArgs += ('-force-'+$GraphicsApi) }
$process = Start-Process -FilePath $mapExe -ArgumentList $mapArgs -WindowStyle Hidden -PassThru
Write-Host ('Auditoria nativa do mapa, PID '+$process.Id)
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw ('A auditoria excedeu '+$TimeoutSeconds+' segundos.') }
$path = Join-Path $mapDir 'map-qa.json'
if (-not (Test-Path -LiteralPath $path)) { throw 'O player não gerou a auditoria do mapa.' }
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $path).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $mapExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$verification = [ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$passed;checks=@($report.checks).Count;exitCode=$process.ExitCode;executable=$mapExe;graphicsApi=$GraphicsApi;arguments=$mapArgs;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;report=$path} | ConvertTo-Json -Depth 4
$verification | Set-Content -LiteralPath (Join-Path $mapDir 'verification.json') -Encoding utf8
if ($mapDir -eq (Join-Path $PSScriptRoot 'Reports\map-audit\native')) {
    $verification | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Reports\map-audit\verification.json') -Encoding utf8
}
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('Auditoria do mapa falhou. Consulte '+$path) }
Write-Host ('Mapa aprovado: '+@($report.checks).Count+' verificações. Capturas: '+$mapDir)
