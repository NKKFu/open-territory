#if CITY_PERFORMANCE_REFERENCE
// Historical algorithms for the controlled native A/B build only.
// This entire file is excluded from ordinary builds; see CityPerformanceBuild.
using System.Collections.Generic;
using UnityEngine;
namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        bool ReferenceRoute(Resident p, Vector3 goal)
        {
            p.Path.Clear(); p.PathIndex = 0; p.RouteGoal = goal;
            int start = ReferenceReachableNode(p.Root.position, p), end = ReferenceReachableNode(goal, p); if (start < 0 || end < 0) return false;
            int n = ctx.Data.nav.Length; var cost = new float[n]; var previous = new int[n]; var closed = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; } cost[start] = 0;
            var open = new List<int> { start }; int iterations = 0;
            while (open.Count > 0 && iterations++ < n)
            {
                int selected = 0; float best = float.MaxValue;
                for (int j = 0; j < open.Count; j++) { int index = open[j]; var node = ctx.Data.nav[index]; float score = cost[index] + Vector2.Distance(new Vector2(node.x, node.z), new Vector2(ctx.Data.nav[end].x, ctx.Data.nav[end].z)); if (score < best) { selected = j; best = score; } }
                int at = open[selected]; open.RemoveAt(selected); if (at == end) break; if (closed[at]) continue; closed[at] = true;
                foreach (var edge in ctx.Data.nav[at].edges)
                {
                    if (!navIndex.TryGetValue(edge.to, out int next) || closed[next] || ctx.LifeNavigation.Blocked(ctx.Data.nav[at].id, edge.to)) continue;
                    // A stable preference per journey varies the chosen side streets
                    // without making the route flicker as the character walks.
                    float variation = Mathf.Repeat(Mathf.Sin((Mathf.Min(at, next) + 1) * 12.9898f + Mathf.Max(at, next) * .718f + p.RouteSalt) * 43758.54f, 1);
                    float proposed = cost[at] + Mathf.Max(.1f, edge.cost) * (1 + variation * .38f); if (proposed >= cost[next]) continue;
                    cost[next] = proposed; previous[next] = at; if (!open.Contains(next)) open.Add(next);
                }
            }
            if (float.IsPositiveInfinity(cost[end])) return false;
            for (int at = end, guard = 0; at >= 0 && guard++ <= n; at = previous[at]) { p.Path.Insert(0, at); if (at == start) break; }
            return true;
        }
        int ReferenceReachableNode(Vector3 point, Resident p)
        {
            var nearest = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < ctx.Data.nav.Length; i++)
            {
                var n = ctx.Data.nav[i]; float distance = new Vector2(n.x - point.x, n.z - point.z).sqrMagnitude;
                if (distance < 24 * 24 && n.edges.Length > 0) nearest.Add(new KeyValuePair<int, float>(i, distance));
            }
            nearest.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < Mathf.Min(20, nearest.Count); i++)
            {
                var n = ctx.Data.nav[nearest[i].Key]; Vector3 at = new Vector3(n.x, ctx.Ground(n.x, n.z) + .04f, n.z);
                if (ClearWalk(point, at, p)) return nearest[i].Key;
            }
            return -1;
        }
    }
    public sealed partial class CityDestruction
    {
        sealed class Fragment { public Rigidbody body; public float until; }
        void PrepareFragmentPool() { }
        void EmitFragments(CityDestructible prop, Bounds bounds, Vector3 impulse)
        {
            int pieces = Mathf.Clamp(Mathf.CeilToInt(bounds.size.magnitude * 1.5f), 4, 9);
            for (int i = 0; i < pieces; i++)
            {
                while (fragments.Count >= DebrisLimit) RemoveFragment();
                var piece = new GameObject("Estilhaço • " + prop.category); piece.transform.position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, .18f + .64f * ((i * 37 % 13) / 12f)),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, .14f + .7f * ((i * 7 % 11) / 10f)),
                    Mathf.Lerp(bounds.min.z, bounds.max.z, .15f + .7f * ((i * 5 % 9) / 8f)));
                piece.transform.rotation = prop.transform.rotation * Quaternion.Euler(i * 31, i * 47, i * 19);
                piece.transform.localScale = new Vector3(Mathf.Clamp(bounds.size.x / 4, .07f, .48f), Mathf.Clamp(bounds.size.y / 5, .06f, .30f), Mathf.Clamp(bounds.size.z / 4, .07f, .44f));
                piece.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = piece.AddComponent<MeshRenderer>(); renderer.sharedMaterial = fragmentMaterial;
                Color color = new Color(.48f, .39f, .29f);
                if (prop.visuals.Length > 0 && prop.visuals[i % prop.visuals.Length] && prop.visuals[i % prop.visuals.Length].sharedMaterial)
                    color = prop.visuals[i % prop.visuals.Length].sharedMaterial.color;
                var meshFilter = prop.visuals.Length > 0 && prop.visuals[0] ? prop.visuals[0].GetComponent<MeshFilter>() : null;
                if (meshFilter && meshFilter.sharedMesh && meshFilter.sharedMesh.colors32.Length > 0)
                { var colors = meshFilter.sharedMesh.colors32; color *= (Color)colors[(i * 24) % colors.Length]; }
                tint.Clear(); tint.SetColor("_Color", color); renderer.SetPropertyBlock(tint);
                var shape = piece.AddComponent<BoxCollider>();
                shape.excludeLayers = (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer);
                var body = piece.AddComponent<Rigidbody>(); body.mass = .35f; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearVelocity = Vector3.ClampMagnitude(impulse, 9) + new Vector3(Mathf.Sin(i * 2.4f) * 1.7f, 1.2f + i * .11f, Mathf.Cos(i * 1.6f) * 1.5f);
                body.angularVelocity = new Vector3(i + 1, 2 - i, 3) * .7f;
                fragments.Enqueue(new Fragment { body = body, until = ctx.Clock + 8 });
            }
        }
        void RemoveFragment()
        {
            var item = fragments.Dequeue();
            if (item.body) { item.body.gameObject.SetActive(false); Destroy(item.body.gameObject); }
        }
        void DisposeFragmentPool() { while (fragments.Count > 0) RemoveFragment(); }
    }
}
#endif
