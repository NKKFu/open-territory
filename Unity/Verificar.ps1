param(
    [switch]$Render,
    [ValidateSet('reference','map','audio','frontages','neighborhood','population','circulation','doors','driving','gameplay','passengers','traffic','incident','life','world')]
    [string[]]$Suite = @('reference')
)
$ErrorActionPreference = 'Stop'
if ('reference' -in $Suite) {
    & (Join-Path $PSScriptRoot 'Verificar-Referencia.ps1')
}
if ('audio' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Audio.ps1') }
if ('map' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Mapa.ps1') }
if ('frontages' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Fachadas.ps1') }
if ('neighborhood' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Quintais.ps1') }
if ('population' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Populacao.ps1') }
if ('circulation' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Circulacao.ps1') }
if ('doors' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Portas.ps1') }
if ('driving' -in $Suite) { & (Join-Path $PSScriptRoot 'Verificar-Direcao.ps1') }
$Suite = @($Suite | Where-Object { $_ -notin @('reference','audio','map','frontages','neighborhood','population','circulation','doors','driving') })
if ($Suite.Count -eq 0) { exit 0 }
$activeManifest = Get-Content (Join-Path $PSScriptRoot 'OpenTerritory\Assets\Resources\world.json') -Raw | ConvertFrom-Json
if ($activeManifest.layoutId -eq 'marin-reference-v2') { throw 'Os probes de coordenadas do mapa antigo não se aplicam à cena nova. Use -Suite reference.' }
$unityExe = Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'
$unityReports = Join-Path $PSScriptRoot 'Reports'
if (-not (Test-Path -LiteralPath $unityExe)) { throw 'Gere o executável pelo menu Open Territory > Construir Windows.' }
New-Item -ItemType Directory -Path $unityReports -Force | Out-Null
$unitySuites = @(
    @{ Name='gameplay'; Flag='--city-qa'; Extra=@('-city-qa-report', ('"' + (Join-Path $unityReports 'gameplay-qa.json') + '"')) },
    @{ Name='passengers'; Flag='--city-qa'; Extra=@('--city-qa-passengers', '-city-qa-report', ('"' + (Join-Path $unityReports 'passengers-qa.json') + '"')) },
    @{ Name='traffic'; Flag='--city-qa'; Extra=@('--city-qa-traffic', '-city-qa-report', ('"' + (Join-Path $unityReports 'traffic-qa.json') + '"')) },
    @{ Name='incident'; Flag='--city-incident-qa'; Extra=@('--city-report-dir', ('"' + $unityReports + '"')) },
    @{ Name='life'; Flag='--city-life-qa'; Extra=@('--city-report-dir', ('"' + $unityReports + '"')) },
    @{ Name='world'; Flag='--city-world-qa'; Extra=@('--city-report-dir', ('"' + $unityReports + '"')) }
)
$unitySuites = @($unitySuites | Where-Object { $_.Name -in $Suite })
if ($Render) { $unitySuites += @{ Name='render'; Flag='--city-render-qa'; Extra=@('--city-report-dir', ('"' + (Join-Path $unityReports 'renders') + '"')) } }
$unityJobs = foreach ($suiteSpec in $unitySuites) {
    $suiteArgs = @('-batchmode', $suiteSpec.Flag, '-logFile', ('"' + (Join-Path $unityReports ($suiteSpec.Name + '-qa.log')) + '"')) + $suiteSpec.Extra
    if ($suiteSpec.Name -notin @('render','coast') -and -not ($Render -and $suiteSpec.Name -eq 'incident')) { $suiteArgs += '-nographics' }
    $started = [DateTime]::UtcNow
    $process = Start-Process -FilePath $unityExe -ArgumentList $suiteArgs -WindowStyle Hidden -PassThru
    $resultPath = if ($suiteSpec.Name -eq 'render') { Join-Path $unityReports 'renders\render-qa.json' } elseif ($suiteSpec.Name -eq 'coast') { Join-Path $unityReports 'coast\coast-qa.json' } else { Join-Path $unityReports ($suiteSpec.Name + '-qa.json') }
    Write-Host ($suiteSpec.Name + ': iniciado, PID ' + $process.Id)
    @{ Name=$suiteSpec.Name; Process=$process; Started=$started; Report=$resultPath }
}
$failed = $false
$results = @()
foreach ($job in $unityJobs) {
    if (-not $job.Process.WaitForExit(240000)) {
        $job.Process.Kill()
        Write-Warning ($job.Name + ': tempo máximo excedido.')
        $failed = $true
    } else {
        $valid = $false
        $checks = 0
        if (Test-Path -LiteralPath $job.Report) {
            $file = Get-Item -LiteralPath $job.Report
            try {
                $report = Get-Content -LiteralPath $job.Report -Raw | ConvertFrom-Json
                $checks = @($report.checks).Count
                if ($job.Name -eq 'render') { $checks = $report.screenshots }
                $valid = $file.LastWriteTimeUtc -ge $job.Started -and $report.passed -eq $true
            } catch { Write-Warning ($job.Name + ': relatório inválido: ' + $_.Exception.Message) }
        }
        $passed = $job.Process.ExitCode -eq 0 -and $valid
        Write-Output ($job.Name + ': exit ' + $job.Process.ExitCode + ', aprovado com relatório atual ' + $valid + ', verificações ' + $checks)
        $results += [pscustomobject]@{ suite=$job.Name; passed=$passed; checks=$checks; report=$job.Report; exitCode=$job.Process.ExitCode }
        $failed = $failed -or -not $passed
    }
}
$assemblyPath = Join-Path (Split-Path -Parent $unityExe) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
[ordered]@{
    verifiedAtUtc=[DateTime]::UtcNow.ToString('o')
    executable=$unityExe
    assemblySha256=(Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    passed=(-not $failed)
    suites=$results
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $unityReports 'verification.json') -Encoding UTF8
if ($failed) { throw ('Uma suíte falhou. Confira os relatórios em ' + $unityReports) }
Write-Output ('Testes nativos concluídos. Relatórios: ' + $unityReports)
