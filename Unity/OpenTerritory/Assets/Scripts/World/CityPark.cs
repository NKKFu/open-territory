using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Authored scenery only. Benches join the usual E interaction at scene load.
    public sealed class CityPark : MonoBehaviour
    {
        public const string DisplayName = "Sunmeadow Park";
        [Serializable] public sealed class Walkway { public string name; public float width; public Vector3[] points; }
        public Vector2[] boundary = Array.Empty<Vector2>();
        public Walkway[] walkways = Array.Empty<Walkway>();
        public SeatData[] seats = Array.Empty<SeatData>();
        public Transform[] seatNodes = Array.Empty<Transform>();
        public Vector3[] entrances = Array.Empty<Vector3>();
        public Vector3 picnicCenter;
        public int plantedTrees, flowerBeds;

        public static void Bind(CityContext ctx)
        {
            var seats = new List<SeatData>(ctx.Data.seats);
            foreach (var park in FindObjectsByType<CityPark>(FindObjectsSortMode.None))
                for (int i = 0; i < park.seats.Length && i < park.seatNodes.Length; i++)
                {
                    var seat = park.seats[i];
                    if (!park.seatNodes[i] || ctx.Nodes.ContainsKey(seat.id)) continue;
                    ctx.Nodes.Add(seat.id, park.seatNodes[i]); seats.Add(seat);
                }
            ctx.Data.seats = seats.ToArray();
        }
    }
}
