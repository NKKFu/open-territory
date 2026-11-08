param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\doors\native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default',
    [switch]$ApartmentsOnly
)
$ErrorActionPreference = 'Stop'
$doorExe = (Resolve-Path -LiteralPath $Executable).Path
$doorDir = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $doorDir -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $doorExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$playerArgs = @('-batchmode','--city-door-qa','--city-report-dir',('"'+$doorDir+'"'),'-screen-fullscreen','0','-screen-width','1200','-screen-height','900','-logFile',('"'+(Join-Path $doorDir 'door-qa.log')+'"'))
if ($ApartmentsOnly) { $playerArgs += '--city-apartment-doors-only' }
if ($GraphicsApi -ne 'default') { $playerArgs += ('-force-' + $GraphicsApi) }
$process = Start-Process -FilePath $doorExe -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Host ('QA das portas, PID '+$process.Id)
if (-not $process.WaitForExit(600000)) { $process.Kill(); throw 'A validação das portas excedeu o limite.' }
$process.WaitForExit()
$path = Join-Path $doorDir 'door-qa.json'
if (-not (Test-Path -LiteralPath $path)) { throw 'O player não gerou o relatório das portas.' }
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $path).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed -and (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $assemblyHash
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$passed;checks=@($report.checks).Count;executable=$doorExe;exitCode=$process.ExitCode;graphicsApi=$GraphicsApi;apartmentsOnly=[bool]$ApartmentsOnly;assemblySha256=$assemblyHash;report=$path} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $doorDir 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA das portas falhou. Consulte '+$path) }
Write-Host ('Portas aprovadas: '+@($report.checks).Count+' verificações. Capturas: '+$doorDir)
