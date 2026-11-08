using System;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Authored scene data for the parking court. Bind before population takes
    // its spawn snapshot, just like the existing fire-garage placement.
    public sealed class CityParkingLot : MonoBehaviour
    {
        public const float Yaw = -22.162f;
        public static readonly Vector3 Origin = new Vector3(520, 0, 632);
        public static readonly float[] BayCenters = { -8.23f, -5.18f, -2.13f, .92f, 3.97f, 7.02f, 10.07f, 13.12f, 16.17f };
        public int[] vehicleIds = Array.Empty<int>();
        public Transform[] parkedVehicles = Array.Empty<Transform>();
        public BoxCollider[] movedStops = Array.Empty<BoxCollider>();
        public BoxCollider[] newStops = Array.Empty<BoxCollider>();
        public MeshFilter stopFilter;
        public Mesh originalStopMesh;
        public CityObjectLod stopLod;
        public int stopLodPart;
        public MeshCollider surface;
        public int bayCount;

        public static Vector3 Point(float u, float v) => Origin + Quaternion.Euler(0, Yaw, 0) * new Vector3(u, 0, v);
        public static Vector3 Local(Vector3 point) => Quaternion.Inverse(Quaternion.Euler(0, Yaw, 0)) * (point - Origin);

        public static void Bind(CityContext context)
        {
            var lot = FindFirstObjectByType<CityParkingLot>();
            if (!lot) return;
            for (int i = 0; i < lot.vehicleIds.Length && i < lot.parkedVehicles.Length; i++)
            {
                var vehicle = context.Data.vehicles.FirstOrDefault(v => v.id == lot.vehicleIds[i]);
                var visual = lot.parkedVehicles[i];
                if (vehicle == null || !visual) continue;
                vehicle.position = visual.position;
                vehicle.yaw = visual.eulerAngles.y * Mathf.Deg2Rad;
                var node = context.Node(vehicle.id);
                if (node) node.SetPositionAndRotation(visual.position, visual.rotation);
            }
            foreach (var box in lot.movedStops)
            {
                if (!box || !box.name.StartsWith("Colisão-") || !int.TryParse(box.name.Substring(8), out int id)) continue;
                foreach (var solid in context.Data.solids.Where(s => s.index == id)) ApplySolid(solid, box);
            }
            // The three added stops are real world obstacles for both physics and
            // the data-driven systems, with IDs outside the imported range.
            int next = context.Data.solids.Length == 0 ? 0 : context.Data.solids.Max(s => s.index) + 1;
            var additions = lot.newStops.Where(b => b).ToArray();
            int oldLength = context.Data.solids.Length;
            Array.Resize(ref context.Data.solids, oldLength + additions.Length);
            for (int i = 0; i < additions.Length; i++)
            {
                var solid = new SolidData { index = next + i, active = true, vault = true, opaque = true };
                ApplySolid(solid, additions[i]);
                context.Data.solids[oldLength + i] = solid;
            }
        }

        static void ApplySolid(SolidData solid, BoxCollider box)
        {
            var center = box.transform.TransformPoint(box.center);
            var scale = box.transform.lossyScale;
            solid.x = center.x; solid.z = center.z;
            solid.w = box.size.x * Mathf.Abs(scale.x); solid.d = box.size.z * Mathf.Abs(scale.z);
            float halfHeight = box.size.y * Mathf.Abs(scale.y) * .5f;
            solid.bottom = center.y - halfHeight; solid.top = center.y + halfHeight;
            solid.yaw = box.transform.eulerAngles.y * Mathf.Deg2Rad;
            solid.active = box.enabled && box.gameObject.activeInHierarchy;
        }
    }
}
