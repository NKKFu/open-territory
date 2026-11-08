// Subtract convex road footprints from convex pavement panels in the XZ plane.
// A diagonal street can cut a corner anywhere between two sampled center points.
const EPS=1e-7;
const cross=(a,b,p)=>(b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x);
const area=p=>p.reduce((sum,a,i)=>{const b=p[(i+1)%p.length];return sum+a.x*b.z-b.x*a.z;},0)/2;
function bounds(p){
  return {x0:Math.min(...p.map(v=>v.x)),x1:Math.max(...p.map(v=>v.x)),
    z0:Math.min(...p.map(v=>v.z)),z1:Math.max(...p.map(v=>v.z))};
}
function intersects(a,b){return a.x0<b.x1-EPS&&a.x1>b.x0+EPS&&a.z0<b.z1-EPS&&a.z1>b.z0+EPS;}
function half(p,a,b,sign){
  const out=[];let prev=p[p.length-1],d0=sign*cross(a,b,prev);
  for(const v of p){
    const d=sign*cross(a,b,v);
    if((d0>=0)!==(d>=0)){
      const t=d0/(d0-d);out.push({x:prev.x+t*(v.x-prev.x),z:prev.z+t*(v.z-prev.z)});
    }
    if(d>=0)out.push(v);
    prev=v;d0=d;
  }
  return out;
}
export function footprint(points){
  const p=area(points)<0?[...points].reverse():points;
  return {points:p,bounds:bounds(p)};
}
export function rectangle(x,z,w,d,angle=0){
  const c=Math.cos(angle),s=Math.sin(angle);
  return footprint([[-.5,-.5],[.5,-.5],[.5,.5],[-.5,.5]].map(([u,v])=>({x:x+c*w*u+s*d*v,z:z-s*w*u+c*d*v})));
}
export function cutPavement(panel,masks){
  let pieces=[panel];
  for(const mask of masks){
    if(!intersects(panel.bounds,mask.bounds))continue;
    const next=[];
    for(const piece of pieces){
      if(!intersects(piece.bounds,mask.bounds)){next.push(piece);continue;}
      const poly=mask.points;
      // A separating edge avoids splitting two neighboring, disjoint ribbons.
      if(poly.some((a,i)=>piece.points.every(p=>cross(a,poly[(i+1)%poly.length],p)<=EPS))){
        next.push(piece);continue;
      }
      let inside=piece.points;
      for(let i=0;i<poly.length&&inside.length>=3;i++){
        const a=poly[i],b=poly[(i+1)%poly.length];
        const outside=half(inside,a,b,-1);
        if(outside.length>=3&&area(outside)>EPS)next.push(footprint(outside));
        inside=half(inside,a,b,1);
      }
    }
    pieces=next;if(!pieces.length)break;
  }
  return pieces;
}

export function overlapFootprints(panel,mask){
  if(!intersects(panel.bounds,mask.bounds))return null;
  let p=panel.points;
  for(let i=0;i<mask.points.length&&p.length>=3;i++)p=half(p,mask.points[i],mask.points[(i+1)%mask.points.length],1);
  return p.length>=3&&area(p)>EPS?footprint(p):null;
}
