"""Keep reused interactive street furniture clear of the new road network.

Move complete roots with their colliders and interaction records. Never hide a
collider, trim a visible asset, or move a building to make the audit pass.
"""
import math
import numpy as np
import shapely as sh
from shapely import Polygon, Point, STRtree
from shapely.affinity import translate


def footprint(s):
    c,t=math.cos(s.get('yaw',0)),math.sin(s.get('yaw',0))
    return Polygon([(s['x']+c*x+t*z,s['z']-t*x+c*z) for x,z in [(-s['w']/2,-s['d']/2),(-s['w']/2,s['d']/2),(s['w']/2,s['d']/2),(s['w']/2,-s['d']/2)]])


def repair_props(world,nodes,network,ground,land,occupied,pixel,image_sample,interior_roots=None,compound_roots=None):
    lookup={n['id']:n for n in nodes}
    def root(ident):
        if ident not in lookup:return 0
        while lookup[ident]['parent']>=0:ident=lookup[ident]['parent']
        return ident
    protected={world['playerId'],world['droneId']}
    protected.update(a['id'] for key in ['vehicles','boats','residents','animals'] for a in world[key])
    protected.update(p['door']['id'] for p in world['places'])
    protected.update(world['harbor'].get(k,0) for k in ['load','mural','sock','platter'])
    protected.update(world['practice']['targets']);protected.update(world['practice']['lamps'])
    protected.update(interior_roots or [])
    compound_roots=compound_roots or set()
    solids=[s for s in world['solids'] if s.get('active',True)]
    shapes=[footprint(s) for s in solids];tree=STRtree(shapes)
    groups={}
    for i,s in enumerate(solids):
        owner=root(s['owner'])
        if owner and owner not in protected and lookup[owner]['dynamic']:
            groups.setdefault(owner,[]).append(i)
    forbidden=network.asphalt.buffer(.5).union(network.sidewalk.buffer(.3))
    if hasattr(network,'frontages'):forbidden=forbidden.union(network.frontages.buffer(.3))
    sh.prepare(forbidden);reserved=[];moved=[]
    def shift(p,delta):
        for k,d in zip('xyz',delta):p[k]=round(p.get(k,0)+float(d),5)
    for owner,ids in groups.items():
        active=[i for i in ids if solids[i]['top']>ground(np.array([solids[i]['x'],0,solids[i]['z']]))+.25]
        if not active:continue
        shape=sh.union_all([shapes[i] for i in active])
        at=np.array(shape.centroid.coords[0])
        in_new_lot=owner not in compound_roots and image_sample(occupied,pixel(np.array([at[0],0,at[1]])))>0
        if not forbidden.intersects(shape) and not in_new_lot:continue
        node=lookup[owner];old=np.array([node['position'][k] for k in 'xyz']);center=np.array(shape.centroid.coords[0])
        nearest=min(network.streets,key=lambda r:network.lines[r['id']].distance(Point(center)))
        line=network.lines[nearest['id']];along=line.project(Point(center));point=np.array(line.interpolate(along).coords[0])
        forward=np.array(line.interpolate(min(line.length,along+1)).coords[0])-np.array(line.interpolate(max(0,along-1)).coords[0]);forward/=max(.001,np.linalg.norm(forward))
        normal=np.array([-forward[1],forward[0]])
        if np.dot(center-point,normal)<0:normal=-normal
        found=None
        for distance in [3,5,7,10,14,19,25,34,46,62]:
            for sign in [1,-1]:
                for offset in [0,3,-3,7,-7,13,-13,22,-22]:
                    delta=normal*distance*sign+forward*offset;candidate=translate(shape,float(delta[0]),float(delta[1]));at=center+delta
                    if forbidden.intersects(candidate.buffer(.2)) or not land.contains(candidate.buffer(.5)):continue
                    if image_sample(occupied,pixel(np.array([at[0],0,at[1]])))>0:continue
                    if ground(np.array([at[0],0,at[1]]))<.2:continue
                    if any(candidate.buffer(.3).intersects(p) for p in reserved):continue
                    collisions=tree.query(candidate.buffer(.3),predicate='intersects')
                    if any(i not in ids and root(solids[i]['owner']) not in protected for i in collisions):continue
                    found=(delta,candidate,at);break
                if found:break
            if found:break
        if found is None:raise RuntimeError('Cannot place roadside asset '+str(owner)+' clear of streets')
        offset,candidate,at=found;dy=ground(np.array([at[0],0,at[1]]))+.1-min(solids[i]['bottom'] for i in active)
        delta=np.array([offset[0],dy,offset[1]]);shift(node['position'],delta)
        for i in ids:
            s=solids[i];s['x']+=float(delta[0]);s['z']+=float(delta[2]);s['bottom']+=float(dy);s['top']+=float(dy)
        old_seats=[]
        for seat in world['seats']:
            if root(seat['id'])==owner:
                old_seats.append(seat['position'].copy());shift(seat['position'],delta);seat['x']+=float(delta[0]);seat['z']+=float(delta[2])
        for person in world['residents']:
            if person['sitting'] and any(math.hypot(person['position']['x']-s['x'],person['position']['z']-s['z'])<2 for s in old_seats):
                shift(person['position'],delta);shift(lookup[person['id']]['position'],delta)
        for entry in world['devices']:
            if root(entry['node'])==owner:
                shift(entry['position'],delta)
                if entry.get('lookAt'):shift(entry['lookAt'],delta)
        for entry in world['destructibles']:
            if root(entry['id'])==owner:shift(entry['position'],delta)
        for entry in world['streetLamps']:
            if root(entry['node'])==owner:shift(entry['position'],delta)
        for entry in world['services']:
            if root(entry.get('node',0))==owner:
                shift(entry['position'],delta)
                if entry.get('anchor'):shift(entry['anchor'],delta)
        reserved.append(candidate)
        moved.append(dict(id=owner,offset=[round(float(v),4) for v in delta]))
    return moved
