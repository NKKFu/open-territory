using UnityEngine;

namespace OpenTerritory
{
    public static class CitySteeringWheel
    {
        public const float GripRadius = .215f;

        // The donor's seat + .90 m mount cuts through the sloped windscreen
        // and sinks the lower rim into the instruments. Fit from the seat,
        // rather than offsetting the current pose, so repeated fits are stable.
        public static void Fit(VehicleData vehicle, Transform wheel)
        {
            if (!wheel || vehicle.kind != "car") return;
            wheel.localPosition = new Vector3(vehicle.wheelMount.x, vehicle.wheelMount.y, vehicle.seatMount.z + .50f);
            wheel.localRotation = Quaternion.Euler(18, 0, 0);
        }

        public static Vector3 GripPoint(Transform wheel, Quaternion neutralRotation, float turn, int hand)
        {
            // Limit the driver's reach as before, but keep both hands in the
            // wheel's tilted plane, including when the vehicle pitches or rolls.
            float grip = Mathf.Clamp(turn, -1.1f, 1.1f), side = hand == 0 ? -1 : 1;
            var point = new Vector3(side * GripRadius * Mathf.Cos(grip), side * GripRadius * Mathf.Sin(grip), -.025f);
            return wheel.parent.TransformPoint(wheel.localPosition + neutralRotation * point);
        }
    }
}
