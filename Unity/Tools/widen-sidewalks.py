"""Widen the saved city without replanning buildings, grounds or street furniture.

Export current World-layer collider footprints through Unity MCP first to
Reports/sidewalks/scene-colliders.json. Only the sidewalk and curb meshes change;
existing forecourts, garden floors and access ramps retain their exact geometry.
"""
import argparse
import hashlib
import json
import math
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import numpy as np
import shapely as sh
from shapely import LineString, MultiPoint, Point, box
from street_network import StreetNetwork, clean, width

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT/'OpenTerritory/Assets'
OUT = ROOT/'Reports/sidewalks'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apply', action='store_true', help='Write the validated patch to the active export')
parser.add_argument('--baseline', type=Path, help='Original 2.2 m asset backup when refining an already widened export')
args = parser.parse_args()


def read(name):
    return json.loads((ASSETS/name).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


names = ['Imported/visual.json', 'Resources/world.json', 'Resources/street-network.json']
original_hashes = {name: digest(ASSETS/name) for name in names}
visual, world, street = (read(name) for name in names)
reference = read('Resources/reference-map.json')
baseline = None
baseline_street = street
if args.baseline:
    baseline = json.loads((args.baseline/'Imported/visual.json').read_text(encoding='utf-8-sig'))
    baseline_street = json.loads((args.baseline/'Resources/street-network.json').read_text(encoding='utf-8-sig'))
old_width = baseline_street['sidewalkWidth']
if old_width >= StreetNetwork.sidewalk_width:
    raise SystemExit('The current export already has the requested sidewalk width.')
g = world['ground']
grid = np.array(g['heights']).reshape(g['height'], g['width'])


def ground(p):
    fx = np.clip((p[0]-g['x'])/g['step'], 0, g['width']-1.001)
    fz = np.clip((p[2]-g['z'])/g['step'], 0, g['height']-1.001)
    i, j = int(fx), int(fz)
    a, b = fx-i, fz-j
    if a+b <= 1:
        return float(grid[j, i]+a*(grid[j, i+1]-grid[j, i])+b*(grid[j+1, i]-grid[j, i]))
    return float(grid[j+1, i+1]+(1-a)*(grid[j+1, i]-grid[j+1, i+1])+(1-b)*(grid[j, i+1]-grid[j+1, i+1]))


paths = [dict(id=r['id'], kind=r['kind'], width=r['width'],
              xz=np.array([[p[k] for k in 'xyz'] for p in r['points']]),
              heights=np.array([p['y']-.07 for p in r['points']])) for r in reference['roads']]
old = StreetNetwork(paths, ground, (g['x'], g['z']), sidewalk_width=old_width)
network = StreetNetwork(paths, ground, (g['x'], g['z']))
# Reuse the reviewed crossings, including moved/removed zebra crossings. Never
# bring back the automatically proposed crossings from the original generator.
crossings = []
for crossing in street['crossings']:
    ends = np.array([[p['x'], p['z']] for p in crossing['ends']])
    normal = (ends[1]-ends[0])/np.linalg.norm(ends[1]-ends[0])
    road = next(r for r in paths if r['id'] == crossing['road'])
    crossings.append(dict(center=np.array([crossing['position']['x'], crossing['position']['z']]),
                          normal=normal, tangent=np.array([normal[1], -normal[0]]),
                          width=width(road), road=road['id']))
old.crossings = network.crossings = crossings
band = network.sidewalk.difference(old.sidewalk)
sh.prepare(band)
by_name = {n['name']: n for n in visual['nodes']}
sidewalk_node = by_name['Calçadas recortadas nos cruzamentos']
original = (baseline or visual)['geometries'][sidewalk_node['geometry']]
tri = np.array(original['positions']).reshape(-1, 3)[np.array(original['indices']).reshape(-1, 3)]
a = tri[:, 1][:, [0, 2]]-tri[:, 0][:, [0, 2]]
b = tri[:, 2][:, [0, 2]]-tri[:, 0][:, [0, 2]]
old_area = np.abs(a[:, 0]*b[:, 1]-a[:, 1]*b[:, 0]).sum()/2
assert abs(old_area-old.sidewalk.area) < .1, 'Export topology differs from the current street layout.'

print('Reserving complete existing gardens, forecourts and physical obstacles', flush=True)
reserved = []
reserved_meshes = []
ground_materials = {i for i, material in enumerate(visual['materials']) if material['name'] in
                    {'Gramados dos quintais', 'Cascalho dos quintais', 'Terra dos canteiros', 'Pátios de pedra'}}
assert len(ground_materials) == 4, 'Authored neighborhood ground materials not found.'
parking_shapes = []
for slot in street['parking']:
    yaw = slot['yaw']
    right = np.array([math.cos(yaw), -math.sin(yaw)])
    forward = np.array([math.sin(yaw), math.cos(yaw)])
    center = np.array([slot['position']['x'], slot['position']['z']])
    parking_shapes.append(sh.Polygon([center+right*x+forward*z for x, z in
        [(-slot['width']/2-.3, -slot['length']/2-.3), (-slot['width']/2-.3, slot['length']/2+.3),
         (slot['width']/2+.3, slot['length']/2+.3), (slot['width']/2+.3, -slot['length']/2-.3)]]))
reserved.extend(parking_shapes)
# Keep the existing coastline and terrain. Cells with a low corner reserve the
# entire terrain triangle, so widening never creates a slab over the sea.
zz, xx = np.indices((g['height']-1, g['width']-1))
xx, zz = (g['x']+xx.ravel()*g['step']), (g['z']+zz.ravel()*g['step'])
step = g['step']
low_a = np.minimum.reduce((grid[:-1, :-1], grid[1:, :-1], grid[:-1, 1:])).ravel() < .5
low_b = np.minimum.reduce((grid[:-1, 1:], grid[1:, :-1], grid[1:, 1:])).ravel() < .5
for mask, offsets in [(low_a, [(0, 0), (0, step), (step, 0)]),
                      (low_b, [(step, 0), (0, step), (step, step)])]:
    points = np.stack([np.column_stack((xx[mask]+x, zz[mask]+z)) for x, z in offsets], axis=1)
    triangles = sh.polygons(points)
    triangles = triangles[sh.intersects(band, triangles)]
    if len(triangles):
        reserved.append(sh.union_all(triangles))
for node in visual['nodes']:
    if node['geometry'] < 0 or not node['visible'] or node['dynamic']:
        continue
    # These are complete authored floors, not just their parcel centers.
    if not (node['name'].startswith('Entrada pavimentada') or
            any(m in ground_materials for m in node['materials'])):
        continue
    geometry = visual['geometries'][node['geometry']]
    vertices = np.asarray(geometry['positions']).reshape(-1, 3)[:, [0, 2]]
    if not len(vertices) or not band.intersects(box(*vertices.min(axis=0), *vertices.max(axis=0))):
        continue
    assert node['parent'] == -1 and all(abs(node['position'][k]) < 1e-6 for k in 'xyz'), node['name']
    triangles = sh.polygons(vertices[np.asarray(geometry['indices']).reshape(-1, 3)])
    triangles = triangles[(sh.area(triangles) > 1e-8) & sh.intersects(band, triangles)]
    if len(triangles):
        reserved.append(sh.union_all(triangles))
        reserved_meshes.append(node['name'])

scene_colliders = json.loads((OUT/'scene-colliders.json').read_text(encoding='utf-8-sig'))
assert len(scene_colliders) > 1000, 'Export physical footprints from the loaded BaiaDoSol scene first.'
near_obstacles = 0
for collider in scene_colliders:
    shape = MultiPoint(collider['points']).convex_hull
    if shape.area < 1e-8 or not band.intersects(shape.buffer(.45)):
        continue
    p = shape.representative_point()
    y = network.sidewalk_height([[p.x, p.y]])[0]
    if collider['bottom'] > y+2 or collider['top'] < y+.08:
        continue
    reserved.append(shape.buffer(.45, quad_segs=4))
    near_obstacles += 1
protected = sh.union_all(reserved)
# Never narrow an existing sidewalk. Reservations apply only to the added land.
extension = clean(band.difference(protected.buffer(.02, quad_segs=4)), .6)
network.sidewalk = clean(old.sidewalk.union(extension), .6)
assert old.sidewalk.difference(network.sidewalk).area < .05
assert extension.intersection(protected).area < .001
assert network.sidewalk.intersection(network.asphalt).area < .001
assert extension.area > old.sidewalk.area*.15, 'Insufficient free space to widen the current sidewalks.'
print('Added sidewalk area:', round(extension.area, 2), 'm2', flush=True)

floor_keys = set()
materials = set()
floor_lookup = {}


def floor_id(flat):
    return len(flat), tuple(flat[:12]), tuple(flat[-12:])


def floor_key(flat):
    return str(math.floor(flat[0]/32))+','+str(math.floor(flat[2]/32))


for floor in visual['floors']:
    floor_lookup.setdefault(floor_id(floor['vertices']), []).append(floor)
removed = set()
added = []


def replace(name, mesh):
    vertices, indices, normals = mesh
    node = by_name[name]
    previous = visual['geometries'][node['geometry']]
    flat = np.array(previous['positions']).reshape(-1, 3)[np.array(previous['indices'])].ravel().tolist()
    matches = [f for f in floor_lookup.get(floor_id(flat), []) if f['vertices'] == flat]
    assert matches, 'Original physical surface not found: '+name
    removed.update(id(f) for f in matches)
    floor_keys.add(floor_key(flat))
    vertices, indices = np.round(vertices, 5), np.asarray(indices, int)
    visual['geometries'][node['geometry']] = dict(name=name, positions=vertices.ravel().tolist(),
        indices=indices.tolist(), normals=np.round(normals, 5).ravel().tolist(), uv=[], colors=[], groups=[])
    flat = vertices[indices].ravel().tolist()
    added.append(dict(vertices=flat))
    floor_keys.add(floor_key(flat))
    materials.update(node['materials'])


print('Triangulating the added pavement while retaining every old pavement vertex', flush=True)
v, indices, normals = network.mesh(extension, network.sidewalk_height, 2)
old_vertices = np.array(original['positions']).reshape(-1, 3)
old_normals = np.array(original['normals']).reshape(-1, 3)
replace(sidewalk_node['name'], (np.concatenate((old_vertices, v)),
    np.concatenate((np.array(original['indices']), np.asarray(indices)+len(old_vertices))),
    np.concatenate((old_normals, normals))))
v, indices = network.curb_walls()
tri = v[np.asarray(indices).reshape(-1, 3)]
normals = np.cross(tri[:, 1]-tri[:, 0], tri[:, 2]-tri[:, 0])
normals /= np.maximum(1e-8, np.linalg.norm(normals, axis=1))[:, None]
replace('Meios-fios com rebaixos de travessia', (v, indices, np.repeat(normals, 3, axis=0)))
visual['floors'] = [f for f in visual['floors'] if id(f) not in removed]+added

# Probe the added outer walking lane as well as the pre-existing center lane.
samples = list(baseline_street['sidewalkSamples'])
outer_samples = []
walkable_extension = extension.buffer(-.32)
sh.prepare(walkable_extension)
for road in network.streets:
    line = network.lines[road['id']]
    for d in np.arange(8, line.length-8, 12):
        c = np.array(line.interpolate(d).coords[0])
        tangent = np.array(line.interpolate(d+.5).coords[0])-np.array(line.interpolate(d-.5).coords[0])
        tangent /= np.linalg.norm(tangent)
        for side in (-1, 1):
            for lateral in (old_width+.45, network.sidewalk_width-.45):
                p = c+np.array([-tangent[1], tangent[0]])*side*(width(road)/2+lateral)
                if walkable_extension.covers(Point(p)):
                    outer_samples.append(dict(x=round(float(p[0]), 5),
                        y=round(float(network.sidewalk_height([p])[0]), 5), z=round(float(p[1]), 5)))
samples.extend(outer_samples)
assert len(outer_samples) > 400, 'Widening must provide usable outer walking lanes.'
before_hash = world['sourceHash']
world['sourceHash'] = hashlib.sha256(before_hash.encode()+Path(__file__).read_bytes()).hexdigest()
street.update(sidewalkWidth=network.sidewalk_width, sidewalkSamples=samples, widenedSidewalkSamples=outer_samples,
              previousSidewalkWidth=old_width, wideningRevision=1)
patch = dict(revision=1, sourceHash=world['sourceHash'], previousSourceHash=before_hash,
             nodes=[], solids=[], materials=sorted(materials), floorKeys=sorted(floor_keys), newNodes=[], batchKeys=[])
report = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), previousWidth=old_width,
    requestedWidth=network.sidewalk_width, previousArea=round(old.sidewalk.area, 2),
    addedArea=round(extension.area, 2), finalArea=round(network.sidewalk.area, 2),
    oldPavementRemovedArea=round(old.sidewalk.difference(network.sidewalk).area, 6),
    addedPavementAcrossRoadArea=round(extension.intersection(network.asphalt).area, 6),
    addedPavementAcrossReservedArea=round(extension.intersection(protected).area, 6),
    reservedMeshes=len(reserved_meshes), reservedPhysicalObstacles=near_obstacles,
    reservedParkingBays=len(parking_shapes), addedPavementAcrossParkingBays=round(extension.intersection(sh.union_all(parking_shapes)).area, 6),
    addedWalkingSamples=len(outer_samples), totalWalkingSamples=len(samples),
    retainedCrossings=len(crossings), changedGeometry=[sidewalk_node['name'], 'Meios-fios com rebaixos de travessia'],
    sourceHash=world['sourceHash'], passed=True)
outputs = list(zip(names, (visual, world, street)))+[('Imported/sidewalk-patch.json', patch)]
stage = OUT/'staged'
for name, data in outputs:
    path = stage/name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
if args.apply:
    assert all(digest(ASSETS/name) == value for name, value in original_hashes.items()), 'Concurrent export change; regenerate the patch.'
    backup = OUT/'backups'/datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
    for name in names+['Scenes/BaiaDoSol.unity']:
        path = backup/name
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ASSETS/name, path)
    report['backup'] = str(backup)
    for name, _ in outputs:
        shutil.copy2(stage/name, (ASSETS/name).with_suffix('.json.tmp'))
    for name, _ in outputs:
        os.replace((ASSETS/name).with_suffix('.json.tmp'), ASSETS/name)
(OUT/'geometry-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=True, indent=2), flush=True)
