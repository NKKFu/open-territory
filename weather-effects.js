// A local volume around the active eye also follows remote cameras and boats.
// The lines end on the highest roof or surface under either end of their streak.
export function createRainField(T,scene,cover,groundHeight){
  const capacity=660,span=34,vertical=19,seeds=new Float32Array(capacity*4);
  let seed=74021;
  for(let i=0;i<seeds.length;i++){seed=(1664525*seed+1013904223)>>>0;seeds[i]=seed/4294967296;}
  const vertices=new Float32Array(capacity*6),colors=new Float32Array(capacity*8),geometry=new T.BufferGeometry();
  const positions=new T.BufferAttribute(vertices,3).setUsage(T.DynamicDrawUsage);
  const tones=new T.BufferAttribute(colors,4).setUsage(T.DynamicDrawUsage);colors.fill(1);
  geometry.setAttribute('position',positions);geometry.setAttribute('color',tones);geometry.setDrawRange(0,0);
  const material=new T.LineBasicMaterial({color:0xb8cbd1,vertexColors:true,transparent:true,depthWrite:false,toneMapped:false});
  const lines=new T.LineSegments(geometry,material);
  lines.name='Chuva';lines.frustumCulled=false;lines.visible=false;scene.add(lines);
  const dayTone=new T.Color('#c0d2d8'),nightTone=new T.Color('#95b0bf');
  const mod=(v,period)=>v-period*Math.floor(v/period);
  let windX=0,windZ=0,fall=0,count=0,clipped=0,eyeSheltered=false;
  return {
    update(dt,eye,state,night,quality,software){
      count=0;clipped=0;eyeSheltered=!cover.exposed(eye.x,eye.z,eye.y);
      if(state.rain<.016){lines.visible=false;geometry.setDrawRange(0,0);return;}
      const speed=12+state.rain*10;
      windX=mod(windX+state.windX*dt*.7,span);windZ=mod(windZ+state.windZ*dt*.7,span);
      fall=mod(fall+dt*speed,vertical);
      const budget=quality==='low'?215:quality==='high'?capacity:software?340:515;
      const drops=Math.ceil(budget*(.15+.85*state.rain)),top=eye.y+10,spanHalf=span/2;
      for(let i=0;i<drops;i++){
        const j=i*4,tipX=eye.x+mod(seeds[j]*span+windX-eye.x+spanHalf,span)-spanHalf;
        const tipZ=eye.z+mod(seeds[j+1]*span+windZ-eye.z+spanHalf,span)-spanHalf;
        const tipY=top-mod(seeds[j+2]*vertical+fall+top,vertical);
        const length=.23+state.rain*.33+seeds[j+3]*.33,dx=state.windX*.7*length/speed,dz=state.windZ*.7*length/speed;
        const highY=tipY+length;
        const headX=tipX-dx,headZ=tipZ-dz;
        const limit=Math.max(groundHeight(tipX,tipZ),groundHeight(headX,headZ),cover.roofHeight(tipX,tipZ),cover.roofHeight(headX,headZ))+.025;
        if(tipY<limit)clipped++;
        if(highY<=limit)continue;
        const distance2=(tipX-eye.x)**2+(tipY-eye.y)**2+(tipZ-eye.z)**2;
        if(distance2<2.25)continue;
        const fraction=Math.max(0,(limit-tipY)/length);
        const lowX=tipX-dx*fraction,lowZ=tipZ-dz*fraction,lowY=Math.max(tipY,limit);
        if((fraction>0&&lowY<groundHeight(lowX,lowZ)+.014)||cover.blocksStreak(lowX,lowY,lowZ,headX,highY,headZ)){clipped++;continue;}
        const v=count*6,k=count++*8;
        vertices[v]=lowX;vertices[v+1]=lowY;vertices[v+2]=lowZ;
        vertices[v+3]=headX;vertices[v+4]=highY;vertices[v+5]=headZ;
        colors[k+3]=.70+.27*seeds[j+3];colors[k+7]=.24+.16*seeds[j+1];
      }
      material.opacity=(.34+.48*state.rain)*(1-night*.22);
      material.color.copy(dayTone).lerp(nightTone,night);
      positions.needsUpdate=true;tones.needsUpdate=true;geometry.setDrawRange(0,count*2);lines.visible=count>0;
    },
    snapshot:()=>({count,clipped,eyeSheltered,budget:capacity})
  };
}
