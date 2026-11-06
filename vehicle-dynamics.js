const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const damp=(value,target,rate,dt)=>value+(target-value)*(1-Math.exp(-rate*dt));
const towardZero=(value,amount)=>Math.sign(value)*Math.max(0,Math.abs(value)-amount);

// A compact tire-based vehicle model: the tires limit engine and brake force,
// steering changes the turning radius, and lateral grip damps a small slide.
export function stepCarDynamics(state,input,dt){
  const grip=clamp(input.grip??1,.35,1),throttle=clamp(input.throttle||0,-1,1);
  const speed=state.speed||0,oldSpeed=speed,maximum=throttle<0?12:38;
  let next=speed,braking=!!input.brake;
  if(braking||throttle*speed<-.2){
    braking=true;next=towardZero(speed,14*grip*dt);
  }else if(throttle){
    const torque=11*(1-.62*clamp(Math.abs(speed)/maximum,0,1));
    next+=throttle*torque*grip*dt;
  }
  const resistance=(.5+.0025*next*next)*dt;
  next=towardZero(next,resistance);
  // The support plane already used by the suspension also supplies hill load.
  next+=Math.sin(input.pitch||0)*9.81*dt;
  next=clamp(next,-12,38);
  if(Math.abs(next)<.025&&!throttle)next=0;
  const steeringLimit=(input.maxSteering??.5)/(1+(Math.abs(next)/12)**1.25);
  const steering=damp(state.steering||0,-clamp(input.steer||0,-1,1)*steeringLimit,7,dt);
  const yawRate=clamp(next*Math.tan(steering)/(input.wheelbase??3),-1.05,1.05);
  const lateralTarget=clamp(-yawRate*next*.075,-3,3);
  const lateral=damp(state.lateral||0,lateralTarget,4*grip,dt);
  const gear=next<-.1?-1:Math.abs(next)<8?1:Math.abs(next)<15?2:Math.abs(next)<23?3:Math.abs(next)<31?4:5;
  const ratios=[0,3.6,2.3,1.6,1.2,.9],ratio=gear<0?3.3:ratios[gear];
  const rpm=clamp(850+Math.abs(next)*ratio*90,850,6500);
  return {speed:next,steering,lateral,yawRate,acceleration:(next-oldSpeed)/Math.max(dt,.001),braking,gear,rpm};
}
