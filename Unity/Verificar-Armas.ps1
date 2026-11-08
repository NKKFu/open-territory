param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\weapons\native'),
    [switch]$Direct3D11
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$started = [DateTime]::UtcNow
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-weapons-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($Direct3D11) { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Host ('Armas: QA nativo iniciado, PID ' + $process.Id)
$deadline = [DateTime]::UtcNow.AddMinutes(3)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação de armas excedeu três minutos.' }
}
$reportPath = Join-Path $ReportDirectory 'weapons-qa.json'
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$process.Refresh()
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $hash) { throw 'O executável mudou durante a verificação.' }
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$hash; graphicsApi=$(if($Direct3D11){'Direct3D11'}else{'Player default'}); exitCode=$process.ExitCode; passed=($process.ExitCode -eq 0 -and $report.passed); checks=@($report.checks).Count; report=$reportPath; captures=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if ($process.ExitCode -ne 0 -or -not $report.passed) { throw ('QA de armas falhou. Consulte ' + $reportPath) }
Write-Host ('Armas aprovadas: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
