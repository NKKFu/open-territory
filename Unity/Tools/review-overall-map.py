"""Fit the saved map to its terrain without replanning buildings or streets.

Stage first, then pass --apply. The source export and scene patch share the
same triangles. Steep private boundaries become open gardens; building shells,
interiors, authored public parks and their furniture are retained.
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
from shapely import LineString, Point, box
from building_frontages import rectangle, make_aprons, apron_height
from neighborhood_spaces import access_grade, access_skirt
from street_network import StreetNetwork

ROOT = Path(__file__).resolve().parents[1]
A = ROOT/'OpenTerritory/Assets'
OUT = ROOT/'Reports/overall-map'
FILES = ['Imported/visual.json', 'Resources/world.json', 'Resources/neighborhood-spaces.json',
         'Resources/street-network.json', 'Resources/reference-map.json', 'Resources/building-frontages.json']
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apply', action='store_true')
parser.add_argument('--baseline', type=Path)
parser.add_argument('--terrain-only', action='store_true', help='Refine only terrain in an already reviewed export')
args = parser.parse_args()

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

fingerprints = {name: digest(A/name) for name in FILES}
source = args.baseline or A
visual, world, neighborhood, street, reference, frontages = [json.loads((source/name).read_text(encoding='utf-8-sig')) for name in FILES]
previous_review = source/'Resources/overall-map-review.json'
if not args.baseline and previous_review.exists() and json.loads(previous_review.read_text(encoding='utf-8-sig'))['sourceHash'] == world['sourceHash']:
    raise SystemExit('This export is already reviewed. Use --baseline with the original Assets backup to refine it, or regenerate the original export first.')
g = world['ground']
grid = np.asarray(g['heights']).reshape(g['height'], g['width'])

def original_ground(xy):
    xy = np.asarray(xy, float).reshape(-1, 2)
    f = (xy - [g['x'], g['z']])/g['step']
    f = np.clip(f, [0, 0], [g['width']-1.001, g['height']-1.001])
    ij = f.astype(int); a, b = (f-ij).T; i, j = ij.T
    p, q, r, s = grid[j, i], grid[j, i+1], grid[j+1, i], grid[j+1, i+1]
    return np.where(a+b <= 1, p+a*(q-p)+b*(r-p), s+(1-a)*(r-s)+(1-b)*(q-s))

def original_point(p):
    return float(original_ground([[p[0], p[2]]])[0])

paths = [dict(id=r['id'], kind=r['kind'], width=r['width'], xz=np.array([[p[k] for k in 'xyz'] for p in r['points']]),
              heights=np.array([p['y']-.07 for p in r['points']])) for r in reference['roads']]
network = StreetNetwork(paths, original_point, (g['x'], g['z']), sidewalk_width=street['sidewalkWidth'])
def source_surface(xy, lift=.07):
    xy = np.asarray(xy, float).reshape(-1, 2); base = original_ground(xy); ps = sh.points(xy)
    for road, line in network.highways:
        influence = np.clip(1-np.maximum(0, sh.distance(ps, line)-road['width']/2)/8, 0, 1)
        chosen = influence > 0
        if np.any(chosen):
            q = sh.line_interpolate_point(line, sh.line_locate_point(line, ps[chosen]))
            grade = sh.get_coordinates(q, include_z=True)[:, 2]
            base[chosen] += np.maximum(0, grade-base[chosen])*influence[chosen]
    return base+lift
network.height = source_surface
nodes = {n['name']: n for n in visual['nodes'] if n['geometry'] >= 0}
materials = {m['name']: i for i, m in enumerate(visual['materials'])}

def geometry(node):
    return visual['geometries'][node['geometry']]

def points(node):
    return np.asarray(geometry(node)['positions'], float).reshape(-1, 3)

def triangles(node):
    v = points(node)
    if node['name'] == 'Reused carpark' and node['instances']:
        m = np.asarray(node['instances'][0]['matrix']).reshape(4, 4).T
        v = (np.column_stack((v, np.ones(len(v))))@m.T)[:, :3]
    return v[np.asarray(geometry(node)['indices'], int).reshape(-1, 3)]

def shape(node):
    tri = triangles(node)[:, :, [0, 2]]
    return sh.union_all(sh.polygons(tri))

road_node = max((n for n in visual['nodes'] if n['geometry'] >= 0 and n['materials'] == [materials['Asfalto contínuo']]), key=lambda n: len(geometry(n)['indices']))
asphalt = shape(road_node)
sidewalk = shape(nodes['Calçadas recortadas nos cruzamentos'])
protected = asphalt.union(sidewalk).buffer(.03)
sh.prepare(protected)
lot_shapes = [rectangle([p['x'], p['z']], [p['lotWidth'], p['lotDepth']], p['yaw']) for p in world['places']]
lot_tree = sh.STRtree([p.buffer(8) for p in lot_shapes])
lots = sh.union_all(lot_shapes)
highways = []
for r in paths:
    if r['kind'] != 'highway':
        continue
    # Eleven meters of longitudinal filtering removes short terrain ripples.
    # Cap displacement so intersections and adjoining shoulders stay close.
    xy = r['xz'][:, [0, 2]]
    step = np.median(np.linalg.norm(np.diff(xy, axis=0), axis=1))
    radius = max(2, math.ceil(11/max(.1, step)))
    k = np.arange(-radius, radius+1); kernel = np.exp(-.5*(k/(radius/2))**2); kernel /= kernel.sum()
    old = network.height(xy, 0)
    smooth = np.convolve(np.pad(old, radius, mode='edge'), kernel, mode='valid')
    delta = np.clip(smooth-old, -.45, .45)
    # Fade at joins, retaining the authored grade at each road endpoint.
    delta *= np.minimum(1, np.minimum(np.arange(len(xy)), np.arange(len(xy))[::-1])/max(1, radius))
    highways.append((r, LineString(xy), np.r_[0, np.cumsum(np.linalg.norm(np.diff(xy, axis=0), axis=1))], delta))

def road_delta(xy):
    xy = np.asarray(xy, float).reshape(-1, 2)
    ps = sh.points(xy); best = np.zeros(len(xy)); weight = np.zeros(len(xy))
    for r, line, along, delta in highways:
        distance = sh.distance(ps, line)
        fade = np.clip(1-np.maximum(0, distance-r['width']/2)/3, 0, 1)
        select = fade > weight
        if np.any(select):
            d = sh.line_locate_point(line, ps[select])
            best[select] = np.interp(d, along, delta)*fade[select]
            weight[select] = fade[select]
    return best

def lot_grade(xy, height, lift=0):
    xy = np.asarray(xy, float).reshape(-1, 2); result = np.asarray(height, float).copy()
    if not len(xy):
        return result
    pairs = lot_tree.query(sh.points(xy), predicate='intersects')
    for ident in np.unique(pairs[1]):
        ids = pairs[0, pairs[1] == ident]; p = world['places'][int(ident)]
        c, s = math.cos(p['yaw']), math.sin(p['yaw']); d = xy[ids]-[p['x'], p['z']]
        local = np.column_stack((c*d[:, 0]-s*d[:, 1], s*d[:, 0]+c*d[:, 1]))
        outside = np.maximum(0, abs(local)-[p['lotWidth']/2, p['lotDepth']/2])
        distance = np.linalg.norm(outside, axis=1)
        limit = p['elevation']-.025+distance*.72+lift
        result[ids] = np.minimum(result[ids], limit)
    # Public paving and every existing entrance retain their boundary levels.
    locked = sh.intersects(protected, sh.points(xy))
    result[locked] = np.asarray(height)[locked]
    return result

def terrain(xy):
    xy = np.asarray(xy, float).reshape(-1, 2)
    # The source highway already has an earth embankment. Keep that fill
    # while grading parcels; returning to the raw height grid opens a gap
    # beneath the raised asphalt edge.
    return lot_grade(xy, source_surface(xy, 0))+road_delta(xy)

def ground_point(p):
    return float(terrain([[p[0], p[2]]])[0])

def fitted_surface(xy, lift=.07):
    xy = np.asarray(xy, float).reshape(-1, 2)
    return lot_grade(xy, network.height(xy, 0))+road_delta(xy)+lift

# Physical floors are matched by their actual exported vertices, including
# duplicate collision chunks. Never identify a floor just by its center.
def floor_key(flat):
    return hashlib.sha256(np.round(np.asarray(flat), 5).tobytes()).digest()

print('Indexing source floors and complete parcel footprints', flush=True)
floor_index = {}
for f in visual['floors']:
    floor_index.setdefault(floor_key(f['vertices']), []).append(f)
removed = set(); added = []; changed_materials = set(); floor_keys = set(); changed_nodes = set(); changed_solids = set(); changes = []; nature_moves = []

def physical_key(flat):
    return f"{math.floor(flat[0]/32)},{math.floor(flat[2]/32)}"

def replace(node, mesh, physical=True):
    v, ix, ns = mesh; v = np.round(v, 5); ix = np.asarray(ix, int)
    old = geometry(node); old_flat = triangles(node).ravel().tolist()
    matches = floor_index.get(floor_key(old_flat), []) if physical else []
    if physical and not matches and node['name'] == 'Reused carpark':
        original_tri = triangles(node); lo = original_tri.min(axis=(0, 1)); hi = original_tri.max(axis=(0, 1))
        # Donor collision floors were exported one triangle per record at a
        # 2.5 cm inset, rather than as the rendered slab's complete mesh.
        for f in visual['floors']:
            flat = f['vertices']
            if len(flat) == 9 and all(abs(y-(lo[1]-.05)) < .002 for y in flat[1::3]) and all(lo[0]-3.001 <= x <= hi[0]+3.001 for x in flat[0::3]) and all(lo[2]-4.501 <= z <= hi[2]+4.501 for z in flat[2::3]):
                matches.append(f)
                continue
            if len(flat) != 9 or not lo[0]-.001 <= flat[0] <= hi[0]+.001 or not lo[2]-.001 <= flat[2] <= hi[2]+.001:
                continue
            p = np.asarray(flat).reshape(-1, 3)
            if np.all(p[:, 0] >= lo[0]-.001) and np.all(p[:, 0] <= hi[0]+.001) and np.all(p[:, 2] >= lo[2]-.001) and np.all(p[:, 2] <= hi[2]+.001) and p[:, 1].min() > lo[1]+.055 and p[:, 1].max() < hi[1]+.06:
                matches.append(f)
    if physical and not matches and node['name'] != 'Reused carpark':
        raise RuntimeError('No source floor for '+node['name'])
    for f in matches:
        removed.add(id(f)); floor_keys.add(physical_key(f['vertices']))
    if physical and len(ix):
        flat = v[ix].ravel().tolist(); added.append(dict(vertices=flat)); floor_keys.add(physical_key(flat))
    if node['name'] == 'Reused carpark' and node['instances']:
        m = np.asarray(node['instances'][0]['matrix']).reshape(4, 4).T
        v = np.round((np.column_stack((v, np.ones(len(v))))@np.linalg.inv(m).T)[:, :3], 5)
    visual['geometries'][node['geometry']] = dict(name=old['name'], positions=v.ravel().tolist(),
        indices=ix.tolist(), normals=np.round(ns, 5).ravel().tolist(), uv=[], colors=[], groups=[])
    changed_materials.update(node['materials']); changes.append(node['name'])

def normals(v, ix):
    tri = v[np.asarray(ix).reshape(-1, 3)]
    n = np.cross(tri[:, 1]-tri[:, 0], tri[:, 2]-tri[:, 0]); n /= np.maximum(1e-9, np.linalg.norm(n, axis=1))[:, None]
    return np.repeat(n, 3, axis=0)

def triangle_mesh(poly, height):
    ts = sh.get_parts(sh.constrained_delaunay_triangles(poly)); ts = ts[sh.area(ts) > .000002]
    if not len(ts):
        return np.empty((0, 3)), np.array([], int), np.empty((0, 3))
    xy = sh.get_coordinates(ts).reshape(-1, 4, 2)[:, :3]
    u, v = xy[:, 1]-xy[:, 0], xy[:, 2]-xy[:, 0]
    cross = u[:, 0]*v[:, 1]-u[:, 1]*v[:, 0]
    xy[cross > 0] = xy[cross > 0][:, [0, 2, 1]]
    xy = xy.reshape(-1, 2); v = np.column_stack((xy[:, 0], height(xy), xy[:, 1])); ix = np.arange(len(v))
    return v, ix, normals(v, ix)

def source_triangle_heights(xy, source_tri, tree):
    xy = np.asarray(xy, float).reshape(-1, 2)
    points_2d = sh.points(xy)
    pairs = tree.query(points_2d, predicate='intersects')
    owner = np.full(len(xy), -1, int); owner[pairs[0]] = pairs[1]
    missing = owner < 0
    if missing.any():
        owner[missing] = tree.nearest(points_2d[missing])
    tri = source_tri[owner]; a = tri[:, 0]; b = tri[:, 1]-a; c = tri[:, 2]-a
    d = xy-a[:, [0, 2]]; det = b[:, 0]*c[:, 2]-b[:, 2]*c[:, 0]
    u = (d[:, 0]*c[:, 2]-d[:, 1]*c[:, 0])/det
    v = (b[:, 0]*d[:, 1]-b[:, 2]*d[:, 0])/det
    return a[:, 1]+u*b[:, 1]+v*c[:, 1]

region = lots.buffer(8).union(sh.union_all([network.corridors[r['id']].buffer(3) for r in paths if r['kind'] == 'highway']))
sh.prepare(region)
terrain_nodes = [n for n in visual['nodes'] if n['name'].startswith('Terreno novo ')]
terrain_changed = 0
for n in terrain_nodes:
    tri = triangles(n); polygons = sh.polygons(tri[:, :, [0, 2]])
    touched = sh.intersects(region, polygons)
    if not touched.any():
        continue
    edit = sh.union_all(polygons[touched]).intersection(region)
    if edit.area < .01:
        continue
    retained = sh.difference(polygons[touched], region)
    # Retain each original terrain triangle's internal edges. Unioning these
    # polygons first would interpolate a large new plane over the old relief.
    # A clipped fragment remains on its original triangle plane. Evaluating
    # a continuous height function here moves shared edges of coarse tiles,
    # opening cracks and potentially lifting retained soil over road paint.
    original_tree = sh.STRtree(polygons)
    original_height = lambda xy: source_triangle_heights(xy, tri, original_tree)
    keep_v, _, _ = triangle_mesh(retained, original_height)
    untouched = tri[~touched].reshape(-1, 3)
    edit = edit.difference(lots).difference(asphalt)
    def continuous_terrain(xy):
        old = original_height(xy)
        fade = np.clip(sh.distance(sh.points(xy), region.boundary)/1.5, 0, 1)
        return old+(terrain(xy)-old)*fade
    fitted_v, _, _ = network.mesh(edit, continuous_terrain, 1)
    v = np.concatenate((untouched, keep_v, fitted_v)); ix = np.arange(len(v))
    replace(n, (v, ix, normals(v, ix)))
    terrain_changed += 1
    if terrain_changed % 10 == 0:
        print('Terrain patches staged:', terrain_changed, flush=True)
print('Fitted terrain tiles:', terrain_changed, flush=True)

if args.terrain_only:
    if not args.baseline:
        raise SystemExit('--terrain-only requires the original baseline')
    current_names = ['Imported/visual.json', 'Resources/world.json', 'Imported/overall-map-patch.json', 'Resources/overall-map-review.json']
    current = [json.loads((A/name).read_text(encoding='utf-8-sig')) for name in current_names]
    cv, cw, cp, cr = current
    current_nodes = {n['id']: n for n in cv['nodes']}
    obsolete = set()
    for n in terrain_nodes:
        if n['name'] not in changes:
            continue
        cn = current_nodes[n['id']]; cg = cv['geometries'][cn['geometry']]
        old_v = np.asarray(cg['positions']).reshape(-1, 3)[np.asarray(cg['indices'])].ravel()
        obsolete.add(floor_key(old_v)); floor_keys.add(physical_key(old_v))
        cv['geometries'][cn['geometry']] = geometry(n)
    cv['floors'] = [f for f in cv['floors'] if floor_key(f['vertices']) not in obsolete]+added
    before_hash = cw['sourceHash']
    after_hash = hashlib.sha256(before_hash.encode()+Path(__file__).read_bytes()+b'terrain-seam-refinement').hexdigest()
    cw['sourceHash'] = cp['sourceHash'] = cr['sourceHash'] = after_hash
    cp['floorKeys'] = sorted(set(cp['floorKeys']) | floor_keys)
    cp['materials'] = sorted(set(cp['materials']) | changed_materials)
    backup = OUT/'refinements'/('terrain-'+datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S'))
    assert args.apply, '--terrain-only requires --apply'
    assert all(digest(A/name) == value for name, value in fingerprints.items()), 'Concurrent source edit'
    for name, value in zip(current_names, current):
        destination = backup/name; destination.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(A/name, destination)
        (A/name).with_suffix('.json.tmp').write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    for name in current_names:
        os.replace((A/name).with_suffix('.json.tmp'), A/name)
    (OUT/'terrain-seam-refinement.json').write_text(json.dumps(dict(sourceHash=after_hash, previousSourceHash=before_hash, fittedTerrainTiles=terrain_changed, backup=str(backup)), indent=2), encoding='utf-8')
    print('Terrain seams refined: '+after_hash, flush=True)
    raise SystemExit(0)

# Use the current road polygons, reviewed crossings and widened sidewalks.
# Smoothing changes elevations only; no street is moved or regenerated.
road_v = points(road_node).copy(); road_v[:, 1] += road_delta(road_v[:, [0, 2]])
road_ix = np.asarray(geometry(road_node)['indices'], int)
road_tri = road_v[road_ix.reshape(-1, 3)]
replace(road_node, (road_tri.reshape(-1, 3), np.arange(road_ix.size), normals(road_v, road_ix)))
road_polys = sh.polygons(road_tri[:, :, [0, 2]])
road_tree = sh.STRtree(road_polys)

def project_paint(node, support_tri, support_tree, lift=.014):
    old_tri = triangles(node); paints = sh.polygons(old_tri[:, :, [0, 2]])
    pairs = support_tree.query(paints, predicate='intersects')
    clipped = sh.intersection(paints[pairs[0]], sh.polygons(support_tri[pairs[1]][:, :, [0, 2]]))
    pieces, owners = sh.get_parts(clipped, return_index=True)
    valid = sh.area(pieces) > .000001; pieces = pieces[valid]; owners = pairs[1, owners[valid]]
    pieces = sh.get_parts(sh.constrained_delaunay_triangles(pieces), return_index=True)
    polys, parent = pieces; valid = sh.area(polys) > .000001; polys = polys[valid]; owners = owners[parent[valid]]
    xy = sh.get_coordinates(polys).reshape(-1, 4, 2)[:, :3]
    a = support_tri[owners]; p = a[:, 0][:, [0, 2]]; u = a[:, 1][:, [0, 2]]-p; v = a[:, 2][:, [0, 2]]-p
    d = xy-p[:, None]; det = u[:, 0]*v[:, 1]-u[:, 1]*v[:, 0]
    s = (d[:, :, 0]*v[:, None, 1]-d[:, :, 1]*v[:, None, 0])/det[:, None]
    t = (u[:, None, 0]*d[:, :, 1]-u[:, None, 1]*d[:, :, 0])/det[:, None]
    y = a[:, None, 0, 1]+s*(a[:, None, 1, 1]-a[:, None, 0, 1])+t*(a[:, None, 2, 1]-a[:, None, 0, 1])+lift
    out = np.stack((xy[:, :, 0], y, xy[:, :, 1]), axis=2)
    cross = np.cross(out[:, 1]-out[:, 0], out[:, 2]-out[:, 0])[:, 1]
    out[cross < 0] = out[cross < 0][:, [0, 2, 1]]
    out = out.reshape(-1, 3); ix = np.arange(len(out)); replace(node, (out, ix, normals(out, ix)), False)
    return len(out)//3

paint_triangles = 0
for name in ['Faixas e travessias', 'Eixos interrompidos nos cruzamentos']:
    paint_triangles += project_paint(nodes[name], road_tri, road_tree)
print('Paint triangles fitted to the exact asphalt triangles:', paint_triangles, flush=True)

# Follow the public road adjustment in sidewalk and curb geometry without
# moving a curb horizontally or touching the current crossing layout.
for name in ['Calçadas recortadas nos cruzamentos', 'Meios-fios com rebaixos de travessia', 'Canteiros interrompidos nos acessos', 'Trilhas conectadas']:
    node = nodes[name]; v = points(node).copy(); delta = road_delta(v[:, [0, 2]])
    if np.max(abs(delta), initial=0) < .00001:
        continue
    v[:, 1] += delta; ix = np.asarray(geometry(node)['indices'], int)
    flat = v[ix]; replace(node, (flat, np.arange(len(flat)), normals(flat, np.arange(len(flat)))))

# A private plot on a steep embankment stays open. This is deliberately based
# on the whole garden, including its perimeter, rather than building centers.
spaces = {}; open_spaces = set()
for s in neighborhood['spaces']:
    node = nodes.get('Quintal e jardim '+s['id'])
    if not node:
        continue
    poly = shape(node); spaces[s['id']] = poly
    xy = sh.get_coordinates(sh.segmentize(poly.boundary, 1))
    y = terrain(xy)
    if s['kind'] == 'yard' and (np.ptp(y) > 1.35 or poly.area < 45):
        open_spaces.add(s['id'])
space_ids = list(spaces); space_shapes = [spaces[k] for k in space_ids]; space_tree = sh.STRtree(space_shapes)

new_network = StreetNetwork(paths, ground_point, (g['x'], g['z']), sidewalk_width=street['sidewalkWidth'])
new_network.height = lambda xy, lift=.07: fitted_surface(xy, lift)
reviewed_crossings = []
for crossing in street['crossings']:
    ends = np.array([[p['x'], p['z']] for p in crossing['ends']]); normal = (ends[1]-ends[0])/np.linalg.norm(ends[1]-ends[0])
    road = next(r for r in paths if r['id'] == crossing['road'])
    reviewed_crossings.append(dict(center=np.array([crossing['position']['x'], crossing['position']['z']]), normal=normal,
        tangent=np.array([normal[1], -normal[0]]), width=max(6.4, road['width']), road=road['id']))
new_network.crossings = reviewed_crossings
# Keep the exact public network heights at the thresholds. Only private
# terrain and its small access ramps are regraded.
aprons = make_aprons(world['places'], new_network, ground_point)
apron_tree = sh.STRtree([a['shape'] for a in aprons])
entrance_nodes = sorted((n for n in visual['nodes'] if n['name'].startswith('Entrada pavimentada • ')), key=lambda n: n['id'])
assert len(entrance_nodes) == len(aprons) == len(frontages['entrances'])
for node, apron, entry in zip(entrance_nodes, aprons, frontages['entrances']):
    assert apron['place']['door']['id'] == entry['door']
    def entrance_height(xy, apron=apron):
        return lot_grade(xy, apron_height(apron, new_network, xy), .12)
    # Keep each reviewed forecourt's exact outline and grade it down at the
    # facade; the former max(terrain, ramp) could bury the ground-floor wall.
    replace(node, network.mesh(shape(node), entrance_height, .5))
    for p in entry['samples']+[entry['sidewalk']]:
        p['y'] = round(float(entrance_height([[p['x'], p['z']]])[0]), 5)
private_changes = 0
for s in neighborhood['spaces']:
    ident = s['id']
    for prefix, lift in [('Quintal e jardim ', .055), ('Pátio de convívio ', .075), ('Canteiros ', .083), ('Estacionamento de vizinhança ', .085)]:
        node = nodes.get(prefix+ident)
        if not node:
            continue
        v = points(node)
        target = fitted_surface(v[:, [0, 2]], lift)
        if np.max(abs(target-v[:, 1]), initial=0) < .002:
            continue
        replace(node, network.mesh(shape(node), lambda xy, lift=lift: fitted_surface(xy, lift), 1))
        private_changes += 1
    node = nodes.get('Caminho lateral aberto '+ident)
    if not node:
        continue
    route_shape = shape(node)
    nearby = [aprons[i] for i in apron_tree.query(route_shape.buffer(3.3))]
    primary = aprons[s['lot']] if s['lot'] >= 0 else None
    def access(xy, nearby=nearby, primary=primary):
        return lot_grade(xy, access_grade(xy, nearby, new_network, primary), .12)
    # Preserve unchanged access, including existing corrections at entrances.
    v = points(node)
    if np.max(abs(access(v[:, [0, 2]])-v[:, 1]), initial=0) > .02:
        replace(node, network.mesh(route_shape, access, .5))
        skirt = nodes.get('Base do acesso lateral '+ident)
        if skirt:
            replace(skirt, access_skirt(route_shape, new_network, access))
        xy = np.array([[p['x'], p['z']] for p in s['samples']]); ys = access(xy)
        for p, y in zip(s['samples'], ys):
            p['y'] = round(float(y), 5)
print('Private surfaces and reserved access ramps staged:', private_changes, flush=True)

walls_by_key = {}; kept_walls = []; removed_walls = 0
for wall in neighborhood['walls']:
    center = np.array([wall['center']['x'], wall['center']['z']]); f = np.array([math.sin(wall['yaw']), math.cos(wall['yaw'])])
    ends = np.array([center-f*wall['length']/2, center+f*wall['length']/2]); ys = terrain(ends)
    owners = space_tree.query(Point(center).buffer(.08), predicate='intersects')
    opened = any(space_ids[i] in open_spaces for i in owners)
    rise = float(np.ptp(ys)); original_rise = float(np.ptp(source_surface(ends, 0)))
    height = float(np.clip(wall['height']-original_rise, .70, 1.02))+rise
    key = tuple(np.round(center, 3))
    if opened or rise > .32 or height > 1.35:
        walls_by_key[key] = None; removed_walls += 1
    else:
        changed = dict(wall, center=dict(x=wall['center']['x'], y=round(float(min(ys)+.025), 5), z=wall['center']['z']), height=height)
        walls_by_key[key] = changed; kept_walls.append(changed)

def wall_key(x, z):
    key = (round(x, 3), round(z, 3))
    if key in walls_by_key:
        return key
    # The exported positions have five decimal places; metadata comes from
    # unrounded polygon coordinates near a millimeter rounding boundary.
    for dx in (-.001, 0, .001):
        for dz in (-.001, 0, .001):
            trial = (round(key[0]+dx, 3), round(key[1]+dz, 3))
            if trial in walls_by_key:
                return trial
    return None

for node in [n for n in visual['nodes'] if n['name'].startswith('Jardins e muros')]:
    old = geometry(node); v = points(node).copy(); ix = np.asarray(old['indices'], int); keep = np.ones(len(v), bool)
    assert len(v) % 24 == 0
    for start in range(0, len(v), 24):
        block = v[start:start+24]; center = (block.min(axis=0)+block.max(axis=0))/2; key = wall_key(center[0], center[2])
        wall_material = node['materials'][0] in [materials['Muros de quintal'], materials['Capas dos muros']]
        if key is not None and wall_material:
            w = walls_by_key[key]
            if w is None:
                keep[start:start+24] = False; continue
            low, high = block[:, 1].min(), block[:, 1].max()
            bottom = w['center']['y']+(w['height'] if node['materials'][0] == materials['Capas dos muros'] else 0)
            target_height = .09 if node['materials'][0] == materials['Capas dos muros'] else w['height']
            block[:, 1] = bottom+(block[:, 1]-low)/max(.001, high-low)*target_height
        else:
            delta = float(terrain([[center[0], center[2]]])[0]-source_surface([[center[0], center[2]]], 0)[0])
            block[:, 1] += delta
    selected = keep[ix].reshape(-1, 3).all(axis=1); flat = v[ix.reshape(-1, 3)[selected]].reshape(-1, 3)
    replace(node, (flat, np.arange(len(flat)), normals(flat, np.arange(len(flat)))), False)

for solid in world['solids']:
    key = wall_key(solid['x'], solid['z'])
    if key is not None and abs(solid['w']-.22) < .01 and solid['d'] < 2.7:
        w = walls_by_key[key]
        if w is None:
            solid['active'] = False
        else:
            solid['bottom'] = w['center']['y']; solid['top'] = w['center']['y']+w['height']; solid['vault'] = True
        changed_solids.add(solid['index'])
neighborhood['walls'] = kept_walls
print('Retained low walls:', len(kept_walls), 'removed on slopes:', removed_walls, flush=True)
for s in neighborhood['spaces']:
    if s['kind'] == 'yard':
        s['boundary'] = 'open slope' if s['id'] in open_spaces else 'low garden wall'

# The donor parking plane and paint were flat at the donor's old height.
# Bake them into world space and drape the apron over the actual landscape.
car_nodes = [n for n in visual['nodes'] if n['name'] == 'Reused carpark']
parking_tri = None
for n in car_nodes:
    if n['materials'] != [24]:
        continue
    source_v = points(n); m = np.asarray(n['instances'][0]['matrix']).reshape(4, 4).T
    v = (np.column_stack((source_v, np.ones(len(source_v))))@m.T)[:, :3]
    ix = np.asarray(geometry(n)['indices'], int).reshape(-1, 3)
    tri = v[ix]; up = np.cross(tri[:, 1]-tri[:, 0], tri[:, 2]-tri[:, 0])[:, 1] > .0001
    footprint = sh.union_all(sh.polygons(tri[up][:, :, [0, 2]]))
    mesh = network.mesh(footprint, lambda xy: fitted_surface(xy, .065), 1)
    replace(n, mesh); parking_tri = mesh[0][np.asarray(mesh[1]).reshape(-1, 3)]
    break
assert parking_tri is not None
parking_tree = sh.STRtree(sh.polygons(parking_tri[:, :, [0, 2]]))
for n in car_nodes:
    if n['materials'] == [27]:
        project_paint(n, parking_tri, parking_tree)
    elif n['materials'] == [795]:
        old_bases = [inst['matrix'][13]-abs(inst['matrix'][5])*.5 for inst in n['instances']]
        bases = [float(fitted_surface([[inst['matrix'][12], inst['matrix'][14]]], .065)[0])-.02 for inst in n['instances']]
        shift = max(bases)-max(old_bases)
        billboard = next(a for a in car_nodes if a['materials'] == [12])
        for inst in billboard['instances']:
            inst['matrix'][13] += shift
        changed_materials.update([12, 795])
        for inst, base in zip(n['instances'], bases):
            m = inst['matrix']; x, z = m[12], m[14]
            top = m[13]+abs(m[5])*.5+shift
            m[5] = top-base; m[13] = (top+base)*.5
            for s in world['solids']:
                if abs(s['x']-x) < .01 and abs(s['z']-z) < .01 and max(s['w'], s['d']) < .3:
                    s['bottom'] = base; s['top'] = top; changed_solids.add(s['index'])

# Original neighborhood parking markings need the same triangle agreement.
small_parking = [n for n in visual['nodes'] if n['name'].startswith('Estacionamento de vizinhança ')]
small_tri = np.concatenate([triangles(n) for n in small_parking])
project_paint(nodes['Marcações dos pequenos estacionamentos'], small_tri, sh.STRtree(sh.polygons(small_tri[:, :, [0, 2]])))

# The donor's old collision apron extends beyond its visible rectangle.
# Retire that entire flat grid, including the outermost parking bay corners.
donor = next(n for n in car_nodes if n['materials'] == [24])
frame = donor['instances'][0]['matrix']; bounds = points(donor)
x0, x1 = bounds[:, 0].min()+frame[12]-3, bounds[:, 0].max()+frame[12]+3
z0, z1 = bounds[:, 2].min()+frame[14]-4.5, bounds[:, 2].max()+frame[14]+4.5
for floor in visual['floors']:
    flat = floor['vertices']
    if len(flat) == 9 and all(abs(y-(frame[13]-.05)) < .002 for y in flat[1::3]) and all(x0-.001 <= x <= x1+.001 for x in flat[0::3]) and all(z0-.001 <= z <= z1+.001 for z in flat[2::3]):
        removed.add(id(floor)); floor_keys.add(physical_key(flat))
visual['floors'] = [f for f in visual['floors'] if id(f) not in removed]+added
node_lookup = {n['id']: n for n in visual['nodes']}
def root_id(ident):
    if ident not in node_lookup:
        return ident
    while node_lookup[ident]['parent'] >= 0:
        ident = node_lookup[ident]['parent']
    return ident

# Move complete interactive roots, keeping their use positions and collisions
# together. The existing prop-footing baker then refits each bottom perimeter.
for prop in world['destructibles']:
    node = node_lookup.get(prop['id'])
    if prop['type'] not in ['bench', 'bin', 'planter', 'lamp'] or not node or node['parent'] >= 0:
        continue
    p = prop['position']; xy = [[p['x'], p['z']]]
    if any(poly.contains(Point(p['x'], p['z'])) for poly in lot_shapes):
        continue
    delta = float(terrain(xy)[0]-source_surface(xy, 0)[0])
    if abs(delta) < .015:
        continue
    node['position']['y'] += delta; p['y'] += delta; changed_nodes.add(node['id'])
    for solid in world['solids']:
        if solid['owner'] and root_id(solid['owner']) == node['id']:
            solid['bottom'] += delta; solid['top'] += delta; changed_solids.add(solid['index'])
    for seat in world['seats']:
        if root_id(seat['id']) == node['id']:
            seat['position']['y'] += delta
    for key in ['streetLamps', 'devices']:
        for entry in world[key]:
            if root_id(entry['node']) == node['id']:
                entry['position']['y'] += delta
                if 'lookAt' in entry:
                    entry['lookAt']['y'] += delta

for category, entries in [('tree', world['natureTrees']), ('rock', world['rocks'])]:
    for entry in entries:
        p = entry['position']; xy = [[p['x'], p['z']]]
        delta = float(terrain(xy)[0]-source_surface(xy, 0)[0])
        if abs(delta) < .015:
            continue
        old = p.copy(); p['y'] += delta
        nature_moves.append(dict(category=category, before=old, after=p.copy()))
        solid = min((s for s in world['solids'] if s['index'] == entry['solidIndex']), key=lambda s: (s['x']-p['x'])**2+(s['z']-p['z'])**2)
        solid['bottom'] += delta; solid['top'] += delta; changed_solids.add(solid['index'])

# The few retained plaza trees are static instances rather than streamed LOD
# trees. Shift every part of one tree by its trunk's single vertical offset.
trunks = [s for s in world['solids'] if abs(s['w']-.77) < .002 and abs(s['d']-.77) < .002 and abs(s['top']-s['bottom']-4.4) < .03]
trunk_points = np.array([[s['x'], s['z']] for s in trunks])
trunk_deltas = terrain(trunk_points)-source_surface(trunk_points, 0)
for n in visual['nodes']:
    if n['name'] != 'Árvore reaproveitada da praça':
        continue
    for inst in n['instances']:
        m = inst['matrix']; distance = np.linalg.norm(trunk_points-[m[12], m[14]], axis=1); ident = int(distance.argmin())
        if distance[ident] < 5.5 and abs(trunk_deltas[ident]) > .015:
            m[13] += float(trunk_deltas[ident]); changed_materials.update(n['materials'])
for solid in world['solids']:
    if not (abs(solid['w']-.77) < .002 and abs(solid['d']-.77) < .002 or abs(solid['w']-4.84) < .02 and abs(solid['d']-4.62) < .02):
        continue
    distances = np.linalg.norm(trunk_points-[solid['x'], solid['z']], axis=1); ident = int(distances.argmin())
    if distances[ident] < .1 and abs(trunk_deltas[ident]) > .015:
        delta = float(trunk_deltas[ident]); solid['bottom'] += delta; solid['top'] += delta; changed_solids.add(solid['index'])

fixture_plan = A/'Imported/overall-map-fixtures.json'
if fixture_plan.exists():
    from map_fixture_moves import apply_fixture_moves
    fixture_nodes, fixture_solids = apply_fixture_moves(visual, world, json.loads(fixture_plan.read_text(encoding='utf-8-sig')))
    changed_nodes.update(fixture_nodes); changed_solids.update(fixture_solids)

previous_hash = world['sourceHash']
world['sourceHash'] = hashlib.sha256(previous_hash.encode()+Path(__file__).read_bytes()).hexdigest()
patch = dict(revision=1, sourceHash=world['sourceHash'], previousSourceHash=previous_hash, nodes=sorted(changed_nodes),
             solids=sorted(changed_solids), materials=sorted(changed_materials), floorKeys=sorted(floor_keys), newNodes=[], batchKeys=[], natureMoves=nature_moves)
# Donor materials are shared with retained interiors. Rebuild only the exact
# original parking batches, never every object using one of those materials.
donor_materials = {12, 24, 27, 795}
patch['materials'] = [i for i in patch['materials'] if i not in donor_materials]
patch['batchKeys'] = sorted({f"s-{'surface' if visual['materials'][n['materials'][0]].get('surface', 0) or visual['materials'][n['materials'][0]].get('sea', 0) else 'scenery'}:{math.floor(inst['matrix'][12]/24)}:{math.floor(inst['matrix'][14]/24)}-{n['materials'][0]}"
    for n in car_nodes for inst in n['instances']})

# Capture dense, reproducible acceptance samples from actual generated meshes.
paint_samples = []
for name in ['Faixas e travessias', 'Eixos interrompidos nos cruzamentos']:
    tri = triangles(nodes[name]); centers = tri.mean(axis=1)
    stride = max(1, len(centers)//12000)
    paint_samples.extend(dict(zip('xyz', map(float, p))) for p in centers[::stride])
boundary_samples = []
for i, p in enumerate(world['places']):
    ring = sh.get_coordinates(sh.segmentize(lot_shapes[i].buffer(.15, join_style='mitre').exterior, .75))
    boundary_samples.extend(dict(place=i, x=float(q[0]), z=float(q[1]), floor=p['elevation']) for q in ring)
review = dict(revision=1, sourceHash=world['sourceHash'], paintSamples=paint_samples, buildingSamples=boundary_samples,
              openYards=sorted(open_spaces), parkingSamples=[dict(zip('xyz', map(float, p))) for p in parking_tri.mean(axis=1)[::3]])

report = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), passed=True, buildings=len(world['places']),
              fittedTerrainTiles=terrain_changed, fittedPrivateSurfaces=private_changes, openSlopeYards=len(open_spaces),
              removedExcessiveWallSections=removed_walls, retainedLowWallSections=len(kept_walls), maximumWallHeight=max(w['height'] for w in kept_walls),
              projectedRoadPaintTriangles=paint_triangles, changedMaterials=sorted(changed_materials), changedSolids=len(changed_solids),
              changedGeometry=changes, sourceHash=world['sourceHash'])
outputs = [('Imported/visual.json', visual), ('Resources/world.json', world), ('Resources/neighborhood-spaces.json', neighborhood),
           ('Imported/overall-map-patch.json', patch), ('Resources/overall-map-review.json', review), ('Resources/building-frontages.json', frontages)]
stage = OUT/'staged'
for name, value in outputs:
    path = stage/name; path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
if args.apply:
    assert all(digest(A/name) == value for name, value in fingerprints.items()), 'Concurrent source edit; rebuild the patch'
    backup = OUT/'backups'/datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
    for name in FILES+['Scenes/BaiaDoSol.unity']:
        path = backup/name; path.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(A/name, path)
    report['backup'] = str(backup)
    for name, _ in outputs:
        shutil.copy2(stage/name, (A/name).with_suffix('.json.tmp'))
    for name, _ in outputs:
        os.replace((A/name).with_suffix('.json.tmp'), A/name)
OUT.mkdir(parents=True, exist_ok=True)
(OUT/'geometry-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k != 'changedGeometry'}, ensure_ascii=True, indent=2), flush=True)
