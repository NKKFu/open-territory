Shader "OpenTerritory/CityGlass" {
 Properties {_Color("Tint",Color)=(1,1,1,.45) _MainTex("Texture",2D)="white"{} _EmissionColor("Emission",Color)=(0,0,0,0) _Glossiness("Smoothness",Range(0,1))=.2 _Metallic("Metallic",Range(0,1))=0 _Unlit("Unlit",Float)=0 [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=0 }
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"} LOD 200 Cull [_Cull] ZWrite Off
 CGPROGRAM
 #pragma surface surf Standard alpha:fade
 #pragma target 3.0
 sampler2D _MainTex;fixed4 _Color,_EmissionColor;half _Glossiness,_Metallic,_Unlit;
 struct Input {float2 uv_MainTex;float4 color:COLOR;};
 void surf(Input IN,inout SurfaceOutputStandard o){fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color*IN.color;o.Albedo=c.rgb*(1-_Unlit);o.Emission=_EmissionColor.rgb+c.rgb*_Unlit;o.Smoothness=_Glossiness;o.Metallic=_Metallic;o.Alpha=c.a;}
 ENDCG
 } Fallback "Transparent/Diffuse"
}
