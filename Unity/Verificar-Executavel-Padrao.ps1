param(
    [string]$Executable = (Join-Path $PSScriptRoot 'OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe'),
    [string]$ReportDirectory = (Join-Path $PSScriptRoot 'Reports\performance\default-player')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$assembly = Join-Path (Split-Path -Parent $Executable) 'BaiaDoSol_Data\Managed\Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
    '--city-debug',('"'+$ReportDirectory+'"'),'-logFile',('"'+(Join-Path $ReportDirectory 'player.log')+'"')) -WindowStyle Hidden -PassThru
$null = $process.Handle
$seq = 0
function Send-PlayerCommand($Command, [int]$TimeoutSeconds = 90) {
    $script:seq++
    $Command['seq'] = $script:seq
    $path = Join-Path $ReportDirectory 'command.json'
    $temporary = Join-Path $ReportDirectory 'command.pending'
    [IO.File]::WriteAllText($temporary,($Command|ConvertTo-Json -Depth 4),[Text.Encoding]::UTF8)
    Move-Item -LiteralPath $temporary -Destination $path -Force
    $until = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $until) {
        if ($process.HasExited) { throw ('Player encerrou antes da resposta: ' + $process.ExitCode) }
        $statePath = Join-Path $ReportDirectory 'state.json'
        if (Test-Path -LiteralPath $statePath) {
            try { $state = Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json; if ($state.seq -eq $script:seq -and $state.ready) { return $state } } catch { }
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'O player não respondeu ao comando opt-in.'
}
$checks = @()
try {
    $initial = Send-PlayerCommand @{op='pause';pause=$false}
    Start-Sleep -Seconds 2
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'OpenTerritory\Assets\Resources\world.json') -Raw|ConvertFrom-Json
    $before = Send-PlayerCommand @{op='warp';point=$manifest.spawn;yaw=0}
    $null = Send-PlayerCommand @{op='input';moveY=1;run=$false;seconds=1.5}
    Start-Sleep -Seconds 2
    $after = Send-PlayerCommand @{op='input';moveY=0;seconds=.1}
    $distance = [Math]::Sqrt([Math]::Pow($after.position.x-$before.position.x,2)+[Math]::Pow($after.position.z-$before.position.z,2))
    $checks += [ordered]@{name='walking-through-real-input-poller';passed=$distance -gt .5;detail=('distance='+$distance)}
    $paused = Send-PlayerCommand @{op='pause';pause=$true}
    Start-Sleep -Seconds 1
    $stillPaused = Send-PlayerCommand @{op='pause';pause=$true}
    $checks += [ordered]@{name='pause-stops-city-clock';passed=$stillPaused.paused -and $stillPaused.clock -eq $paused.clock}
    $null = Send-PlayerCommand @{op='pause';pause=$false}
    $snapshot = Send-PlayerCommand @{op='input';moveY=0;seconds=.1}
    $checks += [ordered]@{name='native-session-has-no-runtime-errors';passed=$snapshot.errors -eq 0;detail=('errors='+$snapshot.errors)}
    [IO.File]::WriteAllText((Join-Path $ReportDirectory 'command.json'),(@{seq=($script:seq+1);op='quit'}|ConvertTo-Json),[Text.Encoding]::UTF8)
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'O player não encerrou.' }
    $process.WaitForExit(); $exitCode = $process.ExitCode
    $checks += [ordered]@{name='normal-exit-with-default-graphics-api';passed=$exitCode -eq 0;detail=('exit='+$exitCode)}
    $passed = @($checks|Where-Object{-not $_.passed}).Count -eq 0 -and (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -eq $hash
    [ordered]@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');startedAtUtc=$started.ToString('o');passed=$passed;executable=$Executable;
        assemblySha256=$hash;exitCode=$exitCode;checks=$checks} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $ReportDirectory 'verification.json') -Encoding utf8
    $checks | ForEach-Object { Write-Host ($_.name+': '+$_.passed+' '+$_.detail) }
    if (-not $passed) { throw 'A validação do player padrão falhou; consulte verification.json.' }
}
finally { if (-not $process.HasExited) { $process.Kill() } }
