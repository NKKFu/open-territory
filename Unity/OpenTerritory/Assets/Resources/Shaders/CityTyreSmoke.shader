Shader "OpenTerritory/CityTyreSmoke"
{
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
            float4 _CityCloudLit;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o; o.position=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv;
                float daylight=saturate(dot(_CityCloudLit.rgb,float3(.25,.5,.25))*1.35);
                o.color.rgb*=lerp(float3(.28,.35,.46),float3(1,1,1),daylight);
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.uv*2-1;
                float octagon=max(max(abs(p.x),abs(p.y)),(abs(p.x)+abs(p.y))*.73);
                float edge=1-smoothstep(.35,1,octagon);
                float lobes=.86+.14*sin(p.x*5+p.y*3)*cos(p.y*4-p.x*2);
                fixed4 color=i.color; color.a*=edge*lobes;
                color.rgb*=.88+.12*saturate(i.uv.y*2);
                UNITY_APPLY_FOG(i.fogCoord,color); return color;
            }
            ENDCG
        }
    }
}
