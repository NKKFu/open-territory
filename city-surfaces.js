// One small neutral atlas supplies world-sized details without splitting the
// city's instance batches by color or downloading image assets.
const groups=[
  ['#60767b','#82918a'],
  ['#e8b494','#e8d5ab','#a6c8bd','#dbaba0','#bdcad2','#e6cfb7','#d7b4ac','#ead1a3','#e6aa87','#e5d4c1','#c4d0bd','#c8d6d6','#c6c6d0','#e9b08e','#e8e9de','#e4b899','#e3b38e','#bccac0','#b5cbbd','#ddc295','#c2c8d2','#ecdfc5'],
  ['#d9d2b9','#e5d9bf','#e3d6ba','#d9d0b8','#ded8c6','#d5d2bd','#e9ddc7','#c4bb9e','#b6b3a5'],
  ['#b47b4f','#bd8758','#b4855e','#9e795a','#b77d5a','#a8795c','#c4936a','#ab805c','#a28060','#bb895e','#a87960','#d8bc91','#bc986e','#d8c5a4','#eee6d1','#af8a61','#ad8962'],
  ['#a9c590','#a2c18b','#a6c58e','#b0ca96','#91ad78','#a2c18b'],
  ['#eed8a4','#e1c49a','#b9b38f','#e9d29d'],
  ['#6abac2','#72bfca','#7dc2c4']
];
const styleByColor=new Map(groups.flatMap((colors,index)=>colors.map(color=>[color,index+1])));
const pigments=new Map(Object.entries({
  '#60767b':'#53696f','#82918a':'#748582','#a9c590':'#8bb577','#a2c18b':'#8ab77a','#a6c58e':'#94be7f','#b0ca96':'#9bbf80',
  '#e8b494':'#e5a178','#e8d5ab':'#e7c675','#a6c8bd':'#79b8aa','#dbaba0':'#d18d85','#bdcad2':'#99bad2','#e6cfb7':'#d9bd9f',
  '#c4d0bd':'#91baa4','#e9b08e':'#e8a076','#e6aa87':'#e09a72','#d7b4ac':'#ce9fa2','#c6c6d0':'#aaaed0',
  '#b5cbbd':'#84b69d','#bccac0':'#a2bc9f','#c2c8d2':'#9cb8ce','#ddc295':'#dfb875',
  '#e9b857':'#e6aa39','#da8573':'#d77666','#689cad':'#4f98b5','#e1b568':'#e4b345','#a595b9':'#9783b9','#8dac8b':'#73a27e',
  '#678e65':'#568f61','#7da972':'#73a86d','#6abac2':'#3da8b9','#72bfca':'#5ab9c9','#7dc2c4':'#66b9be'
}));
export const surfaceKind=color=>styleByColor.get(color)||0;
export const cityPigment=color=>pigments.get(color)||color;

