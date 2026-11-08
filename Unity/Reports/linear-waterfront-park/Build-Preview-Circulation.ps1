param([string]$Variant = 'PreviewCirculation')
$ErrorActionPreference = 'Stop'
if ($Variant -notmatch '^[A-Za-z0-9_-]+$') { throw 'Invalid preview variant.' }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ValidationProjects/LinearWaterfrontPark-Preview'))
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../Deliveries/LinearWaterfrontPark-' + $Variant + '/BaiaDoSol.exe')))
$record = Join-Path $PSScriptRoot ($Variant + '-build.json')
$log = Join-Path $PSScriptRoot ($Variant + '-build.log')
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
if (Test-Path -LiteralPath $output) { throw 'Preview delivery already exists.' }
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'OpenTerritory.Editor.CityLinearParkFinalBuild.Build', '-city-output', ('"' + $output + '"'),
    '-city-build-record', ('"' + $record + '"'), '-logFile', ('"' + $log + '"'), '-job-worker-count', '4')
$process = Start-Process -FilePath $editor -ArgumentList $arguments -PassThru -WindowStyle Hidden
Write-Output ('Isolated circulation build: PID ' + $process.Id)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw ('Unity build exited with code ' + $process.ExitCode) }
Write-Output 'Isolated circulation build succeeded.'
