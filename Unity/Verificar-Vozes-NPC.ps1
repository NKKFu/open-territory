param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\audio\npc-native')
)
$ErrorActionPreference = 'Stop'
$npcExe = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $npcExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$npcReport = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $npcReport -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $npcExe -ArgumentList @('--city-npc-voice-qa','--city-report-dir',('"'+$npcReport+'"'),'-screen-fullscreen','0','-screen-width','960','-screen-height','600','-logFile',('"'+(Join-Path $npcReport 'npc-voice-qa.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('QA de vozes de NPCs iniciado, PID '+$process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'O teste de vozes não terminou em três minutos.' }
$reportPath = Join-Path $npcReport 'npc-voice-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'O player não gerou o relatório de vozes.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$fresh = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started
$process.Refresh()
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação de vozes.' }
$passed = $fresh -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$npcExe;assemblySha256=$assemblyHash;exitCode=$process.ExitCode;passed=$passed;checks=@($report.checks).Count;report=$reportPath} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $npcReport 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.value+' '+$_.detail) }
if (-not $passed) { throw ('QA de vozes falhou. Consulte '+$reportPath) }
Write-Host ('Vozes aprovadas: '+@($report.checks).Count+' verificações. Captura: '+(Join-Path $npcReport 'conversas-e-reacoes-no-jogo.wav'))
