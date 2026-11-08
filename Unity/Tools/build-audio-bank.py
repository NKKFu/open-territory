"""Rebuild the entire bank from cached, licensed source audio. No synthesis.

Download sources with ambient_recordings.py --download and
effect_recordings.py --download and npc_recordings.py --download first.
Subsequent rebuilds are fully offline.
The former placeholder generator is archived only as provenance evidence in
AssetLibrary/AudioSources/legacy-procedural-generator.py.txt.
"""
import json
import ambient_recordings as ambience
import effect_recordings as effects
import weapon_recordings as weapons
import npc_recordings as npcs


if __name__ == '__main__':
    entries = ambience.prepare_recordings()
    entries.update(effects.prepare_recordings())
    entries.update(weapons.prepare_recordings(include_design=False))
    entries.update(npcs.prepare_recordings())
    if len(entries) != 129 or any(entry.get('license') != 'CC0-1.0' for entry in entries.values()):
        raise ValueError('Expected 129 licensed assets with no procedural fallback.')
    bank = dict(**ambience.metadata(effects.source_manifest()['sources'] + weapons.SPECS + npcs.source_manifest()['sources']), clips=list(entries.values()))
    bank['version'] = 5
    bank.update(weapons.metadata())
    (ambience.OUT / 'bank.json').write_text(json.dumps(bank, indent=2)+'\n', encoding='utf-8')
    ambience.REPORT.mkdir(parents=True, exist_ok=True)
    (ambience.REPORT / 'bank-analysis.json').write_text(json.dumps(bank, indent=2)+'\n', encoding='utf-8')
    effects.write_credits(bank)
    size = sum((ambience.OUT / (key+'.wav')).stat().st_size for key in entries)/1048576
    print('Imported %d licensed clips; %.1f MiB PCM; no locally synthesized placeholders.' % (len(entries), size))
