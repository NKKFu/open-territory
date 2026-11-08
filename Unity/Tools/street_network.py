"""Planar street topology and terrain-conforming meshes, built offline.

Roads are united before sidewalks are derived. No independent sidewalk strip
is allowed to continue through another carriageway. Shapely 2.1 / GEOS CDT
retains holes and junction boundaries; cell splits follow the terrain grid.
"""
import math
import numpy as np
import shapely as sh
from shapely import LineString, Polygon, Point, box
from shapely.ops import substring


def parts(shape, min_area=.03):
    if shape.is_empty:
        return []
    return [p for p in sh.get_parts(shape) if isinstance(p, Polygon) and p.area >= min_area]


def clean(shape, min_area=.03):
    shape = sh.make_valid(sh.set_precision(shape, .001))
    return sh.union_all(parts(shape, min_area))


def width(path):
    return max(6.4, path['width']) if path['kind'] == 'street' else path['width']


class StreetNetwork:
    sidewalk_width = 3.6
    asphalt_lift = .07
    sidewalk_lift = .21

    def __init__(self, paths, ground, grid_origin, sidewalk_width=None):
        if sidewalk_width is not None:
            self.sidewalk_width = sidewalk_width
        self.paths, self.ground, self.origin = paths, ground, grid_origin
        self.lines = {r['id']: LineString(r['xz'][:, [0, 2]]) for r in paths}
        self.corridors = {r['id']: self.lines[r['id']].buffer(width(r) / 2, cap_style='round', join_style='round', quad_segs=4) for r in paths}
        self.drivable = [r for r in paths if r['kind'] != 'trail']
        self.streets = [r for r in paths if r['kind'] == 'street']
        asphalt = sh.union_all([self.corridors[r['id']] for r in self.drivable])
        # Small corner fillets remove knife-like notches at acute intersections.
        self.asphalt = clean(asphalt.buffer(1.2, quad_segs=4).buffer(-1.2, quad_segs=4), .5)
        street_area = sh.union_all([self.corridors[r['id']] for r in self.streets])
        outer = street_area.buffer(self.sidewalk_width, quad_segs=4, join_style='round')
        self.sidewalk = clean(outer.difference(self.asphalt.buffer(.035)), 2.5)
        safe_sidewalk = self.sidewalk.buffer(.05)
        sh.prepare(safe_sidewalk)
        self.trails = clean(sh.union_all([self.corridors[r['id']] for r in paths if r['kind'] == 'trail']).difference(self.asphalt.union(self.sidewalk)))
        self.highways = [(r, LineString([(p[0], p[2], h) for p, h in zip(r['xz'], r['heights'])])) for r in paths if r['kind'] == 'highway']
        self.junctions = []
        for i, a in enumerate(self.drivable):
            for b in self.drivable[:i]:
                intersection = self.lines[a['id']].intersection(self.lines[b['id']])
                for p in sh.get_parts(intersection):
                    if not isinstance(p, Point):
                        continue
                    near = next((j for j in self.junctions if p.distance(j['point']) < 8), None)
                    if near:
                        near['roads'].update((a['id'], b['id']))
                    else:
                        self.junctions.append(dict(point=p, roads={a['id'], b['id']}))
        others = {r['id']: sh.union_all([self.corridors[b['id']] for b in self.drivable if b['id'] != r['id']]) for r in self.drivable}
        other_crossings = {key: value.buffer(1) for key,value in others.items()}
        self.crossings = []
        for junction in self.junctions:
            local = [r for r in self.streets if r['id'] in junction['roads']]
            if len(local) < 2 or any(r['kind'] == 'highway' and r['id'] in junction['roads'] for r in self.drivable):
                continue
            for road in local:
                line = self.lines[road['id']]
                center = line.project(junction['point'])
                other = other_crossings[road['id']]
                for side in (-1, 1):
                    for offset in (8, 11, 15, 20, 26):
                        at = center + side * offset
                        if not 4 < at < line.length - 4:
                            continue
                        c = np.array(line.interpolate(at).coords[0])
                        tangent = np.array(line.interpolate(at + .5).coords[0]) - np.array(line.interpolate(at - .5).coords[0])
                        tangent /= np.linalg.norm(tangent)
                        normal = np.array([-tangent[1], tangent[0]])
                        corners = [c + tangent * x + normal * z for x, z in [(-1.6, -width(road)/2), (1.6, -width(road)/2), (1.6, width(road)/2), (-1.6, width(road)/2)]]
                        rectangle = Polygon(corners)
                        if rectangle.intersection(other).area > .05:
                            continue
                        if any(np.linalg.norm(c - x['center']) < 9 for x in self.crossings):
                            break
                        # Both ends must actually lead onto a sidewalk.
                        if not all(safe_sidewalk.contains(Point(c + normal * s * (width(road)/2 + 1))) for s in (-1, 1)):
                            continue
                        self.crossings.append(dict(center=c, tangent=tangent, normal=normal, width=width(road), road=road['id']))
                        break
        self.markings = []
        self.yellow = []
        self.medians = []
        junction_mask = sh.union_all([j['point'].buffer(10) for j in self.junctions])
        for road in self.drivable:
            line = self.lines[road['id']]
            other = others[road['id']].buffer(2.4)
            if road['kind'] == 'highway':
                self.medians.append(line.buffer(.36).difference(other))
                for offset in (-width(road)/2+.38, width(road)/2-.38):
                    self.markings.append(line.offset_curve(offset).buffer(.085).difference(other))
                for offset in (-width(road)/4, width(road)/4):
                    lane = line.offset_curve(offset)
                    for distance in np.arange(5, lane.length-5, 10):
                        self.markings.append(substring(lane, distance, min(distance+4,lane.length)).buffer(.06,cap_style='flat').difference(other))
                continue
            if width(road) < 6 or road['kind'] == 'ramp':
                continue
            # Interrupted lane markings stop before every intersection/merge.
            for distance in np.arange(5, line.length - 5, 8):
                segment = substring(line, distance, min(distance + 3, line.length))
                self.yellow.append(segment.buffer(.055, cap_style='flat').difference(junction_mask).difference(other))
        for crossing in self.crossings:
            c, t, n, half = crossing['center'], crossing['tangent'], crossing['normal'], crossing['width']/2 - .35
            for z in np.arange(-half, half-.3, .85):
                self.markings.append(Polygon([c+t*x+n*y for x,y in [(-1.35,z),(1.35,z),(1.35,z+.43),(-1.35,z+.43)]]))
        self.paint = clean(sh.union_all(self.markings).intersection(self.asphalt))
        self.yellow_paint = clean(sh.union_all(self.yellow).intersection(self.asphalt))
        self.median = clean(sh.union_all(self.medians), .4)
        # Baseline demonstrates the original crossing-strip bug over the map.
        original = []
        for r in self.streets:
            for side in (-1, 1):
                original.append(self.lines[r['id']].offset_curve(side*(r['width']/2+.9)).buffer(.9, cap_style='flat'))
        self.old_overlap = sh.union_all(original).intersection(asphalt).area

    def height(self, coordinates, lift=.07):
        xy = np.asarray(coordinates, float).reshape(-1, 2)
        base = np.array([self.ground(np.array([x, 0, z])) for x, z in xy])
        points = sh.points(xy)
        for road, line in self.highways:
            distance = sh.distance(points, line)
            influence = np.clip(1 - np.maximum(0, distance - width(road)/2) / 8, 0, 1)
            selected = influence > 0
            if not selected.any():
                continue
            q = sh.line_interpolate_point(line, sh.line_locate_point(line, points[selected]))
            grade = sh.get_coordinates(q, include_z=True)[:, 2]
            base[selected] += np.maximum(0, grade - base[selected]) * influence[selected]
        return base + lift

    def sidewalk_height(self, xy):
        xy = np.asarray(xy, float).reshape(-1, 2)
        height = self.height(xy, self.sidewalk_lift)
        drop = np.zeros(len(xy))
        for ramp in self.crossings:
            delta = xy - ramp['center']
            along = abs(delta @ ramp['tangent'])
            across = abs(delta @ ramp['normal']) - ramp['width']/2
            blend = np.clip((2.15-along)/.65, 0, 1) * np.clip(1 - np.maximum(0, across)/1.7, 0, 1)
            drop = np.maximum(drop, blend * .135)
        return height - drop

    def mesh(self, shape, height=None, cell=4, _tile=False):
        if shape.is_empty:
            return np.empty((0,3)), [], np.empty((0,3))
        height = height or self.height
        x0,z0,x1,z1 = shape.bounds
        gx,gz = self.origin
        # Overlaying every 2 m cell against the entire coast-sized polygon is
        # needlessly quadratic. Clip into aligned local tiles first.
        if not _tile and (x1-x0>96 or z1-z0>96):
            tx=np.arange(math.floor((x0-gx)/96)*96+gx,x1,96)
            tz=np.arange(math.floor((z0-gz)/96)*96+gz,z1,96)
            xx,zz=np.meshgrid(tx,tz);xx,zz=xx.ravel(),zz.ravel()
            tiles=sh.box(xx,zz,xx+96,zz+96);sh.prepare(shape)
            tiles=tiles[sh.intersects(shape,tiles)]
            clipped=sh.intersection(shape,tiles)
            vertices=[];indices=[];normals=[];offset=0
            for part in clipped:
                if part.area<.00001:continue
                v,i,n=self.mesh(part,height,cell,True)
                vertices.append(v);indices.append(np.asarray(i)+offset);normals.append(n);offset+=len(v)
            return np.concatenate(vertices),np.concatenate(indices),np.concatenate(normals)
        xs = np.arange(math.floor((x0-gx)/cell)*cell+gx, x1+cell, cell)
        zs = np.arange(math.floor((z0-gz)/cell)*cell+gz, z1+cell, cell)
        xx,zz = np.meshgrid(xs, zs); xx,zz = xx.ravel(),zz.ravel()
        cells = sh.box(xx,zz,xx+cell,zz+cell)
        sh.prepare(shape)
        mask = sh.intersects(shape, cells); xx,zz = xx[mask],zz[mask]
        # Match the terrain's SW/NE diagonal, so no grass slices through a road.
        a = np.stack((np.column_stack((xx,zz)), np.column_stack((xx,zz+cell)), np.column_stack((xx+cell,zz))), axis=1)
        b = np.stack((np.column_stack((xx+cell,zz)), np.column_stack((xx,zz+cell)), np.column_stack((xx+cell,zz+cell))), axis=1)
        clipped = sh.intersection(shape, sh.polygons(np.concatenate((a,b))))
        triangles = sh.get_parts(sh.constrained_delaunay_triangles(clipped))
        triangles = triangles[sh.area(triangles) > .000002]
        xy = sh.get_coordinates(triangles).reshape(-1,4,2)[:,:3,:]
        area = (xy[:,1,0]-xy[:,0,0])*(xy[:,2,1]-xy[:,0,1])-(xy[:,1,1]-xy[:,0,1])*(xy[:,2,0]-xy[:,0,0])
        xy[area > 0] = xy[area > 0][:,[0,2,1],:]
        coordinates = xy.reshape(-1,2)
        vertices = np.column_stack((coordinates[:,0], height(coordinates), coordinates[:,1]))
        tri = vertices.reshape(-1,3,3)
        normal = np.cross(tri[:,1]-tri[:,0], tri[:,2]-tri[:,0]); normal /= np.maximum(1e-9,np.linalg.norm(normal,axis=1))[:,None]
        return vertices, np.arange(len(vertices)), np.repeat(normal,3,axis=0)

    def curb_walls(self):
        vertices = []
        for polygon in parts(self.sidewalk):
            for ring in [polygon.exterior, *polygon.interiors]:
                points = np.asarray(sh.segmentize(ring, max_segment_length=1).coords)
                top = self.sidewalk_height(points)
                bottom = self.height(points, .025)
                for i in range(len(points)-1):
                    a,b = points[i],points[i+1]
                    vertices.extend([[a[0],bottom[i],a[1]],[a[0],top[i],a[1]],[b[0],top[i+1],b[1]], [a[0],bottom[i],a[1]],[b[0],top[i+1],b[1]],[b[0],bottom[i+1],b[1]]])
        vertices = np.asarray(vertices)
        return vertices, np.arange(len(vertices))

    def metadata(self):
        def vector(xy,y):return dict(x=round(float(xy[0]),5),y=round(float(y),5),z=round(float(xy[1]),5))
        samples = []
        for road in self.streets:
            line = self.lines[road['id']]
            for d in np.arange(8,line.length-8,18):
                c = np.array(line.interpolate(d).coords[0]); f=np.array(line.interpolate(d+1).coords[0])-np.array(line.interpolate(d-1).coords[0]);f/=np.linalg.norm(f)
                for side in (-1,1):
                    p=c+np.array([-f[1],f[0]])*side*(width(road)/2+1.1)
                    if self.sidewalk.contains(Point(p)):
                        samples.append(vector(p,self.sidewalk_height([p])[0]))
        crossings=[]
        for r in self.crossings:
            ends=[r['center']+r['normal']*side*(r['width']/2+1) for side in (-1,1)]
            crossings.append(dict(position=vector(r['center'],self.height([r['center']])[0]),road=r['road'],ends=[vector(p,self.sidewalk_height([p])[0]) for p in ends]))
        return dict(revision=3,sidewalkWidth=self.sidewalk_width,curbHeight=self.sidewalk_lift-self.asphalt_lift,
                    junctions=[vector(np.array(j['point'].coords[0]),self.height(np.array(j['point'].coords))[0]) for j in self.junctions],
                    sidewalkSamples=samples,crossings=crossings)

    def audit(self):
        overlap = self.sidewalk.intersection(self.asphalt).area
        if overlap > .001 or not self.sidewalk.is_valid or not self.asphalt.is_valid:
            raise RuntimeError('Invalid street/sidewalk topology')
        return dict(previousSidewalkAcrossRoadArea=round(self.old_overlap,3),sidewalkAcrossRoadArea=round(overlap,6),
                    roadArea=round(self.asphalt.area,2),sidewalkArea=round(self.sidewalk.area,2),junctions=len(self.junctions),crossings=len(self.crossings),
                    isolatedPavingFragments=sum(p.area<2.5 for p in parts(self.sidewalk,0)),valid=True)
