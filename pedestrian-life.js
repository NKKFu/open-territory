// Disc contacts and short street encounters use the same live residents as the
// navigation and bus systems. Positions are in world units, irrespective of LOD.
const cellSize=3,cell=(x,z)=>Math.floor(x/cellSize)*4096+Math.floor(z/cellSize);
const radius=p=>.55*(p?.g.scale?.x||1);
const present=p=>!p.inBus&&!p.ragdoll;
const distance2=(a,b)=>(a.x-b.x)**2+(a.z-b.z)**2;
const exchanges=[
  ['Você viu o mural perto do café?', 'Vi! Aquelas ondas ficaram bonitas.'],
  ['Vai descer até a praia?', 'Vou passar no quiosque antes.'],
  ['O que tem na feira hoje?', 'Vi manga e goiaba quando passei.'],
  ['Bom te ver por aqui!', 'Também! A gente se vê na praça.']
];

// Test the actual crossing segment in the vehicle's local frame. Parallel
// traffic elsewhere in the intersection should not hold the sidewalk queue.
export function trafficBlocksCrossing(a,b,cars){
  for(const car of cars){
    const yaw=car.g.rotation.y,c=Math.cos(yaw),s=Math.sin(yaw),p=car.g.position;
    const x=a.x-p.x,z=a.z-p.z,dx=b.x-a.x,dz=b.z-a.z;
    const w=(car.width??2.5)/2+.55,h=(car.length??4.6)/2+.55;
    const future=(car.velocity||0)*1.7,center=car.centerOffset||0;
    let lo=0,hi=1;
    for(const [start,delta,min,max] of [[c*x-s*z,c*dx-s*dz,-w,w],
      [s*x+c*z,s*dx+c*dz,center-h+Math.min(0,future),center+h+Math.max(0,future)]]){
      if(Math.abs(delta)<1e-8){if(start<min||start>max){hi=-1;break;}continue;}
      const t0=(min-start)/delta,t1=(max-start)/delta;
      lo=Math.max(lo,Math.min(t0,t1));hi=Math.min(hi,Math.max(t0,t1));
      if(lo>hi)break;
    }
    if(lo<=hi)return true;
  }
  return false;
}

