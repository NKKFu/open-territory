param([string]$Executable)
$ErrorActionPreference = 'Stop'
$walkExe = [IO.Path]::GetFullPath($Executable)
$walkDir = Join-Path $PSScriptRoot ('walking-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $walkDir -Force | Out-Null
$walkProcess = Start-Process -FilePath $walkExe -ArgumentList @('-batchmode', '--city-debug', ('"' + $walkDir + '"'), '-logFile', ('"' + (Join-Path $walkDir 'player.log') + '"')) -WindowStyle Hidden -PassThru
function Send-WalkCommand($command) {
    $command | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath (Join-Path $walkDir 'command.tmp') -Encoding UTF8
    Move-Item -LiteralPath (Join-Path $walkDir 'command.tmp') -Destination (Join-Path $walkDir 'command.json') -Force
}
function Read-WalkState([int]$sequence) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($walkProcess.HasExited) { throw 'O player encerrou antes da leitura dos controles.' }
        $statePath = Join-Path $walkDir 'state.json'
        if (Test-Path -LiteralPath $statePath) {
            try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json; if ($state.seq -eq $sequence -and $state.ready) { return $state } } catch { }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'O diagnóstico de controles excedeu o prazo.'
}
try {
    Send-WalkCommand @{ seq=1; op='snapshot' }
    $before = Read-WalkState 1
    Send-WalkCommand @{ seq=2; op='input'; moveX=0; moveY=1; seconds=3; run=$false }
    Start-Sleep -Seconds 4
    Send-WalkCommand @{ seq=3; op='snapshot' }
    $after = Read-WalkState 3
    $distance = [Math]::Sqrt([Math]::Pow($after.position.x-$before.position.x,2)+[Math]::Pow($after.position.z-$before.position.z,2))
    Send-WalkCommand @{ seq=4; op='screenshot'; path=(Join-Path $walkDir 'walking.png') }
    $null = Read-WalkState 4
    Start-Sleep -Milliseconds 500
    $result = [ordered]@{ passed=($distance -gt 1 -and $after.errors -eq 0); distanceMeters=$distance; fps=$after.fps; before=$before; after=$after; executable=$walkExe }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $walkDir 'walking-qa.json') -Encoding UTF8
    Send-WalkCommand @{ seq=5; op='quit' }
    if (-not $walkProcess.WaitForExit(15000)) { throw 'O player não encerrou após o teste.' }
    if (-not $result.passed) { throw ('Falha no movimento: ' + ($result | ConvertTo-Json -Depth 2 -Compress)) }
    Write-Output ('Caminhada aprovada: ' + [Math]::Round($distance,2) + ' m, ' + [Math]::Round($after.fps,1) + ' fps. Relatório: ' + $walkDir)
} finally {
    if (-not $walkProcess.HasExited) { $walkProcess.Kill() }
}
