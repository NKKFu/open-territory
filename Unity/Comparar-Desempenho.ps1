param(
    [string]$Baseline = (Join-Path $PSScriptRoot 'Reports\performance\before'),
    [string]$Optimized = (Join-Path $PSScriptRoot 'Reports\performance\after'),
    [string]$Output = (Join-Path $PSScriptRoot 'Reports\performance\comparison.json')
)
$ErrorActionPreference = 'Stop'
function Read-Runs([string]$directory) {
    $files = @(Get-ChildItem -LiteralPath $directory -Directory -Filter 'run-*' | Sort-Object Name)
    if ($files.Count -eq 0) { throw ('Sem rodadas em ' + $directory) }
    foreach ($folder in $files) {
        $verification = Get-Content -LiteralPath (Join-Path $folder.FullName 'verification.json') -Raw | ConvertFrom-Json
        $report = Get-Content -LiteralPath (Join-Path $folder.FullName 'performance-qa.json') -Raw | ConvertFrom-Json
        if (-not $verification.passed -or -not $report.passed -or -not $report.completed) { throw ('Rodada inválida: ' + $folder.FullName) }
        [pscustomobject]@{ report=$report; verification=$verification }
    }
}
function Summarize-Runs($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    [ordered]@{ runs=$sorted.Count; median=$median; minimum=$sorted[0]; maximum=$sorted[-1] }
}
function Compare-Metric($beforeValues, $afterValues) {
    $before = Summarize-Runs $beforeValues; $after = Summarize-Runs $afterValues
    if ($null -eq $before -or $null -eq $after) { return [ordered]@{ available=$false } }
    $percent = if ($before.median -gt 0) { ($after.median - $before.median) * 100 / $before.median } else { $null }
    [ordered]@{ available=$true; baseline=$before; optimized=$after; changePercent=$percent }
}
$before = @(Read-Runs $Baseline); $after = @(Read-Runs $Optimized)
$reference = $before[0].report
if (-not $reference.referenceImplementation -or @($after | Where-Object { $_.report.referenceImplementation }).Count -gt 0) { throw 'Selecione a variante de referência antes e a otimizada depois.' }
$configuration = @('unity','layout','cpu','gpu','graphicsApi','width','height','quality','msaa','vSync','targetFrameRate','development','navigationNodes')
foreach ($run in @($before + $after)) {
    foreach ($field in @('contentSha256','engineSha256')) {
        if (-not $run.verification.$field -or $run.verification.$field -ne $before[0].verification.$field) { throw ('Conteúdo ou engine divergente: ' + $field) }
    }
    foreach ($field in $configuration) {
        if ($run.report.$field -ne $reference.$field) { throw ('Configuração divergente: ' + $field) }
    }
    if ($run.report.routes.Count -ne $reference.routes.Count) { throw 'Número de trajetos divergente.' }
    for ($i = 0; $i -lt $reference.routes.Count; $i++) {
        foreach ($field in @('start','goal','found','count','valid','signature')) {
            if ($run.report.routes[$i].$field -ne $reference.routes[$i].$field) { throw ('Trajeto divergente: caso ' + $i + ', ' + $field) }
        }
    }
}
$operationMetrics = foreach ($name in @('resident-routes','prop-destruction')) {
    $b = @($before | ForEach-Object { $_.report.operations | Where-Object name -eq $name })
    $a = @($after | ForEach-Object { $_.report.operations | Where-Object name -eq $name })
    $bAllocated = @($b | Where-Object { $_.allocatedBytes.available } | ForEach-Object { $_.allocatedBytes.mean })
    $aAllocated = @($a | Where-Object { $_.allocatedBytes.available } | ForEach-Object { $_.allocatedBytes.mean })
    [ordered]@{ name=$name; operationTimeMs=(Compare-Metric @($b.milliseconds.mean) @($a.milliseconds.mean));
        operationP95Ms=(Compare-Metric @($b.milliseconds.p95) @($a.milliseconds.p95));
        allocatedBytes=(Compare-Metric $bAllocated $aAllocated) }
}
$frameMetrics = foreach ($name in @('city-street','destruction-stress','destruction-stress-tail')) {
    $b = @($before | ForEach-Object { $_.report.phases | Where-Object name -eq $name })
    $a = @($after | ForEach-Object { $_.report.phases | Where-Object name -eq $name })
    $metrics = [ordered]@{ name=$name }
    foreach ($counter in @('frameMs','mainThreadMs','renderThreadMs','gpuMs','gcBytes','usedMemoryMiB','drawCalls')) {
        $bValues = @($b | Where-Object { $_.$counter.available } | ForEach-Object { $_.$counter.mean })
        $aValues = @($a | Where-Object { $_.$counter.available } | ForEach-Object { $_.$counter.mean })
        # A scene with visible meshes cannot have zero draws throughout. Some
        # release player counters report zero despite being registered as valid.
        if ($counter -eq 'drawCalls') { $bValues=@($bValues|Where-Object{$_ -gt 0}); $aValues=@($aValues|Where-Object{$_ -gt 0}) }
        $metrics[$counter] = Compare-Metric $bValues $aValues
    }
    $metrics['frameP95Ms'] = Compare-Metric @($b.frameMs.p95) @($a.frameMs.p95)
    $metrics
}
$result = [ordered]@{ comparedAtUtc=[DateTime]::UtcNow.ToString('o'); configurationMatches=$true; contentMatches=$true; routeSignaturesMatch=$true;
    contentSha256=$before[0].verification.contentSha256; engineSha256=$before[0].verification.engineSha256;
    baselineAssemblies=@($before.verification.assemblySha256 | Select-Object -Unique);
    optimizedAssemblies=@($after.verification.assemblySha256 | Select-Object -Unique);
    baselineRuns=$before.Count; optimizedRuns=$after.Count;
    configuration=($reference | Select-Object $configuration); operations=@($operationMetrics); phases=@($frameMetrics) }
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Output -Encoding utf8
Write-Host ('Comparação gerada: ' + $Output)
$operationMetrics | ForEach-Object {
    $allocation = if ($_.allocatedBytes.available) { [Math]::Round($_.allocatedBytes.changePercent, 1).ToString() + '%' } else { 'indisponível' }
    Write-Host ($_.name + ': tempo ' + [Math]::Round($_.operationTimeMs.changePercent, 1) + '%, alocação ' + $allocation)
}
