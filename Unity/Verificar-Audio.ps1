param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\audio\native')
)
$ErrorActionPreference = 'Stop'
$audioExe = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $audioExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$audioReport = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $audioReport -Force | Out-Null
$started = [DateTime]::UtcNow
# A normal native player is required: batch/headless mode can disable the DSP device.
$process = Start-Process -FilePath $audioExe -ArgumentList @('--city-audio-qa','--city-report-dir',('"'+$audioReport+'"'),'-screen-fullscreen','0','-screen-width','960','-screen-height','600','-logFile',('"'+(Join-Path $audioReport 'audio-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('QA de áudio nativo iniciado, PID '+$process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'O teste de áudio não terminou em três minutos.' }
$reportPath = Join-Path $audioReport 'audio-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O player não gerou o relatório de áudio.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$process.Refresh()
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação de áudio.' }
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$audioExe;assemblySha256=$assemblyHash;exitCode=$process.ExitCode;passed=$passed;checks=@($report.checks).Count;report=$reportPath} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Reports\audio\verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA de áudio falhou. Consulte '+$reportPath) }
Write-Host ('Áudio aprovado: '+@($report.checks).Count+' verificações. Captura: '+(Join-Path $audioReport 'previa-no-jogo.wav'))
