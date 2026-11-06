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
