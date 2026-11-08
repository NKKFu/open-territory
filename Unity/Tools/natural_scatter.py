"""Open, dispersed planting without woodland centers or dense rings."""
import copy
import math
import random


def scattered_plan(world,layout,solids):
    from natural_landscape import Habitat,height
    habitat=Habitat(world,layout,solids)
    habitat.minimum_spacing=10
    rocks=[{k:copy.deepcopy(v) for k,v in r.items() if k!='solidIndex'} for r in world.get('rocks',[])]
    # Preserve the existing outcrops when redistributing the planting.
    if not rocks:
        rng=random.Random(48821);minx,minz,maxx,maxz=habitat.land.bounds
        for _ in range(15000):
            if len(rocks)>=560:break
            x,z=rng.uniform(minx+12,maxx-12),rng.uniform(minz+12,maxz-12)
            width=rng.uniform(.55,6.8);depth=width*rng.uniform(.7,1.2);high=width*rng.uniform(.4,.7)
            if not habitat.clear(x,z,.6*max(width,depth),False):continue
            rock=dict(position=dict(x=round(x,4),y=round(height(world['ground'],x,z),5),z=round(z,4)),
                      scale=dict(x=width,y=high,z=depth),yaw=rng.uniform(0,360),variant=rng.randrange(12))
            rocks.append(rock);habitat.reserve(x,z,.6*max(width,depth),False)
    else:
        for rock in rocks:
            p,s=rock['position'],rock['scale'];habitat.reserve(p['x'],p['z'],.6*max(s['x'],s['z']),False)
    rng=random.Random(902411);trees=[];minx,minz,maxx,maxz=habitat.land.bounds
    for _ in range(160000):
        if len(trees)>=7000:break
        x,z=rng.uniform(minx+12,maxx-12),rng.uniform(minz+12,maxz-12)
        chance=rng.random();size=rng.uniform(.48,.77) if chance<.27 else rng.uniform(.82,1.19) if chance<.85 else rng.uniform(1.28,1.72)
        s=dict(x=round(size*rng.uniform(.88,1.1),4),y=round(size*rng.uniform(.88,1.14),4),z=round(size*rng.uniform(.88,1.1),4))
        radius=3.7*max(s['x'],s['z'])
        if not habitat.clear(x,z,radius):continue
        form=rng.randrange(8)
        if s['y']<.65 and rng.random()<.4:form=7
        trees.append(dict(position=dict(x=round(x,4),y=round(height(world['ground'],x,z),5),z=round(z,4)),
                          scale=s,variant=form+rng.randrange(3)*8,grove=-1,yaw=rng.uniform(0,360)))
        habitat.reserve(x,z,radius,True)
    return trees,rocks,[]
