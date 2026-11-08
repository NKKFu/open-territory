"""Compact, street-facing lots and continuous paved entrance aprons."""
import math
import numpy as np
import shapely as sh
from shapely import LineString, Polygon, Point, STRtree
from shapely.ops import nearest_points
from street_network import StreetNetwork, width as road_width


def rectangle(center,size,yaw,pad=0):
    c,s=math.cos(yaw),math.sin(yaw)
    return Polygon([(center[0]+c*x+s*z,center[1]-s*x+c*z) for x,z in [(-size[0]/2-pad,-size[1]/2-pad),(-size[0]/2-pad,size[1]/2+pad),(size[0]/2+pad,size[1]/2+pad),(size[0]/2+pad,-size[1]/2-pad)]])


def plan_lots(paths,places,fixed,land,to_pixel,raw_height,homes=200,minimum=170):
    """Reserve actual donor footprints instead of assigning big generic lots."""
    routes={r['id']:r for r in paths}
    lines={r['id']:LineString(r['xz'][:,[0,2]]) for r in paths}
    barriers=[lines[r['id']].buffer(road_width(r)/2+(StreetNetwork.sidewalk_width if r['kind']=='street' else 1.3)) for r in paths if r['kind']!='trail']+fixed
    tree=STRtree(barriers);sh.prepare(land)
    central={'town-spine','town-west-cross','town-low-cross','town-center-cross','town-upper-cross','town-diagonal','town-short','town-upper-block','waterfront','marina-street','east-port-street','town-bridge-loop'}
    village={'inland-to-bay','bay-front-west','bay-front-cut','bay-mid-link','bay-west-loop','west-shop-loop','west-cross','bay-west-descent'}
    candidates=[]
    for r in paths:
        if r['id'] not in central|village:continue
        line=lines[r['id']]
        for d in np.arange(12,line.length-12,2):
            p=np.array(line.interpolate(d).coords[0]);f=np.array(line.interpolate(d+.5).coords[0])-np.array(line.interpolate(d-.5).coords[0]);f/=np.linalg.norm(f)
            normal=np.array([-f[1],f[0]])
            uv=to_pixel(np.array([p[0],0,p[1]]))
            if not 200<uv[0]<1320 or not 170<uv[1]<640:continue
            score=((uv[0]-990)/1.1)**2+(uv[1]-355)**2+(0 if r['id'] in central else 23000)
            for side in (-1,1):candidates.append(dict(point=p,normal=normal*side,road=r['id'],score=float(score),distance=float(d),side=side))
    candidates.sort(key=lambda c:(c['score'],c['road'],c['distance'],c['side']))
    occupied={};lots=[];cache={}
    def bins(shape):
        x0,z0,x1,z1=shape.bounds
        return [(x,z) for x in range(math.floor(x0/32),math.floor(x1/32)+1) for z in range(math.floor(z0/32),math.floor(z1/32)+1)]
    def options(donor):
        p=places[donor];size=[p['w'],p['d']]
        if p['kind']=='armory':size=[49,30]
        # Leave at least 1.6 m of the sidewalk free even while a door swings out.
        setback=max(1.45,p['door']['width']-.6) if not p['door']['automatic'] else 1.45
        key=(*size,round(setback,3),round(p['door']['z']-p['z'],3))
        if key in cache:return cache[key]
        result=[]
        for c in candidates:
            r=routes[c['road']];n=c['normal'];front=-n;yaw=math.atan2(front[0],front[1])
            offset=road_width(r)/2+StreetNetwork.sidewalk_width+setback+max(p['door']['z']-p['z'],size[1]/2)
            center=c['point']+n*offset
            shape=rectangle(center,size,yaw,.8)
            if not land.contains(shape) or len(tree.query(shape,predicate='intersects')):continue
            if raw_height(np.array([center[0],0,center[1]]))>35:continue
            result.append(dict(**c,center=center,yaw=yaw,size=size,shape=shape,setback=setback,
                               anchor=c['point']+n*(road_width(r)/2+StreetNetwork.sidewalk_width+.25)))
        cache[key]=result;return result
    for number in range(len(places)+homes):
        donor=number if number<len(places) else [10,13,12][(number-len(places))%3]
        chosen=None
        for c in options(donor):
            buckets=bins(c['shape']);near={i for key in buckets for i in occupied.get(key,[])}
            if any(c['shape'].intersects(lots[i]['shape']) for i in near):continue
            chosen=dict(c,donor=donor,index=number);break
        if chosen is None:
            if number<len(places):raise RuntimeError('No street-facing lot for '+places[donor]['kind'])
            break
        index=len(lots);lots.append(chosen)
        for key in bins(chosen['shape']):occupied.setdefault(key,[]).append(index)
    if len(lots)<minimum:raise RuntimeError('Too few compact lots: '+str(len(lots)))
    return lots


def make_aprons(places,network,ground):
    """Pave the entire small forecourt and provide a direct, flush door route."""
    aprons=[];protected_asphalt=network.asphalt.buffer(.04)
    for place in places:
        yaw=place['yaw'];front=np.array([math.sin(yaw),math.cos(yaw)]);right=np.array([math.cos(yaw),-math.sin(yaw)])
        door=np.array([place['door']['x'],place['door']['z']]);width=place['lotWidth']
        # Search outward along the actual door normal, avoiding a connection
        # around the back or to the far side of another road.
        ray=LineString([door,door+front*25]);hits=ray.intersection(network.sidewalk)
        if hits.is_empty:raise RuntimeError('Door has no sidewalk in front: '+place['name'])
        nearest=nearest_points(Point(door),hits)[1];entry=np.array(nearest.coords[0]);gap=float(np.dot(entry-door,front))
        end=entry+front*.8;start=door-front*.42
        shape=Polygon([start-right*(width/2+.3),start+right*(width/2+.3),end+right*(width/2+.3),end-right*(width/2+.3)])
        nearby_walk=network.sidewalk.intersection(shape.buffer(8))
        shape=shape.difference(protected_asphalt).difference(network.sidewalk)
        apron=dict(place=place,door=door,front=front,right=right,start=start,end=end,gap=gap,shape=shape,walk=nearby_walk)
        aprons.append(apron)
    return aprons


def apron_height(apron,network,xy):
    xy=np.asarray(xy).reshape(-1,2);front=apron['front']
    from_wall=np.maximum(0,(xy-apron['start'])@front)
    to_walk=sh.distance(sh.points(xy),apron['walk'])
    blend=np.divide(from_wall,from_wall+to_walk,out=np.zeros(len(xy)),where=from_wall+to_walk>1e-8)
    # A doorway keeps the donor's interior floor height. At the street end,
    # use the same sidewalk height field so the two meshes meet exactly.
    beginning=apron['place']['elevation']+.12
    side=network.sidewalk_height(xy)
    linear=beginning*(1-blend)+side*blend
    terrain=network.height(xy,.035)
    return np.maximum(linear,terrain)
