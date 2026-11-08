param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$ReportDirectory,
    [ValidateSet('Default','D3D11','D3D12')][string]$GraphicsApi='Default'
)
$ErrorActionPreference='Stop'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory=[IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly=Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash=(Get-FileHash -LiteralPath $assembly).Hash
$started=[DateTime]::UtcNow
$arguments=@('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-linear-park-qa',
 '--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if($GraphicsApi -eq 'D3D11'){$arguments+='-force-d3d11'}
if($GraphicsApi -eq 'D3D12'){$arguments+='-force-d3d12'}
$process=Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline=[DateTime]::UtcNow.AddMinutes(8)
while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){$process.Kill();throw 'A verificação do parque excedeu oito minutos.'}}
$reportPath=Join-Path $ReportDirectory 'linear-park-qa.json'
if(-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started){throw 'Relatório novo do parque ausente.'}
$report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$changed=(Get-FileHash -LiteralPath $assembly).Hash -ne $hash
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$Executable;assemblySha256=$hash;
 assemblyChangedDuringRun=$changed;processExitCode=$process.ExitCode;passed=[bool]$report.passed -and -not $changed -and $process.ExitCode -eq 0;
 checks=$report.checks.Count;routeSamples=$report.samples;graphics=$report.graphicsApi;arguments=$arguments;report=$reportPath} |
 ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Parque: '+@($report.checks | Where-Object passed).Count+'/'+$report.checks.Count+' verificações; '+$report.samples+' amostras; saída '+$process.ExitCode)
if($process.ExitCode -ne 0 -or -not $report.passed -or $changed){$report.checks | Where-Object {-not $_.passed} | Format-Table name,detail -AutoSize;throw ('Falha na verificação. Consulte '+$reportPath)}
