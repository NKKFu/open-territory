"""Regressions against the reviewed, playable map (not a second toy layout)."""
import json
import math
import os
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import numpy as np
import shapely as sh
from shapely import Point, LineString, Polygon


class ReviewedStreetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(__file__).resolve().parents[1]
        assets = cls.root/'OpenTerritory/Assets'
        def load(path): return json.loads((assets/path).read_text(encoding='utf-8'))
        cls.world, cls.reference, cls.street = load('Resources/world.json'), load('Resources/reference-map.json'), load('Resources/street-network.json')
        cls.visual, cls.patch = load('Imported/visual.json'), load('Imported/street-review.json')
        cls.nodes = {n['id']: n for n in cls.visual['nodes']}
        cls.lines = {r['id']: LineString([(p['x'], p['z']) for p in r['points']]) for r in cls.reference['roads']}

    def test_short_street_ends_really_reach_the_other_centerline(self):
        self.assertLess(Point(self.lines['bay-west-descent'].coords[-1]).distance(self.lines['101-south']), .001)
        self.assertLess(Point(self.lines['east-ramp-south'].coords[0]).distance(self.lines['waterfront']), .001)

    def test_each_signal_controls_its_actual_incoming_arm(self):
        for signal in self.world['signals']:
            self.assertGreaterEqual(len(signal['approaches']), 3)
            self.assertEqual(len(signal['lamps']), len(signal['approaches'])*3)
            self.assertEqual(signal['phaseCount'], len({a['axis'] for a in signal['approaches']}))
            for arm in signal['approaches']:
                node = self.nodes[arm['node']]
                self.assertTrue(node['visible'])
                yaw = 2*math.atan2(node['rotation']['y'], node['rotation']['w'])
                self.assertGreater(math.sin(yaw)*arm['heading']['x']+math.cos(yaw)*arm['heading']['z'], .999)
                stop = Point(arm['stop']['x'], arm['stop']['z'])
                self.assertLess(stop.distance(self.lines[arm['road']]), .02)

    def test_disabled_signals_leave_no_live_device_or_invisible_collision(self):
        active = {a['node'] for s in self.world['signals'] for a in s['approaches']}
        devices = [d for d in self.world['devices'] if d['type'] == 'signal']
        self.assertEqual(active, {d['node'] for d in devices})
        self.assertTrue(all(0 <= d['target'] < len(self.world['signals']) for d in devices))
        original_roots = set()
        before = self.root/'Reports/map-revision/backup/Assets/Resources/world.json'
        if before.exists():
            old = json.loads(before.read_text(encoding='utf-8'))
            original_roots = {d['node'] for d in old['devices'] if d['type'] == 'signal'}
        for solid in self.world['solids']:
            if solid['owner'] in original_roots:
                self.assertEqual(solid['active'], solid['owner'] in active)

    def test_centerline_paint_does_not_run_under_zebras(self):
        node = next(n for n in self.visual['nodes'] if n['name'] == 'Eixos interrompidos nos cruzamentos')
        geometry = self.visual['geometries'][node['geometry']]
        points = np.array(geometry['positions']).reshape(-1, 3)[:, [0, 2]]
        triangles = sh.polygons(points[np.array(geometry['indices']).reshape(-1, 3)])
        tree = sh.STRtree(triangles)
        for crossing in self.street['crossings']:
            a, b = (np.array([p['x'], p['z']]) for p in crossing['ends'])
            band = LineString([a, b]).buffer(1.4, cap_style='flat')
            overlap = sum(triangles[i].intersection(band).area for i in tree.query(band, predicate='intersects'))
            self.assertLess(overlap, .0001, crossing['road'])

    def test_benches_have_real_owned_geometry_and_clear_road_verges(self):
        report = json.loads((self.root/'Reports/map-revision/review.json').read_text(encoding='utf-8'))
        roads = [(r, self.lines[r['id']]) for r in self.reference['roads'] if r['kind'] != 'trail']
        for bench in report['benches']:
            ident = bench['id']
            self.assertGreaterEqual(sum(n['parent'] == ident and n['geometry'] >= 0 and n['visible'] for n in self.visual['nodes']), 10)
            self.assertEqual(bench['feet'], 2)
            if bench['moved']:
                p = Point(bench['position']['x'], bench['position']['z'])
                self.assertTrue(all(line.distance(p) > r['width']/2+1 for r, line in roads))

    def test_existing_parcels_and_simulation_content_are_preserved(self):
        before = self.root/'Reports/map-revision/backup/Assets/Resources/world.json'
        if not before.exists(): self.skipTest('Initial workspace backup is not available')
        old = json.loads(before.read_text(encoding='utf-8'))
        for key in ['places', 'vehicles', 'stops', 'boats', 'docks', 'ground', 'natureTrees', 'natureGroves', 'rocks', 'services', 'nav']:
            self.assertEqual(old[key], self.world[key], key)


if __name__ == '__main__': unittest.main()
