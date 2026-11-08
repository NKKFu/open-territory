const clamp=(n,min,max)=>Math.max(min,Math.min(max,n));
const angle=turn=>Math.atan2(Math.sin(turn),Math.cos(turn));

// In a tight passage, aim halfway between the screen angles of head and feet.
// Centering their world-space midpoint would still crop the nearer end.
export function frameCloseActor(eye,look,feet,height,weight,baseFov){
  if(!weight)return baseFov;
  const horizontal=Math.max(.05,Math.hypot(eye.x-feet.x,eye.z-feet.z));
  const headAngle=Math.atan2(feet.y+height-eye.y,horizontal);
  const shoeAngle=Math.atan2(feet.y-eye.y,horizontal);
  look.x+=(feet.x-look.x)*weight;
  look.z+=(feet.z-look.z)*weight;
  const distance=Math.hypot(eye.x-look.x,eye.z-look.z);
  const middle=eye.y+Math.tan((headAngle+shoeAngle)*.5)*distance;
  look.y+=(middle-look.y)*weight;
  const fit=(headAngle-shoeAngle)*180/Math.PI*1.10;
  return baseFov+weight*clamp(fit-baseFov,0,13);
}

export function createWalkCamera(){
  let lastForward=0,lastSide=0,referenceYaw=0,followYaw=null,turnSide=1;
  const travel={x:0,z:0};

  // A held direction is a request for one world heading. Only the player's
  // next input or manual orbit may change it; the following camera cannot.
  function direction(forward,side,viewYaw){
    if(forward!==lastForward||side!==lastSide){
      referenceYaw=viewYaw;lastForward=forward;lastSide=side;
    }
    const magnitude=Math.hypot(forward,side);
    if(!magnitude){travel.x=travel.z=0;return travel;}
    const sin=Math.sin(referenceYaw),cos=Math.cos(referenceYaw);
    travel.x=(-sin*forward+cos*side)/magnitude;
    travel.z=(-cos*forward-sin*side)/magnitude;
    return travel;
  }

  function orbit(delta){
    referenceYaw+=delta;
    followYaw=null;
  }

  function follow(dt,viewYaw,facing,speed,allow){
    if(!allow){followYaw=null;return viewYaw;}
    if((lastForward||lastSide)&&speed>.35)followYaw=facing+Math.PI;
    if(followYaw===null)return viewYaw;
    let error=angle(followYaw-viewYaw);
    if(Math.abs(error)>.03&&Math.abs(error)<Math.PI-.18)turnSide=Math.sign(error);
    else if(Math.abs(error)>Math.PI-.18&&Math.sign(error)!==turnSide)error+=turnSide*Math.PI*2;
    if(Math.abs(error)<.004)return viewYaw+error;
    const run=clamp((speed-4.5)/4,0,1),limit=(3.65+run*.65)*dt;
    return viewYaw+clamp(error*(1-Math.exp(-(4.7+run)*dt)),-limit,limit);
  }

  function release(){lastForward=lastSide=0;}
  function reset(){release();followYaw=null;}
  return {direction,orbit,follow,release,reset};
}
