using UnityEngine;

namespace OpenTerritory
{
    // Keep this field in step with CityWindField.cginc. A travelling pressure
    // band drives vegetation, airborne leaves and the sound at the listener.
    public static class CityWindField
    {
        public static float Gust(Vector3 point, float time, Vector3 wind)
        {
            var direction = new Vector2(wind.x, wind.z).normalized;
            var side = new Vector2(-direction.y, direction.x);
            var p = new Vector2(point.x, point.z);
            float phase = Vector2.Dot(p, direction) * .035f - time * .85f;
            float band = .5f + .5f * Mathf.Sin(phase + .45f * Mathf.Sin(Vector2.Dot(p, side) * .018f - time * .27f));
            return .65f + 1.1f * band * band * band + .05f * Mathf.Sin(point.x * .11f + point.z * .07f - time * .95f);
        }

        public static Vector3 Sample(Vector3 point, float time, Vector3 wind)
        {
            float angle = .13f * Mathf.Sin(time * .18f + point.x * .001f + point.z * .0013f) + .04f * Mathf.Sin(time * .47f);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new Vector3(wind.x * c - wind.z * s, 0, wind.x * s + wind.z * c) * Gust(point, time, wind);
        }
    }
}
