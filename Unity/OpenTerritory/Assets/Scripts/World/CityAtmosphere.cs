using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // Shared cloud meshes are made once. Tree movement stays on the GPU; trunk
    // colliders, LOD meshes and imported material assets are never modified.
    public sealed class CityAtmosphere : MonoBehaviour
    {
        const int Grid = 8;
        const float Span = 3200;
        sealed class Cloud { public Transform root; public MeshRenderer renderer; public Vector3 origin, scale; }
        sealed class Foliage { public MeshRenderer renderer; public Material original; public Bounds bounds; }
        readonly List<Cloud> clouds = new List<Cloud>();
        readonly List<Foliage> foliage = new List<Foliage>();
        readonly List<Mesh> cloudMeshes = new List<Mesh>();
        readonly Dictionary<Material, Material> windMaterials = new Dictionary<Material, Material>();
        CityContext ctx;
        Material cloudMaterial;
        public Vector3 Drift { get; private set; }
        public int CloudCount => clouds.Count;
        public int WindRendererCount { get; private set; }
        public int GroundCoverRendererCount { get; private set; }
        public CityWind Wind { get; private set; }

        public void Initialize(CityContext context)
        {
            ctx = context;
            BindFoliage();
            var wind = new GameObject("Rajadas e folhas ao vento"); wind.transform.SetParent(transform, false);
            Wind = wind.AddComponent<CityWind>(); Wind.Initialize(ctx);
            // Resources keeps the shader available in standalone builds as well.
            var shader = Resources.Load<Shader>("Shaders/CityCloud");
            if (!shader) { Debug.LogError("Cloud shader missing"); return; }
            cloudMaterial = new Material(shader) { name = "Nuvens da baía" };
            for (int i = 0; i < 6; i++) cloudMeshes.Add(CreateCloudMesh(i));
            var random = new System.Random(73129);
            var block = new MaterialPropertyBlock();
            for (int z = 0; z < Grid; z++) for (int x = 0; x < Grid; x++)
            {
                var go = new GameObject("Nuvem • " + clouds.Count);
                go.transform.SetParent(transform, false);
                go.transform.rotation = Quaternion.Euler(0, Range(random, -32, 32), 0);
                go.AddComponent<MeshFilter>().sharedMesh = cloudMeshes[(x + z * 3) % cloudMeshes.Count];
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = cloudMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                block.Clear(); block.SetFloat("_CloudRank", Range(random, 0, 1)); renderer.SetPropertyBlock(block);
                clouds.Add(new Cloud {
                    root = go.transform, renderer = renderer,
                    origin = new Vector3((x + Range(random, .12f, .88f)) * Span / Grid - Span * .5f,
                        Range(random, 215, 305), (z + Range(random, .12f, .88f)) * Span / Grid - Span * .5f),
                    scale = new Vector3(Range(random, .85f, 1.45f), Range(random, .85f, 1.3f), Range(random, .85f, 1.35f))
                });
            }
            UpdateClouds(0);
        }

        public int DrawSunOccluders(CommandBuffer commands, Camera camera, Plane[] frustum)
        {
            if (!cloudMaterial || !gameObject.activeInHierarchy) return 0;
            int pass = cloudMaterial.FindPass("SunOcclusion"), count = 0;
            if (pass < 0) return 0;
            foreach (var cloud in clouds)
            {
                var renderer = cloud.renderer;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)) continue;
                commands.DrawRenderer(renderer, cloudMaterial, 0, pass); count++;
            }
            return count;
        }

        void BindFoliage()
        {
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var material = renderer.sharedMaterial;
                if (!material || !material.HasProperty("_Foliage")) continue;
                // Existing scenes predate the material flag; the importer writes it
                // for new scenes. One runtime copy also supports the current scene.
                bool tree = material.GetFloat("_Foliage") > .5f || material.name.StartsWith("Folhagem e casca", StringComparison.Ordinal);
                float groundCover = material.HasProperty("_GroundCover") ? material.GetFloat("_GroundCover") : 0;
                if (!tree && groundCover < .5f) continue;
                if (!windMaterials.TryGetValue(material, out var animated))
                {
                    animated = new Material(material) { name = material.name + " • vento" };
                    if (tree) animated.SetFloat("_Foliage", 1); windMaterials.Add(material, animated);
                }
                foliage.Add(new Foliage { renderer = renderer, original = material, bounds = renderer.localBounds });
                renderer.sharedMaterial = animated;
                if (tree) WindRendererCount++; else GroundCoverRendererCount++;
                var bounds = renderer.localBounds; bounds.Expand(tree ? new Vector3(8,0,8) : new Vector3(1.4f,.2f,1.4f)); renderer.localBounds = bounds;
            }
        }

        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            UpdateClouds(Time.deltaTime);
        }

        void UpdateClouds(float dt)
        {
            var weather = ctx.Systems.Weather;
            Drift += weather.Wind * (dt * 2.2f);
            // Bound long-running sessions without changing any visible position.
            Drift = new Vector3(Mathf.Repeat(Drift.x, Span), 0, Mathf.Repeat(Drift.z, Span));
            Vector3 eye = ctx.Camera.transform.position;
            float growth = Mathf.Lerp(1, 1.65f, weather.Cloud);
            foreach (var cloud in clouds)
            {
                Vector3 p = cloud.origin + Drift;
                p.x = eye.x + Mathf.Repeat(p.x - eye.x + Span * .5f, Span) - Span * .5f;
                p.z = eye.z + Mathf.Repeat(p.z - eye.z + Span * .5f, Span) - Span * .5f;
                cloud.root.position = p; cloud.root.localScale = cloud.scale * growth;
            }
            float storm = Mathf.Clamp01(weather.Rain * .82f + weather.Cloud * .18f);
            var lit = Color.Lerp(new Color(.98f, .97f, .89f), new Color(.53f, .59f, .62f), storm);
            var shade = Color.Lerp(new Color(.70f, .79f, .81f), new Color(.28f, .35f, .40f), storm);
            Shader.SetGlobalColor("_CityCloudLit", Color.Lerp(lit, new Color(.12f, .18f, .25f), ctx.Night));
            Shader.SetGlobalColor("_CityCloudShade", Color.Lerp(shade, new Color(.045f, .085f, .13f), ctx.Night));
            Shader.SetGlobalFloat("_CityCloudCover", weather.Cloud);
            Shader.SetGlobalFloat("_CityCloudHaze", weather.Fog);
            Shader.SetGlobalVector("_CityWind", weather.Wind);
            Shader.SetGlobalFloat("_CityTime", ctx.Clock);
        }

        static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        static Mesh CreateCloudMesh(int variant)
        {
            var random = new System.Random(931 + variant * 137);
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            Action<Vector3, Vector3, Vector3> face = (a, b, c) => {
                int at = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                var normal = Vector3.Cross(b - a, c - a).normalized;
                float shade = .53f + Mathf.Max(0, normal.y) * .4f + Mathf.Max(0, Vector3.Dot(normal, new Vector3(-.6f, 0, -.8f))) * .12f;
                var color = new Color(shade, shade, shade, 1); colors.Add(color); colors.Add(color); colors.Add(color);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
            };
            var ring = new[] { new Vector2(-.65f,-1), new Vector2(-1,-.65f), new Vector2(-1,.65f), new Vector2(-.65f,1),
                new Vector2(.65f,1), new Vector2(1,.65f), new Vector2(1,-.65f), new Vector2(.65f,-1) };
            for (int block = 0; block < 6; block++)
            {
                var center = block == 0 ? Vector3.zero : new Vector3(Range(random, -85, 85), Range(random, 3, 25), Range(random, -34, 34));
                var size = block == 0 ? new Vector3(225, 27, 112) : new Vector3(Range(random, 85, 155), Range(random, 25, 52), Range(random, 68, 115));
                var rings = new Vector3[4, 8];
                for (int level = 0; level < 4; level++) for (int i = 0; i < 8; i++)
                {
                    float width = level == 0 || level == 3 ? .78f : 1;
                    float y = level == 0 ? -.5f : level == 1 ? -.28f : level == 2 ? .28f : .5f;
                    rings[level, i] = center + Vector3.Scale(new Vector3(ring[i].x * width, y, ring[i].y * width), Vector3.Scale(size, new Vector3(.5f, 1, .5f)));
                }
                for (int level = 0; level < 3; level++) for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    face(rings[level, i], rings[level + 1, next], rings[level + 1, i]);
                    face(rings[level, i], rings[level, next], rings[level + 1, next]);
                }
                for (int i = 1; i < 7; i++)
                {
                    face(rings[3, 0], rings[3, i], rings[3, i + 1]);
                    face(rings[0, 0], rings[0, i + 1], rings[0, i]);
                }
            }
            var mesh = new Mesh { name = "Nuvem facetada • " + variant };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void OnDestroy()
        {
            foreach (var entry in foliage) if (entry.renderer)
            { entry.renderer.sharedMaterial = entry.original; entry.renderer.localBounds = entry.bounds; }
            foreach (var material in windMaterials.Values) if (material) Destroy(material);
            foreach (var mesh in cloudMeshes) if (mesh) Destroy(mesh);
            if (cloudMaterial) Destroy(cloudMaterial);
        }
    }
}
