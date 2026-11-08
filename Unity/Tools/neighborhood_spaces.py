"""Usable backyards and small roadside spaces, cut around the actual city.

Everything is deterministic and built offline. Paths are reserved before walls
or furniture: decoration must fit around access, never the other way round.
"""
import math
import numpy as np
import shapely as sh
from shapely import Point, LineString, Polygon, STRtree
from building_frontages import rectangle, apron_height
from street_network import StreetNetwork, width as road_width


def parts(shape):
    return [p for p in sh.get_parts(shape) if p.geom_type == 'Polygon' and p.area > 1]


def access_grade(xy,aprons,network,primary=None):
    """Meet each existing forecourt exactly, blending between nearby grades.

    Taking the maximum of neighboring forecourts lifts the edge of a lower
    entrance, creating a step. Distance weights preserve both boundary levels.
    """
    xy=np.asarray(xy).reshape(-1,2);points=sh.points(xy);base=network.height(xy,.09)
    weighted=np.zeros(len(xy));weights=np.zeros(len(xy));inside=np.full(len(xy),-np.inf)
    for apron in aprons:
        distance=sh.distance(points,apron['shape']);fade=np.clip(1-distance/3.2,0,1)
        weight=np.where(distance<3.2,1/np.maximum(.02,distance),0)
        # A private side access follows its own home's landing. Neighboring
        # entrances still constrain their exact edges, without pulling the
        # whole side path down to the next property's elevation.
        if apron is primary:weight*=4
        height=apron_height(apron,network,xy)
        weighted+=weight*(height-base)*fade;weights+=weight
        covered=sh.covers(apron['shape'],points)
        inside[covered]=np.maximum(inside[covered],height[covered])
    result=np.maximum(base,base+np.divide(weighted,weights,out=np.zeros(len(xy)),where=weights>0))
    result[np.isfinite(inside)]=inside[np.isfinite(inside)]
    return result


def access_skirt(path,network,height):
    vertices=[]
    for edge in sh.get_parts(path.boundary):
        if edge.geom_type not in ['LineString','LinearRing']:continue
        xy=sh.get_coordinates(sh.segmentize(edge,.5));top=height(xy);bottom=network.height(xy,.03)
        for j in range(len(xy)-1):
            if max(top[j]-bottom[j],top[j+1]-bottom[j+1])<.13:continue
            a,b=xy[j],xy[j+1]
            vertices.extend([[a[0],bottom[j],a[1]],[a[0],top[j],a[1]],[b[0],top[j+1],b[1]], [a[0],bottom[j],a[1]],[b[0],top[j+1],b[1]],[b[0],bottom[j+1],b[1]]])
    vertices=np.array(vertices).reshape(-1,3);tri=vertices.reshape(-1,3,3)
    normal=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);normal/=np.maximum(1e-9,np.linalg.norm(normal,axis=1))[:,None]
    return vertices,np.arange(len(vertices)),np.repeat(normal,3,axis=0)


