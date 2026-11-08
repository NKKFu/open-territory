"""Replace the active world with the traced map, reusing the original city assets.

Run trace-reference.py first. OriginalCity is an asset donor, never a playable
scene. This script regenerates the active manifest, geometry and QA landmarks.
"""
import copy, hashlib, heapq, json, math, os, random, sys, time
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import cv2
import numpy as np
from street_network import StreetNetwork, width as paved_width
from coastal_roads import support_coastal_roads
from map_prop_repair import repair_props
from building_frontages import plan_lots, make_aprons, apron_height, rectangle
from resident_population import remove_children
from neighborhood_spaces import plan_spaces, AssetSites, PLAZA_ASSETS, in_plaza, plaza_asset, access_grade, access_skirt
from shapely import Polygon, box, LineString, Point
import shapely as sh

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'OpenTerritory/Assets'
layout=json.loads((ROOT/'Map/reference-layout.json').read_text(encoding='utf-8'))
source=json.loads((ROOT/'AssetLibrary/OriginalCity/visual.json').read_text(encoding='utf-8-sig'))
old=json.loads((ROOT/'AssetLibrary/OriginalCity/world.json').read_text(encoding='utf-8-sig'))
# A camera is an articulated fixture: its fixed mount must travel with its
# head. Static pieces outside a building/plaza are otherwise omitted below,
# leaving the hospital camera suspended without its mast or collision.
camera_nodes={n['id']:n for n in source['nodes']}
camera_mounts={camera_nodes[d['node']]['parent'] for d in old['devices'] if d['type']=='camera'}
camera_parts=set(camera_mounts)
for n in source['nodes']:
 if n['parent'] in camera_parts:camera_parts.add(n['id'])
 if n['id'] in camera_parts:n['dynamic']=True
for ident in camera_mounts:
 p=camera_nodes[ident]['position']
 for solid in old['solids']:
  if abs(solid['x']-p['x'])<.01 and abs(solid['z']-p['z'])<.01 and max(solid['w'],solid['d'])<1 and solid['top']-solid['bottom']>4:
   solid['owner']=ident
world=copy.deepcopy(old)
S=layout['metersPerPixel'];OX,OY=layout['origin'];rng=random.Random(514207)
def pixel(p):return np.array([p[0]/S+OX,OY-p[2]/S])
def point(uv,y=0):return np.array([(uv[0]-OX)*S,y,(OY-uv[1])*S],dtype=float)
def vec(p):return {k:round(float(v),5) for k,v in zip('xyz',p)}
def arr(p):return np.array([p.get(k,0) for k in 'xyz'],dtype=float)
def yaw_matrix(yaw):
 a=math.radians(yaw);c,s=math.cos(a),math.sin(a)
 return np.array([[c,0,s,0],[0,1,0,0],[-s,0,c,0],[0,0,0,1]],float)
def translate(p):m=np.eye(4);m[:3,3]=p;return m
def transform(m,p):return (m@np.r_[p,1])[:3]
def node_matrix(n):
 q=n.get('rotation',{});x,y,z,w=[q.get(k,1 if k=='w' else 0) for k in 'xyzw']
 m=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w,0],[2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w,0],[2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y,0],[0,0,0,1]],float)
 m[:3,:3]*=arr(n.get('scale',dict(x=1,y=1,z=1)))
 m[:3,3]=arr(n.get('position',{}));return m
def pose(n,m):
 scale=np.linalg.norm(m[:3,:3],axis=0);r=m[:3,:3]/np.maximum(scale,1e-6)
 trace=float(np.trace(r))
 if trace>0:
  s=math.sqrt(trace+1)*2;q=[(r[2,1]-r[1,2])/s,(r[0,2]-r[2,0])/s,(r[1,0]-r[0,1])/s,s/4]
 else:
  i=int(np.argmax(np.diag(r)));j=(i+1)%3;k=(i+2)%3;s=math.sqrt(max(.000001,1+r[i,i]-r[j,j]-r[k,k]))*2;q=[0.,0.,0.,0.];q[i]=s/4;q[j]=(r[j,i]+r[i,j])/s;q[k]=(r[k,i]+r[i,k])/s;q[3]=(r[k,j]-r[j,k])/s
 n['position']=vec(m[:3,3]);n['scale']=vec(scale);n['rotation']=dict(zip('xyzw',q));n['parent']=-1
def sample_curve(controls):
 p=np.array(controls,float);closed=np.linalg.norm(p[0]-p[-1])<.01
 if closed:p=p[:-1]
 out=[];segments=len(p) if closed else len(p)-1
 for i in range(segments):
  a,b,c,d=[p[j%len(p)] if closed else p[min(max(j,0),len(p)-1)] for j in [i-1,i,i+1,i+2]]
  count=max(2,int(np.linalg.norm(c-b)*S/2.0))
  for t in np.linspace(0,1,count,endpoint=False):out.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
 out.append(p[0] if closed else p[-1]);return np.array(out)

# The reference outline supplies the land/water mask; bridges are decks, not land.
W,H=1467,735
land=np.zeros((H,W),np.uint8);cv2.fillPoly(land,[np.array(layout['coast'],np.int32)],255)
land[680:]=0;land[245:,1390:]=0;land[297:,1220:1285]=0
inside=cv2.distanceTransform(land,cv2.DIST_L2,5)*S
outside=cv2.distanceTransform(255-land,cv2.DIST_L2,5)*S
sdf=inside-outside
road_mask=np.zeros_like(land)
paths=[]
for r in layout['roads']:
 r=copy.deepcopy(r);r['uv']=sample_curve(r['points']);r['width']*=1.5 if r['kind']=='highway' else 1
 r['xz']=np.array([point(p) for p in r['uv']])
 cv2.polylines(road_mask,[np.round(r['uv']).astype(np.int32)],False,255,max(2,round((r['width']+3)/S)))
 paths.append(r)
def image_sample(image,uv):
 u,v=uv;u=min(max(u,0),W-1.001);v=min(max(v,0),H-1.001);i,j=int(u),int(v);a,b=u-i,v-j
 return float(image[j,i]*(1-a)*(1-b)+image[j,i+1]*a*(1-b)+image[j+1,i]*(1-a)*b+image[j+1,i+1]*a*b)
def distance_land(p):return image_sample(sdf,pixel(p))
def raw_height(p):
 uv=pixel(p);d=image_sample(sdf,uv)
 if d<0:return max(-20,d*.25)
 u,v=uv;upland=max(0,315-v)*.23
 hill=9+upland+14*math.exp(-((u-800)/280)**2-((v-160)/120)**2)
 return min(d*.18,hill)
def road_distance(p):return image_sample(cv2_road_distance,pixel(p))
cv2_road_distance=cv2.distanceTransform(255-road_mask,cv2.DIST_L2,5)*S

# Find parcels against the traced streets, not against a generated grid.
occupied=np.zeros_like(land);parcels=[]
def reserve_rect(uv,size,yaw,padding=2):
 p=point(uv);rot=yaw_matrix(yaw)[:3,:3];w,d=size
 corners=[pixel(p+rot@np.array([a*(w/2+padding),0,b*(d/2+padding)])) for a,b in [(-1,-1),(-1,1),(1,1),(1,-1)]]
 mask=np.zeros_like(land);cv2.fillConvexPoly(mask,np.round(corners).astype(np.int32),255);return mask
fixed=[('cinema',[-57,-36],[869,356],[17,27]),('fish',[160,-123],[664,386],[35,27]),('harbor',[164,-151],[1260,287.5],[62,58]),('carpark',[-163,-103],[1188,250],[40,31])]
for name,oldcenter,target,size in fixed:
 mask=reserve_rect(target,size,0);occupied[mask>0]=255
 parcels.append(dict(name=name,uv=target,size=size,yaw=0,fixed=True))
fixed_shapes=[rectangle(point(target)[[0,2]],size,0,1) for name,center,target,size in fixed]
fixed_shapes.append(box(970,829,990,894))
planned=plan_lots(paths,old['places'],fixed_shapes,Polygon([point(p)[[0,2]] for p in layout['coast']]),pixel,raw_height,homes=200)
for lot in planned:
 uv=pixel(np.array([lot['center'][0],0,lot['center'][1]]));yaw=math.degrees(lot['yaw'])
 occupied[reserve_rect(uv,lot['size'],yaw,.8)>0]=255
 parcels.append(dict(name='parcel-'+str(lot['index']),uv=uv.tolist(),size=lot['size'],yaw=yaw,fixed=False,donor=lot['donor'],anchor=lot['anchor'],road=lot['road']))
print('Compact street-facing buildings',len(planned),flush=True)

# Original complete buildings, with interiors, doors, furniture and signs.
zones=[]
def zone(name,center,size,target,yaw=0,base=0,height=None):
 p=point(target);p[1]=raw_height(p) if height is None else height
 m=translate(p)@yaw_matrix(yaw)@translate(np.array([-center[0],-base,-center[1]]))
 z=dict(name=name,center=np.array([center[0],base,center[1]]),size=size,target=p,yaw=yaw,matrix=m,uv=target)
 zones.append(z);return z
for name,center,target,size in fixed:
 zone(name,center,size,target,height=.16 if name=='harbor' else None)
zone('pier',[0,-163],[10,35],[1158.333333,295.643],height=0)
slots=[p for p in parcels if not p['fixed']]
place_zones=[]
for i,place in enumerate(old['places']):
 parcel=slots[i];size=[place['w']+4,place['d']+4]
 if place['kind']=='armory':size=[49,30]
 z=zone('building-'+str(i),[place['x']+(8 if place['kind']=='armory' else 0),place['z']],size,parcel['uv'],parcel['yaw'],place['elevation'])
 # Armory's frame remains at the source building center for data transforms.
 if place['kind']=='armory':z['center'][0]-=8;z['matrix']=translate(z['target'])@yaw_matrix(z['yaw'])@translate(-z['center'])
 place_zones.append(z)
