Shader "OpenTerritory/CityCloud"
{
    Properties { [PerRendererData] _CloudRank("Coverage rank", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Back
        CGINCLUDE
            #include "UnityCG.cginc"
            float _CloudRank, _CityCloudCover, _CityCloudHaze;
            float4 _CityCloudLit, _CityCloudShade, _CitySkyColor;
            float4x4 _CitySunViewProjection;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; };
            struct v2f { float4 position:SV_POSITION; float3 world:TEXCOORD0; float2 center:TEXCOORD1; fixed shade:COLOR; };
            v2f vert(appdata v)
            {
                v2f o; o.position = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz; o.shade = saturate((v.color.r - .5) * 2);
                o.center = mul(unity_ObjectToWorld, float4(0,0,0,1)).xz;
                return o;
            }
            float Opacity(v2f i)
            {
                // Fade before wrapping the field, and retain cloud depth above
                // ground fog instead of inheriting its short street visibility.
                float2 centerDelta = abs(i.center - _WorldSpaceCameraPos.xz);
                float edge = 1 - smoothstep(1210, 1460, max(centerDelta.x, centerDelta.y));
                float cover = smoothstep(_CloudRank - .10, _CloudRank + .10, .28 + _CityCloudCover * .82);
                float horizon = smoothstep(.015, .13, normalize(i.world - _WorldSpaceCameraPos).y);
                // Solid nearby volumes hide their internal faces; haze changes
                // their color, while coverage and distant edges fade as a whole.
                float opacity = cover * edge * horizon;
                clip(opacity - .001);
                return opacity;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 delta = abs(i.world.xz - _WorldSpaceCameraPos.xz);
                float haze = saturate(length(delta) / 2100 + _CityCloudHaze * .42);
                float3 color = lerp(_CityCloudShade.rgb, _CityCloudLit.rgb, i.shade);
                color = lerp(color, _CitySkyColor.rgb, haze * .65);
                return fixed4(color, Opacity(i));
            }
            v2f OcclusionVertex(appdata v)
            {
                v2f o = vert(v);
                o.position = mul(_CitySunViewProjection, float4(o.world, 1));
                return o;
            }
            fixed4 OcclusionFragment(v2f i):SV_Target { return fixed4(0,0,0,Opacity(i)); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        // The custom LightMode keeps this out of normal camera rendering.
        // CitySunRays explicitly draws this pass into its small light mask.
        Pass
        {
            Name "SunOcclusion"
            Tags { "LightMode"="CitySunOcclusion" }
            ZWrite Off ZTest Always
            Blend Zero OneMinusSrcAlpha
            ColorMask R
            CGPROGRAM
            #pragma vertex OcclusionVertex
            #pragma fragment OcclusionFragment
            ENDCG
        }
    }
}
