Shader "OpenTerritory/WeaponParticle"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
        _Star ("Flash spokes", Float) = 0
        _Ring ("Pulse ring", Float) = 0
        _Opacity ("Opacity", Float) = 1
        _Solid ("Liquid edge", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha [_DstBlend]
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            float _Star, _Ring, _Opacity, _Solid;
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv*2-1;
                UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float radius=length(i.uv);
                float spokes=lerp(1,.38+.62*pow(abs(cos(atan2(i.uv.y,i.uv.x)*3)),7),_Star);
                float mask=pow(saturate(1-radius/max(.01,spokes)),1.6);
                mask=lerp(mask,1-smoothstep(.72,1,radius),_Solid);
                mask=lerp(mask,smoothstep(.55,.72,radius)*(1-smoothstep(.78,.97,radius)),_Ring);
                fixed4 color=i.color; color.a*=mask*_Opacity;
                UNITY_APPLY_FOG_COLOR(i.fogCoord,color,fixed4(0,0,0,0)); return color;
            }
            ENDCG
        }
    }
}
