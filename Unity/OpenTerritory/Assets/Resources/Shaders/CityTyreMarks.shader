Shader "OpenTerritory/CityTyreMarks"
{
    Properties { _Lifetime("Lifetime",Float)=12 _EffectTime("Simulation time",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -1,-1
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float _EffectTime,_Lifetime;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 birth:TEXCOORD1; };
            struct v2f { float4 position:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata v)
            {
                v2f o; o.position=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv;
                o.color.a*=1-smoothstep(.45,1,saturate((_EffectTime-v.birth.x)/max(.1,_Lifetime)));
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float edge=smoothstep(0,.1,i.uv.x)*(1-smoothstep(.9,1,i.uv.x));
                float grooves=lerp(.4,1,smoothstep(.12,.28,abs(frac(i.uv.x*5)-.5)));
                float grain=.78+.22*sin(i.uv.y*65+i.uv.x*12)*sin(i.uv.y*29);
                fixed4 color=i.color; color.a*=edge*grooves*grain*saturate(i.uv.y*5);
                UNITY_APPLY_FOG(i.fogCoord,color); return color;
            }
            ENDCG
        }
    }
}
