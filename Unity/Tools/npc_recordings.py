"""Import licensed human NPC performances, without speech generation.

Use --download once; bank rebuilds then use the locked local files offline.
Only excerpts, filtering, level matching and edge fades are applied.
"""
import argparse
import json
import wave
import numpy as np
import ambient_recordings as ambient

SOURCES = ambient.SOURCES / 'npc'


def source_manifest():
    return json.loads((SOURCES / 'sources.json').read_text(encoding='utf-8'))


def recipes():
    result = []
    def add(key, source, file, start=0, seconds=None, rms=.115):
        result.append(dict(id=key, source=source, file=file, start=start, seconds=seconds,
                           targetRms=rms, highpass=90, category='effects', loop=False))
    for i, start in enumerate((.1, 2.5, 5, 7.4)):
        add('npc-talk-warm-' + str(i), 'npc-murmur-warm', 'murmur-warm.mp3', start, 2.25)
    for i, start in enumerate((.3, 6.5, 16, 29)):
        add('npc-talk-soft-' + str(i), 'npc-murmur-soft', 'murmur-soft.mp3', start, 2.3)
    for family in ('laugh', 'gasp', 'sigh', 'cheer'):
        for tone in ('warm', 'bright'):
            add('npc-' + family + '-' + tone + '-0', 'npc-' + family + '-' + tone,
                family + '-' + tone + '.mp3', rms=.1 if family == 'sigh' else .13)
    add('npc-ack-0', 'npc-ack', 'ack.mp3', rms=.12)
    add('npc-ack-1', 'npc-question', 'question.mp3', rms=.12)
    return result


def prepare_recordings():
    lock = json.loads((SOURCES / 'downloads.json').read_text(encoding='utf-8'))
    by_id = {s['id']: s for s in source_manifest()['sources']}
    for source in by_id.values():
        for file in source['files']:
            name = file['name']
            if name not in lock or ambient.digest(SOURCES / name) != lock[name]['sha256']:
                raise ValueError('Missing or changed NPC source: ' + name)
    entries = {}
    for recipe in recipes():
        with ambient.sf.SoundFile(SOURCES / recipe['file']) as source:
            source.seek(round(recipe['start'] * source.samplerate))
            count = -1 if recipe['seconds'] is None else round(recipe['seconds'] * source.samplerate)
            samples = source.read(count, dtype='float64', always_2d=True)
            x = ambient.resample(np.mean(samples, axis=1, keepdims=True), source.samplerate)
        x = ambient.highpass(x, recipe['highpass'])
        loud = np.flatnonzero(np.abs(x[:, 0]) > np.max(np.abs(x)) * .012)
        if not len(loud):
            raise ValueError('Silent NPC source: ' + recipe['id'])
        x = x[max(0, loud[0]-round(.012*ambient.RATE)):min(len(x), loud[-1]+round(.04*ambient.RATE))]
        x -= np.mean(x)
        x *= recipe['targetRms'] / max(float(np.sqrt(np.mean(x*x))), 1e-9)
        for seconds, front in ((.004, True), (.035, False)):
            n = min(round(seconds * ambient.RATE), len(x)//5)
            if front:
                x[:n] *= np.linspace(0, 1, n)[:, None]
            else:
                x[-n:] *= np.linspace(1, 0, n)[:, None]
        peak = float(np.max(np.abs(x)))
        if peak > .78:
            x *= .78/peak
        if len(x) < .1*ambient.RATE or not np.isfinite(x).all():
            raise ValueError('Invalid NPC excerpt: ' + recipe['id'])
        pcm = np.round(x*32767).astype('<i2')
        path = ambient.OUT / (recipe['id'] + '.wav')
        temporary = path.with_suffix('.wav.tmp')
        with wave.open(str(temporary), 'wb') as output:
            output.setnchannels(1); output.setsampwidth(2); output.setframerate(ambient.RATE)
            output.writeframes(pcm.tobytes())
        temporary.replace(path)
        values = pcm.astype(np.float64)/32768
        source = by_id[recipe['source']]
        entries[recipe['id']] = dict(id=recipe['id'], category='effects', loop=False,
            seconds=len(pcm)/ambient.RATE, channels=1, peak=round(float(np.max(np.abs(values))),6),
            rms=round(float(np.sqrt(np.mean(values*values))),6), seam=round(float(abs(values[-1,0]-values[0,0])),6),
            source=source['id'], author=source['author'], sourceUrl=source['page'], license='CC0-1.0',
            authorship='Human recorded vocal performance', recordingEvidence=source['recordingEvidence'],
            sourceFiles=[recipe['file']], sourceDownloadSha256=lock[recipe['file']]['sha256'],
            recipe=recipe, sha256=ambient.digest(path))
    return entries


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true')
    args = parser.parse_args()
    if args.download:
        ambient.download_sources(SOURCES / 'sources.json', SOURCES)
    else:
        print(json.dumps(list(prepare_recordings().values()), indent=2))
