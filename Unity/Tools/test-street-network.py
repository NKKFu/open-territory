"""Geometric regressions for the actual crossing/sidewalk failure."""
import unittest
import numpy as np
from shapely import Point
from street_network import StreetNetwork


def path(name, points, kind='street', width=6.4):
    return dict(id=name,kind=kind,width=width,xz=np.array([[x,0,z] for x,z in points],float),heights=np.zeros(len(points)))


class StreetTests(unittest.TestCase):
    def network(self, roads):
        return StreetNetwork(roads,lambda p:0,(-40,-40))

    def test_crossroads_have_no_sidewalk_across_carriageway(self):
        n=self.network([path('main',[(-32,0),(32,0)]),path('cross',[(0,-32),(0,32)])])
        self.assertGreater(n.old_overlap,15)
        self.assertLess(n.sidewalk.intersection(n.asphalt).area,.0001)
        self.assertTrue(n.asphalt.contains(Point(0,0)))
        self.assertFalse(n.sidewalk.contains(Point(0,0)))
        self.assertGreaterEqual(len(n.crossings),4)

    def test_t_junction_does_not_close_the_through_lane(self):
        n=self.network([path('main',[(-32,0),(32,0)]),path('stem',[(0,-32),(0,0)])])
        for x in np.linspace(-30,30,100):
            self.assertTrue(n.asphalt.contains(Point(x,0)))
            self.assertFalse(n.sidewalk.contains(Point(x,0)))

    def test_acute_intersection_has_no_tiny_paving_spikes(self):
        n=self.network([path('main',[(-32,0),(32,0)]),path('branch',[(-32,-12),(32,12)])])
        self.assertTrue(n.sidewalk.is_valid)
        self.assertEqual(n.audit()['isolatedPavingFragments'],0)
        self.assertEqual(n.audit()['sidewalkAcrossRoadArea'],0)

    def test_triangulation_keeps_holes_and_exact_surface_area(self):
        angles=np.linspace(0,2*np.pi,32)
        n=self.network([path('loop',[(25*np.cos(t),25*np.sin(t)) for t in angles])])
        v,indices,_=n.mesh(n.asphalt)
        triangles=v[np.asarray(indices).reshape(-1,3)][:,:,[0,2]]
        a,b=triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0]
        areas=np.abs(a[:,0]*b[:,1]-a[:,1]*b[:,0])/2
        self.assertLess(abs(float(areas.sum())-n.asphalt.area)/n.asphalt.area,1e-6)
        self.assertFalse(n.asphalt.contains(Point(0,0)))
        self.assertGreater(float(areas.min()),0)

    def test_crosswalk_ramps_lower_the_curb_instead_of_adding_a_wall(self):
        n=self.network([path('main',[(-32,0),(32,0)]),path('cross',[(0,-32),(0,32)])])
        for r in n.crossings:
            for side in (-1,1):
                p=r['center']+r['normal']*side*(r['width']/2+.04)
                self.assertLess(n.sidewalk_height([p])[0],.085)
                outer=r['center']+r['normal']*side*(r['width']/2+1.8)
                self.assertAlmostEqual(n.sidewalk_height([outer])[0],.21)


if __name__=='__main__':unittest.main()
