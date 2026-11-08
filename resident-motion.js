// Visual pose only. Navigation owns the feet-centered root and collision body.
export function createResidentMotion({THREE:T,groundHeight=()=>0}){
  const clamp=T.MathUtils.clamp,damp=T.MathUtils.damp,states=new WeakMap();
  const down=new T.Vector3(0,-1,0),direction=new T.Vector3(),pole=new T.Vector3(),upper=new T.Vector3(),lower=new T.Vector3();
  const target=new T.Vector3(),local=new T.Vector3(),rotation=new T.Quaternion(),inverse=new T.Quaternion();
  const desiredRotation=new T.Quaternion(),euler=new T.Euler(),rootScale=new T.Vector3();
  const angle=a=>Math.atan2(Math.sin(a),Math.cos(a));
  function chain(parent,first,second,end,point,a,b,bend,weight=1){
    parent.updateWorldMatrix(true,false);direction.copy(point);parent.worldToLocal(direction).sub(first.position);
    const length=direction.length();if(length<.001)return;
    direction.divideScalar(length);const distance=clamp(length,.035,a+b-.00001);
    pole.copy(bend).addScaledVector(direction,-bend.dot(direction));
    if(pole.lengthSq()<.00001)pole.set(0,0,1).addScaledVector(direction,-direction.z);
    pole.normalize();const along=(a*a+distance*distance-b*b)/(2*distance);
    upper.copy(direction).multiplyScalar(along).addScaledVector(pole,Math.sqrt(Math.max(0,a*a-along*along)));
    lower.copy(direction).multiplyScalar(distance).sub(upper).normalize();
    rotation.setFromUnitVectors(down,upper.normalize());first.quaternion.slerp(rotation,weight);inverse.copy(rotation).invert();
    rotation.setFromUnitVectors(down,lower.applyQuaternion(inverse));second.quaternion.slerp(rotation,weight);
    end.updateWorldMatrix(true,false);
  }
  function orient(node,g,x=0,y=0,z=0){
    if(!node)return;
    g.getWorldQuaternion(desiredRotation);rotation.setFromEuler(euler.set(x,y,z));desiredRotation.multiply(rotation);
    node.parent.getWorldQuaternion(inverse).invert();node.quaternion.copy(inverse).multiply(desiredRotation);
  }
  function world(g,x,y,z){return g.localToWorld(target.set(x,y,z));}
  const armPole=[new T.Vector3(-1,-.3,-.38),new T.Vector3(1,-.3,-.38)],kneePole=new T.Vector3(0,0,1);
  function reach(g,index,point,weight=1){
    const d=g.userData,r=d.rig;
    if(!d.hands?.[index]||!d.elbows?.[index])return;
    chain(d.chest,d.limbs[2+index],d.elbows[index],d.hands[index],point,r.upperLength,r.lowerLength,armPole[index],weight);
  }
  function update(p,dt,time,{talking=false,external=false}={}){
    const g=p.g,d=g.userData,r=d.rig;if(!r||!d.hips||!d.chest)return;
    let s=states.get(p);
    if(!s){g.updateWorldMatrix(true,true);s={previous:g.position.clone(),yaw:g.rotation.y,phase:Math.PI*(.60+(p.phase%2>1?1:0)),speed:0,weight:0,turn:0,acceleration:0,
      hip:d.hips.position.clone(),chest:d.chest.position.clone(),feet:d.ankles.map(a=>({point:a.getWorldPosition(new T.Vector3()),anchor:a.getWorldPosition(new T.Vector3()),from:new T.Vector3(),stance:true})),mode:'idle'};states.set(p,s);}
    const distance=Math.hypot(g.position.x-s.previous.x,g.position.z-s.previous.z),elapsed=Math.max(.001,dt);
    const raw=distance<1?distance/elapsed:0,turn=angle(g.rotation.y-s.yaw)/elapsed,previousSpeed=s.speed;
    s.previous.copy(g.position);s.yaw=g.rotation.y;
    if(p.ragdoll||p.inBus||external||!g.visible){s.speed=0;s.weight=0;s.turn=0;s.reset=true;return;}
    const seated=!!p.sitting||r.seated,performing=p.performancePose?.active;
    s.speed=damp(s.speed,seated?0:Math.min(raw,4),raw>s.speed?13:20,dt);
    s.weight=damp(s.weight,raw>.035&&!seated?1:0,raw>.035?11:14,dt);
    s.turn=damp(s.turn,clamp(turn,-2.2,2.2)*s.weight,8,dt);
    s.acceleration=damp(s.acceleration,clamp((s.speed-previousSpeed)/elapsed/9,-1,1),8,dt);
    const scale=g.getWorldScale(rootScale).y,pace=clamp((s.speed-1.1)/1.8,0,1),stride=(1.16+.21*pace)*scale;
    if(raw>.035)s.phase=(s.phase+distance/stride*Math.PI*2)%(Math.PI*2);
    const routine=!talking?p.routinePose:null,ambient=!talking?p.ambientPose:null,kind=typeof ambient==='string'?ambient:ambient?.kind;
    talking||=routine?.kind==='chat';
    const beat=ambient?.beat??time,ambientWeight=clamp(ambient?.weight??1,0,1),t=time+(p.phase||0),wave=Math.sin(s.phase),idle=1-s.weight;
    const elevation=Number.isFinite(ambient?.lookY)?clamp(Math.atan2(ambient.lookY-(g.position.y+2.05*scale),Math.max(.3,Math.hypot(ambient.lookX-g.position.x,ambient.lookZ-g.position.z))),-.15,.60):0;
    let lean=.025+.055*s.weight+.055*s.acceleration,twist=.045*wave*s.weight-s.turn*.06,roll=-s.turn*.036;
    let headX=-.025,headY=s.turn*.13+idle*.055*Math.sin(t*.63),headZ=idle*.014*Math.sin(t*.89);
    if(routine?.kind==='read'||routine?.kind==='phone'){headX=.24;headY=0;lean=.045;}
    if(routine?.kind==='paint'){headY=.06;headX=.025;twist=.055*Math.sin(t*1.4);}
    if(talking){headX=.04*Math.sin(t*3);headY=.055*Math.sin(t*.9);twist=.025*Math.sin(t*1.5);}
    if(kind==='dance'){roll+=.045*Math.sin(beat*2.4);twist+=.065*Math.sin(beat*2.4);headZ+=.03*Math.sin(beat*2.4);}
    if(kind==='clap'||performing){headX+=.035*Math.sin((performing?p.performancePose.beat:beat)*2);}
    if(kind==='rainCover'){lean+=.10;headX=.11;}
    if(ambient&&Number.isFinite(ambient.lookX)&&Number.isFinite(ambient.lookZ))headY=clamp(angle(Math.atan2(ambient.lookX-g.position.x,ambient.lookZ-g.position.z)-g.rotation.y),-.75,.75);
    if(kind==='photo'||kind==='look')headX=-elevation*ambientWeight;
    d.chest.rotation.set(lean,twist,roll);d.head.rotation.set(headX,headY,headZ);
    d.chest.position.y=s.chest.y+.004*idle*Math.sin(t*1.8);
    s.mode=performing?'guitar':kind||routine?.kind|| (talking?'talk':s.weight>.08?'walk':'idle');p.motionPose=s.mode;
    if(!seated){
      let hipY=r.hipHeight-.014*s.weight;d.hips.position.x=s.hip.x+.010*idle*Math.sin(t*.77);
      g.updateWorldMatrix(true,false);
      for(let i=0;i<2;i++){
        const foot=s.feet[i],u=(s.phase/(Math.PI*2)+i*.5)%1,stance=u<.60,side=i?1:-1;
        if(distance>=1||s.reset){g.localToWorld(foot.anchor.set(side*.2,r.ankleHeight,0));foot.point.copy(foot.anchor);foot.from.copy(foot.point);foot.stance=true;}
        if(s.weight>.06&&raw>.035){
          if(stance){
            if(!foot.stance){foot.anchor.copy(foot.point);foot.anchor.y=groundHeight(foot.anchor.x,foot.anchor.z)+r.ankleHeight*scale;}
            foot.point.copy(foot.anchor);
          }else{
            if(foot.stance)foot.from.copy(foot.point);
            const swing=(u-.60)/.40,ease=swing*swing*(3-2*swing);
            g.localToWorld(target.set(side*.2,r.ankleHeight,(stride/scale)*.28));
            target.y=groundHeight(target.x,target.z)+r.ankleHeight*scale;
            foot.point.lerpVectors(foot.from,target,ease);foot.point.y+=Math.sin(swing*Math.PI)**2*(.11+.065*pace)*scale;
          }
          foot.stance=stance;
        }else{
          // A final small settling step returns the feet below the body after stopping.
          g.localToWorld(target.set(side*.2,r.ankleHeight,0));target.y=groundHeight(target.x,target.z)+r.ankleHeight*scale;
          foot.point.lerp(target,1-Math.exp(-dt*9));foot.anchor.copy(foot.point);foot.from.copy(foot.point);foot.stance=true;
        }
        local.copy(foot.point);g.worldToLocal(local);
        // Shorten the pelvis, never the leg, when a planted foot is behind a turn.
        const span=Math.hypot(local.x-side*.2,local.z),reach=r.thighLength+r.shinLength-.006;
        hipY=Math.min(hipY,local.y+Math.sqrt(Math.max(.20,reach*reach-Math.min(span,.52)**2)));
      }
      s.reset=false;d.hips.position.y=hipY;d.hips.rotation.set(0,.014*wave*s.weight,0);g.updateWorldMatrix(true,true);
      for(let i=0;i<2;i++){
        chain(d.hips,d.limbs[i],d.knees[i],d.ankles[i],s.feet[i].point,r.thighLength,r.shinLength,kneePole);
        orient(d.ankles[i],g,0,0,0);
      }
    }
    for(let i=0;i<2;i++){
      const side=i?1:-1,arm=d.limbs[2+i];
      arm.rotation.set(-.055+side*Math.cos(s.phase)*(.30+.13*pace)*s.weight,0,side*(.075+.015*idle*Math.sin(t)));
      d.elbows[i].rotation.set(-.12-.25*s.weight,0,0);d.hands[i].rotation.set(0,0,0);
      if(seated){arm.rotation.x=-.27;d.elbows[i].rotation.x=-.95;}
      if(talking){arm.rotation.x=i===0?-.32-.17*(.5+.5*Math.sin(t*2.3)):-.12;d.elbows[i].rotation.x=i===0?-.60-.25*Math.sin(t*2.3):-.24;arm.rotation.z=side*(i===0?.22:.08);}
    }
    g.updateWorldMatrix(true,true);
    const props=routine?.props;
    if(props){
      const activity=routine.kind;
      if(activity==='read'){
        props.book.updateWorldMatrix(true,false);
        for(let i=0;i<2;i++){props.book.localToWorld(target.set(i?.25:-.25,-.035,.06));reach(g,i,target);}
      }else if(activity==='phone'){
        reach(g,1,world(g,.29,1.37,.46));reach(g,0,world(g,-.16,1.25,.44));orient(props.phone,g,-.18);
      }else if(activity==='eat'||activity==='smoke'){
        const lift=Math.max(0,Math.sin(t*(activity==='eat'?.8:.65)))**3;
        reach(g,1,world(g,.20,1.22+lift*.63,.39-lift*.09));orient(activity==='eat'?props.cup:props.cigarette,g,activity==='eat'?-.16*lift:0);
      }else if(activity==='work'){
        props.broom.position.set(0,.025,.43+.06*Math.sin(t*1.7));props.broom.rotation.set(.015*Math.sin(t*1.7),0,0);
        props.broom.updateWorldMatrix(true,false);
        for(let i=0;i<2;i++){props.broom.localToWorld(target.set(0,i?1.56:1.35,0));reach(g,i,target);}
      }else if(activity==='paint'){
        reach(g,1,world(g,.33+.07*Math.sin(t*1.2),1.55+.14*Math.sin(t*1.9),.58));orient(props.brush,g,0,0,.09*Math.sin(t));
        reach(g,0,world(g,-.28,1.20,.38));orient(props.palette,g);
      }
    }
    if(kind==='photo'){
      reach(g,1,world(g,.12,1.80+elevation*.22,.51-elevation*.10),ambientWeight);reach(g,0,world(g,-.09,1.77+elevation*.22,.51-elevation*.10),ambientWeight);
    }else if(kind==='umbrella'){
      reach(g,1,world(g,.40,1.66,.24),ambientWeight);orient(d.hands[1],g);
    }else if(kind==='rainCover'){
      reach(g,0,world(g,.06,2.10,.20),ambientWeight);
    }else if(kind==='clap'){
      const gap=.10+.15*(.5+.5*Math.cos(beat*12));
      for(let i=0;i<2;i++)reach(g,i,world(g,(i?1:-1)*gap,1.42,.48),ambientWeight);
    }else if(kind==='dance'){
      for(let i=0;i<2;i++){d.limbs[2+i].rotation.set(-.50-.15*Math.sin(beat*2.4+i),0,(i?1:-1)*.22);d.elbows[i].rotation.x=-1.0;}
    }
    if(performing){
      const pose=p.performancePose;g.updateWorldMatrix(true,true);
      for(let i=0;i<2;i++){
        if(pose.handNodes?.[i]){pose.handNodes[i].getWorldPosition(target);reach(g,i,target);}
        else if(pose.hands?.[i])reach(g,i,pose.hands[i]);
      }
    }
    if(seated&&d.seatedCup){
      const lift=Math.max(0,Math.sin(t*.8))**3;reach(g,1,world(g,.22,s.hip.y+.52+lift*.54,.38-lift*.08));orient(d.seatedCup,g,-.15*lift);
    }
  }
  return {update,reach,snapshot:p=>{const s=states.get(p);return s?{mode:s.mode,speed:s.speed,phase:s.phase,planted:s.feet.map(f=>f.stance)}:null;}};
}
