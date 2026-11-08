"""Population rules shared by the map generator and existing manifest updates."""


def remove_children(world, visual):
    """Remove child residents and their complete visual hierarchies in place."""
    removed = {resident['id'] for resident in world['residents'] if resident.get('child')}
    descendants = {}
    for node in visual['nodes']:
        descendants.setdefault(node['parent'], []).append(node['id'])
    pending = list(removed)
    while pending:
        for ident in descendants.get(pending.pop(), []):
            if ident not in removed:
                removed.add(ident)
                pending.append(ident)
    world['residents'] = [resident for resident in world['residents'] if not resident.get('child')]
    visual['nodes'] = [node for node in visual['nodes'] if node['id'] not in removed]
    return removed
