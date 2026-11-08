// One simulation clock drives the sky, sea, roads and the forecast at the quay.
const fronts=[
  {id:'clear',seconds:78},{id:'mist',seconds:52},{id:'rain',seconds:68},
  {id:'squall',seconds:40},{id:'rain',seconds:47}
];
const climate={
  clear:{label:'Céu aberto',rain:0,fog:.035,cloud:.07,windX:.85,windZ:1.75,seaState:.13},
  mist:{label:'Bruma marítima',rain:0,fog:.91,cloud:.58,windX:.35,windZ:1.25,seaState:.18},
  rain:{label:'Chuva',rain:.62,fog:.46,cloud:.86,windX:2.65,windZ:3.8,seaState:.53},
  squall:{label:'Aguaceiro',rain:1,fog:.70,cloud:.99,windX:6.7,windZ:5.8,seaState:.93}
};
const fields=['rain','fog','cloud','windX','windZ','seaState'];
const clamp=(v,lo,hi)=>Math.min(hi,Math.max(lo,v));

export function createCityWeather(initial='clear'){
  let index=Math.max(0,fronts.findIndex(f=>f.id===initial)),age=0,elapsed=0,blendTime=0;
  const id=fronts[index].id,base=climate[id],state={...base,id,wetness:base.rain*.92,windSpeed:0,transition:1};
  let from={...base};
  function sample(){
    const id=fronts[index].id,target=climate[id],t=blendTime?clamp(age/blendTime,0,1):1;
    const mix=t*t*(3-2*t);
    state.id=id;state.label=target.label;state.transition=t;
    for(const k of fields)state[k]=from[k]+(target[k]-from[k])*mix;
    const gust=1+(.05+state.rain*.13)*Math.sin(elapsed*.43)*Math.sin(elapsed*.17);
    state.windX*=gust;state.windZ*=gust;state.windSpeed=Math.hypot(state.windX,state.windZ);
  }
  function change(next){
    from={};
    // Gusts ride on top of the shared wind field, so they must not be applied twice.
    const gust=1+(.05+state.rain*.13)*Math.sin(elapsed*.43)*Math.sin(elapsed*.17);
    for(const k of fields)from[k]=(k==='windX'||k==='windZ')?state[k]/gust:state[k];
    index=next;age=0;blendTime=fronts[index].id==='squall'?14:20;sample();
  }
  sample();
  return {
    state,
    update(dt){
      if(!Number.isFinite(dt)||dt<=0)return;
      while(dt>1e-8){
        const step=Math.min(dt,fronts[index].seconds-age,1);
        age+=step;elapsed+=step;sample();
        const target=state.rain>.08?Math.min(1,state.rain*1.34):Math.max(0,state.fog-.42)*.07;
        const rate=target>state.wetness?.051:.008*(1.12-state.cloud*.67);
        state.wetness+=(target-state.wetness)*(1-Math.exp(-rate*step));
        dt-=step;if(age>=fronts[index].seconds-1e-8)change((index+1)%fronts.length);
      }
    },
    request(id){
      const next=fronts.findIndex(f=>f.id===id);if(next<0)return false;
      if(fronts[index].id!==id)change(next);return true;
    },
    forecast(){return {current:state.label,next:climate[fronts[(index+1)%fronts.length].id].label,
      secondsToChange:Math.max(0,Math.ceil(fronts[index].seconds-age))};}
  };
}

export function wetRoadGrip(base,wetness,exposure){
  const loss=base>=.9?.29:base>.5?.18:.095;
  return base*(1-loss*clamp(wetness,0,1)*clamp(exposure,0,1));
}

