const clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
const damp=(a,b,rate,dt)=>a+(b-a)*(1-Math.exp(-rate*dt));
const angles=[-1.15,-.75,-.35,0,.35,.75,1.15];

// Fit a plane through the wheel supports, including asymmetric truck axles
// and bicycles whose wheels all lie on the same center line.
function fitPlane(points){
  const a=[[points.length,0,0,0],[0,1e-6,0,0],[0,0,1e-6,0]];
  for(const p of points){a[0][1]+=p.x;a[0][2]+=p.z;a[0][3]+=p.h;a[1][1]+=p.x*p.x;a[1][2]+=p.x*p.z;a[1][3]+=p.x*p.h;a[2][2]+=p.z*p.z;a[2][3]+=p.z*p.h;}
  a[1][0]=a[0][1];a[2][0]=a[0][2];a[2][1]=a[1][2];
  for(let i=0;i<3;i++){
    let pivot=i;for(let j=i+1;j<3;j++)if(Math.abs(a[j][i])>Math.abs(a[pivot][i]))pivot=j;
    [a[i],a[pivot]]=[a[pivot],a[i]];const scale=a[i][i];
    for(let j=i;j<4;j++)a[i][j]/=scale;
    for(let k=0;k<3;k++)if(k!==i){const factor=a[k][i];for(let j=i;j<4;j++)a[k][j]-=factor*a[i][j];}
  }
  return {height:a[0][3],x:a[1][3],z:a[2][3]};
}
function rotate(x,y,z,pitch,roll,yaw){
  const cr=Math.cos(roll),sr=Math.sin(roll),cp=Math.cos(pitch),sp=Math.sin(pitch),cy=Math.cos(yaw),sy=Math.sin(yaw);
  const rx=cr*x-sr*y,ry=cp*(sr*x+cr*y)-sp*z,rz=sp*(sr*x+cr*y)+cp*z;
  return {x:cy*rx+sy*rz,y:ry,z:-sy*rx+cy*rz};
}
export function vehicleGroundPose({x,z,yaw,wheels,heightAt,previous=null,dt=1/60,pitchBias=0,rollBias=0}){
  const cy=Math.cos(yaw),sy=Math.sin(yaw),clearance=.008;
  const supports=wheels.map(w=>({x:w.x,z:w.z,h:heightAt(x+cy*w.x+sy*w.z,z-sy*w.x+cy*w.z)+w.radius-w.y}));
  const plane=fitPlane(supports);
  const targetPitch=clamp(-Math.atan(plane.z)+pitchBias,-.35,.35),targetRoll=clamp(Math.atan2(plane.x,Math.hypot(1,plane.z))+rollBias,-.28,.28);
  const pitch=previous?damp(previous.pitch,targetPitch,12,dt):targetPitch,roll=previous?damp(previous.roll,targetRoll,12,dt):targetRoll;
  const vertical=Math.cos(pitch)*Math.cos(roll),up=.16,down=.18;
  function support(w,offset=0){
    let required=-Infinity;
    function point(angle,cross){
      const p=rotate(w.x+cross*(w.width||.34)/2,w.y+offset-w.radius*Math.cos(angle),w.z+w.radius*Math.sin(angle),pitch,roll,yaw);
      const ground=heightAt(x+p.x,z+p.z);required=Math.max(required,ground-p.y+clearance);return {angle,ground};
    }
    for(const cross of [-1,0,1]){
      let previous=null;
      for(const angle of angles){
        const current=point(angle,cross);
        // Resolve an edge between sample angles instead of allowing the tire
        // to cut into the vertical face of a curb.
        if(previous&&Math.abs(current.ground-previous.ground)>.07){
          let a=previous,b=current;
          for(let i=0;i<6;i++){
            const mid=point((a.angle+b.angle)/2,cross);
            if(Math.abs(mid.ground-a.ground)<Math.abs(mid.ground-b.ground))a=mid;else b=mid;
          }
        }
        previous=current;
      }
    }
    return required;
  }
  const required=wheels.map(w=>support(w));
  const targetHeight=required.reduce((a,b)=>a+b,0)/required.length;
  const minimum=Math.max(...required)-up*vertical,maximum=Math.min(...required)+down*vertical;
  const height=Math.max(minimum,Math.min(maximum,previous?damp(previous.height,targetHeight,16,dt):targetHeight));
  const offsets=required.map((h,i)=>{
    let offset=clamp((h-height)/vertical,-down,up);
    // Suspension follows the tilted body axis, so its horizontal movement
    // must also be checked against a curb beside the tire.
    const correction=support(wheels[i],offset)-height;
    offset=clamp(offset+Math.max(0,correction)/vertical,-down,up);
    return offset;
  });
  return {height,pitch,roll,offsets};
}
