// Small, interruptible reactions use the real residents and their existing paths.
// No destination or body position is changed here. Perception runs at 5 Hz;
// active poses and higher-priority interruptions are checked every simulation step.
const distance2=(a,b)=>(a.x-b.x)**2+(a.z-b.z)**2;
const clamp=(n,a,b)=>Math.max(a,Math.min(b,n));
export function umbrellaSpace(p){
  const pos=p.g.position,angle=p.g.rotation.y,s=p.g.scale.y||1,c=Math.cos(angle),n=Math.sin(angle);
  return {x:pos.x+(c*.4+n*.24)*s,z:pos.z+(-n*.4+c*.24)*s,
    y:pos.y+2.49*s,height:.34*s,radius:.86*s};
}
export function umbrellaClear(space,obstacles){
  return obstacles.every(o=>{
    if(space.y>=o.top-.01||space.y+space.height<=o.bottom+.01)return true;
    const dx=space.x-o.x,dz=space.z-o.z,c=o.cos??1,s=o.sin??0;
    const x=Math.max(0,Math.abs(dx*c-dz*s)-o.w/2),z=Math.max(0,Math.abs(dx*s+dz*c)-o.d/2);
    return x*x+z*z>=space.radius*space.radius;
  });
}

export function createCitySocial({people,focus,drone=()=>null,weather=()=>({rain:0}),music=()=>null,
  canReact=()=>true,canPause=()=>true,canSee=()=>true,exposed=()=>true,canOpenUmbrella=()=>true}){
  const records=new WeakMap(),events=new Map(),rainResidents=new Set(),owned=new Set();
  let serial=0,nextSense=0,now=0;
  const counts={drone:0,music:0,interrupted:0,completed:0,sightChecks:0};
  function record(p){
    if(!records.has(p)){const id=serial++;records.set(p,{id,phase:(id*.61803398875)%1,
      droneAt:0,musicAt:0,wet:0,umbrella:id%5!==1&&id%5!==4});}
    return records.get(p);
  }
  function available(p){
    return !p.ragdoll&&!p.inBus&&!p.busState&&!p.sitting&&!p.indoor&&!p.push&&!p.exitingVehicle&&
      !p.incidentCar&&!p.performance&&!p.role&&!p.stationary&&!p.counterWorker&&canReact(p);
  }
  function see(p,source){counts.sightChecks++;return canSee(p,source);}
  function end(p,interrupted=false){
    const event=events.get(p);if(!event)return;
    record(p)[event.type+'At']=now+(event.type==='drone'?28:20)+record(p).phase*12;
    events.delete(p);counts[interrupted?'interrupted':'completed']++;
    clear(p);
  }
  function clear(p){
    if(p.ambientPose?.owner==='city-social')delete p.ambientPose;
    delete p.ambientSpeed;owned.delete(p);
  }
  function setPose(p,kind,weight,source=null,beat=now){
    const r=record(p),pose=p.ambientPose?.owner==='city-social'?p.ambientPose:{};
    Object.assign(pose,{owner:'city-social',kind,weight,beat,phase:r.phase,
      lookX:source?.x,lookY:source?.y,lookZ:source?.z});
    p.ambientPose=pose;owned.add(p);
  }
  function wetResident(p,amount){
    if(amount<.18||!available(p)||distance2(p.g.position,focus())>46**2)return false;
    // A book, brush or cup is put away by the routine itself, not stolen by an
    // ambient hand pose. Travellers and people resting keep their regular route.
    if(p.lifeState==='activity'&&!['wait','rest','chat'].includes(p.lifeActivity))return false;
    return exposed(p);
  }
  function droneNear(p,source){
    if(!source?.active)return false;
    const pos=p.g.position,dy=source.y-pos.y,d2=distance2(pos,source);
    return d2>=1.4**2&&d2<14**2&&dy>.6&&dy<10.5;
  }
  function eventValid(p,event){
    if(!available(p)||!canPause(p)||distance2(p.g.position,focus())>48**2)return false;
    if(weather().rain>.3&&exposed(p))return false;
    const source=event.type==='drone'?drone():music();
    if(event.type==='drone'?!droneNear(p,source):!source?.active||source.id!==event.id||distance2(p.g.position,source)>9**2)return false;
    event.source=source;return true;
  }
  function start(p,type,source){
    const r=record(p),duration=type==='drone'?4.8+r.phase*1.6:8+r.phase*4;
    events.set(p,{type,id:source.id,source,start:now,duration,photograph:r.id%3!==1});
    counts[type]++;
  }
  function sense(){
    const eye=focus(),rain=weather().rain||0,aircraft=drone(),band=music();
    const nearby=people.filter(p=>{record(p);return distance2(p.g.position,eye)<48**2;})
      .sort((a,b)=>distance2(a.g.position,eye)-distance2(b.g.position,eye));
    rainResidents.clear();const canopies=[];
    for(const p of nearby){
      if(rainResidents.size>=24)break;
      if(!wetResident(p,rain))continue;
      rainResidents.add(p);const r=record(p);
      const space=umbrellaSpace(p),free=canopies.every(other=>distance2(space,other)>(space.radius+other.radius+.12)**2);
      r.rainKind=r.umbrella&&now>=(r.canopyAt||0)&&canopies.length<10&&free&&canOpenUmbrella(p,space)?'umbrella':'rainCover';
      if(r.rainKind==='umbrella')canopies.push(space);
    }
    for(const [p,event] of events){if(!eventValid(p,event)||!see(p,event.source))end(p,true);}
    let droneCount=[...events.values()].filter(e=>e.type==='drone').length;
    let musicCount=events.size-droneCount;
    // A close aircraft is a brief distraction for a spectator too; it does
    // not require creating extra people or cancelling the musician's set.
    for(const [p,event] of events){
      if(droneCount>=2)break;
      if(event.type==='music'&&now>=record(p).droneAt&&droneNear(p,aircraft)&&see(p,aircraft)){
        end(p,true);start(p,'drone',aircraft);droneCount++;musicCount--;
      }
    }
    for(const p of nearby){
      if(droneCount>=2&&musicCount>=3)break;
      if(events.has(p)||rainResidents.has(p)||!available(p)||!canPause(p))continue;
      const r=record(p);
      if(droneCount<2&&now>=r.droneAt&&droneNear(p,aircraft)&&see(p,aircraft)){
        start(p,'drone',aircraft);droneCount++;continue;
      }
      if(musicCount<3&&band?.active&&now>=r.musicAt&&distance2(p.g.position,band)<9**2&&see(p,band)){
        start(p,'music',band);musicCount++;
      }
    }
  }
  return {
    update(dt,time){
      if(!(dt>0))return;now=time;
      if(now>=nextSense){sense();nextSense=now+.2;}
      for(const p of [...owned]){
        if(!available(p)){end(p,true);rainResidents.delete(p);record(p).wet=0;clear(p);}
        else if(!events.has(p)&&!rainResidents.has(p)){record(p).wet=0;clear(p);}
      }
      const openCanopies=[];
      for(const p of rainResidents){
        const r=record(p);
        if(!wetResident(p,weather().rain||0)){r.wet=0;clear(p);continue;}
        if(events.has(p))end(p,true);
        if(r.rainKind==='umbrella'){
          const space=umbrellaSpace(p);
          if(!canOpenUmbrella(p,space)||openCanopies.some(other=>distance2(space,other)<(space.radius+other.radius+.12)**2)){
            r.rainKind='rainCover';r.canopyAt=now+.5;
          }else openCanopies.push(space);
        }
        r.wet+=(1-r.wet)*(1-Math.exp(-dt*4));
        setPose(p,r.rainKind,r.wet);
        p.ambientSpeed=r.rainKind==='umbrella'?1.08:1.24;
      }
      for(const [p,event] of events){
        const elapsed=now-event.start;
        if(elapsed>=event.duration){end(p);continue;}
        if(!eventValid(p,event)){end(p,true);continue;}
        const r=record(p),source=event.source;
        const kind=event.type==='drone'?(event.photograph&&elapsed>1.05&&elapsed<event.duration-.7?'photo':'look'):
          elapsed<1?'listen':(Math.floor((elapsed-1)/2.7)+r.id)%3===0?'clap':r.id%2?'dance':'listen';
        const weight=clamp(Math.min(elapsed/.42,(event.duration-elapsed)/.5),0,1);
        setPose(p,kind,weight,source,source.beat??now);
        const yaw=Math.atan2(source.x-p.g.position.x,source.z-p.g.position.z),delta=yaw-p.g.rotation.y;
        p.g.rotation.y+=Math.atan2(Math.sin(delta),Math.cos(delta))*(1-Math.exp(-dt*3.8));
      }
    },
    isPaused:p=>events.has(p),
    protectsFromRain:p=>rainResidents.has(p),
    interrupt(p){end(p,true);rainResidents.delete(p);record(p).wet=0;clear(p);},
    snapshot:()=>({...counts,active:[...events].map(([p,e])=>({name:p.name,type:e.type,kind:p.ambientPose?.kind,
      x:p.g.position.x,z:p.g.position.z,elapsed:now-e.start})),rain:[...rainResidents].filter(p=>p.ambientPose?.owner==='city-social')
      .map(p=>({name:p.name,kind:p.ambientPose.kind,x:p.g.position.x,z:p.g.position.z,pace:p.ambientSpeed}))})
  };
}

