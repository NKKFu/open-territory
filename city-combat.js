const weapons={
  pulse:{id:'pulse',name:'Pulsor Maré',capacity:1,initialReserve:8,maxReserve:12,interval:.65,reload:1.1,range:18,damage:0,stun:4},
  sidearm:{id:'sidearm',name:'P-9 Farol',capacity:8,initialReserve:16,maxReserve:64,interval:.29,reload:1.45,range:52,damage:36},
  carbine:{id:'carbine',name:'C-12 Orla',capacity:16,initialReserve:32,maxReserve:96,interval:.15,reload:1.85,range:65,damage:25,automatic:true}
};
export const weaponIds=Object.keys(weapons);
const clamp=(v,lo,hi)=>Math.max(lo,Math.min(hi,v));
const amount=(v,max)=>Number.isFinite(v)?clamp(Math.floor(v),0,max):0;

// Intersect a unit ray with oriented cover. A shot beginning inside a wall is blocked immediately.
export function coverDistance(origin,dir,box,limit){
  const dx=origin.x-box.x,dz=origin.z-box.z,c=box.cos??1,s=box.sin??0;
  const axes=[[dx*c-dz*s,dir.x*c-dir.z*s,-box.w/2,box.w/2],[origin.y,dir.y,box.bottom,box.top],
    [dx*s+dz*c,dir.x*s+dir.z*c,-box.d/2,box.d/2]];
  let lo=0,hi=limit;
  for(const [pos,speed,min,max] of axes){
    if(Math.abs(speed)<1e-8){if(pos<min||pos>max)return Infinity;continue;}
    const a=(min-pos)/speed,b=(max-pos)/speed;lo=Math.max(lo,Math.min(a,b));hi=Math.min(hi,Math.max(a,b));
    if(lo>hi)return Infinity;
  }
  return lo;
}

function bodyDistance(origin,dir,actor){
  const p=actor.g.position,scale=actor.g.scale?.y??1,r=.44*scale,h=(actor.sitting?1.75:2.36)*scale;
  const x=origin.x-p.x,z=origin.z-p.z,a=dir.x*dir.x+dir.z*dir.z,b=x*dir.x+z*dir.z,c=x*x+z*z-r*r;
  let enter=0,exit=Infinity;
  if(a<1e-8){if(c>0)return Infinity;}
  else{const disc=b*b-a*c;if(disc<0)return Infinity;const root=Math.sqrt(disc);enter=Math.max(0,(-b-root)/a);exit=(-b+root)/a;}
  if(Math.abs(dir.y)<1e-8){if(origin.y<p.y||origin.y>p.y+h)return Infinity;}
  else{const t0=(p.y-origin.y)/dir.y,t1=(p.y+h-origin.y)/dir.y;enter=Math.max(enter,Math.min(t0,t1));exit=Math.min(exit,Math.max(t0,t1));}
  return enter<=exit?enter:Infinity;
}

export function traceShot(origin,dir,limit,cover,actors,groundHeight){
  let distance=limit,kind='air',actor=null,obstacle=null;
  for(const box of cover){const d=coverDistance(origin,dir,box,distance);if(d<=distance){distance=d;kind='cover';obstacle=box;}}
  for(const candidate of actors){
    if(candidate.ragdoll||candidate.inBus||candidate.exitingVehicle||candidate.g.visible===false)continue;
    const d=bodyDistance(origin,dir,candidate);if(d<distance-.002){distance=d;kind='actor';actor=candidate;}
  }
  if(groundHeight){
    let previous=0;
    for(let t=0;t<=distance+.499;t+=.5){
      const end=Math.min(t,distance);
      if(origin.y+dir.y*end<=groundHeight(origin.x+dir.x*end,origin.z+dir.z*end)){
        let lo=previous,hi=end;for(let i=0;i<5;i++){const m=(lo+hi)/2;
          if(origin.y+dir.y*m>groundHeight(origin.x+dir.x*m,origin.z+dir.z*m))lo=m;else hi=m;}
        distance=hi;kind='ground';actor=null;break;
      }
      if(end===distance)break;previous=end;
    }
  }
  return {kind,actor,cover:kind==='cover'?obstacle:null,distance,point:{x:origin.x+dir.x*distance,y:origin.y+dir.y*distance,z:origin.z+dir.z*distance}};
}

