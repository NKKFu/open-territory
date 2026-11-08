"""Stable residential sign IDs, shared by regeneration and the one-off updater."""
import copy
import hashlib
import json
from PIL import Image, ImageDraw, ImageFont


def apply_house_ids(visual, world, assets, reports):
    records = []
    used = set()
    for index, place in enumerate(world['places']):
        if place['kind'] not in ('home', 'residence'):
            continue
        # Door IDs survive rebuilding and don't depend on a home's current position.
        code = hashlib.sha256(('baia-house:' + str(place['door']['id'])).encode()).hexdigest()[:6].upper()
        assert code not in used, 'Residential ID collision'
        used.add(code)
        label = 'Reused building-' + str(index) if index < 17 else 'Reused residential interior ' + str(index - 17)
        signs = []
        for node in visual['nodes']:
            if node['name'] != label:
                continue
            for slot, mi in enumerate(node['materials']):
                material = visual['materials'][mi]
                # These are the four donor residential facade boards, not interior art.
                if material['texture'] not in ('texture-25', 'texture-27', 'texture-28', 'texture-30', 'house-' + code):
                    continue
                texture = 'house-' + code
                image = Image.new('RGB', (1024, 256), '#315e59')
                draw = ImageDraw.Draw(image)
                draw.rectangle((9, 9, 1014, 246), outline='#eee4c4', width=12)
                font = ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf', 142)
                draw.text((512, 128), '#' + code, font=font, anchor='mm', fill='#fff3d2')
                image.save(assets / 'Imported/Textures' / (texture + '.png'))
                if material['texture'] != texture:
                    replacement = copy.deepcopy(material)
                    replacement.update(name=texture, texture=texture)
                    node['materials'][slot] = len(visual['materials'])
                    visual['materials'].append(replacement)
                signs.append(node['id'])
        assert signs, 'No facade board for ' + label
        place['name'] = '#' + code
        records.append(dict(hash=code, door=place['door']['id'], x=place['x'], z=place['z'], signs=signs))
    reports.mkdir(parents=True, exist_ok=True)
    (reports / 'houses.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
    return records


if __name__ == '__main__':
    from pathlib import Path
    root = Path(__file__).resolve().parents[1]
    assets = root / 'OpenTerritory/Assets'
    vp, wp = assets / 'Imported/visual.json', assets / 'Resources/world.json'
    visual = json.loads(vp.read_text(encoding='utf-8'))
    world = json.loads(wp.read_text(encoding='utf-8'))
    records = apply_house_ids(visual, world, assets, root / 'Reports/house-ids')
    for path, data in ((vp, visual), (wp, world)):
        path.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    fp = assets / 'Resources/building-frontages.json'
    frontages = json.loads(fp.read_text(encoding='utf-8'))
    names = {p['door']['id']: p['name'] for p in world['places']}
    for entrance in frontages['entrances']:
        entrance['name'] = names[entrance['door']]
    fp.write_text(json.dumps(frontages, separators=(',', ':')), encoding='utf-8')
    print(f'Updated {len(records)} unique residential signs')
