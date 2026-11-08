"""Prepare the licensed OBJ art for Unity. No runtime model generation.

Original downloads and licenses live in AssetLibrary/WeaponModels. Connected
pieces are assigned intact to the slide/bolt, magazine, or body. Source normals,
UVs, materials and triangles are retained. Small reload inserts supplement the
two static pistol grips; these are Open Territory additions, not artist assets.
"""
from collections import defaultdict
from pathlib import Path
import hashlib
import json
import math

ROOT = Path(__file__).resolve().parents[1]
LIBRARY = ROOT / "AssetLibrary/WeaponModels"
OUTPUT = ROOT / "OpenTerritory/Assets/Art/Weapons/Prepared"


def read_obj(path):
    positions, normals, uv, faces = [], [], [], []
    material = ""
    for line in path.read_text().splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            positions.append(tuple(map(float, fields[1:4])))
        elif fields[0] == "vn":
            normals.append(tuple(map(float, fields[1:4])))
        elif fields[0] == "vt":
            uv.append(tuple(map(float, fields[1:3])))
        elif fields[0] == "usemtl":
            material = fields[1]
        elif fields[0] == "f":
            faces.append(([[int(n) - 1 if n else -1 for n in v.split("/")]
                           for v in fields[1:]], material))
    parent = list(range(len(positions)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for face, _ in faces:
        for corner in face[1:]:
            parent[find(corner[0])] = find(face[0][0])
    components = defaultdict(list)
    for face in faces:
        components[find(face[0][0][0])].append(face)
    return positions, normals, uv, components.values()


def material_colors(path):
    colors = {}
    name = ""
    for line in path.read_text().splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "newmtl":
            name = fields[1]
        elif fields[0] == "Kd":
            colors[name] = list(map(float, fields[1:4]))
    return colors


def prepare(weapon, source, author, url, scale, offset, muzzle, support):
    positions, normals, uv, components = read_obj(source)
    parts = {}
    source_triangles = 0

    def transform(p, direction=False):
        # Quaternius uses +X forward. Kenney's compact blaster uses -Z.
        v = (-p[2], p[1], p[0]) if author == "Quaternius" else (-p[0], p[1], -p[2])
        return v if direction else tuple(v[i] * scale + offset[i] for i in range(3))

    def part(name, material):
        key = (name, material)
        if key not in parts:
            parts[key] = dict(name=name, material=material, vertices=[], normals=[], uv=[], triangles=[])
        return parts[key]

    for component in components:
        points = [positions[c[0]] for face, _ in component for c in face]
        low = [min(p[i] for p in points) for i in range(3)]
        high = [max(p[i] for p in points) for i in range(3)]
        group = "Body"
        if weapon == "sidearm" and low[1] >= .69 and high[0] < 1.58:
            group = "Action"
        elif weapon == "carbine":
            if low[1] < -.6:
                group = "Magazine"
            elif high[0] < 0 and low[1] > .5 and high[1] < .76:
                group = "Action"
        elif weapon == "pulse" and low[2] < -.25:
            group = "Action"
        for face, material in component:
            target = part(group, material)
            for j in range(1, len(face) - 1):
                source_triangles += 1
                for corner in (face[0], face[j], face[j + 1]):
                    target["triangles"].append(len(target["vertices"]) // 3)
                    target["vertices"].extend(transform(positions[corner[0]]))
                    target["normals"].extend(transform(normals[corner[2]], True))
                    target["uv"].extend(uv[corner[1]] if corner[1] >= 0 else (0, 0))

    def insert_box(center, size):
        target = part("Magazine", "ReloadInsert")
        for axis in range(3):
            u, v = (axis + 1) % 3, (axis + 2) % 3
            for sign in (-1, 1):
                corners = []
                normal = [0, 0, 0]
                normal[axis] = sign
                for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    p = list(center)
                    p[axis] += sign * size[axis] / 2
                    p[u] += a * size[u] / 2
                    p[v] += b * size[v] / 2
                    corners.append(p)
                order = (0, 1, 2, 0, 2, 3) if sign > 0 else (0, 2, 1, 0, 3, 2)
                for i in order:
                    target["triangles"].append(len(target["vertices"]) // 3)
                    target["vertices"].extend(corners[i])
                    target["normals"].extend(normal)
                    target["uv"].extend((0, 0))

    if weapon == "sidearm":
        insert_box((0, -.103, -.043), (.032, .106, .051))
        insert_box((0, -.161, -.043), (.048, .013, .078))
    elif weapon == "pulse":
        insert_box((0, -.084, -.018), (.060, .094, .058))
        insert_box((0, -.133, -.018), (.094, .012, .093))

    colors = material_colors(source.with_suffix(".mtl"))
    colors["ReloadInsert"] = [.021, .029, .032]
    data = dict(id=weapon, source=source.name, author=author, sourceUrl=url,
                sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(), license="CC0-1.0",
                sourceTriangles=source_triangles, parts=list(parts.values()),
                colors=[dict(name=k, rgb=v) for k, v in colors.items()],
                muzzle=muzzle, ejection=[.042, .076, .064] if weapon == "sidearm" else [.031, .033, .105],
                rightGrip=[0, -.071, -.012], supportGrip=support,
                textured=author == "Kenney")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    path = OUTPUT / (weapon + ".json")
    path.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n")
    triangles = sum(len(p["triangles"]) // 3 for p in parts.values())
    assert all(any(p["name"] == name for p in parts.values()) for name in ("Body", "Action", "Magazine"))
    assert triangles == source_triangles + (24 if weapon != "carbine" else 0)
    assert all(math.isfinite(n) for p in parts.values() for n in p["vertices"])
    print(f"{weapon}: {source_triangles} original triangles + {triangles-source_triangles} insert triangles; {len(parts)} material batches")
    return dict(id=weapon, author=author, model=source.name, sourceUrl=url,
                sourceSha256=data["sourceSha256"], sourceTriangles=source_triangles,
                triangles=triangles, preparedSha256=hashlib.sha256(path.read_bytes()).hexdigest())


def main():
    quaternius = "https://quaternius.com/packs/ultimategun.html"
    kenney = "https://kenney.nl/assets/blaster-kit"
    records = [
        prepare("sidearm", LIBRARY / "Quaternius/Pistol_4.obj", "Quaternius", quaternius,
                .155, (0, -.07875, -.0213), [0, .070, .229], [-.061, -.071, .018]),
        prepare("carbine", LIBRARY / "Quaternius/AssaultRifle2_2.obj", "Quaternius", quaternius,
                .16, (0, -.080, -.006), [0, .024, .518], [-.030, -.011, .248]),
        prepare("pulse", LIBRARY / "Kenney/Models/OBJ format/blaster-b.obj", "Kenney", kenney,
                .85, (0, .0395, .0645), [0, .051, .287], [-.075, -.071, .016]),
    ]
    (LIBRARY / "prepared-manifest.json").write_text(json.dumps(records, indent=2) + "\n")


if __name__ == "__main__":
    main()
