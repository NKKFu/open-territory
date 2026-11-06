// Screen-space contact shading, using the same depth as the rendered city.
// Only the small AO buffer is sampled repeatedly; color is rendered once.
export function createAmbientOcclusion({THREE:T,renderer,scene,camera,software=false}){
  const supported=renderer.capabilities.isWebGL2&&renderer.extensions.has('EXT_color_buffer_float');
  if(!supported)return {render:()=>renderer.render(scene,camera),snapshot:()=>({enabled:false,reason:'depth-render-target-unavailable'})};
  const colorTarget=new T.WebGLRenderTarget(1,1,{type:T.HalfFloatType,depthBuffer:true});
  colorTarget.depthTexture=new T.DepthTexture(1,1,T.UnsignedIntType);
  colorTarget.texture.colorSpace=T.LinearSRGBColorSpace;
  colorTarget.samples=software?0:Math.min(2,renderer.capabilities.maxSamples);
  const aoTarget=new T.WebGLRenderTarget(1,1,{depthBuffer:false,minFilter:T.LinearFilter,magFilter:T.LinearFilter});
  const blurTarget=aoTarget.clone();
  const postScene=new T.Scene(),postCamera=new T.OrthographicCamera(-1,1,1,-1,0,1);
  const vertexShader='varying vec2 vUv; void main(){vUv=uv;gl_Position=vec4(position.xy,0.0,1.0);}';
  const positionShader=`
    uniform sampler2D tDepth;
    uniform mat4 inverseProjection;
    varying vec2 vUv;
    vec3 viewPosition(vec2 uv){
      float depth=texture2D(tDepth,uv).x;
      vec4 position=inverseProjection*vec4(uv*2.0-1.0,depth*2.0-1.0,1.0);
      return position.xyz/position.w;
    }
  `;
  const aoMaterial=new T.ShaderMaterial({
    depthTest:false,depthWrite:false,toneMapped:false,vertexShader,
    uniforms:{tDepth:{value:colorTarget.depthTexture},inverseProjection:{value:camera.projectionMatrixInverse},projection:{value:camera.projectionMatrix},
      texel:{value:new T.Vector2()},radius:{value:1.15}},
    fragmentShader:`${positionShader}
      uniform mat4 projection;
      uniform vec2 texel;
      uniform float radius;
      void main(){
        if(texture2D(tDepth,vUv).x>=0.99999){gl_FragColor=vec4(1.0);return;}
        vec3 p=viewPosition(vUv);
        vec3 left=viewPosition(vUv-vec2(texel.x,0.0)),right=viewPosition(vUv+vec2(texel.x,0.0));
        vec3 below=viewPosition(vUv-vec2(0.0,texel.y)),above=viewPosition(vUv+vec2(0.0,texel.y));
        vec3 dx=abs(right.z-p.z)<abs(p.z-left.z)?right-p:p-left;
        vec3 dy=abs(above.z-p.z)<abs(p.z-below.z)?above-p:p-below;
        vec3 normal=normalize(cross(dx,dy));
        if(dot(normal,-p)<0.0)normal=-normal;
        float noise=fract(sin(dot(floor(gl_FragCoord.xy),vec2(12.9898,78.233)))*43758.5453)*6.283185;
        vec3 randomVector=vec3(cos(noise),sin(noise),0.0);
        vec3 tangent=normalize(randomVector-normal*dot(randomVector,normal));
        vec3 bitangent=cross(normal,tangent);
        float occlusion=0.0;
        for(int i=0;i<${software?8:16};i++){
          float f=(float(i)+0.5)/${software?'8.0':'16.0'};
          float angle=float(i)*2.399963;
          float h=0.15+f*0.8,spread=sqrt(1.0-h*h);
          vec3 direction=tangent*cos(angle)*spread+bitangent*sin(angle)*spread+normal*h;
          vec3 samplePosition=p+direction*radius*mix(0.2,1.0,f*f);
          vec4 projected=projection*vec4(samplePosition,1.0);
          vec2 uv=projected.xy/projected.w*0.5+0.5;
          if(uv.x<0.0||uv.x>1.0||uv.y<0.0||uv.y>1.0)continue;
          vec3 surface=viewPosition(uv);
          float range=smoothstep(0.0,1.0,radius/max(abs(p.z-surface.z),0.0001));
          occlusion+=step(samplePosition.z+0.035,surface.z)*range;
        }
        float ao=clamp(1.0-occlusion/${software?'8.0':'16.0'}*1.3,0.42,1.0);
        gl_FragColor=vec4(vec3(ao),1.0);
      }
    `
  });
  const blurMaterial=new T.ShaderMaterial({
    depthTest:false,depthWrite:false,toneMapped:false,vertexShader,
    uniforms:{tDepth:{value:colorTarget.depthTexture},inverseProjection:{value:camera.projectionMatrixInverse},tAO:{value:aoTarget.texture},direction:{value:new T.Vector2()}},
    fragmentShader:`${positionShader}
      uniform sampler2D tAO;
      uniform vec2 direction;
      void main(){
        float center=viewPosition(vUv).z,shade=0.0,weightSum=0.0;
        for(int i=-3;i<=3;i++){
          vec2 uv=vUv+direction*float(i);
          float weight=exp(-float(i*i)*0.16-abs(viewPosition(uv).z-center)*5.0);
          shade+=texture2D(tAO,uv).r*weight;weightSum+=weight;
        }
        gl_FragColor=vec4(vec3(shade/max(weightSum,0.0001)),1.0);
      }
    `
  });
  const compositeMaterial=new T.ShaderMaterial({
    depthTest:false,depthWrite:false,vertexShader,
    uniforms:{tColor:{value:colorTarget.texture},tAO:{value:aoTarget.texture},colorTexel:{value:new T.Vector2()},strength:{value:1}},
    fragmentShader:`
      varying vec2 vUv;
      uniform sampler2D tColor;
      uniform sampler2D tAO;
      uniform vec2 colorTexel;
      uniform float strength;
      vec3 sceneColor(){
        vec3 center=texture2D(tColor,vUv).rgb;
        ${software?`
          // Edge antialiasing avoids a multisampled HDR city buffer on a CPU.
          vec3 nw=texture2D(tColor,vUv+colorTexel*vec2(-1.0,-1.0)).rgb;
          vec3 ne=texture2D(tColor,vUv+colorTexel*vec2(1.0,-1.0)).rgb;
          vec3 sw=texture2D(tColor,vUv+colorTexel*vec2(-1.0,1.0)).rgb;
          vec3 se=texture2D(tColor,vUv+colorTexel*vec2(1.0,1.0)).rgb;
          vec3 luma=vec3(0.299,0.587,0.114);
          float lnw=dot(nw,luma),lne=dot(ne,luma),lsw=dot(sw,luma),lse=dot(se,luma),lc=dot(center,luma);
          float lo=min(lc,min(min(lnw,lne),min(lsw,lse))),hi=max(lc,max(max(lnw,lne),max(lsw,lse)));
          if(hi-lo<max(0.04,hi*0.125))return center;
          vec2 dir=vec2(-((lnw+lne)-(lsw+lse)),(lnw+lsw)-(lne+lse));
          float reduce=max((lnw+lne+lsw+lse)*0.03125,0.0078125);
          dir=clamp(dir/(min(abs(dir.x),abs(dir.y))+reduce),vec2(-6.0),vec2(6.0))*colorTexel;
          vec3 a=0.5*(texture2D(tColor,vUv-dir/6.0).rgb+texture2D(tColor,vUv+dir/6.0).rgb);
          vec3 b=a*0.5+0.25*(texture2D(tColor,vUv-dir*0.5).rgb+texture2D(tColor,vUv+dir*0.5).rgb);
          float lb=dot(b,luma);return lb<lo||lb>hi?a:b;
        `:'return center;'}
      }
      void main(){
        float shade=texture2D(tAO,vUv).r;
        gl_FragColor=vec4(sceneColor()*mix(1.0,shade,strength),1.0);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }
    `
  });
  const quad=new T.Mesh(new T.PlaneGeometry(2,2),aoMaterial);quad.frustumCulled=false;postScene.add(quad);
  const size=new T.Vector2();let width=0,height=0;
  function resize(){
    renderer.getDrawingBufferSize(size);
    if(size.x===width&&size.y===height)return;
    width=size.x;height=size.y;
    const scale=software?.4:.5;
    colorTarget.setSize(width,height);aoTarget.setSize(Math.max(1,Math.ceil(width*scale)),Math.max(1,Math.ceil(height*scale)));blurTarget.setSize(aoTarget.width,aoTarget.height);
    aoMaterial.uniforms.texel.value.set(1/width,1/height);
    compositeMaterial.uniforms.colorTexel.value.set(1/width,1/height);
  }
  return {
    render(){
      resize();const previous=renderer.getRenderTarget(),autoReset=renderer.info.autoReset;
      renderer.info.autoReset=false;renderer.info.reset();
      renderer.setRenderTarget(colorTarget);renderer.render(scene,camera);
      quad.material=aoMaterial;renderer.setRenderTarget(aoTarget);renderer.render(postScene,postCamera);
      quad.material=blurMaterial;blurMaterial.uniforms.tAO.value=aoTarget.texture;blurMaterial.uniforms.direction.value.set(1/aoTarget.width,0);
      renderer.setRenderTarget(blurTarget);renderer.render(postScene,postCamera);
      blurMaterial.uniforms.tAO.value=blurTarget.texture;blurMaterial.uniforms.direction.value.set(0,1/aoTarget.height);
      renderer.setRenderTarget(aoTarget);renderer.render(postScene,postCamera);
      quad.material=compositeMaterial;renderer.setRenderTarget(previous);renderer.render(postScene,postCamera);
      renderer.info.autoReset=autoReset;
    },
    snapshot:()=>({enabled:true,method:'SSAO',samples:software?8:16,width:aoTarget.width,height:aoTarget.height}),
    // Kept out of the game's interface; useful for a fixed-frame visual review.
    setStrength(value){compositeMaterial.uniforms.strength.value=Math.max(0,Math.min(1,value));},
    dispose(){colorTarget.dispose();aoTarget.dispose();blurTarget.dispose();aoMaterial.dispose();blurMaterial.dispose();compositeMaterial.dispose();quad.geometry.dispose();}
  };
}
