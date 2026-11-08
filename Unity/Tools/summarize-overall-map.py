"""Identify the exact delivered player and its fresh, sequential acceptance."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

unity = Path(__file__).resolve().parents[1]
report = unity/'Reports/overall-map'
delivery = unity/'Deliveries/OverallMap-20261108/BaiaDoSol.exe'
assembly = delivery.parent/'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()
build, process, snapshot, editor = [read(report/name) for name in ['build-complete.json', 'build-process-result.json', 'snapshot.json', 'editor-validation.json']]
world = read(unity/'OpenTerritory/Assets/Resources/world.json')
current_assembly = sha(assembly)
assert build['passed'] and process['processExitCode'] == 0
assert editor['passed'] and editor['sourceHash'] == world['sourceHash']
assert sha(Path(snapshot['snapshot'])/'Assets/Scenes/BaiaDoSol.unity') == snapshot['sceneSha256']
current_scene_hash = sha(unity/'OpenTerritory/Assets/Scenes/BaiaDoSol.unity')
required = ['overall', 'map', 'neighborhood', 'frontage', 'reference', 'prop-grounding', 'clothing', 'bike-shop', 'blackthorn', 'fire-station', 'default-session', 'outdoor-d3d11', 'overall-d3d11']
suites = []
for name in required+['outdoor-default', 'blackthorn-default']:
    path = report/'acceptance'/name/'verification.json'
    value = read(path)
    assert path.stat().st_mtime > (report/'build-complete.json').stat().st_mtime, 'Old report: '+name
    assert value['assemblySha256'].upper() == current_assembly, 'Different player: '+name
    assert Path(value['executable']).resolve() == delivery.resolve()
    checks = value.get('checks')
    count = len(checks) if isinstance(checks, list) else checks if isinstance(checks, int) else sum(s['checks'] for s in value.get('suites', []))
    suites.append(dict(name=name, required=name in required, passed=value['passed'], checks=count, report=str(path),
        processExitCode=value.get('processExitCode', value.get('exitCode')), graphics=value.get('graphics', value.get('graphicsApi'))))
overall = read(report/'acceptance/overall/overall-map-qa.json')
assert overall['sourceHash'] == world['sourceHash']
geometry = read(report/'geometry-validation.json')
preserved = read(report/'apply-complete.json')
result = dict(passed=all(s['passed'] for s in suites if s['required']), allSuitesPassed=all(s['passed'] for s in suites),
    verifiedAtUtc=datetime.now(timezone.utc).isoformat(), executable=str(delivery), assemblySha256=current_assembly,
    sourceHash=world['sourceHash'], sceneSha256=snapshot['sceneSha256'], fullBuild=build, buildProcessExitCode=process['processExitCode'],
    snapshotCreatedAtUtc=snapshot['createdAtUtc'], currentSceneSha256=current_scene_hash,
    currentSceneChangedDuringValidation=current_scene_hash != snapshot['sceneSha256'],
    requiredChecks=sum(s['checks'] for s in suites if s['required']), suites=suites,
    coverage=dict(buildings=len(world['places']), windows=editor['windows'], buildingGroundSamples=overall['buildingSamples'],
        roadPaintSamples=overall['paintSamples'], parkingSamples=overall['parkingSamples'], openSlopeYards=geometry['openSlopeYards'],
        removedWallSections=geometry['removedExcessiveWallSections'], retainedWalls=geometry['retainedLowWallSections'],
        maximumWallHeight=geometry['maximumWallHeight'], protectedColliders=preserved['protectedColliders']),
    visualCaptures=str(report/'acceptance/overall'), snapshot=str(report/'snapshot.json'),
    historicalDx12Shutdown=str(report/'outdoor-initial/d3d12-exit-event.json'))
if any(not s['passed'] for s in suites if not s['required']):
    result['limitations'] = ['A default-graphics diagnostic failed; each process exit and its DX11 comparison are recorded separately.']
if result['currentSceneChangedDuringValidation']:
    result.setdefault('limitations', []).append('The shared scene received concurrent police station changes after this isolated snapshot; those later changes were preserved and are not included in this player.')
(report/'verification.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({k: result[k] for k in ['passed', 'allSuitesPassed', 'requiredChecks', 'assemblySha256', 'coverage']}, indent=2))
assert result['passed'], 'Required current map acceptance failed'