function createAtlas(T,software){
  const tile=128,canvas=document.createElement('canvas');canvas.width=tile*4;canvas.height=tile*2;
  const ctx=canvas.getContext('2d');let seed=9137;
  const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
  const grey=v=>`rgb(${v},${v},${v})`;
  for(let style=0;style<8;style++){
    ctx.save();ctx.translate(style%4*tile,Math.floor(style/4)*tile);ctx.beginPath();ctx.rect(0,0,tile,tile);ctx.clip();
    ctx.fillStyle=grey(style===0?218:232);ctx.fillRect(0,0,tile,tile);
    for(let i=0;i<1700;i++){
      const value=Math.round((style===0?180:205)+random()*45);ctx.fillStyle=grey(value);
      const size=style===4?2+random()*5:1+random()*2;ctx.fillRect(random()*tile,random()*tile,size,size);
    }
    if(style===1){
      ctx.globalAlpha=.12;
      for(let i=0;i<45;i++){ctx.fillStyle=grey(170+Math.round(random()*60));ctx.fillRect(random()*tile,random()*tile,4+random()*15,1+random()*2);}
    }else if(style===2){
      ctx.fillStyle=grey(165);
      for(let y=0;y<tile;y+=32){ctx.fillRect(0,y,tile,1.4);for(let x=(y%64?16:0);x<tile;x+=32)ctx.fillRect(x,y,1.4,32);}
    }else if(style===3){
      ctx.strokeStyle=grey(179);ctx.lineWidth=1;
      for(let x=0;x<tile;x+=32){ctx.fillStyle=grey(160);ctx.fillRect(x,0,1.4,tile);for(let i=0;i<8;i++){ctx.beginPath();ctx.moveTo(x+3+random()*24,-5);ctx.bezierCurveTo(x+random()*32,35,x+random()*32,85,x+3+random()*24,133);ctx.stroke();}}
      ctx.fillStyle=grey(155);ctx.fillRect(0,64,32,1);ctx.fillRect(32,20,32,1);ctx.fillRect(64,100,32,1);ctx.fillRect(96,38,32,1);
    }else if(style===5){
      ctx.strokeStyle=grey(215);ctx.lineWidth=2;
      for(let y=8;y<tile;y+=16){ctx.beginPath();ctx.moveTo(-8,y);ctx.bezierCurveTo(30,y-6,85,y+6,136,y);ctx.stroke();}
    }else if(style===6){
      ctx.fillStyle=grey(222);ctx.fillRect(0,0,tile,tile);ctx.strokeStyle=grey(250);ctx.lineWidth=2;
      for(let y=-16;y<tile;y+=32){ctx.beginPath();ctx.moveTo(-8,y);ctx.lineTo(24,y+6);ctx.lineTo(65,y-3);ctx.lineTo(100,y+5);ctx.lineTo(136,y);ctx.stroke();}
    }
    ctx.restore();
  }
  const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.NoColorSpace;texture.flipY=false;texture.wrapS=texture.wrapT=T.ClampToEdgeWrapping;
  texture.anisotropy=software?1:4;texture.minFilter=software?T.LinearMipmapNearestFilter:T.LinearMipmapLinearFilter;
  texture.generateMipmaps=true;return texture;
}