def contains(z,p,pad=0):return abs(p[0]-z['center'][0])<=z['size'][0]/2+pad and abs(p[2]-z['center'][2])<=z['size'][1]/2+pad
def find_zone(p):
 for z in zones:
  if contains(z,p):return z
 return None

# Height field: coastline first, graded roads second, level building lots last.
STEP=8;GX=math.floor((-OX*S-100)/STEP)*STEP;GZ=math.floor(((OY-H)*S-120)/STEP)*STEP;GW=math.ceil((W*S+200)/STEP)+1;GH=math.ceil((H*S+240)/STEP)+1
grid=np.empty((GH,GW),np.float32)
for j in range(GH):
 for i in range(GW):grid[j,i]=raw_height(np.array([GX+i*STEP,0,GZ+j*STEP]))
support_coastal_roads(grid,paths,(GX,GZ),STEP)
def grid_at(p):
 fx=min(max((p[0]-GX)/STEP,0),GW-1.001);fz=min(max((p[2]-GZ)/STEP,0),GH-1.001);i,j=int(fx),int(fz);a,b=fx-i,fz-j
 # Match the exact two triangles used to render/collide the terrain.
 if a+b<=1:return float(grid[j,i]+a*(grid[j,i+1]-grid[j,i])+b*(grid[j+1,i]-grid[j,i]))
 return float(grid[j+1,i+1]+(1-a)*(grid[j+1,i]-grid[j+1,i+1])+(1-b)*(grid[j,i+1]-grid[j+1,i+1]))
for path in paths:
 heights=np.array([max(4 if path['kind']=='highway' else 1.5 if path['kind']=='street' else .45,raw_height(p)) for p in path['xz']])
 for _ in range(10):
  heights[1:-1]=(heights[:-2]+2*heights[1:-1]+heights[2:])/4
 for _ in range(3):
  for ids in [range(1,len(heights)),range(len(heights)-2,-1,-1)]:
   for i in ids:
    neighbor=i-1 if isinstance(ids,range) and ids.step==1 else i+1
    rise=np.linalg.norm(path['xz'][i]-path['xz'][neighbor])*.17
    heights[i]=np.clip(heights[i],heights[neighbor]-rise,heights[neighbor]+rise)
 path['heights']=heights
 for p,h in zip(path['xz'],heights):
  if distance_land(p)<-1:continue
  radius=path['width']/2+5
  lo=np.floor([(p[0]-radius-GX)/STEP,(p[2]-radius-GZ)/STEP]).astype(int);hi=np.ceil([(p[0]+radius-GX)/STEP,(p[2]+radius-GZ)/STEP]).astype(int)
  for j in range(max(0,lo[1]),min(GH,hi[1]+1)):
   for i in range(max(0,lo[0]),min(GW,hi[0]+1)):
    q=np.array([GX+i*STEP,0,GZ+j*STEP]);d=np.linalg.norm((q-p)[[0,2]])
    if d<radius and distance_land(q)>0:
     t=np.clip((radius-d)/5,0,1);grid[j,i]=grid[j,i]*(1-t)+h*t

for z,parcel in zip(place_zones,slots):
 anchor=parcel['anchor'];z['target'][1]=grid_at(np.array([anchor[0],0,anchor[1]]))+.10
 z['matrix']=translate(z['target'])@yaw_matrix(z['yaw'])@translate(-z['center'])
filler=[]
for i,parcel in enumerate(slots[len(old['places']):]):
 donor=parcel['donor']
 src=old['places'][donor]
 z=dict(name='home-'+str(i),center=np.array([src['x'],src['elevation'],src['z']]),size=[src['w']+3,src['d']+3],target=point(parcel['uv']),yaw=parcel['yaw'],uv=parcel['uv'],donor=donor)
 anchor=parcel['anchor'];z['target'][1]=grid_at(np.array([anchor[0],0,anchor[1]]))+.10;z['matrix']=translate(z['target'])@yaw_matrix(z['yaw'])@translate(-z['center']);filler.append(z)
for z in zones+filler:
 if z['name'] in ['harbor','pier']:continue
 # Level only the actual lot; do not flatten an old square neighborhood.
 rot=yaw_matrix(-z['yaw']);radius=math.hypot(*z['size'])/2+5;p=z['target']
 for j in range(max(0,int((p[2]-radius-GZ)/STEP)),min(GH,int((p[2]+radius-GZ)/STEP)+2)):
  for i in range(max(0,int((p[0]-radius-GX)/STEP)),min(GW,int((p[0]+radius-GX)/STEP)+2)):
   q=rot[:3,:3]@(np.array([GX+i*STEP,0,GZ+j*STEP])-p)
   d=max(abs(q[0])-z['size'][0]/2,abs(q[2])-z['size'][1]/2)
   q_world=np.array([GX+i*STEP,0,GZ+j*STEP])
   if d<4 and distance_land(q_world)>0 and (not z['name'].startswith(('building-','home-')) or road_distance(q_world)>1.1):
    t=np.clip(1-d/4,0,1);grid[j,i]=grid[j,i]*(1-t)+p[1]*t

# A quay belongs beside the road, not on top of it. Grade only its working
# apron; pier decks must never flatten the seabed into an artificial island.
port=next(z['target'] for z in zones if z['name']=='harbor');port_bounds=(port[0]-31,port[2]-12,port[0]+31,port[2]+13)
for j in range(max(0,int((port_bounds[1]-5-GZ)/STEP)),min(GH,int((port_bounds[3]+5-GZ)/STEP)+2)):
 for i in range(max(0,int((port_bounds[0]-5-GX)/STEP)),min(GW,int((port_bounds[2]+5-GX)/STEP)+2)):
  q=np.array([GX+i*STEP,0,GZ+j*STEP]);d=max(port_bounds[0]-q[0],q[0]-port_bounds[2],port_bounds[1]-q[2],q[2]-port_bounds[3])
  if d<4 and road_distance(q)>2.2:
   t=np.clip(1-d/4,0,1);grid[j,i]=grid[j,i]*(1-t)+.12*t

spaces=plan_spaces(planned,paths,fixed_shapes,Polygon([point(p)[[0,2]] for p in layout['coast']]),grid_at)
asset_sites=AssetSites(spaces,[rectangle(l['center'],l['size'],l['yaw']) for l in planned])
plaza_sites={}
for name,center,size in sorted(PLAZA_ASSETS,key=lambda a:-a[2][0]*a[2][1]):
 site=asset_sites.allocate(name,size,name in ['fountain','music','guide','bus-stop'])
 if site is None:raise RuntimeError('No usable neighborhood space for plaza asset '+name)
 plaza_sites[name]=dict(**site,source=np.array([center[0],0,center[1]]))
for s in spaces:
 occupied[reserve_rect(pixel(np.array([s['center'][0],0,s['center'][1]])),[1,1],0,0)>0]=255
 # Reserve the complete irregular plot, not just its center, for other props.
 for poly in sh.get_parts(s['shape']):
  if poly.geom_type=='Polygon':cv2.fillPoly(occupied,[np.round([pixel(np.array([x,0,z])) for x,z in poly.exterior.coords]).astype(np.int32)],255)
print('Neighborhood spaces',dict((kind,sum(s['kind']==kind for s in spaces)) for kind in ['yard','garden','parking']),flush=True)


# Flatten the donor renderables into reusable asset instances.
nodes_by_id={n['id']:n for n in source['nodes']};world_matrices={};visibility={}
for n in source['nodes']:
 parent=n['parent'];world_matrices[n['id']]=(world_matrices[parent] if parent>=0 else np.eye(4))@node_matrix(n)
 visibility[n['id']]=n.get('visible',True) and (visibility.get(parent,True))
geo_bounds=[]
for g in source['geometries']:
 p=np.array(g['positions']).reshape(-1,3);geo_bounds.append((p.min(axis=0),p.max(axis=0)))
entries=[]
for n in source['nodes']:
 if n.get('geometry',-1)<0 or n['dynamic'] or not visibility[n['id']]:continue
 if any(source['materials'][i].get('sea',0)>0 for i in n['materials']):continue
 low,high=geo_bounds[n['geometry']]
 for inst in n.get('instances',[]) or [dict(matrix=np.eye(4).flatten(order='F').tolist(),color=dict(r=1,g=1,b=1,a=1),surface=0)]:
  m=world_matrices[n['id']]@np.array(inst['matrix']).reshape(4,4,order='F');center=transform(m,(low+high)/2)
  extent=np.abs(m[:3,:3])@(high-low)/2
  if max(extent[[0,2]])>42:continue
  entries.append(dict(node=n,matrix=m,center=center,extent=extent,instance=inst))
zone_entries={z['name']:[] for z in zones}
for e in entries:
 z=find_zone(e['center'])
 if z and max(e['extent'][[0,2]])<max(z['size'])/2+3:zone_entries[z['name']].append(e)
print('donor asset pieces', {k:len(v) for k,v in zone_entries.items()},flush=True)

walk_samples=[]
for path in paths:
 if path['kind'] in ['trail','highway']:continue
 for i in range(4,len(path['xz'])-4,7):
  p=path['xz'][i];f=path['xz'][i+1]-path['xz'][i-1];f/=np.linalg.norm(f);normal=np.array([-f[2],0,f[0]])
  for side in [-1,1]:
   q=p+normal*side*(path['width']/2+2)
   if distance_land(q)>3:walk_samples.append(q)
