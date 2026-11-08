"""Remove the donor car park's obsolete, invisible flat collision grid."""
import hashlib, json, math, os, shutil
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root/'OpenTerritory/Assets'
names = ['Imported/visual.json', 'Resources/world.json', 'Resources/overall-map-review.json', 'Imported/overall-map-patch.json']
visual, world, review, patch = [json.loads((assets/p).read_text(encoding='utf-8-sig')) for p in names]
node = next(n for n in visual['nodes'] if n['name'] == 'Reused carpark' and n['materials'] == [24])
frame = node['instances'][0]['matrix']; floor = frame[13]-.05
geometry = visual['geometries'][node['geometry']]; p = geometry['positions']
x0, x1 = min(p[0::3])+frame[12]-3, max(p[0::3])+frame[12]+3
z0, z1 = min(p[2::3])+frame[14]-4.5, max(p[2::3])+frame[14]+4.5
removed = []
for f in visual['floors']:
    v = f['vertices']
    if len(v) == 9 and all(abs(y-floor) < .002 for y in v[1::3]) and all(x0-.001 <= x <= x1+.001 for x in v[0::3]) and all(z0-.001 <= z <= z1+.001 for z in v[2::3]):
        removed.append(f)
if not removed:
    print('The legacy parking collision grid is already absent.')
    raise SystemExit(0)
keys = {f"{math.floor(f['vertices'][0]/32)},{math.floor(f['vertices'][2]/32)}" for f in removed}
ids = {id(f) for f in removed}; visual['floors'] = [f for f in visual['floors'] if id(f) not in ids]
before = world['sourceHash']; after = hashlib.sha256((before+'remove-legacy-carpark-collision-grid-v1').encode()).hexdigest()
world['sourceHash'] = review['sourceHash'] = patch['sourceHash'] = after
patch['previousSourceHash'] = before; patch['floorKeys'] = sorted(set(patch['floorKeys']) | keys)
backup = root/'Reports/overall-map/refinements'/datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
for name, value in zip(names, [visual, world, review, patch]):
    source = assets/name; target = backup/name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, target)
    temporary = source.with_suffix('.json.tmp'); temporary.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8'); os.replace(temporary, source)
record = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), removedGhostTriangles=len(removed), floorKeys=sorted(keys), sourceHash=after, backup=str(backup))
(root/'Reports/overall-map/parking-collision-repair.json').write_text(json.dumps(record, indent=2), encoding='utf-8')
print(json.dumps(record, indent=2))
