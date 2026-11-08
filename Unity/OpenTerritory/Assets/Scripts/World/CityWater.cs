using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // Geometry is prepared once; the GPU animates it using the same waves as buoyancy.
    public sealed class CityWater : MonoBehaviour
    {
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Renderer> originals = new List<Renderer>();
        CityContext ctx;
        public static float Wave(float x, float z, float time, float sea, float depth)
        {
            float a = (.14f + Mathf.Clamp01(sea) * .22f) * Mathf.SmoothStep(0, 1, depth / 3);
            return a * (Mathf.Sin(x * .105f + z * .14f + time * 1.12f)
                + .42f * Mathf.Sin(-x * .19f + z * .085f + time * 1.57f));
        }
        public void Initialize(CityContext context)
        {
            ctx = context;
            foreach (var source in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var material = source.sharedMaterial;
                if (!material || !material.HasProperty("_Sea") || material.GetFloat("_Sea") < .5f) continue;
                var bounds = source.bounds;
                // Preserve the imported extent. Tiles allow ordinary camera frustum culling.
                for (float z = bounds.min.z; z < bounds.max.z; z += 256)
                for (float x = bounds.min.x; x < bounds.max.x; x += 256)
                    Tile(x, z, Mathf.Min(256, bounds.max.x - x), Mathf.Min(256, bounds.max.z - z), material);
                originals.Add(source); source.enabled = false;
            }
        }
        void Tile(float x, float z, float width, float length, Material material)
        {
            int nx = Mathf.CeilToInt(width / 8), nz = Mathf.CeilToInt(length / 8), stride = nx + 1;
            var p = new Vector3[stride * (nz + 1)]; var normals = new Vector3[p.Length];
            var tangents = new Vector4[p.Length]; var uv = new Vector2[p.Length];
            var codes = new Vector2[p.Length]; var colors = new Color[p.Length];
            var triangles = new int[nx * nz * 6];
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
            {
                int at = j * stride + i; float px = x + width * i / nx, pz = z + length * j / nz;
                p[at] = new Vector3(px, CityMarine.SeaLevel, pz); normals[at] = Vector3.up;
                tangents[at] = new Vector4(1, 0, 0, 1); uv[at] = new Vector2(px, pz);
                float depth = ctx.Marine.Depth(px, pz);
                codes[at] = new Vector2(0, depth); colors[at] = Color.white;
                if (i == nx || j == nz) continue;
                int t = (j * nx + i) * 6;
                triangles[t] = at; triangles[t + 1] = at + stride; triangles[t + 2] = at + 1;
                triangles[t + 3] = at + 1; triangles[t + 4] = at + stride; triangles[t + 5] = at + stride + 1;
            }
            var mesh = new Mesh { name = "Ondas da baía", vertices = p, normals = normals, tangents = tangents, uv = uv, uv2 = codes, colors = colors, triangles = triangles };
            mesh.RecalculateBounds(); var b = mesh.bounds; b.Expand(new Vector3(0, 2, 0)); mesh.bounds = b;
            meshes.Add(mesh);
            var tile = new GameObject("Mar • ondas"); tile.transform.SetParent(transform, false);
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = tile.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
        }
        void LateUpdate()
        {
            if (!ctx || !ctx.Ready) return;
            Shader.SetGlobalFloat("_CityTime", ctx.Clock);
            Shader.SetGlobalFloat("_CitySeaState", ctx.Systems.Weather.SeaState);
        }
        void OnDestroy()
        {
            foreach (var source in originals) if (source) source.enabled = true;
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
        }
    }
}
