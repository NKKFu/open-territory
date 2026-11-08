param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\english\native')
)
$ErrorActionPreference = 'Stop'
$englishExe = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $englishExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$englishReport = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $englishReport -Force | Out-Null
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $englishExe -ArgumentList @('--city-english-qa','--city-report-dir',('"'+$englishReport+'"'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','800','-logFile',('"'+(Join-Path $englishReport 'player.log')+'"')) -WindowStyle Hidden -PassThru
Write-Host ('English acceptance started, PID '+$process.Id)
if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'English acceptance timed out.' }
$reportPath = Join-Path $englishReport 'english-qa.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'No English acceptance report was written.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$process.Refresh()
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'The executable changed during English acceptance.' }
$passed = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -ge $started -and $process.ExitCode -eq 0 -and $report.passed
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$englishExe;assemblySha256=$assemblyHash;exitCode=$process.ExitCode;passed=$passed;checks=@($report.checks).Count;resolution='1280x800';report=$reportPath} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $englishReport 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FAILED: '+$_.name+' '+$_.detail) }
if (-not $passed) { throw ('English acceptance failed: '+$reportPath) }
Write-Host ('English acceptance passed: '+@($report.checks).Count+' checks.')
