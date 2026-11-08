"""Import licensed, human-authored effects and performances; never synthesize audio.

Former procedural assets keep their IDs. Three recorded pain variations
use CityAudio's existing variant selection. Original downloads remain outside
Assets with their URLs, license evidence and SHA-256 hashes.
"""
from pathlib import Path
import argparse
import io
import json
import math
import shutil
import subprocess
import wave
import zipfile

import ambient_recordings as ambient
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'AssetLibrary/AudioSources/effects'
OUT = ROOT / 'OpenTerritory/Assets/Resources/Audio'
REPORT = ROOT / 'Reports/audio'
RATE = 44100
WEAPON_DESIGN_IDS = {'pulse', 'pulse-0', 'pulse-1', 'pulse-2', 'reload-pulse', 'pulse-impact'} | {
    'bullet-%s-%d' % (surface, variant) for surface in ('metal', 'wood', 'stone', 'glass') for variant in range(3)}


def source_manifest():
    return json.loads((SOURCES / 'sources.json').read_text(encoding='utf-8'))


def recipes():
    rows = []

    def put(key, source, file, member=None, start=0, seconds=None, rms=.09,
            loop=False, category='effects', highpass=60, speed=1):
        rows.append(dict(id=key, source=source, file=file, member=member,
                         start=start, seconds=seconds, targetRms=rms, loop=loop,
                         category=category, highpass=highpass, speed=speed))

    takes = ['L1', 'R1', 'L2', 'R2']
    for surface, material in [('concrete', 'Stone'), ('grass', 'Sand')]:
        for i, take in enumerate(takes):
            put('footstep-%s-%d' % (surface, i), 'fantozzi', 'fantozzi-footsteps.7z',
                'Fantozzi-footsteps/flac/Fantozzi-%s%s.flac' % (material, take), rms=.075)
    for surface, source, file, peaks in [
            ('gravel', 'gravel-steps', 'gravel-steps.mp3', [1.2, 1.8, 2.3, 3.5]),
            ('wood', 'wood-steps', 'wood-steps.mp3', [13.4, 15.4, 17.4, 19.6])]:
        for i, peak in enumerate(peaks):
            put('footstep-%s-%d' % (surface, i), source, file, start=peak-.065,
                seconds=.36, rms=.075, highpass=90)
    for i in range(3):
        put('impact-%d' % i, 'material-foley', 'material-foley.zip',
            'wood_hit_%02d.ogg' % (i + 1), rms=.12)

    for key, source, file, start, seconds, rms, overlap in [
            ('engine-car', 'car-engine', 'car-engine.mp3', .4, 5.7, .14, .2),
            ('engine-bus', 'bus-engine', 'bus-engine.mp3', 20, 9, .14, .3),
            ('engine-boat', 'boat-engine', 'boat-outboard.mp3', 12, 10, .14, .3),
            ('engine-drone', 'drone-engine', 'drone-engine.mp3', .1, 6.9, .12, .2),
            ('tire', 'road-tires', 'road-tires.mp3', 15, 9, .095, .4),
            ('skid', 'skid', 'car-skid.mp3', 3, 3.5, .11, .18),
            ('bicycle', 'bicycle-chain', 'bicycle-chain.mp3', 3, 9, .07, .3),
            ('siren', 'siren', 'siren.mp3', .05, 12.4, .16, .15),
            ('projector', 'projector', 'projector.mp3', 6, 10, .09, .3),
            ('winch', 'winch', 'winch.mp3', 4, 8, .10, .25)]:
        put(key, source, file, start=start, seconds=seconds, rms=rms, loop=True)
        rows[-1]['overlap'] = overlap
    put('wake', 'water-foley', 'water-foley.zip', 'loop_water_01.ogg', rms=.10, loop=True)
    rows[-1]['overlap'] = .12

    for kind, first in [('splash', 1), ('stroke', 6), ('water', 10)]:
        for i in range(3):
            put('%s-%d' % (kind, i), 'water-foley', 'water-foley.zip',
                'splash_%02d.ogg' % (first + i), rms=.12 if kind == 'splash' else .09)
    for i, member in enumerate(['wood_squeak_01.ogg', 'wood_squeak_02.ogg', 'wood_cracking_01.ogg']):
        put('mooring-%d' % i, 'material-foley', 'material-foley.zip', member, rms=.07)

    for key, member in [('door', 'DoorOpen01'), ('door-close', 'DoorClose01')]:
        put(key, 'door-foley', 'door-foley.7z', 'qubodup-DoorSet/flac/qubodup-%s.flac' % member, rms=.11)
    put('door-slide', 'sliding-door', 'sliding-door.mp3', start=.15, seconds=1.6, rms=.09)
    put('bus-door', 'bus-door', 'bus-door.mp3', seconds=2.2, rms=.10)
    put('horn', 'horn', 'horn.mp3', rms=.16)
    put('bell', 'bell', 'bell.mp3', seconds=2.5, rms=.09)
    put('phone', 'telephone', 'telephone.mp3', seconds=2.3, rms=.12)
    put('pulse', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/battle/magic1.wav', rms=.10)
    for key, member in [('metal', 'metal_hit_01.ogg'), ('plate', 'metal_sheet_01.ogg')]:
        put(key, 'material-foley', 'material-foley.zip', member, rms=.08)
    put('jump', 'human-reactions', 'human-reactions.7z', 'slightscream-11.flac', rms=.065, highpass=90)
    put('hurt', 'human-reactions', 'human-reactions.7z', 'slightscream-05.flac', rms=.095, highpass=90)
    for i in range(3):
        put('hurt-%d' % i, 'human-reactions', 'human-reactions.7z', 'slightscream-%02d.flac' % (i+1), rms=.095, highpass=90)
    put('land', 'fantozzi', 'fantozzi-footsteps.7z', 'Fantozzi-footsteps/flac/Fantozzi-StoneR3.flac', rms=.12)
    put('shove', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/battle/swing2.wav', rms=.09)
    put('pet', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/inventory/cloth.wav', rms=.05)
    put('brake', 'air-brake', 'air-brake.mp3', seconds=1.5, rms=.09, highpass=150)
    put('drone-start', 'drone-engine', 'drone-engine.mp3', seconds=1.2, rms=.09)
    put('film-reel', 'projector', 'projector.mp3', start=2.6, seconds=1.2, rms=.08)
    put('harbor-winch', 'winch', 'winch.mp3', start=2, seconds=1.1, rms=.09)
    put('register', 'register', 'register.mp3', rms=.10)
    put('hack', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/interface/interface1.wav', rms=.07)
    put('applause', 'applause', 'applause.mp3', start=2.5, seconds=2.4, rms=.10)
    put('radio', 'material-foley', 'material-foley.zip', 'keys_02.ogg', rms=.06)
    for key, source, file, rms in [
            ('music-street', 'guitar-d', 'guitar-d.mp3', .093),
            ('radio-0', 'guitar-jazz', 'guitar-jazz.mp3', .093),
            ('radio-1', 'piano-major', 'piano-major.mp3', .12),
            ('radio-2', 'piano-waltz', 'piano-waltz.mp3', .09)]:
        put(key, source, file, rms=rms, loop=True, category='music', highpass=30)
    for i, member, start in [(0, 'battle/magic1.wav', 0), (1, 'battle/spell.wav', 0), (2, 'battle/spell.wav', .35)]:
        put('pulse-%d' % i, 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/'+member, start=start, seconds=.5, rms=.10)
    put('reload-pulse', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/interface/interface6.wav', rms=.08)
    rows[-1]['fitSeconds'] = .98
    put('pulse-impact', 'rpg-foley', 'rpg-sound-pack.zip', 'RPG Sound Pack/interface/interface3.wav', seconds=.23, rms=.07)
    for surface in ['metal', 'wood', 'stone', 'glass']:
        for i in range(3):
            if surface in ['metal', 'wood']:
                source, file = 'material-foley', 'material-foley.zip'
                member = '%s_hit_%02d.ogg' % (surface, i + (2 if surface == 'metal' else 5))
            else:
                source, file = 'breaking-foley', 'breaking-foley.zip'
                member = 'bfh1_%s_breaking_%02d.ogg' % ('rock' if surface == 'stone' else 'glass', i+1)
            put('bullet-%s-%d' % (surface, i), source, file, member, seconds=.65 if surface == 'glass' else .4, rms=.08)
    if len(rows) != 89 or len({row['id'] for row in rows}) != 89:
        raise ValueError('Incomplete or duplicated effect recipe IDs')
    return rows


def read_recipe(recipe):
    path = SOURCES / recipe['file']
    member = recipe.get('member')
    if member and path.suffix == '.zip':
        with zipfile.ZipFile(path) as archive:
            data = archive.read(member)
        handle = io.BytesIO(data)
    elif member:
        exe = shutil.which('7z') or shutil.which('7za')
        if not exe:
            raise FileNotFoundError('7-Zip is required to read the original FLAC/WAV archives.')
        result = subprocess.run([exe, 'e', '-so', str(path), member], capture_output=True, check=True)
        handle = io.BytesIO(result.stdout)
    else:
        handle = path
    with ambient.sf.SoundFile(handle) as source:
        source.seek(min(round(recipe['start'] * source.samplerate), len(source)))
        frames = -1 if recipe['seconds'] is None else round(recipe['seconds'] * source.samplerate)
        samples = source.read(frames=frames, dtype='float64', always_2d=True)
        rate = source.samplerate
    x = ambient.resample(np.mean(samples, axis=1, keepdims=True), rate)
    if recipe['speed'] != 1:
        x = ambient.signal.resample(x, round(len(x) / recipe['speed']), axis=0)
    if recipe['highpass'] and len(x) > 64:
        x = ambient.highpass(x, recipe['highpass'])
    if not recipe['loop']:
        loud = np.flatnonzero(np.max(np.abs(x), axis=1) > np.max(np.abs(x)) * .008)
        if len(loud):
            x = x[max(0, loud[0]-round(.006*RATE)):min(len(x), loud[-1]+round(.05*RATE))]
    if recipe.get('fitSeconds'):
        x = ambient.signal.resample(x, round(recipe['fitSeconds'] * RATE), axis=0)
    if len(x) < round(.03 * RATE) or not np.isfinite(x).all():
        raise ValueError('Empty or invalid recording: ' + recipe['id'])
    if recipe['loop'] and recipe['category'] != 'music':
        x = ambient.loop_join(x, recipe.get('overlap', .12))
    elif recipe['category'] == 'music':
        # Preserve the bar length of already loopable human performances.
        count = min(round(.005 * RATE), len(x) // 10)
        x[-count:] -= np.linspace(0, 1, count)[:, None] * (x[-1] - x[0])
        x = np.tile(x, (max(1, math.ceil(17 / (len(x) / RATE))), 1))
    return x


def prepare_recordings(only_ids=None):
    manifest = source_manifest()
    lock = json.loads((SOURCES / 'downloads.json').read_text(encoding='utf-8'))
    source_by_id = {source['id']: source for source in manifest['sources']}
    selected = [recipe for recipe in recipes() if only_ids is None or recipe['id'] in only_ids]
    needed_sources = {recipe['source'] for recipe in selected}
    for source in manifest['sources']:
        if source['id'] not in needed_sources:
            continue
        for item in source['files']:
            name = item['name']
            if name not in lock or not (SOURCES / name).exists() or ambient.digest(SOURCES / name) != lock[name]['sha256']:
                raise ValueError('Missing or changed source: ' + name)
    entries = {}
    OUT.mkdir(parents=True, exist_ok=True)
    for recipe in selected:
        x = read_recipe(recipe)
        x -= np.mean(x, axis=0)
        rms = max(float(np.sqrt(np.mean(x*x))), 1e-9)
        if recipe['loop'] and float(np.max(np.abs(x))) * recipe['targetRms'] / rms > .82:
            knee = rms * 5
            x = knee * np.tanh(x/knee)
        x *= recipe['targetRms'] / max(float(np.sqrt(np.mean(x*x))), 1e-9)
        if not recipe['loop']:
            first = min(round(.003*RATE), len(x)//10)
            last = min(round(.025*RATE), len(x)//5)
            x[:first] *= np.linspace(0, 1, first)[:, None]
            x[-last:] *= np.linspace(1, 0, last)[:, None]
        peak = float(np.max(np.abs(x)))
        if peak > .82:
            x *= .82/peak
        pcm = np.round(x*32767).astype('<i2')
        path = OUT / (recipe['id'] + '.wav')
        temporary = path.with_suffix('.wav.tmp')
        with wave.open(str(temporary), 'wb') as output:
            output.setnchannels(1); output.setsampwidth(2); output.setframerate(RATE)
            output.writeframes(pcm.tobytes())
        temporary.replace(path)
        samples = pcm.astype(np.float64)/32768
        source = source_by_id[recipe['source']]
        entries[recipe['id']] = dict(id=recipe['id'], category=recipe['category'], loop=recipe['loop'],
            seconds=len(pcm)/RATE, channels=1, peak=round(float(np.max(np.abs(samples))),6),
            rms=round(float(np.sqrt(np.mean(samples*samples))),6),
            seam=round(float(np.max(np.abs(samples[-1]-samples[0]))),6),
            source=recipe['source'], author=source['author'], sourceUrl=source['page'],
            license='CC0-1.0', sourceFiles=[recipe['file'] + ('::'+recipe['member'] if recipe['member'] else '')],
            sourceDownloadSha256=lock[recipe['file']]['sha256'], recipe=recipe, sha256=ambient.digest(path))
    return entries


def write_credits(bank):
    lines = ['Baia do Sol / Open Territory - audio credits', '',
             'All listed assets are licensed CC0 1.0 Universal:',
             'https://creativecommons.org/publicdomain/zero/1.0/',
             'Credits retained voluntarily. Free use, modification and redistribution.', '',
             'The bank uses human recordings, performances and authored studio effects.',
             'No locally synthesized placeholders remain. No AI-generated asset is knowingly included.', '',
             'Sources and the game files that use them:', '']
    for source in bank['sources']:
        ids = [e['id'] + '.wav' for e in bank['clips'] if e['source'] == source['id']]
        if not ids:
            continue
        lines.extend([source['title'], 'By ' + source['author'], source['page'], ', '.join(ids), ''])
    lines.extend(['Processing: excerpts, mono mixing for positional sources, 44.1kHz PCM 16 bit,',
                  'rumble filtering, level matching, fades and loop overlaps. Existing stereo',
                  'environment beds are preserved. Short music performances repeat complete bars.',
                  'Freesound assets use publicly available HQ MP3 previews; OpenGameArt uses',
                  'the posted original archives. Source files, SHA-256 locks and license evidence',
                  'are retained in Unity/AssetLibrary/AudioSources. Import recipes are kept in',
                  'bank.json, Unity/Tools/effect_recordings.py and Unity/Tools/npc_recordings.py.',
                  'NPC conversations use recorded nonlexical murmurs alongside English text;',
                  'they are not generated speech or a full spoken translation of the dialogue.',
                  'Runtime audio is fully offline.', ''])
    (OUT / 'CREDITS.txt').write_text('\n'.join(lines), encoding='utf-8')


def replace_bank():
    entries = prepare_recordings()
    bank_path = OUT / 'bank.json'
    bank = json.loads(bank_path.read_text(encoding='utf-8'))
    original_ids = {entry['id'] for entry in bank['clips']}
    bank['clips'] = [entries.get(entry['id'], entry) for entry in bank['clips']]
    bank['clips'].extend(entry for key, entry in entries.items() if key not in original_ids)
    bank.update(ambient.metadata(source_manifest()['sources']))
    if any(not entry.get('source') or entry.get('license') != 'CC0-1.0' for entry in bank['clips']):
        raise ValueError('Rebuild the complete bank to remove every procedural fallback.')
    bank_path.write_text(json.dumps(bank, indent=2) + '\n', encoding='utf-8')
    REPORT.mkdir(parents=True, exist_ok=True)
    (REPORT / 'bank-analysis.json').write_text(json.dumps(bank, indent=2)+'\n', encoding='utf-8')
    write_credits(bank)
    print('Imported %d licensed effects and performances; existing recorded weapons and ambience preserved.' % len(entries))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true')
    parser.add_argument('--sources-only', action='store_true')
    args = parser.parse_args()
    if args.download:
        ambient.download_sources(SOURCES/'sources.json', SOURCES)
    if not args.sources_only:
        replace_bank()
