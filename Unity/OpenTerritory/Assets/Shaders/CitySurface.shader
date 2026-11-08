Shader "OpenTerritory/CitySurface" {
 Properties {
  _Color("Pigment",Color)=(1,1,1,1) _MainTex("Texture",2D)="white"{}
  _EmissionColor("Emission",Color)=(0,0,0,0) _Glossiness("Smoothness",Range(0,1))=.15
  _Metallic("Metallic",Range(0,1))=0 _Surface("Surface",Float)=0 _Unlit("Unlit",Float)=0 _Sea("Sea",Float)=0
  _Foliage("Wind in foliage",Float)=0
  _GroundCover("Wind in grass / shrubs",Float)=0
  [HideInInspector] _RoomOrigin("Room origin",Vector)=(0,0,0,0)
  [HideInInspector] _RoomRight("Room right",Vector)=(1,0,0,0)
  [HideInInspector] _RoomForward("Room forward",Vector)=(0,0,1,0)
  [HideInInspector] _RoomSize("Room bounds",Vector)=(0,0,0,0)
  [HideInInspector] _RoomLight("Room bounce",Color)=(0,0,0,0)
  _CityAtlas("City atlas",2D)="white"{} _CityCover("Rain cover",2D)="white"{}
  [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
 }
 SubShader {
  Tags { "RenderType"="Opaque" } LOD 200 Cull [_Cull]
  CGPROGRAM
  #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
  #pragma target 3.0
  sampler2D _MainTex,_CityAtlas,_CityCover,_CityGrassColor,_CityGrassNormal; fixed4 _Color,_EmissionColor;
  float _CityGrassEnabled,_CityGrassNormalStrength;
  half _Glossiness,_Metallic,_Surface,_Unlit,_Sea,_Foliage,_GroundCover; float _CityWetness,_CityRain,_CitySeaState,_CityTime; float4 _CitySkyColor,_CityWind;
  float4 _RoomOrigin,_RoomRight,_RoomForward,_RoomSize,_RoomLight;
  #include "CityWindField.cginc"
  void SampleGrass(float2 uv,out fixed3 color,out half3 detail){
   // Shift the image only near a tile edge; matching samples on both sides
   // remove hard seams without blurring the middle of the supplied artwork.
   float2 dx=ddx(uv),dy=ddy(uv),cell=frac(uv);
   // Filter the normals more broadly than the color: neighboring blades
   // merge into soft tufts instead of producing sharp, stony edges.
   float2 normalDx=dx*2.5,normalDy=dy*2.5;
   float2 blend=1-smoothstep(0,.12,min(cell,1-cell));
   color=tex2Dgrad(_CityGrassColor,uv,dx,dy).rgb;
   detail=UnpackNormal(tex2Dgrad(_CityGrassNormal,uv,normalDx,normalDy));
   if(blend.x>0){
    float2 shift=uv+float2(.5,0);
    color=lerp(color,tex2Dgrad(_CityGrassColor,shift,dx,dy).rgb,blend.x);
    detail=lerp(detail,UnpackNormal(tex2Dgrad(_CityGrassNormal,shift,normalDx,normalDy)),blend.x);
   }
   if(blend.y>0){
    float2 shift=uv+float2(0,.5);
    fixed3 other=tex2Dgrad(_CityGrassColor,shift,dx,dy).rgb;
    half3 otherNormal=UnpackNormal(tex2Dgrad(_CityGrassNormal,shift,normalDx,normalDy));
    if(blend.x>0){
     shift=uv+float2(.5,.5);
     other=lerp(other,tex2Dgrad(_CityGrassColor,shift,dx,dy).rgb,blend.x);
     otherNormal=lerp(otherNormal,UnpackNormal(tex2Dgrad(_CityGrassNormal,shift,normalDx,normalDy)),blend.x);
    }
    color=lerp(color,other,blend.y);detail=lerp(detail,otherNormal,blend.y);
   }
  }
  struct Input {float2 uv_MainTex;float3 worldPos;float3 worldNormal;float4 color:COLOR;float cityCode;float seaDepth; INTERNAL_DATA };
  void vert(inout appdata_full v,out Input o){UNITY_INITIALIZE_OUTPUT(Input,o);o.cityCode=v.texcoord1.x;
   float3 p=mul(unity_ObjectToWorld,v.vertex).xyz;float kind=fmod(max(v.texcoord1.x,_Surface),8.0);
   o.seaDepth=v.texcoord1.y;
   if(kind>4.5 && kind<5.5){
    // The imported terrain has no tangents. Align a tangent with the world's
    // X axis so its normal map follows the same XZ projection as the color.
    float3 normal=UnityObjectToWorldNormal(v.normal);
    float3 axis=abs(normal.x)>.95?float3(0,0,1):float3(1,0,0);
    float3 tangent=normalize(axis-normal*dot(normal,axis));
    v.tangent.xyz=normalize(mul((float3x3)unity_WorldToObject,tangent));
    v.tangent.w=-unity_WorldTransformParams.w;
   }
   if(_Foliage>.5){
    // Object height anchors the trunk. World-space wind keeps rotated trees and
    // every LOD bending in the same direction, including the shadow caster.
    float3 origin=mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz;
    float phase=dot(origin.xz,float2(.071,.053));
    float weight=saturate((v.vertex.y-.65)/7.0);weight*=weight;
    float sway=.11+.025*sin(_CityTime*1.6+phase)+.012*sin(_CityTime*3.1+phase*.73);
    float3 bend=clamp(CityWindAt(origin,_CityTime),-14,14)*weight*sway;
    v.vertex.xyz+=mul((float3x3)unity_WorldToObject,bend);
   }
   if(_GroundCover>.5 && v.texcoord2.y>0){
    // UV2 stores height above this blade's root and its bend weight. Bases
    // remain planted even on sloping ground and in the simplified meshes.
    float3 flow=CityWindAt(p,_CityTime);float speed=length(flow.xz);
    float h=max(0,v.texcoord2.x),weight=saturate(v.texcoord2.y);
    float flutter=sin(_CityTime*3.4+dot(p.xz,float2(1.7,1.13)))*.035;
    float reach=h*(.14+.42*saturate(speed*.19)+flutter)*weight;
    if(_GroundCover>1.5)reach*=.35;
    float3 bend=float3(flow.x,0,flow.z)/max(speed,.001)*reach;
    bend.y=-dot(bend.xz,bend.xz)/(2*max(h,.1));
    v.vertex.xyz+=mul((float3x3)unity_WorldToObject,bend);
   }
   if(_Sea>.5){
    float amplitude=(.14+saturate(_CitySeaState)*.22)*smoothstep(0,3,o.seaDepth);
    float a=p.x*.105+p.z*.14+_CityTime*1.12,b=-p.x*.19+p.z*.085+_CityTime*1.57;
    float wave=amplitude*(sin(a)+.42*sin(b));
    v.vertex.xyz+=mul((float3x3)unity_WorldToObject,float3(0,wave,0));
   }
  }
  void surf(Input IN,inout SurfaceOutputStandard o){
   float code=max(IN.cityCode,_Surface),kind=fmod(code,8.0);float3 n=abs(WorldNormalVector(IN,float3(0,0,1)));
   fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color*IN.color;
   if(_Sea>.5){
    float2 p=IN.worldPos.xz;float t=_CityTime;
    float amplitude=(.14+saturate(_CitySeaState)*.22)*smoothstep(0,3,IN.seaDepth);
    float a=p.x*.105+p.y*.14+t*1.12,b=-p.x*.19+p.y*.085+t*1.57;
    float2 slope=amplitude*(cos(a)*float2(.105,.14)+.42*cos(b)*float2(-.19,.085));
    float nearDetail=1-smoothstep(70,260,distance(_WorldSpaceCameraPos,IN.worldPos));
    float rippleA=p.x*1.7+p.y*1.1+t*2.3+1.6*sin(p.y*.23),rippleB=-p.x*.8+p.y*2.1-t*1.8+1.2*sin(p.x*.31);
    slope+=nearDetail*.009*(cos(rippleA)*float2(1.7,1.1)+cos(rippleB)*float2(-.8,2.1));
    o.Normal=normalize(float3(-slope.x,slope.y,1));
    float3 normal=normalize(float3(-slope.x,1,-slope.y));
    float3 view=normalize(_WorldSpaceCameraPos-IN.worldPos);
    float fresnel=.035+.62*pow(1-saturate(dot(normal,view)),5);
    float depth=smoothstep(0,14,IN.seaDepth);
    float3 water=lerp(float3(.065,.38,.34),float3(.018,.13,.20),depth);
    water*=.96+.04*sin(p.x*.036-p.y*.047);
    float crest=sin(a)+.42*sin(b);
    float foam=smoothstep(1.05,1.39,crest)*(.035+.12*_CitySeaState);
    float shallows=(1-smoothstep(.2,2.4,IN.seaDepth))*smoothstep(.03,.25,IN.seaDepth);
    foam+=shallows*smoothstep(.6,.95,sin(IN.seaDepth*5-t*1.8+.4*sin(p.x*.2+p.y*.17)))*.42;
    o.Albedo=lerp(water,float3(.66,.83,.78),saturate(foam));
    o.Emission=_CitySkyColor.rgb*fresnel*.28;
    o.Metallic=.08;o.Smoothness=lerp(.74,.52,saturate(foam));o.Alpha=1;return;
   }
   // The original atlas code is also used by brown flower-bed soil. Its
   // pigment identifies it separately from green ground and yard lawns.
   float green=smoothstep(1.0,1.08,c.g/max(c.r,.0001));
   float grass=kind>4.5&&kind<5.5?_CityGrassEnabled*green*smoothstep(.58,.82,n.y):0;
   if(kind>.5){float scale=kind<1.5?.32:kind<2.5?.55:kind<3.5?.50:kind<4.5?.55:kind<5.5?.18:kind<6.5?.30:.11;
    float2 p=n.y>=max(n.x,n.z)?IN.worldPos.xz:n.x>n.z?IN.worldPos.zy:IN.worldPos.xy;
    float2 uv=p*scale;if(kind>6.5)uv+=float2(.025,.012)*_Time.y;
    float tile=kind-1;uv=(float2(fmod(tile,4),floor(tile/4))+lerp(.016,.984,frac(uv)))/float2(4,2);
    uv.y=1-uv.y;
    float3 original=c.rgb;float3 atlas=original*tex2D(_CityAtlas,uv).rgb*1.1;
    if(grass>0){
     float2 grassUV=IN.worldPos.xz*.1;
     // Preserve small regional pigment variations while letting the supplied
     // grass image supply the actual surface color.
     float3 reference=GammaToLinearSpace(float3(.474,.594,.449));
     float3 tint=lerp(float3(1,1,1),clamp(original/max(reference,.001),.65,1.35),.35);
     fixed3 grassColor;half3 detail;SampleGrass(grassUV,grassColor,detail);
     c.rgb=lerp(atlas,grassColor*tint,grass);
     float fade=1-smoothstep(35,130,distance(_WorldSpaceCameraPos,IN.worldPos));
     o.Normal=normalize(half3(detail.xy*(_CityGrassNormalStrength*grass*fade),detail.z));
    }else c.rgb=atlas;
   }
   float2 cover=IN.worldPos.xz/512+.5;cover.y=1-cover.y;
   float film=code>=8&&n.y>.7?_CityWetness*tex2D(_CityCover,cover).r:0;
   c.rgb*=1-film*.24;o.Albedo=c.rgb*(1-_Unlit);o.Emission=_EmissionColor.rgb+c.rgb*_Unlit;
   float3 room=IN.worldPos-_RoomOrigin.xyz;
   float2 edge=_RoomSize.xy-abs(float2(dot(room,_RoomRight.xyz),dot(room,_RoomForward.xyz)));
   float bounce=_RoomSize.w*smoothstep(.06,.19,min(edge.x,edge.y))*step(.02,room.y)*step(room.y,_RoomSize.z);
   o.Emission+=c.rgb*_RoomLight.rgb*bounce*(1-_Unlit);
   // Dry asphalt and porous ground stay matte under headlights. Rain darkens
   // soil rather than coating it with the same glossy film as hard surfaces.
   float asphalt=kind>.5&&kind<1.5?1:0;
   float porous=(kind>2.5&&kind<3.5)||(kind>4.5&&kind<6.5)?1:0;
   float drySmoothness=asphalt>0?min(_Glossiness,.025):porous>0?min(_Glossiness,.01):_Glossiness;
   float wetSmoothness=asphalt>0?.18:porous>0?.04:.7;
   o.Metallic=asphalt+porous>0?0:_Metallic;
   o.Smoothness=lerp(drySmoothness,wetSmoothness,film*.72);o.Alpha=c.a;
  }
  ENDCG
 }
 Fallback "Diffuse"
}
