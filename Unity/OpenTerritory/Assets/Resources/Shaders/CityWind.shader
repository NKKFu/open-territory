Shader "OpenTerritory/CityWind"
{
    Properties { _Color("Color", Color) = (1,1,1,1) _Ribbon("Soft ribbon", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color; float4 _CityCloudLit; float _Ribbon;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o; o.position = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv;
                float daylight = saturate(dot(_CityCloudLit.rgb, float3(.25,.5,.25)) * 1.35);
                o.color.rgb *= lerp(float3(.20,.28,.4), float3(1,1,1), daylight);
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 color = i.color;
                if (_Ribbon > .5) color.a *= smoothstep(0,.22,i.uv.y) * (1-smoothstep(.78,1,i.uv.y));
                UNITY_APPLY_FOG(i.fogCoord, color); return color;
            }
            ENDCG
        }
    }
}
