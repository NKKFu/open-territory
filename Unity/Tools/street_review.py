"""Offline review of street joins, crossings and reused street furniture.

Edits the current export in place; it never replans a parcel or removes scenery.
The small patch manifest lets the Editor update only the affected meshes/props.
"""
import copy
import hashlib
import math
from pathlib import Path
from functools import lru_cache
import numpy as np
import shapely as sh
from shapely import Point, Polygon, LineString
from shapely.ops import nearest_points
from street_network import StreetNetwork, width, clean
from map_prop_repair import footprint


def vector(p):
    return {k: round(float(v), 5) for k, v in zip('xyz', p)}


def xyz(p):
    return np.array([p.get(k, 0) for k in 'xyz'], float)


def rotation(yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rectangle(center, forward, half_width, rear, front):
    right = np.array([forward[1], -forward[0]])
    return Polygon([center+right*x+forward*z for x, z in
                    [(-half_width, -rear), (half_width, -rear),
                     (half_width, front), (-half_width, front)]])


def review_crossings(network, clear):
    """Move a zebra off a warped terrain section, or remove a redundant one.

    Endpoints alone do not describe a traversable cross-section on graded lots:
    inspect the whole width and preserve a short, continuous approach at both ends.
    """
    changes = []
    safe_sidewalk = network.sidewalk.buffer(.05)
    sh.prepare(safe_sidewalk)
    paths = {r['id']: r for r in network.streets}
    others = {r['id']: sh.union_all([network.corridors[b['id']] for b in network.drivable if b['id'] != r['id']]).buffer(.6) for r in network.streets}
    def error(c):
        across = np.linspace(-c['width']/2-1, c['width']/2+1, 25)
        xy = c['center']+across[:, None]*c['normal']
        lift = np.where(abs(across) <= c['width']/2, .07, .21-.135*(1-(abs(across)-c['width']/2)/1.7))
        y = network.height(xy, 0)+lift
        return float(max(abs(y-np.linspace(y[0], y[-1], len(y)))))
    keep = list(network.crossings)
    for crossing in list(keep):
        defect = error(crossing)
        if defect <= .20 or crossing.get('controlled'):
            continue
        line = network.lines[crossing['road']]
        at = line.project(Point(crossing['center']))
        found = None
        for distance in [s*d for d in range(4, 45, 4) for s in (-1, 1)]:
            d = at+distance
            if not 5 < d < line.length-5:
                continue
            center = np.array(line.interpolate(d).coords[0])
            tangent = np.array(line.interpolate(d+.5).coords[0])-np.array(line.interpolate(d-.5).coords[0])
            tangent /= np.linalg.norm(tangent)
            normal = np.array([-tangent[1], tangent[0]])
            candidate = dict(center=center, tangent=tangent, normal=normal, width=width(paths[crossing['road']]), road=crossing['road'])
            band = rectangle(center, tangent, candidate['width']/2+1.4, 1.6, 1.6)
            if band.intersection(others[crossing['road']]).area > .03:
                continue
            if any(np.linalg.norm(center-other['center']) < 10 for other in keep if other is not crossing):
                continue
            ends = [Point(center+normal*s*(candidate['width']/2+1)) for s in (-1, 1)]
            if not all(safe_sidewalk.covers(end) for end in ends):
                continue
            if error(candidate) > .14 or not clear(band, network.height([center])[0]):
                continue
            found = candidate
            break
        old = vector([crossing['center'][0], 0, crossing['center'][1]])
        if found:
            crossing.update(found)
            changes.append(dict(road=crossing['road'], before=old, after=vector([found['center'][0], 0, found['center'][1]]), defect=round(defect, 3)))
        else:
            keep = [other for other in keep if other is not crossing]
            changes.append(dict(road=crossing['road'], before=old, removed=True, defect=round(defect, 3)))
    network.crossings = keep
    return changes


def review(visual, world, reference, street, floor_audit=None):
    nodes = {n['id']: n for n in visual['nodes']}
    children = {}
    for n in visual['nodes']:
        children.setdefault(n['parent'], []).append(n)
    changed_nodes, changed_solids, affected_materials, floor_keys = set(), set(), set(), set()
    before_hash = world['sourceHash']
    new_nodes, batch_keys = [], set()
    template_parts = copy.deepcopy(children[2162])
    # Editor-authored fixtures allocate IDs after the visual export. Keep this
    # namespace clear of those existing records; the Editor also checks parents.
    next_id = max(1000000, max(nodes)+1)
    plaza_roots = [2300, 2315, 2330, 2345]
    # Four plaza benches were baked into static batches; two other seat records
    # had no visible furniture. Restore ownership before adjusting their feet.
    for ident in [2300, 2315, 2330, 2345, 2409, 2442]:
        if ident in children:
            continue
        if ident in plaza_roots:
            suffix = 'bench-'+str(plaza_roots.index(ident))
            for n in visual['nodes']:
                if not n['name'].endswith(suffix) or n['dynamic'] or n['geometry'] < 0:
                    continue
                n['visible'] = False
                changed_nodes.add(n['id'])
                for instance in n['instances']:
                    m = np.array(instance['matrix']).reshape(4, 4, order='F')
                    for material in n['materials']:
                        category = 'surface' if visual['materials'][material].get('surface', 0) or visual['materials'][material].get('sea', 0) else 'scenery'
                        batch_keys.add('s-'+category+':'+str(math.floor(m[0, 3]/24))+':'+str(math.floor(m[2, 3]/24))+'-'+str(material))
        nodes[ident]['dynamic'] = True
        children[ident] = []
        for template in template_parts:
            n = copy.deepcopy(template)
            n.update(id=next_id, parent=ident)
            next_id += 1
            nodes[n['id']] = n
            visual['nodes'].append(n)
            children[ident].append(n)
            changed_nodes.add(n['id'])
            new_nodes.append(dict(id=n['id'], template=template['id']))
    print('Reviewing road joins and signal approaches', flush=True)
    g = world['ground']
    grid = np.array(g['heights']).reshape(g['height'], g['width'])

    def ground(p):
        fx = np.clip((p[0]-g['x'])/g['step'], 0, g['width']-1.001)
        fz = np.clip((p[2]-g['z'])/g['step'], 0, g['height']-1.001)
        i, j = int(fx), int(fz)
        a, b = fx-i, fz-j
        return float(grid[j, i]+a*(grid[j, i+1]-grid[j, i])+b*(grid[j+1, i]-grid[j, i])) if a+b <= 1 else float(grid[j+1, i+1]+(1-a)*(grid[j+1, i]-grid[j+1, i+1])+(1-b)*(grid[j, i+1]-grid[j+1, i+1]))

    paths = [dict(id=r['id'], kind=r['kind'], width=r['width'],
                  xz=np.array([xyz(p) for p in r['points']]),
                  heights=np.array([p['y']-.07 for p in r['points']])) for r in reference['roads']]
    network = StreetNetwork(paths, ground, (g['x'], g['z']))
    old_asphalt = network.asphalt
    solids = world['solids']
    active = [s for s in solids if s.get('active', True)]
    shapes = [footprint(s) for s in active]
    tree = sh.STRtree(shapes)
    lots = []
    for p in world['places']:
        lots.append(rectangle(np.array([p['x'], p['z']]), np.array([math.sin(p['yaw']), math.cos(p['yaw'])]),
                              p.get('lotWidth', p['w'])/2, p.get('lotDepth', p['d'])/2, p.get('lotDepth', p['d'])/2))
    # The cemetery is authored in the Editor, outside the original world export.
    lots.append(Polygon([(94, 374), (158, 374), (158, 451), (94, 451)]))
    protected = sh.union_all(lots).buffer(.3)
    sh.prepare(protected)
    report = dict(roadJoins=[], signals=[], benches=[], beforeSignals=len(world['signals']),
                  beforeSignalDevices=sum(d['type'] == 'signal' for d in world['devices']),
                  beforeCrossings=len(street['crossings']))

    # These two street ends stopped short of their intended carriageway.
    # Limit the correction to the existing open verge and refuse any lot overlap.
    for name, end, target in [('bay-west-descent', -1, '101-south'),
                              ('east-ramp-south', 0, 'waterfront')]:
        road = next(r for r in paths if r['id'] == name)
        other = network.lines[target]
        p = road['xz'][end]
        q = np.array(nearest_points(Point(p[[0, 2]]), other)[1].coords[0])
        extension = LineString([p[[0, 2]], q]).buffer(width(road)/2, cap_style='round')
        added = extension.difference(old_asphalt)
        if added.intersects(protected):
            raise RuntimeError('Road join overlaps an existing lot: '+name)
        conflicts = [active[i]['index'] for i in tree.query(added.buffer(.2), predicate='intersects')
                     if active[i]['owner'] == 0 and active[i]['top'] > ground(p)+.35]
        if conflicts:
            raise RuntimeError('Road join blocked by authored scenery: '+str(conflicts))
        q3 = np.array([q[0], ground(np.array([q[0], 0, q[1]])), q[1]])
        road['xz'] = np.vstack([q3, road['xz']]) if end == 0 else np.vstack([road['xz'], q3])
        road['heights'] = np.array([ground(p) for p in road['xz']])
        report['roadJoins'].append(dict(road=name, target=target, length=round(float(np.linalg.norm(q-p[[0, 2]])), 3)))
    network = StreetNetwork(paths, ground, (g['x'], g['z']))

    @lru_cache(maxsize=None)
    def root(ident):
        while ident in nodes and nodes[ident]['parent'] >= 0:
            ident = nodes[ident]['parent']
        return ident

    signal_roots = list(dict.fromkeys(root(d['node']) for d in world['devices'] if d['type'] == 'signal'))
    signal_root_set = set(signal_roots)
    original_benches = {s['id'] for s in world['seats'] if s['seatTop'] == .82 and s['id'] < 4000 and s['id'] not in plaza_roots}
    movable = signal_root_set | original_benches
    reserved = []

    def clear(shape, y, ignore=(), margin=.15):
        if protected.intersects(shape):
            return False
        expanded, ignored = shape.buffer(margin), set(ignore)
        for i in tree.query(expanded, predicate='intersects'):
            s = active[i]
            if root(s['owner']) in ignored:
                continue
            if s['bottom'] < y+2 and s['top'] > y+.18:
                return False
        return not any(expanded.intersects(p) for p in reserved)

    def move(ident, position, yaw):
        node = nodes[ident]
        old = xyz(node['position'])
        old_yaw = 2*math.atan2(node['rotation']['y'], node['rotation']['w'])
        r = rotation(yaw-old_yaw)
        position = np.array(position, float)
        node['position'] = vector(position)
        node['rotation'] = dict(x=0, y=math.sin(yaw/2), z=0, w=math.cos(yaw/2))
        changed_nodes.add(ident)
        for s in solids:
            if root(s['owner']) != ident:
                continue
            p = r@(np.array([s['x'], s['bottom'], s['z']])-old)+position
            height = s['top']-s['bottom']
            s.update(x=float(p[0]), z=float(p[2]), bottom=float(p[1]), top=float(p[1]+height), yaw=s.get('yaw', 0)+yaw-old_yaw)
            changed_solids.add(s['index'])
        for key, field in [('destructibles', 'id'), ('devices', 'node'), ('streetLamps', 'node')]:
            for entry in world[key]:
                if root(entry[field]) != ident:
                    continue
                entry['position'] = vector(r@(xyz(entry['position'])-old)+position)
                if key == 'devices':
                    entry['lookAt'] = copy.deepcopy(entry['position'])
                    entry['yaw'] = yaw
        for person in world['residents']:
            if person['sitting'] and np.linalg.norm((xyz(person['position'])-old)[[0, 2]]) < 1.7:
                person['position'] = vector(r@(xyz(person['position'])-old)+position)
                pn = nodes[person['id']]
                pn['position'] = copy.deepcopy(person['position'])
                py = 2*math.atan2(pn['rotation']['y'], pn['rotation']['w'])+yaw-old_yaw
                pn['rotation'] = dict(x=0, y=math.sin(py/2), z=0, w=math.cos(py/2))
                changed_nodes.add(person['id'])

    crossings = network.crossings
    safe_sidewalk = network.sidewalk.buffer(.05)
    sh.prepare(safe_sidewalk)
    signal_others = {r['id']: sh.union_all([network.corridors[b['id']] for b in network.drivable if b['id'] != r['id']]).buffer(.6) for r in network.drivable}
    stop_bars = []
    used_roots, devices = set(), []
    new_signals = []
    for index, old_signal in enumerate(world['signals']):
        center = np.array([old_signal['x'], old_signal['z']])
        junction = min(network.junctions, key=lambda j: j['point'].distance(Point(center)))
        center = np.array(junction['point'].coords[0])
        arms = []
        for road in network.drivable:
            if road['kind'] == 'highway' or road['id'] not in junction['roads']:
                continue
            line = network.lines[road['id']]
            at = line.project(Point(center))
            for sign in (-1, 1):
                if not 5 < at+sign*12 < line.length-5:
                    continue
                near = np.array(line.interpolate(at+sign*12).coords[0])
                outward = (near-center)/np.linalg.norm(near-center)
                # Opposite branches of one road share a phase; other branches wait.
                axis = next((a['axis'] for a in arms if a['road'] == road['id']), len({a['axis'] for a in arms}))
                found = None
                for distance in np.arange(8, 58, 2):
                    d = at+sign*distance
                    if not 4 < d < line.length-4:
                        continue
                    c = np.array(line.interpolate(d).coords[0])
                    f = np.array(line.interpolate(d+sign*.5).coords[0])-np.array(line.interpolate(d-sign*.5).coords[0])
                    f /= np.linalg.norm(f)
                    right = np.array([-f[1], f[0]])  # Right of the inbound lane.
                    band = rectangle(c, f, width(road)/2+.08, 1.5, 1.5)
                    if band.intersection(signal_others[road['id']]).area > .03:
                        continue
                    if road['kind'] == 'street' and not all(safe_sidewalk.covers(Point(c+right*s*(width(road)/2+1))) for s in (-1, 1)):
                        continue
                    stop = np.array(line.interpolate(d+sign*3.1).coords[0])
                    pole = stop+right*(width(road)/2+.38)-f*.8
                    base = network.sidewalk_height([pole])[0] if road['kind'] == 'street' else network.height([pole], .09)[0]
                    if not clear(Point(pole).buffer(.32), base, movable):
                        continue
                    found = c, f, right, stop, pole, base
                    break
                if found is None:
                    raise RuntimeError('No safe signal approach: '+str(index)+' '+road['id']+' '+str(sign))
                c, f, right, stop, pole, base = found
                ident = next(ident for ident in signal_roots if ident not in used_roots)
                used_roots.add(ident)
                yaw = math.atan2(-f[0], -f[1])
                move(ident, [pole[0], base, pole[1]], yaw)
                nodes[ident]['visible'] = True
                nodes[ident]['name'] = 'Semáforo · '+road['id']+' · '+str(sign)
                # One readable face per incoming lane; no green facing away from it.
                lenses = []
                for n in children[ident]:
                    if n['position']['z'] > .27 and n['position']['y'] > 4:
                        n['visible'] = False
                        changed_nodes.add(n['id'])
                    if n['position']['z'] < -.27 and n['position']['y'] in (5.37, 4.76, 4.15):
                        n['visible'] = True
                        lenses.append(n['id'])
                if len(lenses) != 3:
                    raise RuntimeError('Signal asset does not have three front lenses')
                if road['kind'] == 'street':
                    crossings[:] = [x for x in crossings if not (x['road'] == road['id'] and np.linalg.norm(x['center']-c) < 7)]
                    crossings.append(dict(center=c, tangent=f, normal=np.array([-f[1], f[0]]), width=width(road), road=road['id'], controlled=True))
                stop_bars.append(rectangle(stop+right*width(road)/4, f, width(road)/4-.18, .18, .18))
                approach = dict(road=road['id'], axis=axis, node=ident, width=width(road),
                                stop=vector([stop[0], network.height([stop])[0], stop[1]]),
                                heading=vector([-f[0], 0, -f[1]]), pole=vector([pole[0], base, pole[1]]), lamps=lenses)
                arms.append(approach)
                devices.append(dict(id='street-signal-'+str(ident), type='signal', label='SEMÁFORO', node=ident, target=index,
                                    position=vector([pole[0], base+4.75, pole[1]]), lookAt=vector([pole[0], base+4.75, pole[1]]), yaw=yaw, pitch=0, range=32))
                reserved.append(Point(pole).buffer(.32))
        if len(arms) < 3:
            raise RuntimeError('Signal placed outside a real junction')
        phase_count = len({a['axis'] for a in arms})
        new_signals.append(dict(x=float(center[0]), z=float(center[1]), phase=old_signal['phase'], phaseCount=phase_count,
                                approaches=[{k: v for k, v in a.items() if k not in ['lamps', 'pole']} for a in arms],
                                lamps=[l for a in arms for l in a['lamps']], lampAxes=[a['axis'] for a in arms for _ in range(3)],
                                lampColors=[c for a in arms for c in range(3)]))
        report['signals'].append(dict(index=index, center=vector([center[0], ground(np.array([center[0], 0, center[1]])), center[1]]), approaches=arms))
    for ident in signal_roots:
        if ident not in used_roots:
            nodes[ident]['visible'] = False
            changed_nodes.add(ident)
        for s in solids:
            if root(s['owner']) == ident:
                s['active'] = ident in used_roots
                changed_solids.add(s['index'])
    world['signals'] = new_signals
    world['devices'] = [d for d in world['devices'] if d['type'] != 'signal']+devices
    world['destructibles'] = [d for d in world['destructibles'] if d['type'] != 'signal' or d['id'] in used_roots]

    # Reused plaza benches had been scattered by a generic collision repair.
    # Give them genuine roadside positions, leaving the full sidewalk open.
    print('Reviewing bench positions and feet', flush=True)
    bench_road_area = network.asphalt.buffer(.5)
    sh.prepare(bench_road_area)
    audit = {b['id']: b for b in floor_audit or []}
    for bench in world['seats']:
        if bench['seatTop'] != .82 or bench['id'] not in nodes:
            continue
        ident = bench['id']
        old_position = xyz(nodes[ident]['position'])
        old_yaw = bench['yaw']
        position, yaw = old_position.copy(), old_yaw
        if ident in original_benches:
            point = Point(old_position[[0, 2]])
            nearest = sorted(network.streets, key=lambda r: network.lines[r['id']].distance(point))[:5]
            found = None
            for road in nearest:
                line = network.lines[road['id']]
                at = line.project(point)
                for offset in [0]+[s*d for d in range(4, 121, 4) for s in (-1, 1)]:
                    d = at+offset
                    if not 12 < d < line.length-12:
                        continue
                    c = np.array(line.interpolate(d).coords[0])
                    f = np.array(line.interpolate(d+.5).coords[0])-np.array(line.interpolate(d-.5).coords[0])
                    f /= np.linalg.norm(f)
                    n = np.array([-f[1], f[0]])
                    for side in (-1, 1):
                        outward = n*side
                        p = c+outward*(width(road)/2+3.25)
                        front = -outward
                        pad = rectangle(p, front, 1.8, .8, 1.75)
                        y = ground(np.array([p[0], 0, p[1]]))+.04
                        if y < .8 or bench_road_area.intersects(pad):
                            continue
                        if any(j['point'].distance(Point(p)) < 17 for j in network.junctions):
                            continue
                        if any(Point(x['center']).distance(pad) < 4 for x in crossings):
                            continue
                        heights = [ground(np.array([x, 0, z])) for x, z in pad.exterior.coords]
                        if max(heights)-min(heights) > .22 or not clear(pad, y, movable):
                            continue
                        # A continuous, unobstructed approach reaches the sidewalk.
                        end = c+outward*(width(road)/2+1.6)
                        link = LineString([p+front*.8, end]).buffer(.6, cap_style='flat')
                        if not clear(link, y, movable):
                            continue
                        found = np.array([p[0], max(heights)+.025, p[1]]), math.atan2(front[0], front[1]), pad
                        break
                    if found:
                        break
                if found:
                    break
            if not found:
                raise RuntimeError('No safe public position for bench '+str(ident))
            position, yaw, pad = found
            reserved.append(pad)
        else:
            samples = []
            for x in (-1.18, 1.18):
                for z in (-.39, .39):
                    p = old_position+rotation(yaw)@np.array([x, 0, z])
                    samples.append(ground(p))
            floor = audit.get(ident, {}).get('maxFloor', max(samples))
            position[1] = max(position[1], floor+.015)
        move(ident, position, yaw)
        bench.update(x=float(position[0]), z=float(position[2]), yaw=float(yaw), position=vector(position))
        # Extend the existing feet down to the local grade, keeping the seat level.
        # These remain part of the destructible bench, not permanent world blocks.
        leg_count = 0
        for n in children[ident]:
            if abs(n['position']['y']-.38) > .001 or abs(n['scale']['y']-.76) > .001:
                continue
            local = xyz(n['position'])
            depths = []
            for z in (-.375, .375):
                p = position+rotation(yaw)@np.array([local[0], 0, z])
                depths.append(ground(p))
            base = min(depths)-.025
            extra = max(0, position[1]-base)
            n['scale']['y'] = .76+extra
            n['position']['y'] = .38-extra/2
            changed_nodes.add(n['id'])
            leg_count += 1
        report['benches'].append(dict(id=ident, moved=ident in original_benches,
                                      fromPosition=vector(old_position), position=vector(position),
                                      yaw=float(yaw), feet=leg_count))

    # Keep one clear zebra per arm. Clear lane dashes and stop bars from zebras.
    print('Reviewing full-width crossing profiles', flush=True)
    report['crossingGradeRepairs'] = review_crossings(network, lambda shape, y: clear(shape, y, movable))
    print('Crossing grade repairs:', len(report['crossingGradeRepairs']), flush=True)
    crossings = network.crossings
    bands, kept, white = [], [], list(stop_bars)
    for crossing in sorted(crossings, key=lambda c: not c.get('controlled', False)):
        c, t, n, w = crossing['center'], crossing['tangent'], crossing['normal'], crossing['width']
        band = rectangle(c, t, w/2+.08, 1.45, 1.45)
        expanded_band = band.buffer(.5)
        if any(expanded_band.intersects(b) for b in bands):
            continue
        bands.append(band)
        kept.append(crossing)
        for z in np.arange(-w/2+.3, w/2-.55, .85):
            white.append(Polygon([c+t*x+n*y for x, y in [(-1.35, z), (1.35, z), (1.35, z+.43), (-1.35, z+.43)]]))
    network.crossings = kept
    network.paint = clean(sh.union_all(white).intersection(network.asphalt))
    network.yellow_paint = clean(network.yellow_paint.difference(sh.union_all(bands).buffer(.8)).difference(sh.union_all(stop_bars).buffer(.8)))

    # Patch only named surface meshes. Unrelated geometry stays byte-for-byte
    # identical inside the export, including every building and its grounds.
    by_name = {n['name']: n for n in visual['nodes']}
    floors = visual['floors']
    def floor_key(flat):
        return str(math.floor(flat[0]/32))+','+str(math.floor(flat[2]/32))

    def replace(name, mesh, physical):
        vertices, indices, normals = mesh
        node = by_name[name]
        previous = visual['geometries'][node['geometry']]
        if physical:
            old = np.array(previous['positions']).reshape(-1, 3)[np.array(previous['indices'])].ravel().tolist()
            matches = [f for f in floors if len(f['vertices']) == len(old) and f['vertices'][:12] == old[:12] and f['vertices'] == old]
            if not matches:
                raise RuntimeError('Original physical surface not found: '+name)
            floor_keys.add(floor_key(old))
            floors[:] = [f for f in floors if f not in matches]
        vertices, indices = np.round(vertices, 5), np.asarray(indices, int)
        visual['geometries'][node['geometry']] = dict(name=name, positions=vertices.ravel().tolist(), indices=indices.tolist(),
                                                      normals=np.round(normals, 5).ravel().tolist(), uv=[], colors=[], groups=[])
        if physical:
            flat = vertices[indices].ravel().tolist()
            floors.append(dict(vertices=flat))
            floor_keys.add(floor_key(flat))
        affected_materials.update(node['materials'])

    print('Rebuilding only street surfaces and paint', flush=True)
    replace('Ruas e cruzamentos contínuos', network.mesh(network.asphalt), True)
    replace('Calçadas recortadas nos cruzamentos', network.mesh(network.sidewalk, network.sidewalk_height, 2), True)
    replace('Faixas e travessias', network.mesh(network.paint, lambda xy: network.height(xy, .082), 2), False)
    replace('Eixos interrompidos nos cruzamentos', network.mesh(network.yellow_paint, lambda xy: network.height(xy, .083), 2), False)
    v, i = network.curb_walls()
    n = np.zeros_like(v)
    if len(v):
        tri = v[np.array(i).reshape(-1, 3)]
        normals = np.cross(tri[:, 1]-tri[:, 0], tri[:, 2]-tri[:, 0])
        normals /= np.maximum(1e-8, np.linalg.norm(normals, axis=1))[:, None]
        n = np.repeat(normals, 3, axis=0)
    replace('Meios-fios com rebaixos de travessia', (v, i, n), True)
    # Original metadata (parking, piers) remains, with current crossing samples.
    street.update(network.metadata())
    street['reviewRevision'] = 1
    for path, road in zip(paths, reference['roads']):
        heights = network.height(path['xz'][:, [0, 2]])
        road['points'] = [vector([p[0], h, p[2]]) for p, h in zip(path['xz'], heights)]
    report.update(afterSignals=len(new_signals), afterSignalDevices=len(devices), afterCrossings=len(kept),
                  inactiveSignalRoots=len(signal_roots)-len(used_roots), exteriorBenches=len(report['benches']),
                  roadAudit=network.audit())
    world['sourceHash'] = hashlib.sha256(before_hash.encode()+Path(__file__).read_bytes()).hexdigest()
    patch = dict(revision=1, sourceHash=world['sourceHash'], previousSourceHash=before_hash,
                 nodes=sorted(changed_nodes), solids=sorted(changed_solids), materials=sorted(affected_materials),
                 floorKeys=sorted(floor_keys), newNodes=new_nodes, batchKeys=sorted(batch_keys))
    return patch, report
