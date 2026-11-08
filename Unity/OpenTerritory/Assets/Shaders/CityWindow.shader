Shader "OpenTerritory/CityWindow" {
 Properties {
  _Color("Glass tint",Color)=(.53,.74,.77,.24)
  _EmissionColor("Room glow",Color)=(.02,.015,.01,1)
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off
  CGPROGRAM
  #pragma surface surf Standard alpha:fade fullforwardshadows
  #pragma target 3.0
  fixed4 _Color,_EmissionColor;
  struct Input {float3 viewDir;};
  void surf(Input IN,inout SurfaceOutputStandard o) {
   half rim=pow(1-saturate(abs(normalize(IN.viewDir).z)),4);
   o.Albedo=_Color.rgb;o.Metallic=.12;o.Smoothness=.86;
   o.Emission=_EmissionColor.rgb+half3(.18,.28,.33)*rim*.18;
   o.Alpha=lerp(_Color.a,.58,rim);
  }
  ENDCG
 }
 Fallback "Transparent/Diffuse"
}
