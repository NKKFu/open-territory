using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    public sealed partial class CityDestruction
    {
        [Serializable] sealed class OutdoorLayout { public FenceData[] walls = new FenceData[0]; }
        [Serializable] sealed class FenceData { public Vector3 center; public float yaw, width, length, height; }

        sealed class Volume
        {
            public Matrix4x4 inverse;
            public Vector3 half;
            public bool Contains(Vector3 point)
            {
                var p = inverse.MultiplyPoint3x4(point);
                return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
            }
        }

        // Sections share their original draw batches. A broken section degenerates
        // only its own triangles, in every LOD; no renderer per fence is required.
        internal sealed class BatchedSection
        {
            public Bounds bounds;
            internal readonly List<BatchPiece> pieces = new List<BatchPiece>();
            readonly List<Volume> volumes = new List<Volume>();
            internal CityDestructible prop;
            bool hasBounds;
            internal void AddVolume(BoxCollider shape, Vector3 extra)
            {
                var matrix = shape.transform.localToWorldMatrix * Matrix4x4.Translate(shape.center);
                volumes.Add(new Volume { inverse = matrix.inverse, half = shape.size * .5f + extra });
            }
            internal void AddVolume(Matrix4x4 matrix, Vector3 half)
            { volumes.Add(new Volume { inverse = matrix.inverse, half = half }); }
            internal bool Contains(Vector3 point) { foreach (var v in volumes) if (v.Contains(point)) return true; return false; }
            internal void Include(Vector3 point)
            { if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; } else bounds.Encapsulate(point); }
            public void Wear(float wear) { foreach (var piece in pieces) piece.Wear(wear); }
            public void Hide() { foreach (var piece in pieces) piece.Hide(); }
            internal int TriangleCount => pieces.Where(p => p.full).Sum(p => p.offsets.Length);
        }

        internal sealed class OutdoorBatch
        {
            internal Mesh source, mesh;
            internal int[] indices;
            internal Vector3[] points, normals;
            internal Color32[] originals, colors;
            internal Matrix4x4 matrix;
            internal Material material;
        }
        internal sealed class BatchPiece
        {
            internal OutdoorBatch batch;
            internal int[] offsets, vertices;
            internal bool full;
            internal void Wear(float wear)
            {
                Color tint = Color.Lerp(Color.white, new Color(.49f, .44f, .39f), wear * .65f);
                foreach (int i in vertices) batch.colors[i] = (Color)batch.originals[i] * tint;
                batch.mesh.SetColors(batch.colors);
            }
            internal void Hide()
            {
                foreach (int i in offsets) batch.indices[i + 1] = batch.indices[i + 2] = batch.indices[i];
                int start = offsets[0], count = offsets[offsets.Length - 1] + 3 - start;
                batch.mesh.SetIndexBufferData(batch.indices, start, start, count,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
        }
        sealed class OutdoorLodBinding
        {
            internal CityLodPart part;
            internal Mesh full, medium, far;
        }
        readonly List<OutdoorLodBinding> outdoorBindings = new List<OutdoorLodBinding>();
        readonly List<Mesh> outdoorMeshes = new List<Mesh>();
        readonly List<BatchedSection> outdoorSections = new List<BatchedSection>();
        readonly Dictionary<CityDestructible, CityDestructible[]> fencePeers = new Dictionary<CityDestructible, CityDestructible[]>();
        bool relayingFenceDamage;
        public int FenceCount { get; private set; }
        public int TreeCount { get; private set; }

        static Vector2Int OutdoorKey(float x, float z) => new Vector2Int(Mathf.RoundToInt(x * 100), Mathf.RoundToInt(z * 100));
        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 8), Mathf.FloorToInt(p.z / 8));

        void RegisterOutdoor(Dictionary<int, List<Collider>> imported)
        {
            var treeRoots = new Dictionary<Vector2Int, Transform>();
            foreach (var lod in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (lod.gameObject.activeInHierarchy) treeRoots[OutdoorKey(lod.transform.position.x, lod.transform.position.z)] = lod.transform;
            foreach (var tree in ctx.Data.natureTrees)
            {
                var root = ctx.Node(tree.node);
                if (!root || !root.GetComponent<LODGroup>()) treeRoots.TryGetValue(OutdoorKey(tree.position.x, tree.position.z), out root);
                if (!root || !root.gameObject.activeInHierarchy || !imported.TryGetValue(tree.solidIndex, out var shapes)) continue;
                foreach (var shape in shapes) if (shape && shape.gameObject.activeInHierarchy) shape.transform.SetParent(root, true);
                RegisterTree(root, tree.node);
            }
            // The cemetery's eight authored cypresses use sibling trunk colliders.
            foreach (var root in treeRoots.Values)
            {
                if (root.GetComponent<CityDestructible>()) continue;
                if (root.name == "Cipreste do cemitério")
                {
                    var trunk = root.parent.GetComponentsInChildren<BoxCollider>().FirstOrDefault(c => c.name == "Tronco do cipreste" &&
                        CityPhysics.Flat(c.bounds.center - root.position).sqrMagnitude < .05f);
                    if (trunk) { trunk.transform.SetParent(root, true); RegisterTree(root, 0); }
                }
                else if (root.GetComponent<Collider>())
                {
                    // Individually planted trees (for example the public park)
                    // reuse the same tree library but have no world-manifest ID.
                    var levels = root.GetComponent<LODGroup>().GetLODs();
                    var renderer = levels.Length > 0 && levels[0].renderers.Length > 0 ? levels[0].renderers[0] : null;
                    var filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
                    if (filter && filter.sharedMesh && filter.sharedMesh.name.Contains("-natural-")) RegisterTree(root, 0);
                }
            }

            var solidsAt = new Dictionary<Vector2Int, List<SolidData>>();
            foreach (var solid in ctx.Data.solids)
            {
                var key = OutdoorKey(solid.x, solid.z);
                if (!solidsAt.TryGetValue(key, out var list)) solidsAt[key] = list = new List<SolidData>();
                list.Add(solid);
            }
            var fences = new List<BatchedSection>();
            var asset = Resources.Load<TextAsset>("neighborhood-spaces");
            var layout = asset ? JsonUtility.FromJson<OutdoorLayout>(asset.text) : null;
            if (layout != null) foreach (var fence in layout.walls)
            {
                if (!solidsAt.TryGetValue(OutdoorKey(fence.center.x, fence.center.z), out var candidates)) continue;
                foreach (var solid in candidates)
                {
                    if (Mathf.Abs(solid.w - fence.width) > .01f || Mathf.Abs(solid.d - fence.length - .015f) > .025f ||
                        Mathf.Abs(solid.bottom - fence.center.y) > .025f || Mathf.Abs(solid.top - solid.bottom - fence.height) > .025f) continue;
                    var box = ImportedBox(imported, solid.index);
                    if (!box || box.GetComponent<CityDestructible>()) continue;
                    var prop = box.gameObject.AddComponent<CityDestructible>(); prop.category = "fence"; prop.durability = 72;
                    var section = new BatchedSection { prop = prop };
                    // Include the coping's 5 cm overhang and 9 cm height.
                    section.AddVolume(box, new Vector3(.07f, .11f, .05f));
                    fences.Add(section);
                }
            }
            var reusedTrees = new List<BatchedSection>();
            foreach (var solid in ctx.Data.solids)
            {
                if (Mathf.Abs(solid.w - .77f) > .001f || Mathf.Abs(solid.d - .77f) > .001f || Mathf.Abs(solid.top - solid.bottom - 4.4f) > .02f) continue;
                var trunk = ImportedBox(imported, solid.index); if (!trunk || trunk.GetComponent<CityDestructible>()) continue;
                var prop = trunk.gameObject.AddComponent<CityDestructible>(); prop.category = "tree"; prop.durability = 145;
                var section = new BatchedSection { prop = prop }; section.AddVolume(trunk, Vector3.one * .065f);
                if (solidsAt.TryGetValue(OutdoorKey(solid.x, solid.z), out var candidates)) foreach (var canopy in candidates)
                {
                    if (Mathf.Abs(canopy.w - 4.84f) > .01f || Mathf.Abs(canopy.d - 4.62f) > .01f) continue;
                    var crown = ImportedBox(imported, canopy.index); if (!crown) continue;
                    section.AddVolume(crown, Vector3.one * .08f); crown.transform.SetParent(trunk.transform, true);
                }
                reusedTrees.Add(section);
            }
            AttachOutdoorBatches(fences, reusedTrees);
            foreach (var section in fences.Concat(reusedTrees))
            {
                if (section.TriangleCount == 0)
                {
                    Debug.LogWarning("Outdoor destruction: no geometry for " + section.prop.name);
                    Destroy(section.prop); continue;
                }
                section.prop.batchSection = section; outdoorSections.Add(section); Register(section.prop);
                if (section.prop.category == "fence") FenceCount++;
                else { PrepareBatchedTree(section); TreeCount++; }
            }
            ConnectFencePeers(fences.Where(s => s.prop.batchSection != null).ToList());
        }
        // Adjacent lots can author the same boundary twice, with differently
        // spaced sections. Only physically overlapping parallel spans share a
        // hit. This is one hop: it never propagates down an entire fence line.
        void ConnectFencePeers(List<BatchedSection> sections)
        {
            var grid = SectionGrid(sections);
            foreach (var section in sections)
            {
                var a = section.prop; var boxA = a.GetComponent<BoxCollider>();
                var peers = new HashSet<CityDestructible>();
                var min = Cell(boxA.bounds.min - Vector3.one * .1f); var max = Cell(boxA.bounds.max + Vector3.one * .1f);
                for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
                {
                    if (!grid.TryGetValue(new Vector2Int(x, z), out var candidates)) continue;
                    foreach (var candidate in candidates)
                    {
                        var b = candidate.prop; if (a == b || Mathf.Abs(Vector3.Dot(a.transform.forward, b.transform.forward)) < .998f) continue;
                        var boxB = b.GetComponent<BoxCollider>(); var delta = boxB.bounds.center - boxA.bounds.center;
                        float across = Mathf.Abs(Vector3.Dot(delta, a.transform.right));
                        float halfWidth = (boxA.size.x * Mathf.Abs(a.transform.lossyScale.x) + boxB.size.x * Mathf.Abs(b.transform.lossyScale.x)) * .5f;
                        float halfSpan = (boxA.size.z * Mathf.Abs(a.transform.lossyScale.z) + boxB.size.z * Mathf.Abs(b.transform.lossyScale.z)) * .5f;
                        float overlap = halfSpan - Mathf.Abs(Vector3.Dot(delta, a.transform.forward));
                        if (across > halfWidth + .015f || overlap < .35f ||
                            Mathf.Min(boxA.bounds.max.y, boxB.bounds.max.y) - Mathf.Max(boxA.bounds.min.y, boxB.bounds.min.y) < .25f) continue;
                        peers.Add(b);
                    }
                }
                fencePeers[a] = peers.ToArray();
            }
        }
        internal IReadOnlyList<CityDestructible> FencePeers(CityDestructible prop) =>
            fencePeers.TryGetValue(prop, out var peers) ? peers : Array.Empty<CityDestructible>();
        internal void RelayFenceDamage(CityDestructible source, float amount, Vector3 point, Vector3 impulse, CityPropDamageKind kind, bool playerCaused)
        {
            if (relayingFenceDamage || !fencePeers.TryGetValue(source, out var peers)) return;
            relayingFenceDamage = true;
            try
            {
                foreach (var peer in peers)
                {
                    if (!peer || peer.Broken) continue;
                    peer.breakingVehicle = source.breakingVehicle; peer.vehicleBreakStep = source.vehicleBreakStep;
                    peer.lastVehicleHit = source.lastVehicleHit;
                    peer.Apply(amount, point, impulse, kind, playerCaused);
                }
            }
            finally { relayingFenceDamage = false; }
        }
        static BoxCollider ImportedBox(Dictionary<int, List<Collider>> imported, int id)
        {
            if (imported.TryGetValue(id, out var shapes)) foreach (var shape in shapes)
                if (shape is BoxCollider box && box.enabled && box.gameObject.activeInHierarchy) return box;
            return null;
        }
        void RegisterTree(Transform root, int node)
        {
            var prop = root.GetComponent<CityDestructible>();
            if (!prop) prop = root.gameObject.AddComponent<CityDestructible>();
            prop.category = "tree"; prop.nodeId = node;
            prop.durability = Mathf.Clamp(105 * Mathf.Sqrt(Mathf.Abs(root.lossyScale.x * root.lossyScale.y)), 55, 240);
            var lod = root.GetComponent<LODGroup>(); var levels = lod ? lod.GetLODs() : new LOD[0];
            var renderer = levels.Length > 0 && levels[0].renderers.Length > 0 ? levels[0].renderers[0] : null;
            var filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
            if (filter) { prop.fallingMesh = filter.sharedMesh; prop.fallingMaterial = renderer.sharedMaterial; }
            Register(prop); TreeCount++;
        }

        static Dictionary<Vector2Int, List<BatchedSection>> SectionGrid(List<BatchedSection> sections)
        {
            var grid = new Dictionary<Vector2Int, List<BatchedSection>>();
            foreach (var section in sections)
            {
                var bounds = section.prop.GetComponent<Collider>().bounds;
                foreach (var shape in section.prop.GetComponentsInChildren<Collider>()) bounds.Encapsulate(shape.bounds);
                bounds.Expand(.3f); var min = Cell(bounds.min); var max = Cell(bounds.max);
                for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<BatchedSection>();
                    list.Add(section);
                }
            }
            return grid;
        }
        void AttachOutdoorBatches(List<BatchedSection> fences, List<BatchedSection> trees)
        {
            var fenceGrid = SectionGrid(fences); var treeGrid = SectionGrid(trees);
            foreach (var owner in FindObjectsByType<CityObjectLod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (owner.category != "scenery") continue;
                foreach (var part in owner.parts)
                {
                    if (!part.renderer || !part.filter || !part.renderer.sharedMaterial) continue;
                    string material = part.renderer.sharedMaterial.name;
                    var grid = material == "Muros de quintal" || material == "Capas dos muros" ? fenceGrid :
                        material == "material-795" ? treeGrid : null;
                    if (grid == null || grid.Count == 0) continue;
                    var binding = new OutdoorLodBinding { part = part, full = part.full, medium = part.medium, far = part.far };
                    var clones = new Dictionary<Mesh, Mesh>();
                    Mesh Connect(Mesh source, bool full)
                    {
                        if (!source) return source;
                        if (clones.TryGetValue(source, out var prior)) return prior;
                        var points = source.vertices; var triangles = source.triangles;
                        if (points.Length % 24 != 0 || triangles.Length % 36 != 0)
                        { Debug.LogWarning("Outdoor destruction: unsupported non-box batch " + source.name); return source; }
                        var assignments = new Dictionary<BatchedSection, List<int>>();
                        var matrix = part.filter.transform.localToWorldMatrix;
                        // These imported materials consist of independent boxes,
                        // twelve consecutive triangles each. Attribute the whole
                        // box by its centre, never individual faces at a shared joint.
                        for (int i = 0; i + 35 < triangles.Length; i += 36)
                        {
                            var first = matrix.MultiplyPoint3x4(points[triangles[i]]);
                            var boxBounds = new Bounds(first, Vector3.zero);
                            for (int n = 1; n < 36; n++) boxBounds.Encapsulate(matrix.MultiplyPoint3x4(points[triangles[i + n]]));
                            var center = boxBounds.center;
                            if (!grid.TryGetValue(Cell(center), out var candidates)) continue;
                            BatchedSection chosen = null; float nearest = float.MaxValue;
                            foreach (var candidate in candidates)
                            {
                                if (!candidate.Contains(center)) continue;
                                float distance = CityPhysics.Flat(center - candidate.prop.transform.position).sqrMagnitude;
                                if (distance < nearest) { chosen = candidate; nearest = distance; }
                            }
                            if (chosen == null) continue;
                            if (!assignments.TryGetValue(chosen, out var list)) assignments[chosen] = list = new List<int>();
                            for (int n = 0; n < 36; n += 3) list.Add(i + n);
                            if (full) { chosen.Include(boxBounds.min); chosen.Include(boxBounds.max); }
                        }
                        if (assignments.Count == 0) { clones[source] = source; return source; }
                        var mesh = Instantiate(source); mesh.name = source.name + " • destructible sections";
                        // Partial index uploads always use 32-bit indices, including simplified LODs.
                        mesh.indexFormat = IndexFormat.UInt32; mesh.SetTriangles(triangles, 0, false); mesh.MarkDynamic();
                        var colors = source.colors32;
                        if (colors.Length != points.Length) { colors = new Color32[points.Length]; for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255); }
                        var batch = new OutdoorBatch { source = source, mesh = mesh, points = points, normals = source.normals,
                            indices = triangles, originals = colors, colors = (Color32[])colors.Clone(), matrix = matrix, material = part.renderer.sharedMaterial };
                        mesh.SetColors(batch.colors); outdoorMeshes.Add(mesh); clones[source] = mesh;
                        foreach (var pair in assignments)
                        {
                            var vertices = new HashSet<int>();
                            foreach (int i in pair.Value) { vertices.Add(triangles[i]); vertices.Add(triangles[i + 1]); vertices.Add(triangles[i + 2]); }
                            pair.Key.pieces.Add(new BatchPiece { batch = batch, offsets = pair.Value.ToArray(), vertices = vertices.ToArray(), full = full });
                        }
                        return mesh;
                    }
                    part.full = Connect(binding.full, true);
                    // Box faces are already low-poly. Shared simplified vertices
                    // cross fence joints, so retain independent authored boxes at
                    // each visible distance; normal culling/shadow LOD stays active.
                    part.medium = part.far = part.full;
                    var current = part.filter.sharedMesh;
                    if (current == binding.full || current == binding.medium || current == binding.far) part.filter.sharedMesh = part.full;
                    outdoorBindings.Add(binding);
                }
            }
        }
        void PrepareBatchedTree(BatchedSection section)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color32>();
            foreach (var piece in section.pieces)
            {
                if (!piece.full) continue;
                var batch = piece.batch; var matrix = section.prop.transform.worldToLocalMatrix * batch.matrix;
                foreach (int offset in piece.offsets) for (int n = 0; n < 3; n++)
                {
                    int i = batch.indices[offset + n]; vertices.Add(matrix.MultiplyPoint3x4(batch.points[i]));
                    normals.Add(matrix.inverse.transpose.MultiplyVector(batch.normals[i]).normalized); colors.Add(batch.originals[i]);
                }
                section.prop.fallingMaterial = batch.material;
            }
            var mesh = new Mesh { name = "Fallen reused tree", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors);
            mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); mesh.RecalculateBounds();
            section.prop.fallingMesh = mesh; outdoorMeshes.Add(mesh);
        }
        void DisposeOutdoorBatches()
        {
            foreach (var binding in outdoorBindings)
            {
                var p = binding.part;
                if (p.filter) p.filter.sharedMesh = binding.full;
                p.full = binding.full; p.medium = binding.medium; p.far = binding.far;
            }
            foreach (var mesh in outdoorMeshes) if (mesh) Destroy(mesh);
            outdoorBindings.Clear(); outdoorMeshes.Clear(); outdoorSections.Clear();
            fencePeers.Clear();
        }
    }
}
