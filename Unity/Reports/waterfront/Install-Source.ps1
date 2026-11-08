$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../OpenTerritory'))
$authoring=Join-Path $PSScriptRoot 'authoring'
$newFiles=@{
    'CityWaterfront.cs'='Assets/Scripts/World/CityWaterfront.cs'
    'CityWaterfrontProbe.cs'='Assets/Scripts/World/CityWaterfrontProbe.cs'
    'CityWaterfrontBuilder.cs'='Assets/Editor/CityWaterfrontBuilder.cs'
}
foreach($name in $newFiles.Keys){
    $destination=Join-Path $root $newFiles[$name]
    if((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath (Join-Path $authoring $name)).Hash){
        throw ('Um arquivo existente foi alterado por outro trabalho: '+$destination)
    }
}
$contextPath=Join-Path $root 'Assets/Scripts/Core/CityContext.cs'
$coverPath=Join-Path $root 'Assets/Editor/CityGroundCoverBuilder.cs'
$builderPath=Join-Path $root 'Assets/Editor/CityBuilder.cs'
$context=[IO.File]::ReadAllText($contextPath);$cover=[IO.File]::ReadAllText($coverPath);$builder=[IO.File]::ReadAllText($builderPath)
if(-not $context.Contains('CityPark.Bind(this);')){throw 'Ponto de ligação dos bancos ausente.'}
if(-not $cover.Contains('if (CityParkBuilder.Reserved(x, z, radius)) return false;')){throw 'Reserva de vegetação ausente.'}
if(-not $builder.Contains('CityFireStationSiteBuilder.Build(root.transform,world);')){throw 'Ponto de importação da cidade ausente.'}
if(-not $builder.Contains('CityClothingShopBuilder.Build(root,world);Configure();')){throw 'Ponto de conclusão da importação ausente.'}
if(-not $context.Contains('CityWaterfront.Bind(this);')){$context=$context.Replace('CityPark.Bind(this);',"CityPark.Bind(this);`n  CityWaterfront.Bind(this);")}
if(-not $cover.Contains('CityWaterfrontBuilder.Reserved(x, z, radius)')){$cover=$cover.Replace('if (CityParkBuilder.Reserved(x, z, radius)) return false;',"if (CityParkBuilder.Reserved(x, z, radius)) return false;`n            if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;")}
if(-not $builder.Contains('CityWaterfrontBuilder.Build(root.transform,world);')){$builder=$builder.Replace('CityFireStationSiteBuilder.Build(root.transform,world);',"CityFireStationSiteBuilder.Build(root.transform,world);`n  CityWaterfrontBuilder.Build(root.transform,world);")}
if(-not $builder.Contains('CityWaterfrontBuilder.Build(root,world);')){$builder=$builder.Replace('CityClothingShopBuilder.Build(root,world);Configure();','CityClothingShopBuilder.Build(root,world);CityWaterfrontBuilder.Build(root,world);Configure();')}
$utf8=[Text.UTF8Encoding]::new($false)
foreach($name in $newFiles.Keys){[IO.File]::WriteAllText((Join-Path $root $newFiles[$name]),[IO.File]::ReadAllText((Join-Path $authoring $name)),$utf8)}
[IO.File]::WriteAllText($contextPath,$context,$utf8)
[IO.File]::WriteAllText($coverPath,$cover,$utf8)
[IO.File]::WriteAllText($builderPath,$builder,$utf8)
Write-Output 'Scripts da orla instalados; o Editor precisa recompilar antes da implantação.'
