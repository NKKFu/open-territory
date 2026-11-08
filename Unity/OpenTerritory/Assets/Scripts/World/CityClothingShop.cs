using System;
using UnityEngine;

namespace OpenTerritory
{
    public sealed class CityClothingShop : MonoBehaviour
    {
        public const string DisplayName = "Tideline";
        public Vector2 size = new Vector2(40.8f, 14.2f);
        public float upperFloor = 2.281f;
        public Transform[] browsePoints = Array.Empty<Transform>(), doorLeaves = Array.Empty<Transform>();
        public Vector3[] doorClosed = Array.Empty<Vector3>(), entrances = Array.Empty<Vector3>(), walkRoute = Array.Empty<Vector3>();
        public Vector3 streetApproach;
        public GameObject[] replacedObjects = Array.Empty<GameObject>();
        public int[] removedSolidSlots = Array.Empty<int>();
        public Light[] lights = Array.Empty<Light>();
        public MeshCollider[] groundColliders = Array.Empty<MeshCollider>();
        public Mesh[] originalCollisionMeshes = Array.Empty<Mesh>(), cutCollisionMeshes = Array.Empty<Mesh>();
        public bool[] originalCollisionEnabled = Array.Empty<bool>();
        public MeshFilter[] groundRenderers = Array.Empty<MeshFilter>();
        public Mesh[] originalGroundMeshes = Array.Empty<Mesh>(), cutGroundMeshes = Array.Empty<Mesh>();
        public float[] DoorAmounts { get; } = new float[2];
        readonly float[] holdUntil = new float[2];
        readonly Collider[] contacts = new Collider[24];
        CityContext ctx;
        public bool Contains(Vector3 p, float margin = 0)
        { var q = transform.InverseTransformPoint(p); return Mathf.Abs(q.x) < size.x * .5f + margin && Mathf.Abs(q.z) < size.y * .5f + margin; }
        public float Floor(float x) => Mathf.Lerp(upperFloor, 0, Mathf.InverseLerp(-5, 5, x));
        public static void Bind(CityContext ctx)
        {
            var shop = FindFirstObjectByType<CityClothingShop>(); if (!shop) return;
            foreach (int slot in shop.removedSolidSlots) if (slot >= 0 && slot < ctx.Data.solids.Length) ctx.Data.solids[slot].active = false;
            for (int i = 0; i < ctx.Data.places.Length; i++)
            {
                var p = ctx.Data.places[i];
                if (p.name == "#639A60")
                {
                    p.name = DisplayName; p.kind = "clothing"; p.x = shop.transform.position.x; p.z = shop.transform.position.z;
                    p.elevation = shop.transform.position.y; p.lotWidth = shop.size.x; p.lotDepth = shop.size.y; p.door = null;
                }
                else if (p.name == "#366604") { p.kind = "retired"; p.name = ""; p.lotWidth = p.lotDepth = .01f; p.door = null; }
            }
        }
        void Start()
        {
            ctx = CityContext.Instance;
            if (!ctx) return;
            for (int i = 0; i < browsePoints.Length; i++)
            {
                int category = i; var anchor = browsePoints[i];
                ctx.AddAction("clothing-" + i, () => anchor.position, () => "Browse", () => ctx.Clothing.Open(this, category),
                    () => CanBrowse() && !ctx.Clothing.IsOpen, 2.75f, 4, target: () => anchor);
            }
        }
        public bool CanBrowse() => ctx && ctx.Ready && !ctx.Paused && !ctx.UI.MapOpen && !ctx.Combat.Down && !ctx.Combat.IsThreatening() &&
            !ctx.Motor.Busy && !ctx.Motor.Riding && !ctx.Motor.Swimming && ctx.Motor.Grounded && !ctx.Remote.Active && Contains(ctx.Player.position);
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            for (int i = 0; i < entrances.Length; i++)
            {
                var p = transform.TransformPoint(entrances[i]);
                bool occupied = Physics.OverlapBoxNonAlloc(p + Vector3.up * 1.3f, new Vector3(2.1f, 1.5f, 2.3f), contacts, transform.rotation,
                    (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer), QueryTriggerInteraction.Ignore) > 0;
                if (occupied) holdUntil[i] = ctx.Clock + 1.2f;
                SetDoor(i, Mathf.MoveTowards(DoorAmounts[i], occupied || ctx.Clock < holdUntil[i] ? 1 : 0, Time.deltaTime * 2));
            }
        }
        public void SetDoor(int entrance, float amount)
        {
            DoorAmounts[entrance] = Mathf.Clamp01(amount);
            for (int side = 0; side < 2; side++)
            {
                int index = entrance * 2 + side;
                if (index < doorLeaves.Length && doorLeaves[index]) doorLeaves[index].localPosition = doorClosed[index] + Vector3.right * ((side == 0 ? -1 : 1) * DoorAmounts[entrance] * 1.42f);
            }
        }
    }
}
