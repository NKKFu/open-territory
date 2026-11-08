const damp=(a,b,rate,dt)=>a+(b-a)*(1-Math.exp(-rate*dt));
const clamp=(n,lo,hi)=>Math.max(lo,Math.min(n,hi));
const turn=(a,b)=>Math.atan2(Math.sin(a-b),Math.cos(a-b));
const skin=.22;

// Slab intersection in the box's frame. The direction is a unit vector,
// so both ends of the interval are distances along the arm.
function clearance(origin,ray,distance,boxes,padding,previousEye,allowPosts){
  let limit=distance;
  for(const b of boxes){
    const dx=origin.x-b.x,dz=origin.z-b.z,c=b.cos??1,s=b.sin??0;
    const px=dx*c-dz*s,pz=dx*s+dz*c,py=origin.y-(b.top+b.bottom)/2;
    const vx=ray.x*c-ray.z*s,vz=ray.x*s+ray.z*c;
    // Distance to a convex box cannot decrease again along a ray. This
    // eliminates false hits on the square padding at the end of a wall.
    const ox=px-clamp(px,-b.w/2,b.w/2),oz=pz-clamp(pz,-b.d/2,b.d/2),hh=(b.top-b.bottom)/2;
    const oy=py-clamp(py,-hh,hh);
    if((ox||oy||oz)&&ox*vx+oy*ray.y+oz*vz>=0)continue;
    let near=-Infinity,far=Infinity,leaving=false;
    for(let axis=0;axis<3;axis++){
      const p=axis===0?px:axis===1?py:pz,v=axis===0?vx:axis===1?ray.y:vz;
      const half=(axis===0?b.w:axis===1?b.top-b.bottom:b.d)/2,r=half+padding;
      if(Math.abs(p)>=half-1e-8&&p*v>=0)leaving=true;
      if(Math.abs(v)<1e-8){
        if(Math.abs(p)>r){far=-Infinity;break;}
      }else{
        const a=(-r-p)/v,e=(r-p)/v;
        near=Math.max(near,Math.min(a,e));far=Math.min(far,Math.max(a,e));
        if(near>far)break;
      }
    }
    if(far<0||near>far||near>limit||near<0&&leaving)continue;
    // A narrow pole can pass in the foreground. It still blocks the lens
    // when the eye is alongside it, or when zooming from its near side.
    if(allowPosts&&Math.max(b.w,b.d)<=.75&&b.top-b.bottom>1.45&&far<distance){
      const previousDepth=previousEye?(previousEye.x-origin.x)*ray.x+
        (previousEye.y-origin.y)*ray.y+(previousEye.z-origin.z)*ray.z:Infinity;
      if(previousDepth>=far-.015)continue;
    }
    limit=Math.min(limit,Math.max(0,near-.035));
  }
  return limit;
}

export function cameraClearance(origin,direction,distance,boxes,padding=skin){
  // Used for opaque line-of-sight tests as well as camera clearance.
  return clearance(origin,direction,distance,boxes,padding,null,false);
}

const horizon=.44;
const poses=[[0,0],[0,-.24],[0,.30],[.40,0],[.80,0],[1.19,0],[1.52,0],
  [.80,-.25],[1.52,-.25],[.80,.34],[1.30,.34],[0,.64],[1.92,0]];

