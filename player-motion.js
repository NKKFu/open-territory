import {dressCharacterRig} from './character-assets.js';

// The model moves inside a feet-centered root. Bending or turning the torso
// never changes the capsule, camera target or the surface under the player.
export function createPlayerMotion({THREE:T, part, shirt, skin, trousers, hair}) {
  const clamp = T.MathUtils.clamp, mix = T.MathUtils.lerp, smooth = T.MathUtils.smoothstep;
  const root = new T.Group();
  function joint(parent, x, y, z) {
    const pivot = new T.Group();
    pivot.position.set(x,y,z); parent.add(pivot);
    return pivot;
  }
  const hips = joint(root,0,.95,0), chest = joint(hips,0,.04,0);
  const head = joint(chest,0,.84,0);
  const arms = [], legs = [];
  const thighLength = .45, shinLength = .44, upperLength = .40, lowerLength = .43;
  for (const side of [-1,1]) {
    const hip = joint(hips,side*.185,0,0), knee = joint(hip,0,-thighLength,0);
    const shoe = joint(knee,0,-shinLength,0);
    legs.push({hip,knee,shoe});
    const shoulder = joint(chest,side*.47,.68,0), elbow = joint(shoulder,0,-upperLength,0);
    const hand = joint(elbow,0,-lowerLength,0);
    arms.push({shoulder,elbow,hand,side,previousUpper:new T.Quaternion(),previousLower:new T.Quaternion()});
  }
  dressCharacterRig({root,hips,chest,head,arms,legs,measurements:{thighLength,shinLength,upperLength,lowerLength}},
    {part,shirt,skin,trousers,hair,style:0,player:true});
  const phone = joint(arms[1].hand,-.045,-.065,.06);
  part(phone,0,0,0,.19,.31,.047,'#26383c');
  const display = new T.MeshBasicMaterial({color:'#80e7c8'});
  part(phone,0,.006,-.026,.15,.242,.012,display);
  phone.visible=false;
  Object.assign(root.userData,{shirt,skin,trousers,hair,hips,chest,head,limbs:[...legs.map(l=>l.hip),...arms.map(a=>a.shoulder)],
    knees:legs.map(l=>l.knee),elbows:arms.map(a=>a.elbow),hands:arms.map(a=>a.hand),shoes:legs.map(l=>l.shoe)});

  const down = new T.Vector3(0,-1,0), direction = new T.Vector3(), pole = new T.Vector3();
  const lap = new T.Vector3(), gripPoint=new T.Vector3(), barrelAxis=new T.Vector3(), supportPoint=new T.Vector3(), across=new T.Vector3();
  const upper = new T.Vector3(), lower = new T.Vector3(), rotation = new T.Quaternion(), inverse = new T.Quaternion();
  // Two articulated links put the palm on an actual point on the animal or ledge.
  function reach(arm, worldPoint, weight) {
    direction.copy(worldPoint);
    chest.worldToLocal(direction).sub(arm.shoulder.position);
    const length = direction.length();
    if (length < .001) return;
    direction.divideScalar(length);
    const distance = clamp(length, .06, upperLength+lowerLength-.0001);
    pole.set(arm.side,-.35,-.45).addScaledVector(direction,-(arm.side*direction.x-.35*direction.y-.45*direction.z));
    if (pole.lengthSq()<.0001) pole.set(0,1,0).addScaledVector(direction,-direction.y);
    pole.normalize();
    const along = (upperLength**2 + distance**2 - lowerLength**2)/(2*distance);
    upper.copy(direction).multiplyScalar(along).addScaledVector(pole,Math.sqrt(Math.max(0,upperLength**2-along**2)));
    lower.copy(direction).multiplyScalar(distance).sub(upper).normalize();
    rotation.setFromUnitVectors(down,upper.normalize());
    inverse.copy(rotation).invert();
    arm.shoulder.quaternion.slerp(rotation,weight);
    rotation.setFromUnitVectors(down,lower.applyQuaternion(inverse));
    arm.elbow.quaternion.slerp(rotation,weight);
  }
  function plantFoot(leg, z, ankleY, pitch) {
    const y = hips.position.y-ankleY, reach = clamp(Math.hypot(y,z),.13,thighLength+shinLength-.0001);
    const knee = Math.acos(clamp((reach**2-thighLength**2-shinLength**2)/(2*thighLength*shinLength),-1,1));
    const hip = -Math.atan2(z,y)-Math.atan2(shinLength*Math.sin(knee),thighLength+shinLength*Math.cos(knee));
    leg.hip.rotation.x=hip; leg.knee.rotation.x=knee; leg.shoe.rotation.x=pitch-hip-knee;
  }
  const feet=[{z:0,y:.125,pitch:0},{z:0,y:.125,pitch:0}];
  let landTime=1, impact=0, gait=0, phoneLift=0, sightLift=0, clapLift=0, mode='idle';
  let previousHeading=root.rotation.y, previousSpeed=0, turnLean=0, accelerationLean=0;
  function land(speed) {
    landTime=0; impact=clamp(speed/9,.28,1);
  }
  function update(dt,{time,phase,speed,airborne,verticalSpeed,vault,pet,petPoint,scan,boarding,weapon,aimed,aimPoint,reloading,recoil=0,swim=null,helm=false,seatHeight=null,seatBlend=1,clap=false}) {
    const turn=Math.atan2(Math.sin(root.rotation.y-previousHeading),Math.cos(root.rotation.y-previousHeading))/Math.max(dt,.001);
    const acceleration=(speed-previousSpeed)/Math.max(dt,.001);previousHeading=root.rotation.y;previousSpeed=speed;
    const free=!swim&&!helm&&!airborne&&!vault&&!pet&&!scan&&!boarding&&!weapon&&seatHeight===null;
    turnLean=T.MathUtils.damp(turnLean,free?clamp(turn,-2.8,2.8)*smooth(speed,.15,2.5):0,8,dt);
    accelerationLean=T.MathUtils.damp(accelerationLean,free?clamp(acceleration/14,-1,1):0,9,dt);
    clapLift=T.MathUtils.damp(clapLift,free&&clap&&speed<.18?1:0,13,dt);
    if(swim||helm){
      const effort=swim?.effort||0,t=swim?.stroke||time,climbing=swim?.climbing;
      mode=helm?'pilot':climbing?'ladder':effort>.25?'swim':'float';phone.visible=false;phoneLift=0;sightLift=0;
      hips.position.set(0,helm?.61:.94,0);hips.rotation.set(helm?.05:climbing?.10:.16+effort*.89,0,helm?0:Math.sin(t)*effort*.09);
      chest.rotation.set(-.025,helm?0:Math.sin(t)*effort*.12,0);head.rotation.set(helm?-.03:-.09-effort*.57,helm?0:Math.sin(t)*effort*.27,0);
      for(let i=0;i<2;i++){
        const side=i?1:-1,s=Math.sin(t+i*Math.PI),c=Math.cos(t+i*Math.PI),arm=arms[i],leg=legs[i];
        leg.hip.rotation.set(helm?-1.22:climbing?-.60-.40*s:.13+s*(.23+effort*.22),0,side*(helm?.045:.09));
        leg.knee.rotation.x=helm?1.32:climbing?.91-.36*s:.34+Math.max(0,-s)*.47;
        leg.shoe.rotation.x=helm?-.1:-.22;
        const shoulder=helm?-1.04:climbing?-1.72+.57*s:mix(-.70+.23*c,-1.45+1.60*c,effort);
        arm.shoulder.rotation.set(shoulder,side*(helm?.08:.15*s),side*(helm?.12:climbing?.20:.83-.40*effort+.12*s));
        arm.elbow.rotation.set(helm?-.84:climbing?-.90+.35*c:-.58-.44*Math.max(0,s),0,0);
      }
      if(helm?.hands){root.updateWorldMatrix(true,true);for(let i=0;i<2;i++)reach(arms[i],helm.hands[i],1);}
      return;
    }
    hips.rotation.x=T.MathUtils.damp(hips.rotation.x,0,14,dt);hips.rotation.z=T.MathUtils.damp(hips.rotation.z,0,14,dt);
    landTime+=dt;
    const landing=landTime<.34 ? Math.sin(Math.PI*landTime/.34)**.8*impact : 0;
    const run=smooth(speed,4.8,8.1);
    gait=T.MathUtils.damp(gait,clamp(speed/1.2,0,1),18,dt);
    const wave=Math.sin(phase), stance=mix(.59,.43,run), stride=mix(1.83,2.56,run);
    mode=airborne?(verticalSpeed>0?'jump':'fall'):landing>.12?'land':speed>6?'run':speed>.18?'walk':'idle';
    let hipY=.989-.07*gait-.065*run-.23*landing+.009*(1-gait)*Math.sin(time*1.8);
    let lean=.04+.07*gait+.17*run+.32*landing+.075*accelerationLean;
    let roll=.025*wave*gait-turnLean*.045, twist=.085*wave*gait-turnLean*.048;
    const resting=free?(1-gait)*(1-clapLift):0;
    let nod=-.04-.13*run, headYaw=turnLean*.14+resting*.065*Math.sin(time*.61);
    const petWeight=pet ? smooth(pet.elapsed,0,.40)*(1-smooth(pet.elapsed,1.88,2.45)) : 0;
    const progress=vault ? clamp(vault.elapsed/vault.duration,0,1) : 0;
    const plant=vault ? smooth(progress,0,.25)*(1-smooth(progress,.69,1)) : 0;
    const sweep=smooth(progress,.19,.76);
    const cabin=boarding ? Math.sin(Math.PI*boarding.progress) : 0;
    for (let i=0;i<2;i++) {
      const side=i===0?-1:1, u=(phase/(Math.PI*2)+i*.5)%1, foot=feet[i];
      const swing=clamp((u-stance)/(1-stance),0,1);
      foot.z=gait*(u<stance ? stride*(stance*.5-u) : stride*stance*(smooth(swing,0,1)-.5));
      foot.y=.125+gait*mix(.19,.38,run)*Math.sin(swing*Math.PI)**2;
      foot.pitch=gait*.24*Math.sin(swing*2*Math.PI);
      if (airborne) {
        const tuck=1-clamp(Math.abs(verticalSpeed)/4.4,0,1);
        foot.z=side<0?.18+.08*tuck:-.16;
        foot.y=.14+(side<0?.18:.09)*tuck;
        foot.pitch=.13;
      }
      if (vault) {
        // Lead knee clears first; the trailing foot follows the hips over the lip.
        const tuck=Math.sin(Math.PI*clamp((progress-(side<0?.02:.16))/.74,0,1))**2;
        foot.z=mix(foot.z,side<0?mix(.29,.55,sweep):mix(-.43,.12,sweep),plant);
        foot.y=.125+(side<0?.32:.28)*tuck;
        foot.pitch=(.12+.28*tuck)*plant;
      }
      if (petWeight) {
        foot.z=mix(foot.z,side<0?.24:-.30,petWeight);
        foot.y=mix(foot.y,.125,petWeight);
        foot.pitch=side>0?.31*petWeight:0;
      }
      // Keep the supporting sole on the ground throughout the stride.
      hipY=Math.min(hipY,foot.y+Math.sqrt(Math.max(.04,(thighLength+shinLength-.006)**2-foot.z**2)));
    }
    if (airborne) {lean=.16+.12*clamp(-verticalSpeed/10,0,1);twist=.09;}
    if (vault) {
      mode=vault.climb?'climb':vault.car?'hood-vault':'wall-vault';
      hipY=mix(hipY,.36+.13*sweep,plant);lean=mix(lean,(vault.climb?1.04:1.24)-.25*sweep,plant);
      roll=mix(roll,vault.climb?0:-.52,plant);twist=(.28-.48*sweep)*plant;nod=-.40*plant;
    } else if (pet) {
      mode='pet';const bird=pet.animal.type==='pigeon';
      hipY=mix(hipY,bird?.38:pet.animal.type==='cat'?.48:.58,petWeight);
      lean=mix(lean,bird?.80:.43,petWeight);
      twist=-.15*petWeight;roll=-.11*petWeight;nod=.26*petWeight;
    } else if (seatHeight!==null) {
      mode='sit';
      const seatedHip=seatHeight+.10;
      hipY=mix(hipY,seatedHip,seatBlend);roll*=1-seatBlend;twist*=1-seatBlend;
      lean=mix(lean,-.065+.012*Math.sin(time*1.55),seatBlend);nod=mix(nod,.045,seatBlend);
      for(const foot of feet){
        foot.y=mix(foot.y,.125,seatBlend);foot.pitch*=1-seatBlend;
        const forward=Math.min(.51,Math.sqrt(Math.max(.04,(thighLength+shinLength-.01)**2-(seatedHip-.125)**2)));
        foot.z=mix(foot.z,forward,seatBlend);
      }
    } else if (boarding) {
      mode='car-door';hipY-=.23*cabin;lean+=.40*cabin;roll-=boarding.side*.25*cabin;
    } else if (scan) {
      mode='scan';nod=.35;headYaw=.13;
    } else if(weapon){
      if(aimed||reloading){mode=reloading?'reload':'aim';twist=-.12;nod=.07;lean+=.065;}
    }
    hips.position.y=T.MathUtils.damp(hips.position.y,hipY,26,dt);
    const blend=1-Math.exp(-dt*24);
    hips.position.x=mix(hips.position.x,vault&&!vault.climb?-.13*plant:resting*.014*Math.sin(time*.77),blend);
    if(clapLift>.01&&free&&speed<.18){mode='clap';nod-=clapLift*(.035+.028*Math.sin(time*4));twist*=1-clapLift;}
    chest.rotation.set(mix(chest.rotation.x,lean,blend),mix(chest.rotation.y,twist,blend),mix(chest.rotation.z,roll,blend));
    head.rotation.set(mix(head.rotation.x,nod,blend),mix(head.rotation.y,headYaw-twist*.42,blend),mix(head.rotation.z,-roll*.34,blend));
    phoneLift=T.MathUtils.damp(phoneLift,scan?1:0,11,dt);
    phone.visible=phoneLift>.16 && !vault && !pet && !boarding;
    for (let i=0;i<2;i++) {
      const side=i===0?-1:1, foot=feet[i], arm=arms[i];
      plantFoot(legs[i],foot.z,foot.y,foot.pitch);
      legs[i].hip.rotation.z=vault&&!vault.climb?-(i===0?.75:.46)*plant:0;
      let shoulder=.04-side*Math.cos(phase)*gait*mix(.44,.76,run), elbow=-.13-.19*gait-.80*run;
      let spread=side*(.08+.06*gait);
      if(seatHeight!==null){shoulder=mix(shoulder,-.30,seatBlend);elbow=mix(elbow,-1.04,seatBlend);spread=mix(spread,side*.045,seatBlend);}
      if (airborne) {shoulder=-.60+side*.24;elbow=-.75;spread=side*.37;}
      if (vault) {shoulder=mix(shoulder,i===0?-.83:.35,plant);elbow=mix(elbow,-.35,plant);spread=side*(.12+.68*plant);}
      if (petWeight) {shoulder=mix(shoulder,-.50,petWeight);elbow=mix(elbow,-.55,petWeight);}
      if (boarding) {shoulder-=.87*cabin;elbow-=.45*cabin;}
      if(weapon&&!vault&&!pet&&!boarding&&seatHeight===null&&!scan){
        if(i===0){shoulder=reloading?-.68:aimed?-1.28+recoil*.14:-.36;elbow=reloading?-1.42:aimed?-.38:-.46;spread=-.04;}
        else if(aimed||reloading){shoulder=-1.02;elbow=-.74;spread=.23;}
      }
      if (phone.visible && i===1) {
        shoulder=mix(shoulder,-.64,phoneLift);elbow=mix(elbow,-1.65,phoneLift);spread=mix(spread,-.18,phoneLift);
      }
      arm.previousUpper.copy(arm.shoulder.quaternion);arm.previousLower.copy(arm.elbow.quaternion);
      arm.shoulder.rotation.set(shoulder,0,spread);arm.elbow.rotation.set(elbow,0,0);
      arm.shoulder.quaternion.slerp(arm.previousUpper,1-blend);arm.elbow.quaternion.slerp(arm.previousLower,1-blend);
    }
    sightLift=T.MathUtils.damp(sightLift,weapon&&aimed&&!reloading&&aimPoint?1:0,20,dt);
    if(weapon&&aimed&&!reloading&&aimPoint&&sightLift>.01){
      // The right hand is on the camera side. The second palm follows the
      // actual barrel direction instead of floating in front of the other arm.
      const rifle=weapon==='carbine';
      arms[0].shoulder.getWorldPosition(gripPoint);
      barrelAxis.copy(aimPoint).sub(gripPoint).normalize();
      across.set(Math.cos(root.rotation.y),0,-Math.sin(root.rotation.y));
      gripPoint.addScaledVector(barrelAxis,(rifle?.43:.57)-.09*recoil).addScaledVector(across,rifle?.28:.095);
      gripPoint.y+=.07+.035*recoil;reach(arms[0],gripPoint,sightLift);
      arms[0].hand.getWorldPosition(supportPoint);
      supportPoint.addScaledVector(barrelAxis,rifle?.22:.035).addScaledVector(across,.08);supportPoint.y-=.035;
      reach(arms[1],supportPoint,sightLift);
    }else if (vault && plant) {
      reach(arms[1],vault.hands[1],plant);
      if (vault.climb) reach(arms[0],vault.hands[0],plant);
    } else if (petPoint && petWeight) reach(arms[1],petPoint,petWeight);
    else if(clapLift>.01&&free&&speed<.18){
      // The two palms close on the same plane instead of crossing their wrists.
      const gap=.10+.16*(.5+.5*Math.cos(time*12));
      root.updateWorldMatrix(true,true);
      for(const arm of arms){root.localToWorld(lap.set(arm.side*gap,1.42,.48));reach(arm,lap,clapLift);}
    }
    else if (seatHeight!==null && seatBlend>0) {
      for(const arm of arms){
        root.localToWorld(lap.set(arm.side*.26,seatHeight+.23,.28));
        reach(arm,lap,seatBlend);
      }
    }
  }
  return {root,update,land,get mode(){return mode;},hand:arms[1].hand,weaponHand:arms[0].hand};
}
