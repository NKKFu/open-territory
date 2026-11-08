$unityProject = Join-Path $PSScriptRoot 'OpenTerritory'
$unityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe'
if (-not (Test-Path -LiteralPath $unityEditor)) { throw 'Instale o Unity 6000.3.27f1 pelo Unity Hub ou adicione OpenTerritory ao Hub.' }
Start-Process -FilePath $unityEditor -ArgumentList @('-projectPath', ('"' + $unityProject + '"'))