export function createCameraBoom(){
  let initialized=false,length=0,lastDistance=0,lastYaw=0,lastPitch=0,frame=0;
  let side=0,raise=0,selectedSide=0,selectedRaise=0,openTime=0,lastSide=1,planIn=0;
  const previous={x:0,y:0,z:0},lastEye={x:0,y:0,z:0};
  const ray={x:0,y:0,z:0},prediction={x:0,y:0,z:0};
  const doorSamples=new WeakMap(),moving=[],futureBoxes=[];

  return function placeCamera(origin,yaw,pitch,distance,minView,boxes,dt,eye){
    if(initialized&&Math.hypot(origin.x-previous.x,origin.y-previous.y,origin.z-previous.z)>4){
      initialized=false;side=raise=selectedSide=selectedRaise=openTime=planIn=0;
    }
    const inverseStep=initialized&&dt>.0001?1/dt:0;
    const vx=clamp((origin.x-previous.x)*inverseStep,-10,10);
    const vy=clamp((origin.y-previous.y)*inverseStep,-3,3);
    const vz=clamp((origin.z-previous.z)*inverseStep,-10,10);
    const spin=clamp(turn(yaw,lastYaw)*inverseStep,-2.8,2.8);
    const tilt=clamp((pitch-lastPitch)*inverseStep,-1.5,1.5);
    // Stop the predicted subject at the closed door or wall that it is
    // approaching. Geometry behind it cannot obstruct today's camera arm.
    const speed=Math.hypot(vx,vy,vz);
    let travelTime=horizon;
    if(speed>.035){
      ray.x=vx/speed;ray.y=vy/speed;ray.z=vz/speed;
      travelTime=cameraClearance(origin,ray,speed*horizon,boxes,.38)/speed;
    }
    moving.length=0;
    frame++;
    for(let i=0;i<boxes.length;i++){
      const b=boxes[i];if(!b.motionKey)continue;
      const angle=Math.atan2(b.sin??0,b.cos??1),old=doorSamples.get(b.motionKey);
      if(old?.frame===frame-1&&inverseStep){
        const dx=(b.x-old.x)*inverseStep,dz=(b.z-old.z)*inverseStep,da=turn(angle,old.angle)*inverseStep;
        if(Math.abs(dx)+Math.abs(dz)+Math.abs(da)>.04){
          moving.push({index:i,box:b,dx:clamp(dx,-8,8),dz:clamp(dz,-8,8),da:clamp(da,-4,4),angle});
        }
      }
      doorSamples.set(b.motionKey,{x:b.x,z:b.z,angle,frame});
    }
    const inMotion=Math.abs(vx)+Math.abs(vy)+Math.abs(vz)+Math.abs(spin)+Math.abs(tilt)>.035||moving.length>0;
    if(moving.length){futureBoxes.length=boxes.length;for(let i=0;i<boxes.length;i++)futureBoxes[i]=boxes[i];}
    function cast(offset,lift,t=0,reach=distance){
      const elevation=clamp(pitch+tilt*t+lift,Math.min(.035,pitch),Math.max(1.09,pitch));
      const angle=yaw+spin*t+offset,h=Math.cos(elevation);
      ray.x=Math.sin(angle)*h;ray.y=Math.sin(elevation);ray.z=Math.cos(angle)*h;
      const travel=Math.min(t,travelTime);
      prediction.x=origin.x+vx*travel;prediction.y=origin.y+vy*travel;prediction.z=origin.z+vz*travel;
      if(t&&moving.length)for(const m of moving){
        const a=m.angle+m.da*t;
        futureBoxes[m.index]={...m.box,x:m.box.x+m.dx*t,z:m.box.z+m.dz*t,cos:Math.cos(a),sin:Math.sin(a)};
      }
      return clearance(prediction,ray,reach,t&&moving.length?futureBoxes:boxes,skin,initialized?lastEye:null,true);
    }
    const nearView=Math.min(distance,minView);
    function usable(offset,lift){
      let room=cast(offset,lift);
      if(inMotion){
        room=Math.min(room,cast(offset,lift,horizon*.5),cast(offset,lift,horizon));
      }
      return room;
    }
    const direct=usable(0,0),escaping=selectedSide!==0||selectedRaise!==0;
    planIn=Math.max(0,planIn-dt);
    if(direct>=Math.min(distance,nearView+.45)){openTime+=dt;planIn=0;}else openTime=0;
    if(escaping&&openTime>.24){selectedSide=0;selectedRaise=0;}
    else if(direct<nearView-.30&&planIn===0){
      // Search only when the subject would be crowded. Prefer a level view
      // down an aisle, and keep that side until the direct view stays clear.
      const committed=selectedSide;
      function score(offset,lift){
        const room=usable(offset,lift);
        // The way to a good view has to be usable too. This distinguishes
        // both ends of a shelf before the character passes its corner.
        const crossing=cast(damp(side,offset,8,.15),damp(raise,lift,8,.15),.15);
        return Math.min(room,nearView+.65)+.55*Math.min(crossing,nearView+.4)-
          Math.abs(offset)*.48-Math.abs(lift)*.95-(committed*offset<0?.48:0);
      }
      let best=score(selectedSide,selectedRaise);
      planIn=.035;
      if(best<nearView+.55*(nearView+.40)+.46){
        for(const [angle,lift] of poses){
          const signs=angle?[lastSide,-lastSide]:[1];
          for(const sign of signs){
            const offset=angle*sign,value=score(offset,lift);
            if(value>best+.13){selectedSide=offset;selectedRaise=lift;best=value;}
          }
        }
        if(selectedSide)lastSide=Math.sign(selectedSide);
      }
    }
    side=initialized?damp(side,selectedSide,selectedSide?8:4.5,dt):selectedSide;
    raise=initialized?damp(raise,selectedRaise,selectedRaise?8:4.5,dt):selectedRaise;
    let hard=cast(side,raise),safe=hard;
    if(inMotion){
      // Find the time at which a moving arm first enters a solid. Bring the
      // lens inward over that approach; a wall that stays beside it is inert.
      let last=0;
      for(let step=1;step<=3;step++){
        const t=horizon*step/3;
        const nextSide=damp(side,selectedSide,8,t),nextRaise=damp(raise,selectedRaise,8,t);
        const upcoming=cast(nextSide,nextRaise,t);
        if(upcoming<safe-.20){
          let lo=last,hi=t;
          for(let i=0;i<4;i++){
            const mid=(lo+hi)/2;
            if(cast(damp(side,selectedSide,8,mid),damp(raise,selectedRaise,8,mid),mid)<upcoming+.20)hi=mid;
            else lo=mid;
          }
          const u=hi/horizon,ease=u*u*(3-2*u);
          safe=Math.min(safe,upcoming+(distance-upcoming)*ease);
        }
        last=t;
      }
    }
    const recovering=clamp(length+distance-lastDistance,0,distance);
    const requested=initialized?Math.max(recovering-Math.max(8,distance*1.6)*dt,
      Math.min(safe,damp(recovering,safe,4.3,dt))):safe;
    if(initialized&&hard<requested-.03){
      // Slide along the corner instead of sweeping the eye through it on
      // the next frame. Start at the last actual eye, not the desired orbit.
      const x=lastEye.x-origin.x,y=lastEye.y-origin.y,z=lastEye.z-origin.z;
      const fromSide=turn(Math.atan2(x,z),yaw),fromRaise=Math.atan2(y,Math.hypot(x,z))-pitch;
      const step=clamp(27*dt/Math.max(length,2),.03,.20);
      let best=hard,chosenSide=side,chosenRaise=raise;
      function consider(offset,lift){
        const room=cast(offset,lift),cost=.55*Math.abs(turn(offset,side))+.55*Math.abs(lift-raise);
        const value=Math.min(requested,room)-cost;
        if(value>best+.005){best=value;chosenSide=offset;chosenRaise=lift;hard=room;}
      }
      for(const fraction of [.5,0]){
        consider(fromSide+turn(side,fromSide)*fraction,fromRaise+(raise-fromRaise)*fraction);
      }
      for(const offset of [step,-step,2*step,-2*step]){
        consider(fromSide+offset,fromRaise);
      }
      consider(fromSide,fromRaise+step);consider(fromSide,fromRaise-step);
      side=chosenSide;raise=chosenRaise;
    }
    length=Math.min(hard,requested);
    // A shorter arm behind furniture can put its actual tip beside a post.
    length=cast(side,raise,0,Math.max(0,length));
    eye.x=origin.x+ray.x*length;eye.y=origin.y+ray.y*length;eye.z=origin.z+ray.z*length;
    previous.x=origin.x;previous.y=origin.y;previous.z=origin.z;lastYaw=yaw;lastPitch=pitch;
    lastEye.x=eye.x;lastEye.y=eye.y;lastEye.z=eye.z;lastDistance=distance;initialized=true;
    return length;
  };
}
