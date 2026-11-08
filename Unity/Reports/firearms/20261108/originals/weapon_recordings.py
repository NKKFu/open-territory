"""Prepare licensed weapon recordings and imported studio/foley effects.

Run --download once to cache the CC0 originals, then rebuild entirely offline.
Originals, license evidence and SHA-256 locks stay outside Unity Assets.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import urllib.request
import wave

sys.path.insert(0, str(Path(os.environ.get('TEMP', '.')) / 'open-territory-audio-deps'))
sys.path.append(str(Path(os.environ.get('TEMP', '.')) / 'open-territory-map-trace-deps'))
import numpy as np
import soundfile as sf
from scipy import signal

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'AssetLibrary/WeaponAudioSources'
OUT = ROOT / 'OpenTerritory/Assets/Resources/Audio'
RATE = 44100
LICENSE = 'https://creativecommons.org/publicdomain/zero/1.0/'
ARCHIVE = 'https://opengameart.org/sites/default/files/Prepared%20SFX%20Library.7z'
SPECS = [
    dict(id='free-firearms', title='The Free Firearm Sound Library', author='Ben Jaszczak and the Free Firearm Sound Library team',
         page='https://opengameart.org/content/the-free-firearm-sound-library', archive=ARCHIVE,
         files=[dict(name='walther-ppq.wav', member='Prepared SFX Library/Walther PPQ/X_39P.wav'),
                dict(name='ar15.wav', member='Prepared SFX Library/AR-15/D_32P.wav')]),
    dict(id='reloads', title='Gun reload sounds', author='SpringySpringo', page='https://opengameart.org/content/gun-reload-sounds',
         files=[dict(name='pistol-reload.wav', url='https://opengameart.org/sites/default/files/gunreload1.wav'),
                dict(name='rifle-reload.wav', url='https://opengameart.org/sites/default/files/assaultriflereload1_0.wav')]),
    dict(id='mechanism', title='Gun Reload Sound Effects', author='Brian MacIntosh (BMacZero)', page='https://opengameart.org/content/gun-reload-sound-effects',
         files=[dict(name='clip-load.wav', url='https://opengameart.org/sites/default/files/clipload1.wav'),
                dict(name='single-bullet.wav', url='https://opengameart.org/sites/default/files/singlebullet1.wav')]),
]


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'OpenTerritoryAudioImporter/1.0'})
    with urllib.request.urlopen(request, timeout=90) as response:
        return response.read()


def download():
    import py7zr
    SOURCES.mkdir(parents=True, exist_ok=True)
    evidence = SOURCES / 'licenses'
    evidence.mkdir(exist_ok=True)
    cache = Path(os.environ.get('TEMP', '.')) / 'open-territory-weapon-audio'
    cache.mkdir(exist_ok=True)
    locked = {}
    for spec in SPECS:
        page = fetch(spec['page'])
        if b'publicdomain/zero' not in page and b'CC0' not in page:
            raise ValueError('CC0 evidence absent: ' + spec['page'])
        (evidence / (spec['id'] + '.html')).write_bytes(page)
        if spec.get('archive'):
            archive = cache / 'Prepared-SFX-Library.7z'
            if not archive.exists():
                archive.write_bytes(fetch(spec['archive']))
            selected = [item['member'] for item in spec['files']]
            with py7zr.SevenZipFile(archive) as collection:
                collection.extract(path=cache / 'prepared', targets=selected)
        for item in spec['files']:
            target = SOURCES / item['name']
            if item.get('member'):
                shutil.copyfile(cache / 'prepared' / item['member'], target)
            else:
                target.write_bytes(fetch(item['url']))
            # Fail on an HTML error response masquerading as a WAV.
            sf.info(target)
            locked[item['name']] = dict(sha256=sha(target), bytes=target.stat().st_size,
                                       source=spec['id'], original=item.get('member', item.get('url')))
    manifest = dict(license='CC0-1.0', licenseUrl=LICENSE, sources=SPECS,
                    retrievedAtUtc=datetime.now(timezone.utc).isoformat(), files=locked)
    (SOURCES / 'sources.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    import ambient_recordings as ambient
    import effect_recordings as effects
    needed = {r['source'] for r in effects.recipes() if r['id'] in effects.WEAPON_DESIGN_IDS}
    ambient.download_sources(effects.SOURCES / 'sources.json', effects.SOURCES, source_ids=needed)
    print('Cached and verified %d CC0 original recordings.' % len(locked))


def read(name):
    x, rate = sf.read(SOURCES / name, dtype='float64', always_2d=True)
    x = np.mean(x, axis=1)
    if rate != RATE:
        divisor = math.gcd(rate, RATE)
        x = signal.resample_poly(x, RATE // divisor, rate // divisor)
    return x


def trim(x, threshold=.006):
    active = np.flatnonzero(abs(x) > max(abs(x)) * threshold)
    return x[max(0, active[0] - 44):min(len(x), active[-1] + 220)] if len(active) else x


def save(key, x, source=None, originals=None):
    x = np.array(x, dtype='float64', copy=True)
    x -= np.mean(x)
    fade = min(330, len(x) // 4)
    x[:22] *= np.linspace(0, 1, 22)
    x[-fade:] *= np.linspace(1, 0, fade)
    peak = max(1e-9, float(max(abs(x))))
    x *= .82 / peak
    pcm = np.round(x * 32767).astype('<i2')
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / (key + '.wav')), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    decoded = pcm.astype(np.float64) / 32768
    return dict(id=key, category='effects', loop=False, seconds=round(len(x)/RATE, 6), channels=1,
                peak=round(float(max(abs(decoded))), 6), rms=round(float(np.sqrt(np.mean(decoded*decoded))), 6),
                seam=round(float(abs(decoded[-1]-decoded[0])), 6), sha256=sha(OUT / (key + '.wav')),
                source=source or 'Open Territory original sound design',
                license='CC0-1.0' if source else 'Original', sourceFiles=originals or [])


def shots(name, seconds):
    x = read(name)
    window = 220
    envelope = np.max(abs(x[:len(x)//window*window].reshape(-1, window)), axis=1)
    peaks, _ = signal.find_peaks(envelope, height=max(envelope)*.45, distance=round(.7*RATE/window))
    if len(peaks) < 2:
        raise ValueError('Expected two separate single shots in ' + name)
    result = []
    for p in peaks[:2]:
        at = p*window
        # Find the leading transient, preserving its attack rather than the
        # quiet recording lead-in or a later echo.
        look = max(0, at-round(.06*RATE))
        onset = np.flatnonzero(abs(x[look:at+window]) > .05*max(abs(x)))
        start = max(0, look+int(onset[0])-44)
        y = x[start:start+round(seconds*RATE)]
        y = signal.sosfilt(signal.butter(2, 75, 'highpass', fs=RATE, output='sos'), y)
        # Retain the recorded outdoor tail but shorten it for automatic fire.
        t = np.arange(len(y))/RATE
        y *= np.exp(-np.maximum(0, t-.10)*3)
        result.append(y)
    return result


def prepare_recordings(include_design=True):
    manifest = json.loads((SOURCES / 'sources.json').read_text(encoding='utf-8'))
    for name, item in manifest['files'].items():
        if sha(SOURCES / name) != item['sha256']:
            raise ValueError('Weapon source checksum mismatch: ' + name)
    entries = {}
    def put(key, x, source=None, originals=None):
        entries[key] = save(key, x, source, originals)
    for key, name, duration in [('shot', 'walther-ppq.wav', .7), ('carbine', 'ar15.wav', .52)]:
        recordings = shots(name, duration)
        for i, x in enumerate(recordings):
            put(key+'-'+str(i), x, 'free-firearms', [name])
        put(key, recordings[0], 'free-firearms', [name])
    for key, name, duration in [('reload-sidearm', 'pistol-reload.wav', 1.30), ('reload-carbine', 'rifle-reload.wav', 1.68)]:
        x = trim(read(name))
        x = signal.resample(x, round(duration*RATE))
        put(key, x, 'reloads', [name])
    latch = trim(read('clip-load.wav'))
    bullet = trim(read('single-bullet.wav'))
    put('reload', signal.resample(trim(read('pistol-reload.wav')), round(1.30*RATE)), 'reloads', ['pistol-reload.wav'])
    put('reload-ready', latch, 'mechanism', ['clip-load.wav'])
    put('empty', bullet[:round(.10*RATE)], 'mechanism', ['single-bullet.wav'])
    put('case-drop', bullet[:round(.20*RATE)], 'mechanism', ['single-bullet.wav'])
    if include_design:
        # Authored source assets replace the former oscillator/noise placeholders.
        from effect_recordings import WEAPON_DESIGN_IDS, prepare_recordings as prepare_effects
        entries.update(prepare_effects(only_ids=WEAPON_DESIGN_IDS))
    return entries


def metadata():
    return dict(weaponSources=SPECS, weaponLicense='CC0-1.0', weaponLicenseUrl=LICENSE,
                weaponAuthorship='Recorded pistol, rifle and mechanisms: CC0 authors in weaponSources. Pulsor and surface impacts: licensed third-party studio/foley assets in sources.')


def replace_bank():
    entries = prepare_recordings()
    count = len(entries)
    path = OUT / 'bank.json'
    bank = json.loads(path.read_text(encoding='utf-8'))
    bank.update(metadata())
    from ambient_recordings import metadata as source_metadata
    from effect_recordings import source_manifest as effect_sources
    bank.update(source_metadata(bank.get('sources', []) + SPECS + effect_sources()['sources']))
    bank['clips'] = [entries.pop(entry['id'], entry) for entry in bank['clips']]
    bank['clips'].extend(entries.values())
    path.write_text(json.dumps(bank, indent=2)+'\n', encoding='utf-8')
    report = ROOT / 'Reports/audio'
    report.mkdir(parents=True, exist_ok=True)
    (report / 'bank-analysis.json').write_text(json.dumps(bank, indent=2)+'\n', encoding='utf-8')
    print('Prepared %d licensed weapon/impact clips; ambience and music preserved.' % count)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true')
    args = parser.parse_args()
    if args.download:
        download()
    replace_bank()