fallback={}
old_ground=old['ground'];old_heights=np.array(old_ground['heights']).reshape(old_ground['height'],old_ground['width'])
def old_floor(x,z):
 i=int(np.clip(round((x-old_ground['x'])/old_ground['step']),0,old_ground['width']-1));j=int(np.clip(round((z-old_ground['z'])/old_ground['step']),0,old_ground['height']-1));return float(old_heights[j,i])
plaza_frames={}
def plaza_frame(p):
 name=plaza_asset(p)
 if name not in plaza_frames:
  site=plaza_sites[name];center=site['center'];src=site['source'].copy();src[1]=old_floor(src[0],src[2]);target=np.array([center[0],grid_at(np.array([center[0],0,center[1]]))+.10,center[1]])
  if name=='fountain':src[1]=0
  site['target']=target;plaza_frames[name]=translate(target)@yaw_matrix(math.degrees(site['yaw']))@translate(-src)
 return plaza_frames[name]
def source_frame(p):
 if in_plaza(p):return plaza_frame(p)
 z=find_zone(p)
 if z:return z['matrix']
 key=(round(p[0]/8),round(p[2]/8))
 if key not in fallback:
  h=(key[0]*73856093 ^ key[1]*19349663)&0xffffffff;q=walk_samples[h%len(walk_samples)].copy();q[1]=grid_at(q)
  fallback[key]=translate(q)@translate(np.array([-key[0]*8,-old_floor(key[0]*8,key[1]*8),-key[1]*8]))
 return fallback[key]
