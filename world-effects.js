// Fixed pools: a breeze, fountain droplets and water rings add motion with
// three draws, regardless of how long the player keeps exploring.
export function createWorldEffects({THREE:T,scene,groundHeight}){
  const dummy=new T.Object3D(),geometry=new T.BoxGeometry(1,1,1),count=10;
  const drops=new T.InstancedMesh(geometry,new T.MeshStandardMaterial({color:'#b4e6e5',roughness:.28}),count);
  drops.instanceMatrix.setUsage(T.DynamicDrawUsage);drops.frustumCulled=false;scene.add(drops);
  const fades=new T.InstancedBufferAttribute(new Float32Array(count),1);
  const ringGeometry=new T.PlaneGeometry(1,1);ringGeometry.setAttribute('effectFade',fades);
  const ringMaterial=new T.ShaderMaterial({transparent:true,depthWrite:false,uniforms:{brightness:{value:1}},
    vertexShader:'attribute float effectFade; varying vec2 vUv; varying float vFade; void main(){vUv=uv;vFade=effectFade;gl_Position=projectionMatrix*modelViewMatrix*instanceMatrix*vec4(position,1.0);}',
    fragmentShader:`varying vec2 vUv; varying float vFade; uniform float brightness;
      void main(){float radius=length(vUv-0.5)*2.0;float ring=smoothstep(0.65,0.8,radius)*(1.0-smoothstep(0.85,1.0,radius));
        gl_FragColor=vec4(vec3(0.62,0.91,0.94)*brightness,ring*vFade*0.4);
        #include <colorspace_fragment>
      }`
  });
  const rings=new T.InstancedMesh(ringGeometry,ringMaterial,count);rings.frustumCulled=false;scene.add(rings);
  const leaves=new T.InstancedMesh(geometry,new T.MeshStandardMaterial({color:'#ffffff',roughness:1}),24);
  leaves.instanceMatrix.setUsage(T.DynamicDrawUsage);leaves.frustumCulled=false;scene.add(leaves);
  const anchors=[[-52,20],[-20,20],[-52,52],[-20,52],[94,94],[114,94],[94,114],[114,114]];
  const colors=['#74a261','#a7b775','#c9ad6b'].map(c=>new T.Color(c));
  for(let i=0;i<24;i++)leaves.setColorAt(i,colors[i%colors.length]);
  let visibleLeaves=0,visibleRipples=0;
  return {
    update(time,focus,night=0){
      drops.visible=rings.visible=Math.hypot(focus.x+36,focus.z-36)<75;visibleRipples=drops.visible?count:0;
      if(drops.visible){
        for(let i=0;i<count;i++){
          const t=(time*.6+i/count)%1,angle=i*Math.PI*2/count,radius=.5+t*2.6;
          dummy.position.set(-36+Math.cos(angle)*radius,2.85+t*3.2-t*t*5.13,36+Math.sin(angle)*radius);
          dummy.rotation.set(t*2,angle,t);dummy.scale.set(.14,.2,.14);dummy.updateMatrix();drops.setMatrixAt(i,dummy.matrix);
          const age=Math.min(1,t/.42),size=.35+age*.9;
          dummy.position.set(-36+Math.cos(angle)*3.1,.927,36+Math.sin(angle)*3.1);dummy.rotation.set(-Math.PI/2,0,0);dummy.scale.set(size,size,1);dummy.updateMatrix();rings.setMatrixAt(i,dummy.matrix);
          fades.setX(i,1-age);
        }
        ringMaterial.uniforms.brightness.value=1-night*.7;drops.instanceMatrix.needsUpdate=true;rings.instanceMatrix.needsUpdate=true;fades.needsUpdate=true;
      }
      visibleLeaves=0;
      for(let i=0;i<24;i++){
        const [x,z]=anchors[i%anchors.length];if(Math.hypot(x-focus.x,z-focus.z)>60)continue;
        const t=(time*.075+i*.471)%1,px=x+t*2.2+Math.sin(t*7+i)*.65,pz=z+Math.sin(t*5+i)*.9;
        const floor=groundHeight(px,pz),fade=Math.min(1,t*12,(1-t)*12);
        dummy.position.set(px,floor+.035+(1-t)**1.4*4.6,pz);dummy.rotation.set(t*8+i,t*5,t*7);
        dummy.scale.set(.13*fade,.025*fade,.24*fade);dummy.updateMatrix();leaves.setMatrixAt(visibleLeaves++,dummy.matrix);
      }
      leaves.count=visibleLeaves;leaves.visible=visibleLeaves>0;leaves.instanceMatrix.needsUpdate=true;
    },
    snapshot:()=>({leaves:visibleLeaves,ripples:visibleRipples,budget:44}),
    dispose(){drops.removeFromParent();rings.removeFromParent();leaves.removeFromParent();geometry.dispose();ringGeometry.dispose();drops.material.dispose();ringMaterial.dispose();leaves.material.dispose();}
  };
}
