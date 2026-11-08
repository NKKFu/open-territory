Shader "OpenTerritory/CitySky"
{
    Properties
    {
        _SkyColor("Sky", Color) = (.64,.84,.89,1)
        _HorizonColor("Horizon", Color) = (.64,.84,.89,1)
        _SunColor("Sun", Color) = (1,.96,.72,1)
        _MoonColor("Moon", Color) = (.77,.86,.94,0)
        _SunDirection("Sun direction", Vector) = (0,1,0,0)
        _MoonDirection("Moon direction", Vector) = (0,-1,0,0)
        _Veil("Weather veil", Range(0,1)) = 0
        _Night("Night", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _SkyColor, _HorizonColor, _SunColor, _MoonColor;
            float3 _SunDirection, _MoonDirection;
            float _Veil, _Night;
            struct v2f { float4 position:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.position = UnityObjectToClipPos(vertex);
                o.direction = vertex.xyz; return o;
            }
            float2 DiscUV(float3 ray, float3 center, float radius)
            {
                // This tangent frame remains stable throughout the inclined orbit.
                float3 right = normalize(cross(float3(0,1,0), center));
                float3 up = cross(center, right);
                return float2(dot(ray, right), dot(ray, up)) / radius;
            }
            float Octagon(float2 p)
            {
                p = abs(p);
                return max(max(p.x, p.y), (p.x + p.y) * .70710678);
            }
            float Disc(float2 uv, float facing)
            {
                float edge = Octagon(uv);
                return (1 - smoothstep(1 - fwidth(edge), 1 + fwidth(edge), edge)) * step(0, facing);
            }
            float Crater(float2 uv, float2 center, float size)
            {
                return 1 - smoothstep(.78, 1, Octagon((uv - center) / size));
            }
            half4 frag(v2f i):SV_Target
            {
                float3 ray = normalize(i.direction);
                float altitude = smoothstep(0, .85, max(0, ray.y));
                float3 zenith = _SkyColor.rgb * lerp(float3(.78,.91,1), float3(.60,.72,.92), _Night);
                float3 color = lerp(_HorizonColor.rgb, zenith, altitude);
                float aboveHorizon = smoothstep(-.025, .012, ray.y);
                float2 sunUV = DiscUV(ray, _SunDirection, .025);
                float sunFacing = dot(ray, _SunDirection);
                float sunDisc = Disc(sunUV, sunFacing) * _SunColor.a * aboveHorizon;
                float halo = pow(saturate(sunFacing), 420) * .18 * _SunColor.a * aboveHorizon;
                float sunset = (1 - smoothstep(.02, .38, abs(_SunDirection.y))) * (1 - _Veil);
                float horizonGlow = pow(saturate(sunFacing), 12) * pow(1 - saturate(abs(ray.y)), 5) * sunset * .28;
                color = lerp(color, _SunColor.rgb, saturate(halo + horizonGlow));
                color = lerp(color, _SunColor.rgb, sunDisc);
                float2 moonUV = DiscUV(ray, _MoonDirection, .031);
                float moonDisc = Disc(moonUV, dot(ray, _MoonDirection)) * _MoonColor.a * aboveHorizon;
                float craters = Crater(moonUV, float2(-.32,.22), .28) * .17
                    + Crater(moonUV, float2(.28,-.30), .35) * .14
                    + Crater(moonUV, float2(.36,.40), .16) * .12;
                float facet = floor(saturate(moonUV.x * .24 + moonUV.y * .16 + .65) * 5) / 5;
                float3 moon = _MoonColor.rgb * (.80 + facet * .20 - craters);
                color = lerp(color, moon, moonDisc);
                return half4(color, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