def move(p):return transform(source_frame(p),p)
root_overrides={};parking_slots=[];vehicle_yaws={}
for car in old['vehicles']:
 if car['auto']:continue
 # Reserve separate roadside bays, leaving the full sidewalk available.
 serial=len(parking_slots);found=False
 shared_parks=[s for s in spaces if s['kind']=='parking']
 if serial<min(8,len(shared_parks)*2) and car['kind']=='car' and car['length']<5.8:
  park=shared_parks[serial//2];yaw=park['yaw'];front=np.array([math.sin(yaw),0,math.cos(yaw)]);right=np.array([math.cos(yaw),0,-math.sin(yaw)])
  q=np.array([park['center'][0],0,park['center'][1]])-front*1.85+right*(-3.05 if serial%2==0 else 3.05);q[1]=grid_at(q)+.10
  parking_slots.append(dict(id=car['id'],center=q.copy(),yaw=yaw,width=car['width']+1.3,length=car['length']+1.8,shared=True));found=True
 for attempt in range(1200):
  if found:break
  sample=walk_samples[(serial*79+attempt*17+71)%len(walk_samples)]
  path,k=min(((r,int(np.argmin(np.linalg.norm(r['xz'][:,[0,2]]-sample[[0,2]],axis=1)))) for r in paths if r['kind']=='street'),key=lambda item:np.linalg.norm(item[0]['xz'][item[1]]-sample))
  k=min(max(1,k),len(path['xz'])-2);p=path['xz'][k];f=path['xz'][k+1]-path['xz'][k-1];f/=np.linalg.norm(f);normal=np.array([-f[2],0,f[0]])
  side=1 if np.dot(sample-p,normal)>0 else -1
  q=p+normal*side*(paved_width(path)/2+StreetNetwork.sidewalk_width+car['width']/2+.85);yaw=math.atan2(f[0],f[2])+(math.pi if side>0 else 0)
  size=[car['width']+1.3,car['length']+1.8];uv=pixel(q);mask=reserve_rect(uv,size,math.degrees(yaw),.4)
  if np.any((mask>0)&((occupied>0)|(road_mask>0)|(land==0))):continue
  if distance_land(q)<8:continue
  occupied[mask>0]=255;q[1]=grid_at(q)+.10;found=True
  parking_slots.append(dict(id=car['id'],center=q.copy(),yaw=yaw,width=size[0],length=size[1]));break
 if not found:raise RuntimeError('No clear parking bay for '+str(car['id']))
 vehicle_yaws[car['id']]=yaw
 root_overrides[car['id']]=translate(q)@yaw_matrix(math.degrees(yaw-car.get('yaw',0)))@translate(-arr(car['position']))
pier_target=next(z['target'] for z in zones if z['name']=='pier')
boat_positions=[pixel(pier_target+np.array([-5.2,0,-1])).tolist(),pixel(pier_target+np.array([5.2,0,-1])).tolist(),[1262.29,296],[1266.04,295.5],[1262.29,300]]
for number,(boat,uv) in enumerate(zip(old['boats'],boat_positions)):
 uv=uv.copy()
 while grid_at(point(uv))>-2:uv[1]+=2
 q=point(uv,boat['position']['y']);yaw=0 if number%2==0 else math.pi;vehicle_yaws[boat['id']]=yaw
 root_overrides[boat['id']]=translate(q)@yaw_matrix(math.degrees(yaw-boat.get('yaw',0)))@translate(-arr(boat['position']))

new_nodes=[];new_geometry=copy.deepcopy(source['geometries']);materials=copy.deepcopy(source['materials']);next_id=max(nodes_by_id)+100
root_frames={}
for n in source['nodes']:
 if n['dynamic'] and (n['parent']<0 or not nodes_by_id[n['parent']]['dynamic']):
  p=world_matrices[n['id']][:3,3];root_frames[n['id']]=root_overrides.get(n['id'],source_frame(p))
def dynamic_root(ident):
 while nodes_by_id[ident]['parent']>=0 and nodes_by_id[nodes_by_id[ident]['parent']]['dynamic']:ident=nodes_by_id[ident]['parent']
 return ident
signal_targets=[[900,411],[1012,361],[1076,281],[1037,265],[978,330],[931,345]]
hidden_signal_roots=set()
for i,signal in enumerate(old['signals']):
 roots={dynamic_root(ident) for ident in signal['lamps']}
 if i>=len(signal_targets):hidden_signal_roots.update(roots);continue
 q=point(signal_targets[i]);q[1]=grid_at(q);frame=translate(q)@translate(np.array([-signal['x'],-old_floor(signal['x'],signal['z']),-signal['z']]))
 for ident in roots:root_frames[ident]=frame
for n in source['nodes']:
 out=copy.deepcopy(n)
 if n['id'] in hidden_signal_roots:out['visible']=False
 if n['dynamic'] and n['parent']>=0 and nodes_by_id[n['parent']]['dynamic']:
  new_nodes.append(out);continue
 m=(root_frames[n['id']] if n['id'] in root_frames else source_frame(world_matrices[n['id']][:3,3]))@world_matrices[n['id']]
 pose(out,m)
 if not n['dynamic']:
  out['geometry']=-1;out['instances']=[];out['kind']='group' if out['kind']!='light' else 'light'
 new_nodes.append(out)
new_by_id={n['id']:n for n in new_nodes}
def add_instances(es,frame,label):
 global next_id
 groups={}
 for e in es:
  n=e['node'];key=(n['geometry'],tuple(n['materials']))
  if key not in groups:
   out=copy.deepcopy(n);out.update(id=next_id,parent=-1,name=label,dynamic=False,position=vec([0,0,0]),scale=vec([1,1,1]),rotation=dict(x=0,y=0,z=0,w=1),instances=[]);next_id+=1;groups[key]=out
  inst=copy.deepcopy(e['instance']);inst['matrix']=np.round(frame@e['matrix'],6).flatten(order='F').tolist();groups[key]['instances'].append(inst)
 new_nodes.extend(groups.values())
for z in zones:add_instances(zone_entries[z['name']],z['matrix'],'Reused '+z['name'])
# Dismantle the square. Only its individual assets survive; the lawn slab,
# crossing paths, paving joints and old floor triangles are deliberately absent.
plaza_reused=0
for e in entries:
 if not in_plaza(e['center']) or e['center'][1]<.30 or max(e['extent'][[0,2]])>5:continue
 add_instances([e],plaza_frame(e['center']),'Reaproveitado da praça • '+plaza_asset(e['center']));plaza_reused+=1

def remap_point(p,frame=None):return vec(transform(frame or np.eye(4),arr(p))) if frame is not None else vec(move(arr(p)))
def apply_frame(p,frame):return vec(transform(frame,arr(p)))
def map_xz(item,frame=None):
 p=np.array([item['x'],0,item['z']]);q=transform(frame if frame is not None else source_frame(p),p);item['x'],item['z']=float(q[0]),float(q[2]);return q
for place,z in zip(world['places'],place_zones):
 orig=copy.deepcopy(place);q=transform(z['matrix'],np.array([place['x'],place['elevation'],place['z']]))
 place.update(x=float(q[0]),z=float(q[2]),elevation=float(q[1]),yaw=math.radians(z['yaw']),lotWidth=orig['w'],lotDepth=orig['d']);angle=math.radians(z['yaw']);place['w']=abs(math.cos(angle))*orig['w']+abs(math.sin(angle))*orig['d'];place['d']=abs(math.sin(angle))*orig['w']+abs(math.cos(angle))*orig['d']
 map_xz(place['door'],z['matrix']);place['door']['position']=apply_frame(orig['door']['position'],z['matrix']);place['door']['yaw']=math.radians(z['yaw'])
solids=[];solid_index={}
for s in old['solids']:
 # Keep physical assets, not old terrain boundaries/roads/pond contours.
 p=np.array([s['x'],s['bottom'],s['z']]);z=find_zone(p)
 owner=s.get('owner',0);frame=root_frames.get(owner,plaza_frame(p) if in_plaza(p) else z['matrix'] if z else None)
 if frame is None:continue
 if max(s['w'],s['d'])>70:continue
 a=copy.deepcopy(s);q=transform(frame,p);a.update(x=float(q[0]),z=float(q[2]),bottom=float(q[1]),top=float(q[1]+s['top']-s['bottom']),yaw=s.get('yaw',0)+math.atan2(frame[0,2],frame[2,2]),index=len(solids));solid_index[s['index']]=a['index'];solids.append(a)

# Clone complete donor homes (including their working doors and floor meshes).
clone_frames=[]
for i,z in enumerate(filler):
 donor=old['places'][z['donor']];donor_zone=place_zones[z['donor']]
 add_instances(zone_entries[donor_zone['name']],z['matrix'],'Reused residential interior '+str(i))
 root=donor['door']['id'];ids=[]
 for n in source['nodes']:
  if n['id']==root or n['parent'] in ids:ids.append(n['id'])
 mapping={ident:next_id+j for j,ident in enumerate(ids)};next_id+=len(ids)
 for ident in ids:
  n=copy.deepcopy(nodes_by_id[ident]);n['id']=mapping[ident]
  if ident==root:pose(n,z['matrix']@world_matrices[ident])
  else:n['parent']=mapping[n['parent']]
  new_nodes.append(n)
 p=copy.deepcopy(donor);q=transform(z['matrix'],np.array([p['x'],p['elevation'],p['z']]))
 p.update(name='Casa da orla '+str(i+1),x=float(q[0]),z=float(q[2]),elevation=float(q[1]),yaw=math.radians(z['yaw']),lotWidth=donor['w'],lotDepth=donor['d']);ang=math.radians(z['yaw']);p['w']=abs(math.cos(ang))*donor['w']+abs(math.sin(ang))*donor['d'];p['d']=abs(math.sin(ang))*donor['w']+abs(math.cos(ang))*donor['d'];p['door']['id']=mapping[root];map_xz(p['door'],z['matrix']);p['door']['position']=apply_frame(donor['door']['position'],z['matrix']);p['door']['yaw']=math.radians(z['yaw']);world['places'].append(p)
 for s in old['solids']:
  if not contains(donor_zone,np.array([s['x'],0,s['z']])) or s['w']>30 or s['d']>30:continue
  a=copy.deepcopy(s);q=transform(z['matrix'],np.array([s['x'],s['bottom'],s['z']]))
  a.update(index=len(solids),owner=mapping.get(s['owner'],0),x=float(q[0]),z=float(q[2]),bottom=float(q[1]),top=float(q[1]+s['top']-s['bottom']),yaw=s.get('yaw',0)+math.radians(z['yaw']));solids.append(a)
 clone_frames.append((donor_zone,z['matrix']))
world['solids']=solids

for key in ['residents','animals']:
 for a,original in zip(world[key],old[key]):
  frame=root_frames.get(a['id'],source_frame(arr(original['position'])));a['position']=apply_frame(original['position'],frame)
for a,original in zip(world['seats'],old['seats']):
 frame=root_frames.get(a['id'],source_frame(arr(original['position'])));q=map_xz(a,frame);a['position']=apply_frame(original['position'],frame);a['yaw']+=math.atan2(frame[0,2],frame[2,2])
for a,original in zip(world['locations'],old['locations']):
 frame=source_frame(np.array([a['x'],0,a['z']]));map_xz(a,frame);a['yaw']+=math.atan2(frame[0,2],frame[2,2])
for a,original in zip(world['devices'],old['devices']):
 frame=root_frames.get(dynamic_root(a['node']),source_frame(arr(original['position']))) if a['node'] in nodes_by_id else source_frame(arr(original['position']));a['position']=apply_frame(original['position'],frame)
 if a.get('lookAt') and arr(a['lookAt']).any():a['lookAt']=apply_frame(original['lookAt'],frame)
 a['yaw']+=math.atan2(frame[0,2],frame[2,2])
for a,original in zip(world['services'],old['services']):
 frame=source_frame(arr(original['position']));a['position']=apply_frame(original['position'],frame)
 if arr(original.get('anchor',{})).any():a['anchor']=apply_frame(original['anchor'],frame)
for a,original in zip(world['destructibles'],old['destructibles']):
 frame=root_frames.get(a['id'],source_frame(arr(original['position'])));a['position']=apply_frame(original['position'],frame);a['solidIndex']=solid_index.get(a['solidIndex'],-1)
for a,original in zip(world['streetLamps'],old['streetLamps']):
 frame=root_frames.get(a['node'],source_frame(arr(original['position'])));a['position']=apply_frame(original['position'],frame)
# Signal geometry and logical positions move through the same source frame.
world['signals']=world['signals'][:len(signal_targets)]
for a,uv in zip(world['signals'],signal_targets):q=point(uv);a.update(x=float(q[0]),z=float(q[2]))
for key,fields in [('music',['position']),('fish',['position']),('cinema',['projector']),('practice',['position']),('harbor',['loadPosition'])]:
 for field in fields:world[key][field]=vec(move(arr(old[key][field])))
world['fish']['actions']=[vec(move(arr(p))) for p in old['fish']['actions']]
world['fountain']=vec(transform(plaza_frame(np.array([-36,0,36])),np.array([-36,1.7,36])))
spawn_space=next((s for s in spaces if s['id']==plaza_sites['guide']['space']))
spawn_point=np.array(spawn_space['route'].coords[0]);world['spawn']=vec([spawn_point[0],grid_at(np.array([spawn_point[0],0,spawn_point[1]]))+.19,spawn_point[1]])
pose(new_by_id[old['playerId']],translate(arr(world['spawn'])))

# A connected graph follows the actual traced roads. No old street grid survives.
graph={};graph_points=[];graph_key={}
def graph_node(p):
 key=(round(p[0]/3),round(p[2]/3))
 if key not in graph_key:graph_key[key]=len(graph_points);graph_points.append(p.copy());graph[graph_key[key]]={}
 return graph_key[key]
for path in paths:
 if path['kind']=='trail':continue
 sequence=[]
 for p in path['xz'][::2]:sequence.append(graph_node(p))
 sequence.append(graph_node(path['xz'][-1]));path['graph']=sequence
 for a,b in zip(sequence,sequence[1:]):
  d=float(np.linalg.norm(graph_points[a]-graph_points[b]));graph[a][b]=d;graph[b][a]=d
graph_bins={}
for a,p in enumerate(graph_points):
 key=(math.floor(p[0]/7),math.floor(p[2]/7))
 for x in range(key[0]-1,key[0]+2):
  for z in range(key[1]-1,key[1]+2):
   for b in graph_bins.get((x,z),[]):
    d=float(np.linalg.norm(p-graph_points[b]))
    if d<7:graph[a][b]=graph[b][a]=d
 graph_bins.setdefault(key,[]).append(a)
def shortest(start,end):
 dist={start:0};prev={};heap=[(0,start)]
 while heap:
  cost,a=heapq.heappop(heap)
  if a==end:break
  if cost>dist[a]:continue
  for b,d in graph[a].items():
   n=cost+d
   if n<dist.get(b,1e20):dist[b]=n;prev[b]=a;heapq.heappush(heap,(n,b))
 if end not in dist:raise RuntimeError('Disconnected reference road graph')
 result=[end]
 while result[-1]!=start:result.append(prev[result[-1]])
 return result[::-1]
def nearest(uv):q=point(uv);return int(np.argmin([np.linalg.norm(p-q) for p in graph_points]))
anchors=[[900,411],[892,483],[838,514],[769,519],[743,506],[725,480],[740,474],[727,437],[697,366],[688,314],[596,265],[655,268],[776,276],[915,290],[1024,230],[1106,205],[1114,226],[1108,244],[1094,260],[1082,276],[1037,265],[1019,283],[978,330],[931,345],[902,373],[900,411]]
route_indices=[]
for a,b in zip(anchors,anchors[1:]):route_indices+=shortest(nearest(a),nearest(b))[:-1]
route=np.array([graph_points[i] for i in route_indices]);
if len(route)<50:raise RuntimeError('Invalid circular route')
def route_lane(offset):
 result=[]
 for i,p in enumerate(route):
  f=route[(i+1)%len(route)]-route[i-1];f/=max(.01,np.linalg.norm(f));q=p+np.array([f[2],0,-f[0]])*offset;q[1]=grid_at(q)+.08;result.append(q)
 return result
main_route=route_lane(1.6)
world['roadRoute']=[vec(p) for p in main_route]
for i,(a,original) in enumerate(zip(world['vehicles'],old['vehicles'])):
 if a['auto']:
  lane=route_lane(1.6 if i%2==0 or a['kind']=='bus' else -1.6)
  if i%2 and a['kind']!='bus':lane=lane[::-1]
  index=(i*43)%len(lane);p=lane[index];f=lane[(index+1)%len(lane)]-p;yaw=math.atan2(f[0],f[2]);frame=translate(p)@yaw_matrix(math.degrees(yaw-original.get('yaw',0)))@translate(-arr(original['position']))
  pose(new_by_id[a['id']],frame@world_matrices[a['id']]);a.update(position=vec(p),yaw=yaw,route=[vec(p) for p in lane],alternateRoute=[],routeStops=[],oneWay=False,speed=0)
 else:
  frame=root_overrides[a['id']];a['position']=apply_frame(original['position'],frame);a['yaw']=vehicle_yaws[a['id']];a['route']=[];a['alternateRoute']=[];a['routeStops']=[];a['oneWay']=False
for a,original in zip(world['boats'],old['boats']):a['position']=apply_frame(original['position'],root_overrides[a['id']]);a['yaw']=vehicle_yaws[a['id']]
stop_ids=['acacias','praca','hospital','livraria'];stop_uv=[[748,508],[900,420],[1024,344],[1068,289]]
world['stops']=[]
for ident,uv in zip(stop_ids,stop_uv):
 k=int(np.argmin([np.linalg.norm(p-point(uv)) for p in main_route]));p=main_route[k];f=main_route[(k+1)%len(main_route)]-main_route[k-1];f/=np.linalg.norm(f);side=np.array([f[2],0,-f[0]]);q=p+side*4.9
 world['stops'].append(dict(id=ident,name={'acacias':'Enseada','praca':'Centro','hospital':'Hospital','livraria':'Marina'}[ident],x=float(q[0]),z=float(q[2]),roadX=float(p[0]),roadZ=float(p[2]),yaw=math.atan2(f[0],f[2])))
# Sidewalk graph, joined at actual intersections and linked to destination doors.
nav=[];nav_key={};nav_edges=[]
def nav_node(p):
 key=(round(p[0]/2),round(p[2]/2))
 if key not in nav_key:nav_key[key]=len(nav);nav.append(dict(id=len(nav),x=float(p[0]),z=float(p[2]),edges=[]));nav_edges.append(set())
 return nav_key[key]
for path in paths:
 if path['kind']=='highway':continue
 for side in [-1,1]:
  sequence=[]
  for i in range(1,len(path['xz'])-1,4):
   p=path['xz'][i];f=path['xz'][i+1]-path['xz'][i-1];f/=max(.01,np.linalg.norm(f));q=p+np.array([f[2],0,-f[0]])*side*(path['width']/2+1.7)
   if distance_land(q)<0:continue
   sequence.append(nav_node(q))
  for a,b in zip(sequence,sequence[1:]):nav_edges[a].add(b);nav_edges[b].add(a)
nav_bins={}
for a in range(len(nav)):
 p=nav[a];key=(math.floor(p['x']/11),math.floor(p['z']/11))
 for x in range(key[0]-1,key[0]+2):
  for z in range(key[1]-1,key[1]+2):
   for b in nav_bins.get((x,z),[]):
    if math.hypot(p['x']-nav[b]['x'],p['z']-nav[b]['z'])<11:nav_edges[a].add(b);nav_edges[b].add(a)
 nav_bins.setdefault(key,[]).append(a)
for i,edges in enumerate(nav_edges):nav[i]['edges']=[dict(to=j,cost=math.hypot(nav[i]['x']-nav[j]['x'],nav[i]['z']-nav[j]['z']),crossing=False) for j in sorted(edges)]
world['nav']=nav

# Floor meshes and static terrain, roads and original donor floors.
floors=[]
def material(name,color,surface=0,sea=0):
 rgb=[int(color[i:i+2],16)/255 for i in [1,3,5]]
 m=dict(name=name,texture='',color=dict(zip('rgba',[*(c**2.2 for c in rgb),1])),emission=dict(r=0,g=0,b=0,a=1),roughness=.85,metalness=0,opacity=1,surface=surface,sea=sea,transparent=False,unlit=False,doubleSide=False,repeat=dict(x=1,y=1),offset=dict(x=0,y=0));materials.append(m);return len(materials)-1
grass=material('Reference terrain','#789672',5);sand=material('Reference shore','#b5aa8a',0);asphalt=material('Asfalto contínuo','#505b60',1);paving=material('Concreto das calçadas','#b6bab8',2);trail=material('Headland trails','#bcac8c',3);paint=material('Faixas viárias','#eee8db',0);sea=material('Reference sea','#5699af',0,1)
curb=material('Meio-fio','#9ca5a1',0);yellow=material('Linha de eixo','#d8bd69',0);deck_mat=material('Acesso aos píeres','#998667',3);materials[curb]['doubleSide']=True
def mesh_node(name,vertices,indices,mat,physical=False,normals=None):
 global next_id
 vertices=np.array(vertices,float);indices=np.array(indices,int)
 if normals is None:
  normals=np.zeros_like(vertices)
  for tri in indices.reshape(-1,3):
   a,b,c=vertices[tri];n=np.cross(b-a,c-a);n/=max(.00001,np.linalg.norm(n));normals[tri]+=n
  normals/=np.maximum(1e-6,np.linalg.norm(normals,axis=1))[:,None]
 g=dict(name=name,positions=np.round(vertices,5).flatten().tolist(),normals=np.round(normals,5).flatten().tolist(),uv=[],colors=[],indices=indices.tolist(),groups=[])
 index=len(new_geometry);new_geometry.append(g);new_nodes.append(dict(id=next_id,parent=-1,name=name,kind='mesh',geometry=index,dynamic=False,visible=True,castShadow=False,position=vec([0,0,0]),scale=vec([1,1,1]),rotation=dict(x=0,y=0,z=0,w=1),materials=[mat],instances=[]));next_id+=1
 if physical:floors.append(dict(vertices=np.round(vertices[indices],5).flatten().tolist()))
for j in range(0,GH-1,20):
 for i in range(0,GW-1,20):
  vertices=[];indices=[];rows=min(21,GH-j);cols=min(21,GW-i)
  for b in range(rows):
   for a in range(cols):vertices.append([GX+(i+a)*STEP,float(grid[j+b,i+a]),GZ+(j+b)*STEP])
  for b in range(rows-1):
   for a in range(cols-1):
    n=b*cols+a;indices.extend([n,n+cols,n+1,n+1,n+cols,n+cols+1])
  mesh_node(f'Terreno novo {i}-{j}',vertices,indices,grass,True)
network=StreetNetwork(paths,grid_at,(GX,GZ));street_audit=network.audit()
aprons=make_aprons(world['places'],network,grid_at)
network.frontages=sh.union_all([a['shape'] for a in aprons])
print('Street topology',street_audit,flush=True)
def surface(name,shape,mat,height=None,physical=True,cell=4):
 started=time.monotonic()
 vertices,indices,normals=network.mesh(shape,height,cell)
 if len(vertices):mesh_node(name,vertices,indices,mat,physical,normals)
 print(name,len(vertices),'vertices',round(time.monotonic()-started,2),'s',flush=True)
surface('Ruas e cruzamentos contínuos',network.asphalt,asphalt)
surface('Calçadas recortadas nos cruzamentos',network.sidewalk,paving,network.sidewalk_height,True,2)
surface('Trilhas conectadas',network.trails,trail,lambda xy:network.height(xy,.045))
surface('Faixas e travessias',network.paint,paint,lambda xy:network.height(xy,.078),False,2)
surface('Eixos interrompidos nos cruzamentos',network.yellow_paint,yellow,lambda xy:network.height(xy,.079),False,2)
surface('Canteiros interrompidos nos acessos',network.median,curb,lambda xy:network.height(xy,.14))
vertices,indices=network.curb_walls()
if len(vertices):mesh_node('Meios-fios com rebaixos de travessia',vertices,indices,curb,True)
entrance_meta=[]
base_nav=np.array([[n['x'],n['z']] for n in nav]);entrance_links=0;nav_road_barrier=network.asphalt.buffer(.04);sh.prepare(nav_road_barrier)
for apron in aprons:
 vertices,indices,normals=network.mesh(apron['shape'],lambda xy,apron=apron:apron_height(apron,network,xy),1)
 mesh_node('Entrada pavimentada • '+apron['place']['name'],vertices,indices,paving,True,normals)
 place=apron['place'];front=apron['front'];door=apron['door'];outside=door+front*.75;end=apron['end']
 chain=[]
 for t in np.linspace(0,1,max(2,int(np.linalg.norm(end-outside)/.8)+1)):
  q=outside*(1-t)+end*t;ident=len(nav);nav.append(dict(id=ident,x=float(q[0]),z=float(q[1]),edges=[]));nav_edges.append(set());chain.append(ident)
 for a,b in zip(chain,chain[1:]):nav_edges[a].add(b);nav_edges[b].add(a)
 for candidate in np.argsort(np.linalg.norm(base_nav-end,axis=1))[:18]:
  if not nav_road_barrier.intersects(LineString([end,base_nav[candidate]])):
   nav_edges[chain[-1]].add(int(candidate));nav_edges[candidate].add(chain[-1]);entrance_links+=1;break
 samples=[q for q in np.linspace(end,door+front*.5,max(3,int(np.linalg.norm(end-door)/.45)+1))]
 entrance_meta.append(dict(door=place['door']['id'],name=place['name'],gap=round(apron['gap'],4),
  sidewalk=vec([end[0],apron_height(apron,network,[end])[0],end[1]]),
  inside=vec([door[0]-front[0]*1.5,place['elevation']+.12,door[1]-front[1]*1.5]),
  samples=[vec([q[0],apron_height(apron,network,[q])[0],q[1]]) for q in samples]))
for i,edges in enumerate(nav_edges):nav[i]['edges']=[dict(to=j,cost=math.hypot(nav[i]['x']-nav[j]['x'],nav[i]['z']-nav[j]['z']),crossing=False) for j in sorted(edges)]
print('Paved entrances',len(aprons),'navigation connections',entrance_links,flush=True)
for slot in parking_slots:
 if slot.get('shared'):continue
 c=slot['center'];rot=yaw_matrix(math.degrees(slot['yaw']))[:3,:3]
 polygon=Polygon([(c+rot@np.array([a*slot['width']/2,0,b*slot['length']/2]))[[0,2]] for a,b in [(-1,-1),(-1,1),(1,1),(1,-1)]])
 surface('Baia de estacionamento '+str(slot['id']),polygon,asphalt,lambda xy:np.array([grid_at(np.array([x,0,z]))+.10 for x,z in xy]))

# Connected yards replace leftover lawns. All new boundaries and furniture
# have matching native collision; the pedestrian corridors remain open.
lawn_mat=material('Gramados dos quintais','#879c72',5);gravel_mat=material('Cascalho dos quintais','#a89f89',3)
soil_mat=material('Terra dos canteiros','#796c54',5);patio_mat=material('Pátios de pedra','#b2aa96',2)
materials[gravel_mat]['doubleSide']=True
wall_mat=material('Muros de quintal','#b2ac96');cap_mat=material('Capas dos muros','#d7cbb2')
leaf_mat=material('Jardins baixos','#63845d',5);flower_mat=material('Flores dos jardins','#d7ad6d')
box_batches={};landscape_roots=set();landscape_benches=0;landscape_trees=0;wall_count=0
def garden_box(center,size,yaw,mat,solid=False,label='Jardim',base=None):
 global wall_count
 x,z=center;w,h,d=size;y=grid_at(np.array([x,0,z]))+.065 if base is None else base
 c,s=math.cos(yaw),math.sin(yaw)
 p=np.array([[x+c*a+s*b,y+v,z-s*a+c*b] for a,v,b in [(-w/2,0,-d/2),(w/2,0,-d/2),(w/2,h,-d/2),(-w/2,h,-d/2),(-w/2,0,d/2),(w/2,0,d/2),(w/2,h,d/2),(-w/2,h,d/2)]])
 # Outward winding, flat faces and a real box collider use the same bounds.
 faces=[[0,3,2,1],[4,5,6,7],[0,4,7,3],[1,2,6,5],[3,7,6,2],[0,1,5,4]]
 vertices,indices=box_batches.setdefault(mat,([],[]))
 for face in faces:
  offset=len(vertices);vertices.extend(p[face]);indices.extend([offset,offset+1,offset+2,offset,offset+2,offset+3])
 if solid:
  solids.append(dict(index=len(solids),owner=0,x=float(x),z=float(z),w=float(w),d=float(d),bottom=float(y),top=float(y+h),yaw=float(yaw),active=True,vault=h<1.2,opaque=True,rangeBackstop=False))
 return y

def clone_garden_prop(root_id,site,kind):
 global next_id,landscape_benches
 ids=[]
 for n in source['nodes']:
  if n['id']==root_id or n['parent'] in ids:ids.append(n['id'])
 mapping={ident:next_id+i for i,ident in enumerate(ids)};next_id+=len(ids)
 center=site['center'];q=np.array([center[0],grid_at(np.array([center[0],0,center[1]]))+.085,center[1]])
 frame=translate(q)@yaw_matrix(math.degrees(site['yaw']))@translate(-world_matrices[root_id][:3,3])
 for ident in ids:
  n=copy.deepcopy(nodes_by_id[ident]);n['id']=mapping[ident];n['dynamic']=True
  if ident==root_id:pose(n,frame@world_matrices[ident]);n['name']='Quintal • '+kind
  else:n['parent']=mapping[n['parent']]
  new_nodes.append(n)
 root=mapping[root_id];landscape_roots.add(root);start=len(solids);yaw=site['yaw']
 if kind=='banco':
  for dz,w,d,h in [(0,2.9,.94,.82),(-.42,2.9,.14,1.575)]:
   at=transform(translate(q)@yaw_matrix(math.degrees(yaw)),np.array([0,0,dz]))
   solids.append(dict(index=len(solids),owner=root,x=float(at[0]),z=float(at[2]),w=w,d=d,bottom=float(q[1]),top=float(q[1]+h),yaw=float(yaw),active=True,vault=False,opaque=False,rangeBackstop=False))
  world['seats'].append(dict(id=root,x=float(q[0]),z=float(q[2]),yaw=float(yaw),seatTop=.82,width=2.9,depth=1.1,occupied=False,position=vec(q)));landscape_benches+=1
 else:
  w,d,h={'floreira':(2.1,1.1,1.4),'lixeira':(.87,.87,1.48),'poste':(.22,.22,5.86)}[kind]
  solids.append(dict(index=len(solids),owner=root,x=float(q[0]),z=float(q[2]),w=w,d=d,bottom=float(q[1]),top=float(q[1]+h),yaw=float(yaw),active=True,vault=False,opaque=False,rangeBackstop=False))
 world['destructibles'].append(dict(id=root,solidIndex=start,type={'banco':'bench','floreira':'planter','lixeira':'bin','poste':'lamp'}[kind],position=vec(q)))
 if kind=='poste':world['streetLamps'].append(dict(node=root,bulb=mapping[2379],position=vec(transform(translate(q)@yaw_matrix(math.degrees(yaw)),np.array([.4,5.5,0])))))

plaza_tree_center=np.array([-52,.075,20])
plaza_tree_entries=[e for e in entries if abs(e['center'][0]+52)<3.5 and abs(e['center'][2]-20)<3.5 and .3<e['center'][1]<9 and max(e['extent'][[0,2]])<3.5]
walk_guard=network.sidewalk.buffer(.4).union(network.asphalt.buffer(.6)).union(network.frontages.buffer(.45));sh.prepare(walk_guard)
all_paths=sh.union_all([s['path'].buffer(.4) for s in spaces]);body_guard=sh.union_all([rectangle(l['center'],l['size'],l['yaw'],.6) for l in planned]);sh.prepare(body_guard)
space_meta=[];parking_markings=[];path_points=[];wall_meta=[]
frontage_shapes=[a['shape'] for a in aprons];frontage_tree=sh.STRtree(frontage_shapes)
for number,s in enumerate(spaces):
 shape=s['shape'].difference(network.asphalt).difference(network.sidewalk).difference(network.frontages)
 yard_route=s['route'];path=s['path'].difference(network.asphalt).difference(network.sidewalk).difference(network.frontages)
 nearby_aprons=[aprons[i] for i in frontage_tree.query(s['path'].buffer(3.3))]
 def access_height(xy,nearby=nearby_aprons,kind=s['kind'],primary=aprons[s['lot']] if s['lot']>=0 else None):
  return network.height(xy,.085) if kind=='parking' else access_grade(xy,nearby,network,primary)
 if s['kind']=='parking':
  surface('Estacionamento de vizinhança '+s['id'],shape.union(path),asphalt,lambda xy:network.height(xy,.085),True,2)
  front=np.array([math.sin(s['yaw']),math.cos(s['yaw'])]);right=np.array([math.cos(s['yaw']),-math.sin(s['yaw'])]);c=s['center']
  for off in [-4.45,-1.5,1.5,4.45]:
   line=LineString([c+right*off-front*4.7,c+right*off+front*.8]);parking_markings.append(line.buffer(.045,cap_style='flat'))
  for off in [-3.05,0,3.05]:
   at=c+right*off-front*4.6;garden_box(at,[2.15,.12,.22],s['yaw'],cap_mat,True,'Limitador de vaga')
  end=yard_route.coords[-1]
 else:
  surface('Quintal e jardim '+s['id'],shape,lawn_mat,lambda xy:network.height(xy,.055),True,2)
  patio=s['patio'].difference(network.frontages).difference(network.sidewalk).difference(network.asphalt)
  surface('Pátio de convívio '+s['id'],patio,patio_mat,lambda xy:network.height(xy,.075),True,2)
  # Planting borders and access have different materials and follow the ground.
  surface('Canteiros '+s['id'],s['bed'],soil_mat,lambda xy:network.height(xy,.083),True,2)
  surface('Caminho lateral aberto '+s['id'],path,gravel_mat,access_height,True,.5)
  v,i,n=access_skirt(path,network,access_height)
  if len(v):mesh_node('Base do acesso lateral '+s['id'],v,i,gravel_mat,True,n)
  boundary=shape.boundary.difference(all_paths).difference(walk_guard).difference(body_guard.buffer(.25))
  for line in sh.get_parts(boundary):
   if line.geom_type not in ['LineString','LinearRing'] or line.length<1.5:continue
   length=line.length
   for start in np.arange(0,length,2.6):
    a=np.array(line.interpolate(start).coords[0]);b=np.array(line.interpolate(min(start+2.6,length)).coords[0]);delta=b-a;span=np.linalg.norm(delta)
    if span<.45:continue
    center=(a+b)/2;yaw=math.atan2(delta[0],delta[1]);height=.78+(number%3)*.12
    base=min(grid_at(np.array([a[0],0,a[1]])),grid_at(np.array([b[0],0,b[1]])))+.035
    rise=abs(grid_at(np.array([a[0],0,a[1]]))-grid_at(np.array([b[0],0,b[1]])))
    garden_box(center,[.22,height+rise,span+.015],yaw,wall_mat,True,'Muro baixo',base)
    garden_box(center,[.32,.09,span+.04],yaw,cap_mat,False,'Capa do muro',base+height+rise)
    wall_meta.append(dict(center=vec([center[0],base,center[1]]),yaw=float(yaw),width=.22,length=float(span),height=float(height+rise)));wall_count+=1
  # A mix of the original plaza trees, usable benches, flower boxes and bins.
  tree_site=asset_sites.allocate('Árvore de quintal '+s['id'],[5.4,5.4],False,s['id'])
  if tree_site:
   p=tree_site['center'];base=grid_at(np.array([p[0],0,p[1]]))+.075
   frame=translate(np.array([p[0],base,p[1]]))@yaw_matrix(math.degrees(tree_site['yaw']))@translate(-plaza_tree_center)
   add_instances(plaza_tree_entries,frame,'Árvore reaproveitada da praça');landscape_trees+=1
   for w,d,bottom,top in [(.77,.77,0,4.4),(4.84,4.62,3.3,7.9)]:solids.append(dict(index=len(solids),owner=0,x=float(p[0]),z=float(p[1]),w=w,d=d,bottom=float(base+bottom),top=float(base+top),yaw=float(tree_site['yaw']),active=True,vault=False,opaque=True,rangeBackstop=False))
  for kind,root,size in [('banco',2300,[4.4,3.7]),('floreira',2360,[2.7,1.7]),('floreira',2360,[2.7,1.7]),('lixeira',2311,[1.4,1.4])]+([('poste',2376,[1.6,1.3])] if number%4==0 or s['kind']=='garden' else []):
   site=asset_sites.allocate(kind+' '+s['id'],size,False,s['id'])
   if site:clone_garden_prop(root,site,kind)
  # Low flowers in planted soil, rather than another empty green rectangle.
  for bed in sh.get_parts(s['bed']):
   if bed.geom_type!='Polygon' or bed.area<.25:continue
   for along in np.arange(.6,bed.exterior.length,1.2):
    at=np.array(bed.exterior.interpolate(along).coords[0]);at=at*.7+np.array(bed.representative_point().coords[0])*.3
    if not bed.covers(Point(at)):continue
    garden_box(at,[.45,.36,.42],s['yaw'],leaf_mat)
    garden_box(at,[.17,.13,.17],s['yaw'],flower_mat,False,base=grid_at(np.array([at[0],0,at[1]]))+.43)
  end=yard_route.coords[-1]
 # Connect every yard or parking court to the original sidewalk graph through
 # the same explicit route used for the physical paving and wall openings.
 sampled=sh.get_coordinates(sh.segmentize(yard_route,.75));chain=[]
 for x,z in sampled:
  ident=len(nav);nav.append(dict(id=ident,x=float(x),z=float(z),edges=[]));nav_edges.append(set());chain.append(ident)
 for a,b in zip(chain,chain[1:]):nav_edges[a].add(b);nav_edges[b].add(a)
 start=sampled[0]
 for candidate in np.argsort(np.linalg.norm(base_nav-start,axis=1))[:25]:
  if not nav_road_barrier.intersects(LineString([start,base_nav[candidate]])):
   nav_edges[chain[0]].add(int(candidate));nav_edges[candidate].add(chain[0]);break
 samples=[vec([q[0],h,q[1]]) for q,h in zip(sampled,access_height(sampled))]
 space_meta.append(dict(id=s['id'],kind=s['kind'],lot=s['lot'],area=round(s['shape'].area,3),center=vec([s['center'][0],grid_at(np.array([s['center'][0],0,s['center'][1]])),s['center'][1]]),samples=samples))
 path_points.extend(samples)
for mat,(v,i) in box_batches.items():mesh_node('Jardins e muros • '+materials[mat]['name'],v,i,mat)
if parking_markings:surface('Marcações dos pequenos estacionamentos',sh.union_all(parking_markings),paint,lambda xy:network.height(xy,.105),False,2)
for i,edges in enumerate(nav_edges):nav[i]['edges']=[dict(to=j,cost=math.hypot(nav[i]['x']-nav[j]['x'],nav[i]['z']-nav[j]['z']),crossing=False) for j in sorted(edges)]
neighborhood_meta=dict(revision=1,oldSquareRemoved=True,oldSquareCenter=vec(point([880,325])),spaces=space_meta,walls=wall_meta,
 reusedPlazaPieces=plaza_reused,additionalPlazaTrees=landscape_trees,additionalBenches=landscape_benches,
 assets=[dict(name=name,position=vec(site['target']),space=site['space']) for name,site in plaza_sites.items()])
(ASSETS/'Resources/neighborhood-spaces.json').write_text(json.dumps(neighborhood_meta,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
(ROOT/'Reports/neighborhood').mkdir(exist_ok=True,parents=True)
neighborhood_report=dict(yards=sum(s['kind']=='yard' for s in spaces),gardens=sum(s['kind']=='garden' for s in spaces),parkingCourts=sum(s['kind']=='parking' for s in spaces),markedParkingSpaces=3*sum(s['kind']=='parking' for s in spaces),walls=wall_count,reusedPlazaPieces=plaza_reused,additionalPlazaTrees=landscape_trees,additionalBenches=landscape_benches,usedArea=round(sum(s['shape'].area for s in spaces),2),plazaRemoved=True)
(ROOT/'Reports/neighborhood/generation.json').write_text(json.dumps(neighborhood_report,indent=2),encoding='utf-8')
print('Neighborhood',neighborhood_report,flush=True)
quay=box(*port_bounds).difference(network.asphalt.buffer(2.3))
surface('Cais nivelado fora da via',quay,paving,lambda xy:np.array([max(.14,grid_at(np.array([x,0,z]))+.10) for x,z in xy]))
mesh_node('Oceano novo',[[GX-500,-.28,GZ-500],[GX-500,-.28,GZ+GH*STEP+500],[GX+GW*STEP+500,-.28,GZ+GH*STEP+500],[GX+GW*STEP+500,-.28,GZ-500]],[0,1,2,0,2,3],sea)

for f in source['floors']:
 pts=np.array(f['vertices'],float).reshape(-1,3)
 for k in range(0,len(pts),3):
  triangle=pts[k:k+3]
  if len(triangle)<3:continue
  z=find_zone(triangle.mean(axis=0))
  if in_plaza(triangle.mean(axis=0)) or z is None or z['name'] in ['pier','harbor'] or np.max(np.ptp(triangle[:,[0,2]],axis=0))>65:continue
  pieces=[triangle]
  if z['name'].startswith('building-'):
   donor=old['places'][int(z['name'].split('-')[1])]
   fw,fd=(49,30) if donor['kind']=='armory' else (donor['w'],donor['d'])
   limit=box(z['center'][0]-fw/2-.3,z['center'][2]-fd/2-.3,z['center'][0]+fw/2+.3,z['center'][2]+fd/2+.65)
   polygon=Polygon(triangle[:,[0,2]])
   if polygon.area<1e-7:continue
   if not limit.covers(polygon):
    clipped=polygon.intersection(limit)
    if clipped.is_empty:continue
    tris=sh.get_coordinates(sh.get_parts(sh.constrained_delaunay_triangles(clipped))).reshape(-1,4,2)[:,:3,:]
    coefficients=np.linalg.solve(np.c_[triangle[:,[0,2]],np.ones(3)],triangle[:,1])
    pieces=[np.c_[q[:,0],np.c_[q,np.ones(3)]@coefficients,q[:,1]] for q in tris]
  for donor,frame in [(z,z['matrix'])]+[(donor,frame) for donor,frame in clone_frames if donor is z]:
   for piece in pieces:
    new=(frame@np.c_[piece,np.ones(3)].T).T[:,:3];floors.append(dict(vertices=np.round(new,5).flatten().tolist()))

# Reuse a real donor tree, then distribute that asset on the traced green hills.
tree_candidates=[s for s in old['solids'] if .55<s['w']<1.0 and .55<s['d']<1.0 and 170<s['z']<210]
if not tree_candidates:tree_candidates=[s for s in old['solids'] if .65<s['w']<.8 and .65<s['d']<.8]
tree=tree_candidates[0];tree_center=np.array([tree['x'],tree['bottom'],tree['z']])
tree_entries=[e for e in entries if abs(e['center'][0]-tree_center[0])<3.8 and abs(e['center'][2]-tree_center[2])<3.8 and tree_center[1]+.3<e['center'][1]<tree_center[1]+9 and max(e['extent'][[0,2]])<4]
trees=0
for _ in range(2300):
 uv=np.array([rng.uniform(180,1295),rng.uniform(15,530)]);p=point(uv)
 if distance_land(p)<12 or road_distance(p)<9 or image_sample(occupied,uv)>0:continue
 if uv[0]>720 and uv[1]>290:continue
 p[1]=grid_at(p);frame=translate(p)@yaw_matrix(rng.choice([0,90,180,270]))@translate(-tree_center)
 add_instances(tree_entries,frame,'Reused donor tree');solids.append(dict(index=len(solids),owner=0,x=float(p[0]),z=float(p[2]),w=.7,d=.7,bottom=float(p[1]),top=float(p[1]+4),yaw=0,active=True,vault=False,opaque=True,rangeBackstop=False));trees+=1

world['solids']=solids
world['ground']=dict(x=GX,z=GZ,step=STEP,width=GW,height=GH,heights=np.round(grid,5).flatten().tolist())
world['layoutId']=layout['id'];world['version']=2
world['sourceHash']=hashlib.sha256((json.dumps(layout,sort_keys=True)+old['sourceHash']+Path(__file__).read_text(encoding='utf-8')+''.join((ROOT/'Tools'/name).read_text(encoding='utf-8') for name in ['street_network.py','building_frontages.py','neighborhood_spaces.py','coastal_roads.py'])).encode()).hexdigest()
# Piers/ladders from the old harbor move with its complete asset.
harbor_frame=next(z['matrix'] for z in zones if z['name']=='harbor')
for a in world['docks']:
 z=find_zone(np.array([a['x'],0,a['z']]));frame=z['matrix'] if z else harbor_frame;q=map_xz(a,frame);a['top']+=float(frame[1,3])
for a in world['ladders']:
 z=find_zone(np.array([a['x'],0,a['z']]));frame=z['matrix'] if z else harbor_frame;q=map_xz(a,frame);p=transform(frame,np.array([a['landX'],a['top'],a['landZ']]));a.update(landX=float(p[0]),landZ=float(p[2]),top=float(p[1]))

# Real walkable gangways join each reused deck to dry, supported ground.
gangways=[]
for number,dock in enumerate(world['docks']):
 if number==1:
  old_tip=dock['z']-dock['d']/2;dock['z']-=1.6;dock['d']+=3.2
  extension=box(dock['x']-dock['w']/2,old_tip-3.2,dock['x']+dock['w']/2,old_tip+.05)
  surface('Extensão de embarque do cais',extension,deck_mat,lambda xy,dock=dock:np.full(len(xy),dock['top']))
 x=dock['x'];start=dock['z']+dock['d']/2-.5;end=start+3
 while end<start+65:
  q=np.array([x,0,end]);rise=grid_at(q)+.10-dock['top']
  if distance_land(q)>4 and rise/(end-start)<.17:break
  end+=2
 top=grid_at(np.array([x,0,end]))+.10
 def gangway_height(xy,start=start,end=end,top=top,dock=dock):
  return np.maximum(np.array([grid_at(np.array([a,0,b]))+.06 for a,b in xy]),dock['top']+np.clip((xy[:,1]-start)/(end-start),0,1)*(top-dock['top']))
 shape=box(x-1.55,start,x+1.55,end+.3)
 surface('Passarela de acesso ao píer '+str(number+1),shape,deck_mat,gangway_height,True,2)
 gangways.append(dict(waterward=vec([x,dock['top'],start]),landward=vec([x,gangway_height(np.array([[x,end]]))[0],end]),width=3.1))
street_meta=network.metadata();street_meta['gangways']=gangways;street_meta['parking']=[dict(id=s['id'],position=vec(s['center']),yaw=s['yaw'],width=s['width'],length=s['length']) for s in parking_slots];street_meta['harbor']=world['harbor']['loadPosition']
interior_roots=set();compound_roots=set(landscape_roots)
for ident in root_frames:
 p=world_matrices[ident][:3,3];z=find_zone(p)
 if z and z['name'].startswith('building-'):
  room=old['places'][int(z['name'].split('-')[1])]
  if abs(p[0]-room['x'])<room['w']/2-.4 and abs(p[2]-room['z'])<room['d']/2-.4:interior_roots.add(ident)
 elif z or in_plaza(p):compound_roots.add(ident)
prop_moves=repair_props(world,new_nodes,network,grid_at,Polygon([point(p)[[0,2]] for p in layout['coast']]),occupied,pixel,image_sample,interior_roots,compound_roots)
street_audit['relocatedInteractiveProps']=len(prop_moves)
world['sourceHash']=hashlib.sha256((world['sourceHash']+(ROOT/'Tools/map_prop_repair.py').read_text(encoding='utf-8')).encode()).hexdigest()
(ROOT/'Reports/map-audit').mkdir(parents=True,exist_ok=True)
(ROOT/'Reports/map-audit/relocated-props.json').write_text(json.dumps(prop_moves,indent=2),encoding='utf-8')
(ASSETS/'Resources/street-network.json').write_text(json.dumps(street_meta,separators=(',',':')),encoding='utf-8')
(ROOT/'Reports/reference-map/street-audit.json').write_text(json.dumps(street_audit,indent=2),encoding='utf-8')
frontage_report=dict(buildings=len(world['places']),pavedEntrances=len(entrance_meta),navigationConnections=entrance_links,
 medianEntranceGap=round(float(np.median([a['gap'] for a in aprons])),3),maxEntranceGap=round(max(a['gap'] for a in aprons),3),pavedForecourtArea=round(network.frontages.area,2))
(ROOT/'Reports/frontages').mkdir(exist_ok=True,parents=True)
(ROOT/'Reports/frontages/after.json').write_text(json.dumps(frontage_report,indent=2),encoding='utf-8')
from house_ids import apply_house_ids
apply_house_ids(dict(nodes=new_nodes,materials=materials),world,ASSETS,ROOT/'Reports/house-ids')
house_names={p['door']['id']:p['name'] for p in world['places']}
for entrance in entrance_meta:entrance['name']=house_names[entrance['door']]
(ASSETS/'Resources/building-frontages.json').write_text(json.dumps(dict(revision=1,entrances=entrance_meta),separators=(',',':')),encoding='utf-8')

runtime_layout=dict(id=layout['id'],minX=GX,maxX=GX+(GW-1)*STEP,minZ=GZ,maxZ=GZ+(GH-1)*STEP,metersPerPixel=S,originX=OX,originY=OY,
 coast=[dict(x=float(point(p)[0]),y=float(point(p)[2])) for p in layout['coast']],
 roads=[dict(id=r['id'],kind=r['kind'],width=paved_width(r),points=[vec([p[0],h,p[2]]) for p,h in zip(r['xz'],network.height(r['xz'][:,[0,2]]))]) for r in paths],
 fountain=world['fountain'],harborRadio=vec(transform(harbor_frame,np.array([145.1,1.5,-152.8]))))
visual=dict(nodes=new_nodes,geometries=new_geometry,materials=materials,floors=floors)
remove_children(world,visual)
from natural_landscape import apply_nature
nature_report=apply_nature(visual,world,runtime_layout)
from doorways import apply_doorways
apply_doorways(visual,world)
# Keep future offline rebuilds consistent with the reviewed street furniture.
from street_review import review as review_streets
street_patch,street_review=review_streets(visual,world,runtime_layout,street_meta)
(ASSETS/'Imported/street-review.json').write_text(json.dumps(street_patch,separators=(',',':')),encoding='utf-8')
(ASSETS/'Resources/street-network.json').write_text(json.dumps(street_meta,separators=(',',':')),encoding='utf-8')
(ROOT/'Reports/map-revision').mkdir(parents=True,exist_ok=True)
(ROOT/'Reports/map-revision/review.json').write_text(json.dumps(street_review,indent=2,ensure_ascii=False),encoding='utf-8')
trees=nature_report['trees'];solids=world['solids']
(ROOT/'Reports/nature').mkdir(parents=True,exist_ok=True)
(ROOT/'Reports/nature/layout.json').write_text(json.dumps(nature_report,indent=2),encoding='utf-8')
for path,data in [(ASSETS/'Imported/visual.json',visual),(ASSETS/'Resources/world.json',world),(ASSETS/'Resources/reference-map.json',runtime_layout)]:
 path.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':'),allow_nan=False),encoding='utf-8')
