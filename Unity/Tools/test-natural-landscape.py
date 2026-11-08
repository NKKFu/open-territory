"""Read-only geometric validation against the current exported map."""
import json
from pathlib import Path
from natural_landscape import Habitat, height, plan
import numpy as np

root=Path(__file__).resolve().parents[1]
world=json.loads((root/'OpenTerritory/Assets/Resources/world.json').read_text(encoding='utf-8'))
layout=json.loads((root/'OpenTerritory/Assets/Resources/reference-map.json').read_text(encoding='utf-8'))
trees,rocks=world['natureTrees'],world['rocks']
indices={p['solidIndex'] for p in trees+rocks}
base=[s for s in world['solids'] if s['index'] not in indices]
habitat=Habitat(world,layout,base)
assert len(indices)==len(trees)+len(rocks),'duplicate physical indices'
for item in trees:
    p,s=item['position'],item['scale']
    assert habitat.clear(p['x'],p['z'],3.7*max(s['x'],s['z'])),('tree clearance',p)
for item in rocks:
    p,s=item['position'],item['scale']
    assert habitat.clear(p['x'],p['z'],max(s['x'],s['z'])*.6,False),('rock clearance',p)
assert max(p['scale']['y'] for p in trees)/min(p['scale']['y'] for p in trees)>4
assert not world.get('natureGroves'), 'groves still present'
assert all(t['grove'] < 0 for t in trees)
assert len(trees) >= 6000
assert len({r['variant'] for r in rocks})==12
assert max(r['scale']['x'] for r in rocks)>6 and min(r['scale']['x'] for r in rocks)<.8
# Physical terrain uses triangles rather than bilinear interpolation.
fixture=dict(x=0,z=0,step=1,width=2,height=2,heights=[0,1,2,8])
assert abs(height(fixture,.25,.25)-.75)<1e-8
assert abs(height(fixture,.75,.75)-4.75)<1e-8
again,again_rocks,groves=plan(world,layout,base)
def authored(p):return {k:v for k,v in p.items() if k not in ('node','solidIndex')}
assert [authored(t) for t in trees]==again,'forest generation is not deterministic'
assert [authored(r) for r in rocks]==again_rocks,'rock generation is not deterministic'
points=np.array([[t['position']['x'],t['position']['z']] for t in trees])
nearest=[]
for start in range(0,len(points),128):
    stop=min(start+128,len(points));block=points[start:stop]
    distances=np.sqrt(((block[:,None,:]-points[None,:,:])**2).sum(axis=2))
    distances[np.arange(stop-start),np.arange(start,stop)]=np.inf
    nearest.extend(distances.min(axis=1))
assert min(nearest)>9.9998,'trees too close'
assert np.median(nearest)>10.5,'vegetation still forms dense groups'
cells={}
for x,z in points:
    key=(int(x//120),int(z//120));cells[key]=cells.get(key,0)+1
assert len(cells)>80 and max(cells.values())<100,'vegetation is concentrated in a few areas'
before=root/'Reports/nature/before-scatter-world.json'
if before.exists():
    previous=json.loads(before.read_text(encoding='utf-8'))
    assert [authored(r) for r in rocks]==[authored(r) for r in previous['rocks']],'outcrops changed'
report=dict(passed=True,trees=len(trees),rocks=len(rocks),groves=0,
            checkedFootprints=len(trees)+len(rocks),deterministic=True,roadsTrailsEntrancesAndCoastClear=True,
            medianSpacing=float(np.median(nearest)),minimumSpacing=float(min(nearest)),occupied120mCells=len(cells),maxTreesPerCell=max(cells.values()))

destination=root/'Reports/nature/geometry-qa.json'
destination.write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report),flush=True)
