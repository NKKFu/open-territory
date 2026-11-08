import {seaLevel,shoreZ,seabed,waveHeight} from './marine.js';

export function createMarineArt({THREE:T,scene,part}){
  const paint=(color,roughness=.72)=>new T.MeshStandardMaterial({color,roughness,flatShading:true});
  const rope=paint('#a18462'),seal=paint('#263e49'),cream=paint('#e8ddc2');
  function cylinder(parent,x,y,z,a,b,h,material,sides=8){const m=new T.Mesh(new T.CylinderGeometry(a,b,h,sides),material);
    m.position.set(x,y,z);m.castShadow=m.receiveShadow=true;parent.add(m);return m;}
  function strut(parent,a,b,r,material){const start=new T.Vector3(...a),end=new T.Vector3(...b),axis=end.clone().sub(start);
    const beam=cylinder(parent,0,0,0,r,r,axis.length(),material,5);beam.position.copy(start).addScaledVector(axis,.5);
    beam.quaternion.setFromUnitVectors(new T.Vector3(0,1,0),axis.normalize());}
  function panel(parent,vertices,color){const geo=new T.BufferGeometry();geo.setAttribute('position',new T.Float32BufferAttribute(vertices,3));geo.computeVertexNormals();
    const mat=paint(color);mat.side=T.DoubleSide;const m=new T.Mesh(geo,mat);m.castShadow=m.receiveShadow=true;parent.add(m);return m;}
  function buildBoat(b){
    const g=new T.Group();scene.add(g);const w=b.w/2,d=b.d/2,stations=[[-1,.64,.56],[-.73,.96,.52],[.04,1,.51],[.53,.83,.60],[.83,.51,.72],[1,.055,.84]];
    // A V-keel and rising bow share their chines with the cream rubbing
    // strake and deck. There is no rectangular slab sticking out at the bow.
    const sides=[],keel=[],deck=[],stripe=[];
    function quad(dest,a,b,c,d){dest.push(...a,...b,...c,...a,...c,...d);}
    for(let i=1;i<stations.length;i++){
      const [z0,k0,h0]=stations[i-1],[z1,k1,h1]=stations[i];
      for(const sign of [-1,1]){
        const a=[sign*w*k0,h0,d*z0],c=[sign*w*k1,h1,d*z1],lo0=[sign*w*k0*.74,-.12,d*z0],lo1=[sign*w*k1*.74,-.12,d*z1];
        quad(sides,lo0,lo1,c,a);quad(keel,[0,-.44,z0*d],lo0,lo1,[0,-.44,z1*d]);
        quad(stripe,[a[0]*1.008,h0-.14,a[2]],[c[0]*1.008,h1-.14,c[2]],[c[0]*1.008,h1-.07,c[2]],[a[0]*1.008,h0-.07,a[2]]);
      }
      quad(deck,[-w*k0,h0,d*z0],[w*k0,h0,d*z0],[w*k1,h1,d*z1],[-w*k1,h1,d*z1]);
    }
    quad(sides,[-w*.64,.56,-d],[w*.64,.56,-d],[w*.47,-.12,-d],[-w*.47,-.12,-d]);
    panel(g,sides,b.color);panel(g,keel,'#344d52');panel(g,deck,'#d4bc89');panel(g,stripe,'#f5e7c9');
    part(g,0,.56,-.9,w*1.35,.09,1.5,'#9b7c56');
    if(b.model==='sail'){
      strut(g,[0,.55,.45],[0,5.0,.45],.052,rope);strut(g,[0,.82,-1.9],[0,4.65,.45],.018,seal);
      panel(g,[.02,1.59,-1.82,.02,4.85,.44,.02,1.55,.38],'#ece6cd');
      panel(g,[-.02,1.50,.7,-.02,4.45,.48,-.02,1.13,2.63],'#ead1a4');
    }else if(b.model==='fishing'){
      part(g,0,.95,1.16,1.63,.72,1.48,'#ebdeb5');part(g,0,1.55,1.22,1.54,.5,1.28,'#6e999b');
      part(g,0,1.86,1.22,1.85,.13,1.59,'#efe6ce');
      for(const side of [-1,1])strut(g,[side*.69,.61,-1.4],[side*.88,2,-1.9],.038,rope);
      part(g,-.60,.72,-.58,.66,.34,.78,'#819583');
    }else{
      part(g,.43,.86,.42,.69,.56,.55,'#dedac8');
      for(const side of [-1,1])strut(g,[side*.90,.55,1.14],[side*.78,1.09,.83],.033,seal);
      panel(g,[-.78,1.09,.83,.78,1.09,.83,.89,.67,1.14,-.78,1.09,.83,.89,.67,1.14,-.89,.67,1.14],'#8fb6b7');
      if(b.model==='rescue'){for(const x of [-w,w])part(g,x,.36,-.33,.2,.22,3.6,'#4e5660');part(g,-.54,.85,-.88,.68,.48,.9,'#e8dfcb');}
    }
    const helmX=b.model==='sail'?0:.43,helmZ=b.model==='fishing'?.47:-.40;
    part(g,helmX,.69,helmZ,.65,.22,.62,'#606e65');part(g,helmX,.91,helmZ-.3,.63,.45,.14,'#606e65');
    const wheel=new T.Mesh(new T.TorusGeometry(.19,.027,5,10),seal);wheel.position.set(helmX,.98,helmZ+.47);wheel.rotation.x=-.63;g.add(wheel);
    const ring=new T.Mesh(new T.TorusGeometry(.28,.075,5,12),paint('#e38b49'));ring.rotation.x=Math.PI/2;ring.position.set(-.45,.62,-1.65);g.add(ring);
    for(const side of [-1,1]){strut(g,[side*w*.55,.67,.82*d],[side*w*.55,.92,.82*d],.022,cream);
      cylinder(g,side*w*.55,.95,.82*d,.05,.055,.08,paint(side<0?'#cb5048':'#5caa92'));}
    part(g,0,.47,-d,.51,.59,.40,'#394c55');part(g,0,.72,-d-.02,.45,.17,.35,'#d2c9ad');
    b.wheel=wheel;return g;
  }

  const grid=new T.PlaneGeometry(1000,270,144,66);grid.rotateX(-Math.PI/2);grid.translate(0,0,shoreZ-135);
  const coords=grid.attributes.position,colors=[],pale=new T.Color('#79bfc0'),deep=new T.Color('#458994'),tint=new T.Color();
  for(let i=0;i<coords.count;i++){
    const x=coords.getX(i),z=coords.getZ(i),depth=seaLevel-seabed(x,z);
    tint.copy(pale).lerp(deep,Math.max(0,Math.min(1,(depth+.8)/8+.045*Math.sin(x*.16+z*.18))));colors.push(tint.r,tint.g,tint.b);
    coords.setY(i,seaLevel);
  }
  grid.setAttribute('color',new T.Float32BufferAttribute(colors,3));
  const surfaceMaterial=new T.MeshStandardMaterial({vertexColors:true,roughness:.39,metalness:.13,flatShading:true});
  const clock={value:0},chop={value:0};
  surfaceMaterial.onBeforeCompile=shader=>{
    shader.uniforms.bayTime=clock;shader.uniforms.bayChop=chop;
    shader.vertexShader='varying vec2 bayPoint;\n'+shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nbayPoint=position.xz;');
    shader.fragmentShader='varying vec2 bayPoint; uniform float bayTime; uniform float bayChop;\n'+shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
      float longWave=sin(bayPoint.y*.43+bayPoint.x*.14+bayTime*.82+1.8*sin(bayPoint.x*.095)+.67*sin(bayPoint.y*.12));
      float facet=sin(bayPoint.y*2.8+bayPoint.x*.39+bayTime*1.58+2.4*sin(bayPoint.x*.21+bayPoint.y*.16));
      float patches=sin(bayPoint.x*.58+bayPoint.y*.33-bayTime*.20)*sin(bayPoint.x*.18-bayPoint.y*.20);
      float visibleWave=1.0-smoothstep(45.0,145.0,length(vViewPosition));
      float glint=smoothstep(.78,.98,facet)*smoothstep(.13,.58,patches+.16*longWave);
      diffuseColor.rgb*=1.0+visibleWave*(.047*longWave+.028*facet);
      diffuseColor.rgb+=visibleWave*glint*vec3(.041,.067,.065)*(1.0+.9*bayChop);`);
  };
  const water=new T.Mesh(grid,surfaceMaterial);
  water.receiveShadow=true;water.frustumCulled=false;scene.add(water);
  // The navigable patch ends at the buoys. Continue the visible ocean under
  // the remote forest and around both headlands, including from the drone.
  const horizon=new T.PlaneGeometry(2400,1550,112,116);horizon.rotateX(-Math.PI/2);horizon.translate(0,0,-170);
  const distant=horizon.attributes.position,farColors=[];
  for(let i=0;i<distant.count;i++){
    const x=distant.getX(i),z=distant.getZ(i),forest=Math.abs(x)<410&&z> -130;
    distant.setY(i,forest?-.88:seaLevel);
    const offshore=Math.abs(x)>214?Math.min(1,(Math.abs(x)-214)/46):Math.max(0,shoreZ-z)/28;
    tint.copy(pale).lerp(deep,Math.max(.16,Math.min(1,offshore)));farColors.push(tint.r,tint.g,tint.b);
  }
  horizon.setAttribute('color',new T.Float32BufferAttribute(farColors,3));
  const farMaterial=surfaceMaterial.clone();farMaterial.customProgramCacheKey=()=>'bay-horizon';
  farMaterial.onBeforeCompile=s=>{surfaceMaterial.onBeforeCompile(s);s.fragmentShader=s.fragmentShader.replace('#include <alphatest_fragment>',
    `if(abs(bayPoint.x)<500.0 && bayPoint.y<=${shoreZ} && bayPoint.y>=${shoreZ-270})discard;\n#include <alphatest_fragment>`);};
  scene.add(new T.Mesh(horizon,farMaterial));
  // The authored sand ends at x ±215. A tapered beach continues past each
  // playable limit and meets the existing forest shelf at z=-125.
  const edge=[[215,-166],[219,-165.6],[225,-164.1],[235,-160.6],[245,-155.4],[259,-148.7],[271,-141.2],[285,-133.4],[301,-125.1]];
  for(const sign of [-1,1]){
    const sand=[],bevel=[];
    for(let i=1;i<edge.length;i++){
      const [ax,az]=edge[i-1],[bx,bz]=edge[i],a=[ax*sign,-.19,az],b=[bx*sign,-.19,bz];
      const c=[bx*sign,-.24,-124.4],d=[ax*sign,i===1?-.19:-.24,i===1?-124:-124.4];sand.push(...a,...b,...c,...a,...c,...d);
      const aa=[ax*sign,-.69,az-2.5],bb=[bx*sign,-.69,bz-2.5];bevel.push(...a,...aa,...bb,...a,...bb,...b);
    }
    panel(scene,sand,'#e5c998');panel(scene,bevel,'#c5ae85');
    for(const [x,z,scale] of [[231,-164.4,.82],[233.4,-163.8,.49],[264,-147.6,.60],[267,-146.4,.40]]){
      const stone=new T.Mesh(new T.DodecahedronGeometry(1,0),paint('#929e91'));stone.position.set(x*sign,-.36,z);stone.scale.set(scale*1.6,scale*.72,scale);
      stone.rotation.y=x*.61;stone.castShadow=true;scene.add(stone);
    }
  }
  const markers=[],markerMat=paint('#d78a43'),red=paint('#bd6153'),green=paint('#539486');
  function buoy(x,z,type=0){const g=new T.Group(),accent=type===1?red:type===2?green:markerMat;
    cylinder(g,0,.13,0,.35,.52,.48,accent);cylinder(g,0,.48,0,.18,.29,.24,cream);
    cylinder(g,0,.86,0,.08,.12,.72,accent);cylinder(g,0,1.28,0,.13,.18,.16,seal);
    cylinder(g,0,1.42,0,type===2?0:.105,.12,.2,accent);g.position.set(x,seaLevel,z);scene.add(g);markers.push(g);return g;}
  function foamPool(shape,count){
    const opacity=new T.InstancedBufferAttribute(new Float32Array(count),1);shape.setAttribute('foamOpacity',opacity);
    const mat=new T.MeshBasicMaterial({color:'#c6ede1',side:T.DoubleSide,transparent:true,depthWrite:false});
    mat.onBeforeCompile=s=>{s.vertexShader='attribute float foamOpacity; varying float foamFade;\n'+s.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nfoamFade=foamOpacity;');
      s.fragmentShader='varying float foamFade;\n'+s.fragmentShader.replace('#include <color_fragment>','#include <color_fragment>\ndiffuseColor.a*=foamFade;');};
    const mesh=new T.InstancedMesh(shape,mat,count);mesh.count=0;mesh.frustumCulled=false;scene.add(mesh);return {mesh,opacity};
  }
  const strip=new T.BufferGeometry();strip.setAttribute('position',new T.Float32BufferAttribute([-.24,0,-.8,.13,0,-.6,.32,0,.35,-.24,0,-.8,.32,0,.35,-.42,0,.13,-.42,0,.13,.32,0,.35,.02,0,.96],3));
  const wash=foamPool(strip,64),rings=foamPool(new T.RingGeometry(.91,1,24,1,.32,Math.PI*1.47),32);
  const foam=Array.from({length:32},()=>({age:20})),dummy=new T.Object3D();
  let foamIndex=0,lastWake=-1,lastWave=-2;
  function update(time,dt,marine,state,focus){
    const strength=state?.seaState||0,close=focus.z< -112,interval=close?.09:.5;clock.value=time;chop.value=strength;
    if(time-lastWave>=interval){lastWave=time;
      for(let i=0;i<coords.count;i++){const x=coords.getX(i),z=coords.getZ(i);
        if(z> -265&&Math.abs(x-focus.x)<150)coords.setY(i,waveHeight(x,z,time,strength));}
      coords.needsUpdate=true;
    }
    for(const m of markers){m.position.y=waveHeight(m.position.x,m.position.z,time,strength);m.rotation.z=.035*Math.sin(time*1.3+m.position.x);}
    const w=marine.wake;if(w&&w.time!==lastWake){lastWake=w.time;Object.assign(foam[foamIndex++%foam.length],w,{age:0});}
    wash.mesh.count=rings.mesh.count=0;
    for(const a of foam){a.age+=dt;const duration=a.width?2.8:1.6;if(a.age>duration)continue;
      const fade=1-a.age/duration;
      if(a.width){
        for(const sign of [-1,1]){
          const spread=sign*(a.width*.25+a.age*(.54+.035*a.speed)),c=Math.cos(a.heading),s=Math.sin(a.heading);
          const x=a.x+c*spread,z=a.z-s*spread;dummy.position.set(x,waveHeight(x,z,time,strength)+.033,z);
          dummy.rotation.set(0,a.heading+sign*.30,0);dummy.scale.set(.18+.14*a.age,1,.55+a.speed*.21);
          dummy.updateMatrix();const i=wash.mesh.count++;wash.mesh.setMatrixAt(i,dummy.matrix);wash.opacity.setX(i,.39*fade*fade);
        }
      }else{
        const size=.17+.41*a.age;dummy.position.set(a.x,waveHeight(a.x,a.z,time,strength)+.028,a.z);
        dummy.rotation.set(-Math.PI/2,0,a.heading+.55*Math.sin(a.time));dummy.scale.set(size,size*.82,1);dummy.updateMatrix();
        const i=rings.mesh.count++;rings.mesh.setMatrixAt(i,dummy.matrix);rings.opacity.setX(i,.36*fade*fade);
      }
    }
    for(const pool of [wash,rings]){pool.mesh.visible=pool.mesh.count>0;if(pool.mesh.visible){pool.mesh.instanceMatrix.needsUpdate=true;pool.opacity.needsUpdate=true;}}
    for(const b of marine.boats)if(b.wheel)b.wheel.rotation.z=b.turnRate*.7;
  }
  return {buildBoat,buoy,update};
}
