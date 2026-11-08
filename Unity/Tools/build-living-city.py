"""Derive ambient circuits and pedestrian edge rules from the existing map.

Does not rebuild geometry or move authored objects. Uses the same isolated
Shapely dependency directory as the map tools when it is not on PYTHONPATH.
"""
import collections
import heapq
import json
import math
import os
from pathlib import Path
import random
import sys

try:
    import shapely as sh
except ModuleNotFoundError:
    sys.path.insert(0, str(Path(os.environ['TEMP']) / 'open-territory-map-trace-deps'))
    import shapely as sh
from shapely import LineString, Point, Polygon

ROOT = Path(__file__).resolve().parents[1]
RES = ROOT / 'OpenTerritory/Assets/Resources'
world = json.loads((RES / 'world.json').read_text(encoding='utf-8'))
layout = json.loads((RES / 'reference-map.json').read_text(encoding='utf-8'))
roads = [r for r in layout['roads'] if r['kind'] == 'street']
lines = [LineString([(p['x'], p['z']) for p in r['points']]) for r in roads]
asphalt = sh.union_all([line.buffer(r['width'] / 2 + .02) for line, r in zip(lines, roads)])
all_asphalt = sh.union_all([LineString([(p['x'], p['z']) for p in r['points']]).buffer(r['width'] / 2)
                          for r in layout['roads'] if r['kind'] != 'trail'])
sh.prepare(asphalt)
points, edges, ids = [], collections.defaultdict(dict), {}


def node(p):
    key = (round(p[0], 4), round(p[1], 4))
    if key not in ids:
        ids[key] = len(points)
        points.append(key)
    return ids[key]


for part in sh.get_parts(sh.union_all(lines)):
    coords = list(part.coords)
    for a, b in zip(coords, coords[1:]):
        i, j = node(a), node(b)
        if i == j:
            continue
        edges[i][j] = edges[j][i] = math.dist(a, b)

# Collapse degree-two chains; actual intersections become route decisions.
junctions = {i for i in edges if len(edges[i]) != 2}
graph, segments = collections.defaultdict(dict), {}
for start in sorted(junctions):
    for nxt in edges[start]:
        chain, previous, at = [start, nxt], start, nxt
        while at not in junctions:
            following = next(n for n in edges[at] if n != previous)
            previous, at = at, following
            chain.append(at)
        if at == start:
            continue
        length = sum(edges[a][b] for a, b in zip(chain, chain[1:]))
        if at not in graph[start] or length < graph[start][at]:
            graph[start][at] = length
            segments[start, at] = chain


def alternate(start, end, rng):
    costs, prev, queue = {start: 0}, {}, [(0, start)]
    weights = {tuple(sorted((a, b))): rng.uniform(.7, 1.5) for a in graph for b in graph[a]}
    while queue:
        cost, at = heapq.heappop(queue)
        if at == end:
            result = [end]
            while result[-1] != start:
                result.append(prev[result[-1]])
            return result[::-1]
        if cost > costs[at]:
            continue
        for nxt, length in graph[at].items():
            if {at, nxt} == {start, end}:
                continue
            proposed = cost + length * weights[tuple(sorted((at, nxt)))]
            if proposed < costs.get(nxt, 1e30):
                costs[nxt], prev[nxt] = proposed, at
                heapq.heappush(queue, (proposed, nxt))
    return None


ground = world['ground']


def height(x, z):
    fx = max(0, min(ground['width'] - 1.001, (x - ground['x']) / ground['step']))
    fz = max(0, min(ground['height'] - 1.001, (z - ground['z']) / ground['step']))
    i, j = int(fx), int(fz)
    a, b, at = fx - i, fz - j, j * ground['width'] + i
    h, r, t, c = [ground['heights'][at + d] for d in (0, 1, ground['width'], ground['width'] + 1)]
    return (h + a * (r - h) + b * (t - h) if a + b <= 1
            else c + (1 - a) * (t - c) + (1 - b) * (r - c)) + .08


rejected = collections.Counter()


