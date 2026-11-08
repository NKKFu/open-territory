import {stepCarDynamics,steeringLock,integrateCarPose} from './vehicle-dynamics.js';

const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));

// The route supplies a point ahead; the same tire and engine model used by the
// player turns the front wheels and integrates the car's actual movement.
export function stepTrafficDrive(state,{target,speed,grip=1,frontGrip=grip,rearGrip=grip,pitch=0,maxSteering=.62,
  turn=null,driveScale=1,massScale=1,wheelbase=3,rearOffset=wheelbase/2,frontWeight,frontDrive,urgent=false},dt){
  const current=state.speed||0,magnitude=Math.abs(current),direction=Math.sign(speed)||Math.sign(current)||1;
  const stopping=Math.abs(speed)<.02,reversing=speed*current<-.04;
  const error=Math.abs(speed)-magnitude;
  const targetChange=state.targetSpeed===undefined?0:(Math.abs(speed)-Math.abs(state.targetSpeed))/Math.max(.001,dt);
  const targetRate=(state.targetRate||0)+(clamp(targetChange,-3.4,1.4)-(state.targetRate||0))*(1-Math.exp(-8*dt));
  const command=clamp(1.9*error+targetRate,-5.4,3.5);
  const previous=state.commandAcceleration??command;
  const acceleration=previous+clamp(command-previous,-6.8*dt,4.4*dt);
  const load=.25+.003*magnitude*magnitude+(1-grip)-Math.sin(pitch)*9.81*direction;
  const effort=acceleration+load;
  let throttle=direction*clamp(effort*Math.sqrt(massScale)/((direction<0?3.45:4.65)*driveScale),0,1);
  let brake=clamp(-effort*Math.pow(massScale,.12)/(10.6*grip),0,1);
  if(urgent&&magnitude>Math.abs(speed)+.35){throttle=0;brake=1;}
  if(reversing){throttle=direction;brake=0;}
  if(stopping){
    throttle=0;
    if(magnitude<.032||urgent||state.commandAcceleration===undefined)brake=1;
    else if(magnitude<.20&&Math.abs(pitch)<.015)brake=Math.min(brake,.012);
  }
  let steer=turn??0;
  if(target&&turn===null){
    const dx=target.x-state.x,dz=target.z-state.z,c=Math.cos(state.heading),s=Math.sin(state.heading);
    const right=c*dx-s*dz,forward=s*dx+c*dz+rearOffset,distance2=Math.max(1,right*right+forward*forward);
    const limit=steeringLock(current,maxSteering);
    const angle=forward<0?Math.sign(right||1)*limit:Math.atan(2*wheelbase*right/distance2);
    steer=clamp(-angle/limit,-1,1);
  }
  const motion=stepCarDynamics(state,{throttle,brake,steer,grip,frontGrip,rearGrip,pitch,maxSteering,driveScale,
    massScale,wheelbase,rearOffset,frontWeight,frontDrive,automaticDirection:true},dt);
  return {...motion,commandAcceleration:acceleration,targetRate,targetSpeed:speed,
    ...integrateCarPose(state.heading,motion.speed,motion.lateral,motion.yawRate,dt)};
}
