using System;
using UnityEngine;

namespace OpenTerritory
{
    // The bay is authored offline. Only the sectional door moves during play.
    public sealed class CityFireGarage : MonoBehaviour
    {
        [Serializable] public sealed class MeshChange
        {
            public MeshFilter filter;
            public CityObjectLod lod;
            public int part;
            public Mesh full, medium, far;
        }
        [Serializable] public sealed class ColliderChange
        {
            public BoxCollider collider;
            public bool enabled;
            public Vector3 position, center, size;
        }
        public const float BayX = -6.2f, Front = 10.5f, Floor = .31f, Width = 6.2f;
        public Transform[] panels = Array.Empty<Transform>();
        public Renderer statusLamp;
        public int vehicle;
        public Transform parkedTruck;
        public Vector3 originalTruckPosition;
        public Quaternion originalTruckRotation;
        public MeshChange[] originals = Array.Empty<MeshChange>();
        public ColliderChange[] colliders = Array.Empty<ColliderChange>();
        public GameObject[] replacedLamps = Array.Empty<GameObject>();
        public Vector3 TruckPosition => transform.TransformPoint(new Vector3(BayX, Floor + .04f, 1.6f));
        public float OpenAmount { get; private set; }
        public bool Occupied { get; private set; }
        readonly Collider[] contacts = new Collider[64];
        CityContext ctx;
        float holdUntil, nextSense;
        MaterialPropertyBlock lamp;

        // Apply the authored parking pose before the population takes its spawn snapshot.
        public static void Bind(CityContext context)
        {
            var bay = FindFirstObjectByType<CityFireGarage>();
            if (!bay) return;
            foreach (var change in bay.colliders)
            {
                if (!change.collider || !change.collider.name.StartsWith("Colisão-")) continue;
                if (!int.TryParse(change.collider.name.Substring(8), out int index)) continue;
                foreach (var solid in context.Data.solids) if (solid.index == index)
                {
                    solid.active = change.collider.enabled;
                    var center = change.collider.transform.TransformPoint(change.collider.center);
                    solid.x = center.x; solid.z = center.z;
                    break;
                }
            }
            foreach (var data in context.Data.vehicles)
                if (data.id == bay.vehicle)
                {
                    data.position = bay.TruckPosition;
                    data.yaw = bay.transform.eulerAngles.y * Mathf.Deg2Rad;
                    var truck = context.Node(data.id);
                    if (truck) truck.SetPositionAndRotation(data.position, bay.transform.rotation);
                    break;
                }
        }

        void Start()
        {
            ctx = CityContext.Instance;
            lamp = new MaterialPropertyBlock();
            SetPose(0);
        }

        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            if (ctx.Clock >= nextSense)
            {
                nextSense = ctx.Clock + .08f;
                int mask = (1 << CityContext.PlayerLayer) | (1 << CityContext.VehicleLayer) | (1 << CityContext.ActorLayer);
                int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(new Vector3(BayX, 1.8f, 10.7f)),
                    new Vector3(3.55f, 1.8f, 4.15f), contacts, transform.rotation, mask, QueryTriggerInteraction.Ignore);
                // A saturated query conservatively holds the gate open.
                Occupied = count > 0;
                // A moving vehicle needs lead time before reaching the beam.
                // The parked truck stays behind it and does not hold the bay open.
                foreach (var car in ctx.Vehicles.Fleet)
                {
                    var q = transform.InverseTransformPoint(car.Position);
                    if (Mathf.Abs(q.x - BayX) > 3.8f || q.z < -6 || q.z > 32) continue;
                    float approach = Vector3.Dot(car.Body.linearVelocity, transform.forward) * (q.z < Front ? 1 : -1);
                    float gap = Mathf.Abs(q.z - Front) - car.Data.length * .5f;
                    if (approach > .15f && gap < approach * 2.2f + 3) { Occupied = true; break; }
                }
                if (Occupied) holdUntil = ctx.Clock + 2.8f;
            }
            float target = Occupied || ctx.Clock < holdUntil ? 1 : 0;
            SetPose(Mathf.MoveTowards(OpenAmount, target, Time.deltaTime * .65f));
        }

        public void SetPose(float amount)
        {
            OpenAmount = Mathf.Clamp01(amount);
            const float panelHeight = .345f, straight = 3.45f, radius = .55f;
            float curve = radius * Mathf.PI * .5f;
            for (int i = 0; i < panels.Length; i++)
            {
                if (!panels[i]) continue;
                float s = (i + .5f) * panelHeight + OpenAmount * 4.18f;
                float y, z, angle;
                if (s <= straight) { y = Floor + s; z = Front + .12f; angle = 0; }
                else if (s < straight + curve)
                {
                    float a = (s - straight) / radius;
                    y = Floor + straight + radius * Mathf.Sin(a);
                    z = Front + .12f - radius * (1 - Mathf.Cos(a));
                    angle = -a * Mathf.Rad2Deg;
                }
                else { y = Floor + straight + radius; z = Front + .12f - radius - (s - straight - curve); angle = -90; }
                panels[i].localPosition = new Vector3(BayX, y, z);
                panels[i].localRotation = Quaternion.Euler(angle, 0, 0);
            }
            if (statusLamp && lamp != null)
            {
                Color color = OpenAmount > .98f ? new Color(.25f, .78f, .54f) : new Color(1, .55f, .12f);
                lamp.SetColor("_Color", color); lamp.SetColor("_EmissionColor", color * .35f);
                statusLamp.SetPropertyBlock(lamp);
            }
        }
    }
}
