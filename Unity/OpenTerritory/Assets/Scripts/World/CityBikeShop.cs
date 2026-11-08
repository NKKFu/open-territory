using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Authored scenery; only the entrance leaves and lighting change during play.
    public sealed class CityBikeShop : MonoBehaviour
    {
        [Serializable] public sealed class RetiredObject { public GameObject target; public bool active; }
        [Serializable] public sealed class RetiredCollider { public Collider target; public bool enabled; }
        [Serializable] public sealed class FloorChange { public MeshCollider target; public Mesh original; }
        public const string DisplayName = "Sunspoke Cycles";
        public Transform[] halls = Array.Empty<Transform>(), doorLeaves = Array.Empty<Transform>();
        public Vector3[] closedDoors = Array.Empty<Vector3>(), approaches = Array.Empty<Vector3>(), walkRoute = Array.Empty<Vector3>();
        public VehicleData[] bicycles = Array.Empty<VehicleData>();
        public int[] retiredSolids = Array.Empty<int>(), retiredSeats = Array.Empty<int>();
        public RetiredObject[] retiredObjects = Array.Empty<RetiredObject>();
        public RetiredCollider[] retiredColliders = Array.Empty<RetiredCollider>();
        public FloorChange[] floorChanges = Array.Empty<FloorChange>();
        public Light[] lights = Array.Empty<Light>();
        public int displayBikes;
        public float[] DoorAmounts { get; } = new float[2];
        readonly float[] holdUntil = new float[2];
        readonly Collider[] contacts = new Collider[24];
        CityContext ctx;

        public static void Bind(CityContext context)
        {
            var shop = FindFirstObjectByType<CityBikeShop>(); if (!shop) return;
            foreach (var old in shop.retiredObjects) if (old.target) old.target.SetActive(false);
            foreach (var old in shop.retiredColliders) if (old.target) old.target.enabled = false;
            var retired = new HashSet<int>(shop.retiredSolids);
            // Legacy door panels share numeric IDs with remote trees. Retire
            // only the records physically belonging to these two old lots.
            foreach (var solid in context.Data.solids)
                if (retired.Contains(solid.index) && shop.halls.Any(h => {
                    var q = h.InverseTransformPoint(new Vector3(solid.x, solid.bottom, solid.z));
                    return Mathf.Abs(q.x) < 11.95f && q.z > -10.95f && q.z < 12.75f;
                })) solid.active = false;
            context.Data.seats = context.Data.seats.Where(s => !shop.retiredSeats.Contains(s.id)).ToArray();
            context.Data.services = context.Data.services.Where(s => s.id != "service-12").ToArray();
            context.Data.devices = context.Data.devices.Where(s => s.id != "device-98").ToArray();
            context.Data.vehicles = context.Data.vehicles.Concat(shop.bicycles).ToArray();
            for (int wing = 0; wing < 2; wing++)
            {
                var place = context.Data.places[wing == 0 ? 0 : 3]; var hall = shop.halls[wing];
                place.name = wing == 0 ? DisplayName : ""; place.kind = wing == 0 ? "bike-shop" : "retired";
                place.x = hall.position.x; place.z = hall.position.z; place.elevation = hall.position.y;
                place.lotWidth = 22; place.lotDepth = 19; place.yaw = hall.eulerAngles.y * Mathf.Deg2Rad; place.door = null;
                int residentId = wing == 0 ? 7782 : 7971;
                var resident = context.Data.residents.FirstOrDefault(r => r.id == residentId);
                if (resident != null)
                {
                    resident.name = wing == 0 ? "Flor" : "Henry"; resident.role = ""; resident.place = DisplayName;
                    resident.stationary = true; resident.indoor = true; resident.sitting = false; resident.activity = "work";
                    resident.position = hall.TransformPoint(new Vector3(wing == 0 ? -7 : 7, .20f, -6.8f));
                    resident.line = wing == 0 ? "Take a look around. The city bikes are ready to ride." : "A little care keeps a good bike rolling.";
                    resident.lines = new[] { resident.line }; resident.routine = Array.Empty<string>();
                    var model = context.Node(residentId); if (model) model.position = resident.position;
                }
            }
        }

        void Start() { ctx = CityContext.Instance; for (int i = 0; i < 2; i++) SetDoor(i, 0); }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            for (int wing = 0; wing < 2; wing++)
            {
                var hall = halls[wing];
                bool occupied = Physics.OverlapBoxNonAlloc(hall.TransformPoint(new Vector3(0, 1.6f, 9.4f)), new Vector3(2.7f, 1.7f, 3.6f),
                    contacts, hall.rotation, (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer), QueryTriggerInteraction.Ignore) > 0;
                if (occupied) holdUntil[wing] = ctx.Clock + 1.4f;
                SetDoor(wing, Mathf.MoveTowards(DoorAmounts[wing], occupied || ctx.Clock < holdUntil[wing] ? 1 : 0, Time.deltaTime * 1.5f));
            }
            foreach (var lamp in lights) if (lamp) lamp.intensity = Mathf.Lerp(.9f, 2.2f, ctx.Night);
        }
        public void SetDoor(int wing, float amount)
        {
            DoorAmounts[wing] = Mathf.Clamp01(amount);
            for (int side = 0; side < 2; side++)
            {
                int index = wing * 2 + side;
                if (doorLeaves[index]) doorLeaves[index].localPosition = closedDoors[index] + Vector3.right * ((side == 0 ? -1 : 1) * DoorAmounts[wing] * 2.2f);
            }
        }
    }
}
