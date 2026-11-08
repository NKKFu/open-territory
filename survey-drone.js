import {cameraClearance} from './follow-camera.js';

const radius=.46,halfHeight=.19,signalRange=48;
const clamp=(n,a,b)=>Math.max(a,Math.min(b,n));
export function droneFits(point,boxes){
  return boxes.every(box=>{
    if(point.y+halfHeight<=box.bottom||point.y-halfHeight>=box.top)return true;
    const dx=point.x-box.x,dz=point.z-box.z,c=box.cos??1,s=box.sin??0;
    const x=Math.max(0,Math.abs(dx*c-dz*s)-box.w/2),z=Math.max(0,Math.abs(dx*s+dz*c)-box.d/2);
    return x*x+z*z>=radius*radius;
  });
}
export function createSurveyDrone({THREE:T,scene,part,groundHeight,obstacles}){
  const root=new T.Group();root.visible=false;scene.add(root);
  const shell=new T.Group();root.add(shell);
  const carbon='#34444b',cream='#dedec4',orange='#e8a351';
  part(shell,0,0,0,.28,.12,.38,carbon);
  part(shell,0,.070,-.012,.23,.045,.28,orange);
  part(shell,0,.081,-.15,.15,.028,.09,cream);
  part(shell,0,-.066,-.16,.14,.095,.11,'#5e7b79');
  part(shell,0,-.066,-.223,.071,.054,.022,'#263238');
  const lamp=new T.MeshBasicMaterial({color:'#81ffcc',toneMapped:false});
  const propellers=[],ring=new T.TorusGeometry(.147,.014,4,12);
  const guardMaterial=new T.MeshStandardMaterial({color:carbon,roughness:.68});
  for(const sx of [-1,1])for(const sz of [-1,1]){
    const x=sx*.243,z=sz*.222;
    const strut=part(shell,x*.61,.012,z*.61,.058,.052,.30,carbon);strut.rotation.y=Math.atan2(x,z);
    part(shell,x,.04,z,.072,.095,.072,'#5f7169');
    const guard=new T.Mesh(ring,guardMaterial);guard.rotation.x=Math.PI/2;guard.position.set(x,.096,z);shell.add(guard);
    const rotor=new T.Group();rotor.position.set(x,.097,z);shell.add(rotor);
    for(const side of [-1,1])part(rotor,side*.062,0,0,.126,.009,.061,side>0?cream:'#839c9a').rotation.y=side*.18;
    part(shell,x,-.05,z,.036,.075,.05,sz<0?lamp:orange);
    propellers.push(rotor);
  }
  let mode='stowed',yaw=0,pitch=.20,drive=0,timer=0,blockedTime=0,lensScale=1;
  const velocity=new T.Vector3(),candidate=new T.Vector3(),direction=new T.Vector3(),pivot=new T.Vector3(),look=new T.Vector3();
  const route=[],base=new T.Vector3();
  const position=root.position;
  function within(p){
    return Math.abs(p.x)<209&&p.z<219&&p.z>-249&&p.y>=Math.max(-.31,groundHeight(p.x,p.z))+.30&&p.y<=base.y+23;
  }
  function move(delta,boxes){
    const divisions=Math.max(1,Math.ceil(delta.length()/.18));
    for(let n=0;n<divisions;n++)for(const axis of ['x','z','y']){
      candidate.copy(position);candidate[axis]+=delta[axis]/divisions;
      if(within(candidate)&&droneFits(candidate,boxes))position.copy(candidate);
      else velocity[axis]=0;
    }
  }
  function launch(body,heading){
    if(mode!=='stowed')return false;
    base.copy(body);yaw=heading;pitch=.20;lensScale=1;
    const nearby=obstacles(body,3);
    for(const offset of [0,.66,-.66]){
      const x=body.x-Math.sin(yaw)*.66+Math.cos(yaw)*offset;
      const z=body.z-Math.cos(yaw)*.66-Math.sin(yaw)*offset;
      candidate.set(x,body.y+1.86,z);
      const hand=pivot.set(body.x,body.y+1.68,body.z);
      direction.copy(candidate).sub(hand);const length=direction.length();
      if(!within(candidate)||!droneFits(candidate,nearby)||cameraClearance(hand,direction.normalize(),length,nearby,.20)<length-.01)continue;
      position.copy(candidate);route.length=0;route.push(position.clone());
      mode='flying';root.visible=true;velocity.set(0,0,0);drive=0;blockedTime=0;return true;
    }
    return false;
  }
  function recall(){
    if(mode!=='flying')return false;
    mode='returning';velocity.set(0,0,0);blockedTime=0;drive=0;return true;
  }
  function stow(){mode='stowed';root.visible=false;velocity.set(0,0,0);route.length=0;}
  function update(dt,input,body,wind){
    if(mode==='stowed')return;
    timer+=dt;
    const boxes=obstacles(position,2.5);
    if(mode==='flying'){
      const speed=6.8,length=Math.max(1,Math.hypot(input.forward,input.sideways));
      const dx=(-Math.sin(yaw)*input.forward+Math.cos(yaw)*input.sideways)*speed/length;
      const dz=(-Math.cos(yaw)*input.forward-Math.sin(yaw)*input.sideways)*speed/length;
      // The flight controller holds a hover, but driven turns still lean into the coastal wind.
      const moving=input.forward||input.sideways||input.lift;
      const gust=moving?.065:0,rate=1-Math.exp(-dt*6.5);
      velocity.x+=(dx+(wind.windX||0)*gust-velocity.x)*rate;
      velocity.z+=(dz+(wind.windZ||0)*gust-velocity.z)*rate;
      velocity.y+=(input.lift*4.2-velocity.y)*rate;
      candidate.copy(position).addScaledVector(velocity,dt);
      if(candidate.distanceTo(base)>signalRange){
        direction.copy(candidate).sub(base).setLength(signalRange);
        velocity.copy(direction.add(base)).sub(position).multiplyScalar(1/Math.max(dt,.001));
      }
      move(direction.copy(velocity).multiplyScalar(dt),boxes);
      const latest=route.at(-1);
      if(latest.distanceToSquared(position)>.52**2){
        // Keep corners even when a short diagonal would cut through a door jamb.
        // Only simplify a clear, nearly straight segment of the return flight.
        if(route.length>1){
          const prev=route.at(-2);
          const a=direction.copy(latest).sub(prev).normalize();
          const b=pivot.copy(position).sub(latest).normalize();
          if(a.dot(b)>.998&&prev.distanceToSquared(position)<16)route.pop();
        }
        route.push(position.clone());
      }
      if(route.length>1200)recall();
    }else{
      const hand=pivot.set(body.x,body.y+1.67,body.z);
      const target=route.at(-1)||hand;
      direction.copy(target).sub(position);
      if(direction.length()<.23){if(route.length)route.pop();else{stow();return;}}
      else{
        const previous=position.distanceTo(target);
        move(direction.setLength(Math.min(previous,dt*11)),boxes);
        blockedTime=position.distanceTo(target)<previous-dt*.35?0:blockedTime+dt;
        // A closed moving door must never keep the player's controller captive.
        if(blockedTime>2.5||(!route.length&&position.distanceTo(hand)>signalRange)){stow();return;}
      }
    }
    drive+=(Math.hypot(velocity.x,velocity.z)-drive)*(1-Math.exp(-dt*5));
    shell.rotation.order='YXZ';shell.rotation.y=yaw;
    shell.rotation.x=(-velocity.z*Math.cos(yaw)-velocity.x*Math.sin(yaw))*.032+.012*Math.sin(timer*2.2);
    shell.rotation.z=-(velocity.x*Math.cos(yaw)-velocity.z*Math.sin(yaw))*.029-(wind.windSpeed||0)*.003;
    const weak=position.distanceToSquared(base)>(signalRange-6)**2;
    lamp.color.set(weak?'#ffc25c':mode==='returning'?'#91bcff':'#89ffd1');
    shell.position.y=.021*Math.sin(timer*4.8);
    propellers.forEach((rotor,i)=>rotor.rotation.y+=(i===0||i===3?1:-1)*dt*(37+drive*3));
  }
  function view(camera,baseFov){
    const fov=clamp(baseFov*lensScale,33,82);
    if(Math.abs(camera.fov-fov)>.01){camera.fov=fov;camera.updateProjectionMatrix();}
    pivot.copy(position);pivot.y+=.13;
    direction.set(Math.sin(yaw)*Math.cos(pitch),Math.sin(pitch),Math.cos(yaw)*Math.cos(pitch));
    const arm=cameraClearance(pivot,direction,2.45,obstacles(pivot,3.2),.14);
    camera.position.copy(pivot).addScaledVector(direction,Math.max(.035,arm));
    camera.position.y=Math.max(Math.max(-.31,groundHeight(camera.position.x,camera.position.z))+.16,camera.position.y);
    look.copy(pivot).addScaledVector(direction,-3.2);look.y+=.35;
    camera.lookAt(look);
    shell.visible=arm>.5;
  }
  return {root,position,launch,recall,update,view,
    turn(dx,dy){yaw+=dx;pitch=clamp(pitch+dy,-.66,.99);},
    zoom(delta){lensScale=clamp(lensScale*Math.exp(delta*.0012),.58,1);},
    get active(){return mode==='flying';},get mode(){return mode;},get yaw(){return yaw;},
    showBody(){shell.visible=true;},
    snapshot:()=>({mode,x:position.x,y:position.y,z:position.z,distance:mode==='stowed'?0:position.distanceTo(base),yaw,pitch,lensScale})};
}