export function createPedestrianLife({people,cars=[],groundHeight,canStep,canPause,canSee,canReact=()=>true,onRoad=()=>false,focus,random=Math.random}){
  const buckets=new Map(),records=new WeakMap(),events=new Set(),members=new Map(),cooldowns=new WeakMap(),dodging=new Set();
  let serial=0,nextEncounter=0,started=0,finished=0,avoidances=0,corrections=0;
  let nextTraffic=0,lastTraffic=0;
  const traffic={checks:0,notices:0,sidesteps:0,hurried:0,horns:0};
  function record(p){
    if(!records.has(p))records.set(p,{id:serial++,cell:null});
    return records.get(p);
  }
  function index(p){
    const pos=p.g.position,r=record(p),key=cell(pos.x,pos.z);
    if(key===r.cell)return;
    if(r.cell!==null){const old=buckets.get(r.cell);if(old)old.delete(p);}
    if(!buckets.has(key))buckets.set(key,new Set());
    buckets.get(key).add(p);r.cell=key;
  }
  function refresh(){
    buckets.clear();
    for(const p of people){record(p).cell=null;if(present(p))index(p);}
  }
  function neighbours(x,z,range){
    const near=[];
    for(let ix=Math.floor((x-range)/cellSize);ix<=Math.floor((x+range)/cellSize);ix++){
      for(let iz=Math.floor((z-range)/cellSize);iz<=Math.floor((z+range)/cellSize);iz++){
        const bucket=buckets.get(ix*4096+iz);if(bucket)for(const p of bucket)near.push(p);
      }
    }
    return near;
  }
  function blocked(p,x,z,near){
    const pos=p.g.position;
    for(const q of near){
      if(p===q||!present(q))continue;
      const other=q.g.position;if(Math.abs(pos.y-other.y)>1.6)continue;
      const edge=radius(p)+radius(q),dx=x-other.x,dz=z-other.z,d2=dx*dx+dz*dz;
      // A previously overlapping pair is always allowed to move apart.
      if(d2<edge*edge && d2<=distance2(pos,other)+1e-9)return true;
    }
    return false;
  }
  function occupied(p,x,z){
    const y=p?.g.position.y??groundHeight(x,z);
    return neighbours(x,z,2).some(q=>q!==p&&present(q)&&Math.abs(q.g.position.y-y)<1.6&&
      (q.g.position.x-x)**2+(q.g.position.z-z)**2<(radius(p)+radius(q))**2);
  }
  function place(p,x,z){p.g.position.set(x,groundHeight(x,z),z);index(p);}
  function walk(p,dx,dz,amount,options){
    const pos=p.g.position,distance=Math.hypot(dx,dz);if(distance<.001||amount<=0)return false;
    const fx=dx/distance,fz=dz/distance,rx=fz,rz=-fx,near=neighbours(pos.x,pos.z,3.2);
    let nearest=Infinity,steer=0;
    for(const q of near){
      if(q===p||!present(q)||Math.abs(pos.y-q.g.position.y)>1.6)continue;
      const x=q.g.position.x-pos.x,z=q.g.position.z-pos.z,ahead=x*fx+z*fz,side=x*rx+z*rz;
      const room=radius(p)+radius(q)+.28;
      if(ahead<.02||ahead>Math.min(2.7,distance+.12)||ahead>=nearest||Math.abs(side)>=room)continue;
      nearest=ahead;
      steer=(side>.09?-1:1)*Math.min(2.7,1.65*(room-Math.abs(side))/Math.max(.5,ahead-.65));
    }
    const probe=Math.min(.67,distance),memory=record(p);
    const obstacle=!canStep(p,pos.x+fx*probe,pos.z+fz*probe,options);
    if(!obstacle)memory.passSide=0;
    // Keep the chosen side of a post or other solid until the forward path is
    // clear. At contact a fully lateral step can still move along its edge.
    let sign=memory.passSide||Math.sign(steer)||1;
    if(obstacle&&!memory.passSide&&!canStep(p,pos.x+fx*probe+rx*.65*sign,pos.z+fz*probe+rz*.65*sign,options))sign*=-1;
    const across=sign*Math.PI/2;
    const choices=obstacle?[sign,sign*1.28,across,-sign,-across,0]:
      steer?[Math.atan(steer),sign*1.20,-sign*.93,0,across,-across]:[0,.70,-.70,across,-across];
    for(const angle of choices){
      const forward=Math.cos(angle),side=Math.sin(angle),vx=fx*forward+rx*side,vz=fz*forward+rz*side;
      const x=pos.x+vx*amount,z=pos.z+vz*amount;
      if(blocked(p,x,z,near)||!canStep(p,x,z,options))continue;
      place(p,x,z);
      if(obstacle&&side)memory.passSide=Math.sign(side);
      turn(p,Math.atan2(vx,vz),Math.min(1,amount*10));
      if(Math.abs(side)>.09)avoidances++;
      return true;
    }
    return false;
  }
  function turn(p,yaw,mix){
    const delta=yaw-p.g.rotation.y;
    p.g.rotation.y+=Math.atan2(Math.sin(delta),Math.cos(delta))*mix;
  }
  function movable(p){return !(p.stationary||p.sitting||p.exitingVehicle||p.incidentCar||p.push||p.role&&!p.axis);}
  function separate(dt){
    // Rebuild after boarding, collisions and other systems which may relocate a body.
    refresh();
    for(const p of people){
      if(!present(p))continue;
      const a=p.g.position,pr=record(p);
      for(const q of neighbours(a.x,a.z,1.55)){
        if(record(q).id<=pr.id)continue;
        const b=q.g.position,dx=b.x-a.x,dz=b.z-a.z,edge=radius(p)+radius(q),d2=dx*dx+dz*dz;
        if(d2>=edge*edge||Math.abs(a.y-b.y)>1.6)continue;
        const d=Math.sqrt(d2),nx=d>.0001?dx/d:Math.cos(pr.id*2.4),nz=d>.0001?dz/d:Math.sin(pr.id*2.4);
        const moveA=movable(p),moveB=movable(q),overlap=Math.min(edge-d+.002,dt*4);
        function shift(actor,sign,amount){
          const pos=actor.g.position,x=pos.x+sign*nx*amount,z=pos.z+sign*nz*amount;
          if(blocked(actor,x,z,neighbours(x,z,1.6))||!canStep(actor,x,z,{contact:true}))return false;
          place(actor,x,z);corrections++;return true;
        }
        const first=moveA&&shift(p,-1,moveB?overlap/2:overlap);
        if(moveB&&!shift(q,1,first?overlap/2:overlap)&&first)shift(p,-1,overlap/2);
      }
    }
  }

  function end(event,time){
    for(const {p,yaw,limbs} of event.original){
      members.delete(p);cooldowns.set(p,time+26+random()*20);
      if(p.stationary)p.g.rotation.y=yaw;
      p.g.userData.limbs?.forEach((l,i)=>{l.rotation.x=limbs[i];});
    }
    events.delete(event);finished++;
  }
  function start(actors,kind,time,lines,source=null){
    const event={actors,kind,start:time,lines,source,duration:kind==='ring'?2.8:5.6,
      original:actors.map(p=>({p,yaw:p.g.rotation.y,limbs:p.g.userData.limbs?.map(l=>l.rotation.x)||[]}))};
    for(const p of actors)members.set(p,event);
    events.add(event);started++;return event;
  }
  function available(p,time){
    return present(p)&&!members.has(p)&&!dodging.has(p)&&canPause(p)&&(cooldowns.get(p)??0)<=time;
  }
  function beginNearby(time){
    const listener=focus();let best=null,score=Infinity;
    for(const a of neighbours(listener.x,listener.z,13)){
      if(!available(a,time))continue;
      const pos=a.g.position,heard=distance2(pos,listener);
      if(heard>13**2||!canSee(listener,pos))continue;
      for(const b of neighbours(pos.x,pos.z,2.7)){
        if(a===b||!available(b,time)||!canSee(pos,b.g.position))continue;
        const d2=distance2(pos,b.g.position);
        if(d2<(radius(a)+radius(b)+.10)**2||d2>2.7**2)continue;
        if(a.stationary&&b.stationary&&!(a.chatting&&b.chatting))continue;
        const rank=heard+d2;
        if(rank<score){score=rank;best=[a,b];}
      }
    }
    if(best){start(best,'chat',time,exchanges[Math.floor(random()*exchanges.length)]);return true;}
    return false;
  }
  function reactToSound(x,z,time){
    const source={x,z},heard=neighbours(x,z,8).filter(p=>present(p)&&!dodging.has(p)&&canPause(p)&&distance2(source,p.g.position)<=64&&canSee(source,p.g.position));
    heard.sort((a,b)=>distance2(a.g.position,source)-distance2(b.g.position,source));
    const actors=heard.filter(p=>members.get(p)?.kind!=='ring').slice(0,2);
    if(!actors.length)return 0;
    for(const p of actors){const old=members.get(p);if(old)end(old,time);}
    start(actors,'ring',time,actors.length>1?['Ouviu esse telefone?','É o orelhão ali.']:['De onde veio esse toque?'],source);
    return actors.length;
  }

  function attentive(p){
    return present(p)&&!p.indoor&&!p.inBus&&!p.busState&&!p.exitingVehicle&&!p.push&&!p.incidentCar&&
      members.get(p)?.kind!=='ring'&&canReact(p);
  }
  function reactToHorn(car,time){
    const source=car.g.position,bike=car.kind==='bike',range=bike?7:12;
    const heard=neighbours(source.x,source.z,range).filter(p=>{
      const d=distance2(p.g.position,source),r=record(p);
      return d<range*range&&Math.abs(source.y-p.g.position.y)<2.4&&attentive(p)&&
        time>=(r.nextHorn||0)&&canSee(source,p.g.position);
    }).sort((a,b)=>distance2(a.g.position,source)-distance2(b.g.position,source)).slice(0,bike?2:3);
    for(const p of heard){
      const r=record(p);r.horn={source,until:time+1.15+(r.id%4)*.12};r.nextHorn=time+8+r.id%4;
    }
    traffic.horns+=heard.length;return heard.length;
  }

  function curbReady(p,dx,dz,dt,time){
    const nav=p.nav,r=record(p);
    if(nav.lookIndex!==nav.index){nav.lookIndex=nav.index;nav.lookTime=0;traffic.checks++;}
    nav.lookTime+=dt;
    const duration=.78+(r.id%4)*.075,phase=(nav.lookTime%(4.1+(r.id%3)*.4))/duration;
    // Glance over both shoulders while waiting for the signal. The first
    // green phase is usable immediately if both directions were checked.
    turn(p,Math.atan2(dx,dz),1-Math.exp(-dt*6));
    r.curbYaw=phase<.45?-1.14:phase<1.05?1.13:0;r.curbUntil=time+.09;
    p.g.userData.limbs?.forEach(l=>l.rotation.x*=Math.exp(-dt*9));
    return nav.lookTime>=duration&&!(r.risk?.until>time);
  }

  function trySidestep(p,sensor,side,time){
    const pos=p.g.position,r=record(p);
    if(p.stationary||p.sitting||p.nav?.crossing||onRoad(pos.x,pos.z))return;
    const preferred=Math.sign(side)||(r.id%2?1:-1),edge=sensor.halfWidth+radius(p)+.28;
    for(const sign of [preferred,-preferred]){
      const across=sign*edge-side;if(Math.abs(across)<.2||Math.abs(across)>2.2)continue;
      for(const along of [0,.8,-.8]){
        const dx=sensor.cos*across+sensor.sin*along,dz=-sensor.sin*across+sensor.cos*along;
        // Check the whole step, including a diagonal past a post. If every
        // path crosses a curb, a wall or a neighbour, just brace in place.
        let clear=true;
        for(let i=1;i<=6;i++){
          const x=pos.x+dx*i/6,z=pos.z+dz*i/6;
          if(onRoad(x,z)||occupied(p,x,z)||!canStep(p,x,z)){clear=false;break;}
        }
        if(!clear)continue;
        r.dodge={x:pos.x+dx,z:pos.z+dz,until:time+1.12};dodging.add(p);traffic.sidesteps++;return;
      }
    }
  }

  function senseTraffic(time){
    const elapsed=Math.max(.01,time-lastTraffic);lastTraffic=time;
    const moving=[];
    for(const car of cars){
      if(Math.abs(car.velocity)<2.2)continue;
      const yaw=car.g.rotation.y;
      moving.push({car,pos:car.g.position,sin:Math.sin(yaw),cos:Math.cos(yaw),
        halfWidth:(car.width??2.5)/2,halfLength:(car.length??4.6)/2});
    }
    for(const p of people){
      const r=record(p),pos=p.g.position;
      const vx=r.lastX===undefined?0:(pos.x-r.lastX)/elapsed,vz=r.lastZ===undefined?0:(pos.z-r.lastZ)/elapsed;
      r.lastX=pos.x;r.lastZ=pos.z;
      if(!attentive(p)||Math.hypot(vx,vz)>4)continue;
      let threat=null,earliest=1.45,side=0;
      for(const sensor of moving){
        const {car,sin,cos}=sensor,dx=pos.x-sensor.pos.x,dz=pos.z-sensor.pos.z;
        if(Math.abs(pos.y-sensor.pos.y)>2.1||Math.abs(dx)>65||Math.abs(dz)>65)continue;
        const sign=Math.sign(car.velocity),closing=(car.velocity-vx*sin-vz*cos)*sign;
        if(closing<1)continue;
        const gap=(dx*sin+dz*cos-(car.centerOffset||0))*sign-sensor.halfLength;
        if(gap<-.25)continue;
        const arrival=Math.max(0,gap/closing);if(arrival>earliest)continue;
        const across=dx*cos-dz*sin,turn=Math.max(-2,Math.min(2,(car.yawRate||0)*car.velocity*arrival*arrival*.5));
        const atImpact=across+(vx*cos-vz*sin-(car.lateralSpeed||0))*arrival-turn;
        if(Math.abs(atImpact)>sensor.halfWidth+radius(p)+.16)continue;
        threat=sensor;earliest=arrival;side=across;
      }
      if(!threat||!canSee(threat.pos,pos))continue;
      if(r.risk?.source!==threat.pos||time>r.risk.until){
        traffic.notices++;
        if(p.nav?.crossing)traffic.hurried++;
        else if(time>=(r.nextDodge||0)){
          trySidestep(p,threat,side,time);
          if(dodging.has(p)){r.nextDodge=time+2.1;const chat=members.get(p);if(chat)end(chat,time);}
        }
      }
      r.risk={source:threat.pos,until:time+.62};
    }
  }

  function updateDodges(dt,time){
    for(const p of dodging){
      const d=record(p).dodge;
      const pos=p.g.position,dx=d.x-pos.x,dz=d.z-pos.z,gap=Math.hypot(dx,dz);
      if(time>=d.until||gap<.04||!attentive(p)){dodging.delete(p);continue;}
      const step=Math.min(gap,dt*2.15),x=pos.x+dx/gap*step,z=pos.z+dz/gap*step;
      if(onRoad(x,z)||occupied(p,x,z)||!canStep(p,x,z)){dodging.delete(p);continue;}
      place(p,x,z);
    }
  }

  function animateAttention(p,dt,time){
    const head=p.g.userData.head;if(!head)return;
    const r=record(p),risk=r.risk?.until>time?r.risk:null,horn=r.horn?.until>time?r.horn:null;
    const alert=attentive(p)&&(risk||horn),startled=!!(alert&&risk);
    p.attentionActive=!!(alert||r.curbUntil>time);
    // The resident's main rig owns recovery, reading and idle head motion.
    // Traffic attention only overrides it while an actual cue is present.
    if(!p.attentionActive&&(p.motionPose||p.ambientPose||p.lifeState==='activity'))return;
    let yaw=r.curbUntil>time?r.curbYaw:0;
    if(alert){
      const dx=alert.source.x-p.g.position.x,dz=alert.source.z-p.g.position.z;
      const bearing=Math.atan2(dx,dz)-p.g.rotation.y;
      yaw=Math.atan2(Math.sin(bearing),Math.cos(bearing));
      if(Math.abs(yaw)>2.78)yaw=(Math.sign(head.rotation.y)||(r.id%2?1:-1))*Math.PI;
      yaw=Math.max(-1.24,Math.min(1.24,yaw));
    }
    const blend=1-Math.exp(-dt*(alert||r.curbUntil>time?9:5));
    head.rotation.y+=(yaw-head.rotation.y)*blend;
    head.rotation.x+=((startled?.11:0)-head.rotation.x)*blend;
    const arm=p.g.userData.limbs?.[2];
    if(arm)arm.rotation.z+=((startled?-.59:0)-arm.rotation.z)*blend;
  }
  function update(dt,time){
    separate(dt);
    if(time>=nextTraffic){senseTraffic(time);nextTraffic=time+.13;}
    updateDodges(dt,time);
    for(const event of events){
      const elapsed=time-event.start;
      if(elapsed>=event.duration||event.actors.some(p=>!present(p)||!canPause(p))){end(event,time);continue;}
      event.actors.forEach((p,i)=>{
        const target=event.source||event.actors[1-i].g.position,pos=p.g.position;
        turn(p,Math.atan2(target.x-pos.x,target.z-pos.z),1-Math.exp(-dt*6));
        const speaking=i===Math.min(event.actors.length-1,Math.floor(elapsed/(event.duration/2)));
        p.g.userData.limbs?.forEach((l,n)=>{
          const pose=n<2?0:n===2&&speaking?-.86+.23*Math.sin(elapsed*5):-.08;
          l.rotation.x+=(pose-l.rotation.x)*(1-Math.exp(-dt*9));
        });
      });
    }
    if(time>=nextEncounter){
      const got=events.size===0&&beginNearby(time);
      nextEncounter=time+(got?13+random()*6:1.1);
    }
  }
  function utterance(time){
    let nearest=12**2,result=null;const listener=focus();
    for(const event of events){
      const age=time-event.start,index=event.actors.length>1&&age>=event.duration/2?1:0;
      const speaker=event.actors[index],d=distance2(listener,speaker.g.position);
      if(d<nearest){
        nearest=d;result={speaker,line:event.lines[index],kind:event.kind,until:event.start+(index===0&&event.actors.length>1?event.duration/2:event.duration)};
      }
    }
    return result;
  }
  return {refresh,place,walk,occupied,update,reactToSound,reactToHorn,curbReady,animateAttention,utterance,
    attentionPriority:(p,time)=>dodging.has(p)||record(p).risk?.until>time||record(p).horn?.until>time||record(p).curbUntil>time,
    pace:(p,time)=>p.nav?.crossing&&record(p).risk?.until>time?1.55:1,
    isPaused:p=>members.has(p)||dodging.has(p),
    interrupt(p,time){const event=members.get(p);if(event)end(event,time);dodging.delete(p);},
    snapshot:()=>({started,finished,avoidances,corrections,traffic:{...traffic},active:[...events].map(e=>({kind:e.kind,participants:e.actors.map(p=>p.name)}))})};
}
