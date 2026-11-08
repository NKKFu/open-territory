// Pan/tilt fixtures in the city. The eyepoint is attached to the physical
// lens, so the bracket stays fixed while the live camera turns.
export function installPublicCameras({THREE:T,scene,part,solid,fixtures,obstacles,groundHeight}){
  const devices=[];
  function install(name,x,z,targetX,targetZ){
    const base=groundHeight(x,z),post=new T.Group();
    post.position.set(x,base,z);scene.add(post);fixtures.push(post);
    const metal='#576465',ivory='#deded0',dark='#293f44';
    const homeYaw=Math.atan2(targetX-x,targetZ-z),ox=Math.sin(homeYaw),oz=Math.cos(homeYaw);
    part(post,0,.12,0,.64,.24,.64,metal);
    part(post,0,2.55,0,.22,5.02,.22,metal);
    part(post,ox*.31,5.04,oz*.31,.18,.17,.82,metal).rotation.y=homeYaw;
    part(post,ox*.64,4.87,oz*.64,.18,.24,.18,dark);
    solid(x,z,.54,.54);
    const mast={x,z,w:.54,d:.54,bottom:base,top:base+5.17};obstacles.push(mast);
    const head=new T.Group();head.position.set(ox*.64,4.59,oz*.64);head.rotation.order='YXZ';post.add(head);
    part(head,0,0,0,.53,.39,.86,ivory);
    part(head,0,.23,.035,.65,.075,1.02,ivory);
    part(head,0,.01,.455,.43,.31,.08,dark);
    part(head,0,.01,.509,.23,.22,.06,'#486771');
    const glass=new T.MeshStandardMaterial({color:'#37555d',emissive:'#68b4c0',emissiveIntensity:.14});
    part(head,0,.012,.548,.14,.14,.035,glass);
    part(head,.17,-.085,.50,.044,.04,.018,'#dd886b');
    const lens=new T.Object3D();lens.position.set(0,.012,.67);head.add(lens);
    const pivot=new T.Vector3(x+ox*.64,base+4.59,z+oz*.64),hint=new T.Vector3(targetX,groundHeight(targetX,targetZ)+2.8,targetZ);
    const dx=hint.x-pivot.x,dz=hint.z-pivot.z;
    const homeTilt=Math.atan2(pivot.y-hint.y,Math.hypot(dx,dz));
    head.rotation.set(homeTilt,homeYaw,0,'YXZ');
    devices.push({type:'camera',name,pivot,head,lens,hint,homeYaw,homeTilt,mast});
  }
  install('Praça do Ipê',-48.2,62.6,-58.5,58.12);
  install('Hospital Baía',89,54,104,46.75);
  let active=null,pan=0,tilt=0;
  const direction=new T.Vector3(),target=new T.Vector3();
  function turn(dx,dy){
    pan=T.MathUtils.clamp(pan+dx,-1.48,1.48);
    tilt=T.MathUtils.clamp(tilt+dy,-.15,1.1);
  }
  return {
    devices,get active(){return active;},
    snapshot:()=>active?{name:active.name,pan:active.head.rotation.y-active.homeYaw,tilt:active.head.rotation.x}:null,
    connect(unit){
      active=unit;pan=0;tilt=unit.homeTilt;
      unit.head.rotation.set(tilt,unit.homeYaw,0,'YXZ');
    },
    disconnect(){active=null;},
    turn,
    view(camera,dt,horizontal,vertical){
      turn(horizontal*dt*1.05,vertical*dt*.75);
      active.head.rotation.y=T.MathUtils.damp(active.head.rotation.y,active.homeYaw+pan,14,dt);
      active.head.rotation.x=T.MathUtils.damp(active.head.rotation.x,tilt,14,dt);
      active.lens.getWorldPosition(camera.position);
      active.lens.getWorldDirection(direction);
      camera.lookAt(target.copy(camera.position).add(direction));
    }
  };
}
