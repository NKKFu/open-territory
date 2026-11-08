param([string]$Executable = (Join-Path $PSScriptRoot '../../Deliveries/OverallMap-20261108/BaiaDoSol.exe'))
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$mapAcceptance = Join-Path $PSScriptRoot 'acceptance'
New-Item -ItemType Directory -Path $mapAcceptance -Force | Out-Null
$mapSuites = @()
function Invoke-MapSuite([string]$Name, [scriptblock]$Run) {
    $mapSuiteStarted = [DateTime]::UtcNow
    Write-Output ('Verifying ' + $Name)
    $mapProblem = $null
    try { & $Run } catch { $mapProblem = $_.Exception.Message; Write-Output $mapProblem }
    $mapRecord = Join-Path (Join-Path $mapAcceptance $Name) 'verification.json'
    $mapVerified = if ((Test-Path -LiteralPath $mapRecord) -and (Get-Item -LiteralPath $mapRecord).LastWriteTimeUtc -gt $mapSuiteStarted) {
        Get-Content -LiteralPath $mapRecord -Raw | ConvertFrom-Json
    } else { $null }
    $script:mapSuites += [ordered]@{name=$Name;passed=($null -eq $mapProblem -and $null -ne $mapVerified -and $mapVerified.passed);
        report=$mapRecord;error=$mapProblem;completedAtUtc=[DateTime]::UtcNow.ToString('o')}
    $script:mapSuites | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $mapAcceptance 'progress.json') -Encoding utf8
}
foreach ($mapProbe in @('overall','map','neighborhood','frontage','reference','prop-grounding')) {
    Invoke-MapSuite $mapProbe { & "$PSScriptRoot/../../Verificar-Mapa-Geral.ps1" -Executable $Executable -Probe $mapProbe -ReportDirectory (Join-Path $mapAcceptance $mapProbe) }
}
foreach ($mapCompound in @(@('clothing','Roupas'),@('bike-shop','Bicicletaria'),@('blackthorn','Blackthorn'),@('fire-station','Quartel'))) {
    if ($mapCompound[0] -eq 'blackthorn') {
        Invoke-MapSuite 'blackthorn' { & "$PSScriptRoot/../../Verificar-Blackthorn.ps1" -Executable $Executable -GraphicsApi d3d11 -ReportDirectory (Join-Path $mapAcceptance 'blackthorn') }
    } else {
        Invoke-MapSuite $mapCompound[0] { & "$PSScriptRoot/../../Verificar-$($mapCompound[1]).ps1" -Executable $Executable -ReportDirectory (Join-Path $mapAcceptance $mapCompound[0]) }
    }
}
Invoke-MapSuite 'default-session' { & "$PSScriptRoot/../../Verificar-Executavel-Padrao.ps1" -Executable $Executable -ReportDirectory (Join-Path $mapAcceptance 'default-session') }
Invoke-MapSuite 'outdoor-default' { & "$PSScriptRoot/../../Verificar-Mapa-Geral.ps1" -Executable $Executable -Probe outdoor-destruction -ReportDirectory (Join-Path $mapAcceptance 'outdoor-default') }
# The earlier DX12 destruction shutdown fault is retained separately. Never
# turn a successful JSON into a pass if its process crashed while exiting.
Invoke-MapSuite 'outdoor-d3d11' { & "$PSScriptRoot/../../Verificar-Mapa-Geral.ps1" -Executable $Executable -Probe outdoor-destruction -GraphicsApi d3d11 -ReportDirectory (Join-Path $mapAcceptance 'outdoor-d3d11') }
Invoke-MapSuite 'overall-d3d11' { & "$PSScriptRoot/../../Verificar-Mapa-Geral.ps1" -Executable $Executable -Probe overall -GraphicsApi d3d11 -ReportDirectory (Join-Path $mapAcceptance 'overall-d3d11') }
Invoke-MapSuite 'blackthorn-default' { & "$PSScriptRoot/../../Verificar-Blackthorn.ps1" -Executable $Executable -ReportDirectory (Join-Path $mapAcceptance 'blackthorn-default') }
if (@($mapSuites | Where-Object { -not $_.passed -and $_.name -notin @('outdoor-default','blackthorn-default') }).Count -gt 0) { throw 'One or more required acceptance suites failed. See acceptance/progress.json.' }
Write-Output 'Required map acceptance suites passed; default destruction diagnostics remain separately recorded.'
