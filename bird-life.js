// Reuse the plaza's existing flock. Flights reserve an empty landing spot and
// follow a checked path; quiet approaches still allow ground interactions.
export function createBirdLife({birds,groundHeight,canLand,canTravel,isPetting=()=>false,random=Math.random,onTakeoff=()=>{}}){
  for(const bird of birds){bird.flight=null;bird.flightCooldown=4+random()*5;}
  function occupied(bird,x,z){
    return birds.some(other=>other!==bird&&Math.hypot((other.flight?.goal.x??other.g.position.x)-x,(other.flight?.goal.z??other.g.position.z)-z)<.9);
  }
  function plan(bird,threat){
    const p=bird.g.position,away=threat?Math.atan2(p.x-threat.x,p.z-threat.z):random()*Math.PI*2;
    for(let attempt=0;attempt<12;attempt++){
      const angle=away+(random()-.5)*(attempt<6?2:Math.PI*2),length=4+random()*4;
      const x=p.x+Math.sin(angle)*length,z=p.z+Math.cos(angle)*length;
      if(Math.hypot(x-bird.originX,z-bird.originZ)>12||!canLand(x,z,bird)||occupied(bird,x,z))continue;
      let clear=true;
      for(let step=1;step<=Math.ceil(length/.6);step++){
        const t=step/Math.ceil(length/.6);
        if(!canTravel(p.x+(x-p.x)*t,p.z+(z-p.z)*t)){clear=false;break;}
      }
      if(!clear)continue;
      bird.flight={start:{x:p.x,y:p.y,z:p.z},goal:{x,y:groundHeight(x,z),z},elapsed:0,duration:1.2+length/5,height:1.5+random()*.9,landedWait:0};
      bird.heading=Math.atan2(x-p.x,z-p.z);bird.flightCooldown=8+random()*8;onTakeoff(p.x,p.z);return true;
    }
    bird.flightCooldown=2;return false;
  }
  return {
    update(dt,threat,night=0){
      for(const bird of birds){
        if(bird.flight){
          const f=bird.flight,ready=canLand(f.goal.x,f.goal.z,bird);
          // Wait above an occupied landing instead of passing through a person.
          if(f.elapsed/f.duration<.8||ready)f.elapsed=Math.min(f.duration,f.elapsed+dt);
          else{f.landedWait+=dt;if(f.landedWait>1){plan(bird,null);f.landedWait=0;}}
          if(bird.flight!==f)continue;
          const t=f.elapsed/f.duration,p=bird.g.position;
          p.set(f.start.x+(f.goal.x-f.start.x)*t,f.start.y+(f.goal.y-f.start.y)*t+Math.sin(t*Math.PI)*f.height,f.start.z+(f.goal.z-f.start.z)*t);
          bird.g.rotation.y=bird.heading;
          if(t>=1){p.y=f.goal.y;bird.flight=null;bird.turn=.6;bird.landings=(bird.landings||0)+1;}
          continue;
        }
        bird.flightCooldown=Math.max(0,bird.flightCooldown-dt);
        if(isPetting(bird)){bird.pendingFlight=null;continue;}
        if(bird.pendingFlight){
          bird.pendingFlight.delay-=dt;
          if(bird.pendingFlight.delay<=0){const pending=bird.pendingFlight;bird.pendingFlight=null;plan(bird,pending.threat);}
          continue;
        }
        const p=bird.g.position,distance=Math.hypot(p.x-threat.x,p.z-threat.z);
        if(bird.flightCooldown>0||distance>60)continue;
        const startled=threat.speed>.6&&distance<(threat.speed>6?5:1.9),idle=night<.6&&random()<dt/65;
        if(!startled&&!idle)continue;
        if(plan(bird,startled?threat:null)&&startled){
          let count=0;
          for(const other of birds){
            if(other===bird||other.flight||other.pendingFlight||other.flightCooldown>0||isPetting(other)||Math.hypot(other.g.position.x-p.x,other.g.position.z-p.z)>5)continue;
            other.pendingFlight={delay:.1+random()*.3,threat:{x:threat.x,z:threat.z}};if(++count===3)break;
          }
        }
      }
    },
    snapshot:()=>birds.map(b=>({x:b.g.position.x,y:b.g.position.y,z:b.g.position.z,ground:groundHeight(b.g.position.x,b.g.position.z),flying:!!b.flight,pending:!!b.pendingFlight,cooldown:b.flightCooldown,landings:b.landings||0}))
  };
}
