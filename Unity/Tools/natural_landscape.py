"""Deterministic forest groves and rocks; also applies to the current exported map."""
import copy
import hashlib
import json
import math
import os
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(os.environ.get('TEMP', '/tmp')) / 'open-territory-map-trace-deps'))
from shapely.geometry import Point, Polygon, LineString, box
from shapely.ops import unary_union
from shapely.strtree import STRtree


def height(g, x, z):
    fx = min(max((x - g['x']) / g['step'], 0), g['width'] - 1.001)
    fz = min(max((z - g['z']) / g['step'], 0), g['height'] - 1.001)
    i, j = int(fx), int(fz)
    a, b = fx - i, fz - j
    at = j * g['width'] + i
    h, r, t, c = (g['heights'][k] for k in (at, at + 1, at + g['width'], at + g['width'] + 1))
    return h + a * (r-h) + b * (t-h) if a+b <= 1 else c + (1-a) * (t-c) + (1-b) * (r-c)


class Habitat:
    def __init__(self, world, layout, solids):
        self.g = world['ground']
        self.land = Polygon([(p['x'], p['y']) for p in layout['coast']]).buffer(0)
        self.roads = unary_union([LineString([(p['x'], p['z']) for p in r['points']]).buffer(
            r['width']/2 + (2.8 if r['kind'] != 'trail' else 1.8)) for r in layout['roads']])
        reserved = [box(p['x']-p['w']/2-4, p['z']-p['d']/2-4, p['x']+p['w']/2+4, p['z']+p['d']/2+4) for p in world['places']]
        for s in solids:
            if not s.get('active', True) or s['top'] < height(self.g, s['x'], s['z'])-.2:
                continue
            # A conservative rotated footprint, expanded for pedestrians.
            w, d, a = s['w']/2+1.2, s['d']/2+1.2, s.get('yaw', 0)
            c, sn = math.cos(a), math.sin(a)
            reserved.append(Polygon([(s['x']+c*x+sn*z, s['z']-sn*x+c*z) for x,z in [(-w,-d),(-w,d),(w,d),(w,-d)]]))
        for p in world.get('services', []):
            q = p['position']; reserved.append(Point(q['x'],q['z']).buffer(4))
        for p in world.get('locations', []):
            reserved.append(Point(p['x'],p['z']).buffer(3))
        q = world['spawn']; reserved.append(Point(q['x'],q['z']).buffer(12))
        self.reserved = STRtree(reserved)
        self.placed = {}
        self.minimum_spacing=0

    def clear(self, x, z, radius, tree=True, dense=False):
        p = Point(x,z)
        if not self.land.contains(p) or self.land.boundary.distance(p) < radius+1.5:
            return False
        if self.roads.distance(p) < radius+1.5:
            return False
        footprint = p.buffer(radius+.5, quad_segs=4)
        if len(self.reserved.query(footprint, predicate='intersects')):
            return False
        h = height(self.g,x,z)
        if h < (1.1 if tree else .55):
            return False
        for dx,dz in [(radius,0),(-radius,0),(0,radius),(0,-radius)]:
            edge = height(self.g,x+dx,z+dz)
            if edge < .35 or abs(edge-h) > radius*(.39 if tree else .5):
                return False
        cell = (int(x//12), int(z//12))
        for iz in range(cell[1]-2,cell[1]+3):
            for ix in range(cell[0]-2,cell[0]+3):
                for px,pz,pr,pt in self.placed.get((ix,iz),[]):
                    gap = max(1.8 if dense else 2.2, (radius+pr)*((.32 if dense else .53) if tree and pt else .95))
                    if tree and pt:gap=max(gap,self.minimum_spacing)
                    if (px-x)**2+(pz-z)**2 < gap*gap:
                        return False
        return True

    def reserve(self,x,z,radius,tree):
        self.placed.setdefault((int(x//12),int(z//12)),[]).append((x,z,radius,tree))


def plan(world,layout,solids):
    from natural_scatter import scattered_plan
    return scattered_plan(world,layout,solids)


def apply_nature(visual,world,layout):
    forest=[n for n in visual['nodes'] if n['name']=='Reused donor tree']
    if not forest: raise ValueError('Forest source nodes not found')
    old_indices={t['solidIndex'] for t in world.get('natureTrees',[])} | {r['solidIndex'] for r in world.get('rocks',[])}
    old_points={(round(n['instances'][0]['matrix'][12],2),round(n['instances'][0]['matrix'][14],2)) for n in forest}
    solids=[s for s in world['solids'] if s['index'] not in old_indices and not (
        abs(s['w']-.7)<.001 and abs(s['d']-.7)<.001 and (round(s['x'],2),round(s['z'],2)) in old_points)]
    trees,rocks,groves=plan(world,layout,solids)
    if len(trees)<1100 or len(rocks)<250: raise ValueError(f'Insufficient natural habitat: {len(trees)} trees, {len(rocks)} rocks')
    nodes=[n for n in visual['nodes'] if n['name']!='Reused donor tree']
    def key(p):return round(p['x'],4),round(p['z'],4)
    previous_trees={key(t['position']):t for t in world.get('natureTrees',[])}
    previous_rocks={key(r['position']):r for r in world.get('rocks',[])}
    next_id=max(n['id'] for n in visual['nodes'])+1;next_solid=max(s['index'] for s in world['solids'])+1
    template=forest[0]
    for i,t in enumerate(trees):
        previous=previous_trees.get(key(t['position']))
        node=copy.deepcopy(template);node['id']=previous['node'] if previous else next_id
        if not previous:next_id+=1
        # The Unity tree modeller uses the explicit scale/variant; this anchor remains useful
        # to the legacy converter and provides an unambiguous trunk-to-solid association.
        m=node['instances'][0]['matrix']; dx=t['position']['x']-m[12]; dz=t['position']['z']-m[14]
        dy=t['position']['y']+1.7-m[13]
        for instance in node['instances']:
            matrix=instance['matrix'];matrix[12]+=dx;matrix[13]+=dy;matrix[14]+=dz
        nodes.append(node);t['node']=node['id'];t['solidIndex']=previous['solidIndex'] if previous else next_solid
        if not previous:next_solid+=1
        s=t['scale'];p=t['position']
        solids.append(dict(index=t['solidIndex'],owner=0,x=p['x'],z=p['z'],w=.7*s['x'],d=.7*s['z'],bottom=p['y'],top=p['y']+4*s['y'],yaw=math.radians(t['yaw']),active=True,vault=False,opaque=True,rangeBackstop=False))
    for rock in rocks:
        p,s=rock['position'],rock['scale'];previous=previous_rocks.get(key(p));rock['solidIndex']=previous['solidIndex'] if previous else next_solid
        if not previous:next_solid+=1
        solids.append(dict(index=rock['solidIndex'],owner=0,x=p['x'],z=p['z'],w=s['x'],d=s['z'],bottom=p['y']-.24*s['y'],top=p['y']+.70*s['y'],yaw=math.radians(rock['yaw']),active=True,vault=False,opaque=True,rangeBackstop=False))
    visual['nodes']=nodes;world['solids']=solids;world['natureTrees']=trees;world['rocks']=rocks;world['natureGroves']=groves
    world['natureBaseHash']=world.get('natureBaseHash',world['sourceHash'])
    world['sourceHash']=hashlib.sha256((world['natureBaseHash']+Path(__file__).read_text(encoding='utf-8')+Path(__file__).with_name('natural_scatter.py').read_text(encoding='utf-8')).encode()).hexdigest()
    report=dict(revision=3,trees=len(trees),groves=groves,isolated=sum(t['grove']<0 for t in trees),rocks=len(rocks),
                minScale=min(t['scale']['y'] for t in trees),maxScale=max(t['scale']['y'] for t in trees),
                young=sum(t['scale']['y']<.78 for t in trees),mature=sum(t['scale']['y']>1.27 for t in trees))
    return report


if __name__=='__main__':
    root=Path(__file__).resolve().parents[1];assets=root/'OpenTerritory/Assets'
    visual=json.loads((assets/'Imported/visual.json').read_text(encoding='utf-8'))
    world=json.loads((assets/'Resources/world.json').read_text(encoding='utf-8'))
    layout=json.loads((assets/'Resources/reference-map.json').read_text(encoding='utf-8'))
    report=apply_nature(visual,world,layout)
    (assets/'Imported/visual.json').write_text(json.dumps(visual,separators=(',',':')),encoding='utf-8')
    (assets/'Resources/world.json').write_text(json.dumps(world,separators=(',',':')),encoding='utf-8')
    destination=root/'Reports/nature';destination.mkdir(parents=True,exist_ok=True)
    (destination/'layout.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='groves'}),flush=True)
