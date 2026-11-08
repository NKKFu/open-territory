using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    // Offline furnishing: shared meshes, authored collisions and real rooms.
    // Runtime never loads the web donor or generates buildings.
    public static class CityHomeBuilder
    {
        const string AssetPath = "Assets/Imported/CityHomes.asset";
        const float Floor = .18f, Storey = 4.32f;
        static readonly HashSet<int> apartments = new HashSet<int>();
        static readonly List<int> removedSolids = new List<int>();
        public static int[] RemovedSolidSlots => removedSolids.ToArray();
        static readonly Dictionary<string, Template> templates = new Dictionary<string, Template>();
        static readonly List<CityHomeCatalog.Home> homes = new List<CityHomeCatalog.Home>();
        static readonly List<CityHomeCatalog.Room> rooms = new List<CityHomeCatalog.Room>();
        static readonly List<CityHomeCatalog.InteriorDoor> doors = new List<CityHomeCatalog.InteriorDoor>();
        static readonly List<CityLighting.Fixture> fixtures = new List<CityLighting.Fixture>();
        static readonly List<CityObjectLod> lods = new List<CityObjectLod>();
        static readonly Color Cream = new Color(.91f, .86f, .74f), Wood = new Color(.49f, .30f, .16f), Dark = new Color(.065f, .10f, .11f), Metal = new Color(.42f, .48f, .47f);
        static readonly Color[] Accents = { new Color(.18f, .46f, .43f), new Color(.66f, .32f, .21f), new Color(.27f, .39f, .54f), new Color(.65f, .52f, .27f) };
        static CityAssetLibrary library;
        static Material pigment, glass;
        static Mesh cube;
        static int propCount;
        sealed class Template { public Mesh mesh; public Bounds[] collision; }

        public static bool Housing(PlaceData p) => p.kind == "home" || p.kind == "residence";
        public static bool IsApartment(WorldData world,int index) => Enumerable.Range(0,world.places.Length).Where(i=>world.places[i].kind=="residence")
            .OrderBy(i=>Vector2.Distance(new Vector2(world.places[i].x,world.places[i].z),new Vector2(world.spawn.x,world.spawn.z))).Take(12).Contains(index);
        static string Label(int i) => i < 17 ? "Reused building-" + i : "Reused residential interior " + (i - 17);
        static Matrix4x4 Matrix(float[] a)
        { var m = new Matrix4x4(); for (int i = 0; i < 4; i++) m.SetColumn(i, new Vector4(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3])); return m; }
        static Bounds BoundsOf(GeometryData g)
        {
            var b = new Bounds(new Vector3(g.positions[0], g.positions[1], g.positions[2]), Vector3.zero);
            for (int i = 3; i < g.positions.Length; i += 3) b.Encapsulate(new Vector3(g.positions[i], g.positions[i + 1], g.positions[i + 2])); return b;
        }
        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))); return result;
        }
        // Called on an in-memory copy of the donor. Raw map files stay intact.
        public static void PrepareImport(VisualData visual, WorldData world)
        {
            apartments.Clear(); removedSolids.Clear();
            foreach (int index in Enumerable.Range(0, world.places.Length).Where(i => world.places[i].kind == "residence")
                .OrderBy(i => Vector2.Distance(new Vector2(world.places[i].x, world.places[i].z), new Vector2(world.spawn.x, world.spawn.z))).Take(12)) apartments.Add(index);
            var byLabel = Enumerable.Range(0, world.places.Length).Where(i => Housing(world.places[i])).ToDictionary(Label);
            var geometry = visual.geometries.Select(BoundsOf).ToArray();
            foreach (var node in visual.nodes)
            {
                if (node.geometry < 0 || !byLabel.TryGetValue(node.name ?? "", out int index)) continue;
                var p = world.places[index];
                if (apartments.Contains(index)) { node.geometry = -1; node.instances = Array.Empty<InstanceData>(); continue; }
                var toRoom = Matrix4x4.TRS(new Vector3(p.x, p.elevation, p.z), Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0), Vector3.one).inverse;
                var transform = Matrix4x4.TRS(node.position, node.rotation, node.scale);
                node.instances = node.instances.Where(instance => {
                    Bounds b = TransformBounds(geometry[node.geometry], toRoom * transform * Matrix(instance.matrix));
                    bool interior = Mathf.Abs(b.center.x) < p.lotWidth * .5f - .42f && Mathf.Abs(b.center.z) < p.lotDepth * .5f - .42f
                        && b.min.y >= .159f && b.max.y < 5.015f;
                    return !interior;
                }).ToArray();
                if (node.instances.Length == 0) node.geometry = -1;
            }
            for(int slot=0;slot<world.solids.Length;slot++)
            {
                var s=world.solids[slot];
                if (!s.active) continue;
                for (int i = 0; i < world.places.Length; i++)
                {
                    var p = world.places[i]; if (!Housing(p)) continue;
                    Vector3 local = CityLighting.LocalPoint(p, new Vector3(s.x, (s.bottom + s.top) * .5f, s.z));
                    bool inside = Mathf.Abs(local.x) < p.lotWidth * .5f - .42f && Mathf.Abs(local.z) < p.lotDepth * .5f - .42f;
                    bool oldFurniture = inside && s.bottom >= p.elevation + .159f && s.top < p.elevation + 4.99f;
                    bool oldApartmentShell = apartments.Contains(i) && Mathf.Abs(local.x) <= p.lotWidth * .5f + .45f
                        && Mathf.Abs(local.z) <= p.lotDepth * .5f + .45f && s.top > p.elevation + .20f && s.bottom < p.elevation + 25;
                    if (oldFurniture || oldApartmentShell) { s.active = false; removedSolids.Add(slot); break; }
                }
            }
            // Commercial ceiling panels used to share one renderer. Separate each
            // fixture so damaging one never removes its neighbour or the ceiling.
            var lampIds = new HashSet<int>(world.streetLamps.SelectMany(l => visual.nodes.Where(n => n.id == l.bulb).SelectMany(n => n.materials)));
            var extra = new List<VisualNode>(); int nextId = visual.nodes.Max(n => n.id) + 1;
            foreach (var node in visual.nodes)
            {
                if (node.geometry < 0 || node.dynamic || !node.materials.Any(lampIds.Contains)) continue;
                var keep = new List<InstanceData>();
                foreach (var instance in node.instances)
                {
                    var m = Matrix4x4.TRS(node.position, node.rotation, node.scale) * Matrix(instance.matrix);
                    var shape = geometry[node.geometry]; Vector3 point = m.MultiplyPoint3x4(shape.center);
                    float height = m.MultiplyVector(Vector3.up * shape.size.y).magnitude;
                    if (height > .2f || !world.places.Any(p => CityLighting.Inside(p, point - Vector3.up * .3f) && point.y - p.elevation > 4.5f)) { keep.Add(instance); continue; }
                    extra.Add(new VisualNode { id = nextId++, parent = node.parent, name = "Luminária independente • " + node.id,
                        geometry = node.geometry, materials = node.materials, position = node.position, rotation = node.rotation, scale = node.scale,
                        dynamic = true, instances = new[] { instance }, castShadow = false });
                }
                node.instances = keep.ToArray(); if (keep.Count == 0) node.geometry = -1;
            }
            visual.nodes = visual.nodes.Concat(extra).ToArray();
        }

        public static void Build(Transform city, WorldData world, Dictionary<int, Transform> nodes)
        {
            templates.Clear(); homes.Clear(); rooms.Clear(); doors.Clear(); fixtures.Clear(); lods.Clear(); propCount = 0;
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, AssetPath);
            pigment = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Interiores • cores e madeira", enableInstancing = true };
            pigment.SetColor("_Color", Color.white); pigment.SetFloat("_Glossiness", .18f); pigment.SetFloat("_Cull", 2); AssetDatabase.AddObjectToAsset(pigment, library);
            glass = new Material(Shader.Find("OpenTerritory/CityWindow")) { name = "Apartamentos • vidro" }; glass.SetColor("_Color", new Color(.60f, .79f, .77f, .18f)); AssetDatabase.AddObjectToAsset(glass, library);
            var sample = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = sample.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(sample);
            var catalog = new GameObject("Casas • cômodos, mobiliário e apartamentos").AddComponent<CityHomeCatalog>(); catalog.transform.SetParent(city, false);
            var lighting = UnityEngine.Object.FindFirstObjectByType<CityLighting>();
            if (lighting)
            {
                foreach (var f in lighting.fixtures.Where(f => f.place >= 0 && Housing(world.places[f.place])))
                { if (f.source) UnityEngine.Object.DestroyImmediate(f.source.gameObject); if (f.lens) f.lens.enabled = false; }
            }
            foreach (var data in world.destructibles)
                if (nodes.TryGetValue(data.id, out var root) && world.places.Any(p => Housing(p) && p.Contains(root.position, .55f))) root.gameObject.SetActive(false);
            for (int i = 0; i < world.places.Length; i++)
            {
                var p = world.places[i]; if (!Housing(p)) continue;
                var root = new GameObject((apartments.Contains(i) ? "Edifício • " : "Casa • ") + p.name).transform;
                root.SetParent(catalog.transform, false); root.position = new Vector3(p.x, p.elevation, p.z); root.rotation = Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0);
                if (apartments.Contains(i)) Apartment(root, p, i); else House(root, p, i);
                if (lighting)
                {
                    var existing = lighting.rooms.FirstOrDefault(r => r.place == i);
                    if (existing != null) {existing.surfaces = existing.surfaces.Concat(root.GetComponentsInChildren<Renderer>()).ToArray();existing.height=apartments.Contains(i)?9.0f:5.12f;}
                }
                var home = homes[homes.Count - 1];
                AddLod(root, apartments.Contains(i)?"building":"interior", apartments.Contains(i) ? 600 : 160, false);
            }
            catalog.homes = homes.ToArray(); catalog.rooms = rooms.ToArray(); catalog.doors = doors.ToArray(); catalog.removedSolids = removedSolids.ToArray();
            CityApartmentEntranceBuilder.Fit(world, UnityEngine.Object.FindFirstObjectByType<WorldRegistry>());
            if (lighting) lighting.fixtures = lighting.fixtures.Where(f => f.place < 0 || !Housing(world.places[f.place])).Concat(fixtures).ToArray();
            // Existing public lamps and doors are registered by the runtime damage service.
            var manager = UnityEngine.Object.FindFirstObjectByType<CityLodManager>(); if (manager) manager.objects = manager.objects.Where(l => l).Concat(lods).Distinct().ToArray();
            CityHomeDecorBuilder.Bake();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../Reports/interiors");
            File.WriteAllText("../Reports/interiors/import.json", JsonUtility.ToJson(new ImportReport { houses = homes.Count(h => h.units == 1), apartmentBuildings = apartments.Count,
                apartments = homes.Where(h => h.units > 1).Sum(h => h.units), rooms = rooms.Count, props = propCount, interiorDoors = doors.Count, sharedMeshes = templates.Count,
                removedSolids = removedSolids.Count, apartmentPlaces = homes.Where(h => h.units > 1).Select(h => h.place).ToArray() }, true));
            Debug.Log("CITY_HOMES_BAKED houses=" + homes.Count(h => h.units == 1) + " apartments=" + homes.Where(h => h.units > 1).Sum(h => h.units) + " props=" + propCount);
        }
        [Serializable] sealed class ImportReport { public int houses, apartmentBuildings, apartments, rooms, props, interiorDoors, sharedMeshes, removedSolids; public int[] apartmentPlaces; }

        static void House(Transform root, PlaceData p, int place)
        {
            int palette = place % 4; float w = p.lotWidth * .5f - .24f, d = p.lotDepth * .5f - .24f;
            Color wall = Color.Lerp(Cream, Accents[palette], .12f);
            foreach (int side in new[] { -1, 1 })
            {
                Partition(root, new Vector3(side * CityHomeLayoutBuilder.HallHalf, Floor, 0), Quaternion.Euler(0, 90, 0), d * 2, 4.80f, new[] { -3.25f, 3.25f }, 1.5f, wall, place);
                Block(root, "Divisória entre os cômodos", new Vector3(side * (w + CityHomeLayoutBuilder.HallHalf) * .5f, 2.58f, 0), new Vector3(w - CityHomeLayoutBuilder.HallHalf, 4.80f, .16f), wall);
            }
            AddRoom(root, place, 0, "Sala", new Vector3(-(w + CityHomeLayoutBuilder.HallHalf) * .5f, Floor, d * .5f), new Vector3(w - CityHomeLayoutBuilder.HallHalf, 4.8f, d), new Vector3(-CityHomeLayoutBuilder.HallHalf, Floor, 3.25f));
            AddRoom(root, place, 0, "Quarto", new Vector3(-(w + CityHomeLayoutBuilder.HallHalf) * .5f, Floor, -d * .5f), new Vector3(w - CityHomeLayoutBuilder.HallHalf, 4.8f, d), new Vector3(-CityHomeLayoutBuilder.HallHalf, Floor, -3.25f));
            AddRoom(root, place, 0, "Cozinha", new Vector3((w + CityHomeLayoutBuilder.HallHalf) * .5f, Floor, d * .5f), new Vector3(w - CityHomeLayoutBuilder.HallHalf, 4.8f, d), new Vector3(CityHomeLayoutBuilder.HallHalf, Floor, 3.25f));
            AddRoom(root, place, 0, "Banheiro e lavanderia", new Vector3((w + CityHomeLayoutBuilder.HallHalf) * .5f, Floor, -d * .5f), new Vector3(w - CityHomeLayoutBuilder.HallHalf, 4.8f, d), new Vector3(CityHomeLayoutBuilder.HallHalf, Floor, -3.25f));
            Living(root, new Vector3(-5.05f, Floor, 3.15f), palette, place, false);
            Bedroom(root, new Vector3(-5, Floor, -3.55f), palette, place, false);
            Kitchen(root, new Vector3(5.15f, Floor, 3.35f), palette, place, false);
            Bathroom(root, new Vector3(5.05f, Floor, -3.55f), palette, place, false);
            foreach (var room in rooms.Where(r => r.place == place).ToArray())
            {
                Vector3 at = root.InverseTransformPoint(room.center); at.y = 4.86f; Lamp(root, at, place, palette);
            }
            homes.Add(new CityHomeCatalog.Home { place = place, root = root, name = p.name });
        }

        static void Apartment(Transform root, PlaceData p, int place)
        {
            int palette = place % 4; float w = p.lotWidth * .5f, d = p.lotDepth * .5f;
            var windows = UnityEngine.Object.FindFirstObjectByType<CityWindows>();
            if (windows)
            {
                var old = windows.transform.Find("Janelas • " + place); if (old) old.gameObject.SetActive(false);
                windows.windows = windows.windows.Where(v => v.place != place).ToArray(); windows.glazing = windows.glazing.Where(v => v.place != place).ToArray();
            }
            Color facade = Color.Lerp(Cream, Accents[palette], .28f);
            Block(root, "Base do edifício", new Vector3(0, .08f, 0), new Vector3(w * 2, .20f, d * 2), Cream, true, true);
            // First-floor slab leaves the stairwell physically open.
            Block(root, "Laje esquerda", new Vector3(-(w + 1.75f) * .5f, Storey + .08f, 0), new Vector3(w - 1.75f, .20f, d * 2), Cream);
            Block(root, "Laje direita", new Vector3((w + 1.75f) * .5f, Storey + .08f, 0), new Vector3(w - 1.75f, .20f, d * 2), Cream);
            Block(root, "Laje do corredor", new Vector3(0, Storey + .08f, (d + .30f) * .5f), new Vector3(3.50f, .20f, d - .30f), Cream);
            Block(root, "Cobertura", new Vector3(0, Storey * 2 + .20f, 0), new Vector3(w * 2 + .5f, .30f, d * 2 + .5f), Color.Lerp(Accents[palette], Dark, .5f));
            for (int floor = 0; floor < 2; floor++)
            {
                float y = floor * Storey + Floor;
                WindowWall(root, new Vector3(0, y, -d), Quaternion.identity, w * 2, 4.14f, new[] { -5.7f, 5.7f }, facade, place);
                WindowWall(root, new Vector3(-w, y, 0), Quaternion.Euler(0, 90, 0), d * 2, 4.14f, new[] { -3.7f, 3.7f }, facade, place);
                WindowWall(root, new Vector3(w, y, 0), Quaternion.Euler(0, 90, 0), d * 2, 4.14f, new[] { -3.7f, 3.7f }, facade, place);
                // Facade windows sit either side of the shared entrance.
                WindowWall(root, new Vector3(-5.5f, y, d), Quaternion.identity, 9, 4.14f, new[] { 0f }, facade, place);
                WindowWall(root, new Vector3(5.5f, y, d), Quaternion.identity, 9, 4.14f, new[] { 0f }, facade, place);
                if (floor == 0)
                {
                    Block(root, "Entrada • lateral", new Vector3(-.85f, y + 2.07f, d), new Vector3(.30f, 4.14f, .24f), facade);
                    Block(root, "Entrada • lateral", new Vector3(.85f, y + 2.07f, d), new Vector3(.30f, 4.14f, .24f), facade);
                    Block(root, "Entrada • verga", new Vector3(0, y + 3.73f, d), new Vector3(1.4f, .82f, .24f), facade);
                }
                else WindowWall(root, new Vector3(0, y, d), Quaternion.identity, 2, 4.14f, new[] { 0f }, facade, place, 1.1f);
                foreach (int side in new[] { -1, 1 })
                {
                    int unit = (floor + 1) * 100 + (side < 0 ? 1 : 2);
                    var flat = new GameObject("Apartamento " + unit).transform; flat.SetParent(root, false); flat.localPosition = new Vector3(side * 5.80f, y, 0);
                    Color wall = Color.Lerp(Cream, Accents[(palette + floor) % 4], .10f);
                    Partition(root, new Vector3(side * 1.75f, y, 0), Quaternion.Euler(0, 90, 0), d * 2, 4.14f, new[] { -3.3f }, 1.4f, wall, place);
                    NumberPlate(doors[doors.Count-1].leaf,unit,palette);
                    // Each flat has a living/kitchen room, a bedroom and a bathroom.
                    Partition(flat, new Vector3(0, 0, -.7f), Quaternion.identity, 7.9f, 4.14f, new[] { side * -2.45f }, 1.4f, wall, place);
                    Partition(flat, new Vector3(side * 1.2f, 0, -4.0f), Quaternion.Euler(0, 90, 0), 6.35f, 4.14f, new[] { -1.2f }, 1.3f, wall, place);
                    Living(flat, new Vector3(-side * 1.35f, 0, 3.75f), (palette + floor + (side > 0 ? 1 : 0)) % 4, place, true);
                    Kitchen(flat, new Vector3(side * 2.3f, 0, 3.65f), palette, place, true);
                    Bedroom(flat, new Vector3(-side * .9f, 0, -4.15f), palette, place, true);
                    Bathroom(flat, new Vector3(side * 2.4f, 0, -4.1f), palette, place, true);
                    AddRoom(flat, place, unit, "Sala e cozinha", new Vector3(0, 0, 3.25f), new Vector3(7.9f, 4.14f, 7.9f), new Vector3(-side * 4.05f, 0, 3.3f));
                    AddRoom(flat, place, unit, "Quarto", new Vector3(-side * 1.35f, 0, -4.1f), new Vector3(5.15f, 4.14f, 6.35f), new Vector3(-side * 2.45f, 0, -.7f));
                    AddRoom(flat, place, unit, "Banheiro", new Vector3(side * 2.5f, 0, -4.1f), new Vector3(2.7f, 4.14f, 6.35f), new Vector3(side * 1.2f, 0, -2.8f));
                    Lamp(flat, new Vector3(0, 3.96f, 3.1f), place, palette); Lamp(flat, new Vector3(-side * 1.3f, 3.96f, -4), place, palette); Lamp(flat, new Vector3(side * 2.5f, 3.96f, -4), place, palette);
                }
                Lamp(root, new Vector3(0, y + 3.96f, 3.2f), place, palette);
            }
            // Two flights, 27 cm tread, 18 cm rise, with a turning landing.
            // The .32 m step setting can climb this without jumping.
            var route = new List<Vector3> { new Vector3(0, Floor + .04f, 1.4f), new Vector3(-.9f, Floor + .04f, -.35f) };
            for (int i = 0; i < 12; i++)
            {
                float height = (i + 1) * .18f;
                Block(root, "Escada • primeiro lance", new Vector3(-.9f, Floor + height * .5f, -.62f - i * .27f), new Vector3(1.5f, height, .28f), Cream);
                route.Add(new Vector3(-.9f, Floor + height + .04f, -.62f - i * .27f));
            }
            Block(root, "Patamar de retorno", new Vector3(0, Floor + 2.06f, -4.40f), new Vector3(3.3f, .20f, 1.40f), Cream);
            route.Add(new Vector3(-.9f, Floor + 2.20f, -4.4f)); route.Add(new Vector3(.9f, Floor + 2.20f, -4.4f));
            for (int i = 0; i < 12; i++)
            {
                float top = 2.16f + (i + 1) * .18f;
                // A thin tread keeps the ground-floor corridor open beneath.
                Block(root, "Escada • segundo lance", new Vector3(.9f, Floor + top - .09f, -3.59f + i * .27f), new Vector3(1.5f, .18f, .28f), Cream);
                route.Add(new Vector3(.9f, Floor + top + .04f, -3.59f + i * .27f));
            }
            // Complete the upper landing so there is no gap at the last step.
            Block(root, "Patamar superior", new Vector3(.9f, Storey + .08f, -.10f), new Vector3(1.5f, .20f, .70f), Cream);
            route.Add(new Vector3(.9f, Storey + Floor + .04f, .25f)); route.Add(new Vector3(0, Storey + Floor + .04f, 2.0f));
            // Guardrails do not block either flight or the landing.
            foreach (float x in new[] { -1.68f, 1.68f })
            {
                Block(root, "Guarda-corpo do vão", new Vector3(x, Storey + Floor + .48f, -2.65f), new Vector3(.09f, .96f, 4.7f), Metal);
            }
            for (int i = 0; i < 12; i++)
            {
                float z = -.62f - i * .27f;
                Block(root, "Corrimão do primeiro lance", new Vector3(-1.67f, Floor + (i + 1) * .18f + .9f, z), new Vector3(.06f, .06f, .32f), Wood, false);
                Block(root, "Corrimão do segundo lance", new Vector3(1.67f, Floor + 2.16f + (i + 1) * .18f + .9f, -3.59f + i * .27f), new Vector3(.06f, .06f, .32f), Wood, false);
            }
            homes.Add(new CityHomeCatalog.Home { place = place, root = root, units = 4, name = "Apartamentos " + p.name,
                stairRoute = route.Select(root.TransformPoint).ToArray() });
        }

        static void AddRoom(Transform root, int place, int unit, string name, Vector3 center, Vector3 size, Vector3 entrance)
        {
            rooms.Add(new CityHomeCatalog.Room { place = place, unit = unit, name = name, center = root.TransformPoint(center), size = size, entrance = root.TransformPoint(entrance) });
            bool bathroom=name.StartsWith("Banheiro");
            Color floor=bathroom?new Color(.62f,.73f,.69f):name=="Quarto"?new Color(.53f,.38f,.25f):new Color(.76f,.68f,.54f);
            Block(root,"Piso • "+name,center+Vector3.up*.007f,new Vector3(size.x-.08f,.012f,size.z-.08f),floor,false);
        }
        static void NumberPlate(Transform door,int number,int palette)
        {
            var g=new Geometry();g.Box(Vector3.zero,new Vector3(.46f,.24f,.035f),Accents[palette]);
            string digits=number.ToString();int[] codes={63,6,91,79,102,109,125,7,127,111};
            for(int d=0;d<digits.Length;d++)for(int bit=0;bit<7;bit++)
            {
                if((codes[digits[d]-'0']&(1<<bit))==0)continue;
                Vector3 center;Vector3 size;
                if(bit==0||bit==3||bit==6){center=new Vector3((d-1)*.125f,bit==0?.075f:bit==3?-.075f:0,.025f);size=new Vector3(.067f,.012f,.012f);}
                else {bool right=bit==1||bit==2;bool upper=bit==1||bit==5;center=new Vector3((d-1)*.125f+(right?.039f:-.039f),upper?.038f:-.038f,.025f);size=new Vector3(.012f,.061f,.012f);}
                g.Box(center,size,Cream);
            }
            Mesh mesh=g.Save("Número • "+number);
            foreach(int side in new[]{-1,1}){var go=new GameObject("Apartamento "+number);go.transform.SetParent(door,false);go.transform.localPosition=new Vector3(-.7f,2.42f,side*.09f);go.transform.localRotation=Quaternion.Euler(0,side<0?180:0,0);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=pigment;}
        }
        static void Partition(Transform parent, Vector3 point, Quaternion rotation, float width, float height, float[] openings, float doorWidth, Color color, int place)
        {
            var wall = new GameObject("Parede • passagens e portas").transform; wall.SetParent(parent, false); wall.localPosition = point; wall.localRotation = rotation;
            float start = -width * .5f;
            foreach (float at in openings.OrderBy(x => x))
            {
                float end = at - doorWidth * .5f;
                if (end > start) Block(wall, "Divisória", new Vector3((start + end) * .5f, height * .5f, 0), new Vector3(end - start, height, .16f), color);
                Block(wall, "Verga", new Vector3(at, (height + 3.05f) * .5f, 0), new Vector3(doorWidth, height - 3.05f, .16f), color);
                var door = Prop(wall, "door", new Vector3(at + doorWidth * .5f, 0, 0), 0, place);
                door.transform.localScale = new Vector3(doorWidth / 1.4f, 1, 1);
                doors.Add(new CityHomeCatalog.InteriorDoor { leaf = door.transform, width = doorWidth });
                start = at + doorWidth * .5f;
            }
            if (start < width * .5f) Block(wall, "Divisória", new Vector3((start + width * .5f) * .5f, height * .5f, 0), new Vector3(width * .5f - start, height, .16f), color);
        }
        static void WindowWall(Transform parent, Vector3 point, Quaternion rotation, float width, float height, float[] openings, Color color, int place, float windowWidth = 2.4f)
        {
            var root = new GameObject("Fachada • janelas abertas").transform; root.SetParent(parent, false); root.localPosition = point; root.localRotation = rotation;
            float start = -width * .5f;
            foreach (float at in openings.OrderBy(x => x))
            {
                float end = at - windowWidth * .5f;
                if (end > start) Block(root, "Parede", new Vector3((start + end) * .5f, height * .5f, 0), new Vector3(end - start, height, .24f), color);
                Block(root, "Peitoril", new Vector3(at, .72f, 0), new Vector3(windowWidth, 1.44f, .24f), color);
                Block(root, "Verga da janela", new Vector3(at, (3.20f + height) * .5f, 0), new Vector3(windowWidth, height - 3.2f, .24f), color);
                foreach (int side in new[] { -1, 1 }) Block(root, "Caixilho", new Vector3(at + side * windowWidth * .5f, 2.32f, 0), new Vector3(.075f, 1.90f, .30f), Wood, false);
                Block(root, "Caixilho", new Vector3(at, 2.32f, 0), new Vector3(.055f, 1.76f, .08f), Wood, false);
                var pane = Block(root, "Vidro", new Vector3(at, 2.32f, 0), new Vector3(windowWidth, 1.76f, .035f), Color.white);
                pane.GetComponent<MeshRenderer>().sharedMaterial = glass;
                var global = UnityEngine.Object.FindFirstObjectByType<CityWindows>();
                if (global)
                {
                    global.windows = global.windows.Append(new CityWindows.Window { place = place, center = pane.transform.position, outward = root.forward, width = windowWidth, height = 1.76f }).ToArray();
                    global.glazing = global.glazing.Append(new CityWindows.Glazing { place = place, renderer = pane.GetComponent<Renderer>() }).ToArray();
                }
                start = at + windowWidth * .5f;
            }
            if (start < width * .5f) Block(root, "Parede", new Vector3((start + width * .5f) * .5f, height * .5f, 0), new Vector3(width * .5f - start, height, .24f), color);
        }
        static GameObject Block(Transform parent, string name, Vector3 point, Vector3 size, Color color, bool collision = true, bool ground = false)
        {
            string key = "block:" + ColorUtility.ToHtmlStringRGB(color);
            if (!templates.TryGetValue(key, out var template))
            { var geometry = new Geometry(); geometry.Box(Vector3.zero, Vector3.one, color); template = new Template { mesh = geometry.Save(key) }; templates[key] = template; }
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = point; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = template.mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pigment;
            go.layer = ground ? CityContext.GroundLayer : CityContext.WorldLayer;
            if (collision) go.AddComponent<BoxCollider>(); return go;
        }
        static GameObject Prop(Transform parent, string type, Vector3 point, int palette, int place, float yaw = 0)
        {
            string key = type + ":" + palette;
            if (!templates.TryGetValue(key, out var template)) { template = MakeTemplate(type, palette); templates[key] = template; }
            var go = new GameObject(type); go.transform.SetParent(parent, false); go.transform.localPosition = point; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.layer = CityContext.WorldLayer;
            go.AddComponent<MeshFilter>().sharedMesh = template.mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pigment;
            foreach (var shape in template.collision) { var collider = go.AddComponent<BoxCollider>(); collider.center = shape.center; collider.size = shape.size; }
            var prop = go.AddComponent<CityDestructible>(); prop.category = type; prop.place = place; prop.visuals = new Renderer[] { renderer }; prop.shapes = go.GetComponents<Collider>();
            prop.durability = type == "light" || type.StartsWith("magazine") || type == "hangers" ? 18 : type == "tv" || type == "radio" || type == "phone" || type == "mirror" ? 32 : type == "bed" || type == "wardrobe" || type == "fridge" ? 85 : type == "door" ? 74 : 52;
            propCount++; return go;
        }
        static void Lamp(Transform parent, Vector3 at, int place, int palette)
        {
            var lamp = Prop(parent, "light", at, palette, place);fixtures.Add(AttachLight(lamp.transform,place,fixtures.Count));
        }
        static CityLighting.Fixture AttachLight(Transform lamp,int place,int index)
        {
            var emitter = new GameObject("Luz • cômodo").transform; emitter.SetParent(lamp, false); emitter.localPosition = new Vector3(0, -.05f, 0);
            emitter.localRotation = Quaternion.Euler(90, 0, 0); var source = emitter.gameObject.AddComponent<Light>();
            source.type = LightType.Spot; source.spotAngle = 155; source.innerSpotAngle = 115; source.range = 12; source.color = new Color(1, .91f, .78f); source.intensity = .85f;
            source.shadows = LightShadows.Soft; source.shadowBias = .015f; source.shadowNormalBias = .035f; source.shadowResolution = LightShadowResolution.Low; source.enabled = false;
            return new CityLighting.Fixture { kind = "cômodo", place = place, anchor = lamp, source = source, lens = lamp.GetComponent<Renderer>(),
                localSurface = Vector3.zero, localEmitter = emitter.localPosition, intensity = .85f, node = -200000 - index };
        }
        public static void RebindLighting()
        {
            var catalog=UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();var lighting=UnityEngine.Object.FindFirstObjectByType<CityLighting>();if(!catalog||!lighting)return;
            var added=new List<CityLighting.Fixture>();
            foreach(var home in catalog.homes)
            {
                foreach(var prop in home.root.GetComponentsInChildren<CityDestructible>(true).Where(p=>p.category=="light"))
                {foreach(var old in prop.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(old.gameObject);added.Add(AttachLight(prop.transform,home.place,added.Count));}
                var room=lighting.rooms.FirstOrDefault(r=>r.place==home.place);
                if(room!=null){room.surfaces=room.surfaces.Concat(home.root.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();room.height=home.units>1?9:5.12f;}
            }
            var residential=new HashSet<int>(catalog.homes.Select(h=>h.place));lighting.fixtures=lighting.fixtures.Where(f=>!residential.Contains(f.place)).Concat(added).ToArray();EditorUtility.SetDirty(lighting);
        }
        public static void RebindWindows()
        {
            var catalog=UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();var windows=UnityEngine.Object.FindFirstObjectByType<CityWindows>();if(!catalog||!windows)return;
            var apartmentPlaces=new HashSet<int>(catalog.homes.Where(h=>h.units>1).Select(h=>h.place));
            var openings=windows.windows.Where(w=>!apartmentPlaces.Contains(w.place)).ToList();var panes=windows.glazing.Where(w=>!apartmentPlaces.Contains(w.place)).ToList();
            foreach(var home in catalog.homes.Where(h=>h.units>1))foreach(var renderer in home.root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Vidro"))
            {
                Vector3 local=home.root.InverseTransformPoint(renderer.transform.position);
                Vector3 outward=Mathf.Abs(local.x)>Mathf.Abs(local.z)?home.root.right*Mathf.Sign(local.x):home.root.forward*Mathf.Sign(local.z);
                openings.Add(new CityWindows.Window{place=home.place,center=renderer.bounds.center,outward=outward,width=renderer.transform.localScale.x,height=renderer.transform.localScale.y});
                panes.Add(new CityWindows.Glazing{place=home.place,renderer=renderer});
            }
            windows.windows=openings.ToArray();windows.glazing=panes.ToArray();EditorUtility.SetDirty(windows);
        }
        static void Living(Transform parent, Vector3 at, int palette, int place, bool compact)
        {
            var room = new GameObject("Sala • estar e leitura").transform; room.SetParent(parent, false); room.localPosition = at;
            Prop(room, "sofa", new Vector3(0, 0, compact ? 1.1f : 1.8f), palette, place, 180);
            Prop(room, "tvstand", new Vector3(0, 0, compact ? -1.3f : -1.65f), palette, place);
            Prop(room, "tv", new Vector3(0, .90f, compact ? -1.3f : -1.65f), palette, place);
            Prop(room, "coffee", new Vector3(0, 0, .05f), palette, place);
            Prop(room, "magazine-a", new Vector3(-.28f, .64f, .08f), palette, place, -12);
            Prop(room, "magazine-b", new Vector3(.23f, .64f, -.07f), palette, place, 18);
            if (!compact)
            {
                Prop(room, "side", new Vector3(-2.25f, 0, 1.7f), palette, place);
                Prop(room, "radio", new Vector3(-2.25f, .87f, 1.7f), palette, place);
                Prop(room, "plant", new Vector3(2.4f, 0, 2.15f), palette, place);
            }
            else Prop(room, "radio", new Vector3(-.9f, .90f, -1.3f), palette, place);
        }
        static void Bedroom(Transform parent, Vector3 at, int palette, int place, bool compact)
        {
            var room = new GameObject("Quarto • dormir e vestir").transform; room.SetParent(parent, false); room.localPosition = at;
            Prop(room, "bed", new Vector3(compact ? -.35f : -.3f, 0, -.7f), palette, place);
            Prop(room, "side", new Vector3(compact ? 1.08f : 1.35f, 0, -1.7f), palette, place);
            Prop(room, "phone", new Vector3(compact ? 1.08f : 1.35f, .87f, -1.7f), palette, place);
            if (!compact)
            {
                Prop(room, "wardrobe", new Vector3(-2.6f, 0, .3f), palette, place, 90);
                Prop(room, "hangers", new Vector3(-2.61f, 2.13f, .3f), palette, place, 90);
                Prop(room, "desk", new Vector3(1.7f, 0, 2.25f), palette, place);
                Prop(room, "magazine-c", new Vector3(1.55f, 1.08f, 2.25f), palette, place);
            }
            else
            {
                Prop(room, "wardrobe", new Vector3(-.55f, 0, 2.4f), palette, place, 180);
                Prop(room, "hangers", new Vector3(-.55f, 2.13f, 2.4f), palette, place, 180);
            }
        }
        static void Kitchen(Transform parent, Vector3 at, int palette, int place, bool compact)
        {
            var room = new GameObject("Cozinha • preparo e refeições").transform; room.SetParent(parent, false); room.localPosition = at;
            if (compact)
            {
                Prop(room, "counter", new Vector3(0, 0, 1.8f), palette, place, 180);
                Prop(room, "fridge", new Vector3(0, 0, -.2f), palette, place, -90);
            }
            else
            {
                Prop(room, "counter", new Vector3(0, 0, 2.4f), palette, place, 180);
                Prop(room, "fridge", new Vector3(2.6f, 0, 2.3f), palette, place, 180);
                Prop(room, "oven", new Vector3(-2.35f, 0, 2.4f), palette, place, 180);
                Prop(room, "dining", new Vector3(0, 0, -.7f), palette, place);
                Prop(room, "chair", new Vector3(-1.2f, 0, -.7f), palette, place, 90);
                Prop(room, "chair", new Vector3(1.2f, 0, -.7f), palette, place, -90);
                Prop(room, "bin", new Vector3(2.6f, 0, -1.5f), palette, place);
            }
        }
        static void Bathroom(Transform parent, Vector3 at, int palette, int place, bool compact)
        {
            var room = new GameObject("Banheiro • louças e lavanderia").transform; room.SetParent(parent, false); room.localPosition = at;
            Prop(room, "sink", new Vector3(0, 0, compact ? 2.6f : -2.3f), palette, place, compact ? 180 : 0);
            Prop(room, "mirror", new Vector3(0, 1.55f, compact ? 2.95f : -2.7f), palette, place, compact ? 180 : 0);
            Prop(room, "toilet", new Vector3(compact ? 0 : -2.2f, 0, compact ? -1.55f : -1.9f), palette, place);
            if (!compact)
            {
                Prop(room, "bath", new Vector3(2.1f, 0, -1.2f), palette, place, 90);
                Prop(room, "washer", new Vector3(2.1f, 0, 2.2f), palette, place);
                Prop(room, "towels", new Vector3(-2.6f, 1.1f, 1.8f), palette, place, 90);
            }
            else Prop(room, "shower", new Vector3(0, 0, -2.6f), palette, place);
        }

        static Template MakeTemplate(string type, int palette)
        {
            var g = new Geometry(); Color accent = Accents[palette], cloth = Color.Lerp(Cream, accent, .28f);
            void Box(float x, float y, float z, float w, float h, float d, Color color) => g.Box(new Vector3(x, y, z), new Vector3(w, h, d), color);
            void Legs(float w, float d, float h)
            { foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 }) Box(x * w, h * .5f, z * d, .09f, h, .09f, Wood); }
            switch (type)
            {
                case "tv":
                    Box(0, .10f, 0, .66f, .08f, .35f, Dark); Box(0, .23f, 0, .10f, .26f, .10f, Metal);
                    Box(0, .72f, 0, 1.68f, .95f, .13f, Dark); Box(0, .74f, .073f, 1.52f, .79f, .015f, new Color(.16f, .32f, .36f));
                    Box(0, .56f, .087f, 1.52f, .18f, .012f, accent); Box(-.42f, .91f, .088f, .25f, .19f, .01f, new Color(.94f, .68f, .32f));
                    for (int i = 0; i < 5; i++) Box(-.6f + i * .28f, .66f + (i % 2) * .05f, .09f, .17f, .27f + (i % 2) * .1f, .012f, new Color(.11f, .24f, .26f));
                    Box(.64f, .30f, .08f, .035f, .018f, .012f, Color.green); break;
                case "bed":
                    Legs(.80f, 1.04f, .30f); Box(0, .35f, 0, 1.90f, .22f, 2.5f, Wood); Box(0, .55f, 0, 1.84f, .23f, 2.40f, Cream);
                    Box(0, .71f, .3f, 1.87f, .12f, 1.65f, accent); Box(0, .78f, .98f, 1.89f, .07f, .32f, cloth);
                    Box(0, .77f, -1.20f, 1.97f, 1.1f, .12f, Wood);
                    foreach (int side in new[] { -1, 1 }) Box(side * .44f, .73f, -.76f, .75f, .18f, .52f, Color.Lerp(Cream, Color.white, .45f)); break;
                case "sofa":
                    Legs(1.16f, .38f, .22f); Box(0, .43f, 0, 2.70f, .42f, 1.08f, accent); Box(0, .96f, -.44f, 2.7f, 1.05f, .24f, cloth);
                    foreach (int side in new[] { -1, 1 }) { Box(side * .61f, .68f, .06f, 1.17f, .19f, .87f, cloth); Box(side * 1.31f, .80f, 0, .21f, .55f, 1.14f, accent); }
                    Box(.78f, .92f, -.24f, .45f, .40f, .17f, Cream); break;
                case "tvstand":
                    Legs(1.0f, .24f, .16f); Box(0, .51f, 0, 2.25f, .66f, .65f, Wood); Box(0, .86f, 0, 2.34f, .08f, .72f, Cream);
                    foreach (int side in new[] { -1, 1 }) { Box(side * .57f, .51f, .335f, 1.02f, .53f, .035f, accent); Box(side * .58f, .59f, .37f, .24f, .04f, .04f, Metal); } break;
                case "side": Legs(.28f, .27f, .72f); Box(0, .79f, 0, .72f, .16f, .72f, Wood); Box(0, .55f, .33f, .55f, .26f, .05f, accent); break;
                case "coffee": Legs(.61f, .38f, .55f); Box(0, .59f, 0, 1.48f, .10f, .98f, Wood); Box(0, .21f, 0, 1.23f, .05f, .76f, accent); break;
                case "radio":
                    Box(0, .23f, 0, .65f, .42f, .25f, Wood); Box(-.15f, .23f, .135f, .27f, .31f, .025f, Dark);
                    for (int i = 0; i < 6; i++) Box(-.15f, .105f + i * .048f, .153f, .25f, .012f, .016f, Metal);
                    Box(.17f, .30f, .14f, .21f, .09f, .02f, new Color(.71f, .78f, .60f));
                    foreach (float x in new[] { .10f, .24f }) Box(x, .15f, .16f, .075f, .075f, .06f, Dark);
                    g.Bar(new Vector3(.23f, .43f, 0), new Vector3(.40f, .94f, 0), .016f, Metal); break;
                case "phone":
                    Box(0, .055f, 0, .42f, .11f, .35f, accent); Box(0, .135f, -.07f, .42f, .08f, .10f, Dark);
                    foreach (int side in new[] { -1, 1 }) Box(side * .18f, .11f, -.01f, .11f, .13f, .18f, Dark);
                    for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) Box((x - 1) * .065f, .117f, .025f + z * .045f, .04f, .016f, .025f, Cream); break;
                case "magazine-a": case "magazine-b": case "magazine-c":
                    Box(0, .014f, 0, .31f, .028f, .42f, Cream); Box(0, .031f, 0, .315f, .009f, .425f, type == "magazine-b" ? accent : type == "magazine-a" ? new Color(.77f, .37f, .19f) : new Color(.23f, .46f, .58f));
                    Box(-.015f, .038f, -.14f, .24f, .005f, .045f, Cream); Box(.025f, .038f, .025f, .18f, .005f, .18f, Color.Lerp(Cream, accent, .3f));
                    for (int i = 0; i < 3; i++) Box(0, .038f, .13f + i * .022f, .22f, .005f, .007f, Cream); break;
                case "wardrobe":
                    Box(0, .10f, 0, 1.8f, .20f, .67f, Wood); Box(0, 2.43f, 0, 1.8f, .14f, .67f, Wood);
                    foreach (int side in new[] { -1, 1 }) Box(side * .85f, 1.28f, 0, .10f, 2.25f, .67f, Wood);
                    Box(0, 1.25f, -.30f, 1.8f, 2.4f, .07f, Wood); Box(.47f, 1.25f, .33f, .8f, 2.2f, .07f, accent); Box(.25f, 1.25f, .39f, .045f, .29f, .04f, Metal);
                    Box(-.35f, 2.1f, 0, .9f, .04f, .04f, Metal); break;
                case "hangers":
                    for (int i = 0; i < 4; i++)
                    {
                        float z = -.19f + i * .12f;
                        g.Bar(new Vector3(-.55f, -.12f, z), new Vector3(-.32f, 0, z), .018f, Wood);
                        g.Bar(new Vector3(-.32f, 0, z), new Vector3(-.09f, -.12f, z), .018f, Wood);
                        Box(-.32f, -.12f, z, .46f, .018f, .018f, Wood); Box(-.32f, .04f, z, .018f, .08f, .018f, Metal);
                        Box(-.32f, -.4f, z, .36f, .52f, .055f, i % 2 == 0 ? accent : Cream);
                    } break;
                case "desk": case "dining":
                    Legs(type == "desk" ? .67f : .76f, .38f, 1.0f); Box(0, 1.04f, 0, type == "desk" ? 1.55f : 1.8f, .08f, 1.0f, Wood); break;
                case "chair":
                    Legs(.27f, .27f, .64f); Box(0, .69f, 0, .70f, .10f, .70f, accent); Box(0, 1.12f, -.29f, .70f, .77f, .12f, Wood); break;
                case "counter":
                    Box(0, .57f, 0, 2.8f, 1.1f, .85f, accent); Box(0, 1.17f, 0, 2.9f, .12f, .95f, Cream);
                    Box(-.62f, 1.24f, 0, .86f, .025f, .57f, Metal); Box(-.62f, 1.26f, 0, .66f, .018f, .40f, Dark);
                    g.Bar(new Vector3(-.62f, 1.24f, -.29f), new Vector3(-.62f, 1.56f, -.29f), .045f, Metal); g.Bar(new Vector3(-.62f, 1.55f, -.29f), new Vector3(-.62f, 1.55f, -.05f), .045f, Metal);
                    for (int i = 0; i < 3; i++) { Box(-.91f + i * .91f, .57f, .435f, .85f, .97f, .025f, Color.Lerp(accent, Wood, .14f)); Box(-.91f + i * .91f, .94f, .46f, .22f, .035f, .035f, Metal); } break;
                case "fridge":
                    Box(0, 1.03f, 0, .96f, 2.06f, .88f, Cream); Box(0, 1.50f, .45f, .91f, .96f, .04f, Color.Lerp(Cream, Color.white, .25f));
                    Box(0, .49f, .45f, .91f, .93f, .04f, Color.Lerp(Cream, Color.white, .25f)); Box(-.32f, 1.27f, .51f, .045f, .40f, .045f, Metal); Box(-.32f, .76f, .51f, .045f, .35f, .045f, Metal); break;
                case "oven":
                    Box(0, .56f, 0, .92f, 1.12f, .87f, Cream); Box(0, .52f, .45f, .72f, .62f, .035f, Dark); Box(0, .88f, .49f, .64f, .045f, .05f, Metal);
                    foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 }) Box(x * .23f, 1.14f, z * .23f, .30f, .035f, .30f, Dark); break;
                case "sink":
                    Box(0, .54f, 0, .88f, 1.08f, .65f, accent); Box(0, 1.12f, 0, 1.02f, .16f, .76f, Cream); Box(0, 1.21f, 0, .71f, .02f, .44f, Metal);
                    Box(0, 1.33f, -.26f, .05f, .29f, .05f, Metal); Box(0, 1.46f, -.17f, .05f, .05f, .23f, Metal); break;
                case "mirror":
                    Box(0, .40f, 0, 1.12f, .94f, .075f, Wood); Box(0, .40f, .045f, .96f, .78f, .025f, new Color(.57f, .76f, .76f)); break;
                case "toilet":
                    Box(0, .27f, .12f, .44f, .54f, .60f, Cream); Box(0, .53f, .16f, .60f, .16f, .77f, Cream); Box(0, .63f, .18f, .46f, .045f, .55f, Dark);
                    Box(0, .68f, -.26f, .65f, .74f, .24f, Cream); Box(.19f, 1.07f, -.26f, .08f, .03f, .08f, Metal); break;
                case "bath":
                    Box(0, .16f, 0, 1.10f, .32f, 2.10f, Cream); foreach (int side in new[] { -1, 1 }) { Box(side * .49f, .43f, 0, .12f, .50f, 2.10f, Cream); Box(0, .43f, side * .99f, .98f, .50f, .12f, Cream); }
                    Box(0, .73f, -.94f, .06f, .18f, .06f, Metal); break;
                case "washer":
                    Box(0, .57f, 0, .94f, 1.14f, .84f, Cream); Box(0, .49f, .435f, .63f, .65f, .045f, Dark); Box(0, .49f, .465f, .46f, .47f, .025f, Metal); Box(.23f, .95f, .45f, .13f, .13f, .05f, Dark); break;
                case "shower":
                    Box(0, .065f, 0, 1.04f, .13f, 1.05f, Cream); Box(0, 1.25f, -.46f, 1.02f, 2.5f, .08f, Color.Lerp(accent, Cream, .7f));
                    g.Bar(new Vector3(.26f, .7f, -.37f), new Vector3(.26f, 2.32f, -.37f), .045f, Metal); Box(.26f, 2.34f, -.18f, .29f, .06f, .38f, Metal); break;
                case "towels":
                    Box(0, .18f, 0, .04f, .04f, .74f, Metal); Box(.03f, -.10f, 0, .045f, .56f, .54f, cloth); Box(.055f, -.06f, .12f, .035f, .42f, .24f, accent); break;
                case "light": Box(0, .035f, 0, .72f, .12f, .72f, Wood); Box(0, -.035f, 0, .58f, .025f, .58f, new Color(1, .95f, .78f)); break;
                case "plant":
                    Box(0, .24f, 0, .49f, .48f, .49f, accent); Box(0, .50f, 0, .54f, .07f, .54f, Wood);
                    g.Bar(new Vector3(0, .5f, 0), new Vector3(.12f, 1.50f, 0), .04f, Wood);
                    for (int i = 0; i < 5; i++) Box(Mathf.Sin(i * 2.4f) * .19f, .72f + i * .16f, Mathf.Cos(i * 2.4f) * .17f, .36f, .10f, .28f, new Color(.28f, .48f, .27f)); break;
                case "bin": Box(0, .42f, 0, .48f, .84f, .48f, Metal); Box(0, .86f, 0, .53f, .06f, .53f, Dark); break;
                case "door":
                    Box(-.7f, 1.525f, 0, 1.4f, 3.05f, .12f, Wood); Box(-.7f, 1.58f, .066f, 1.13f, 2.68f, .025f, Color.Lerp(Cream, accent, .16f));
                    foreach (int side in new[] { -1, 1 }) Box(-1.17f, 1.35f, side * .105f, .18f, .055f, .08f, Metal); break;
                default: Box(0, .5f, 0, 1, 1, 1, accent); break;
            }
            Mesh mesh = g.Save(type + " • " + palette); var bounds = mesh.bounds;
            if (type == "radio") bounds = new Bounds(new Vector3(0, .24f, 0), new Vector3(.67f, .48f, .28f)); // the thin antenna is not a solid wall
            if (type == "hangers") bounds = new Bounds(new Vector3(-.32f, -.2f, 0), new Vector3(.48f, .58f, .45f));
            var size = bounds.size; size.x = Mathf.Max(size.x, .045f); size.y = Mathf.Max(size.y, .045f); size.z = Mathf.Max(size.z, .045f); bounds.size = size;
            Bounds[] collision=new[]{bounds};
            if(type=="wardrobe")collision=new[]{new Bounds(new Vector3(0,.1f,0),new Vector3(1.8f,.2f,.67f)),new Bounds(new Vector3(0,2.43f,0),new Vector3(1.8f,.14f,.67f)),
                new Bounds(new Vector3(-.85f,1.28f,0),new Vector3(.10f,2.25f,.67f)),new Bounds(new Vector3(.85f,1.28f,0),new Vector3(.10f,2.25f,.67f)),
                new Bounds(new Vector3(0,1.25f,-.3f),new Vector3(1.8f,2.4f,.07f)),new Bounds(new Vector3(.47f,1.25f,.33f),new Vector3(.8f,2.2f,.07f))};
            return new Template { mesh = mesh, collision = collision };
        }
        static void AddLod(Transform root, string category, float cull, bool exterior)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(); if (renderers.Length == 0) return;
            var lod = root.gameObject.AddComponent<CityObjectLod>(); lod.category = category; lod.cullDistance = cull; lod.fullDistance = 30;
            lod.localBounds = new Bounds(new Vector3(0, 4, 0), new Vector3(20, 9, 16));
            lod.parts = renderers.Select(r => { var filter = r.GetComponent<MeshFilter>(); bool prop = r.GetComponent<CityDestructible>();
                return new CityLodPart { renderer = r, filter = filter, full = filter.sharedMesh, medium = filter.sharedMesh, far = filter.sharedMesh,
                    lastVisibleLevel = prop ? 0 : 2, shadows = r.shadowCastingMode }; }).ToArray(); lods.Add(lod);
        }
        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>(); readonly List<int> indices = new List<int>();
            public void Box(Vector3 center, Vector3 size, Color color) => Box(center, size, color, Quaternion.identity);
            public void Bar(Vector3 a, Vector3 b, float width, Color color) => Box((a + b) * .5f, new Vector3(width, (b - a).magnitude, width), color, Quaternion.FromToRotation(Vector3.up, b - a));
            public void Box(Vector3 center, Vector3 size, Color color, Quaternion rotation)
            {
                for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                {
                    var n = Vector3.zero; n[axis] = sign; var u = Vector3.zero; u[(axis + 1) % 3] = 1; var v = Vector3.Cross(n, u);
                    int first = vertices.Count;
                    foreach (var corner in new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f) })
                    { vertices.Add(center + rotation * Vector3.Scale(n * .5f + u * corner.x + v * corner.y, size)); normals.Add(rotation * n); colors.Add(color); }
                    indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                }
            }
            public Mesh Save(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors);
                mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, vertices.Count).ToList()); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
        }
    }
}
