"""Regression for traced streets below sea level, including sidewalk edges."""
import json, os, sys, unittest
from pathlib import Path
sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import numpy as np
from coastal_roads import support_coastal_roads


class CoastalRoadTests(unittest.TestCase):
    def test_fill_supports_entire_diagonal_corridor_but_not_bridges(self):
        grid = np.full((41, 41), -12., dtype=float)
        road = dict(kind='street', width=6.4, xz=np.array([[-60,0,-45],[60,0,45]]))
        support_coastal_roads(grid, [road], (-160,-160), 8)
        self.assertGreaterEqual(grid[20,20], 1.5)
        self.assertEqual(grid[0,0], -12.)
        bridge = dict(road, kind='highway')
        seabed = np.full_like(grid, -12.)
        support_coastal_roads(seabed, [bridge], (-160,-160), 8)
        np.testing.assert_array_equal(seabed, -12.)

    def test_active_map_streets_and_sidewalk_edges_are_dry(self):
        root = Path(__file__).resolve().parents[1]/'OpenTerritory/Assets/Resources'
        world = json.loads((root/'world.json').read_text(encoding='utf-8'))
        reference = json.loads((root/'reference-map.json').read_text(encoding='utf-8'))
        street = json.loads((root/'street-network.json').read_text(encoding='utf-8'))
        g = world['ground']; grid = np.asarray(g['heights']).reshape(g['height'],g['width'])
        def height(x,z):
            fx=(x-g['x'])/g['step']; fz=(z-g['z'])/g['step']; i,j=int(fx),int(fz); a,b=fx-i,fz-j
            if a+b<=1:return grid[j,i]+a*(grid[j,i+1]-grid[j,i])+b*(grid[j+1,i]-grid[j,i])
            return grid[j+1,i+1]+(1-a)*(grid[j+1,i]-grid[j+1,i+1])+(1-b)*(grid[j,i+1]-grid[j+1,i+1])
        samples=0
        for road in reference['roads']:
            if road['kind']!='street':continue
            p=np.array([[v['x'],v['z']] for v in road['points']])
            tangent=p[2:]-p[:-2]; tangent/=np.linalg.norm(tangent,axis=1)[:,None]
            normal=np.column_stack((-tangent[:,1],tangent[:,0]))
            # The saved-city widening tapers around the coast and existing
            # grounds. Check its retained edge and all installed outer samples.
            retained_width = street.get('previousSidewalkWidth', street['sidewalkWidth'])
            for lateral in (-road['width']/2-retained_width,0,road['width']/2+retained_width):
                q=p[1:-1]+normal*lateral
                minimum=min(height(x,z) for x,z in q);samples+=len(q)
                self.assertGreater(minimum,.5,road['id']+' sidewalk/road submerged')
        for p in street['sidewalkSamples']:
            self.assertGreater(height(p['x'], p['z']), .5, 'installed sidewalk over low ground')
        print('Dry road/sidewalk terrain samples:',samples)


if __name__=='__main__':unittest.main()
