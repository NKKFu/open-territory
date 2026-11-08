$ErrorActionPreference = 'Stop'
$frontageExe = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
$frontageDir = Join-Path $PSScriptRoot 'Reports\frontages\native'
New-Item -ItemType Directory -Path $frontageDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $frontageExe -ArgumentList @('-batchmode','--city-frontage-qa','--city-report-dir',('"'+$frontageDir+'"'),'-logFile',('"'+(Join-Path $frontageDir 'frontage-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('QA de fachadas e acessos, PID '+$process.Id)
if (-not $process.WaitForExit(210000)) { $process.Kill(); throw 'A validação das fachadas excedeu o limite.' }
$path = Join-Path $frontageDir 'frontage-qa.json'
if (-not (Test-Path -LiteralPath $path)) { throw 'O player não gerou o relatório de acessos.' }
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $path).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $frontageExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$passed;checks=@($report.checks).Count;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;report=$path} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Reports\frontages\verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA das fachadas falhou. Consulte '+$path) }
Write-Host ('Acessos aprovados: '+@($report.checks).Count+' verificações. Capturas: '+$frontageDir)
