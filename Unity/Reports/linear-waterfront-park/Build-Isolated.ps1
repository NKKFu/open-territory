param([string]$DeliveryName=('LinearPark-'+(Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference='Stop'
if($DeliveryName -notmatch '^[A-Za-z0-9_-]+$'){throw 'Nome de entrega inválido.'}
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$snapshot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../ValidationProjects/'+$DeliveryName)))
$output=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../Deliveries/'+$DeliveryName+'/BaiaDoSol.exe')))
if(Test-Path -LiteralPath $output){throw ('A entrega já existe: '+$output)}
New-Item -ItemType Directory -Force -Path $snapshot | Out-Null
function Get-SourceManifest {
    $result=@{}
    foreach($part in @('Assets','Packages','ProjectSettings')) {
        foreach($item in Get-ChildItem -LiteralPath (Join-Path $source $part) -File -Recurse) {
            $relative=$item.FullName.Substring($source.Length+1)
            if($relative -like 'Assets\Scenes\Backups\*'){continue}
            $result[$relative]=@($item.Length,$item.LastWriteTimeUtc.Ticks)
        }
    }
    return $result
}
function Test-Manifest($first,$second) {
    if($first.Count -ne $second.Count){return $false}
    foreach($key in $first.Keys){
        if(-not $second.ContainsKey($key) -or $first[$key][0] -ne $second[$key][0] -or $first[$key][1] -ne $second[$key][1]){return $false}
    }
    return $true
}
$coherent=$false
for($attempt=1;$attempt -le 5 -and -not $coherent;$attempt++){
    $before=Get-SourceManifest
    foreach($part in @('Assets','Packages','ProjectSettings')){
        $copyArgs=@((Join-Path $source $part),(Join-Path $snapshot $part),'/E','/COPY:DAT','/DCOPY:DAT','/R:2','/W:1','/NFL','/NDL','/NJH','/NJS','/NP')
        if($part -eq 'Assets'){$copyArgs+=@('/XD',(Join-Path $source 'Assets/Scenes/Backups'))}
        & robocopy @copyArgs | Out-Null
        if($LASTEXITCODE -gt 7){throw "Falha ao copiar $part ($LASTEXITCODE)."}
    }
    $after=Get-SourceManifest;Start-Sleep -Seconds 3;$quiet=Get-SourceManifest
    $coherent=(Test-Manifest $before $after) -and (Test-Manifest $after $quiet)
    if($coherent){
        foreach($key in $quiet.Keys){
            $copy=Join-Path $snapshot $key
            if(-not (Test-Path -LiteralPath $copy)){$coherent=$false;break}
            $item=Get-Item -LiteralPath $copy
            if($item.Length -ne $quiet[$key][0] -or $item.LastWriteTimeUtc.Ticks -ne $quiet[$key][1]){$coherent=$false;break}
        }
    }
    if(-not $coherent){Write-Output "Arquivos mudaram durante a cópia; repetindo ($attempt/5)."}
}
if(-not $coherent){throw 'Não foi possível congelar uma cópia consistente do projeto.'}
$sceneHash=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Scenes/BaiaDoSol.unity')).Hash
$terrainHash=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Imported/ClothingShop.asset')).Hash
$parkHash=(Get-FileHash -LiteralPath (Join-Path $snapshot 'Assets/Imported/LinearWaterfrontPark.asset')).Hash
if($parkHash -ne (Get-FileHash -LiteralPath (Join-Path $source 'Assets/Imported/LinearWaterfrontPark.asset')).Hash){throw 'Asset do parque mudou durante a cópia.'}
if($sceneHash -ne (Get-FileHash -LiteralPath (Join-Path $source 'Assets/Scenes/BaiaDoSol.unity')).Hash -or
   $terrainHash -ne (Get-FileHash -LiteralPath (Join-Path $source 'Assets/Imported/ClothingShop.asset')).Hash){throw 'Cena ou terreno mudaram durante a cópia.'}
foreach($part in @('Artifacts','PackageCache','ShaderCache')){
    $cache=Join-Path $source ('Library/'+$part)
    if(Test-Path -LiteralPath $cache){
        & robocopy $cache (Join-Path $snapshot ('Library/'+$part)) /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if($LASTEXITCODE -gt 7){throw "Falha ao copiar o cache $part."}
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'authoring/CityLinearParkFinalBuild.cs') -Destination (Join-Path $snapshot 'Assets/Editor/CityLinearParkFinalBuild.cs') -Force
$scripts=Get-ChildItem -LiteralPath (Join-Path $snapshot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($snapshot.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
[ordered]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');source=$source;snapshot=$snapshot;executable=$output;
    sourceFiles=$quiet.Count;consistent=$coherent;sceneSha256=$sceneHash;terrainAssetSha256=$terrainHash;parkAssetSha256=$parkHash;scripts=@($scripts)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Encoding utf8
$editor='C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$snapshot+'"'),'-executeMethod','OpenTerritory.Editor.CityLinearParkFinalBuild.Build',
    '-city-output',('"'+$output+'"'),'-city-build-record',('"'+(Join-Path $PSScriptRoot 'build-complete.json')+'"'),'-logFile',('"'+(Join-Path $PSScriptRoot 'isolated-build.log')+'"'),'-job-worker-count','4')
$process=Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('Build isolado do parque: PID '+$process.Id)
$process.WaitForExit()
[ordered]@{completedAtUtc=[DateTime]::UtcNow.ToString('o');processExitCode=$process.ExitCode;executable=$output} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build-process-result.json') -Encoding utf8
if($process.ExitCode -ne 0){throw ('Unity encerrou com código '+$process.ExitCode)}
