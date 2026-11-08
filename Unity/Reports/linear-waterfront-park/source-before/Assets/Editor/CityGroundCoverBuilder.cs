using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    public static class CityGroundCoverBuilder
    {
        // Optional offline authoring sampler; normal imports retain their height field.
        public static Func<float, float, float> PlacementHeight;
        public const string RootName = "Vegetação rasteira • arbustos e grama";
        const string LibraryPath = "Assets/Imported/GroundCover.asset";
        const float TileSize = 24;
        struct Plant { public Vector3 point; public float size, yaw; public int seed, palette; }
        [Serializable] sealed class Report
        {
            public string sourceHash;
            public int grassTufts, shrubs, grassChunks, shrubChunks, maskedTriangles, maskedSolids;
            public int groveShrubs;
            public int scatteredShrubs;
            public int[] fullAndMediumGrassTriangles = new int[2], shrubTriangles = new int[3];
            public int[] grassSectors = new int[8], shrubSectors = new int[8];
            public float seconds;
        }

        [MenuItem("Open Territory/Reconstruir arbustos e grama")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode para reconstruir a vegetação.");
            var world = JsonUtility.FromJson<WorldData>(File.ReadAllText("Assets/Resources/world.json"));
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var parent = scene.GetRootGameObjects().First(g => g.name.StartsWith("Marin •"));
            Build(parent.transform, visual, world);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem("Open Territory/Atualizar vento da grama e arbustos")]
        public static void UpgradeWind()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode antes de atualizar o vento.");
            // Only existing mesh/material assets are updated. Never save or
            // rebuild the scene, so unrelated unsaved scene edits are preserved.
            var info = UnityEngine.Object.FindFirstObjectByType<CityGroundCoverInfo>();
            if (!info) throw new InvalidOperationException("Vegetação rasteira não encontrada.");
            int count = 0;
            foreach (var filter in info.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh; bool grass = mesh.name.StartsWith("Grama ", StringComparison.Ordinal);
                var data = new List<Vector2>(); mesh.GetUVs(2, data);
                if (data.Count != mesh.vertexCount)
                {
                    var vertices = mesh.vertices; data = new List<Vector2>(vertices.Length);
                    if (grass)
                    {
                        for (int i = 0; i < vertices.Length; i += 3)
                        {
                            float bottom = (vertices[i].y + vertices[i + 1].y) * .5f;
                            data.Add(Vector2.zero); data.Add(Vector2.zero); data.Add(new Vector2(Mathf.Max(0, vertices[i + 2].y - bottom), 1));
                        }
                    }
                    else
                    {
                        // Faces duplicate vertices for flat shading. Rejoin exact positions
                        // only to recover each existing bush volume's base and height.
                        var parents = Enumerable.Range(0, vertices.Length).ToArray();
                        var indices = new Dictionary<Vector3, int>();
                        int Root(int index) { while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; } return index; }
                        void Join(int a, int b) { parents[Root(a)] = Root(b); }
                        for (int i = 0; i < vertices.Length; i++)
                        { if (indices.TryGetValue(vertices[i], out int shared)) Join(i, shared); else indices[vertices[i]] = i; }
                        var triangles = mesh.triangles;
                        for (int i = 0; i < triangles.Length; i += 3) { Join(triangles[i], triangles[i + 1]); Join(triangles[i], triangles[i + 2]); }
                        var bounds = new Dictionary<int, Bounds>();
                        for (int i = 0; i < vertices.Length; i++)
                        { int root = Root(i); if (!bounds.TryGetValue(root, out var b)) b = new Bounds(vertices[i], Vector3.zero); b.Encapsulate(vertices[i]); bounds[root] = b; }
                        for (int i = 0; i < vertices.Length; i++)
                        { var b = bounds[Root(i)]; float h = Mathf.Max(0, vertices[i].y - b.min.y); data.Add(new Vector2(h, Mathf.Clamp01(h / Mathf.Max(.01f, b.size.y)))); }
                    }
                    mesh.SetUVs(2, data); mesh.RecalculateBounds(); var expanded = mesh.bounds; expanded.Expand(new Vector3(1.4f,.2f,1.4f)); mesh.bounds = expanded;
                    EditorUtility.SetDirty(mesh); count++;
                }
                var material = filter.GetComponent<MeshRenderer>().sharedMaterial;
                material.SetFloat("_GroundCover", grass ? 1 : 2); EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets(); Debug.Log("GROUND_COVER_WIND_UPGRADE_OK meshes=" + count);
        }

        public static void Build(Transform parent, VisualData visual, WorldData world)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var old = parent.Find(RootName);
            if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, LibraryPath);
            var root = new GameObject(RootName); root.transform.SetParent(parent, false);
            var info = root.AddComponent<CityGroundCoverInfo>(); info.sourceHash = world.sourceHash;
            var report = new Report { sourceHash = world.sourceHash };
            var mask = new SurfaceMask(world.ground);
            ReserveSurfaces(mask, visual, report);
            // Entire footprints also reserve interiors, doorway swings, trees, furniture and walls.
            foreach (var p in world.places)
                mask.Rectangle(p.x, p.z, p.lotWidth > 0 ? p.lotWidth : p.w, p.lotDepth > 0 ? p.lotDepth : p.d, p.yaw, .9f);
            // Native compounds can merge or replace exported lots. Reserve
            // their actual complete footprints, including the gap between lots.
            var clothing = UnityEngine.Object.FindFirstObjectByType<CityClothingShop>();
            if (clothing) mask.Rectangle(clothing.transform.position.x, clothing.transform.position.z, clothing.size.x, clothing.size.y, clothing.transform.eulerAngles.y * Mathf.Deg2Rad, .9f);
            var bikes = UnityEngine.Object.FindFirstObjectByType<CityBikeShop>();
            if (bikes) foreach (var hall in bikes.halls) if (hall) mask.Rectangle(hall.position.x, hall.position.z, 22, 19, hall.eulerAngles.y * Mathf.Deg2Rad, .9f);
            var woodlandMask=new SurfaceMask(mask);
            var treeSolids=new HashSet<int>(world.natureTrees.Select(t=>t.solidIndex));
            foreach (var s in world.solids.Where(s => s.active && s.top > s.bottom))
            {
                float ground = Height(world.ground, s.x, s.z);
                if (s.bottom > ground + 2 || s.top < ground - .1f) continue;
                mask.Rectangle(s.x, s.z, s.w, s.d, s.yaw, .35f); report.maskedSolids++;
                if(!treeSolids.Contains(s.index))woodlandMask.Rectangle(s.x,s.z,s.w,s.d,s.yaw,.35f);
            }
            foreach (var p in world.places)
                if (p.door != null) {mask.Rectangle(p.door.x, p.door.z, p.door.width + 2.2f, 5.6f, p.door.yaw, .3f);woodlandMask.Rectangle(p.door.x,p.door.z,p.door.width+2.2f,5.6f,p.door.yaw,.3f);}
            foreach (var p in world.locations) {mask.Rectangle(p.x, p.z, 2, 2, 0, .4f);woodlandMask.Rectangle(p.x,p.z,2,2,0,.4f);}
            foreach (var p in world.services) {mask.Rectangle(p.position.x, p.position.z, 2.4f, 2.4f, 0, .4f);woodlandMask.Rectangle(p.position.x,p.position.z,2.4f,2.4f,0,.4f);}
            mask.Rectangle(world.spawn.x, world.spawn.z, 3, 3, 0, 0);
            woodlandMask.Rectangle(world.spawn.x,world.spawn.z,3,3,0,0);

            var grass = new Dictionary<Vector2Int, List<Plant>>();
            var shrubs = new Dictionary<Vector2Int, List<Plant>>();
            var grassSamples = new List<Vector3>(); var shrubSamples = new List<Vector3>();
            var random = new System.Random(731041);
            var g = world.ground;
            float endX = g.x + (g.width - 1) * g.step, endZ = g.z + (g.height - 1) * g.step;
            // Jittered, correlated patches leave breathing space instead of a visible planting grid.
            for (float z = g.z + 3; z < endZ - 3; z += 3.6f)
                for (float x = g.x + 3; x < endX - 3; x += 3.6f)
                {
                    float px = x + Range(random, -1.6f, 1.6f), pz = z + Range(random, -1.6f, 1.6f);
                    float patch = Mathf.PerlinNoise((px + 2400) * .018f, (pz + 500) * .018f);
                    float chance = Mathf.Lerp(.12f, .9f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .68f, patch)));
                    float size = Range(random, .72f, 1.42f);
                    if (random.NextDouble() > chance || !CanPlant(mask, g, px, pz, size * .85f, .68f)) continue;
                    var plant = MakePlant(random, g, px, pz, size);
                    Add(grass, plant); report.grassSectors[Sector(g, px, pz)]++;
                    if (report.grassTufts++ % 173 == 0) grassSamples.Add(plant.point);
                }
            // Low, soft shrubs grow in loose groups, with a separate, sparser distribution.
            bool scattered=world.natureGroves.Length==0;
            float shrubStep=scattered?13:23;
            for (float z = g.z + 9; z < endZ - 9; z += shrubStep)
                for (float x = g.x + 9; x < endX - 9; x += shrubStep)
                {
                    float jitter=scattered?5.5f:10;
                    float px = x + Range(random, -jitter, jitter), pz = z + Range(random, -jitter, jitter);
                    if (random.NextDouble() > (scattered?.72f:.52f) || !scattered&&Mathf.PerlinNoise((px + 1800) * .026f, (pz + 700) * .026f) < .39f) continue;
                    int count = scattered?random.Next(1,5):random.Next(2, 6);
                    for (int i = 0; i < count; i++)
                    {
                        float angle = Range(random, 0, Mathf.PI * 2), radius = i == 0 ? 0 : Range(random, 2.2f, 5.5f);
                        float sx = px + Mathf.Cos(angle) * radius, sz = pz + Mathf.Sin(angle) * radius;
                        float size = Range(random, .72f, 1.34f);
                        if (Clearing(world,sx,sz,size*1.55f)||!CanPlant(mask, g, sx, sz, size * 1.55f, .48f)) continue;
                        var plant = MakePlant(random, g, sx, sz, size);
                        Add(shrubs, plant); report.shrubSectors[Sector(g, sx, sz)]++;
                        if (report.shrubs++ % 13 == 0) shrubSamples.Add(plant.point);
                    }
                }
            // Woodland understorey follows the existing tree groups. Clearings and the same
            // terrain/road/obstacle mask remain reserved, including the shrub's full footprint.
            var groveSamples=new List<Vector3>();var scatteredSamples=new List<Vector3>();var occupiedShrubs=new HashSet<Vector2Int>();
            foreach(var tile in shrubs.Values)foreach(var plant in tile)occupiedShrubs.Add(new Vector2Int(Mathf.FloorToInt(plant.point.x/1.35f),Mathf.FloorToInt(plant.point.z/1.35f)));
            var understorey=new System.Random(775202);
            var trunks=world.natureTrees.GroupBy(t=>new Vector2Int(Mathf.FloorToInt(t.position.x/8),Mathf.FloorToInt(t.position.z/8))).ToDictionary(g=>g.Key,g=>g.ToArray());
            bool TrunkFree(float x,float z)
            {
                var cell=new Vector2Int(Mathf.FloorToInt(x/8),Mathf.FloorToInt(z/8));
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    if(trunks.TryGetValue(cell+new Vector2Int(dx,dz),out var near))foreach(var trunk in near)
                    {
                        float radius=.35f*Mathf.Max(trunk.scale.x,trunk.scale.z)+.55f;
                        if(new Vector2(x-trunk.position.x,z-trunk.position.z).sqrMagnitude<radius*radius)return false;
                    }
                return true;
            }
            foreach(var tree in world.natureTrees.Where(t=>scattered||t.grove>=0))
                for(int attempt=0;attempt<(scattered?2:7);attempt++)
                {
                    if(understorey.NextDouble()>.72f)continue;
                    float angle=Range(understorey,0,Mathf.PI*2),reach=Range(understorey,scattered?3:1.8f,scattered?8:5.2f),size=Range(understorey,.62f,1.22f);
                    float x=tree.position.x+Mathf.Cos(angle)*reach,z=tree.position.z+Mathf.Sin(angle)*reach;
                    if(Clearing(world,x,z,size*1.55f)||!TrunkFree(x,z)||!CanPlant(woodlandMask,g,x,z,size*1.55f,.48f))continue;
                    if(!occupiedShrubs.Add(new Vector2Int(Mathf.FloorToInt(x/1.35f),Mathf.FloorToInt(z/1.35f))))continue;
                    var plant=MakePlant(understorey,g,x,z,size);plant.palette=understorey.Next(0,7);
                    Add(shrubs,plant);report.shrubSectors[Sector(g,x,z)]++;
                    if(report.shrubs++%13==0)shrubSamples.Add(plant.point);
                    if(scattered){if(report.scatteredShrubs++%17==0)scatteredSamples.Add(plant.point);}
                    else if(report.groveShrubs++%17==0)groveSamples.Add(plant.point);
                }
            info.groveShrubs=report.groveShrubs;info.groveShrubSamples=groveSamples.ToArray();
            info.scatteredShrubs=report.scatteredShrubs;info.scatteredShrubSamples=scatteredSamples.ToArray();
            var grassMaterial = Material("Grama • verde, sálvia e palha", true, library);
            var shrubMaterial = Material("Arbustos • folhagem facetada", false, library);
            Bake(grass, false, root.transform, grassMaterial, library, g, report.fullAndMediumGrassTriangles);
            Bake(shrubs, true, root.transform, shrubMaterial, library, g, report.shrubTriangles);
            info.grassTufts = report.grassTufts; info.shrubs = report.shrubs;
            info.grassChunks = report.grassChunks = grass.Count; info.shrubChunks = report.shrubChunks = shrubs.Count;
            info.grassSamples = grassSamples.ToArray(); info.shrubSamples = shrubSamples.ToArray();
            report.seconds = (float)watch.Elapsed.TotalSeconds;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../Reports/vegetation");
            File.WriteAllText("../Reports/vegetation/ground-cover-import.json", JsonUtility.ToJson(report, true));
            Debug.Log($"GROUND_COVER_IMPORT_OK grass={info.grassTufts} shrubs={info.shrubs} chunks={grass.Count + shrubs.Count} seconds={report.seconds:F1}");
        }

        static int Sector(GroundData g, float x, float z) => Mathf.Clamp((int)((x - g.x) / ((g.width - 1) * g.step) * 4), 0, 3)
            + 4 * Mathf.Clamp((int)((z - g.z) / ((g.height - 1) * g.step) * 2), 0, 1);
        public static bool Clearing(WorldData world,float x,float z,float margin)
        {
            foreach(var grove in world.natureGroves)
                if((new Vector2(x-grove.clearingX,z-grove.clearingZ)).sqrMagnitude<Mathf.Pow(grove.clearingRadius+margin,2))return true;
            return false;
        }
        static float Range(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
        static Plant MakePlant(System.Random r, GroundData g, float x, float z, float size) => new Plant
        { point = new Vector3(x, Height(g, x, z) - .025f, z), size = size, yaw = Range(r, 0, 360), seed = r.Next(), palette = r.Next(10) };
        static void Add(Dictionary<Vector2Int, List<Plant>> tiles, Plant p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.point.x / TileSize), Mathf.FloorToInt(p.point.z / TileSize));
            if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<Plant>();
            list.Add(p);
        }
        static bool CanPlant(SurfaceMask mask, GroundData g, float x, float z, float radius, float slope)
        {
            if (CityCemeteryBuilder.Reserved(x, z, radius)) return false;
            if (CityParkBuilder.Reserved(x, z, radius)) return false;
            if (CityWaterfrontBuilder.Reserved(x, z, radius)) return false;
            if (CityParkingLotBuilder.Reserved(x, z, radius)) return false;
            if (CityCarDealershipBuilder.Reserved(x, z, radius)) return false;
            if (CityFireStationSiteBuilder.Reserved(x, z, radius)) return false;
            if (!mask.Clear(x, z, radius)) return false;
            float h = Height(g, x, z);
            if (h < CityMarine.SeaLevel + .65f) return false;
            foreach (var offset in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            {
                float edge = Height(g, x + offset.x * radius, z + offset.y * radius);
                if (edge < CityMarine.SeaLevel + .45f || Mathf.Abs(edge - h) > radius * slope) return false;
            }
            return true;
        }
        public static float Height(GroundData g, float x, float z)
        {
            if (PlacementHeight != null) return PlacementHeight(x, z);
            float fx = Mathf.Clamp((x - g.x) / g.step, 0, g.width - 1.001f), fz = Mathf.Clamp((z - g.z) / g.step, 0, g.height - 1.001f);
            int i = (int)fx, j = (int)fz, at = j * g.width + i; float a = fx - i, b = fz - j;
            float h = g.heights[at], r = g.heights[at + 1], t = g.heights[at + g.width], c = g.heights[at + g.width + 1];
            return a + b <= 1 ? h + a * (r - h) + b * (t - h) : c + (1 - a) * (t - c) + (1 - b) * (r - c);
        }
        static Material Material(string name, bool doubleSide, UnityEngine.Object library)
        {
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
            material.SetColor("_Color", Color.white); material.SetFloat("_Glossiness", .02f);
            material.SetFloat("_Cull", doubleSide ? (float)CullMode.Off : (float)CullMode.Back);
            material.SetFloat("_GroundCover", doubleSide ? 1 : 2);
            AssetDatabase.AddObjectToAsset(material, library); return material;
        }
        static Color Leaf(int palette, bool shrub)
        {
            Color c = palette < 4 ? new Color(.30f, .43f, .15f) : palette < 7 ? new Color(.40f, .50f, .23f)
                : palette < 9 ? new Color(.33f, .46f, .31f) : new Color(.53f, .51f, .28f);
            return shrub ? c * .88f : c;
        }
        static void Bake(Dictionary<Vector2Int, List<Plant>> tiles, bool shrub, Transform parent, Material material,
            UnityEngine.Object library, GroundData ground, int[] triangleTotals)
        {
            foreach (var tile in tiles.OrderBy(k => k.Key.y).ThenBy(k => k.Key.x))
            {
                var origin = new Vector3((tile.Key.x + .5f) * TileSize, 0, (tile.Key.y + .5f) * TileSize);
                origin.y = Height(ground, origin.x, origin.z);
                var chunk = new GameObject((shrub ? "Arbustos " : "Grama ") + tile.Key + " • " + tile.Value.Count);
                chunk.transform.SetParent(parent, false); chunk.transform.localPosition = origin;
                var lods = new LOD[shrub ? 3 : 2];
                float[] thresholds = shrub ? new[] { .37f, .14f, .065f } : new[] { .52f, .19f };
                for (int level = 0; level < lods.Length; level++)
                {
                    var parts = new MeshParts();
                    foreach (var p in tile.Value)
                    {
                        if (!shrub && level == 1 && p.seed % 3 == 0 && p.seed != tile.Value[0].seed) continue;
                        if (shrub) parts.Shrub(p, origin, level, ground); else parts.Grass(p, origin, level, ground);
                    }
                    var go = new GameObject("LOD" + level); go.transform.SetParent(chunk.transform, false);
                    var mesh = parts.Finish(chunk.name + "-LOD" + level); AssetDatabase.AddObjectToAsset(mesh, library);
                    triangleTotals[level] += (int)mesh.GetIndexCount(0) / 3;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = shrub && level == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    lods[level] = new LOD(thresholds[level], new Renderer[] { renderer });
                }
                var group = chunk.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
                // Use the tile width consistently, so sparse border chunks retain the same view distances.
                group.size = Mathf.Max(TileSize, group.size);
            }
        }

        static void ReserveSurfaces(SurfaceMask mask, VisualData visual, Report report)
        {
            var matrices = new Dictionary<int, Matrix4x4>(); var visible = new Dictionary<int, bool>();
            foreach (var n in visual.nodes)
            {
                var local = Matrix4x4.TRS(n.position, n.rotation, n.scale);
                matrices[n.id] = n.parent >= 0 && matrices.TryGetValue(n.parent, out var parent) ? parent * local : local;
                visible[n.id] = n.visible && (n.parent < 0 || !visible.TryGetValue(n.parent, out bool pv) || pv);
                if (n.geometry < 0 || !visible[n.id] || n.dynamic || n.name.StartsWith("Terreno novo")) continue;
                var geometry = visual.geometries[n.geometry];
                var groups = n.materials.Length > 1 && geometry.groups.Length > 0 ? geometry.groups
                    : new[] { new GeometryGroup { start = 0, count = geometry.indices.Length, materialIndex = 0 } };
                foreach (var group in groups)
                {
                    if (group.materialIndex >= n.materials.Length) continue;
                    var material = visual.materials[n.materials[group.materialIndex]];
                    float surface = material.surface % 8;
                    bool deck = n.name.Contains("Passarela") || n.name.Contains("Cais nivelado") || n.name.Contains("embarque");
                    if (material.sea != 0 || !deck && (surface < .5f || Mathf.Abs(surface - 5) < .1f)) continue;
                    var transforms = n.instances.Length > 0 ? n.instances.Select(instance =>
                    { var m = new Matrix4x4(); for (int k = 0; k < 16; k++) m[k] = instance.matrix[k]; return matrices[n.id] * m; })
                        : new[] { matrices[n.id] };
                    foreach (var matrix in transforms)
                        for (int i = group.start; i + 2 < Mathf.Min(geometry.indices.Length, group.start + group.count); i += 3)
                        {
                            Vector3 Vertex(int at) { int k = geometry.indices[at] * 3; return matrix.MultiplyPoint3x4(new Vector3(geometry.positions[k], geometry.positions[k + 1], geometry.positions[k + 2])); }
                            var a = Vertex(i); var b = Vertex(i + 1); var c = Vertex(i + 2);
                            var cross = Vector3.Cross(b - a, c - a);
                            if (Mathf.Abs(cross.y) < cross.magnitude * .3f || cross.sqrMagnitude < .00001f) continue;
                            mask.Triangle(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z)); report.maskedTriangles++;
                        }
                }
            }
        }

        sealed class SurfaceMask
        {
            readonly float x, z; readonly int width, height; readonly bool[] occupied;
            public SurfaceMask(GroundData g)
            { x = g.x; z = g.z; width = Mathf.CeilToInt((g.width - 1) * g.step); height = Mathf.CeilToInt((g.height - 1) * g.step); occupied = new bool[width * height]; }
            public SurfaceMask(SurfaceMask source)
            {x=source.x;z=source.z;width=source.width;height=source.height;occupied=(bool[])source.occupied.Clone();}
            public bool Clear(float px, float pz, float radius)
            {
                int left = Mathf.FloorToInt(px - x - radius), right = Mathf.FloorToInt(px - x + radius);
                int bottom = Mathf.FloorToInt(pz - z - radius), top = Mathf.FloorToInt(pz - z + radius);
                if (left < 0 || bottom < 0 || right >= width || top >= height) return false;
                for (int j = bottom; j <= top; j++) for (int i = left; i <= right; i++) if (occupied[j * width + i]) return false;
                return true;
            }
            public void Rectangle(float px, float pz, float w, float d, float yaw, float margin)
            {
                float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw); w = w * .5f + margin; d = d * .5f + margin;
                Vector2 P(float a, float b) => new Vector2(px + c * a + s * b, pz - s * a + c * b);
                var a = P(-w, -d); var b = P(-w, d); var e = P(w, d); var f = P(w, -d);
                Triangle(a, b, e); Triangle(a, e, f);
            }
            static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
            static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
            { var ab = b - a; return (p - a - ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(.000001f, ab.sqrMagnitude))).sqrMagnitude; }
            public void Triangle(Vector2 a, Vector2 b, Vector2 c)
            {
                int left = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) - x - .71f), 0, width - 1);
                int right = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) - x + .71f), 0, width - 1);
                int bottom = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y) - z - .71f), 0, height - 1);
                int top = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.y, b.y, c.y) - z + .71f), 0, height - 1);
                for (int j = bottom; j <= top; j++) for (int i = left; i <= right; i++)
                {
                    if (occupied[j * width + i]) continue;
                    var p = new Vector2(x + i + .5f, z + j + .5f);
                    float ab = Cross(b - a, p - a), bc = Cross(c - b, p - b), ca = Cross(a - c, p - c);
                    if ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0)
                        || EdgeDistance(p, a, b) <= .51f || EdgeDistance(p, b, c) <= .51f || EdgeDistance(p, c, a) <= .51f)
                        occupied[j * width + i] = true;
                }
            }
        }

        sealed class MeshParts
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> wind = new List<Vector2>();
            Vector3 windRoot; float windHeight;
            readonly List<Color32> colors = new List<Color32>(); readonly List<int> triangles = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, bool grass = false)
            {
                int at = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                var normal = grass ? new Vector3(0, 1, 0) : Vector3.Cross(b - a, c - a).normalized;
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                colors.Add(ca); colors.Add(cb); colors.Add(cc); triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                foreach (var p in new[] { a, b, c }) { float h = Mathf.Max(0, p.y - windRoot.y); wind.Add(new Vector2(h, Mathf.Clamp01(h / Mathf.Max(.01f, windHeight)))); }
            }
            public void Grass(Plant p, Vector3 origin, int lod, GroundData g)
            {
                var random = new System.Random(p.seed); var leaf = Leaf(p.palette, false);
                int blades = lod == 0 ? 7 : 3;
                for (int i = 0; i < blades; i++)
                {
                    float angle = (p.yaw + i * 137.5f) * Mathf.Deg2Rad, reach = Range(random, .1f, .46f) * p.size;
                    var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var center = p.point + direction * reach; center.y = Height(g, center.x, center.z) - .04f;
                    var side = new Vector3(-direction.z, 0, direction.x) * Range(random, .075f, .14f) * p.size;
                    var tip = center + direction * Range(random, .12f, .32f) * p.size + Vector3.up * Range(random, .27f, .54f) * p.size;
                    windRoot = center - origin; windHeight = tip.y - center.y;
                    Face(center - side - origin, center + side - origin, tip - origin, leaf * .72f, leaf * .85f, leaf * 1.18f, true);
                }
            }
            public void Shrub(Plant p, Vector3 origin, int lod, GroundData g)
            {
                var leaf = Leaf(p.palette, true); var rotation = Quaternion.Euler(0, p.yaw, 0);
                Block(p.point - origin, new Vector3(1.8f, .86f, 1.5f) * p.size, rotation, leaf, lod == 0);
                if (lod < 2)
                {
                    var offset = rotation * new Vector3(-.62f, 0, .24f) * p.size;
                    var point = p.point + offset; point.y = Height(g, point.x, point.z) - .025f;
                    Block(point - origin, new Vector3(1.18f, .60f, 1.1f) * p.size, rotation, leaf * .91f, lod == 0);
                }
                if (lod == 0)
                {
                    var point = p.point + rotation * new Vector3(.52f, 0, -.28f) * p.size;
                    point.y = Height(g, point.x, point.z) - .025f;
                    Block(point - origin, new Vector3(1.12f, .73f, 1.04f) * p.size, rotation, leaf * 1.1f, true);
                }
            }
            void Block(Vector3 basePoint, Vector3 size, Quaternion rotation, Color color, bool detailed)
            {
                windRoot = basePoint + Vector3.up * .035f; windHeight = size.y - .035f;
                Vector2[] ring = detailed ? new[] { new Vector2(-.65f,-1),new Vector2(-1,-.65f),new Vector2(-1,.65f),new Vector2(-.65f,1),new Vector2(.65f,1),new Vector2(1,.65f),new Vector2(1,-.65f),new Vector2(.65f,-1) }
                    : new[] { new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1) };
                var bottom = new Vector3[ring.Length]; var top = new Vector3[ring.Length];
                for (int i = 0; i < ring.Length; i++)
                {
                    bottom[i] = basePoint + rotation * new Vector3(ring[i].x * size.x * .5f, .035f, ring[i].y * size.z * .5f);
                    top[i] = basePoint + rotation * new Vector3(ring[i].x * size.x * .34f, size.y, ring[i].y * size.z * .34f);
                }
                for (int i = 0; i < ring.Length; i++)
                {
                    int next = (i + 1) % ring.Length;
                    Face(bottom[i], top[next], top[i], color * .72f, color, color);
                    Face(bottom[i], bottom[next], top[next], color * .72f, color * .72f, color);
                }
                for (int i = 1; i < ring.Length - 1; i++)
                    Face(top[0], top[i], top[i + 1], color * 1.12f, color * 1.12f, color * 1.12f);
            }
            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
                mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, vertices.Count).ToList()); mesh.SetUVs(2, wind);
                mesh.RecalculateBounds(); var bounds = mesh.bounds; bounds.Expand(new Vector3(1.4f,.2f,1.4f)); mesh.bounds = bounds; return mesh;
            }
        }
    }
}
