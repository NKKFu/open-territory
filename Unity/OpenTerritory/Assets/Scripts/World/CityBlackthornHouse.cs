using System;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // All architecture and props are authored offline. Only the prepared crystal
    // and lights animate, using the city clock so Esc freezes the whole scene.
    [DefaultExecutionOrder(500)]
    public sealed class CityBlackthornHouse : MonoBehaviour
    {
        public const string DisplayName = "Blackthorn House";
        public const int PlaceIndex = 146;
        public Vector3 streetApproach;
        public Vector3[] walkRoute = Array.Empty<Vector3>();
        public Light[] lights = Array.Empty<Light>();
        public Transform crystal;
        public Transform entranceLeaf;
        public int originalColliders;
        public string originalCollisionHash;
        public int candleCount;
        public GameObject[] replacedInterior = Array.Empty<GameObject>();
        public int replacedInteriorColliders, shelfCount;
        public Vector3 pentagramCenter, counterApproach;
        public Transform wizard;
        public ResidentData wizardData;
        public NodeReference[] wizardNodes = Array.Empty<NodeReference>();
        CityContext ctx;
        Vector3 crystalRest;
        Quaternion crystalRotation;

        public static void Bind(CityContext context)
        {
            foreach (var shop in FindObjectsByType<CityBlackthornHouse>(FindObjectsSortMode.None))
            {
                if (!shop.wizard || shop.wizardData == null || context.Data.residents.Any(r => r.id == shop.wizardData.id)) continue;
                foreach (var node in shop.wizardNodes) if (node.transform) context.Nodes.Add(node.id, node.transform);
                context.Data.residents = context.Data.residents.Append(shop.wizardData).ToArray();
            }
        }

        void Start()
        {
            ctx = CityContext.Instance;
            if (crystal) { crystalRest = crystal.localPosition; crystalRotation = crystal.localRotation; }
        }

        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            if (crystal)
            {
                crystal.localPosition = crystalRest + Vector3.up * (Mathf.Sin(ctx.Clock * 1.3f) * .055f);
                crystal.localRotation = crystalRotation * Quaternion.Euler(0, ctx.Clock * 12, 0);
            }
            bool nearby = (ctx.Player.position - transform.position).sqrMagnitude < 140 * 140;
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i]) continue;
                lights[i].enabled = nearby;
                lights[i].intensity = (i < 2 ? .30f + ctx.Night * .8f : .90f) *
                    (1 + .025f * Mathf.Sin(ctx.Clock * 8.1f + i * 2.7f));
            }
        }
    }
}
