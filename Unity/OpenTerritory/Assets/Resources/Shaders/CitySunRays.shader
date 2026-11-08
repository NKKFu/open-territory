Shader "Hidden/OpenTerritory/SunRays"
{
    Properties { _MainTex("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _RayTexture;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize, _SunPosition, _RayColor;
        float _BlurStep;
        struct Interpolators { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
        Interpolators Fullscreen(appdata_img v)
        { Interpolators o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        float4 Mask(Interpolators i):SV_Target
        {
            float2 depthUV = i.uv;
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0) depthUV.y = 1 - depthUV.y;
            #endif
            float sky = step(.9999, Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthUV)));
            float2 delta = (i.uv - _SunPosition.xy) * float2(_SunPosition.z, 1);
            float source = 1 - smoothstep(.018, _SunPosition.w * .74, length(delta));
            return sky * source;
        }
        float4 RadialBlur(Interpolators i):SV_Target
        {
            float2 stepUV = (_SunPosition.xy - i.uv) * (_BlurStep * .55 / 12);
            float2 uv = i.uv; float light = 0;
            [unroll] for (int sample = 0; sample < 12; sample++)
            { light += tex2D(_MainTex, uv).r; uv += stepUV; }
            return light / 12;
        }
        float4 Composite(Interpolators i):SV_Target
        {
            float4 scene = tex2D(_MainTex, i.uv);
            float shafts = pow(saturate(tex2D(_RayTexture, i.uv).r), 1.35);
            // Screen blend protects highlights and the readability of the city.
            scene.rgb += max(0, 1 - scene.rgb) * saturate(shafts * _RayColor.a) * _RayColor.rgb;
            return scene;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Fullscreen
            #pragma fragment Mask
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Fullscreen
            #pragma fragment RadialBlur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Fullscreen
            #pragma fragment Composite
            ENDCG
        }
    }
    Fallback Off
}
