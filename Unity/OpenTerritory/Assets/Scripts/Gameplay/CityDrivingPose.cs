using UnityEngine;

namespace OpenTerritory
{
    // The walking rig is taller than the donor car occupants. Fit its whole
    // articulated model to the seat; the controller and vehicle anchor retain
    // their original transforms. Ordinary animation restores the model scale.
    public static class CityDrivingPose
    {
        public const float ModelScale = .64f;
        // All five authored car shells have a .17 m floor centred at .74 m.
        public const float FloorTop = .825f;
        public static readonly Vector3 Hips = new Vector3(0, 1.28f, .10f);
        public static readonly Quaternion HipRotation = Quaternion.Euler(8, 0, 0);
        public static readonly Quaternion ChestRotation = Quaternion.Euler(8, 0, 0);
        public static readonly Quaternion HeadRotation = Quaternion.Euler(-16, 0, 0);

        public static Vector3 FootPoint(VehicleData car, int leg)
        {
            float side = leg == 0 ? -1 : 1;
            // Keep the full shoe, including its sole, above the floor. Taller
            // cabins raise the pedals with the seat, preserving knee reach.
            return new Vector3(car.seatMount.x + side * .14f,
                Mathf.Max(FloorTop + .115f, car.seatMount.y + .94f), car.seatMount.z + .57f);
        }

        public static void Apply(CityRig rig, CityVehicles.Vehicle car)
        {
            if (rig == null || !rig.Valid || car == null || car.Data.kind != "car") return;
            rig.Hips.localScale = rig.HipScaleRest * ModelScale;
            rig.Hips.localPosition = Hips; rig.Hips.localRotation = HipRotation;
            rig.Chest.localRotation = ChestRotation; rig.Head.localRotation = HeadRotation;
            for (int i = 0; i < 2; i++)
            {
                CityActors.PlaceFoot(rig, i, car.Root.TransformPoint(FootPoint(car.Data, i)));
                if (i < car.Hands.Length && car.Hands[i]) CityActors.Reach(rig, i, car.Hands[i].position);
            }
        }
    }
}
