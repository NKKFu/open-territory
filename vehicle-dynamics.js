const gravity=9.81;
const reverseLimit=7;
const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const damp=(value,target,rate,dt)=>value+(target-value)*(-Math.expm1(-rate*dt));
const shiftSpeeds=[8.5,15,22.5,30.5];
const gearRatios=[0,3.65,2.24,1.54,1.12,.86];

// The steering rack has its full travel when parking. At speed, a held key
// requests a progressively shallower curvature; tapping it makes a small correction.
export function steeringLock(speed,parkingLock=.62){
  return Math.atan(Math.tan(parkingLock)/(1+(speed/8.6)**2));
}

export function wheelSteeringAngle(steering,wheelbase,wheelX){
  const tangent=Math.tan(steering);
  return Math.atan2(wheelbase*tangent,wheelbase-wheelX*tangent);
}

// Integrate the body's forward and lateral velocities around the turn, in
// world coordinates (+Z forward, +X lateral). Both manual and AI driving use it.
export function integrateCarPose(heading,speed,lateral,yawRate,dt){
  const halfTurn=yawRate*dt*.5,mid=heading+halfTurn;
  const span=dt*(Math.abs(halfTurn)<1e-5?1:Math.sin(halfTurn)/halfTurn);
  return {heading:heading+2*halfTurn,dx:(Math.sin(mid)*speed+Math.cos(mid)*lateral)*span,
    dz:(Math.cos(mid)*speed-Math.sin(mid)*lateral)*span};
}

