param(
    [string]$Executable = (Join-Path $PSScriptRoot 'Deliveries/ClothingShop-20261108/BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports/clothing-shop/native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$clothingAssembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
$clothingHash = (Get-FileHash -LiteralPath $clothingAssembly).Hash
$clothingStarted = [DateTime]::UtcNow
$clothingArguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-clothing-qa','--city-report-dir',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"'))
if ($GraphicsApi -ne 'default') { $clothingArguments += '-force-' + $GraphicsApi }
$clothingProcess = Start-Process -FilePath $Executable -ArgumentList $clothingArguments -WindowStyle Hidden -PassThru
$clothingDeadline = [DateTime]::UtcNow.AddMinutes(6)
while (-not $clothingProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $clothingDeadline) { $clothingProcess.Kill(); throw 'A verificação de roupas excedeu seis minutos.' }
}
$clothingReportPath = Join-Path $ReportDirectory 'clothing-qa.json'
if (-not (Test-Path -LiteralPath $clothingReportPath) -or (Get-Item -LiteralPath $clothingReportPath).LastWriteTimeUtc -lt $clothingStarted) { throw 'Relatório novo de roupas ausente.' }
$clothingReport = Get-Content -LiteralPath $clothingReportPath -Raw | ConvertFrom-Json
$clothingChanged = (Get-FileHash -LiteralPath $clothingAssembly).Hash -ne $clothingHash
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$clothingHash; assemblyChangedDuringRun=$clothingChanged;
    processExitCode=$clothingProcess.ExitCode; passed=[bool]$clothingReport.passed -and -not $clothingChanged -and $clothingProcess.ExitCode -eq 0;
    checks=$clothingReport.checks.Count; routeSamples=$clothingReport.routeSamples; graphics=$clothingReport.graphicsApi; arguments=$clothingArguments; report=$clothingReportPath } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
Write-Output ('Roupas: '+@($clothingReport.checks | Where-Object passed).Count+'/'+$clothingReport.checks.Count+' verificações; saída '+$clothingProcess.ExitCode)
if ($clothingProcess.ExitCode -ne 0 -or -not $clothingReport.passed -or $clothingChanged) {
    $clothingReport.checks | Where-Object { -not $_.passed } | Format-Table name,detail -AutoSize
    throw ('Falha na verificação. Consulte '+$clothingReportPath)
}
