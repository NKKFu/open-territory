import unittest
import numpy as np
from shapely import Polygon,Point
from building_frontages import plan_lots,make_aprons,apron_height
from street_network import StreetNetwork


class FrontageTests(unittest.TestCase):
    def setUp(self):
        self.road=dict(id='town-spine',kind='street',width=6.4,xz=np.array([[-250,0,0],[250,0,0]],float),heights=np.zeros(2))
        self.places=[dict(name=str(i),kind='home',w=18,d=14,x=0,z=0,door=dict(x=0,z=7.24,width=2,automatic=False)) for i in range(17)]
        self.lots=plan_lots([self.road],self.places,[],Polygon([(-500,-500),(-500,500),(500,500),(500,-500)]),lambda p:np.array([990+p[0]/2.4,355-p[2]/2.4]),lambda p:0,homes=0,minimum=17)

    def test_fronts_face_the_street_with_a_small_consistent_setback(self):
        for p in self.lots:
            forward=np.array([np.sin(p['yaw']),np.cos(p['yaw'])]);door=p['center']+forward*7.24
            self.assertGreater(np.dot(forward,p['point']-p['center']),0)
            self.assertAlmostEqual(abs(door[1])-(3.2+StreetNetwork.sidewalk_width),1.45,places=5)

    def test_compact_rows_preserve_clear_separation_between_buildings(self):
        for i,a in enumerate(self.lots):
            for b in self.lots[:i]:self.assertFalse(a['shape'].intersects(b['shape']))
        for side in (-1,1):
            row=sorted(p['center'][0] for p in self.lots if p['side']==side)
            self.assertLess(float(np.median(np.diff(row)))-18,3)

    def test_each_apron_meets_the_correct_sidewalk_without_covering_asphalt(self):
        network=StreetNetwork([self.road],lambda p:0,(-256,-64));places=[]
        for lot in self.lots:
            f=np.array([np.sin(lot['yaw']),np.cos(lot['yaw'])]);door=lot['center']+f*7.24
            places.append(dict(name=str(lot['index']),x=lot['center'][0],z=lot['center'][1],lotWidth=18,elevation=0,yaw=lot['yaw'],door=dict(x=door[0],z=door[1])))
        aprons=make_aprons(places,network,lambda p:0)
        self.assertEqual(len(aprons),17)
        for a in aprons:
            self.assertTrue(network.sidewalk.contains(Point(a['end'])))
            self.assertLess(a['shape'].intersection(network.asphalt).area,.00001)
            self.assertAlmostEqual(apron_height(a,network,[a['end']])[0],.21,places=5)
            samples=np.linspace(a['start'],a['end'],30);heights=apron_height(a,network,samples)
            slope=np.max(abs(np.diff(heights)))/np.linalg.norm(samples[1]-samples[0])
            self.assertLess(slope,.15)


if __name__=='__main__':unittest.main()
