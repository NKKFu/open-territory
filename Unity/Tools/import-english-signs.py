"""Replace sign textures by matching original pixels, preserving geometry and GUIDs."""
import hashlib
import json
import shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'AssetLibrary/EnglishSigns'
TEXTURES = ROOT / 'OpenTerritory/Assets/Imported/Textures'
BACKUP = SOURCE / 'Backups'

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def pixels(path):
    with Image.open(path) as image:
        image = image.convert('RGBA')
        return hashlib.sha256(str(image.size).encode() + image.tobytes()).hexdigest()

def run():
    BACKUP.mkdir(parents=True, exist_ok=True)
    canvases = json.loads((SOURCE / 'canvases.json').read_text(encoding='utf-8'))
    by_pixels = {}
    for row in canvases:
        by_pixels.setdefault(pixels(SOURCE / row['original']), []).append(row)
    matches, seen = [], set()
    for path in sorted(TEXTURES.glob('*.png')):
        original = BACKUP / path.name if (BACKUP / path.name).exists() else path
        rows = by_pixels.get(pixels(original))
        if not rows:
            continue
        row = rows[0]
        seen.update(item['id'] for item in rows)
        if not (BACKUP / path.name).exists():
            shutil.copy2(path, BACKUP / path.name)
        translated = SOURCE / row['english']
        if digest(path) != digest(translated):
            shutil.copyfile(translated, path)
        matches.append(dict(texture=path.name, sourceTexts=row['texts'], originalSha256=digest(BACKUP / path.name), englishSha256=digest(path)))

    # The native map has its own roads and coastline. Repaint only its text areas.
    map_matches = []
    font_path = 'C:/Windows/Fonts/arialbd.ttf'
    def font(size): return ImageFont.truetype(font_path, size)
    for index in (42, 43):
        path = TEXTURES / ('texture-%d.png' % index)
        original = BACKUP / path.name
        if not original.exists(): shutil.copy2(path, original)
        board = Image.open(original).convert('RGB')
        if board.size != (850, 548):
            raise ValueError('The native guide layout changed: ' + str(path))
        map_before = board.crop((24, 148, 492, 489)).tobytes()
        draw = ImageDraw.Draw(board)
        draw.rectangle((0, 0, 849, 124), fill='#213e45')
        draw.text((25, 15), 'COASTAL GUIDE  /  01', font=font(20), fill='#b1cebb')
        draw.text((24, 49), 'Sun Bay', font=font(56), fill='#f2e6c6')
        draw.text((572, 74), 'COASTAL GARDENS', font=font(19), fill='#e0ac5a')
        draw.rectangle((510, 125, 849, 515), fill='#f2e6c6')
        labels = ['You are here', 'Beacon Garage', 'Bay Hospital', 'Tide Market', 'Antenna Docks', 'Passage Cinema']
        palette = ['#213e45','#ad6750','#527e65','#38828a','#a4673d','#805567']
        for i, title in enumerate(labels):
            y = 177 + i*57
            draw.ellipse((530, y-14, 558, y+14), fill=palette[i])
            draw.text((537, y-11), chr(65+i), font=font(19), fill='white')
            draw.text((577, y-15), title, font=font(25), fill='#213e45')
        draw.rectangle((0, 500, 849, 547), fill='#f2e6c6')
        draw.text((25, 516), 'INLETS  /  HILLS  /  NEIGHBORHOODS', font=font(15), fill='#41675f')
        if board.crop((24, 148, 492, 489)).tobytes() != map_before:
            raise ValueError('Native map artwork changed unexpectedly.')
        board.save(path)
        map_matches.append(dict(texture=path.name, originalSha256=digest(original), englishSha256=digest(path), mapPixelsPreserved=True))
    report = dict(translatedTextures=matches, nativeGuideTextures=map_matches,
        unmatchedSourceCanvases=[row for row in canvases if row['id'] not in seen])
    report_dir = ROOT / 'Reports/english'; report_dir.mkdir(parents=True, exist_ok=True)
    (report_dir / 'signs.json').write_text(json.dumps(report, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    print('Matched %d sign textures; updated two native guide boards.' % len(matches))
    print('Unmatched source canvases:', len(report['unmatchedSourceCanvases']))
    for row in report['unmatchedSourceCanvases']: print(row['id'], row['texts'])

if __name__ == '__main__': run()
