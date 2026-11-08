using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        // One synchronous workspace per city. Search generations avoid clearing the
        // whole graph, and heap positions support decrease-key without duplicate nodes.
        sealed class RouteWorkspace
        {
            public readonly float[] cost, priority;
            public readonly int[] previous;
            readonly int[] visited, closed, heap, position, insertion;
            int generation, count, sequence;
            public int Capacity => cost.Length;
            public int Count => count;

            public RouteWorkspace(int capacity)
            {
                cost = new float[capacity]; priority = new float[capacity]; previous = new int[capacity];
                visited = new int[capacity]; closed = new int[capacity]; heap = new int[capacity];
                position = new int[capacity]; insertion = new int[capacity];
            }
            public void Begin()
            {
                if (generation == int.MaxValue)
                { Array.Clear(visited, 0, visited.Length); Array.Clear(closed, 0, closed.Length); generation = 0; }
                generation++; count = sequence = 0;
            }
            public void Touch(int node)
            {
                if (visited[node] == generation) return;
                visited[node] = generation; cost[node] = float.PositiveInfinity;
                previous[node] = position[node] = -1;
            }
            public bool IsClosed(int node) => closed[node] == generation;
            public bool Reached(int node) => visited[node] == generation && !float.IsPositiveInfinity(cost[node]);
            bool Before(int a, int b)
            {
                // Preserve the old open list's first-in tie breaking, even after a decrease-key.
                return priority[a] < priority[b] || priority[a] == priority[b] && insertion[a] < insertion[b];
            }
            void Swap(int a, int b)
            {
                int node = heap[a]; heap[a] = heap[b]; heap[b] = node;
                position[heap[a]] = a; position[heap[b]] = b;
            }
            public void Offer(int node, float score)
            {
                priority[node] = score; int at = position[node];
                if (at < 0)
                { at = count++; heap[at] = node; position[node] = at; insertion[node] = sequence++; }
                while (at > 0)
                {
                    int parent = (at - 1) / 2;
                    if (!Before(heap[at], heap[parent])) break;
                    Swap(at, parent); at = parent;
                }
            }
            public int Take()
            {
                int first = heap[0]; position[first] = -1; count--;
                if (count > 0)
                {
                    heap[0] = heap[count]; position[heap[0]] = 0; int at = 0;
                    while (at * 2 + 1 < count)
                    {
                        int child = at * 2 + 1;
                        if (child + 1 < count && Before(heap[child + 1], heap[child])) child++;
                        if (!Before(heap[child], heap[at])) break;
                        Swap(at, child); at = child;
                    }
                }
                closed[first] = generation; return first;
            }
        }
        sealed class NodeDistanceComparer : IComparer<KeyValuePair<int, float>>
        {
            public static readonly NodeDistanceComparer Instance = new NodeDistanceComparer();
            public int Compare(KeyValuePair<int, float> a, KeyValuePair<int, float> b) => a.Value.CompareTo(b.Value);
        }
        readonly List<KeyValuePair<int, float>> reachableCandidates = new List<KeyValuePair<int, float>>(128);
        RouteWorkspace routeWorkspace;

        bool FindRoute(Resident person, Vector3 goal)
        {
            person.Path.Clear(); person.PathIndex = 0; person.RouteGoal = goal;
            int start = ReachableNode(person.Root.position, person), end = ReachableNode(goal, person);
            if (start < 0 || end < 0) return false;
            var nav = ctx.Data.nav;
            if (routeWorkspace == null || routeWorkspace.Capacity != nav.Length) routeWorkspace = new RouteWorkspace(nav.Length);
            var search = routeWorkspace; search.Begin(); search.Touch(start); search.cost[start] = 0;
            var target = new Vector2(nav[end].x, nav[end].z);
            search.Offer(start, Vector2.Distance(new Vector2(nav[start].x, nav[start].z), target));
            int iterations = 0;
            while (search.Count > 0 && iterations++ < nav.Length)
            {
                int at = search.Take(); if (at == end) break;
                foreach (var edge in nav[at].edges)
                {
                    if (!navIndex.TryGetValue(edge.to, out int next) || search.IsClosed(next) || ctx.LifeNavigation.Blocked(nav[at].id, edge.to)) continue;
                    search.Touch(next);
                    float variation = Mathf.Repeat(Mathf.Sin((Mathf.Min(at, next) + 1) * 12.9898f + Mathf.Max(at, next) * .718f + person.RouteSalt) * 43758.54f, 1);
                    float proposed = search.cost[at] + Mathf.Max(.1f, edge.cost) * (1 + variation * .38f);
                    if (proposed >= search.cost[next]) continue;
                    search.cost[next] = proposed; search.previous[next] = at;
                    search.Offer(next, proposed + Vector2.Distance(new Vector2(nav[next].x, nav[next].z), target));
                }
            }
            if (!search.Reached(end)) return false;
            for (int at = end, guard = 0; at >= 0 && guard++ < nav.Length; at = search.previous[at])
            { person.Path.Add(at); if (at == start) break; }
            if (person.Path.Count == 0 || person.Path[person.Path.Count - 1] != start) { person.Path.Clear(); return false; }
            person.Path.Reverse(); return true;
        }
    }
}