report=dict(layout=layout['id'],coastAnchors=len(layout['coast']),tracedRoads=len(paths),sourceAssets='Unity/AssetLibrary/OriginalCity',oldPlayableMapRemoved=True,
 reusedOriginalBuildings=len(old['places']),reusedInteriorHomes=len(filler),trees=trees,donorTreePieces=len(tree_entries),residents=len(world['residents']),vehicles=len(world['vehicles']),boats=len(world['boats']),places=len(world['places']),navNodes=len(nav),circularRoutePoints=len(route),solids=len(solids))
(ROOT/'Reports/reference-map/generation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
# Reuse the physical district-guide board, but replace its obsolete grid map.
from PIL import Image,ImageDraw,ImageFont
board=Image.new('RGB',(850,548),'#f2e6c6');draw=ImageDraw.Draw(board)
font_path='C:/Windows/Fonts/arialbd.ttf'
def font(size):return ImageFont.truetype(font_path,size)
draw.rectangle((0,0,850,125),fill='#213e45');draw.text((25,15),'GUIA DA COSTA  /  01',font=font(20),fill='#b1cebb');draw.text((24,49),'Baía do Sol',font=font(56),fill='#f2e6c6');draw.text((596,74),'JARDINS DA ORLA',font=font(19),fill='#e0ac5a')
draw.rectangle((24,148,491,488),fill='#76aaa9')
def board_point(uv):return (24+uv[0]/1467*467,177+uv[1]/735*270)
draw.polygon([board_point(p) for p in layout['coast']],fill='#b9caa9')
for path in paths:draw.line([board_point(p) for p in path['uv']],fill='#f2e6c6' if path['kind']!='trail' else '#d5c6a0',width=2 if path['kind']=='highway' else 1)
labels=[('A','Você está aqui',pixel(arr(world['spawn']))),('B','Oficina Farol',pixel(np.array([world['places'][8]['x'],0,world['places'][8]['z']]))),('C','Hospital Baía',pixel(np.array([world['places'][9]['x'],0,world['places'][9]['z']]))),('D','Galpão da Maré',pixel(arr(world['fish']['position']))),('E','Cais das Antenas',pixel(arr(world['harbor']['loadPosition']))),('F','Cine da Passagem',pixel(arr(world['cinema']['projector'])))]
palette=['#213e45','#ad6750','#527e65','#38828a','#a4673d','#805567']
for i,(letter,title,uv) in enumerate(labels):
 x,y=board_point(uv);draw.ellipse((x-8,y-8,x+8,y+8),fill=palette[i]);draw.text((x-4,y-7),letter,font=font(12),fill='white')
 y=177+i*57;draw.ellipse((530,y-14,558,y+14),fill=palette[i]);draw.text((537,y-11),letter,font=font(19),fill='white');draw.text((577,y-15),title,font=font(25),fill='#213e45')
draw.text((25,516),'ENSEADAS  /  COLINAS  /  BAIRROS',font=font(15),fill='#41675f')
for index in [42,43]:board.save(ASSETS/'Imported/Textures'/f'texture-{index}.png')
print(json.dumps(report,indent=2),flush=True)