def plan_spaces(lots, paths, fixed, land, ground):
    buildings=[rectangle(l['center'],l['size'],l['yaw'],.22) for l in lots]
    roads=sh.union_all([LineString(r['xz'][:,[0,2]]).buffer(road_width(r)/2+(StreetNetwork.sidewalk_width+.25 if r['kind']=='street' else 1.7)) for r in paths])
    barriers=buildings+[roads,*fixed]
    tree=STRtree(barriers); reserved=[];spaces=[]
    sh.prepare(land)
    def blocked(shape):
        return any(shape.intersects(barriers[i]) for i in tree.query(shape))
    def local(l,x,z):
        c,s=math.cos(l['yaw']),math.sin(l['yaw'])
        return l['center']+np.array([c*x+s*z,-s*x+c*z])
    for number,l in enumerate(lots):
        w,d=l['size'];back=10+(number%3)*3
        # Each property can grow into genuinely free space behind its facade.
        desired=rectangle(local(l,0,-back/2),[w+10,d+back],l['yaw'])
        nearby=[barriers[i] for i in tree.query(desired.buffer(2))]
        nearby.extend(p for p in reserved if p.intersects(desired))
        available=desired.difference(sh.union_all(nearby)).intersection(land)
        chosen=None
        for side in [1 if number%2 else -1,-1 if number%2 else 1]:
            route=LineString([local(l,side*(w/2-1),d/2+1.1),local(l,side*(w/2+.95),d/2+1.1),local(l,side*(w/2+.95),-d/2-3),local(l,side*min(w/2-2,4),-d/2-3)])
            corridor=route.buffer(.65,cap_style='square',join_style='mitre')
            if blocked(corridor) or any(corridor.intersects(p) for p in reserved):continue
            points=sh.get_coordinates(sh.segmentize(route,1.5));heights=[ground(np.array([x,0,z])) for x,z in points]
            if max((abs(b-a)/max(.001,np.linalg.norm(q-p)) for a,b,p,q in zip(heights,heights[1:],points,points[1:])),default=0)>.48:continue
            end=Point(route.coords[-1]);connected=[p for p in parts(available) if p.buffer(.05).intersects(end)]
            if not connected:continue
            plot=max(connected,key=lambda p:p.area)
            if plot.area<35:continue
            chosen=(plot,route,corridor);break
        if not chosen:continue
        plot,route,corridor=chosen
        patio=rectangle(local(l,0,-d/2-2.5),[min(w-2,12),4.6],l['yaw']).intersection(plot)
        bed=rectangle(local(l,0,-d/2-back+1.5),[max(4,w-2),1.5],l['yaw']).intersection(plot).difference(corridor.buffer(.3))
        spaces.append(dict(id='yard-'+str(number),kind='yard',lot=number,shape=plot,route=route,path=corridor,patio=patio,bed=bed,yaw=l['yaw'],center=np.array(plot.representative_point().coords[0])))
        reserved.append(plot.buffer(.12))

    # Vacant intervals along streets become a few shared gardens and parking
    # courts. They have their own clear connection to the existing sidewalk.
    occupied=sh.union_all(barriers+reserved);sh.prepare(occupied)
    candidates=[]
    for r in paths:
        if r['kind']!='street':continue
        line=LineString(r['xz'][:,[0,2]])
        for along in np.arange(16,line.length-16,8):
            p=np.array(line.interpolate(along).coords[0]);t=np.array(line.interpolate(along+.5).coords[0])-np.array(line.interpolate(along-.5).coords[0]);t/=np.linalg.norm(t)
            for side in [-1,1]:
                normal=np.array([-t[1],t[0]])*side
                # Center the shared spaces around the occupied neighborhood.
                score=np.linalg.norm(p-np.array([620.,700.]))
                candidates.append((float(score),r['id'],float(along),side,p,normal,r))
    candidates.sort(key=lambda x:x[:4])
    for kind,count in [('garden',10),('parking',10)]:
        made=0
        for _,_,_,_,p,n,r in candidates:
            w,d=([18,17] if kind=='garden' and made<2 else [12,12]) if kind=='garden' else [10.4,10.5]
            center=p+n*(road_width(r)/2+StreetNetwork.sidewalk_width+1+d/2);yaw=math.atan2(-n[0],-n[1]);shape=rectangle(center,[w,d],yaw)
            if occupied.intersects(shape.buffer(.6)) or any(shape.buffer(.7).intersects(s) for s in reserved) or not land.contains(shape.buffer(1)):continue
            h=[ground(np.array([q[0],0,q[1]])) for q in sh.get_coordinates(shape)[:-1]]
            if min(h)<.3 or max(h)-min(h)>3.4:continue
            f=-n;right=np.array([math.cos(yaw),-math.sin(yaw)])
            entry=center+f*(d/2+.5)
            if kind=='garden':
                end=center-right*(w/2-1.4)-f*(d/2-2)
                route=LineString([entry,center+f*(d/2-1.3)-right*(w/2-1.4),end]);path=route.buffer(.9,join_style='mitre')
                patio=shape.buffer(-1.2).difference(path);bed=shape.difference(shape.buffer(-.85)).difference(path.buffer(.2))
            else:
                end=center+f*(d/2-1.8);route=LineString([entry,end]);path=route.buffer(1.9,cap_style='square');patio=shape;bed=Polygon()
            spaces.append(dict(id=kind+'-'+str(made),kind=kind,lot=-1,shape=shape,route=route,path=path,patio=patio,bed=bed,yaw=yaw,center=center,entry=entry,width=w,depth=d))
            reserved.append(shape.buffer(.6));made+=1
            if made==count:break
    return spaces


