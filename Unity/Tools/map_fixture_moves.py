"""Keep relocated, usable benches and their collision in the offline export."""
import copy


def apply_fixture_moves(visual, world, plan):
    nodes = {node['id']: node for node in visual['nodes']}
    changed_nodes, changed_solids = set(), set()
    for move in plan['moves']:
        ident = move['id']
        if ident < 0:
            continue  # Native road lights use the same plan in their owning builder.
        node = nodes[ident]
        assert node['dynamic'] and node['parent'] == -1
        delta = {key: move['to'][key] - node['position'][key] for key in 'xyz'}
        node['position'] = copy.deepcopy(move['to'])
        changed_nodes.add(ident)
        for solid in world['solids']:
            if solid['owner'] != ident:
                continue
            solid['x'] += delta['x']; solid['z'] += delta['z']
            solid['bottom'] += delta['y']; solid['top'] += delta['y']
            changed_solids.add(solid['index'])
        seat = next(seat for seat in world['seats'] if seat['id'] == ident)
        seat['x'], seat['z'] = move['to']['x'], move['to']['z']
        seat['position'] = copy.deepcopy(move['to'])
    return changed_nodes, changed_solids
