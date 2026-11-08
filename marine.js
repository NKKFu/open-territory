// Heights are relative to the same sea-level datum as the city. The narrow
// sand shelf lets a swimmer stand up and walk out anywhere along the beach.
export const seaLevel=-.28,shoreZ=-165.2,seaBounds={left:-209,right:209,outer:-248};
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
const damp=(a,b,rate,dt)=>a+(b-a)*(1-Math.exp(-rate*dt));
export function seabed(x,z){const offshore=Math.max(0,shoreZ-z);
  return -.19-Math.min(10,offshore*(.30+.028*Math.sin(x*.06))+Math.max(0,offshore-8)*.12);}
export function waveHeight(x,z,t,state=0){
  const shoal=clamp((shoreZ-z)/8,0,1),amplitude=(.042+clamp(state,0,1)*.125)*shoal;
  return seaLevel+amplitude*(Math.sin(x*.22+z*.26+t*1.37)+.45*Math.sin(z*.43-x*.12+t*1.84));
}
const profiles={launch:{w:2.38,d:5.8,top:12,turn:1.15,draft:.68},fishing:{w:2.65,d:6.7,top:8.2,turn:.93,draft:.90},
  rescue:{w:2.55,d:6.3,top:13,turn:1.17,draft:.72},sail:{w:2.42,d:7.2,top:7.1,turn:.86,draft:1.05}};
function frame(x,z,yaw){return {x,z,c:Math.cos(yaw),s:Math.sin(yaw)};}
function local(f,x,z){const dx=x-f.x,dz=z-f.z;return {x:f.c*dx-f.s*dz,z:f.s*dx+f.c*dz};}
function world(f,x,z){return {x:f.x+f.c*x+f.s*z,z:f.z-f.s*x+f.c*z};}
function rectangle(f,w,d){return {...f,w,d};}
function overlaps(a,b){
  const dx=b.x-a.x,dz=b.z-a.z;
  for(const [x,z] of [[a.c,-a.s],[a.s,a.c],[b.c,-b.s],[b.s,b.c]]){
    const length=(o)=>Math.abs(x*o.c-z*o.s)*o.w/2+Math.abs(x*o.s+z*o.c)*o.d/2;
    if(Math.abs(dx*x+dz*z)>=length(a)+length(b))return false;
  }
  return true;
}

