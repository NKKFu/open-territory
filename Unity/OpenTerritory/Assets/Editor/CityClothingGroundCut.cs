using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    // Clip only the new store's interior from shared donor floors. The old upper
    // house floor otherwise creates a hidden 0.9 m ledge across the new ramp.
    public static class CityClothingGroundCut
    {
        struct Vertex
        {
            public Vector3 p, n; public Vector2 uv, uv2; public Color color;
            public static Vertex Mix(Vertex a, Vertex b, float t) => new Vertex { p = Vector3.LerpUnclamped(a.p, b.p, t), n = Vector3.LerpUnclamped(a.n, b.n, t), uv = Vector2.LerpUnclamped(a.uv, b.uv, t), uv2 = Vector2.LerpUnclamped(a.uv2, b.uv2, t), color = Color.LerpUnclamped(a.color, b.color, t) };
        }
        public static void Restore(CityClothingShop shop)
        {
            for (int i = 0; i < shop.groundColliders.Length; i++)
                if (shop.groundColliders[i] && shop.groundColliders[i].sharedMesh == shop.cutCollisionMeshes[i])
                { shop.groundColliders[i].sharedMesh = shop.originalCollisionMeshes[i]; shop.groundColliders[i].enabled = shop.originalCollisionEnabled[i]; }
            for (int i = 0; i < shop.groundRenderers.Length; i++)
                if (shop.groundRenderers[i] && shop.groundRenderers[i].sharedMesh == shop.cutGroundMeshes[i]) shop.groundRenderers[i].sharedMesh = shop.originalGroundMeshes[i];
        }
        public static HashSet<Collider> Bake(CityClothingShop shop, Object library)
        {
            var collisions = new List<MeshCollider>(); var originalCollision = new List<Mesh>(); var cutCollision = new List<Mesh>(); var enabled = new List<bool>();
            var filters = new List<MeshFilter>(); var originals = new List<Mesh>(); var cuts = new List<Mesh>();
            var inverse = shop.transform.worldToLocalMatrix;
            float Distance(Vertex v, int side)
            { var p = inverse.MultiplyPoint3x4(v.p); return side == 0 ? p.x + 20.34f : side == 1 ? 20.34f - p.x : side == 2 ? p.z + 7.03f : 7.03f - p.z; }
            List<Vertex> Clip(List<Vertex> polygon, int side, bool inside)
            {
                var result = new List<Vertex>(); if (polygon.Count == 0) return result;
                var previous = polygon[polygon.Count - 1]; float before = Distance(previous, side) * (inside ? 1 : -1);
                foreach (var current in polygon)
                {
                    float after = Distance(current, side) * (inside ? 1 : -1); bool a = before >= 0, b = after >= 0;
                    if (a != b) result.Add(Vertex.Mix(previous, current, before / (before - after)));
                    if (b) result.Add(current); previous = current; before = after;
                }
                return result;
            }
            Mesh Cut(Mesh source, Transform owner)
            {
                var points = source.vertices; var normals = source.normals; var us = source.uv; var vs = source.uv2; var colors = source.colors; var tris = source.triangles;
                var matrix = owner.localToWorldMatrix; var back = matrix.inverse; var outVertices = new List<Vertex>(); bool changed = false;
                Vertex Read(int i) => new Vertex { p = matrix.MultiplyPoint3x4(points[i]), n = normals.Length == points.Length ? normals[i] : Vector3.up,
                    uv = us.Length == points.Length ? us[i] : Vector2.zero, uv2 = vs.Length == points.Length ? vs[i] : Vector2.zero, color = colors.Length == points.Length ? colors[i] : Color.white };
                void Add(List<Vertex> p)
                { for (int j = 1; j + 1 < p.Count; j++) if (Vector3.Cross(p[j].p - p[0].p, p[j + 1].p - p[0].p).sqrMagnitude > 1e-12f) { outVertices.Add(p[0]); outVertices.Add(p[j]); outVertices.Add(p[j + 1]); } }
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = Read(tris[i]); var b = Read(tris[i + 1]); var c = Read(tris[i + 2]); var polygon = new List<Vertex> { a, b, c };
                    bool outside = false;
                    for (int side = 0; side < 4; side++) if (Distance(a, side) <= 0 && Distance(b, side) <= 0 && Distance(c, side) <= 0) { outside = true; break; }
                    if (outside) { Add(polygon); continue; }
                    // Each outside fragment retains its world-space position and every vertex attribute.
                    var inside = polygon;
                    for (int side = 0; side < 4 && inside.Count >= 3; side++) { Add(Clip(inside, side, false)); inside = Clip(inside, side, true); }
                    if (inside.Count >= 3) changed = true;
                }
                if (!changed) return source;
                var mesh = new Mesh { name = source.name + " • Tideline interior cut", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(outVertices.Select(v => back.MultiplyPoint3x4(v.p)).ToList()); mesh.SetNormals(outVertices.Select(v => v.n.normalized).ToList());
                mesh.SetUVs(0, outVertices.Select(v => v.uv).ToList()); mesh.SetUVs(1, outVertices.Select(v => v.uv2).ToList()); mesh.SetColors(outVertices.Select(v => v.color).ToList());
                mesh.SetTriangles(Enumerable.Range(0, outVertices.Count).ToArray(), 0); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
            bool Overlaps(Mesh mesh, Transform owner)
            {
                var b = mesh.bounds; var local = new Bounds(inverse.MultiplyPoint3x4(owner.TransformPoint(b.center)), Vector3.zero);
                for (int i = 0; i < 8; i++) local.Encapsulate(inverse.MultiplyPoint3x4(owner.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
                return local.max.x > -20.34f && local.min.x < 20.34f && local.max.z > -7.03f && local.min.z < 7.03f;
            }
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if (!c.name.StartsWith("Piso-") || !c.sharedMesh || !Overlaps(c.sharedMesh, c.transform)) continue;
                var original = c.sharedMesh; var cut = Cut(original, c.transform); if (cut == original) continue;
                collisions.Add(c); originalCollision.Add(original); cutCollision.Add(cut.vertexCount == 0 ? null : cut); enabled.Add(c.enabled);
                c.sharedMesh = cut.vertexCount == 0 ? null : cut; c.enabled = c.enabled && cut.vertexCount != 0;
            }
            foreach (var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!f.sharedMesh || !f.transform.parent || !f.transform.parent.name.StartsWith("surface:") || !Overlaps(f.sharedMesh, f.transform)) continue;
                var r = f.GetComponent<Renderer>(); if (r && r.sharedMaterial && r.sharedMaterial.HasProperty("_Sea") && r.sharedMaterial.GetFloat("_Sea") > .5f) continue;
                var original = f.sharedMesh; var cut = Cut(original, f.transform); if (cut == original) continue;
                filters.Add(f); originals.Add(original); cuts.Add(cut); f.sharedMesh = cut;
            }
            shop.groundColliders = collisions.ToArray(); shop.originalCollisionMeshes = originalCollision.ToArray(); shop.cutCollisionMeshes = cutCollision.ToArray(); shop.originalCollisionEnabled = enabled.ToArray();
            shop.groundRenderers = filters.ToArray(); shop.originalGroundMeshes = originals.ToArray(); shop.cutGroundMeshes = cuts.ToArray();
            return new HashSet<Collider>(collisions);
        }
    }
}
