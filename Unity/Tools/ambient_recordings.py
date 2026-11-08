"""Download and prepare the CC0 environmental recordings used by the native game.

Run with --download once; subsequent imports and bank rebuilds work offline.
Requires NumPy, SciPy and SoundFile (also checks TEMP/open-territory-audio-deps).
Original downloads, license evidence and SHA-256 locks live outside Unity Assets.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
import argparse
import hashlib
import io
import json
import math
import os
import re
import sys
import urllib.request
import wave
import zipfile

sys.path.insert(0, str(Path(os.environ.get('TEMP', '.')) / 'open-territory-audio-deps'))
sys.path.append(str(Path(os.environ.get('TEMP', '.')) / 'open-territory-map-trace-deps'))
import numpy as np
from scipy import signal
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'AssetLibrary/AudioSources'
OUT = ROOT / 'OpenTerritory/Assets/Resources/Audio'
REPORT = ROOT / 'Reports/audio'
RATE = 44100
RECORDED_IDS = {'sea', 'wind', 'rain', 'leaves', 'night', 'fountain',
                'bird-0', 'bird-1', 'bird-2', 'bird-3'}


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def source_manifest():
    return json.loads((SOURCES / 'sources.json').read_text(encoding='utf-8'))


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'OpenTerritoryAudioImporter/1.0'})
    with urllib.request.urlopen(request, timeout=90) as response:
        return response.read()


def download_sources(manifest_path=None, directory=None, source_ids=None):
    directory = Path(directory) if directory else SOURCES
    manifest = json.loads(Path(manifest_path).read_text(encoding='utf-8')) if manifest_path else source_manifest()
    lock_path = directory / 'downloads.json'
    lock = json.loads(lock_path.read_text(encoding='utf-8')) if lock_path.exists() else {}
    pending = []
    license_dir = directory / 'licenses'
    license_dir.mkdir(parents=True, exist_ok=True)
    for source in manifest['sources']:
        if source_ids is not None and source['id'] not in source_ids:
            continue
        evidence_path = license_dir / (source['id'] + '.json')
        if evidence_path.exists():
            evidence = json.loads(evidence_path.read_text(encoding='utf-8'))
            cached = all(item['name'] in lock and (directory / item['name']).exists() and
                         digest(directory / item['name']) == lock[item['name']]['sha256'] for item in source['files'])
            if cached and evidence.get('page') == source['page'] and evidence.get('author') == source['author']:
                continue
        page = fetch(source['page']).decode('utf-8', errors='replace')
        if 'publicdomain/zero' not in page and 'CC0' not in page:
            raise ValueError('CC0 license evidence missing: ' + source['page'])
        evidence = dict(title=source['title'], author=source['author'], page=source['page'],
                        license=manifest['license'], licenseUrl=manifest['licenseUrl'],
                        retrievedAtUtc=datetime.now(timezone.utc).isoformat(),
                        pageSha256=hashlib.sha256(page.encode('utf-8')).hexdigest())
        (license_dir / (source['id'] + '.json')).write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
        for item in source['files']:
            url = item.get('url')
            if item.get('publicPreview'):
                matches = re.findall(r'https://cdn\.freesound\.org/previews/[^\s"<>]+-hq\.mp3', page)
                if not matches:
                    raise ValueError('Public high quality preview missing: ' + source['page'])
                url = matches[0]
            pending.append((item['name'], url))

    def download(item):
        name, url = item
        path = directory / name
        old = lock.get(name)
        if not old or not path.exists() or digest(path) != old['sha256']:
            print('Downloading ' + name, flush=True)
            data = fetch(url)
            actual = hashlib.sha256(data).hexdigest()
            if old and actual != old['sha256']:
                raise ValueError('Source changed; review the download before updating its hash: ' + name)
            path.write_bytes(data)
        return name, dict(url=url, sha256=digest(path), bytes=path.stat().st_size)

    with ThreadPoolExecutor(max_workers=3) as pool:
        for future in as_completed([pool.submit(download, item) for item in pending]):
            name, info = future.result()
            lock[name] = info
            lock_path.write_text(json.dumps(lock, indent=2) + '\n', encoding='utf-8')
    print('Downloaded and verified %d original files.' % len(pending), flush=True)


def read_audio(name, start=0, seconds=None):
    path = SOURCES / name
    with sf.SoundFile(path) as source:
        source.seek(min(round(start * source.samplerate), len(source)))
        count = -1 if seconds is None else round(seconds * source.samplerate)
        x = source.read(frames=count, dtype='float64', always_2d=True)
        rate = source.samplerate
    return resample(x, rate)


def resample(x, rate):
    if rate != RATE:
        common = math.gcd(rate, RATE)
        x = signal.resample_poly(x, RATE // common, rate // common, axis=0)
    return x


def highpass(x, hz):
    return signal.sosfiltfilt(signal.butter(2, hz, 'highpass', fs=RATE, output='sos'), x, axis=0)


def loop_join(x, seconds=1.5):
    n = min(round(seconds * RATE), len(x) // 5)
    phase = np.linspace(0, np.pi / 2, n)[:, None]
    overlap = x[-n:] * np.cos(phase) + x[:n] * np.sin(phase)
    # The wrap now falls in the middle of an unchanged part of the recording.
    x = np.concatenate((x[n:-n], overlap))
    # Remove the remaining one-sample step without fading the entire bed to silence.
    count = round(.005 * RATE)
    x[-count:] -= np.linspace(0, 1, count)[:, None] * (x[-1] - x[0])
    return x


def write_clip(key, x, source_id, target_rms, loop, files, treatment):
    if len(x) < RATE // 4 or not np.isfinite(x).all():
        raise ValueError('Invalid or empty recording: ' + key)
    x -= np.mean(x, axis=0)
    rms = max(float(np.sqrt(np.mean(x * x))), 1e-9)
    if loop and float(np.max(np.abs(x))) * target_rms / rms > .84:
        # Isolated bird/microphone transients must not force the entire bed quiet.
        knee = rms * 5
        x = knee * np.tanh(x / knee)
        treatment += ' Gentle soft peak control before level matching.'
    x *= target_rms / max(float(np.sqrt(np.mean(x * x))), 1e-9)
    if not loop:
        x[:round(.012 * RATE)] *= np.linspace(0, 1, round(.012 * RATE))[:, None]
        x[-round(.075 * RATE):] *= np.linspace(1, 0, round(.075 * RATE))[:, None]
    peak = float(np.max(np.abs(x)))
    if peak > .84:
        x *= .84 / peak
    pcm = np.round(x * 32767).astype('<i2')
    path = OUT / (key + '.wav')
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(x.shape[1]); output.setsampwidth(2); output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    decoded = pcm.astype(np.float64) / 32768
    return dict(id=key, category='ambience', loop=loop, seconds=len(x) / RATE,
                channels=x.shape[1], peak=round(float(np.max(np.abs(decoded))), 6),
                rms=round(float(np.sqrt(np.mean(decoded * decoded))), 6),
                seam=round(float(np.max(np.abs(decoded[-1] - decoded[0]))), 6),
                source=source_id, license='CC0-1.0', sourceFiles=files,
                processing=treatment, sha256=digest(path))


def prepare_recordings():
    manifest = source_manifest()
    lock_path = SOURCES / 'downloads.json'
    if not lock_path.exists():
        raise FileNotFoundError('Download the CC0 sources first: python Tools/ambient_recordings.py --download')
    lock = json.loads(lock_path.read_text(encoding='utf-8'))
    for source in manifest['sources']:
        for item in source['files']:
            path = SOURCES / item['name']
            if not path.exists() or item['name'] not in lock or digest(path) != lock[item['name']]['sha256']:
                raise ValueError('Missing or changed source: ' + str(path))
    OUT.mkdir(parents=True, exist_ok=True)
    entries = {}

    def add(key, x, source_id, rms, loop, files, treatment):
        entries[key] = write_clip(key, x, source_id, rms, loop, files, treatment)

    sea = read_audio('ocean-waves-hq.mp3', 85, 42)
    add('sea', loop_join(highpass(sea, 55)), 'beach', .12, True, ['ocean-waves-hq.mp3'],
        'Public HQ preview; 42-second excerpt from 85s; 55 Hz high-pass; equal-power loop overlap; level matched.')
    add('wind', loop_join(highpass(read_audio('park-wind.wav', 20, 38), 100)),
        'park-wind', .066, True, ['park-wind.wav'],
        '38-second excerpt from 20s; 100 Hz high-pass for microphone rumble; loop overlap; level matched.')
    with zipfile.ZipFile(SOURCES / 'rain-ogg.zip') as archive:
        name = sorted(n for n in archive.namelist() if n.lower().endswith('.ogg'))[0]
        rain, rate = sf.read(io.BytesIO(archive.read(name)), dtype='float64', always_2d=True)
    add('rain', loop_join(highpass(resample(rain, rate), 70), .8), 'rain', .13, True,
        ['rain-ogg.zip::' + name], 'First rain take; 70 Hz high-pass; loop overlap; level matched.')
    add('leaves', loop_join(highpass(read_audio('wind-and-leaves-hq.mp3', 35, 34), 400)),
        'leaves', .057, True, ['wind-and-leaves-hq.mp3'],
        'Public HQ preview; 34-second excerpt from 35s; 400 Hz high-pass; loop overlap; level matched.')
    add('night', loop_join(highpass(read_audio('crickets.wav', 5, 32), 1400)),
        'crickets', .025, True, ['crickets.wav'],
        '32-second excerpt from 5s; 1400 Hz high-pass reduces distant background; loop overlap; level matched.')
    water = read_audio('fountain-toulouse-hq.mp3', 8, 28)
    water = np.mean(water, axis=1, keepdims=True)
    add('fountain', loop_join(highpass(water, 220)), 'fountain', .15, True,
        ['fountain-toulouse-hq.mp3'],
        'Public HQ preview; 28-second excerpt from 8s; mono for positional emitter; 220 Hz high-pass; loop overlap; level matched.')
    for i in range(4):
        name = 'seagull-%d.wav' % (i + 1)
        gull = np.mean(read_audio(name), axis=1, keepdims=True)
        add('bird-%d' % i, highpass(gull, 550), 'gulls', .075, False, [name],
            'Mono for positional emitter; 550 Hz high-pass; short end fades; level matched.')
    if set(entries) != RECORDED_IDS:
        raise ValueError('Incomplete ambient replacement')
    return entries


def metadata(extra_sources=()):
    manifest = source_manifest()
    sources = {source['id']: source for source in extra_sources}
    sources.update({source['id']: source for source in manifest['sources']})
    return dict(version=4, sampleRate=RATE,
                authorship='Licensed human recordings, performances and authored studio effects. No locally synthesized placeholder audio.',
                sources=list(sources.values()), sourceLicense=manifest['license'], sourceLicenseUrl=manifest['licenseUrl'])


def replace_bank():
    entries = prepare_recordings()
    bank_path = OUT / 'bank.json'
    bank = json.loads(bank_path.read_text(encoding='utf-8'))
    bank.update(metadata(bank.get('sources', [])))
    bank['clips'] = [entries.get(entry['id'], entry) for entry in bank['clips']]
    bank_path.write_text(json.dumps(bank, indent=2) + '\n', encoding='utf-8')
    REPORT.mkdir(parents=True, exist_ok=True)
    (REPORT / 'bank-analysis.json').write_text(json.dumps(bank, indent=2) + '\n', encoding='utf-8')
    print('Replaced %d ambient clips; effects and music preserved.' % len(entries))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true', help='Download and lock the CC0 sources before importing.')
    parser.add_argument('--sources-only', action='store_true', help='Only download/verify originals; do not replace game audio.')
    args = parser.parse_args()
    if args.download:
        download_sources()
    if not args.sources_only:
        replace_bank()
