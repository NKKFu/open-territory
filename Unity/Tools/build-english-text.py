"""Build the English-only presentation catalog without changing gameplay IDs."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def build():
    entries = []
    seen = {}
    for number, line in enumerate((ROOT / 'Localization/English.tsv').read_text(encoding='utf-8').splitlines(), 1):
        if not line or line.startswith('#'):
            continue
        if '\t' not in line:
            raise ValueError('Missing translation separator on line %d' % number)
        source, target = (part.strip().replace('\\n', '\n') for part in line.split('\t', 1))
        if not source or not target:
            raise ValueError('Empty translation on line %d' % number)
        key = source.casefold()
        if key in seen:
            if seen[key] != target:
                raise ValueError('Conflicting translation: ' + source)
            continue
        seen[key] = target
        entries.append(dict(source=source, target=target))
    target = ROOT / 'OpenTerritory/Assets/Resources/English.json'
    target.write_text(json.dumps(dict(language='en', entries=entries), indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print('English presentation entries:', len(entries))
    return entries

if __name__ == '__main__':
    build()