class AssetSites:
    """Pack complete reused objects into gardens, reserving walking space."""
    def __init__(self,spaces,buildings):
        self.spaces=spaces;self.used=[];self.items=[]
        bodies=sh.union_all(buildings).buffer(.6)
        self.available={s['id']:s['shape'].buffer(-.7).difference(s['path'].buffer(.35)).difference(s['bed']).difference(bodies) for s in spaces if s['kind']!='parking'}
    def allocate(self,label,size,prefer_garden=False,space_id=None):
        ordered=sorted((s for s in self.spaces if s['kind']!='parking'),key=lambda s:(0 if s['kind']=='garden' else 1,-s['shape'].area))
        if space_id is not None:ordered=[s for s in ordered if s['id']==space_id]
        if not prefer_garden:ordered=sorted(ordered,key=lambda s:(len([a for a in self.items if a['space']==s['id']]),s['kind']!='yard',-s['shape'].area))
        for s in ordered:
            area=self.available[s['id']];x0,z0,x1,z1=area.bounds if not area.is_empty else (0,0,0,0)
            candidates=[np.array(area.representative_point().coords[0])] if not area.is_empty else []
            candidates.extend(np.array([x,z]) for x in np.arange(x0+.7,x1,1.4) for z in np.arange(z0+.7,z1,1.4))
            for p in candidates:
                shape=rectangle(p,size,s['yaw'],.3)
                if not area.covers(shape):continue
                if any(shape.intersects(a) for a in self.used):continue
                self.used.append(shape.buffer(.3));item=dict(label=label,space=s['id'],center=p,yaw=s['yaw'],size=size,shape=shape);self.items.append(item);return item
        return None


# Small complete assemblies from the removed square. No old cross, lawn slab
# or 52 m plaza surface belongs to the new scene.
PLAZA_ASSETS=[
    ('fountain',[-36,36],[10,10]),('guide',[-22.65,54.9],[5,2.5]),
    ('music',[-26,20.5],[6,5]),('ice-cream',[-57,53],[5.4,3.4]),
    ('phone',[-58.5,57],[2.5,3]),('camera',[-48.2,62.6],[2,2]),
    ('bus-stop',[-61.7,36],[3.5,7]),('bike-rack',[-54.15,58],[4.6,2.5]),
    ('popcorn',[-47,53],[4,3.5]),('wall-west',[-46,60],[6.5,1]),('wall-east',[-26,60],[6.5,1]),
]
PLAZA_ASSETS += [('tree-'+str(i),p,[5.5,5.5]) for i,p in enumerate([[-52,20],[-52,52],[-20,20],[-20,52],[-60,15],[-60,51],[-12,15],[-12,33],[-12,51]])]
PLAZA_ASSETS += [('bench-'+str(i),p,[3.8,2.5]) for i,p in enumerate([[-52,24],[-52,48],[-20,24],[-20,48],[-28,13.4]])]
PLAZA_ASSETS += [('planter-'+str(i),p,[2.7,1.7]) for i,p in enumerate([[-42,50],[-42,22],[-30,50],[-30,22]])]
PLAZA_ASSETS += [('lamp-'+str(i),p,[1.5,1.2]) for i,p in enumerate([[-42,43],[-42,29],[-30,43],[-30,29],[-61.3,19],[-61.3,55],[-10.7,19],[-10.7,37],[-10.7,55]])]
PLAZA_ASSETS += [('bin-'+str(i),p,[1.3,1.3]) for i,p in enumerate([[-49,22],[-49,54],[-17,22],[-17,54],[-61.4,11],[-61.4,47],[-10.6,11],[-10.6,29],[-10.6,47],[-25,13.4]])]
PLAZA_ASSETS += [('conversation-'+str(i),p,[3.5,2.5]) for i,p in enumerate([[-47,32],[-16,19]])]


def in_plaza(p):
    return -66<=p[0]<=-6 and 7.5<=p[2]<=64.5


def plaza_asset(p):
    # Reference coordinates sometimes extend beyond an assembly's collision
    # footprint (camera lookAt, guide anchor, music guests). Explicit grouping
    # preserves those relationships through the move.
    x,z=p[0],p[2]
    if p[1]>2.8:
        tree=min((a for a in PLAZA_ASSETS if a[0].startswith('tree-')),key=lambda a:math.hypot(x-a[1][0],z-a[1][1]))
        if math.hypot(x-tree[1][0],z-tree[1][1])<3:return tree[0]
    if -25.1<x<-20 and 53.6<z<56.4:return 'guide'
    if -29<x<-22 and 17<z<26.5:return 'music'
    if -59.8<x<-57.1 and 55.2<z<59.3:return 'phone'
    return min(PLAZA_ASSETS,key=lambda a:math.hypot(x-a[1][0],z-a[1][1]))[0]
