import copy
import json
import math
import unittest
from pathlib import Path
from doorways import apply_doorways


class DoorwaysTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        assets = Path(__file__).resolve().parents[1] / 'OpenTerritory/Assets'
        # The immutable donor remains the source for future map generations.
        donor = assets.parents[1] / 'AssetLibrary/OriginalCity'
        cls.world = json.loads((donor / 'world.json').read_text(encoding='utf-8-sig'))
        cls.visual = json.loads((donor / 'visual.json').read_text(encoding='utf-8-sig'))

    def setUp(self):
        self.world = copy.deepcopy(self.world)
        door_ids = {p['door']['id'] for p in self.world['places']}
        self.visual = dict(nodes=copy.deepcopy([n for n in self.visual['nodes'] if n['id'] in door_ids or n['parent'] in door_ids]))
        self.original = copy.deepcopy(self.world)
        self.changes = apply_doorways(self.visual, self.world)

    def test_widths_and_automatic_doors(self):
        for before, after in zip(self.original['places'], self.world['places']):
            d = after['door']
            if d['automatic']:
                self.assertEqual(before, after)
            else:
                self.assertEqual(d['width'], 1.4 if after['kind'] in ('home', 'residence') else 1.6)
                self.assertEqual((d['x'], d['z']), (before['door']['x'], before['door']['z']))

    def test_apertures_are_filled_and_centered(self):
        nodes = {n['id']: n for n in self.visual['nodes']}
        for before, after in zip(self.original['places'], self.world['places']):
            d = after['door']
            if d['automatic']:
                continue
            yaw = d.get('yaw', 0)
            center = nodes[d['id']]['position']
            old = before['door']['position']
            side = (before['door']['width'] - d['width']) / 2
            self.assertAlmostEqual(center['x'] - math.cos(yaw) * d['width'] / 2, old['x'] - math.cos(yaw) * before['door']['width'] / 2)
            self.assertAlmostEqual(center['z'] + math.sin(yaw) * d['width'] / 2, old['z'] + math.sin(yaw) * before['door']['width'] / 2)
            panels = [n for n in self.visual['nodes'] if n['name'].startswith(f'Doorway sidelight-{d["id"]}-')]
            self.assertEqual(len(panels), 2)
            solids = [s for s in self.world['solids'] if s['owner'] in {n['id'] for n in panels}]
            self.assertAlmostEqual(sum(s['w'] for s in solids) + d['width'], before['door']['width'])
            self.assertTrue(all(s['active'] and abs(s['w'] - side) < .001 for s in solids))

    def test_handles_and_stiles_keep_their_thickness(self):
        roots = {p['door']['id'] for p in self.world['places'] if not p['door']['automatic']}
        for node in self.visual['nodes']:
            if node['parent'] in roots and abs(node['position']['z']) > .05:
                self.assertEqual(node['scale']['x'], .13)
            self.assertGreater(node['scale']['x'], 0)

    def test_idempotent_and_unique_ids(self):
        snapshot = json.dumps((self.world, self.visual), sort_keys=True)
        self.assertEqual(apply_doorways(self.visual, self.world), [])
        self.assertEqual(json.dumps((self.world, self.visual), sort_keys=True), snapshot)
        ids = [n['id'] for n in self.visual['nodes']]
        self.assertEqual(len(ids), len(set(ids)))

    def test_rotated_entrance_keeps_its_center(self):
        for yaw in (-2.44, .55, 1.8):
            world = copy.deepcopy(self.original)
            visual = copy.deepcopy(type(self).visual)
            for place in world['places']:
                place['door']['yaw'] = yaw
            before = copy.deepcopy(world['places'])
            apply_doorways(visual, world)
            nodes = {n['id']: n for n in visual['nodes']}
            for old, place in zip(before, world['places']):
                if place['door']['automatic']:
                    continue
                root = nodes[place['door']['id']]['position']; origin = old['door']['position']
                for axis, normal in [('x', math.cos(yaw)), ('z', -math.sin(yaw))]:
                    self.assertAlmostEqual(root[axis] - normal * place['door']['width'] / 2,
                                           origin[axis] - normal * old['door']['width'] / 2)

    def test_navigation_interiors_and_existing_solids_preserved(self):
        for field in ('nav', 'locations', 'residents', 'spawn'):
            self.assertEqual(self.world[field], self.original[field])
        self.assertEqual(self.world['solids'][:len(self.original['solids'])], self.original['solids'])


if __name__ == '__main__':
    unittest.main()
