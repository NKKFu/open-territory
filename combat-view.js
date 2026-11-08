// The barrel, sight, tracer and status label are all objects in the city.
export function createCombatView({THREE:T,part,scene,player,hand,camera}){
  const rig=new T.Group();scene.add(rig);
  const models={};
  for(const [id,length,color] of [['pulse',.37,'#448990'],['sidearm',.44,'#657783'],['carbine',.74,'#526960']]){
    const g=new T.Group();models[id]=g;rig.add(g);
    part(g,0,.12,length*.34,.14,.145,length,color);part(g,0,-.018,.015,.11,.23,.13,'#384248');
    if(id==='pulse'){for(const x of [-.052,.052])part(g,x,.12,length*.91,.031,.053,.08,'#6de6dc');}
    else{part(g,0,.12,length*.88,.083,.081,.13,'#26353b');part(g,0,.205,length*.65,.029,.033,.033,'#d5c695');
      part(g,0,.191,length*.32,.10,.018,length*.36,'#889991');}
    if(id==='carbine'){part(g,0,.018,-.22,.12,.12,.27,'#756951');part(g,0,-.06,.23,.09,.19,.14,'#344349');}
  }
  const flash=new T.Mesh(new T.OctahedronGeometry(1),new T.MeshBasicMaterial({color:0xffde91,transparent:true,depthWrite:false,toneMapped:false}));
  flash.scale.set(.11,.095,.21);flash.visible=false;rig.add(flash);
  const tip=id=>id==='carbine'?.73:id==='sidearm'?.455:.382;
  const pointer=new T.Mesh(new T.RingGeometry(.033,.051,20),new T.MeshBasicMaterial({color:0x85f4de,side:T.DoubleSide}));
  pointer.renderOrder=2;pointer.visible=false;scene.add(pointer);
  const pts=new Float32Array(6),geom=new T.BufferGeometry(),positions=new T.BufferAttribute(pts,3);
  geom.setAttribute('position',positions);const tracer=new T.Line(geom,new T.LineBasicMaterial({transparent:true,color:0xffdd91}));
  tracer.frustumCulled=false;tracer.visible=false;scene.add(tracer);
  const spark=new T.Mesh(new T.OctahedronGeometry(.115),new T.MeshBasicMaterial({color:0xffe289,transparent:true}));spark.visible=false;scene.add(spark);
  const paper=document.createElement('canvas');paper.width=640;paper.height=96;
  const ctx=paper.getContext('2d'),texture=new T.CanvasTexture(paper);texture.colorSpace=T.SRGBColorSpace;
  const note=new T.Sprite(new T.SpriteMaterial({map:texture,transparent:true,depthWrite:false}));note.visible=false;scene.add(note);
  const muzzle=new T.Vector3(),direction=new T.Vector3(),handPoint=new T.Vector3();
  let glow=0,life=0,flashLife=0,anchor=null,overShoulder=false;
  function notice(label,actor=player,seconds=1.6){
    const text=String(label);ctx.clearRect(0,0,640,96);ctx.font='bold 37px system-ui,sans-serif';
    const w=Math.min(624,Math.ceil(ctx.measureText(text).width)+34),x=(640-w)/2;
    ctx.fillStyle='#203839';ctx.fillRect(x,14,w,66);ctx.fillStyle='#81d9c9';ctx.fillRect(x,14,5,66);
    ctx.fillStyle='#fff7df';ctx.textBaseline='middle';ctx.textAlign='center';ctx.fillText(text,323,47,590);texture.needsUpdate=true;
    anchor=actor;life=seconds;note.material.depthTest=actor!==player;
  }
  function pose(id,aim,point){
    overShoulder=!!id&&aim;rig.visible=!!id&&player.g.visible;if(!rig.visible)return;
    for(const [key,m] of Object.entries(models))m.visible=id===key;
    hand.getWorldPosition(rig.position);
    if(aim&&point)direction.copy(point);
    else direction.copy(rig.position).add(handPoint.set(Math.sin(player.g.rotation.y)*.43,-.87,Math.cos(player.g.rotation.y)*.43));
    rig.lookAt(direction);rig.updateMatrixWorld();
  }
  function barrel(id,out){return out.copy(rig.localToWorld(muzzle.set(0,.12,tip(id))));}
  function burst(start,end,id,impact,fromHand=false){
    const color=id==='pulse'?0x68e9e5:0xffce81;tracer.material.color.setHex(color);spark.material.color.setHex(color);
    if(fromHand){flashLife=.085;flash.position.set(0,.12,tip(id)+.11);flash.material.color.setHex(color);flash.visible=true;}
    positions.setXYZ(0,start.x,start.y,start.z);positions.setXYZ(1,end.x,end.y,end.z);positions.needsUpdate=true;
    spark.position.copy(end);spark.visible=impact;tracer.visible=true;glow=.105;
  }
  function sight(hit,active){
    pointer.visible=active&&!!hit&&hit.kind!=='air';if(!pointer.visible)return;
    pointer.position.copy(hit.point);direction.copy(camera.position).sub(pointer.position).normalize();pointer.position.addScaledVector(direction,.045);
    pointer.quaternion.copy(camera.quaternion);pointer.scale.setScalar(Math.max(.8,camera.position.distanceTo(pointer.position)*.058));
    pointer.material.color.setHex(hit.actor?0xffc785:0x85f4de);
  }
  function update(dt){
    glow=Math.max(0,glow-dt);tracer.visible=glow>0;tracer.material.opacity=glow/.105;
    flashLife=Math.max(0,flashLife-dt);flash.visible=flashLife>0;flash.material.opacity=Math.min(1,flashLife*18);
    if(glow){spark.material.opacity=Math.min(1,glow*16);spark.scale.setScalar(.6+glow*6);}else spark.visible=false;
    life=Math.max(0,life-dt);note.visible=!!anchor&&life>0;
    if(note.visible){note.position.copy(anchor.g.position);
      const beside=anchor===player&&overShoulder&&!anchor.ragdoll;
      note.position.y+=anchor.ragdoll?1.05:(beside?1.14:2.64)*(anchor.g.scale.y||1);
      if(beside)note.position.addScaledVector(handPoint.setFromMatrixColumn(camera.matrixWorld,0),-.48);
      const size=2*Math.tan(camera.fov*Math.PI/360)*camera.position.distanceTo(note.position)/innerHeight*Math.min(420,innerWidth*.86);
      note.scale.set(size,size*.15,1);note.material.opacity=Math.min(1,life*4);}
  }
  return {pose,barrel,burst,sight,notice,update};
}
