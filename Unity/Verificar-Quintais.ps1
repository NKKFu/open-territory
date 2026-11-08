$ErrorActionPreference = 'Stop'
$yardExe = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
$yardDir = Join-Path $PSScriptRoot 'Reports\neighborhood\native'
New-Item -ItemType Directory -Path $yardDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $yardExe -ArgumentList @('-batchmode','--city-neighborhood-qa','--city-report-dir',('"'+$yardDir+'"'),'-logFile',('"'+(Join-Path $yardDir 'neighborhood-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('QA de quintais, jardins e estacionamentos, PID '+$process.Id)
if (-not $process.WaitForExit(240000)) { $process.Kill(); throw 'A validação dos quintais excedeu o limite.' }
$path = Join-Path $yardDir 'neighborhood-qa.json'
if (-not (Test-Path -LiteralPath $path)) { throw 'O player não gerou o relatório dos quintais.' }
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $path).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $yardExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$passed;checks=@($report.checks).Count;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;report=$path} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Reports\neighborhood\verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA dos quintais falhou. Consulte '+$path) }
Write-Host ('Quintais aprovados: '+@($report.checks).Count+' verificações. Capturas: '+$yardDir)
