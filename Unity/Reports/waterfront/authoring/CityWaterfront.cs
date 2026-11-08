using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Authored scenery and the ordinary seat interactions used by the city.
    public sealed class CityWaterfront : MonoBehaviour
    {
        public Vector3[] route = Array.Empty<Vector3>();
        public Vector3[] gardenEntrances = Array.Empty<Vector3>();
        public Vector3[] gardenCenters = Array.Empty<Vector3>();
        public SeatData[] seats = Array.Empty<SeatData>();
        public Transform[] seatNodes = Array.Empty<Transform>();
        public Light[] lanterns = Array.Empty<Light>();
        public int palms, shadeTrees, planters, flowerBeds, bays, rocks;

        // These references let the authoring tool restore the original vegetation
        // meshes before rebuilding this particular stretch of waterfront.
        public MeshFilter[] trimmedVegetation = Array.Empty<MeshFilter>();
        public Mesh[] originalVegetation = Array.Empty<Mesh>();

        public static void Bind(CityContext ctx)
        {
            var all = new List<SeatData>(ctx.Data.seats);
            foreach (var waterfront in FindObjectsByType<CityWaterfront>(FindObjectsSortMode.None))
                for (int i = 0; i < waterfront.seats.Length && i < waterfront.seatNodes.Length; i++)
                {
                    var seat = waterfront.seats[i];
                    if (!waterfront.seatNodes[i] || ctx.Nodes.ContainsKey(seat.id)) continue;
                    ctx.Nodes.Add(seat.id, waterfront.seatNodes[i]);
                    all.Add(seat);
                }
            ctx.Data.seats = all.ToArray();
        }

        void Update()
        {
            var ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) return;
            float glow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .7f, ctx.Night));
            foreach (var lantern in lanterns)
                if (lantern) { lantern.enabled = glow > .01f; lantern.intensity = glow * 1.35f; }
        }
    }
}
