// Heights of the horizontal surfaces actually drawn in the world. Painted
// markings are excluded so tires do not bounce on lane stripes.
export function createSurfaceSampler({baseHeight=()=>0,cellSize=16}={}){
  const cells=new Map();
  function addRectangle({x,z,w,d,top,rotation=0}){
    const c=Math.cos(rotation),s=Math.sin(rotation),rx=Math.abs(c)*w/2+Math.abs(s)*d/2,rz=Math.abs(s)*w/2+Math.abs(c)*d/2;
    const surface={x,z,w,d,top,c,s};
    for(let ix=Math.floor((x-rx)/cellSize);ix<=Math.floor((x+rx)/cellSize);ix++)for(let iz=Math.floor((z-rz)/cellSize);iz<=Math.floor((z+rz)/cellSize);iz++){
      const key=`${ix},${iz}`;if(!cells.has(key))cells.set(key,[]);cells.get(key).push(surface);
    }
  }
  function heightAt(x,z){
    let height=baseHeight(x,z);
    for(const r of cells.get(`${Math.floor(x/cellSize)},${Math.floor(z/cellSize)}`)||[]){
      if(r.points){
        const [a,b,c]=r.points,cross=(p,q)=>(q.x-p.x)*(z-p.z)-(q.z-p.z)*(x-p.x);
        const ab=cross(a,b),bc=cross(b,c),ca=cross(c,a);
        if((ab>=-.001&&bc>=-.001&&ca>=-.001)||(ab<=.001&&bc<=.001&&ca<=.001))height=Math.max(height,r.top);
      }else{
        const dx=x-r.x,dz=z-r.z;
        if(Math.abs(r.c*dx-r.s*dz)<=r.w/2+.001&&Math.abs(r.s*dx+r.c*dz)<=r.d/2+.001)height=Math.max(height,r.top);
      }
    }
    return height;
  }
  function addTriangle(points,top){
    const area=(points[1].x-points[0].x)*(points[2].z-points[0].z)-(points[1].z-points[0].z)*(points[2].x-points[0].x);
    if(Math.abs(area)<.00001)return;
    const surface={points,top},xs=points.map(p=>p.x),zs=points.map(p=>p.z);
    for(let ix=Math.floor(Math.min(...xs)/cellSize);ix<=Math.floor(Math.max(...xs)/cellSize);ix++)for(let iz=Math.floor(Math.min(...zs)/cellSize);iz<=Math.floor(Math.max(...zs)/cellSize);iz++){
      const key=`${ix},${iz}`;if(!cells.has(key))cells.set(key,[]);cells.get(key).push(surface);
    }
  }
  return {addRectangle,addTriangle,heightAt};
}
