param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$referenceExe = $Executable
$referenceDir = if ($ReportDirectory) { [IO.Path]::GetFullPath($ReportDirectory) } else { Join-Path $PSScriptRoot 'Reports\reference-map\native' }
New-Item -ItemType Directory -Path $referenceDir -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $referenceExe -ArgumentList @('-batchmode','--city-reference-qa','--city-report-dir',('"'+$referenceDir+'"'),'-logFile',('"'+(Join-Path $referenceDir 'reference-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('Mapa traçado: QA nativo iniciado, PID '+$process.Id)
if (-not $process.WaitForExit(240000)) { $process.Kill(); throw 'O teste do mapa não terminou em quatro minutos.' }
$reportPath = Join-Path $referenceDir 'reference-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O executável não gerou o relatório do novo mapa.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
$assembly = Join-Path (Split-Path -Parent $referenceExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$verificationPath = if ($ReportDirectory) { Join-Path $referenceDir 'verification.json' } else { Join-Path $PSScriptRoot 'Reports\verification.json' }
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o');layout=$report.layout;executable=$referenceExe;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;passed=$passed;checks=@($report.checks).Count;report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath $verificationPath -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA do novo mapa falhou. Consulte '+$reportPath) }
Write-Host ('Mapa traçado aprovado: '+@($report.checks).Count+' verificações. Capturas: '+$referenceDir)
