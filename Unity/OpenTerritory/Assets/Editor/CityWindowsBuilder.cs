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
    public static class CityWindowsBuilder
    {
        const string AssetPath = "Assets/Imported/CityWindows.asset", Suffix = " • window openings";
        sealed class Aperture
        {
            public Vector3 center, outward;
            public float width, height;
            public Bounds cut;
        }
        struct Vertex
        {
            public Vector3 p, q, n;
            public Vector2 uv, surface;
            public Color color;
            public static Vertex Mix(Vertex a, Vertex b, float t) => new Vertex {
                p = Vector3.LerpUnclamped(a.p, b.p, t), q = Vector3.LerpUnclamped(a.q, b.q, t),
                n = Vector3.LerpUnclamped(a.n, b.n, t), uv = Vector2.LerpUnclamped(a.uv, b.uv, t),
                surface = Vector2.LerpUnclamped(a.surface, b.surface, t), color = Color.LerpUnclamped(a.color, b.color, t) };
        }
        sealed class Geometry
        {
            public readonly List<Vertex> vertices = new List<Vertex>();
            public void Polygon(List<Vertex> polygon)
            {
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    if (Vector3.Cross(polygon[i].p - polygon[0].p, polygon[i + 1].p - polygon[0].p).sqrMagnitude < 1e-13f) continue;
                    vertices.Add(polygon[0]); vertices.Add(polygon[i]); vertices.Add(polygon[i + 1]);
                }
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                var points = new[] { a, b, c, a, c, d };
                foreach (var p in points) vertices.Add(new Vertex { p = p, n = normal, color = Color.white });
            }
            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                {
                    var n = Vector3.zero; n[axis] = sign;
                    var u = Vector3.zero; u[(axis + 1) % 3] = 1;
                    var v = Vector3.Cross(n, u);
                    Vector3 Point(float x, float y) => center + rotation * Vector3.Scale(n * .5f + u * x + v * y, size);
                    Quad(Point(-.5f, -.5f), Point(.5f, -.5f), Point(.5f, .5f), Point(-.5f, .5f), rotation * n);
                }
            }
            public Mesh Save(string name, UnityEngine.Object library)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices.Select(v => v.p).ToList()); mesh.SetNormals(vertices.Select(v => v.n.normalized).ToList());
                mesh.SetUVs(0, vertices.Select(v => v.uv).ToList()); mesh.SetUVs(1, vertices.Select(v => v.surface).ToList());
                mesh.SetColors(vertices.Select(v => v.color).ToList());
                mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
        }

        [MenuItem("Open Territory/Reconstruir janelas das casas e prédios")]
        public static void BakeActiveScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de reconstruir janelas.");
            var registry = UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("A cena da cidade não está carregada.");
            Build(JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json")), JsonUtility.FromJson<WorldData>(registry.manifest.text));
            CityHomeBuilder.RebindWindows();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void Build(VisualData visual, WorldData world)
        {
            // Always cut immutable imported originals, including when this menu runs again.
            var originals = AssetDatabase.LoadAllAssetsAtPath("Assets/Imported/CityAssets.asset").OfType<Mesh>()
                .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
            Mesh Original(Mesh mesh) => mesh && mesh.name.EndsWith(Suffix) && originals.TryGetValue(mesh.name.Substring(0, mesh.name.Length - Suffix.Length), out var original) ? original : mesh;
            foreach (var lod in UnityEngine.Object.FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None)) foreach (var part in lod.parts)
            {
                part.full = Original(part.full); part.medium = Original(part.medium); part.far = Original(part.far);
                if (part.filter) part.filter.sharedMesh = part.full;
                part.externalMesh = false;
            }
            foreach (var previous in UnityEngine.Object.FindObjectsByType<CityWindows>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, AssetPath);
            var root = new GameObject("Janelas • aberturas, caixilhos e vidro").AddComponent<CityWindows>();
            var trim = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Caixilhos • marfim" };
            trim.SetColor("_Color", new Color(.77f, .80f, .75f)); trim.SetFloat("_Glossiness", .25f); trim.SetFloat("_Cull", 2);
            var glass = new Material(Shader.Find("OpenTerritory/CityWindow")) { name = "Vidro • transparente com reflexo" };
            AssetDatabase.AddObjectToAsset(trim, library); AssetDatabase.AddObjectToAsset(glass, library);
            var buildings = UnityEngine.Object.FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None)
                .Where(l => l.category == "building").ToDictionary(l => l.name);
            var windows = new List<CityWindows.Window>(); var glazing = new List<CityWindows.Glazing>(); var lods = new List<CityObjectLod>();
            for (int i = 0; i < world.places.Length; i++)
            {
                var place = world.places[i]; string label = i < 17 ? "Reused building-" + i : "Reused residential interior " + (i - 17);
                if (!buildings.TryGetValue("building:" + label, out var building))
                {if(CityHomeBuilder.IsApartment(world,i))continue;throw new InvalidOperationException("Fachada ausente: " + label);}
                float width = place.lotWidth > 0 ? place.lotWidth : place.w, depth = place.lotDepth > 0 ? place.lotDepth : place.d;
                var origin = new Vector3(place.x, place.elevation, place.z); var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
                var worldToRoom = Matrix4x4.TRS(origin, rotation, Vector3.one).inverse;
                var openings = new List<Aperture>();
                void Add(Vector3 center, Vector3 outward, float w, float h)
                {
                    var size = outward.x == 0 ? new Vector3(w, h, .86f) : new Vector3(.86f, h, w);
                    openings.Add(new Aperture { center = center, outward = outward, width = w, height = h, cut = new Bounds(center, size) });
                }
                // Replace the existing opaque front panels, matching their authored apertures.
                foreach (var node in visual.nodes.Where(n => n.name == label && n.geometry >= 0))
                {
                    var shape = BoundsOf(visual.geometries[node.geometry]);
                    foreach (var instance in node.instances)
                    {
                        var matrix = Matrix4x4.TRS(node.position, node.rotation, node.scale) * Matrix(instance.matrix);
                        var center = worldToRoom.MultiplyPoint3x4(matrix.MultiplyPoint3x4(shape.center));
                        float w = matrix.MultiplyVector(Vector3.right * shape.size.x).magnitude;
                        float h = matrix.MultiplyVector(Vector3.up * shape.size.y).magnitude;
                        float d = matrix.MultiplyVector(Vector3.forward * shape.size.z).magnitude;
                        if (Mathf.Abs(center.z - depth * .5f) < .08f && Mathf.Abs(center.x) > 2 && d < .13f && w > 1.8f && h > 1.4f && h < 1.8f)
                            Add(center, Vector3.forward, w, h);
                    }
                }
                foreach (int side in new[] { -1, 1 })
                {
                    Add(new Vector3(side * width * .28f, 2.65f, -depth * .5f), Vector3.back, 2.8f, 2.2f);
                    foreach (int along in new[] { -1, 1 }) Add(new Vector3(side * width * .5f, 2.65f, along * depth * .26f), Vector3.right * side, 2.6f, 2.2f);
                }
                var cache = new Dictionary<Mesh, Mesh>();
                foreach (var part in building.parts)
                {
                    Mesh Cut(Mesh source)
                    {
                        if (!source || source.vertexCount == 0) return source;
                        if (!cache.TryGetValue(source, out var result)) { result = CutMesh(source, worldToRoom * part.filter.transform.localToWorldMatrix, openings, library); cache[source] = result; }
                        return result;
                    }
                    part.full = Cut(part.full); part.medium = Cut(part.medium); part.far = Cut(part.far); part.filter.sharedMesh = part.full;
                }
                var frames = new Geometry(); var panes = new Geometry();
                foreach (var opening in openings)
                {
                    var frame = Quaternion.LookRotation(opening.outward, Vector3.up);
                    Vector3 right = frame * Vector3.right;
                    foreach (int sign in new[] { -1, 1 })
                    {
                        frames.Box(opening.center + right * sign * (opening.width * .5f + .04f), new Vector3(.12f, opening.height + .24f, .49f), frame);
                        frames.Box(opening.center + Vector3.up * sign * (opening.height * .5f + .04f), new Vector3(opening.width, .12f, .49f), frame);
                    }
                    frames.Box(opening.center, new Vector3(.07f, opening.height, .12f), frame);
                    frames.Box(opening.center, new Vector3(opening.width, .065f, .12f), frame);
                    frames.Box(opening.center - Vector3.up * (opening.height * .5f + .10f) + opening.outward * .08f,
                        new Vector3(opening.width + .4f, .12f, .72f), frame);
                    var halfRight = right * opening.width * .5f; var halfUp = Vector3.up * opening.height * .5f;
                    panes.Quad(opening.center - halfRight - halfUp, opening.center + halfRight - halfUp,
                        opening.center + halfRight + halfUp, opening.center - halfRight + halfUp, opening.outward);
                    windows.Add(new CityWindows.Window { place = i, center = origin + rotation * opening.center,
                        outward = rotation * opening.outward, width = opening.width, height = opening.height });
                }
                var owner = new GameObject("Janelas • " + i).transform; owner.SetParent(root.transform, false); owner.position = origin; owner.rotation = rotation;
                var frameRenderer = Renderer(owner, frames.Save("Caixilhos • " + i, library), trim, "Caixilhos", true);
                var glassRenderer = Renderer(owner, panes.Save("Vidros • " + i, library), glass, "Vidros", false);
                glazing.Add(new CityWindows.Glazing { place = i, renderer = glassRenderer });
                var lod = owner.gameObject.AddComponent<CityObjectLod>(); lod.category = "windows"; lod.cullDistance = 550; lod.fullDistance = 35;
                lod.localBounds = new Bounds(Vector3.up * 2.65f, new Vector3(width + 1, 3.5f, depth + 1));
                lod.parts = new[] { frameRenderer, glassRenderer }.Select(r => new CityLodPart { renderer = r, filter = r.GetComponent<MeshFilter>(),
                    full = r.GetComponent<MeshFilter>().sharedMesh, medium = r.GetComponent<MeshFilter>().sharedMesh, far = r.GetComponent<MeshFilter>().sharedMesh,
                    shadows = r.shadowCastingMode }).ToArray(); lods.Add(lod);
            }
            root.windows = windows.ToArray(); root.glazing = glazing.ToArray();
            var manager = UnityEngine.Object.FindFirstObjectByType<CityLodManager>();
            if (manager) manager.objects = manager.objects.Where(l => l).Concat(lods).ToArray();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../Reports/windows"); File.WriteAllText("../Reports/windows/import.json", JsonUtility.ToJson(new WindowReport { buildings = glazing.Count, windows = windows.ToArray() }, true));
            Debug.Log("CITY_WINDOWS_BAKED buildings=" + glazing.Count + " windows=" + windows.Count);
        }

        [Serializable] sealed class WindowReport { public int buildings; public CityWindows.Window[] windows; }
        static MeshRenderer Renderer(Transform owner, Mesh mesh, Material material, string name, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(owner, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; return renderer;
        }
        static Mesh CutMesh(Mesh source, Matrix4x4 toRoom, List<Aperture> openings, UnityEngine.Object library)
        {
            var positions = source.vertices; var normals = source.normals; var uv = source.uv; var surface = source.uv2; var colors = source.colors;
            var input = new Vertex[positions.Length];
            for (int i = 0; i < input.Length; i++) input[i] = new Vertex { p = positions[i], q = toRoom.MultiplyPoint3x4(positions[i]),
                n = normals.Length > i ? normals[i] : Vector3.up, uv = uv.Length > i ? uv[i] : Vector2.zero,
                surface = surface.Length > i ? surface[i] : Vector2.zero, color = colors.Length > i ? colors[i] : Color.white };
            var triangles = source.triangles; var output = new Geometry(); bool changed = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var polygons = new List<List<Vertex>> { new List<Vertex> { input[triangles[i]], input[triangles[i + 1]], input[triangles[i + 2]] } };
                foreach (var opening in openings)
                {
                    var next = new List<List<Vertex>>();
                    foreach (var polygon in polygons)
                    {
                        var bounds = new Bounds(polygon[0].q, Vector3.zero); foreach (var vertex in polygon) bounds.Encapsulate(vertex.q);
                        if (!bounds.Intersects(opening.cut)) { next.Add(polygon); continue; }
                        var remainder = polygon;
                        for (int axis = 0; axis < 3 && remainder.Count >= 3; axis++) for (int side = 0; side < 2 && remainder.Count >= 3; side++)
                        {
                            float value = side == 0 ? opening.cut.min[axis] : opening.cut.max[axis]; float sign = side == 0 ? 1 : -1;
                            var outside = Clip(remainder, axis, value, -sign); if (outside.Count >= 3) next.Add(outside);
                            remainder = Clip(remainder, axis, value, sign);
                        }
                        if (remainder.Count >= 3) changed = true;
                    }
                    polygons = next;
                }
                foreach (var polygon in polygons) output.Polygon(polygon);
            }
            return changed ? output.Save(source.name + Suffix, library) : source;
        }
        static List<Vertex> Clip(List<Vertex> polygon, int axis, float value, float sign)
        {
            var result = new List<Vertex>(); var previous = polygon[polygon.Count - 1]; float a = (previous.q[axis] - value) * sign;
            foreach (var current in polygon)
            {
                float b = (current.q[axis] - value) * sign;
                if ((a >= 0) != (b >= 0)) result.Add(Vertex.Mix(previous, current, a / (a - b)));
                if (b >= 0) result.Add(current); previous = current; a = b;
            }
            return result;
        }
        static Matrix4x4 Matrix(float[] values)
        {
            var matrix = new Matrix4x4(); for (int c = 0; c < 4; c++) matrix.SetColumn(c, new Vector4(values[c * 4], values[c * 4 + 1], values[c * 4 + 2], values[c * 4 + 3])); return matrix;
        }
        static Bounds BoundsOf(GeometryData geometry)
        {
            var bounds = new Bounds(new Vector3(geometry.positions[0], geometry.positions[1], geometry.positions[2]), Vector3.zero);
            for (int i = 3; i < geometry.positions.Length; i += 3) bounds.Encapsulate(new Vector3(geometry.positions[i], geometry.positions[i + 1], geometry.positions[i + 2])); return bounds;
        }
    }
}
