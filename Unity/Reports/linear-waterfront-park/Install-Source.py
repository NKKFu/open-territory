from pathlib import Path
import shutil

report = Path(__file__).resolve().parent
unity = report.parents[1] / 'OpenTerritory'
authored = report / 'authoring'
patches = {
    'Assets/Scripts/Core/CityContext.cs': [
        (b'CityWaterfront.Bind(this);', b'CityWaterfront.Bind(this);\n  CityLinearPark.Bind(this);'),
    ],
    'Assets/Editor/CityGroundCoverBuilder.cs': [
        (b'if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;',
         b'if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;\n            if (CityLinearParkBuilder.Reserved(x, z, radius)) return false;'),
    ],
    'Assets/Editor/CityBuilder.cs': [
        (b'CityParkingLotBuilder.Build(root.transform,world,registry);',
         b'CityParkingLotBuilder.Build(root.transform,world,registry);\r\n  CityLinearParkBuilder.Build(root.transform,world);'),
        (b'CityParkingLotBuilder.Build(root,world,registry);',
         b'CityParkingLotBuilder.Build(root,world,registry);CityLinearParkBuilder.Build(root,world);'),
    ],
}
planned = {}
for relative, replacements in patches.items():
    path = unity / relative
    raw = path.read_bytes()
    for before, after in replacements:
        if after in raw:
            continue
        if raw.count(before) != 1:
            raise RuntimeError(f'Anchor missing or ambiguous: {relative}: {before!r}')
        raw = raw.replace(before, after, 1)
    planned[path] = raw

new_files = {
    'CityLinearPark.cs': 'Assets/Scripts/World/CityLinearPark.cs',
    'CityLinearParkProbe.cs': 'Assets/Scripts/World/CityLinearParkProbe.cs',
    'CityLinearParkBuilder.cs': 'Assets/Editor/CityLinearParkBuilder.cs',
    'LinearParkYards.json': 'Assets/Editor/LinearParkYards.json',
}
for source, relative in new_files.items():
    target = unity / relative
    if target.exists():
        raise RuntimeError(f'Refusing to replace an existing file: {target}')
    if not (authored / source).is_file():
        raise RuntimeError(f'Missing authoring file: {source}')

for path in planned:
    backup = report / 'source-before' / path.relative_to(unity)
    backup.parent.mkdir(parents=True, exist_ok=True)
    if not backup.exists():
        shutil.copy2(path, backup)
for source, relative in new_files.items():
    shutil.copy2(authored / source, unity / relative)
for path, value in planned.items():
    path.write_bytes(value)
shutil.copy2(authored / 'Verificar-ParqueLinear.ps1', report.parents[1] / 'Verificar-ParqueLinear.ps1')
print('Park sources and generator hooks installed.')
