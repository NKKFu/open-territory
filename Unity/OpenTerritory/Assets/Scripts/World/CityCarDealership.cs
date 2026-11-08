using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // The dealership is authored in the scene. Only its two entrance leaves move.
    public sealed class CityCarDealership : MonoBehaviour
    {
        public const string DisplayName = "Sun Motors";
        public Vector2 size = new Vector2(62, 60);
        public Vector3 streetApproach, drivewayApproach;
        public Vector3[] walkRoute = Array.Empty<Vector3>();
        public int[] reservedNaturalSolids = Array.Empty<int>();
        public Transform[] doorLeaves = Array.Empty<Transform>();
        public Transform[] displayCars = Array.Empty<Transform>();
        public Light[] lights = Array.Empty<Light>();
        public float OpenAmount { get; private set; }
        public bool Occupied { get; private set; }
        readonly Collider[] contacts = new Collider[32];
        CityContext ctx;
        float holdUntil, nextSense;

        public bool Contains(Vector3 p, float margin = 0)
        {
            var q = transform.InverseTransformPoint(p);
            return Mathf.Abs(q.x) < size.x * .5f + margin && Mathf.Abs(q.z) < size.y * .5f + margin;
        }

        public static void Bind(CityContext context)
        {
            foreach (var shop in FindObjectsByType<CityCarDealership>(FindObjectsSortMode.None))
            {
                var reserved = new HashSet<int>(shop.reservedNaturalSolids);
                foreach (var solid in context.Data.solids)
                    if (reserved.Contains(solid.index)) solid.active = false;
            }
        }

        void Start() { ctx = CityContext.Instance; SetDoorPose(0); }

        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            if (ctx.Clock >= nextSense)
            {
                nextSense = ctx.Clock + .08f;
                int mask = (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer);
                Occupied = Physics.OverlapBoxNonAlloc(transform.TransformPoint(new Vector3(1, 1.6f, 1)),
                    new Vector3(2.5f, 1.6f, 4.5f), contacts, transform.rotation, mask, QueryTriggerInteraction.Ignore) > 0;
                if (Occupied) holdUntil = ctx.Clock + 1.8f;
            }
            SetDoorPose(Mathf.MoveTowards(OpenAmount, Occupied || ctx.Clock < holdUntil ? 1 : 0, Time.deltaTime * 1.6f));
        }

        public void SetDoorPose(float amount)
        {
            OpenAmount = Mathf.Clamp01(amount);
            for (int i = 0; i < doorLeaves.Length; i++)
                if (doorLeaves[i]) doorLeaves[i].localPosition = new Vector3(i == 0 ? -OpenAmount * 2.05f : 2 + OpenAmount * 2.05f, 0, 1);
        }
    }
}
