const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const damp=(value,target,rate,dt)=>value+(target-value)*(1-Math.exp(-rate*dt));
const angle=value=>Math.atan2(Math.sin(value),Math.cos(value));

const car={leadTime:.30,maxLead:4.3,turnLimit:2.2};
const bike={leadTime:.23,maxLead:2.5,turnLimit:2.55};
const bus={leadTime:.39,maxLead:3.8,turnLimit:1.8};

// Yaw describes the world-space arm *from* the vehicle to the camera.
// Positive longitudinal speed is local +Z, positive lateral speed is local +X.
// While dragging, viewYaw is the absolute orbit angle. lookX/lookZ offset the
// look-at point in world space. Reset when the player leaves the vehicle.
export function createVehicleCamera(){
  const shot={yaw:0,lookX:0,lookZ:0,reversing:false};
  let previousHeading=null,turnRate=0,slip=0,orbitRate=0;
  let oppositeTime=0,manualHold=0,turnSide=0;

  function reset(){
    previousHeading=null;
    turnRate=slip=orbitRate=oppositeTime=manualHold=turnSide=0;
    shot.lookX=shot.lookZ=0;
    shot.reversing=false;
  }

  function update(dt,{heading,speed,lateralSpeed=0,kind,viewYaw=heading+Math.PI,manual=false}){
    const profile=kind==='bike'?bike:kind==='bus'?bus:car;
    if(previousHeading===null){
      previousHeading=heading;
      shot.yaw=viewYaw;
    }
    // The game simulates up to a quarter of a second on slow frames. Match
    // that clock for direction changes and manual hold timers as well.
    dt=clamp(dt,0,.25);
    const measuredTurn=dt?angle(heading-previousHeading)/dt:0;
    turnRate=damp(turnRate,clamp(measuredTurn,-1.8,1.8),4.5,dt);
    previousHeading=heading;

    // Braking, rolling down a slope or a collision can briefly change the
    // sign of wheel speed. Wait for continued travel before changing ends.
    const opposite=shot.reversing?speed:-speed;
    if(opposite>.9)oppositeTime+=dt;
    else if(opposite<.6)oppositeTime=0;
    if(oppositeTime>=(shot.reversing?.36:.60)){
      shot.reversing=!shot.reversing;
      oppositeTime=0;
    }
    const sign=shot.reversing?-1:1,along=Math.max(0,speed*sign);
    // A sustained slide changes the path. A sideways knock by itself should
    // only make a small correction to an otherwise stable view down the road.
    const slipAngle=Math.atan2(lateralSpeed*sign,Math.max(2,Math.abs(speed)));
    slip=damp(slip,clamp(slipAngle,-.55,.55),4,dt);
    const prediction=clamp(turnRate*.26,-.32,.32);
    const travelYaw=heading+(shot.reversing?Math.PI:0);
    const follow=clamp((along-.45)/1.8,0,1);

    if(manual){
      shot.yaw+=angle(viewYaw-shot.yaw);
      manualHold=1.05;
    }else manualHold=Math.max(0,manualHold-dt);

    if(manualHold>0||!follow){
      orbitRate=0;
      turnSide=0;
    }else{
      let error=angle(travelYaw+Math.PI+slip*.72+prediction-shot.yaw);
      // Near a half turn, tiny changes on either side of ±π can pick opposite
      // arcs on consecutive frames. Keep the first choice until they diverge.
      if(Math.abs(error)>Math.PI-.38){
        if(!turnSide)turnSide=Math.abs(orbitRate)>.2?Math.sign(orbitRate):Math.sign(error);
        if(Math.sign(error)!==turnSide)error+=turnSide*Math.PI*2;
      }else turnSide=0;
      const limit=profile.turnLimit*follow;
      orbitRate=damp(orbitRate,clamp(error*3.5,-limit,limit),7.5,dt);
      const step=orbitRate*dt;
      if(step*error>=0&&Math.abs(step)>=Math.abs(error)){
        shot.yaw+=error;
        orbitRate=0;
      }else shot.yaw+=step;
    }

    const lookYaw=travelYaw+slip+prediction;
    const reach=shot.reversing?Math.min(profile.maxLead,2.35):profile.maxLead;
    const lead=Math.min(reach,along*profile.leadTime)*(manualHold>0?.25:1);
    shot.lookX=damp(shot.lookX,Math.sin(lookYaw)*lead,3.8,dt);
    shot.lookZ=damp(shot.lookZ,Math.cos(lookYaw)*lead,3.8,dt);
    return shot;
  }

  return {update,reset};
}
