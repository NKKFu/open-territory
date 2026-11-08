#ifndef CITY_WIND_FIELD_INCLUDED
#define CITY_WIND_FIELD_INCLUDED
// Identical travelling gusts to CityWindField.cs; use the simulation clock.
float CityWindGust(float3 position, float time)
{
    float2 direction = _CityWind.xz / max(length(_CityWind.xz), .0001);
    float2 side = float2(-direction.y, direction.x);
    float phase = dot(position.xz, direction) * .035 - time * .85;
    float band = .5 + .5 * sin(phase + .45 * sin(dot(position.xz, side) * .018 - time * .27));
    return .65 + 1.1 * band * band * band + .05 * sin(position.x * .11 + position.z * .07 - time * .95);
}
float3 CityWindAt(float3 position, float time)
{
    float angle = .13 * sin(time * .18 + position.x * .001 + position.z * .0013) + .04 * sin(time * .47);
    float s = sin(angle), c = cos(angle);
    return float3(_CityWind.x * c - _CityWind.z * s, 0, _CityWind.x * s + _CityWind.z * c) * CityWindGust(position, time);
}
#endif
