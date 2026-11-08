// Fixed pools and distance gates keep ambient motion bounded to six draws.
export function createWorldEffects({THREE:T,scene,groundHeight,trees=[],lake={x:0,z:182,rx:12,rz:6}}){
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
  // Two additional draws, no textures, lights, emitters or per-frame allocation.
  const foam=new T.InstancedMesh(geometry,new T.MeshBasicMaterial({color:'#d8eee2',transparent:true,opacity:.55,depthWrite:false}),96);
  const fireflies=new T.InstancedMesh(geometry,new T.MeshBasicMaterial({color:'#e5ff8d',transparent:true,depthWrite:false,toneMapped:false}),32);
  for(const mesh of [foam,fireflies]){mesh.instanceMatrix.setUsage(T.DynamicDrawUsage);mesh.frustumCulled=false;scene.add(mesh);}
  const bursts=Array.from({length:16},()=>({x:0,z:0,start:-100}));
  const cooldowns=new Float64Array(trees.length);cooldowns.fill(-100);
  // Reuse the leaf material/geometry, with a separate fixed reactive pool.
  const stirred=new T.InstancedMesh(geometry,leaves.material,16);stirred.instanceMatrix.setUsage(T.DynamicDrawUsage);stirred.frustumCulled=false;scene.add(stirred);
  for(let i=0;i<16;i++)stirred.setColorAt(i,colors[i%colors.length]);
  let visibleLeaves=0,visibleRipples=0,visibleFoam=0,visibleFireflies=0,visibleStirred=0,burstCursor=0,triggerCount=0;
  return {
    update(time,focus,night=0,speed=0,onFoot=true){
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
      visibleFoam=0;
      if(Math.abs(focus.z+166)<75){
        for(let i=0;i<96;i++){
          const x=-211+(i%48)*9,phase=time*.85+Math.floor(i/48)*Math.PI+x*.055;
          if(Math.abs(x)<7||Math.abs(x-focus.x)>65)continue;
          const surge=(Math.sin(phase)+1)*.5,z=-168+surge*3.1;
          dummy.position.set(x,-.165,z);dummy.rotation.set(0,Math.sin(x)*.035,0);
          dummy.scale.set(5.5+Math.sin(i*2)*1.2,.025,.10+surge*.23);dummy.updateMatrix();foam.setMatrixAt(visibleFoam++,dummy.matrix);
        }
      }
      foam.count=visibleFoam;foam.visible=visibleFoam>0;foam.material.opacity=.55-night*.35;
      if(foam.visible)foam.instanceMatrix.needsUpdate=true;
      visibleFireflies=0;
      if(night>.15&&Math.hypot(focus.x-lake.x,focus.z-lake.z)<55){
        fireflies.material.opacity=Math.min(1,(night-.15)*2);
        for(let i=0;i<32;i++){
          const a=i*2.39996,drift=Math.sin(time*.45+i)*.7;
          const x=lake.x+Math.cos(a)*(lake.rx+1.2+drift),z=lake.z+Math.sin(a)*(lake.rz+1.2+drift);
          const pulse=Math.max(0,Math.sin(time*1.8+i*2.7)),size=.035+pulse*.065;
          dummy.position.set(x,groundHeight(x,z)+.65+(Math.sin(time*.7+i)+1)*.6,z);
          dummy.rotation.set(0,time*.3+i,0);dummy.scale.set(size,size,size);dummy.updateMatrix();fireflies.setMatrixAt(visibleFireflies++,dummy.matrix);
        }
      }
      fireflies.count=visibleFireflies;fireflies.visible=visibleFireflies>0;
      if(fireflies.visible)fireflies.instanceMatrix.needsUpdate=true;
      if(onFoot&&speed>6){
        for(let i=0;i<trees.length;i++){
          const tree=trees[i];
          if(time-cooldowns[i]<2.5||Math.hypot(tree.x-focus.x,tree.z-focus.z)>3)continue;
          cooldowns[i]=time;triggerCount++;
          for(let j=0;j<8;j++){const b=bursts[burstCursor++%bursts.length];b.x=tree.x;b.z=tree.z;b.start=time-j*.035;}
          break;
        }
      }
      visibleStirred=0;
      for(let i=0;i<bursts.length;i++){
        const b=bursts[i],age=time-b.start;if(age<0||age>1.8||Math.hypot(b.x-focus.x,b.z-focus.z)>35)continue;
        const a=i*2.39996,fade=Math.min(1,(1.8-age)*3),x=b.x+Math.cos(a)*(.7+age*1.5),z=b.z+Math.sin(a)*(.7+age*1.5);
        dummy.position.set(x,groundHeight(x,z)+.08+(1-age/1.8)*2.4,z);dummy.rotation.set(age*6+i,a,age*8);
        dummy.scale.set(.17*fade,.025*fade,.28*fade);dummy.updateMatrix();stirred.setMatrixAt(visibleStirred++,dummy.matrix);
      }
      stirred.count=visibleStirred;stirred.visible=visibleStirred>0;if(stirred.visible)stirred.instanceMatrix.needsUpdate=true;
    },
    snapshot:()=>({leaves:visibleLeaves,ripples:visibleRipples,foam:visibleFoam,fireflies:visibleFireflies,stirred:visibleStirred,triggers:triggerCount,budget:188,maxDrawCalls:6}),
    dispose(){for(const mesh of [drops,rings,leaves,foam,fireflies,stirred])mesh.removeFromParent();geometry.dispose();ringGeometry.dispose();drops.material.dispose();ringMaterial.dispose();leaves.material.dispose();foam.material.dispose();fireflies.material.dispose();}
  };
}
