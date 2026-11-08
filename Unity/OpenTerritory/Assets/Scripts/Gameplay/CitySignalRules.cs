using UnityEngine;

namespace OpenTerritory
{
    // Shared by the visible lenses and drivers: phases follow actual road arms.
    public static class CitySignalRules
    {
        public static int State(SignalData signal, float clock, int axis)
        {
            float phase = Mathf.Repeat(clock + signal.phase, Mathf.Max(2, signal.phaseCount) * 12);
            float local = phase - axis * 12;
            return local >= 0 && local < 9 ? 0 : local >= 9 && local < 11 ? 1 : 2;
        }

        public static float AmberRemaining(SignalData signal, float clock, int axis)
            => axis * 12 + 11 - Mathf.Repeat(clock + signal.phase, Mathf.Max(2, signal.phaseCount) * 12);

        public static bool Approach(SignalData signal, Vector3 position, Vector3 forward, float nose,
            out SignalApproachData approach, out float gap)
        {
            approach = null; gap = float.PositiveInfinity;
            forward = CityPhysics.Flat(forward).normalized;
            foreach (var arm in signal.approaches)
            {
                if (Vector3.Dot(forward, arm.heading) < .72f || Mathf.Abs(position.y - arm.stop.y) > 3) continue;
                Vector3 offset = CityPhysics.Flat(arm.stop - position);
                float along = Vector3.Dot(offset, arm.heading);
                float across = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, arm.heading)));
                float distance = along - nose - 1.4f;
                // Once committed beyond the line, clear the junction instead of
                // braking in front of an unrelated light on the outgoing road.
                if (along - nose < -1.2f || distance > 42 || across > arm.width * .5f + .4f) continue;
                if (distance >= gap) continue;
                gap = distance; approach = arm;
            }
            return approach != null;
        }
    }
}