def lane(cycle, reverse):
    chain = []
    for a, b in zip(cycle, cycle[1:]):
        chain.extend(points[i] for i in segments[a, b][:-1])
    if reverse:
        chain.reverse()
    base = LineString(chain + chain[:1])
    # Sample by distance before rounding; the source has metre-long segments
    # at junctions, so their individual lengths are not a turning-radius limit.
    corners = list(base.simplify(.3).coords)[:-1]
    for i, p in enumerate(corners):
        before, after = corners[i-1], corners[(i+1)%len(corners)]
        a, b = (p[0]-before[0],p[1]-before[1]), (after[0]-p[0],after[1]-p[1])
        turn = math.acos(max(-1,min(1,(a[0]*b[0]+a[1]*b[1])/max(.001,math.hypot(*a)*math.hypot(*b)))))
        if turn > math.radians(125):
            rejected['hairpin'] += 1
            return None
    rounded = [base.interpolate(i*base.length/math.ceil(base.length/3)) for i in range(math.ceil(base.length/3))]
    rounded = [(p.x,p.y) for p in rounded]
    for _ in range(5):
        rounded = [((rounded[i-1][0]+2*p[0]+rounded[(i+1)%len(rounded)][0])*.25,
                    (rounded[i-1][1]+2*p[1]+rounded[(i+1)%len(rounded)][1])*.25) for i,p in enumerate(rounded)]
    line = LineString(rounded + rounded[:1])
    count = max(3, math.ceil(line.length / 2))
    center = [line.interpolate(i * line.length / count) for i in range(count)]
    curvature, turn_signs = [], []
    for i, p in enumerate(center):
        before, after = center[i-1], center[(i+1)%count]
        a, b = (p.x-before.x, p.y-before.y), (after.x-p.x, after.y-p.y)
        length = max(.001, math.hypot(*a)*math.hypot(*b))
        turn = math.acos(max(-1,min(1,(a[0]*b[0]+a[1]*b[1])/length)))
        curvature.append(turn/max(.01,(math.hypot(*a)+math.hypot(*b))*.5))
        turn_signs.append(1 if a[0]*b[1]-a[1]*b[0]>0 else -1)
    result = []
    for i, p in enumerate(center):
        before, after = center[i - 1], center[(i + 1) % count]
        dx, dz = after.x - before.x, after.y - before.y
        length = max(.001, math.hypot(dx, dz))
        # Positive offset is the right-hand side of travel in the X/Z plane.
        # Take a wider line at tight junctions, tapering over twelve metres.
        # A constant inside-lane offset would demand a physically impossible
        # turning circle on the narrowest corners.
        nearby = [(curvature[(i+j)%count] * max(0,1-abs(j)/7), turn_signs[(i+j)%count]) for j in range(-6,7)]
        bend, turn_side = max(nearby)
        offset = 1.45 - (2 * max(0,min(1,(bend-.045)/.11)) if turn_side < 0 else 0)
        x, z = p.x + dz / length * offset, p.y - dx / length * offset
        result.append(dict(x=round(x, 4), y=round(height(x, z), 4), z=round(z, 4)))
    for _ in range(3):
        result = [dict(x=(result[i-1]['x']+2*p['x']+result[(i+1)%count]['x'])*.25,
                       z=(result[i-1]['z']+2*p['z']+result[(i+1)%count]['z'])*.25) for i,p in enumerate(result)]
    for p in result:
        p['y'] = round(height(p['x'],p['z']),4)
        p['x'],p['z'] = round(p['x'],4),round(p['z'],4)
    # Reject residual kinks rather than handing an impossible bend to PhysX.
    for i, p in enumerate(result):
        previous, following = result[i-1], result[(i+1)%count]
        a, b = (p['x']-previous['x'],p['z']-previous['z']), (following['x']-p['x'],following['z']-p['z'])
        la, lb = math.hypot(*a), math.hypot(*b)
        turn = math.acos(max(-1,min(1,(a[0]*b[0]+a[1]*b[1])/max(.001,la*lb))))
        if turn/max(.01,(la+lb)*.5) > .24:
            rejected['curvature'] += 1
            return None
    return result


rng, seen, circuits = random.Random(93217), set(), []
for attempt in range(18):
    for a in sorted(graph):
        for b in sorted(graph[a]):
            if a > b:
                continue
            path = alternate(a, b, rng)
            if not path or len(path) < 3:
                continue
            cycle = path + [a]
            key = tuple(sorted(tuple(sorted((x, y))) for x, y in zip(cycle, cycle[1:])))
            if key in seen:
                continue
            seen.add(key)
            length = sum(graph[x][y] for x, y in zip(cycle, cycle[1:]))
            if not 100 < length < 3600:
                continue
            for reverse in (False, True):
                route = lane(cycle, reverse)
                if not route:
                    continue
                # Reserve body width plus a bounded lateral driving offset.
                invalid = sum(not asphalt.covers(Point(p['x'], p['z']).buffer(1.5, quad_segs=4)) for p in route)
                if invalid:
                    rejected['footprint'] += 1
                    continue
                circuits.append(dict(id=f'local-{len(circuits):02d}', points=route, length=round(length, 2)))
    if len(circuits) >= 64:
        break

# Crossing flags were lost in the reference conversion. Restore them without
# changing world.json. Also prevent paths from cutting through building walls.
buildings = []
for p in world['places']:
    w, d = p.get('lotWidth', p['w']), p.get('lotDepth', p['d'])
    yaw = p.get('yaw', 0)
    corners = [(p['x'] + x * math.cos(yaw) + z * math.sin(yaw),
                p['z'] - x * math.sin(yaw) + z * math.cos(yaw))
               for x, z in [(-w/2, -d/2), (w/2, -d/2), (w/2, d/2), (-w/2, d/2)]]
    buildings.append(Polygon(corners).buffer(.25))
building_area = sh.union_all(buildings)
sh.prepare(building_area)
road_interior = all_asphalt.buffer(-.25)
sh.prepare(road_interior)
print(f'Circuits ready: {len(circuits)}; rejected={dict(rejected)}; auditing pedestrian links', flush=True)
nav = {n['id']: n for n in world['nav']}
blocked, crossing = [], []
for a in world['nav']:
    for edge in a['edges']:
        if a['id'] >= edge['to']:
            continue
        b = nav[edge['to']]
        line = LineString([(a['x'], a['z']), (b['x'], b['z'])])
        pair = dict(a=a['id'], b=b['id'])
        if building_area.intersects(line):
            blocked.append(pair)
        elif road_interior.intersects(line) and line.intersection(road_interior).length > 1:
            crossing.append(pair)

if len(circuits) < 8:
    raise RuntimeError(f'Only {len(circuits)} safe ambient circuits')
result = dict(revision=1, layoutId=world['layoutId'], routes=circuits,
              blockedEdges=blocked, crossingEdges=crossing)
(RES / 'living-city.json').write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
report = dict(routes=len(circuits), uniqueRoadEdges=len(seen), blockedEdges=len(blocked),
              crossingEdges=len(crossing), routeLengths=[r['length'] for r in circuits])
out = ROOT / 'Reports/living-city'
out.mkdir(parents=True, exist_ok=True)
(out / 'navigation-generation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
