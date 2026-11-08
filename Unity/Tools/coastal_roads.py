"""Keep ordinary streets and their sidewalks on dry, continuous embankments.

The traced coastline can cut through a road. Grade the terrain itself, including
the grid vertices beyond both sidewalk edges, rather than lifting asphalt alone.
Highways/ramps remain independent bridge decks; no seabed is filled under them.
"""
import numpy as np
import shapely as sh
from shapely import LineString
from street_network import StreetNetwork, width


def support_coastal_roads(grid, paths, origin, step):
    z, x = np.indices(grid.shape)
    points = sh.points(np.column_stack(((origin[0] + x * step).ravel(),
                                       (origin[1] + z * step).ravel())))
    minimum = np.full(grid.size, -np.inf)
    # One full cell diagonal guarantees support between samples on curved roads.
    margin = StreetNetwork.sidewalk_width + step * np.sqrt(2)
    for road in paths:
        if road['kind'] != 'street':
            continue
        line = LineString(road['xz'][:, [0, 2]])
        distance = sh.distance(points, line)
        shoulder = np.maximum(0, distance - width(road) / 2 - margin)
        target = 1.5 - shoulder * .25
        minimum = np.maximum(minimum, np.where(shoulder < 24, target, -np.inf))
    np.maximum(grid, minimum.reshape(grid.shape), out=grid)
