"""Check imported lane direction and the full vehicle envelope against solids."""
import collections
import json
import math
from pathlib import Path

root = Path(__file__).resolve().parents[1]
world = json.loads((root / "OpenTerritory/Assets/Resources/world.json").read_text(encoding="utf-8"))
cells = collections.defaultdict(list)
for solid in world["solids"]:
    if not solid.get("active", True) or solid.get("owner") or solid["top"] - solid["bottom"] < .08:
        continue
    for x in range(math.floor((solid["x"] - solid["w"] / 2) / 12), math.floor((solid["x"] + solid["w"] / 2) / 12) + 1):
        for z in range(math.floor((solid["z"] - solid["d"] / 2) / 12), math.floor((solid["z"] + solid["d"] / 2) / 12) + 1):
            cells[x, z].append(solid)

results = []
routes = [(car, "main", car["route"]) for car in world["vehicles"] if car["auto"]]
routes += [(car, "alternate", car["alternateRoute"]) for car in world["vehicles"] if car["auto"] and car.get("oneWay") and car.get("alternateRoute")]
for car, variant, points in routes:
    position = car["position"]
    closest, heading, conflicts = math.inf, 1, []
    for index, (a, b) in enumerate(zip(points, points[1:] + points[:1])):
        dx, dz = b["x"] - a["x"], b["z"] - a["z"]
        length = math.hypot(dx, dz)
        if length < .001:
            continue
        ux, uz = dx / length, dz / length
        fraction = max(0, min(1, ((position["x"] - a["x"]) * dx + (position["z"] - a["z"]) * dz) / length**2))
        distance = math.hypot(position["x"] - a["x"] - fraction * dx, position["z"] - a["z"] - fraction * dz)
        if distance < closest:
            closest = distance
            heading = math.sin(car["yaw"]) * ux + math.cos(car["yaw"]) * uz
        x, y, z = ((a[axis] + b[axis]) / 2 for axis in ["x", "y", "z"])
        radius, seen = max(car["width"], car["length"]) / 2 + 1, set()
        for cx in range(math.floor((x - radius) / 12), math.floor((x + radius) / 12) + 1):
            for cz in range(math.floor((z - radius) / 12), math.floor((z + radius) / 12) + 1):
                for solid in cells[cx, cz]:
                    if solid["index"] in seen:
                        continue
                    seen.add(solid["index"])
                    if solid["bottom"] >= y + 1.95 or solid["top"] <= y + .25:
                        continue
                    hit = True
                    for ax, az in [(1, 0), (0, 1), (ux, uz), (uz, -ux)]:
                        half = abs(ax * ux + az * uz) * car["length"] / 2 + abs(ax * uz - az * ux) * car["width"] / 2 + abs(ax) * solid["w"] / 2 + abs(az) * solid["d"] / 2
                        if abs(ax * (x - solid["x"]) + az * (z - solid["z"])) >= half - .025:
                            hit = False
                            break
                    if hit:
                        conflicts.append({"segment": index, "x": round(x, 2), "z": round(z, 2), "solid": solid["index"]})
    results.append({"id": car["id"], "kind": car["kind"], "axis": car.get("axis", "bus"), "variant": variant, "samples": len(points),
                    "spawnDistance": closest, "headingDot": heading, "staticConflicts": len(conflicts), "examples": conflicts[:8]})
(root / "Reports/route-audit.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
for result in results:
    print(result["id"], result["kind"], result["axis"], result["variant"], "spawn", round(result["spawnDistance"], 3), "heading", round(result["headingDot"], 3), "solid intersections", result["staticConflicts"])

raise SystemExit(2 if any(result["staticConflicts"] for result in results) else 0)
