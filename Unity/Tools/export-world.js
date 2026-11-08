// Offline conversion runs inside the original game's lexical scope. Native builds contain no JavaScript.
window.__unityExport=()=>{
 const id=o=>o?.id||0,ids=a=>(a||[]).map(id),num=(v,f=0)=>Number.isFinite(v)?v:f;
 const v=p=>({x:num(p?.x),y:num(p?.y),z:num(p?.z)}),at=(x,z,h=0)=>({x,y:groundHeight(x,z)+h,z});
 const color=(c,a=1)=>({r:c?.r??1,g:c?.g??1,b:c?.b??1,a});
 const rig=g=>{const u=g.userData,m=u.rig||{},isPlayer=g===player,ankles=u.ankles||u.shoes||[];return {root:id(g),hips:id(u.hips),chest:id(u.chest),head:id(u.head),limbs:ids(u.limbs),knees:ids(u.knees),elbows:ids(u.elbows),hands:ids(u.hands),ankles:ids(ankles),hipHeight:m.hipHeight??u.hips?.position.y??.84,ankleHeight:m.ankleHeight??(isPlayer?.125:.08),thighLength:m.thighLength??Math.abs(u.knees?.[0]?.position.y??-.41),shinLength:m.shinLength??Math.abs(ankles[0]?.position.y??-.35),upperLength:m.upperLength??Math.abs(u.elbows?.[0]?.position.y??-.38),lowerLength:m.lowerLength??Math.abs(u.hands?.[0]?.position.y??-.4),seated:!!u.seated,seatHeight:m.seatHeight??0};};
 const fish=fishMarket.exportData(),port=harbor.exportData(),movie=streetCinema.exportData();
 fishMarket.update(0,0);scene.updateMatrixWorld(true);
 const owners=new Map();
 for(const p of people)if(p.bodyCollider)owners.set(p.bodyCollider,id(p.g));
 for(const d of destructibles){owners.set(d.collider,id(d.group));if(d.backCollider)owners.set(d.backCollider,id(d.group));if(d.cameraObstacle)owners.set(d.cameraObstacle,id(d.group));}
 owners.set(port.cargoSolid.body,id(port.crate));owners.set(port.cargoSolid.view,id(port.crate));
 const key=s=>[s.x,s.z,s.w,s.d].map(n=>num(n).toFixed(3)).join(',');
 const exact=new Map(cameraObstacles.map(s=>[key(s),s]));
 const bounds=(s,index)=>{const match=exact.get(key(s)),base=groundHeight(s.x,s.z);return {index,owner:owners.get(s)||0,x:s.x,z:s.z,w:s.w,d:s.d,bottom:num(s.bottom,match?.bottom??base),top:num(s.supportTop??s.vaultTop??s.top,match?.top??base+(s.opaque?6:3.2)),active:s.active!==false,vault:Number.isFinite(s.vaultTop),opaque:!!s.opaque,rangeBackstop:!!s.rangeBackstop,yaw:0};};
 const nativeSolids=solids.map(bounds),solidKeys=new Set(solids.map(key));
 for(const s of cameraObstacles)if(!solidKeys.has(key(s))&&!owners.has(s)&&s.bottom>groundHeight(s.x,s.z)+2.3)nativeSolids.push(bounds(s,nativeSolids.length));
 function route(c){
  const points=rawRoute(c),poles=nativeSolids.filter(s=>s.active&&!s.owner&&s.w<.28&&s.d<.28&&s.top-s.bottom>.7);
  // The roadside poles are preserved. Give their actual collision envelopes
  // a small, gradual berth inside the lane instead of driving through them.
  return points.map((p,i)=>{
   const before=points[(i+points.length-1)%points.length],after=points[(i+1)%points.length],dx=after.x-before.x,dz=after.z-before.z,length=Math.hypot(dx,dz);
   if(length<.001)return p;const tx=dx/length,tz=dz/length,nx=tz,nz=-tx;let shift=0;
   for(const s of poles){
    if(s.top<p.y+.25||s.bottom>p.y+2.2)continue;
    const x=s.x-p.x,z=s.z-p.z,along=x*tx+z*tz,side=x*nx+z*nz,margin=c.width/2+Math.max(s.w,s.d)/2+.18;
    if(Math.abs(along)>8||Math.abs(side)>margin||Math.abs(side)<.4)continue;
    const full=c.length/2+1,u=Math.max(0,Math.min(1,(Math.abs(along)-full)/(8-full))),fade=1-u*u*(3-2*u);
    shift-=Math.sign(side)*Math.min(.55,margin-Math.abs(side))*fade;
   }
   return Math.abs(shift)>.001?at(p.x+nx*shift,p.z+nz*shift):p;
  });
 }
 function rawRoute(c){
  if(c.track){if(c.axis==='district'){const a=[];for(let d=0;d<c.track.length;d+=2.5){const p=districtTrackPosition(c.track,d,c.homeLane||0);a.push(at(p.x,p.z));}return a;}return c.track.points.map(p=>at(p.x,p.z));}
  if(!c.auto)return [];
  const points=[],add=(x,z)=>{const a=points.at(-1),n=a?Math.max(1,Math.ceil(Math.hypot(a.x-x,a.z-z)/2.5)):1;for(let j=1;j<=n;j++)points.push(at(a?a.x+(x-a.x)*j/n:x,a?a.z+(z-a.z)*j/n:z));};
  if(c.curve){for(let x=-199;x<=199;x+=2)add(x,curvedRoadZ(x)-3.3);for(let i=1;i<=16;i++){const a=-Math.PI/2+i*Math.PI/16;add(199+Math.cos(a)*3.3,curvedRoadZ(199)+Math.sin(a)*3.3);}for(let x=199;x>=-199;x-=2)add(x,curvedRoadZ(x)+3.3);for(let i=1;i<=16;i++){const a=Math.PI/2+i*Math.PI/16;add(-199+Math.cos(a)*3.3,curvedRoadZ(-199)+Math.sin(a)*3.3);}return points;}
  const dir=c.direction||1,road=c.laneRoad??Math.round((c.axis==='z'?c.g.position.x:c.g.position.z)/72)*72;
  let corners;
  if(c.axis==='z'){
   const other=road<=-100?-72:road>=100?72:road>=72?0:road+72;
   corners=dir>0?[[road,-131],[road,curvedRoadZ(road)],[other,curvedRoadZ(other)],[other,-131]]:
    [[road,curvedRoadZ(road)],[road,-131],[other,-131],[other,curvedRoadZ(other)]];
  }else{
   const other=road===72?0:road===-131?-72:road+72;
   corners=dir>0?[[-130,horizontalRoadZ(-130,road)],[130,horizontalRoadZ(130,road)],[130,horizontalRoadZ(130,other)],[-130,horizontalRoadZ(-130,other)]]:
    [[130,horizontalRoadZ(130,road)],[-130,horizontalRoadZ(-130,road)],[-130,horizontalRoadZ(-130,other)],[130,horizontalRoadZ(130,other)]];
  }
  // Offset each direction to its actual right-hand lane. Turn inside the
  // intersection; never snap ninety degrees into the opposite lane.
  const lane=3.3,radius=9,turns=corners.map((p,i)=>{
   const prev=corners[(i+3)%4],next=corners[(i+1)%4],a=[Math.sign(p[0]-prev[0]),Math.sign(p[1]-prev[1])],b=[Math.sign(next[0]-p[0]),Math.sign(next[1]-p[1])];
   if(Math.abs(p[0]-prev[0])>Math.abs(p[1]-prev[1]))a[1]=0;else a[0]=0;
   if(Math.abs(next[0]-p[0])>Math.abs(next[1]-p[1]))b[1]=0;else b[0]=0;
   return {entry:[p[0]-a[0]*radius+a[1]*lane,p[1]-a[1]*radius-a[0]*lane],exit:[p[0]+b[0]*radius+b[1]*lane,p[1]+b[1]*radius-b[0]*lane],control:[p[0]+(a[1]+b[1])*lane,p[1]-(a[0]+b[0])*lane],out:b};
  });
  for(let i=0;i<4;i++){
   const t=turns[i];add(...t.entry);
   for(let j=1;j<=12;j++){const u=j/12,v=1-u;add(v*v*t.entry[0]+2*v*u*t.control[0]+u*u*t.exit[0],v*v*t.entry[1]+2*v*u*t.control[1]+u*u*t.exit[1]);}
   const end=turns[(i+1)%4].entry;
   if(t.out[0]){const n=Math.ceil(Math.abs(end[0]-t.exit[0])/2.5),roadZ=corners[i][1];for(let j=1;j<=n;j++){const x=t.exit[0]+(end[0]-t.exit[0])*j/n,center=roadZ>95?curvedRoadZ(x):horizontalRoadZ(x,Math.round(roadZ/72)*72);add(x,(Math.abs(roadZ+131)<1?-131:center)-t.out[0]*lane);}}
   else add(...end);
  }
  return points;
 }

 const routeStops=c=>c.axis==='district'?(c.track.stops||[]).map(s=>{const p=districtTrackPosition(c.track,s.progress,c.homeLane||0);return {id:s.id,position:at(p.x,p.z),junction:at(s.junctionX,s.junctionZ)};}):[];
 const lampIds=(c,material)=>{const found=[];if(material)c.g.traverse(o=>{if(o.isMesh&&(Array.isArray(o.material)?o.material.includes(material):o.material===material))found.push(id(o));});return found;};
 const world={version:1,playerId:id(player),droneId:id(surveyDrone.root),spawn:v(player.position),playerRig:rig(player),
  residents:people.map(p=>({id:id(p.g),name:p.name||'Morador',line:p.line||'',lines:p.lines||[],role:p.role||'',activity:p.busStop?'wait':p.lifeActivity||'idle',initialDestination:p.busStop?'espera-'+p.busStop.id:typeof p.initialDestination==='string'?p.initialDestination:p.initialDestination?.id||'',routine:p.busStop?['wait','rest','wait']:p.routine||[],stationary:!!p.stationary&&!p.busStop,sitting:!!p.sitting,indoor:!!p.indoor,child:!!p.child,performance:!!p.performance,musicGuest:!!p.musicGuest,phase:p.phase||0,speed:p.speed||1.15,position:v(p.g.position),place:p.place?.name||'',rig:rig(p.g)})),
  animals:animals.map(a=>({id:id(a.g),head:id(a.head),body:id(a.body),legs:ids(a.legs),wings:(a.wings||[]).map(w=>id(w.group)),type:a.type,position:v(a.g.position),phase:a.phase})),
  vehicles:cars.map(c=>({id:id(c.g),driver:id(c.driver),rider:id(c.rider),steeringWheel:id(c.steeringWheel),kind:c.kind||'car',role:c.role||'',model:c.design?.name||c.design?.id||c.kind||'car',auto:!!c.auto,oneWay:!!(c.axis==='district'&&c.track?.oneWay),routeStops:routeStops(c),alternateRoute:c.axis==='district'?route({...c,homeLane:-(c.homeLane||0)}):[],width:c.width,length:c.length,mass:1350*(c.handling?.massScale||1),...c.handling,tire:c.design?.tire||.43,speed:c.velocity||0,direction:c.direction||1,axis:c.axis,road:c.laneRoad??Math.round((c.axis==='z'?c.g.position.x:c.g.position.z)/72)*72,centerOffset:c.centerOffset||0,seatMount:v(c.seatMount||c.driver?.position),wheelMount:v(c.steeringWheel?.position),doors:ids(c.carDoors?[-1,1].map(s=>c.carDoors[s]):c.doors),headlights:ids(c.spots),brakeLights:lampIds(c,c.brake),leftIndicators:lampIds(c,c.indicators?.left),rightIndicators:lampIds(c,c.indicators?.right),reverseLights:lampIds(c,c.reverse),driverHands:(c.driver?.userData.grips||[]).map(g=>id(g.hand)),driverArms:(c.driver?.userData.grips||[]).map(g=>id(g.arm)),riderLegs:(c.rider?.userData.unityLegs||[]).flatMap(l=>ids([l.thigh,l.shin,l.foot])),wheels:(c.wheelContacts||[]).map(w=>({pivot:id(w.pivot),spin:id(w.spin),position:{x:w.x,y:w.y,z:w.z},radius:w.radius,width:w.width,front:!!w.steer})),surfaces:c.surfaces||[],route:route(c),position:v(c.g.position),yaw:c.g.rotation.y})),
  boats:boats.map(b=>({id:id(b.g),wheel:id(b.wheel),name:b.name,model:b.model,width:b.w,length:b.d,top:b.top,speed:b.speed,yaw:b.heading,draft:b.draft,turn:b.turn,seat:{x:b.model==='sail'?0:.43,y:.24,z:b.model==='fishing'?.47:-.40},position:v(b.g.position)})),
  places:places.map(p=>({name:p.name,kind:p.kind,x:p.x,z:p.z,w:p.w,d:p.d,elevation:p.elevation||0,door:{id:id(p.door.hinge),x:p.door.x,z:p.door.z,width:p.door.width,automatic:!!p.door.automatic,position:v(p.door.hinge.position)}})),
  seats:benches.map(b=>({id:id(b.group),x:b.x,z:b.z,yaw:b.rotation,seatTop:b.seatTop,width:b.width,depth:b.depth,occupied:!!b.occupied,position:v(b.group.position)})),
  solids:nativeSolids,nav:sidewalkGraph.map(n=>({id:n.id,x:n.x,z:n.z,edges:n.edges.map(e=>({to:e.to.id,cost:e.cost,crossing:!!e.crossing}))})),
  locations:lifeLocations.map(l=>{const queue=people.find(p=>p.busStop&&l.id==='espera-'+p.busStop.id);return {id:l.id,activity:l.activity,role:l.role||'',x:queue?.g.position.x??l.x,z:queue?.g.position.z??l.z,yaw:l.rotation||0,spacing:l.slotSpacing||1.6,duration:Array.isArray(l.duration)?(l.duration[0]+l.duration[1])/2:l.duration||24,capacity:l.capacity||1}}),
  stops:busStops.map(s=>({...s,yaw:s.rotation||0})),roadRoute:busTrack.points.map(p=>at(p.x,p.z)),
  services:servicePoints.map((s,i)=>({id:'service-'+i,kind:s.kind,mode:s.mode||'',item:s.id||'',label:s.label||s.name||'',place:s.place?.name||'',position:at(s.x,s.z,1),anchor:s.anchor?{x:s.anchor.x,y:groundHeight(s.anchor.x,s.anchor.z)+(s.anchor.height||1),z:s.anchor.z}:at(s.x,s.z,2),price:0,node:0})),
  devices:devices.map((d,i)=>{const u=d.unit,o=u.g||u.hinge||u.group||u.head||u.handset;let p=u.antenna||u.pivot||o?.getWorldPosition(new THREE.Vector3())||at(u.x,u.z,1.6);if(u.hinge)p=p.clone().add(new THREE.Vector3(0,2.6,0));if(u.junction)p=p.clone().add(new THREE.Vector3(0,3.1,0));return {id:'device-'+i,type:u.type||(u.hinge?'door':u.g?'vehicle':u.junction?'signal':'fire-water'),label:d.label,node:id(o),target:u.junction?junctions.indexOf(u.junction):id(u.lens||o),position:v(p),lookAt:v(u.hint||p),yaw:u.homeYaw||0,pitch:u.homeTilt||0,range:32};}),
  destructibles:destructibles.map(d=>({id:id(d.group),solidIndex:solids.indexOf(d.collider),type:d.type,position:v(d.group.position)})),
  signals:junctions.map(j=>({x:j.x,z:j.z,phase:j.offset||0,lamps:(j.heads||[]).flatMap(h=>ids(h.lamps)),lampAxes:(j.heads||[]).flatMap(h=>h.lamps.map(()=>h.axis==='ns'?0:1)),lampColors:(j.heads||[]).flatMap(h=>h.lamps.map((_,i)=>i))})),docks:marine.docks.map(d=>({x:d.x,z:d.z,w:d.w,d:d.d,top:d.top,yaw:d.heading||0})),ladders:marine.ladders,
  music:{position:at(-26,19.2),musician:id(streetMusician.g),guitar:id(scene.getObjectByName('ipe-acoustic-guitar')),handNodes:ids(streetMusician.performancePose?.handNodes)},
  fish:{position:at(160,-120.6),worker:id(fishMarket.worker.g),beam:id(fish.beam),leftPan:id(fish.pans[0]),rightPan:id(fish.pans[1]),fish:ids(fish.load),weights:ids(fish.weights),actions:fish.targets.map(t=>at(t.x,t.z,1))},
  cinema:{screen:id(movie.screen),projector:at(movie.projector.x,movie.projector.z,2),spools:ids(movie.spools)},
  practice:{position:at(54,104),targets:practiceRange.exportData().targets.map(t=>id(t.root)),lamps:practiceRange.exportData().targets.map(t=>id(t.lamp))},
  streetLamps:streetLamps.map(l=>{const d=destructibles.find(d=>d.lamp===l),bulb=d?.group.children[2];return {node:id(d?.group),bulb:id(bulb),position:bulb?v(bulb.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(0,-.1,0))):v(l)};}),streetLightIds:ids(streetLights),shopLightIds:ids(shopLights),canopyLightId:id(canopyLight),
  harbor:{load:id(port.crate),mural:id(port.mural.mesh),sock:id(port.sock),platter:id(port.platter),cables:ids(port.cables),slats:[],loadPosition:v(port.crate.position)}
 };
 const ground={x:-214,z:-252,step:2,width:215,height:240,heights:[]};for(let j=0;j<ground.height;j++)for(let i=0;i<ground.width;i++)ground.heights.push(groundHeight(ground.x+i*ground.step,ground.z+j*ground.step));world.ground=ground;
 const dynamic=new Set([player,surveyDrone.root,...surveillance.devices.map(c=>c.head),...practiceRange.exportData().targets.flatMap(t=>[t.root,t.lamp]),...people.map(p=>p.g),...cars.map(c=>c.g),...boats.map(b=>b.g),...animals.map(a=>a.g),...places.map(p=>p.door.hinge),...destructibles.map(d=>d.group),port.crate,port.sock,port.platter,port.mural.mesh,...port.cables,fish.beam,...fish.pans,...movie.spools,movie.screen].filter(Boolean).map(id));
 const visual={nodes:[],geometries:[],materials:[],floors:[]},textures={},gm=new Map(),mm=new Map(),tm=new Map();
 function tex(t,name){if(!t?.image)return '';if(tm.has(t))return tm.get(t);const n=name||'texture-'+tm.size,im=t.image;let canvas=im;
  if(!im.toDataURL){canvas=document.createElement('canvas');canvas.width=im.width;canvas.height=im.height;const ctx=canvas.getContext('2d');if(im.data){const a=ctx.createImageData(im.width,im.height),channels=im.data.length/(im.width*im.height);for(let i=0;i<im.width*im.height;i++){a.data[i*4]=im.data[i*channels];a.data[i*4+1]=channels>1?im.data[i*channels+1]:im.data[i];a.data[i*4+2]=channels>2?im.data[i*channels+2]:im.data[i];a.data[i*4+3]=255;}ctx.putImageData(a,0,0);}else ctx.drawImage(im,0,0);}
  textures[n]=canvas.toDataURL('image/png');tm.set(t,n);return n;
 }
 function mat(m){if(mm.has(m))return mm.get(m);const i=visual.materials.length;mm.set(m,i);
  if(m.userData.citySurface){const shader={uniforms:{},vertexShader:'',fragmentShader:''};m.onBeforeCompile(shader);tex(shader.uniforms.cityAtlas?.value,'city-atlas');tex(shader.uniforms.cityCover?.value,'city-cover');}
  const sea=marineArt.exportMaterials(),e=m.emissive?.clone().multiplyScalar(m.emissiveIntensity??1);visual.materials.push({name:m.name||'material-'+i,texture:tex(m.map),color:color(m.color,m.opacity),emission:color(e||new THREE.Color(0)),roughness:m.roughness??.9,metalness:m.metalness||0,opacity:m.opacity??1,surface:typeof m.userData.citySurface==='number'?m.userData.citySurface:0,sea:m===sea.surfaceMaterial?1:m===sea.farMaterial?2:0,transparent:!!m.transparent,unlit:!!m.isMeshBasicMaterial,doubleSide:m.side===THREE.DoubleSide,repeat:m.map?{x:m.map.repeat.x,y:m.map.repeat.y}:{x:1,y:1},offset:m.map?{x:m.map.offset.x,y:m.map.offset.y}:{x:0,y:0}});return i;
 }
 function geom(g){if(gm.has(g))return gm.get(g);const i=visual.geometries.length;gm.set(g,i);const attr=name=>g.attributes[name]?Array.from(g.attributes[name].array):[];visual.geometries.push({name:g.name||'mesh-'+i,positions:attr('position'),normals:attr('normal'),uv:attr('uv'),colors:attr('color'),indices:g.index?Array.from(g.index.array):Array.from({length:g.attributes.position.count},(_,j)=>j),groups:g.groups});return i;}
 function node(o,parent,isDynamic){if(o.isPoints||o.isSprite||o.isCamera)return;const dyn=isDynamic||dynamic.has(id(o));let instances=[];
  if(o.isInstancedMesh)for(let i=0;i<o.count;i++){const matrix=new THREE.Matrix4(),c=new THREE.Color(1,1,1);o.getMatrixAt(i,matrix);if(o.instanceColor)o.getColorAt(i,c);instances.push({matrix:matrix.elements,color:color(c),surface:o.geometry.attributes.citySurface?.getX(i)||0});}
  visual.nodes.push({id:id(o),parent,name:o.name||o.type+'-'+id(o),kind:o.isLight?'light':o.isMesh?'mesh':'group',visible:o.visible,dynamic:dyn,castShadow:o.castShadow,geometry:o.isMesh?geom(o.geometry):-1,materials:o.isMesh?(Array.isArray(o.material)?o.material:[o.material]).map(mat):[],position:v(o.position),rotation:{x:o.quaternion.x,y:o.quaternion.y,z:o.quaternion.z,w:o.quaternion.w},scale:v(o.scale),instances,lightType:o.type,intensity:o.intensity||0,range:o.distance||0,lightColor:color(o.color)});
  for(const c of o.children)node(c,id(o),dyn);
 }
 for(const o of scene.children)node(o,-1,false);
 for(const s of renderedGround._surfaces()){
  let points;if(s.points)points=s.points;else{const p=(x,z)=>({x:s.x+s.c*x+s.s*z,z:s.z-s.s*x+s.c*z});points=[p(-s.w/2,-s.d/2),p(-s.w/2,s.d/2),p(s.w/2,s.d/2),p(s.w/2,-s.d/2)];}
  const verts=[];addGradedFace(verts,points.map(p=>({x:p.x,y:s.top,z:p.z})));visual.floors.push({vertices:verts});
 }
 for(let z=-252;z<226;z+=2)for(let x=-214;x<214;x+=2){const h=(a,b)=>b<shoreZ?seabed(a,b):localTerrainHeight(a,b)+cityElevation(b)-.05;visual.floors.push({vertices:[x,h(x,z),z,x,h(x,z+2),z+2,x+2,h(x+2,z+2),z+2,x,h(x,z),z,x+2,h(x+2,z+2),z+2,x+2,h(x+2,z),z]});}
 return {world,visual,textures};
};
