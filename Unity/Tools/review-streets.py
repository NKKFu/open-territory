"""Review the active authored map without regenerating the city."""
import argparse
import json
import os
import sys
from pathlib import Path
sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
from street_review import review

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, default=root/'OpenTerritory/Assets')
args = parser.parse_args()
assets = root/'OpenTerritory/Assets'
def read(path): return json.loads((args.source/path).read_text(encoding='utf-8-sig'))
visual, world = read('Imported/visual.json'), read('Resources/world.json')
reference, street = read('Resources/reference-map.json'), read('Resources/street-network.json')
if street.get('reviewRevision') == 1:
    raise SystemExit('This export is already reviewed; use a pristine export or --source backup.')
audit = root/'Reports/map-revision/before/benches.json'
patch, report = review(visual, world, reference, street, json.loads(audit.read_text()) if audit.exists() else None)
outputs = [('Imported/visual.json', visual), ('Resources/world.json', world), ('Resources/reference-map.json', reference),
           ('Resources/street-network.json', street), ('Imported/street-review.json', patch)]
for name, data in outputs:
    path = assets/name
    path.with_suffix('.json.tmp').write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
for name, _ in outputs:
    path = assets/name
    os.replace(path.with_suffix('.json.tmp'), path)
out = root/'Reports/map-revision'
out.mkdir(parents=True, exist_ok=True)
(out/'review.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
print('Reviewed:', len(report['roadJoins']), 'joins,', report['afterSignalDevices'], 'signal approaches,', report['afterCrossings'], 'crossings,', report['exteriorBenches'], 'exterior benches', flush=True)
