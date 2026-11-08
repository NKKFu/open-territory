param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\firearms\native'),
    [switch]$Direct3D11
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$started = [DateTime]::UtcNow
$arguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-firearm-injury-qa','--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($Direct3D11) { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Host ('Ferimentos: QA iniciado, PID ' + $process.Id)
$deadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação excedeu quatro minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'firearm-injury-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente: ' + $ReportDirectory) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$process.Refresh()
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $hash) { throw 'O assembly mudou durante a verificação.' }
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$Executable;assemblySha256=$hash;graphicsApi=$(if($Direct3D11){'Direct3D11'}else{'Player default'});exitCode=$process.ExitCode;passed=($process.ExitCode -eq 0 -and $report.passed);checks=@($report.checks).Count;report=$reportPath;resolution='1280x800';captures=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if ($process.ExitCode -ne 0 -or -not $report.passed) { throw ('QA de ferimentos falhou: ' + $reportPath) }
Write-Host ('Ferimentos aprovados: ' + @($report.checks).Count + ' verificações. ' + $reportPath)
