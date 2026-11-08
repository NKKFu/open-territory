using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // Static cartography shares the manifest's coordinates with the physical city.
    // No second camera, scene copy or per-frame terrain rendering is needed.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CityMapGraphic : MaskableGraphic
    {
        struct Road { public Vector2[] points; public float width; public bool highway; }
        readonly List<Road> roads = new List<Road>();
        WorldData world;
        Rect bounds;

        public void Initialize(WorldData data, Rect area)
        {
            world = data; bounds = area; raycastTarget = false;
            if (ReferenceMap.Active)
            {
                foreach (var road in ReferenceMap.Layout.roads)
                {
                    var points = new List<Vector2>();
                    for (int i = 0; i < road.points.Length; i++)
                    {
                        var p = new Vector2(road.points[i].x, road.points[i].z);
                        if (points.Count == 0 || i == road.points.Length - 1 || (p - points[points.Count - 1]).sqrMagnitude >= 64)
                            points.Add(p);
                    }
                    roads.Add(new Road { points = points.ToArray(), width = road.width, highway = road.kind == "highway" });
                }
            }
            else
            {
                var points = new Vector2[data.roadRoute.Length];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector2(data.roadRoute[i].x, data.roadRoute[i].z);
                roads.Add(new Road { points = points, width = 10 });
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (world == null) return;
            var center = bounds.center;
            foreach (var p in world.places)
            {
                float w = p.lotWidth > 0 ? p.lotWidth : p.w, d = p.lotDepth > 0 ? p.lotDepth : p.d;
                var q = Quaternion.Euler(0, 0, -p.yaw * Mathf.Rad2Deg);
                var origin = new Vector2(p.x, p.z) - center;
                var a = origin + (Vector2)(q * new Vector3(-w / 2, -d / 2));
                var b = origin + (Vector2)(q * new Vector3(-w / 2, d / 2));
                var c = origin + (Vector2)(q * new Vector3(w / 2, d / 2));
                var e = origin + (Vector2)(q * new Vector3(w / 2, -d / 2));
                Quad(mesh, a, b, c, e, p.kind == "home" || p.kind == "residence" ? new Color(.52f, .59f, .49f) : new Color(.77f, .68f, .52f));
            }
            foreach (var road in roads) Stroke(mesh, road, road.width + 3, new Color(.32f, .40f, .36f));
            foreach (var road in roads) Stroke(mesh, road, road.width, road.highway ? new Color(.96f, .82f, .53f) : new Color(.87f, .88f, .77f));
            foreach (var dock in world.docks)
            {
                var q = Quaternion.Euler(0, 0, -dock.yaw * Mathf.Rad2Deg); var p = new Vector2(dock.x, dock.z) - center;
                Quad(mesh, p + (Vector2)(q * new Vector3(-dock.w / 2, -dock.d / 2)), p + (Vector2)(q * new Vector3(-dock.w / 2, dock.d / 2)),
                    p + (Vector2)(q * new Vector3(dock.w / 2, dock.d / 2)), p + (Vector2)(q * new Vector3(dock.w / 2, -dock.d / 2)), new Color(.78f, .66f, .46f));
            }
        }

        void Stroke(VertexHelper mesh, Road road, float width, Color color)
        {
            int start = mesh.currentVertCount;
            for (int i = 0; i < road.points.Length; i++)
            {
                var direction = road.points[Mathf.Min(i + 1, road.points.Length - 1)] - road.points[Mathf.Max(i - 1, 0)];
                var side = new Vector2(-direction.y, direction.x).normalized * width * .5f;
                var point = road.points[i] - bounds.center;
                mesh.AddVert(point - side, color, Vector2.zero); mesh.AddVert(point + side, color, Vector2.zero);
                if (i == 0) continue;
                int at = start + i * 2; mesh.AddTriangle(at - 2, at - 1, at); mesh.AddTriangle(at, at - 1, at + 1);
            }
        }

        internal static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int at = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }

        internal static Texture2D Terrain(WorldData data)
        {
            var g = data.ground;
            if (g == null || g.width < 2 || g.height < 2 || g.heights.Length != g.width * g.height) return Texture2D.whiteTexture;
            var pixels = new Color32[g.heights.Length];
            for (int z = 0; z < g.height; z++) for (int x = 0; x < g.width; x++)
            {
                int at = z * g.width + x; float h = g.heights[at]; Color c;
                if (h < CityMarine.SeaLevel)
                    c = Color.Lerp(new Color(.18f, .35f, .44f), new Color(.32f, .53f, .55f), Mathf.InverseLerp(-22, CityMarine.SeaLevel, h));
                else
                {
                    c = Color.Lerp(new Color(.67f, .73f, .58f), new Color(.43f, .54f, .42f), Mathf.InverseLerp(0, 105, h));
                    float dx = g.heights[z * g.width + Mathf.Min(x + 1, g.width - 1)] - h;
                    float dz = g.heights[Mathf.Min(z + 1, g.height - 1) * g.width + x] - h;
                    c *= Mathf.Clamp(1 + (dz - dx) * .018f, .83f, 1.12f);
                    // Quiet elevation bands read as a coastal survey, including at night.
                    if (h > 12 && Mathf.Repeat(h, 12) < 1.25f) c *= .93f;
                    if (h < CityMarine.SeaLevel + 1.2f) c = Color.Lerp(c, new Color(.81f, .78f, .60f), .6f);
                }
                pixels[at] = c;
            }
            var texture = new Texture2D(g.width, g.height, TextureFormat.RGB24, false) { name = "Carta da Baía · relevo", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
        }
    }

    // Constant-size map symbols remain legible at every zoom level.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CityMapSymbols : MaskableGraphic
    {
        public CityMapUI Map;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (!Map || !Map.IsOpen) return;
            foreach (var place in Map.Places)
            {
                if (place.bus && !Map.ShowStops) continue;
                var p = Map.WorldToLocal(place.point);
                Circle(mesh, p, place.bus ? 4 : 6, new Color(.04f, .13f, .17f));
                Circle(mesh, p, place.bus ? 2 : 3.6f, place.bus ? new Color(.52f, .81f, .87f) : new Color(.99f, .85f, .58f));
            }
            if (Map.Marker.HasValue)
            {
                var p = Map.WorldToLocal(Map.Marker.Value);
                Circle(mesh, p, 12, new Color(.10f, .20f, .25f)); Circle(mesh, p, 9, new Color(.99f, .66f, .39f));
                Circle(mesh, p, 4, new Color(.10f, .20f, .25f));
            }
            var player = Map.WorldToLocal(Map.PlayerPoint); float angle = -Map.PlayerYaw * Mathf.Deg2Rad;
            var forward = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)); var side = new Vector2(forward.y, -forward.x);
            Circle(mesh, player, 13, new Color(.03f, .13f, .18f));
            int at = mesh.currentVertCount;
            mesh.AddVert(player + forward * 12, Color.white, Vector2.zero);
            mesh.AddVert(player - forward * 7 - side * 7, Color.white, Vector2.zero);
            mesh.AddVert(player - forward * 3, Color.white, Vector2.zero);
            mesh.AddVert(player - forward * 7 + side * 7, Color.white, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }
        static void Circle(VertexHelper mesh, Vector2 center, float radius, Color color)
        {
            int at = mesh.currentVertCount; mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= 20; i++)
            {
                float a = i * Mathf.PI * .1f; mesh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(at, at + i, at + i + 1);
            }
        }
    }
}