export function stepCarDynamics(state,input,dt){
  const throttle=clamp(input.throttle||0,-1,1),requested=Math.sign(throttle);
  const service=clamp(Number(input.brake)||0,0,1),hand=input.handbrake?1:0;
  const wheelbase=input.wheelbase??3,rearOffset=input.rearOffset??wheelbase*.5;
  const mass=input.massScale??1,drive=input.driveScale??1,parkingLock=input.maxSteering??.62;
  const frontWeight=input.frontWeight??.54,frontDrive=input.frontDrive??.83;
  const frontGrip=clamp(input.frontGrip??input.grip??1,.32,1.15);
  const rearGrip=clamp(input.rearGrip??input.grip??1,.32,1.15);
  const rearArm=wheelbase*frontWeight,frontArm=wheelbase-rearArm,centerZ=rearArm-rearOffset;
  const inertia=wheelbase*wheelbase*(.19+.025*Math.min(2,Math.max(0,mass-1)));
  const grade=gravity*Math.sin(input.pitch||0);
  let speed=state.speed||0,lateral=state.lateral||0,yawRate=state.yawRate||0,steering=state.steering||0;
  let driveDirection=state.driveDirection||(speed<-.12?-1:1),shiftTime=state.shiftTime||0;
  let pedal=state.pedal??Math.abs(throttle),handbrakeLevel=state.handbrakeLevel||0;
  let braking=service>0,tireSlip=0,cornerAcceleration=0;
  const initialSpeed=speed,steps=Math.max(1,Math.ceil(dt*100)),h=dt/steps;

  for(let i=0;i<steps;i++){
    const absSpeed=Math.abs(speed),againstMotion=requested*speed<-.045;
    let changing=!!requested&&requested!==driveDirection;
    if(changing&&absSpeed<.12&&!service){
      shiftTime+=h;
      if(input.automaticDirection||shiftTime>=.24){
        driveDirection=requested;changing=false;shiftTime=0;
      }
    }else shiftTime=0;
    const brake=Math.max(service,changing||againstMotion?Math.abs(throttle):0);
    braking=brake>.015;
    const power=braking?0:Math.abs(throttle);
    pedal=damp(pedal,power,power>pedal?8:16,h);
    handbrakeLevel=damp(handbrakeLevel,hand,hand?13:9,h);

    const target=-clamp(input.steer||0,-1,1)*steeringLock(speed,parkingLock);
    const returning=steering*target<=0||Math.abs(target)<Math.abs(steering);
    const rackRate=(returning?2.95:1.85)/(1+absSpeed*.035)/Math.pow(mass,.19);
    const rackStep=(target-steering)*(-Math.expm1(-(returning?13:9)*h));
    steering+=clamp(rackStep,-rackRate*h,rackRate*h);

    const reverse=driveDirection<0,top=reverse?reverseLimit:(input.topSpeed??39);
    const taper=clamp((top-absSpeed)/(reverse?2:8),0,1);
    const motor=braking?0:driveDirection*pedal*drive/Math.sqrt(mass)*
      Math.min(reverse?3.45:4.35,(reverse?16:77)/Math.max(absSpeed,1))*(reverse?1:(input.powerScale??1))*taper;
    const travelSign=Math.sign(speed)||Math.sign(grade);
    const serviceForce=brake*10.8/Math.pow(mass,.12);
    const request=motor-travelSign*(serviceForce+handbrakeLevel*3.5);
    const loadFront=clamp(frontWeight-request*.58/(gravity*wheelbase),.26,.82);
    const frontMax=gravity*1.09*frontGrip*loadFront,rearMax=gravity*1.09*rearGrip*(1-loadFront);
    const brakeBias=clamp(frontWeight+.12,.56,.72);
    const frontLong=clamp(motor*frontDrive-travelSign*serviceForce*brakeBias,-frontMax*.93,frontMax*.93);
    const rearLong=clamp(motor*(1-frontDrive)-travelSign*(serviceForce*(1-brakeBias)+handbrakeLevel*4.2),-rearMax*.96,rearMax*.96);
    // What remains of each axle's friction circle is available to turn the car.
    const frontCorner=Math.sqrt(Math.max(0,frontMax**2-frontLong**2));
    const rearCorner=Math.sqrt(Math.max(0,rearMax**2-rearLong**2))*(1-handbrakeLevel*.60);
    const centerSide=lateral+yawRate*centerZ;
    const frontSide=centerSide+yawRate*frontArm,rearSide=centerSide-yawRate*rearArm;
    const sin=Math.sin(steering),cos=Math.cos(steering);
    const frontSlip=(frontSide*cos-speed*sin)/Math.max(2.5,Math.abs(speed*cos+frontSide*sin));
    const rearSlip=rearSide/Math.max(2.5,absSpeed);
    const tireFront=-frontCorner*Math.tanh(frontSlip*8.8);
    const tireRear=-rearCorner*Math.tanh(rearSlip*10.4);
    const frontSideForce=tireFront*cos+frontLong*sin;
    cornerAcceleration=frontSideForce+tireRear;
    const newYaw=yawRate+(frontArm*frontSideForce-rearArm*tireRear)/inertia*h;
    const rotation=(yawRate+newYaw)*.5;
    const newSide=centerSide+(cornerAcceleration-speed*rotation)*h;
    const rolling=.23+.0031*speed*speed+(1-(frontGrip+rearGrip)*.5)*1.1;
    const longitudinal=frontLong*cos-tireFront*sin+rearLong+grade+centerSide*rotation;
    const moving=speed+longitudinal*h;
    let next=Math.sign(moving)*Math.max(0,Math.abs(moving)-rolling*h);
    if((braking||handbrakeLevel>.6)&&Math.abs(grade)<serviceForce+handbrakeLevel*3.5&&
        (speed*next<=0||absSpeed<.12&&Math.abs(next)<.12))next=0;
    speed=clamp(next,-40,Math.max(44,(input.topSpeed??39)+5));
    // At walking pace the rear wheels roll about the turn center. Fade to
    // tire dynamics as speed rises; impacts can still shed lateral velocity.
    const parking=clamp((4.5-absSpeed)/2.5,0,1),settle=-Math.expm1(-20*parking*h);
    const parkingYaw=speed*Math.tan(steering)/wheelbase;
    yawRate=newYaw+(parkingYaw-newYaw)*settle;
    // Once the wheel is straight, stability control helps the tires catch
    // a residual slide. The handbrake leaves the rear free during the maneuver.
    const straightAssist=clamp(1-Math.abs(steering)/.065,0,1)*(1-handbrakeLevel)*clamp(absSpeed/8,0,1);
    yawRate*=Math.exp(-1.45*straightAssist*h);
    lateral=newSide-yawRate*centerZ;
    lateral+=(parkingYaw*rearOffset-lateral)*settle;
    if(speed===0&&Math.abs(lateral)<.035&&Math.abs(yawRate)<.045){lateral=0;yawRate=0;}
    tireSlip=Math.max(Math.abs(frontSlip),Math.abs(rearSlip),handbrakeLevel*Math.min(.35,absSpeed*.028));
  }
  const absSpeed=Math.abs(speed);
  let gear=driveDirection<0?-1:Math.max(1,state.gear||1);
  if(gear>0){
    while(gear<5&&absSpeed>shiftSpeeds[gear-1]+.5)gear++;
    while(gear>1&&absSpeed<shiftSpeeds[gear-2]-.8)gear--;
  }
  const ratio=gear<0?3.4:gearRatios[gear];
  const rpm=clamp(820+absSpeed*ratio*85+pedal*380,820,6200);
  return {speed,steering,lateral,yawRate,acceleration:dt?(speed-initialSpeed)/dt:0,braking,gear,rpm,
    pedal,driveDirection,shiftTime,handbrakeLevel,tireSlip,cornerAcceleration};
}
