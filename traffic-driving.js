import {stepCarDynamics} from './vehicle-dynamics.js';

const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));

// The route supplies a point ahead; the same tire and engine model used by the
// player turns the front wheels and integrates the car's actual movement.
export function stepTrafficDrive(state,{target,speed,grip=1,pitch=0,maxSteering=.5,turn=null},dt){
  const current=state.speed||0,delta=Math.abs(speed)-Math.abs(current);
  let throttle=0,brake=false;
  if(Math.abs(speed)<.02)brake=Math.abs(current)>.02;
  else if(current*speed<-.1)throttle=Math.sign(speed);
  else if(delta<-.3)brake=true;
  else throttle=Math.sign(speed)*clamp(.07+Math.max(0,delta)*.3,0,1);
  let steer=turn||0;
  if(target&&!turn){
    const dx=target.x-state.x,dz=target.z-state.z,c=Math.cos(state.heading),s=Math.sin(state.heading);
    const right=c*dx-s*dz,forward=s*dx+c*dz,distance2=Math.max(1,dx*dx+dz*dz);
    const limit=maxSteering/(1+(Math.abs(current)/12)**1.25);
    const angle=forward<0?Math.sign(right||1)*limit:Math.atan(6*right/distance2);
    steer=clamp(-angle/limit,-1,1);
  }
  const motion=stepCarDynamics({speed:current,steering:state.steering,lateral:state.lateral},
    {throttle,brake,steer,grip,pitch,maxSteering},dt);
  const heading=state.heading+motion.yawRate*dt,c=Math.cos(heading),s=Math.sin(heading);
  return {...motion,heading,dx:(s*motion.speed+c*motion.lateral)*dt,dz:(c*motion.speed-s*motion.lateral)*dt};
}