export function createCombat({player,onShot=()=>{},onState=()=>{},onDamage=()=>{},onRecover=()=>{}}){
  const loadout=new Map(),health=new Map();
  let time=0,equipped=null,aiming=false,held=false,reloading=0,nextShot=0,dry=false,shots=0;
  function grant(id){
    const w=weapons[id];if(!Object.hasOwn(weapons,id)||loadout.has(id))return false;
    loadout.set(id,{loaded:w.capacity,reserve:w.initialReserve});return true;
  }
  grant('pulse');
  function weaponInfo(id){const w=weapons[id];return Object.hasOwn(weapons,id)?{...w,...(loadout.get(id)||{loaded:0,reserve:0}),owned:loadout.has(id)}:null;}
  function release(){held=false;dry=false;}
  function aim(value){aiming=!!(value&&equipped&&!isDown());if(!aiming)release();return aiming;}
  function holster(){equipped=null;reloading=0;aim(false);}
  function select(id){if(!loadout.has(id)||isDown())return false;
    if(equipped!==id){aim(false);reloading=0;equipped=id;}onState('equip',weaponInfo(id));return true;}
  function remove(id){if(id==='pulse'||!loadout.has(id))return false;if(equipped===id)holster();return loadout.delete(id);}
  function addAmmo(id,n){const item=loadout.get(id);if(!item)return 0;const added=Math.min(amount(n,9999),weapons[id].maxReserve-item.reserve);item.reserve+=added;return added;}
  function reload(){const item=loadout.get(equipped),w=weapons[equipped];
    if(!item||reloading||isDown()||item.loaded===w.capacity)return false;
    if(!item.reserve){onState('empty',weaponInfo(equipped));return false;}
    release();reloading=time+w.reload;onState('reload',weaponInfo(equipped));return true;
  }
  function fire(){
    if(!aiming||reloading||time<nextShot||isDown())return false;
    const item=loadout.get(equipped),w=weapons[equipped];
    if(!item.loaded){if(!dry){onState('empty',weaponInfo(equipped));dry=true;}return false;}
    item.loaded--;shots++;nextShot=time+w.interval;
    onState(item.loaded?'shot':'last',weaponInfo(equipped));onShot(w);return true;
  }
  function trigger(value){if(!value){release();return false;}if(held||!aiming)return false;held=true;return fire();}
  function isDown(actor=player){return (health.get(actor)?.downUntil||0)>time;}
  function canTalk(actor){const state=health.get(actor);
    return !!actor&&!actor.ragdoll&&!isDown(actor)&&(state?.hp??100)>=100&&(state?.fearUntil??0)<=time;}
  function startle(actor,seconds=4){
    if(!actor||actor===player||actor.ragdoll)return;
    const state=health.get(actor)||{hp:100,downUntil:0,safeUntil:0};
    state.fearUntil=Math.max(state.fearUntil||0,time+seconds);health.set(actor,state);
  }
  function damage(actor,value,{stun=0,source=null}={}){
    if(!actor||isDown(actor)||(!stun&&!(value>0)))return false;
    const state=health.get(actor)||{hp:100,downUntil:0,safeUntil:0};if(time<state.safeUntil)return false;
    if(!stun)state.hp=Math.max(0,state.hp-amount(value,100));state.lastHit=time;
    const down=stun>0||state.hp===0;if(down)state.downUntil=time+(stun||7);
    health.set(actor,state);if(actor===player&&down)holster();onDamage(actor,{hp:state.hp,down,stun:!!stun,source});return true;
  }
  function update(dt){
    time+=dt;
    for(const [actor,s] of health){
      if(s.downUntil){if(time<s.downUntil)continue;s.downUntil=0;s.safeUntil=time+1.5;if(!s.hp)s.hp=54;onRecover(actor,s.hp);}
      if(time-s.lastHit>7&&s.hp<100)s.hp=Math.min(100,s.hp+dt*7);
      if(s.hp===100&&time>=s.safeUntil&&time>=(s.fearUntil||0))health.delete(actor);
    }
    if(reloading&&time>=reloading){const i=loadout.get(equipped),n=Math.min(i.reserve,weapons[equipped].capacity-i.loaded);
      i.loaded+=n;i.reserve-=n;reloading=0;onState('ready',weaponInfo(equipped));}
    if(held&&weapons[equipped]?.automatic)fire();
  }
  function restoreLoadout(saved){
    holster();loadout.clear();grant('pulse');if(!saved||typeof saved!=='object')return;
    for(const id of weaponIds){const entry=saved[id];if(!entry||typeof entry!=='object')continue;grant(id);
      const w=weapons[id];loadout.set(id,{loaded:amount(entry.loaded,w.capacity),reserve:amount(entry.reserve,w.maxReserve)});}
  }
  function healAtClinic(){const s=health.get(player);if(!s||isDown()||player.ragdoll)return 0;const healed=Math.ceil(100-s.hp);
    if(healed){s.hp=100;onRecover(player,100);}return healed;}
  return {grant,remove,owns:id=>loadout.has(id),select,holster,aim,trigger,reload,damage,startle,update,weaponInfo,addAmmo,
    isDown,canTalk,healAtClinic,isThreatening:()=>aiming,hp:(actor=player)=>Math.round(health.get(actor)?.hp??100),
    exportLoadout:()=>Object.fromEntries([...loadout].map(([id,item])=>[id,{...item}])),restoreLoadout,
    get equipped(){return equipped;},get aiming(){return aiming;},get reloading(){return !!reloading;},
    snapshot:()=>({equipped,aiming,reloading:!!reloading,shots,hp:Math.round(health.get(player)?.hp??100),down:isDown(),loadout:Object.fromEntries([...loadout].map(([id,v])=>[id,{...v}]))})};
}
