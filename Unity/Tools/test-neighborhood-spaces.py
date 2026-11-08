"""Geometry checks for usable access, disjoint property and intact assemblies."""
import os,sys,unittest,math
from pathlib import Path
sys.path.insert(0,str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import numpy as np
from shapely import box,LineString
from neighborhood_spaces import plan_spaces,AssetSites,plaza_asset,access_grade
from building_frontages import rectangle,apron_height


class NeighborhoodGeometry(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lots=[dict(center=np.array([x,19.]),size=[18,14],yaw=math.pi) for x in [-38,0,38]]
        cls.paths=[dict(id='street',kind='street',width=6.4,xz=np.array([[-180,0,0],[180,0,0]]))]
        cls.bodies=[rectangle(l['center'],l['size'],l['yaw'],.22) for l in cls.lots]
        cls.spaces=plan_spaces(cls.lots,cls.paths,[],box(-220,-150,220,150),lambda p:2)
    def test_paths_connect_rear_yards_without_crossing_houses_or_road(self):
        yards=[s for s in self.spaces if s['kind']=='yard'];self.assertEqual(len(yards),3)
        for s in yards:
            for b in self.bodies:self.assertFalse(s['path'].intersects(b))
            self.assertGreater(s['path'].distance(LineString([[-180,0],[180,0]])),5)
            self.assertTrue(s['shape'].buffer(.05).intersects(s['route']))
    def test_parcels_and_parking_do_not_overlap(self):
        self.assertGreaterEqual(sum(s['kind']=='parking' for s in self.spaces),6)
        for i,s in enumerate(self.spaces):
            for other in self.spaces[:i]:self.assertLess(s['shape'].intersection(other['shape']).area,.0001)
            for b in self.bodies:self.assertLess(s['shape'].intersection(b).area,.0001)
    def test_steep_final_ground_does_not_create_an_inaccessible_yard(self):
        steep=plan_spaces(self.lots,self.paths,[],box(-220,-150,220,150),lambda p:max(0,p[2]-20)*2)
        self.assertFalse(any(s['kind']=='yard' for s in steep))
    def test_furniture_reserves_routes_and_other_complete_objects(self):
        pack=AssetSites(self.spaces,self.bodies)
        for i in range(20):
            item=pack.allocate('bench'+str(i),[4.4,3.7]);self.assertIsNotNone(item)
            space=next(s for s in self.spaces if s['id']==item['space'])
            self.assertFalse(item['shape'].intersects(space['path']))
        for i,a in enumerate(pack.items):
            for b in pack.items[:i]:self.assertFalse(a['shape'].intersects(b['shape']))
    def test_guide_music_and_tree_branches_keep_one_transform(self):
        for p in [[-24.19,1.3,54.9],[-21.11,1.3,54.9],[-22.65,3.2,55.03]]:self.assertEqual(plaza_asset(p),'guide')
        for p in [[-26,.091,19.2],[-24.6,1,20.43],[-25.1,0,24.2]]:self.assertEqual(plaza_asset(p),'music')
        for p in [[-52,2.2,20],[-50.8,5.8,21.1],[-52.5,4,19.1]]:self.assertEqual(plaza_asset(p),'tree-0')
    def test_neighboring_forecourts_preserve_both_boundary_heights(self):
        class FlatNetwork:
            def height(self,xy,lift=.07):return np.full(len(xy),lift)
            def sidewalk_height(self,xy):return np.full(len(xy),.21)
        network=FlatNetwork();aprons=[]
        for x,elevation in [(-2.5,.2),(2.5,1.2)]:
            aprons.append(dict(shape=box(x-1.5,0,x+1.5,2),start=np.array([x,0]),front=np.array([0,1]),walk=box(x-1.5,2,x+1.5,3),place=dict(elevation=elevation)))
        for primary in [None,*aprons]:
            for apron,x in zip(aprons,[-1.05,1.05]):
                expected=apron_height(apron,network,[[x,1]])
                np.testing.assert_allclose(access_grade([[x,1]],aprons,network,primary),expected,atol=.00001)
            heights=access_grade([[x,1] for x in np.linspace(-1.1,1.1,101)],aprons,network,primary)
            self.assertLess(np.abs(np.diff(heights)).max(),.04)


if __name__=='__main__':unittest.main()
