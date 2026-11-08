using System;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // The architecture is baked in the Editor. Only the three cell gates move.
    public sealed class CityPoliceStation : MonoBehaviour
    {
        public const int PlaceIndex = 5;
        public Transform[] gates = Array.Empty<Transform>();
        public Vector3[] gateAnchors = Array.Empty<Vector3>();
        public int[] retiredSolidSlots = Array.Empty<int>();
        public GameObject[] retiredObjects = Array.Empty<GameObject>();
        public Collider[] retiredColliders = Array.Empty<Collider>();
        public Vector3[] stairRoute = Array.Empty<Vector3>();
        public Vector3 streetApproach, noticeAnchor;
        public float upperFloor = 4.50f;
        public readonly float[] gateAmounts = new float[3];
        readonly bool[] requestedOpen = new bool[3];
        readonly Collider[] doorwayContacts = new Collider[16];
        CityContext ctx;

        public static void Bind(CityContext context)
        {
            var station = FindFirstObjectByType<CityPoliceStation>();
            if (!station) return;
            foreach (int slot in station.retiredSolidSlots)
                if (slot >= 0 && slot < context.Data.solids.Length) context.Data.solids[slot].active = false;
            var notice = context.Data.services.FirstOrDefault(s => s.id == "service-13" && s.kind == "police-info");
            if (notice != null) { notice.position = station.noticeAnchor - Vector3.up; notice.anchor = station.noticeAnchor; }
        }

        void Start()
        {
            ctx = CityContext.Instance;
            if (!ctx) return;
            for (int i = 0; i < gates.Length; i++)
            {
                int cell = i;
                ctx.AddAction("police-cell-" + cell, () => transform.TransformPoint(gateAnchors[cell]),
                    () => requestedOpen[cell] ? "Close" : "Open", () => requestedOpen[cell] = !requestedOpen[cell],
                    () => ctx.Ready && !ctx.Paused && (requestedOpen[cell] ? DoorwayClear(cell) : true),
                    2.7f, 3, target: () => gates[cell]);
            }
        }

        bool DoorwayClear(int cell)
        {
            Vector3 center = transform.TransformPoint(gateAnchors[cell]);
            return Physics.OverlapBoxNonAlloc(center, new Vector3(.65f, 1.2f, .45f), doorwayContacts, transform.rotation,
                (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer), QueryTriggerInteraction.Ignore) == 0;
        }

        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            for (int i = 0; i < gates.Length; i++)
            {
                float target = requestedOpen[i] ? 1 : 0;
                // A person crossing an open gate must never be enclosed by it.
                if (target == 0 && gateAmounts[i] > .01f && !DoorwayClear(i)) target = 1;
                gateAmounts[i] = Mathf.MoveTowards(gateAmounts[i], target, Time.deltaTime * 1.45f);
                gates[i].localRotation = Quaternion.Euler(0, gateAmounts[i] * 90f, 0);
            }
        }
    }
}
