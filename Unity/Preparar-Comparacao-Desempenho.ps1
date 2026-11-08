param(
    [string]$Baseline = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease'),
    [string]$Destination = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\PerformanceBefore'),
    [switch]$RestoreContent
)
$ErrorActionPreference = 'Stop'
$Baseline = (Resolve-Path -LiteralPath $Baseline).Path
$Destination = [IO.Path]::GetFullPath($Destination)
$buildPrefix = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'OpenTerritory\Builds')).TrimEnd('\') + '\'
if (-not $Destination.StartsWith($buildPrefix,[StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFileName($Destination).StartsWith('Performance',[StringComparison]::OrdinalIgnoreCase)) -or
    $Destination.Equals($Baseline,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use uma pasta Performance distinta dentro de OpenTerritory/Builds para a comparação.'
}
if ((Test-Path -LiteralPath $Destination) -and -not $RestoreContent) { throw ('O destino já existe; preserve-o e use outro caminho: ' + $Destination) }
if (-not (Test-Path -LiteralPath (Join-Path $Baseline 'BaiaDoSol.exe'))) { throw 'Player de referência ausente.' }
$saved = @{}
if ($RestoreContent) {
    foreach ($name in @('Assembly-CSharp.dll','Assembly-CSharp.pdb','Assembly-CSharp.dll.mdb')) {
        $path = Join-Path $Destination ('BaiaDoSol_Data\Managed\' + $name)
        if (Test-Path -LiteralPath $path) { $saved[$path] = [IO.File]::ReadAllBytes($path) }
    }
    if ($saved.Count -eq 0) { throw 'Assembly do candidato ausente.' }
    $basePrefix = $Baseline.TrimEnd('\') + '\'
    $destinationPrefix = $Destination.TrimEnd('\') + '\'
    $baseFiles = @{}
    Get-ChildItem -LiteralPath $Baseline -Recurse -File | ForEach-Object { $baseFiles[$_.FullName.Substring($basePrefix.Length)] = $true }
    foreach ($file in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
        $resolved = [IO.Path]::GetFullPath($file.FullName)
        if (-not $resolved.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Arquivo fora do destino de comparação.' }
        $relative = $resolved.Substring($destinationPrefix.Length)
        if (-not $baseFiles.ContainsKey($relative) -and -not $saved.ContainsKey($resolved)) { Remove-Item -LiteralPath $resolved }
    }
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $Baseline) {
    Copy-Item -LiteralPath $entry.FullName -Destination $Destination -Recurse -Force
}
foreach ($path in $saved.Keys) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
. (Join-Path $PSScriptRoot 'Performance-Content.ps1')
if ((Get-PerformanceContentHash $Baseline) -ne (Get-PerformanceContentHash $Destination)) { throw 'Os dados congelados não coincidem.' }
Write-Host ('Referência copiada: ' + $Destination)
if (-not $RestoreContent) { Write-Host 'Compile a variante e repita com -RestoreContent para preservar também os blobs externos de áudio e textura.' }
