param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\emotes\native'),
    [ValidateSet('Direct3D11', 'Default')][string]$GraphicsApi = 'Default'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assemblyPath = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$started = [DateTime]::UtcNow
$reportPath = Join-Path $ReportDirectory 'emote-qa.json'
$arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '800', '--city-emote-qa', '--city-report-dir', ('"' + $ReportDirectory + '"'), '-logFile', ('"' + (Join-Path $ReportDirectory 'player.log') + '"'))
if ($GraphicsApi -eq 'Direct3D11') { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
$null = $process.Handle
Write-Host ('Emojis: QA nativo iniciado, PID ' + $process.Id)
$deadline = [DateTime]::UtcNow.AddMinutes(3)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'A verificação de emojis excedeu três minutos.' }
}
if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $started) { throw ('Relatório atual ausente. Consulte ' + (Join-Path $ReportDirectory 'player.log')) }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$process.Refresh()
if ((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'O executável mudou durante a verificação; execute novamente sobre uma compilação estável.' }
[ordered]@{ verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); executable=$Executable; assemblySha256=$assemblyHash; graphicsApi=$GraphicsApi; exitCode=$process.ExitCode; passed=($process.ExitCode -eq 0 -and $report.passed); checks=@($report.checks).Count; report=$reportPath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
$report.checks | Where-Object { -not $_.passed } | ForEach-Object { Write-Host ('FALHOU: ' + $_.name + ' ' + $_.detail) }
if ($process.ExitCode -ne 0 -or -not $report.passed) { throw ('QA de emojis falhou. Consulte ' + $reportPath) }
Write-Host ('Emojis aprovados: ' + @($report.checks).Count + ' verificações. Relatório: ' + $reportPath)
