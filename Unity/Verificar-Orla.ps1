param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries\Bayfront-20261108-Current\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\waterfront\native-current'),
    [ValidateSet('Default','D3D11','D3D12')][string]$GraphicsApi = 'Default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly).Hash
$started = [DateTime]::UtcNow
$arguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-waterfront-qa',
    '--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($GraphicsApi -eq 'D3D11') { $arguments += '-force-d3d11' }
if ($GraphicsApi -eq 'D3D12') { $arguments += '-force-d3d12' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(6)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação da orla excedeu seis minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'waterfront-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw 'Relatório novo da orla ausente.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$changed = (Get-FileHash -LiteralPath $assembly).Hash -ne $hash
[ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$Executable;assemblySha256=$hash;
    assemblyChangedDuringRun=$changed;processExitCode=$process.ExitCode;passed=[bool]$report.passed -and -not $changed -and $process.ExitCode -eq 0;
    checks=$report.checks.Count;routeSamples=$report.routeSamples;graphics=$report.graphicsApi;arguments=$arguments;report=$reportPath} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Host ('Orla: '+@($report.checks | Where-Object passed).Count+'/'+$report.checks.Count+' verificações; '+$report.routeSamples+' amostras; saída '+$process.ExitCode)
if($process.ExitCode -ne 0 -or -not $report.passed -or $changed){
    $report.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte '+$reportPath)
}
