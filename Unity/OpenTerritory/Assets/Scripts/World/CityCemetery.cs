using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Native scene metadata. Geometry and collisions are authored in the Editor.
    // No scenery is generated and no QA is installed during a normal session.
    public sealed class CityCemetery : MonoBehaviour
    {
        public const string DisplayName = "Cemitério Jardim da Saudade";
        public Vector2 size = new Vector2(44, 58);
        public Vector3 streetApproach;
        public Vector3[] walkRoute = Array.Empty<Vector3>();
        public SeatData[] seats = Array.Empty<SeatData>();
        public Transform[] seatNodes = Array.Empty<Transform>();
        public int[] reservedNaturalSolids = Array.Empty<int>();
        public int graveCount;

        public bool Contains(Vector3 point, float margin = 0)
        {
            var local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) < size.x * .5f + margin && Mathf.Abs(local.z) < size.y * .5f + margin;
        }

        public static void Bind(CityContext ctx)
        {
            var benches = new List<SeatData>(ctx.Data.seats);
            foreach (var cemetery in FindObjectsByType<CityCemetery>(FindObjectsSortMode.None))
            {
                for (int i = 0; i < cemetery.seats.Length; i++)
                {
                    var seat = cemetery.seats[i];
                    if (i >= cemetery.seatNodes.Length || !cemetery.seatNodes[i] || ctx.Nodes.ContainsKey(seat.id)) continue;
                    benches.Add(seat);
                    ctx.Nodes.Add(seat.id, cemetery.seatNodes[i]);
                }
                // The original landscape remains in the source manifest. Its
                // reserved trunks/rocks are disabled in this native scene too.
                var reserved = new HashSet<int>(cemetery.reservedNaturalSolids);
                foreach (var solid in ctx.Data.solids) if (reserved.Contains(solid.index)) solid.active = false;
            }
            ctx.Data.seats = benches.ToArray();
        }
    }
}
