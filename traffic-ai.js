// Conservative clearance for using the opposite lane to pass a stopped car.
// Positions and velocities are in world space; forward and right are unit vectors.
export function passingLaneClear(car,vehicles,pedestrians,requiredAhead){
  const {x,z,fx,fz,rx,rz}=car,lane=car.laneOffset??-6.6;
  for(const other of vehicles){
    const dx=other.x-x,dz=other.z-z,ahead=dx*fx+dz*fz,across=dx*rx+dz*rz;
    if(Math.abs(across-lane)>(other.width??2.5)/2+2.15)continue;
    const speed=(other.vx||0)*fx+(other.vz||0)*fz;
    const front=requiredAhead+Math.max(0,-speed)*9+(other.length??4.6)/2;
    const rear=12+Math.max(0,speed)*4+(other.length??4.6)/2;
    if(ahead>-rear&&ahead<front)return false;
  }
  for(const person of pedestrians){
    const dx=person.x-x,dz=person.z-z,ahead=dx*fx+dz*fz,across=dx*rx+dz*rz;
    if(ahead>-6&&ahead<requiredAhead+8&&across>lane-2.5&&across<2.5)return false;
  }
  return true;
}

// Solve for a speed that leaves time to react and brake in the available
// bumper-to-bumper space. A moving leader retains its own stopping distance.
export function approachSpeed(gap,leaderSpeed=0,headway=.70,deceleration=2.65){
  const reaction=deceleration*headway,lead=Math.max(0,leaderSpeed);
  return Math.max(0,Math.sqrt(Math.max(0,reaction*reaction+lead*lead+2*deceleration*gap))-reaction);
}

// ahead and across are measured from the front bumper in its local frame.
// Compare the time the pedestrian occupies the lane with this vehicle's pass.
export function pedestrianConflict(ahead,across,alongSpeed,acrossSpeed,vehicleSpeed,width,length){
  const margin=width/2+.56,closing=Math.max(.8,vehicleSpeed-alongSpeed);
  if(ahead<-.55||ahead>Math.max(14,vehicleSpeed*3+vehicleSpeed*vehicleSpeed/6+3))return null;
  let enter=0,leave=Infinity;
  if(Math.abs(acrossSpeed)<.09){
    if(Math.abs(across)>margin)return null;
  }else{
    const a=(-margin-across)/acrossSpeed,b=(margin-across)/acrossSpeed;
    enter=Math.max(0,Math.min(a,b));leave=Math.max(a,b);
  }
  if(enter>3.3||leave<0)return null;
  const frontArrival=Math.max(0,ahead/closing),rearArrival=(ahead+length)/closing;
  if(enter>rearArrival+.35||(leave<frontArrival-.50&&ahead>1.8))return null;
  return {gap:Math.max(0,ahead+alongSpeed*enter-1.25),contactGap:Math.max(0,ahead-.4),occupied:Math.abs(across)<=margin};
}

// Sample each bend once, including the join of a closed track. A turn signal
// is reserved for a deliberate change of direction at an actual intersection.
export function routeBends(track,atJunction){
  const {points,cumulative,length}=track,n=points.length-1,bends=[];
  for(let i=0;i<n;i++){
    const a=points[(i+n-1)%n],v=points[i],b=points[(i+1)%n];
    const ax=v.x-a.x,az=v.z-a.z,bx=b.x-v.x,bz=b.z-v.z;
    const angle=Math.atan2(az*bx-ax*bz,ax*bx+az*bz);
    if(Math.abs(angle)<.009)continue;
    const spacing=Math.max(.65,Math.min(6,(Math.hypot(ax,az)+Math.hypot(bx,bz))/2));
    bends.push({at:cumulative[i],angle,curvature:Math.abs(angle)/spacing,junction:atJunction(v.x,v.z)});
  }
  for(const bend of bends){
    if(track.turns){
      bend.signal=track.turns.find(turn=>bend.at>=turn.start&&bend.at<=turn.end)?.signal;
      continue;
    }
    if(!bend.junction)continue;
    let arc=0;
    for(const other of bends){
      const delta=Math.abs(other.at-bend.at);
      if(Math.min(delta,length-delta)<5.8)arc+=other.angle;
    }
    if(Math.abs(arc)>.40)bend.signal=arc>0?'right':'left';
  }
  return bends;
}

export function routeAdvice(bends,length,progress,speed,cruise,lateralLimit=2.7){
  let limit=cruise,signal=null,first=Infinity;
  for(const bend of bends){
    let ahead=(bend.at-progress+length)%length;
    if(ahead>length-4)ahead-=length;
    if(ahead>Math.max(15,speed*2.7+7))continue;
    const corner=Math.max(1.8,Math.sqrt(lateralLimit/bend.curvature));
    limit=Math.min(limit,Math.sqrt(corner*corner+5.0*Math.max(0,ahead-2-speed*.55)));
    if(bend.signal&&ahead<Math.max(9,speed*2.1+2)&&ahead<first){signal=bend.signal;first=ahead;}
  }
  return {speed:limit,signal};
}

const allRed={ns:0,ew:0}, nsGreen={ns:2,ew:0}, ewGreen={ns:0,ew:2};
const nsAmber={ns:1,ew:0}, ewAmber={ns:0,ew:1};

export function signalPhase(time,junction){
  if(time<junction.amberUntil)return junction.amber;
  if(time<junction.reopenAt)return allRed;
  const phase=(time+junction.offset)%24;
  if(phase<9)return nsGreen;
  if(phase<11)return nsAmber;
  if(phase<12)return allRed;
  if(phase<21)return ewGreen;
  return phase<23?ewAmber:allRed;
}

export function clearsAmber(gap,speed,time,junction,axis){
  if(speed<2||gap>=speed*speed/6.4)return false;
  // Only continue if the nose will actually cross the paint during yellow.
  // This applies to the shorter yellow produced by a remote crossing too.
  const arrival=time+(Math.max(0,gap)+.32)/speed+.10;
  return signalPhase(arrival,junction)[axis]===1;
}

export function requestCrossing(junction,time){
  if(time<junction.reopenAt)return;
  const phase=signalPhase(time,junction);
  junction.amber=phase.ns?nsAmber:phase.ew?ewAmber:allRed;
  junction.amberUntil=time+(phase===allRed?0:1.5);
  junction.reopenAt=junction.amberUntil+8;
  // Start with a full green interval after the crossing, never a fraction of one.
  const next=phase.ns?12:phase.ew?0:((time+junction.offset)%24<12?12:0);
  junction.offset=next-junction.reopenAt;
}
