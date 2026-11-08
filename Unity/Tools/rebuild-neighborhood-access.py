"""Rebuild only yard access meshes after changing their grade calculation.

Uses the same planner/height functions as the full map generator, and refuses
to proceed if the authored parcel layout no longer matches. Buildings, roads,
furniture, interaction IDs and physical walls stay intact.
"""
import hashlib,json,math,os,sys
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import numpy as np
import shapely as sh
from shapely import Polygon,box
from building_frontages import rectangle,make_aprons
from neighborhood_spaces import plan_spaces,access_grade,access_skirt
from street_network import StreetNetwork

ROOT=Path(__file__).resolve().parents[1];A=ROOT/'OpenTerritory/Assets'
world=json.loads((A/'Resources/world.json').read_text(encoding='utf-8'))
reference=json.loads((A/'Resources/reference-map.json').read_text(encoding='utf-8'))
meta=json.loads((A/'Resources/neighborhood-spaces.json').read_text(encoding='utf-8'))
g=world['ground'];grid=np.array(g['heights']).reshape(g['height'],g['width'])
def ground(p):
 fx=min(max((p[0]-g['x'])/g['step'],0),g['width']-1.001);fz=min(max((p[2]-g['z'])/g['step'],0),g['height']-1.001);i,j=int(fx),int(fz);a,b=fx-i,fz-j
 return float(grid[j,i]+a*(grid[j,i+1]-grid[j,i])+b*(grid[j+1,i]-grid[j,i])) if a+b<=1 else float(grid[j+1,i+1]+(1-a)*(grid[j+1,i]-grid[j+1,i+1])+(1-b)*(grid[j,i+1]-grid[j+1,i+1]))
def point(uv):return np.array([(uv[0]-reference['originX'])*reference['metersPerPixel'],(reference['originY']-uv[1])*reference['metersPerPixel']])
paths=[dict(id=r['id'],kind=r['kind'],width=r['width'],xz=np.array([[p[k] for k in 'xyz'] for p in r['points']]),heights=np.array([p['y']-.07 for p in r['points']])) for r in reference['roads']]
lots=[dict(center=np.array([p['x'],p['z']]),size=[49,30] if p['kind']=='armory' else [p['lotWidth'],p['lotDepth']],yaw=p['yaw']) for p in world['places']]
fixed=[rectangle(point(uv),size,0,1) for uv,size in [([869,356],[17,27]),([664,386],[35,27]),([1260,287.5],[62,58]),([1188,250],[40,31])]]+[box(970,829,990,894)]
spaces=plan_spaces(lots,paths,fixed,Polygon([(p['x'],p['y']) for p in reference['coast']]),ground);lookup={s['id']:s for s in spaces}
assert set(lookup)=={s['id'] for s in meta['spaces']},'Parcel layout changed: run the full map generator instead'
for s in meta['spaces']:
 other=lookup[s['id']];assert abs(other['shape'].area-s['area'])<.05 and np.linalg.norm(other['center']-np.array([s['center']['x'],s['center']['z']]))<.02
network=StreetNetwork(paths,ground,(g['x'],g['z']));aprons=make_aprons(world['places'],network,ground);frontages=sh.union_all([a['shape'] for a in aprons]);tree=sh.STRtree([a['shape'] for a in aprons])
print('Matched parcels:',len(spaces),flush=True)
visual=json.loads((A/'Imported/visual.json').read_text(encoding='utf-8'));nodes={n['name']:n for n in visual['nodes'] if n['geometry']>=0};removed=set();added=[]
def key(flat):return len(flat),tuple(flat[:12]),tuple(flat[-12:])
floors={}
for f in visual['floors']:floors.setdefault(key(f['vertices']),[]).append(f)
next_id=max(n['id'] for n in visual['nodes'])+1
gravel=next(i for i,m in enumerate(visual['materials']) if m['name']=='Cascalho dos quintais');visual['materials'][gravel]['doubleSide']=True
def replace(name,mesh):
 global next_id
 vertices,indices,normals=mesh;vertices=np.round(vertices,5);indices=np.asarray(indices,int);node=nodes.get(name)
 if node:
  old=visual['geometries'][node['geometry']];flat=np.asarray(old['positions']).reshape(-1,3)[np.asarray(old['indices'],int)].ravel().tolist()
  matches=[f for f in floors.get(key(flat),[]) if f['vertices']==flat];assert matches,'Physical floor not found: '+name
  removed.update(id(f) for f in matches)
 if len(vertices)==0:
  if node:node['geometry']=-1
  return
 geometry=dict(name=name,positions=vertices.ravel().tolist(),normals=np.round(normals,5).ravel().tolist(),uv=[],colors=[],indices=indices.tolist(),groups=[])
 if node:visual['geometries'][node['geometry']]=geometry
 else:
  index=len(visual['geometries']);visual['geometries'].append(geometry)
  visual['nodes'].append(dict(id=next_id,parent=-1,name=name,kind='mesh',geometry=index,dynamic=False,visible=True,castShadow=False,position=dict(x=0,y=0,z=0),scale=dict(x=1,y=1,z=1),rotation=dict(x=0,y=0,z=0,w=1),materials=[gravel],instances=[]));next_id+=1
 added.append(dict(vertices=vertices[indices].ravel().tolist()))
for entry in meta['spaces']:
 s=lookup[entry['id']];nearby=[aprons[i] for i in tree.query(s['path'].buffer(3.3))]
 def height(xy,nearby=nearby,kind=s['kind'],primary=aprons[s['lot']] if s['lot']>=0 else None):return network.height(xy,.085) if kind=='parking' else access_grade(xy,nearby,network,primary)
 if s['kind']!='parking':
  path=s['path'].difference(network.asphalt).difference(network.sidewalk).difference(frontages)
  replace('Caminho lateral aberto '+s['id'],network.mesh(path,height,.5))
  replace('Base do acesso lateral '+s['id'],access_skirt(path,network,height))
 xy=np.array([[p['x'],p['z']] for p in entry['samples']]);ys=height(xy)
 for p,y in zip(entry['samples'],ys):p['y']=round(float(y),5)
visual['floors']=[f for f in visual['floors'] if id(f) not in removed]+added
layout=json.loads((ROOT/'Map/reference-layout.json').read_text(encoding='utf-8'));old=json.loads((ROOT/'AssetLibrary/OriginalCity/world.json').read_text(encoding='utf-8-sig'))
digest=hashlib.sha256((json.dumps(layout,sort_keys=True)+old['sourceHash']+(ROOT/'Tools/build-reference-map.py').read_text(encoding='utf-8')+''.join((ROOT/'Tools'/s).read_text(encoding='utf-8') for s in ['street_network.py','building_frontages.py','neighborhood_spaces.py'])).encode()).hexdigest()
world['sourceHash']=hashlib.sha256((digest+(ROOT/'Tools/map_prop_repair.py').read_text(encoding='utf-8')).encode()).hexdigest()
outputs=[(A/'Imported/visual.json',visual),(A/'Resources/world.json',world),(A/'Resources/neighborhood-spaces.json',meta)]
for path,data in outputs:path.with_suffix('.json.tmp').write_text(json.dumps(data,ensure_ascii=False,separators=(',',':'),allow_nan=False),encoding='utf-8')
for path,_ in outputs:os.replace(path.with_suffix('.json.tmp'),path)
print('Rebuilt access surfaces:',len(added),'removed old physical surfaces:',len(removed),flush=True)
