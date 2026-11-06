// Contact point on an oriented rectangle facing a fixed obstacle. When a face
// is parallel to the obstacle, use its midpoint instead of inventing torque.
export function vehicleSupportArm(heading,width,length,normal){
  const c=Math.cos(heading),s=Math.sin(heading);
  const right=c*normal.x-s*normal.z,forward=s*normal.x+c*normal.z;
  const x=Math.abs(right)<1e-5?0:Math.sign(right)*width/2;
  const z=Math.abs(forward)<1e-5?0:Math.sign(forward)*length/2;
  return {x:c*x+s*z,z:-s*x+c*z};
}

// Resolve a planar impact along the contact normal. Velocities are in world
// coordinates and mass is relative, so cars, bikes and buses respond differently.
export function resolveVehicleImpact(a,b,normal,restitution=.12,contact=null){
  const length=Math.hypot(normal.x,normal.z);
  if(length<1e-8)return null;
  const nx=normal.x/length,nz=normal.z/length;
  const armA=contact?.a||{x:0,z:0},armB=contact?.b||{x:0,z:0};
  const torqueA=armA.z*nx-armA.x*nz,torqueB=armB.z*nx-armB.x*nz;
  const angularA=a.angular||0,angularB=b.angular||0;
  const closing=(a.x-b.x)*nx+(a.z-b.z)*nz+angularA*torqueA-angularB*torqueB;
  if(closing<=.05)return null;
  const invA=1/Math.max(.1,a.mass),invB=1/Math.max(.1,b.mass);
  const invInertiaA=a.inertia>0?1/a.inertia:0,invInertiaB=b.inertia>0?1/b.inertia:0;
  const impulse=(1+restitution)*closing/(invA+invB+torqueA*torqueA*invInertiaA+torqueB*torqueB*invInertiaB);
  return {
    a:{x:a.x-impulse*invA*nx,z:a.z-impulse*invA*nz,angular:angularA-impulse*torqueA*invInertiaA},
    b:{x:b.x+impulse*invB*nx,z:b.z+impulse*invB*nz,angular:angularB+impulse*torqueB*invInertiaB},
    closing
  };
}
