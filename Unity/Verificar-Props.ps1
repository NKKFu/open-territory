param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\prop-grounding\native'),
    [ValidateSet('default','d3d11','d3d12')][string]$GraphicsApi = 'default'
)
$ErrorActionPreference = 'Stop'
$propExe = (Resolve-Path -LiteralPath $Executable).Path
$propAssembly = Join-Path (Split-Path -Parent $propExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$propReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$propStarted = [DateTime]::UtcNow
$propHash = (Get-FileHash -LiteralPath $propAssembly -Algorithm SHA256).Hash
$propArguments = @('-batchmode','--city-prop-grounding-qa','--city-report-dir',('"'+$propReportDirectory+'"'),'-logFile',('"'+(Join-Path $propReportDirectory 'player.log')+'"'))
if ($GraphicsApi -ne 'default') { $propArguments += '-force-'+$GraphicsApi }
$propProcess = Start-Process -FilePath $propExe -ArgumentList $propArguments -WindowStyle Hidden -PassThru
$null = $propProcess.Handle
Write-Host ('Validando mobiliário no player, PID '+$propProcess.Id)
while (-not $propProcess.WaitForExit(1000)) {
    if (([DateTime]::UtcNow-$propStarted).TotalSeconds -gt 180) { $propProcess.Kill(); throw 'A validação de props excedeu três minutos.' }
}
$propProcess.WaitForExit()
$propReport = Join-Path $propReportDirectory 'prop-grounding-qa.json'
$propReportWritten = Test-Path -LiteralPath $propReport
$propData = if ($propReportWritten) { Get-Content -LiteralPath $propReport -Raw | ConvertFrom-Json } else { [pscustomobject]@{passed=$false; checks=@(); props=0; samples=0; sourceHash=$null; graphicsApi=$null} }
$propPassed = $propReportWritten -and (Get-Item -LiteralPath $propReport).LastWriteTimeUtc -ge $propStarted -and $propProcess.ExitCode -eq 0 -and $propData.passed
[ordered]@{
    verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); startedAtUtc=$propStarted.ToString('o'); executable=$propExe
    assemblySha256=$propHash; sourceHash=$propData.sourceHash; requestedGraphicsApi=$GraphicsApi; graphicsApi=$propData.graphicsApi
    exitCode=$propProcess.ExitCode; passed=$propPassed; reportWritten=$propReportWritten; props=$propData.props; samples=$propData.samples
    checks=@($propData.checks).Count; report=$propReport
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $propReportDirectory 'verification.json') -Encoding utf8
$propData.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: '+$_.name+' '+$_.detail) }
if (-not $propPassed) { throw ('A validação de props falhou (exit '+$propProcess.ExitCode+'). Consulte '+$propReportDirectory) }
Write-Host ('Props validados: '+$propData.props+' objetos, '+$propData.samples+' amostras e '+@($propData.checks).Count+' verificações aprovadas.')
