"""Narrow the moving leaves without moving an entrance or rebuilding a district."""
import copy
import hashlib
import json
import math
from pathlib import Path


def apply_doorways(visual, world):
    nodes = {n['id']: n for n in visual['nodes']}
    children = {}
    for node in visual['nodes']:
        children.setdefault(node['parent'], []).append(node)
    next_id = max(nodes) + 1
    changed = []

    def resize(part, width, original):
        # Keep the stiles and handles at their original thickness. Only the
        # horizontal rails and glass span the new width.
        if abs(part['position']['z']) > .05:
            part['position']['x'] = -width + .28
        elif part['scale']['x'] < .2:
            part['position']['x'] = -.075 if part['position']['x'] > -original / 2 else -width + .075
        else:
            part['position']['x'] = -width / 2
            part['scale']['x'] = width - (original - part['scale']['x'])

    for place in world['places']:
        door = place['door']
        if door['automatic']:
            continue
        width = 1.4 if place['kind'] in ('home', 'residence') else 1.6
        original = door['width']
        if original <= width + .001:
            continue
        root = nodes[door['id']]
        parts = children[root['id']]
        if len(parts) != 7 or any(p['geometry'] < 0 for p in parts):
            raise ValueError('Unsupported donor door: ' + str(root['id']))
        templates = copy.deepcopy(parts)
        origin = copy.deepcopy(root['position'])
        side = (original - width) / 2
        yaw = door.get('yaw', 0)

        def point(x):
            return dict(x=origin['x'] + math.cos(yaw) * x, y=origin['y'], z=origin['z'] - math.sin(yaw) * x)

        root['position'] = point(-side)
        door['position'] = copy.deepcopy(root['position'])
        door['width'] = width
        for part in parts:
            resize(part, width, original)

        # Fixed glazed sidelights fill the former leaf width. The facade,
        # threshold, interior, navigation and center of the entrance stay put.
        for name, offset in [('right', 0), ('left', -side - width)]:
            fixed = copy.deepcopy(root)
            fixed.update(id=next_id, name=f'Doorway sidelight-{root["id"]}-{name}', dynamic=False, position=point(offset))
            next_id += 1
            visual['nodes'].append(fixed)
            for template in templates:
                if abs(template['position']['z']) > .05:
                    continue  # No handles on the fixed panels.
                part = copy.deepcopy(template)
                part.update(id=next_id, parent=fixed['id'], dynamic=False, name=f'Doorway fixed part-source-{template["id"]}')
                next_id += 1
                resize(part, side, original)
                visual['nodes'].append(part)
            center = point(offset - side / 2)
            world['solids'].append(dict(index=len(world['solids']), owner=fixed['id'], x=center['x'], z=center['z'],
                w=side, d=.16, bottom=origin['y'], top=origin['y'] + 3.3, yaw=yaw,
                active=True, vault=False, opaque=False, rangeBackstop=False))
        changed.append(dict(door=root['id'], kind=place['kind'], before=original, after=width))
    if changed:
        source = world['sourceHash'] + Path(__file__).read_text(encoding='utf-8')
        world['sourceHash'] = hashlib.sha256(source.encode()).hexdigest()
    return changed


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    assets = root / 'OpenTerritory/Assets'
    visual_path = assets / 'Imported/visual.json'
    world_path = assets / 'Resources/world.json'
    visual = json.loads(visual_path.read_text(encoding='utf-8-sig'))
    world = json.loads(world_path.read_text(encoding='utf-8-sig'))
    changes = apply_doorways(visual, world)
    if changes:
        # Keep exact pre-change inputs for the targeted scene updater and review.
        backup = root / 'OpenTerritory/Temp/DoorQA'
        backup.mkdir(parents=True, exist_ok=True)
        staged = []
        for path, data in [(visual_path, visual), (world_path, world)]:
            (backup / (path.stem + '-before.json')).write_bytes(path.read_bytes())
            temporary = path.with_suffix('.json.doors.tmp')
            temporary.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
            staged.append((path, temporary))
        for path, temporary in staged:
            temporary.replace(path)
        report_dir = root / 'Reports/doors'
        report_dir.mkdir(parents=True, exist_ok=True)
        (report_dir / 'geometry.json').write_text(json.dumps(changes, indent=2), encoding='utf-8')
    print('Narrowed moving leaves:', len(changes))