// Static roof footprints are reused for drop clipping, tire contact and the
// pavement material. A point above the roof still receives rain.
export function createRainCover(obstacles,groundHeight){
  const cells=new Map(),walls=new Map(),roofs=[],cell=12;
  function inRoof(x,z,r){
    const dx=x-r.x,dz=z-r.z;
    return Math.abs(dx*r.c-dz*r.s)<=r.w/2&&Math.abs(dx*r.s+dz*r.c)<=r.d/2;
  }
  function index(map,r){
    for(let i=Math.floor((r.x-r.rx)/cell);i<=Math.floor((r.x+r.rx)/cell);i++)for(let j=Math.floor((r.z-r.rz)/cell);j<=Math.floor((r.z+r.rz)/cell);j++){
      const key=i*1024+j;if(!map.has(key))map.set(key,[]);map.get(key).push(r);
    }
  }
  for(const b of obstacles){
    if(!Number.isFinite(b.top)||!Number.isFinite(b.bottom))continue;
    const c=b.cos??1,s=b.sin??0,rx=(Math.abs(c)*b.w+Math.abs(s)*b.d)/2,rz=(Math.abs(s)*b.w+Math.abs(c)*b.d)/2;
    const r={x:b.x,z:b.z,w:b.w,d:b.d,source:b,c,s,rx,rz};
    if(b.w>=1.1&&b.d>=1.1&&b.top>=groundHeight(b.x,b.z)+1.7){
      r.roof=true;roofs.push(r);index(cells,r);index(walls,r);
    }else if(Math.max(b.w,b.d)>1&&b.top-b.bottom>.36)index(walls,r);
  }
  function roofHeight(x,z){
    let top=-Infinity;
    for(const r of cells.get(Math.floor(x/cell)*1024+Math.floor(z/cell))||[])if(r.source.top>top&&inRoof(x,z,r))top=r.source.top;
    return top;
  }
  // A vertical facade stops windblown drops but does not turn the pavement on
  // either side of it into a sheltered area (notably the outdoor cinema).
  function blocksStreak(ax,ay,az,bx,by,bz){
    for(let i=Math.floor(Math.min(ax,bx)/cell);i<=Math.floor(Math.max(ax,bx)/cell);i++){
      for(let j=Math.floor(Math.min(az,bz)/cell);j<=Math.floor(Math.max(az,bz)/cell);j++){
        for(const r of walls.get(i*1024+j)||[]){
          const bottom=r.roof?-Infinity:r.source.bottom,top=r.source.top;
          if(Math.max(ay,by)<bottom||Math.min(ay,by)>top)continue;
          const x=ax-r.x,z=az-r.z,dx=bx-ax,dz=bz-az;let near=0,far=1;
          function overlaps(p,v,lo,hi){
            if(Math.abs(v)<1e-8)return p>=lo&&p<=hi;
            const a=(lo-p)/v,b=(hi-p)/v;near=Math.max(near,Math.min(a,b));far=Math.min(far,Math.max(a,b));return near<=far;
          }
          if(overlaps(x*r.c-z*r.s,dx*r.c-dz*r.s,-r.w/2,r.w/2)&&overlaps(x*r.s+z*r.c,dx*r.s+dz*r.c,-r.d/2,r.d/2)&&
            overlaps(ay,by-ay,bottom,top))return true;
        }
      }
    }
    return false;
  }
  return {roofHeight,blocksStreak,exposed:(x,z,y)=>y>=roofHeight(x,z)-.015,
    pavementMask(size=512){
      const mask=new Uint8Array(size*size);mask.fill(255);const scale=size/512;
      for(const r of roofs){
        const sx=clamp(Math.floor((r.x-r.rx+256)*scale),0,size),ex=clamp(Math.ceil((r.x+r.rx+256)*scale),0,size);
        const sz=clamp(Math.floor((r.z-r.rz+256)*scale),0,size),ez=clamp(Math.ceil((r.z+r.rz+256)*scale),0,size);
        for(let j=sz;j<ez;j++)for(let i=sx;i<ex;i++)if(inRoof((i+.5)/scale-256,(j+.5)/scale-256,r))mask[j*size+i]=0;
      }
      return mask;
    }
  };
}