export function createMarine({player,buildBoat,weather=()=>({}),landBlocked=()=>false,onChange=()=>{},sound=()=>{}}){
  const docks=[],ladders=[],boats=[],buoys=[];
  let mode='land',activeBoat=null,time=0,seaState=0,vx=0,vz=0,stroke=0,swimEffort=0,cooldown=0,collisions=0,lastWake=0;
  let climb=null,wake=null,lastExit=null;
  function addDock(d){const o={...d,...rectangle(frame(d.x,d.z,d.heading||0),d.w,d.d)};docks.push(o);return o;}
  function addLadder(l){ladders.push(l);return l;}
  function addBoat(spec){const profile=profiles[spec.model]||profiles.launch;
    const b={...profile,...spec,heading:spec.heading||0,speed:0,turnRate:0,exitRequested:false,moored:true};
    b.g=buildBoat(b);b.g.position.set(spec.x,seaLevel,spec.z);b.g.rotation.y=b.heading;boats.push(b);return b;}
  function addBuoy(x,z,r=.45){buoys.push({x,z,r});}
  function dockAt(x,z,margin=0){return docks.find(d=>{const p=local(d,x,z);return Math.abs(p.x)<=d.w/2+margin&&Math.abs(p.z)<=d.d/2+margin;});}
  function inSea(x,z){return z<shoreZ&&x>seaBounds.left&&x<seaBounds.right&&z>seaBounds.outer;}
  function depth(x,z){return Math.max(0,seaLevel-seabed(x,z));}
  function surface(x,z){return waveHeight(x,z,time,seaState);}
  function hull(b,x=b.g.position.x,z=b.g.position.z,yaw=b.heading){return rectangle(frame(x,z,yaw),b.w,b.d);}
  function nearHull(b,x,z){const p=local(hull(b),x,z);return Math.hypot(Math.max(0,Math.abs(p.x)-b.w/2),Math.max(0,Math.abs(p.z)-b.d/2));}
  function swimmerClear(x,z,r=.42){return x>seaBounds.left+r&&x<seaBounds.right-r&&z>seaBounds.outer+r&&!dockAt(x,z,r)&&
    !boats.some(b=>nearHull(b,x,z)<r)&&!buoys.some(b=>Math.hypot(x-b.x,z-b.z)<r+b.r)&&!landBlocked(x,z,surface(x,z));}
  function hullClear(b,x,z,yaw){
    const f=hull(b,x,z,yaw);
    for(const side of [-1,1])for(const end of [-1,1]){
      const p=world(f,side*b.w/2,end*b.d/2);
      if(!inSea(p.x,p.z)||depth(p.x,p.z)<b.draft||p.x<seaBounds.left+.15||p.x>seaBounds.right-.15||p.z<seaBounds.outer+.15)return false;
    }
    if(docks.some(d=>overlaps(f,d))||boats.some(o=>o!==b&&overlaps(f,hull(o))))return false;
    return !buoys.some(o=>{const p=local(f,o.x,o.z);return Math.hypot(Math.max(0,Math.abs(p.x)-b.w/2),Math.max(0,Math.abs(p.z)-b.d/2))<o.r;});
  }
  function change(next){if(mode===next)return;const old=mode;mode=next;onChange(next,old);}
  function nearestAction(){
    const p=player.position;
    if(climb)return null;
    if(activeBoat)return activeBoat.exitRequested?null:{type:'marine-exit',data:activeBoat};
    if(p.z>shoreZ+8)return null;
    let nearest=null,best=2.05;
    if(mode==='swim')for(const l of ladders){const d=Math.hypot(l.x-p.x,l.z-p.z);
      if(d<best&&!landBlocked(l.landX,l.landZ,l.top)){best=d;nearest={type:'marine-ladder',data:l};}}
    for(const b of boats){const d=nearHull(b,p.x,p.z),deck=dockAt(p.x,p.z,.08);
      if(d<best&&(mode==='swim'||deck)&&Math.abs(b.speed)<1.25&&(!deck||Math.hypot(p.x-b.g.position.x,p.z-b.g.position.z)<b.d/2+2)){
        best=d;nearest={type:'marine-board',data:b};}}
    return nearest;
  }
  function sitAtHelm(b){const p=world(hull(b),b.model==='sail'?0:.43,b.model==='fishing'?.47:-.40);
    player.position.set(p.x,b.g.position.y+.24,p.z);player.rotation.y=b.heading;}
  function leaveBoat(){
    const b=activeBoat,f=hull(b),p=b.g.position,candidates=[];
    for(const d of docks){const v=local(d,p.x,p.z);
      for(const along of [0,1.25,-1.25]){const c=world(d,clamp(v.x,-d.w/2+.62,d.w/2-.62),clamp(v.z+along,-d.d/2+.62,d.d/2-.62));
        if(nearHull(b,c.x,c.z)<1.72&&!landBlocked(c.x,c.z,d.top))candidates.push({...c,y:d.top,mode:'land'});}}
    if(!candidates.length){
      for(const sign of [1,-1])for(const along of [-.65,.8,-b.d*.43]){
        const c=world(f,sign*(b.w/2+.69),along);if(swimmerClear(c.x,c.z)&&depth(c.x,c.z)>.75)candidates.push({...c,y:surface(c.x,c.z)-1.56,mode:'swim'});}
    }
    if(!candidates.length){b.exitRequested=false;return false;}
    candidates.sort((a,c)=>Math.hypot(a.x-player.position.x,a.z-player.position.z)-Math.hypot(c.x-player.position.x,c.z-player.position.z));
    const c=candidates[0];b.speed=0;b.turnRate=0;b.exitRequested=false;b.moored=true;activeBoat=null;vx=vz=0;swimEffort=0;
    player.position.set(c.x,c.y,c.z);lastExit=c.mode;cooldown=time+.25;change(c.mode);sound(c.mode==='swim'?'splash':'mooring',c.x,c.z);return true;
  }
  function interact(action){
    if(!action)return false;
    if(action.type==='marine-exit'&&activeBoat){if(Math.abs(activeBoat.speed)>.70){activeBoat.exitRequested=true;return true;}return leaveBoat();}
    if(action.type==='marine-ladder'&&mode==='swim'){
      const l=action.data;if(landBlocked(l.landX,l.landZ,l.top))return false;
      climb={l,x:player.position.x,y:player.position.y,z:player.position.z,t:0};vx=vz=0;return true;
    }
    if(action.type!=='marine-board'||time<cooldown||!boats.includes(action.data)||action.data!==nearestAction()?.data)return false;
    activeBoat=action.data;activeBoat.moored=false;activeBoat.exitRequested=false;vx=vz=0;climb=null;change('boat');sitAtHelm(activeBoat);sound('mooring',player.position.x,player.position.z);return true;
  }
  function updateBoat(b,dt,input,wind){
    const p=b.g.position;
    if(b===activeBoat){
      const throttle=b.exitRequested||input.brake?0:input.forward||0;
      const desired=throttle*(throttle<0?b.top*.33:b.top)*(1-seaState*.15);
      b.speed=damp(b.speed,desired,b.exitRequested||input.brake?1.85:throttle?.65:.30,dt);
      b.turnRate=damp(b.turnRate,-(input.sideways||0)*b.turn*clamp(Math.abs(b.speed)/3.4,0,1)*Math.sign(b.speed),2.9,dt);
      const yaw=b.heading+b.turnRate*dt;
      if(hullClear(b,p.x,p.z,yaw))b.heading=yaw;else b.turnRate=0;
      const drift=clamp(Math.abs(b.speed)/3,0,1),dx=(Math.sin(b.heading)*b.speed+(wind.windX||0)*.018*drift)*dt;
      const dz=(Math.cos(b.heading)*b.speed+(wind.windZ||0)*.018*drift)*dt;
      if(hullClear(b,p.x+dx,p.z+dz,b.heading)){p.x+=dx;p.z+=dz;}
      else{const a=Math.abs(b.speed);if(hullClear(b,p.x+dx,p.z,b.heading))p.x+=dx;else if(hullClear(b,p.x,p.z+dz,b.heading))p.z+=dz;
        b.speed*=Math.exp(-8*dt);if(a>1.2&&time>(b.lastContact||0)+.7){collisions++;b.lastContact=time;sound('mooring',p.x,p.z);}}
      if(Math.abs(b.speed)>1.2&&time-lastWake>.16){const stern=world(hull(b),0,-Math.sign(b.speed)*b.d*.49);wake={...stern,heading:b.heading+(b.speed<0?Math.PI:0),speed:Math.abs(b.speed),width:b.w,time};lastWake=time;}
    }
    const sin=Math.sin(b.heading),cos=Math.cos(b.heading),ahead=surface(p.x+sin*1.9,p.z+cos*1.9),behind=surface(p.x-sin*1.9,p.z-cos*1.9);
    p.y=(ahead+behind+surface(p.x,p.z)) /3;
    b.g.rotation.set(-Math.atan2(ahead-behind,3.8)-clamp(Math.abs(b.speed)*.004,0,.05),b.heading,
      .7*Math.atan2(surface(p.x+cos,p.z-sin)-surface(p.x-cos,p.z+sin),2)-b.turnRate*b.speed*.009,'YXZ');
    if(b===activeBoat){sitAtHelm(b);if(b.exitRequested&&Math.abs(b.speed)<.62)leaveBoat();}
  }
  function update(dt,now,input={}){
    time=now;const wind=weather()||{};seaState=clamp(wind.seaState||0,0,1);
    const wasBoat=!!activeBoat;for(const b of boats)updateBoat(b,dt,input,wind);
    if(wasBoat)return true;
    if(input.suspended)return false;
    const p=player.position;
    if(mode==='land'){
      if(time<cooldown||!inSea(p.x,p.z)||dockAt(p.x,p.z)||depth(p.x,p.z)<1.0||p.y>surface(p.x,p.z)+.28)return false;
      vx=vz=0;swimEffort=0;change('swim');sound('splash',p.x,p.z);
    }
    if(climb){
      const c=climb;c.t=Math.min(1,c.t+dt/.68);const f=c.t*c.t*(3-2*c.t);
      p.set(c.x+(c.l.landX-c.x)*f,c.y+(c.l.top-c.y)*Math.sin(c.t*Math.PI/2),c.z+(c.l.landZ-c.z)*f);
      player.rotation.y=Math.atan2(c.l.landX-c.x,c.l.landZ-c.z);
      if(c.t===1){p.set(c.l.landX,c.l.top,c.l.landZ);climb=null;lastExit='ladder';cooldown=time+.3;change('land');}return true;
    }
    const x=input.moveX||0,z=input.moveZ||0,length=Math.hypot(x,z),speed=input.boost?3.45:2.32;
    swimEffort=damp(swimEffort,Math.min(1,length),3.6,dt);stroke+=dt*(1.5+swimEffort*(input.boost?5.2:3.65));
    vx=damp(vx,(length?x/length:0)*speed+(wind.windX||0)*.009,4,dt);
    vz=damp(vz,(length?z/length:0)*speed+(wind.windZ||0)*.009,4,dt);
    if(swimmerClear(p.x+vx*dt,p.z))p.x+=vx*dt;else vx=0;
    if(swimmerClear(p.x,p.z+vz*dt))p.z+=vz*dt;else vz=0;
    if(length){const error=Math.atan2(x,z)-player.rotation.y;player.rotation.y+=Math.atan2(Math.sin(error),Math.cos(error))*(1-Math.exp(-dt*6));}
    if(depth(p.x,p.z)<.62){p.y=seabed(p.x,p.z);lastExit='beach';cooldown=time+.26;change('land');return true;}
    p.y=damp(p.y,surface(p.x,p.z)-1.63+swimEffort*.39+.035*Math.sin(stroke),7,dt);
    if(time-lastWake>(swimEffort>.35?.42:1.25)){wake={x:p.x,z:p.z,heading:player.rotation.y,speed:swimEffort*1.4,width:0,time};lastWake=time;
      if(swimEffort>.5)sound('stroke',p.x,p.z);}
    return true;
  }
  function fall(){if(activeBoat)leaveBoat();climb=null;}
  return {boats,docks,ladders,buoys,addDock,addLadder,addBoat,addBuoy,dockAt,inSea,depth,surface,hullClear,nearHull,nearestAction,interact,update,fall,
    get mode(){return mode;},get activeBoat(){return activeBoat;},get stroke(){return stroke;},get effort(){return swimEffort;},get climb(){return climb;},get wake(){return wake;},
    snapshot:()=>({mode,boat:activeBoat?.name||null,speed:activeBoat?.speed||0,heading:activeBoat?.heading??player.rotation.y,depth:depth(player.position.x,player.position.z),
      surface:surface(player.position.x,player.position.z),seaState,effort:swimEffort,stroke,climbing:!!climb,lastExit,collisions,wake:wake?.time??null,
      fleet:boats.map(b=>({name:b.name,model:b.model,x:b.g.position.x,y:b.g.position.y,z:b.g.position.z,yaw:b.heading,moored:b.moored}))})};
}
