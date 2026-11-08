import json, math, os, sys
from collections import Counter
from pathlib import Path
sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import shapely as sh
from shapely import Polygon, LineString, Point, STRtree
from shapely.affinity import rotate, translate
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT/'OpenTerritory/Assets/Resources'
w = json.loads((RES/'world.json').read_text(encoding='utf-8-sig'))
layout = json.loads((RES/'reference-map.json').read_text(encoding='utf-8-sig'))
spaces = json.loads((RES/'neighborhood-spaces.json').read_text(encoding='utf-8-sig'))
def rect(x,z,width,depth,yaw=0):
    return translate(rotate(sh.box(-width/2,-depth/2,width/2,depth/2),-yaw, use_radians=True),x,z)

obstacles=[];labels=[]
def add(shape,label): obstacles.append(shape); labels.append(label)
for i,p in enumerate(w['places']): add(rect(p['x'],p['z'],p.get('lotWidth',0) or p['w'],p.get('lotDepth',0) or p['d'],p.get('yaw',0)).buffer(1),f'building-{i}')
for s in w['solids']:
    if s.get('active',True) and s['top']-s['bottom']>.16:
        add(rect(s['x'],s['z'],s['w'],s['d'],s.get('yaw',0)).buffer(.4),f'solid-{s["index"]}')
for s in spaces['spaces']:
    points=[(p['x'],p['z']) for p in s['samples']]
    if points:add(sh.MultiPoint(points).convex_hull.buffer(.5),s['id'])
for t in w['natureTrees']:
    add(Point(t['position']['x'],t['position']['z']).buffer(2.6*max(t['scale']['x'],t['scale']['z'])),f'tree-{t["node"]}')
add(rect(126.112,412.534,46,60,math.radians(169.677)),'cemetery')
add(rect(95,499,66,86,math.radians(349.677)),'car-dealership')
add(rect(210,410,105,90,math.radians(48)),'sunmeadow-park')
roads=[]
for r in layout['roads']:
    line=LineString([(p['x'],p['z']) for p in r['points']])
    roads.append((r,line))
    add(line.buffer(r['width']/2+(3.6 if r['kind']=='street' else 1.8)),r['id'])
tree=STRtree(obstacles)
g=w['ground']
def height(x,z):
    fx=max(0,min(g['width']-1.001,(x-g['x'])/g['step']));fz=max(0,min(g['height']-1.001,(z-g['z'])/g['step']))
    i,j=int(fx),int(fz);a,b=fx-i,fz-j;at=j*g['width']+i
    h,hr,ht,hc=[g['heights'][q] for q in [at,at+1,at+g['width'],at+g['width']+1]]
    return h+a*(hr-h)+b*(ht-h) if a+b<=1 else hc+(1-a)*(ht-hc)+(1-b)*(hr-hc)
candidates=[]; rejected=Counter(); clear=[]
for r,line in roads:
    if r['kind']!='street':continue
    for along in range(30,int(line.length)-30,7):
        p=line.interpolate(along);a=line.interpolate(along-1);b=line.interpolate(along+1)
        dx,dz=b.x-a.x,b.y-a.y;length=math.hypot(dx,dz)
        for side in [-1,1]:
            nx,nz=-dz/length*side,dx/length*side
            offset=r['width']/2+3.6+24.8
            x,z=p.x+nx*offset,p.y+nz*offset
            if math.hypot(x-603,z-703)>1100:continue
            yaw=math.atan2(-nx,-nz)
            # Covers every wall, awning, roof overhang, planter and both approaches.
            footprint=rect(x,z,27,27,yaw).union(rect(x-nx*18.5-4*math.cos(yaw),z-nz*18.5+4*math.sin(yaw),13,11,yaw))
            hits=[labels[k] for k in tree.query(footprint) if footprint.intersects(obstacles[k])]
            if hits:
                rejected.update([h.split('-')[0] for h in hits]);continue
            clear.append((x,z))
            corners=list(footprint.exterior.coords)[:-1];hs=[height(*q) for q in corners]+[height(x,z)]
            if min(hs)<1 or max(hs)-min(hs)>3.6:continue
            score=math.hypot(x-603,z-703)+200*(max(hs)-min(hs))
            candidates.append(dict(x=x,z=z,yaw=yaw,road=r['id'],along=along,height=height(x,z),range=max(hs)-min(hs),score=score))
candidates.sort(key=lambda p:p['score'])
out=Path(__file__).parent
(out/'candidates.json').write_text(json.dumps(candidates[:30],indent=2),encoding='utf-8')
print(json.dumps(candidates[:10],indent=2))
print('Rejected',rejected,'Clear before height',len(clear),clear[:8])
im=Image.new('RGB',(1400,1100),'#d7dfc8');d=ImageDraw.Draw(im)
left,right,bottom,top=0,1250,220,1120
def xy(x,z):return ((x-left)/(right-left)*1400,1100-(z-bottom)/(top-bottom)*1100)
def draw_poly(poly,fill,outline=None):
    if poly.geom_type=='Polygon':d.polygon([xy(*p) for p in poly.exterior.coords],fill=fill,outline=outline)
for r,line in roads:
    d.line([xy(p['x'],p['z']) for p in r['points']],fill='#d5d2c7',width=max(2,int((r['width']+7.2)*1.12)))
    d.line([xy(p['x'],p['z']) for p in r['points']],fill='#626968',width=max(1,int(r['width']*1.12)))
for p in w['places']:draw_poly(rect(p['x'],p['z'],p.get('lotWidth',0) or p['w'],p.get('lotDepth',0) or p['d'],p.get('yaw',0)),'#a68662')
for s in spaces['spaces']:
    points=[(p['x'],p['z']) for p in s['samples']]
    if points:draw_poly(sh.MultiPoint(points).convex_hull,'#a9bf8d')
for t in w['natureTrees']:
    x,z=t['position']['x'],t['position']['z'];u,v=xy(x,z);d.ellipse([u-2,v-2,u+2,v+2],fill='#738b66')
for i,p in enumerate(candidates[:20]):
    draw_poly(rect(p['x'],p['z'],29,35,p['yaw']),'#d03b30','#ffffff');d.text(xy(p['x'],p['z']),str(i),fill='white')
d.ellipse([xy(603,703)[0]-6,xy(603,703)[1]-6,xy(603,703)[0]+6,xy(603,703)[1]+6],fill='#173b72')
im.save(out/'placement-map.png')