export function createCitySurfaces({THREE:T,enabled=true,software=false,atlas=null}){
  const texture=enabled?(atlas||createAtlas(T,software)):null,time={value:0};
  const wetness={value:0},rain={value:0},sky={value:new T.Color('#9ac2cd')},cover={value:null};
  function decorate(material,kind=0,instanced=false){
    if(!enabled||(!kind&&!instanced))return material;
    material.userData.citySurface=instanced?'atlas':kind;
    material.onBeforeCompile=shader=>{
      shader.uniforms.cityAtlas={value:texture};shader.uniforms.cityTime=time;shader.uniforms.cityKind={value:kind};
      shader.uniforms.cityWetness=wetness;shader.uniforms.cityRain=rain;shader.uniforms.citySky=sky;shader.uniforms.cityCover=cover;
      const vertex=`
        varying vec4 vCityUvTile;
        varying float vCityKind;
        ${instanced?'attribute float citySurface;':'uniform float cityKind;'}
      `;
      shader.vertexShader=vertex+shader.vertexShader;
      shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>',`#include <begin_vertex>
        float cityCode=${instanced?'citySurface':'cityKind'};
        vCityKind=mod(cityCode,8.0);
        vCityUvTile=vec4(0.0);
        if(vCityKind>0.5){
          vec4 cityPoint=vec4(transformed,1.0);vec3 cityDirection=normal;
          #ifdef USE_INSTANCING
            cityPoint=instanceMatrix*cityPoint;cityDirection=mat3(instanceMatrix)*cityDirection;
          #endif
          vec3 cityPosition=(modelMatrix*cityPoint).xyz,cityFacing=mat3(modelMatrix)*cityDirection,cityNormal=abs(cityFacing);
          vec2 cityUv=cityNormal.y>max(cityNormal.x,cityNormal.z)?cityPosition.xz:(cityNormal.x>cityNormal.z?cityPosition.zy:cityPosition.xy);
          float cityScale=vCityKind<1.5?0.32:vCityKind<2.5?0.55:vCityKind<3.5?0.5:vCityKind<4.5?0.55:vCityKind<5.5?0.18:vCityKind<6.5?0.3:0.11;
          float cityTile=floor(vCityKind+0.1)-1.0;
          vCityUvTile=vec4(cityUv*cityScale,mod(cityTile,4.0),floor(cityTile/4.0));
          if(cityCode>=8.0&&cityFacing.y>max(cityNormal.x,cityNormal.z))
            vCityUvTile.w+=2.0+min(.9,length(cityFacing.xz)/cityFacing.y);
        }
      `);
      shader.fragmentShader=`
        uniform sampler2D cityAtlas;
        uniform float cityTime;
        uniform float cityWetness;
        uniform float cityRain;
        uniform vec3 citySky;
        uniform sampler2D cityCover;
        varying vec4 vCityUvTile;
        varying float vCityKind;
      `+shader.fragmentShader;
      shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
        float cityFilm=0.0,cityPool=0.0;
        if(vCityKind>0.5){
          vec2 cityUv=vCityUvTile.xy;
          if(vCityKind>6.5)cityUv+=vec2(cityTime*0.025,cityTime*0.012);
          vec2 tile=vec2(vCityUvTile.z,floor(mod(vCityUvTile.w,2.0)+.0001));
          vec2 cityAtlasUv=(tile+mix(vec2(0.016),vec2(0.984),fract(cityUv)))/vec2(4.0,2.0);
          diffuseColor.rgb*=texture2D(cityAtlas,cityAtlasUv).rgb*1.1;
          if(vCityUvTile.w>1.5&&cityWetness>.012){
            vec2 p=cityUv/(vCityKind<1.5?.32:vCityKind<3.5?.50:.55);
            cityFilm=cityWetness*texture2D(cityCover,p/512.0+.5).r;
            float hollows=.48+.22*sin(p.x*.49+sin(p.y*.34))+.18*cos(p.y*.63-p.x*.18)+.10*sin(dot(p,vec2(.73,.37)));
            cityPool=vCityKind>3.5?0.0:smoothstep(.62,.74,hollows)*smoothstep(.25,.84,cityFilm)*(1.0-smoothstep(.025,.105,fract(vCityUvTile.w)));
            float angle=1.0-clamp(abs(dot(normalize(vViewPosition),viewMatrix[1].xyz)),0.0,1.0);
            float ripple=.96+.04*cityRain*sin(10.0*length(fract(p*.63)-.5)-cityTime*7.0+dot(floor(p*.63),vec2(1.9,3.2)));
            diffuseColor.rgb*=1.0-cityFilm*(vCityKind<1.5?.34:.21);
            diffuseColor.rgb=mix(diffuseColor.rgb,citySky*(.23+.53*angle*angle)*ripple+diffuseColor.rgb*.22,cityPool*.74);
          }
        }
      `);
      shader.fragmentShader=shader.fragmentShader.replace('#include <roughnessmap_fragment>',
        '#include <roughnessmap_fragment>\nroughnessFactor=mix(roughnessFactor,.16,clamp(cityFilm*.23+cityPool*.70,0.0,1.0));');
    };
    material.customProgramCacheKey=()=>`city-surfaces-v4:${instanced?'instances':'uniform'}`;
    return material;
  }
  return {decorate,
    setWeatherCover(mask){
      if(!enabled)return;
      const tex=new T.DataTexture(mask,512,512,T.RedFormat);tex.minFilter=tex.magFilter=T.LinearFilter;
      tex.needsUpdate=true;cover.value?.dispose();cover.value=tex;
    },
    update(t,state,skyColor){time.value=t;wetness.value=state?.wetness||0;rain.value=state?.rain||0;if(skyColor)sky.value.copy(skyColor);},
    snapshot:()=>({enabled,atlasSize:texture?[texture.image.width,texture.image.height]:null,styles:7,wetness:wetness.value,filter:software?'light':'anisotropic'}),
    dispose(){texture?.dispose();cover.value?.dispose();}};
}
