using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Scene-authored relocation. Bind the matching gameplay records before any
    // population, doors, map, navigation or interactions consume the manifest.
    public sealed class CityFireStationSite : MonoBehaviour
    {
        public const int PlaceIndex = 3;
        public PlaceData place;
        public SolidData[] solids = Array.Empty<SolidData>();
        public int[] originalSolidIds = Array.Empty<int>(), originalSolidOwners = Array.Empty<int>();
        public ServiceData service;
        public DeviceData device;
        public ResidentData firefighter;
        public NavNodeData[] entrance = Array.Empty<NavNodeData>();
        public int connection;
        public float streetLocalZ = 32.4f;
        public int movedColliders, floorTriangles, clearedCoverTriangles;

        static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        public static void Bind(CityContext ctx)
        {
            var site = FindFirstObjectByType<CityFireStationSite>();
            if (!site || site.place == null) return;
            ctx.Data.places[PlaceIndex] = Copy(site.place);
            long Key(int index, int owner) => ((long)index << 32) | (uint)owner;
            var moved = new Dictionary<long, SolidData>();
            for (int i = 0; i < site.solids.Length; i++)
                moved[Key(i < site.originalSolidIds.Length ? site.originalSolidIds[i] : site.solids[i].index,
                    i < site.originalSolidOwners.Length ? site.originalSolidOwners[i] : site.solids[i].owner)] = site.solids[i];
            for (int i = 0; i < ctx.Data.solids.Length; i++)
                if (moved.TryGetValue(Key(ctx.Data.solids[i].index, ctx.Data.solids[i].owner), out var solid)) ctx.Data.solids[i] = Copy(solid);
            if (site.service != null)
                ctx.Data.services = ctx.Data.services.Where(s => s.id != site.service.id).Append(Copy(site.service)).ToArray();
            if (site.device != null)
                ctx.Data.devices = ctx.Data.devices.Where(d => d.id != site.device.id).Append(Copy(site.device)).ToArray();
            if (site.firefighter != null)
                ctx.Data.residents = ctx.Data.residents.Where(r => r.id != site.firefighter.id).Append(Copy(site.firefighter)).ToArray();

            var ids = new HashSet<int>(site.entrance.Select(n => n.id));
            foreach (var node in ctx.Data.nav)
                node.edges = node.edges.Where(e => !ids.Contains(e.to)).ToArray();
            ctx.Data.nav = ctx.Data.nav.Where(n => !ids.Contains(n.id)).Concat(site.entrance.Select(Copy)).ToArray();
            var sidewalk = ctx.Data.nav.First(n => n.id == site.connection);
            var last = site.entrance.Last();
            sidewalk.edges = sidewalk.edges.Append(new NavEdgeData { to = last.id,
                cost = Vector2.Distance(new Vector2(sidewalk.x, sidewalk.z), new Vector2(last.x, last.z)) }).ToArray();
        }
    }
}