// Props are pooled independently from residents: at most ten canopies and two
// phones can exist, even after a long visit or repeated changes of district.
export function createSocialProps(T,{invalidate=()=>{}}={}){
  const pool={umbrella:[],photo:[]},attached=new Map(),materials=new Map();
  const cube=new T.BoxGeometry(1,1,1),desired=new T.Quaternion(),inverse=new T.Quaternion(),angles=new T.Euler();
  function material(color){
    if(!materials.has(color))materials.set(color,new T.MeshStandardMaterial({color,roughness:.8,side:T.DoubleSide}));
    return materials.get(color);
  }
  function box(parent,x,y,z,w,h,d,color){
    const mesh=new T.Mesh(cube,material(color));mesh.position.set(x,y,z);mesh.scale.set(w,h,d);parent.add(mesh);return mesh;
  }
  function make(kind,index){
    const g=new T.Group();g.name=kind==='umbrella'?'Guarda-chuva do morador':'Celular do observador';
    if(kind==='photo'){
      box(g,0,-.015,.035,.18,.31,.045,'#263b44');
      box(g,0,-.015,.010,.145,.255,.008,'#8bafb6');
      box(g,.054,.090,.061,.035,.035,.009,'#161f27');
    }else{
      box(g,0,.45,0,.035,1.02,.035,'#465458');
      box(g,0,-.06,0,.075,.17,.065,'#31434a');
      const colors=['#d6ac63','#4f8f91','#a56e78','#7288b3','#adba8c'];
      const positions=[],radius=.84,top=1.12,rim=.83;
      for(let i=0;i<8;i++){
        const a=i*Math.PI/4,b=(i+1)*Math.PI/4;
        positions.push(0,top,0,Math.sin(a)*radius,rim,Math.cos(a)*radius,Math.sin(b)*radius,rim,Math.cos(b)*radius);
      }
      const geometry=new T.BufferGeometry();geometry.setAttribute('position',new T.Float32BufferAttribute(positions,3));geometry.computeVertexNormals();
      const canopy=new T.Mesh(geometry,material(colors[index%colors.length]));canopy.castShadow=true;g.add(canopy);
      box(g,0,1.13,0,.07,.09,.07,'#31434a');
    }
    const item={g,kind,person:null};pool[kind].push(item);return item;
  }
  function detach(p,item){
    item.g.removeFromParent();item.g.visible=false;item.person=null;attached.delete(p);invalidate(p.g);
  }
  return {
    update(people){
      for(const [p,item] of attached){
        const pose=p.ambientPose;
        if(!p.g.visible||!pose||pose.owner!=='city-social'||pose.kind!==item.kind||pose.weight<.03)detach(p,item);
      }
      for(const p of people){
        const pose=p.ambientPose,hand=p.g.userData.hands?.[1];
        if(!hand||!p.g.visible||!pose||pose.owner!=='city-social'||!Object.hasOwn(pool,pose.kind)||pose.weight<.03)continue;
        let item=attached.get(p);
        if(!item){
          item=pool[pose.kind].find(prop=>!prop.person);
          if(!item&&pool[pose.kind].length<(pose.kind==='umbrella'?10:2))item=make(pose.kind,pool[pose.kind].length);
          if(!item)continue;
          item.person=p;attached.set(p,item);hand.add(item.g);item.g.visible=true;invalidate(p.g);
        }
        // Cancel the wrist rotation: the shaft stays upright while the hand
        // follows the walk cycle. The phone faces the observed object.
        hand.getWorldQuaternion(inverse).invert();
        const pitch=pose.kind==='photo'&&Number.isFinite(pose.lookY)?-Math.atan2(pose.lookY-(p.g.position.y+1.8*p.g.scale.y),
          Math.hypot(pose.lookX-p.g.position.x,pose.lookZ-p.g.position.z)):0;
        angles.set(clamp(pitch,-.7,.2),p.g.rotation.y,pose.kind==='umbrella'?Math.sin(pose.beat*1.4+pose.phase)*.025:0,'YXZ');
        desired.setFromEuler(angles);item.g.quaternion.copy(inverse).multiply(desired);
        item.g.scale.setScalar(pose.kind==='umbrella'?clamp(pose.weight*1.4,.08,1):1);
      }
    },
    snapshot:()=>({created:{umbrella:pool.umbrella.length,phone:pool.photo.length},attached:[...attached].map(([p,item])=>({name:p.name,kind:item.kind}))}),
    dispose(){for(const [p,item] of attached)detach(p,item);for(const item of pool.umbrella)item.g.traverse(o=>{if(o.geometry&&o.geometry!==cube)o.geometry.dispose();});
      for(const mat of materials.values())mat.dispose();cube.dispose();}
  };
}
