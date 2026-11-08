$ErrorActionPreference='Stop'
$reviewRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$reviewExe=Join-Path $reviewRoot 'Unity/OpenTerritory/Builds/Firearms-20261108-0800/BaiaDoSol.exe'
$reviewDir=Join-Path $PSScriptRoot 'city-native-batch'
New-Item -ItemType Directory -Path $reviewDir -Force | Out-Null
$reviewHash=(Get-FileHash -LiteralPath (Join-Path (Split-Path $reviewExe -Parent) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll')).Hash
$reviewProcess=Start-Process -FilePath $reviewExe -ArgumentList @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','800','--city-debug',('"'+$reviewDir+'"'),'-logFile',('"'+(Join-Path $reviewDir 'player.log')+'"')) -WindowStyle Hidden -PassThru
$null=$reviewProcess.Handle
$script:reviewSeq=0
function Send-ReviewCommand($command) {
    $script:reviewSeq++;$command['seq']=$script:reviewSeq
    $pending=Join-Path $reviewDir 'command.pending'
    [IO.File]::WriteAllText($pending,($command|ConvertTo-Json -Depth 5),[Text.Encoding]::UTF8)
    Move-Item -LiteralPath $pending -Destination (Join-Path $reviewDir 'command.json') -Force
    $deadline=[DateTime]::UtcNow.AddSeconds(55)
    while([DateTime]::UtcNow -lt $deadline) {
        if($reviewProcess.HasExited){throw ('Player closed: '+$reviewProcess.ExitCode)}
        $statePath=Join-Path $reviewDir 'state.json'
        if(Test-Path -LiteralPath $statePath) { try{$state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json;if($state.seq -eq $script:reviewSeq -and $state.ready){return $state}}catch{} }
        Start-Sleep -Milliseconds 100
    }
    throw 'Native review command timed out.'
}
try {
    $initial=Send-ReviewCommand @{op='pause';pause=$false}
    $null=Send-ReviewCommand @{op='weapon';weapon='sidearm'}
    $null=Send-ReviewCommand @{op='input';aim=$true;seconds=2}
    Start-Sleep -Milliseconds 600
    $pose=Send-ReviewCommand @{op='pause';pause=$true}
    $p=$pose.position
    $null=Send-ReviewCommand @{op='camera';follow=$false;point=@{x=($p.x+1.8);y=($p.y+1.8);z=($p.z+2.0)};lookAt=@{x=$p.x;y=($p.y+1.45);z=($p.z+.25)}}
    $null=Send-ReviewCommand @{op='screenshot';path=(Join-Path $reviewDir 'sidearm-city.png')}
    $null=Send-ReviewCommand @{op='camera';follow=$true}
    $null=Send-ReviewCommand @{op='pause';pause=$false}
    $null=Send-ReviewCommand @{op='weapon';weapon='carbine'}
    $null=Send-ReviewCommand @{op='input';aim=$true;seconds=2}
    Start-Sleep -Milliseconds 600
    $pose=Send-ReviewCommand @{op='pause';pause=$true}
    $p=$pose.position
    $null=Send-ReviewCommand @{op='camera';follow=$false;point=@{x=($p.x+1.8);y=($p.y+1.8);z=($p.z+2.0)};lookAt=@{x=$p.x;y=($p.y+1.45);z=($p.z+.25)}}
    $last=Send-ReviewCommand @{op='screenshot';path=(Join-Path $reviewDir 'carbine-city.png')}
    [IO.File]::WriteAllText((Join-Path $reviewDir 'command.json'),(@{seq=($script:reviewSeq+1);op='quit'}|ConvertTo-Json),[Text.Encoding]::UTF8)
    if(-not $reviewProcess.WaitForExit(30000)){$reviewProcess.Kill();throw 'Player did not exit.'}
    $reviewProcess.Refresh()
    $passed=$last.errors -eq 0 -and $reviewProcess.ExitCode -eq 0 -and (Get-FileHash -LiteralPath (Join-Path (Split-Path $reviewExe -Parent) 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll')).Hash -eq $reviewHash
    [ordered]@{passed=$passed;verifiedAtUtc=[DateTime]::UtcNow.ToString('o');executable=$reviewExe;assemblySha256=$reviewHash;graphicsApi='Player default';exitCode=$reviewProcess.ExitCode;errors=$last.errors;position=$p;captures=@('sidearm-city.png','carbine-city.png')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reviewDir 'verification.json') -Encoding utf8
    if(-not $passed){throw 'Native city review failed.'}
    Write-Host 'Native city review completed without errors.'
} finally {if(-not $reviewProcess.HasExited){$reviewProcess.Kill()}}
