Shader "Hidden/OpenTerritory/AmbientOcclusion"
{
    Properties { _MainTex("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _AOTexture;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize, _AOProjection, _AOParameters, _AOBufferSize;

        struct Interpolators { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Interpolators Fullscreen(appdata_img v)
        {
            Interpolators o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o;
        }
        float2 DepthPixel(float2 uv)
        {
            // A point-sampled depth belongs to its texel centre. Reconstructing
            // it at a fractional tap position produces stripes on flat floors.
            return (floor(uv * _MainTex_TexelSize.zw) + .5) * abs(_MainTex_TexelSize.xy);
        }

        float EyeDepth(float2 uv)
        {
            // The resolved colour and depth can have opposite orientations
            // with MSAA on Direct3D. AO stays in the colour texture's space.
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
            #endif
            return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        }
        float3 Position(float2 uv, float depth)
        {
            return float3((uv * 2 - 1) * _AOProjection.zw, 1) * depth;
        }
        float3 Position(float2 uv) { uv = DepthPixel(uv); return Position(uv, EyeDepth(uv)); }

        float2 Occlusion(Interpolators i) : SV_Target
        {
            float2 centerUV = DepthPixel(i.uv);
            float depth = EyeDepth(centerUV);
            if (depth >= _AOParameters.z) return float2(0, min(depth, 65000));
            float3 center = Position(centerUV, depth);
            float2 dx = float2(_AOBufferSize.x, 0), dy = float2(0, _AOBufferSize.y);
            float3 left = center - Position(centerUV - dx), right = Position(centerUV + dx) - center;
            float3 down = center - Position(centerUV - dy), up = Position(centerUV + dy) - center;
            // Pick the nearer derivative so silhouettes do not become normals.
            float3 tangentX = abs(left.z) < abs(right.z) ? left : right;
            float3 tangentY = abs(down.z) < abs(up.z) ? down : up;
            float3 normal = normalize(cross(tangentY, tangentX));
            float radius = _AOParameters.x;
            float2 reach = min(.5 * radius * _AOProjection.xy / max(depth, .01), _AOBufferSize.xy * 32);
            // Fixed spiral: eight taps without a noise texture or temporal shimmer.
            const float2 taps[8] = {
                float2(.25, 0), float2(-.28, .26), float2(.044, -.50), float2(.35, .46),
                float2(-.66, -.12), float2(.64, -.41), float2(-.22, .82), float2(-.46, -.89)
            };
            float obscurance = 0;
            [unroll] for (int tap = 0; tap < 8; tap++)
            {
                float2 uv = centerUV + taps[tap] * reach;
                float3 delta = Position(uv) - center;
                float distanceSquared = max(dot(delta, delta), .0001);
                float horizon = saturate((dot(normal, delta) - .02) * rsqrt(distanceSquared) - .08);
                float range = saturate(1 - distanceSquared / (radius * radius));
                float onScreen = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                obscurance += horizon * range * onScreen;
            }
            float fade = 1 - smoothstep(_AOParameters.z * .65, _AOParameters.z, depth);
            return float2(min(obscurance * (_AOParameters.y / 8), _AOParameters.w) * fade, depth);
        }

        float4 Composite(Interpolators i) : SV_Target
        {
            float4 color = tex2D(_MainTex, i.uv);
            float depth = EyeDepth(i.uv);
            if (depth >= _AOParameters.z) return color;
            float2 pixel = i.uv * _AOBufferSize.zw - .5;
            float2 origin = (floor(pixel) + .5) * _AOBufferSize.xy;
            float2 blend = frac(pixel);
            float total = 0, obscurance = 0;
            // Four-tap depth-aware upsampling smooths AO without dark halos
            // spreading across a foreground silhouette or into the sky.
            [unroll] for (int y = 0; y < 2; y++)
            [unroll] for (int x = 0; x < 2; x++)
            {
                float2 sampleAO = tex2D(_AOTexture, origin + float2(x, y) * _AOBufferSize.xy).rg;
                float weight = (x ? blend.x : 1 - blend.x) * (y ? blend.y : 1 - blend.y);
                weight *= saturate(1 - abs(sampleAO.y - depth) / max(.08, depth * .012));
                obscurance += sampleAO.x * weight;
                total += weight;
            }
            color.rgb *= 1 - obscurance / max(total, .0001);
            return color;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Fullscreen
            #pragma fragment Occlusion
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
