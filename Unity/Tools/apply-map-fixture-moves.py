"""Persist the read-only Unity footprint search; does not mutate the scene."""
import hashlib
import json
import os
import shutil
from datetime import datetime, timezone
from pathlib import Path
from map_fixture_moves import apply_fixture_moves

root = Path(__file__).resolve().parents[1]
assets = root/'OpenTerritory/Assets'
report = root/'Reports/overall-map'
plan_file = report/'access-moves.json'
plan = json.loads(plan_file.read_text(encoding='utf-8-sig'))
paths = ['Imported/visual.json', 'Resources/world.json', 'Imported/overall-map-patch.json', 'Resources/overall-map-review.json']
values = [json.loads((assets/name).read_text(encoding='utf-8-sig')) for name in paths]
visual, world, patch, review = values
for move in plan['moves']:
    if move['id'] >= 0:
        position = next(n for n in visual['nodes'] if n['id'] == move['id'])['position']
        assert sum((position[k]-move['from'][k])**2 for k in 'xyz') < .0025, 'Fixture changed since review; do not apply twice.'
changed_nodes, changed_solids = apply_fixture_moves(visual, world, plan)
previous = world['sourceHash']
current = hashlib.sha256(previous.encode()+plan_file.read_bytes()).hexdigest()
world['sourceHash'] = patch['sourceHash'] = review['sourceHash'] = current
patch['nodes'] = sorted(set(patch['nodes']) | changed_nodes)
patch['solids'] = sorted(set(patch['solids']) | changed_solids)
backup = report/'refinements'/('access-'+datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S'))
for name, value in zip(paths, values):
    destination = backup/name
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(assets/name, destination)
    temporary = (assets/name).with_suffix('.json.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
for name in paths:
    os.replace((assets/name).with_suffix('.json.tmp'), assets/name)
shutil.copy2(plan_file, assets/'Imported/overall-map-fixtures.json')
(report/'fixture-source-update.json').write_text(json.dumps(dict(sourceHash=current, previousSourceHash=previous,
    benches=len(changed_nodes), nativeLights=2, solids=len(changed_solids), backup=str(backup)), indent=2), encoding='utf-8')
print(current)
