// A small, original street set. Time follows the simulation, including pauses.
export function createStreetSet(){
  let elapsed=0,playing=false,encores=0,lastClap=-10,nextBeat=0,phrase=0;
  return {
    update(dt,{interrupted=false,rain=0}={}){
      const next=!interrupted&&rain<.62;
      if(next!==playing){playing=next;nextBeat=Math.floor(elapsed/.27)+1;}
      if(playing)elapsed+=dt;
      const beat=Math.floor(elapsed/.27),strum=playing&&beat>=nextBeat;
      if(strum)nextBeat=beat+1;
      return {playing,elapsed,beat,strum,phrase};
    },
    applaud(time){
      if(!playing||time-lastClap<3.2)return false;
      lastClap=time;encores++;phrase=(phrase+1)%3;return true;
    },
    snapshot:()=>({playing,elapsed,encores,phrase})
  };
}

export function installStreetPerformance({THREE:T,scene,part,block,solid,sign,groundHeight,renderedGround,cameraObstacles,servicePoints}){
  const x=-26,z=19.2,set=createStreetSet(),source={id:'roda-ipe',x,z,active:true,beat:0};
  const ink='#314c52',wood='#bd824b',trim='#e1b56d';
  // The mat stays flush with the lawn and keeps the plaza's crossing clear.
  block(x,.083,z+.5,5.2,.016,4.0,'#657c75');
  renderedGround.addRectangle({x,z:z+.5,w:5.2,d:4,top:.091});
  for(const side of [-1,1])block(x+side*2.39,.092,z+.5,.13,.004,3.82,'#e1b56d');
  function fixture(px,py,pz,w,h,d,color){
    block(px,py,pz,w,h,d,color);
    const s=solid(px,pz,w,d);Object.assign(s,{bottom:py-h/2,supportTop:py+h/2,opaque:false});
    cameraObstacles.push({x:px,z:pz,w,d,bottom:s.bottom,top:s.supportTop});
  }
  fixture(x+1.48,.51,z,.69,.84,.53,ink);
  block(x+1.48,.52,z+.275,.52,.61,.024,'#1f363e');
  for(const yy of [.34,.69])block(x+1.48,yy,z+.293,.28,.21,.015,'#51675e');
  fixture(x-1.58,.22,z+.26,.74,.26,1.4,'#754e42');
  block(x-1.58,.355,z+.26,.61,.025,1.22,'#c6a080');
  block(x-1.58,.377,z+.26,.49,.025,.99,'#385a5d');
  fixture(x+1.4,.58,z+1.23,.08,.95,.08,ink);
  fixture(x+1.4,.105,z+1.23,.64,.07,.49,ink);
  sign('RODA DO IPÊ',x+1.4,1.03,z+1.28,1.24,{background:'#27545a',ink:'#f1cf8b'});
  const point={kind:'street-music',x,z:z+2.6,anchor:{x,z:z+.8,height:2.62},display:{x,y:3.0,z:z+.8}};
  servicePoints.push(point);
  // The projecting neck has a shallow body-height volume, independent of the lawn.
  const instrumentSolid=solid(x+.41,z+.43,1.12,.31);
  Object.assign(instrumentSolid,{bottom:1.08,supportTop:2.16,opaque:false});
  let actor=null,guitar=null,pose=null,state={playing:true,elapsed:0,beat:0,phrase:0};
  const strumPoint=new T.Vector3(),fretPoint=new T.Vector3();
  function attach(person){
    actor=person;actor.performance=true;actor.counterWorker=true;actor.chatting=false;
    guitar=new T.Group();guitar.name='ipe-acoustic-guitar';actor.g.add(guitar);
    guitar.position.set(-.08,1.35,.30);guitar.rotation.z=-.96;
    const strap=part(actor.g,-.27,1.52,.26,.075,.50,.035,'#435250');strap.rotation.z=.90;
    if(actor.g.userData.chest){actor.g.updateWorldMatrix(true,true);actor.g.userData.chest.attach(strap);}
    // Twelve straight edges form the waist, shoulders and lower bout.
    const outline=[[-.13,.38],[-.28,.28],[-.27,.10],[-.16,.02],[-.31,-.14],[-.30,-.36],
      [-.15,-.45],[.15,-.45],[.30,-.36],[.31,-.14],[.16,.02],[.27,.10],[.28,.28],[.13,.38]];
    const positions=[];
    for(let i=0;i<outline.length;i++){
      const a=outline[i],b=outline[(i+1)%outline.length];
      positions.push(0,0,.09,a[0],a[1],.09,b[0],b[1],.09,
        0,0,-.09,b[0],b[1],-.09,a[0],a[1],-.09,
        a[0],a[1],-.09,b[0],b[1],-.09,a[0],a[1],.09,
        b[0],b[1],-.09,b[0],b[1],.09,a[0],a[1],.09);
    }
    const shape=new T.BufferGeometry();shape.setAttribute('position',new T.Float32BufferAttribute(positions,3));shape.computeVertexNormals();
    const body=new T.Mesh(shape,new T.MeshStandardMaterial({color:wood,roughness:.91,flatShading:true,side:T.DoubleSide}));body.castShadow=true;guitar.add(body);
    part(guitar,0,.68,0,.115,.76,.10,'#725241');
    part(guitar,0,.70,.059,.10,.75,.02,ink);
    part(guitar,0,1.13,0,.18,.25,.09,wood);
    part(guitar,0,-.23,.105,.24,.055,.025,ink);
    const hole=new T.Mesh(new T.CylinderGeometry(.105,.105,.012,8),new T.MeshStandardMaterial({color:'#263b3d',roughness:1}));
    hole.rotation.x=Math.PI/2;hole.position.set(0,.07,.098);guitar.add(hole);
    for(const side of [-1,1])for(let i=0;i<3;i++)part(guitar,side*.12,1.05+i*.075,0,.08,.03,.05,trim);
    for(let i=0;i<4;i++)part(guitar,(i-1.5)*.018,.43,.115,.004,1.31,.004,'#e8ce9b');
    for(let i=0;i<6;i++)part(guitar,0,.37+i*.10,.08,.11,.012,.008,trim);
    const handNodes=[new T.Object3D(),new T.Object3D()];handNodes.forEach(node=>guitar.add(node));
    pose={kind:'guitar',hands:[strumPoint,fretPoint],handNodes,beat:0,active:true};actor.performancePose=pose;
    if(actor.g.userData.chest){
      actor.g.updateWorldMatrix(true,true);actor.g.userData.chest.attach(guitar);
    }
  }
  function update(dt,time,{rain=0,interrupted=false,play=()=>{}}={}){
    state=set.update(dt,{rain,interrupted});source.active=state.playing;source.beat=state.elapsed/.27;
    if(!actor)return;
    guitar.visible=!actor.ragdoll&&!actor.inBus;
    instrumentSolid.active=guitar.visible&&Math.hypot(actor.g.position.x-x,actor.g.position.z-z)<.25;
    pose.active=state.playing;pose.beat=source.beat;
    if(guitar.visible){
      guitar.rotation.y=Math.sin(state.elapsed*2.2)*.018;
      pose.handNodes[0].position.set(Math.sin(source.beat*Math.PI*2)*.115,-.015,.15);
      pose.handNodes[1].position.set(.04,.63+(state.beat%16>7?.055:0),.025);
      guitar.updateWorldMatrix(true,false);
      pose.handNodes[0].getWorldPosition(strumPoint);pose.handNodes[1].getWorldPosition(fretPoint);
    }
    if(state.strum)play('busker-'+state.phrase+'-'+state.beat,x,z);
  }
  return {source,point,attach,update,get active(){return state.playing;},
    applaud(time,play){if(!set.applaud(time))return false;play('applause',x,z+2);return true;},
    snapshot:()=>({...set.snapshot(),x,z,hands:pose?.hands.map(v=>({x:v.x,y:v.y,z:v.z}))})};
}
