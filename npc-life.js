// Local routines: navigation belongs to the city, gestures and scheduling live here.
// moveTo(person, reservedLocation, dt) must walk the actor and return true on arrival.
export function createNpcLife({THREE:T, scene, people, player, groundHeight,
  isTalking=()=>false, moveTo, locations, random=Math.random, canStand=()=>true}) {
  if(typeof moveTo!=='function')throw new TypeError('NPC routines require a navigation callback');
  const managed=new Set(),actors=[],reservations=new Map(),materials=new Map();
  const cube=new T.BoxGeometry(1,1,1);
  const programs=[['home','work','eat','wait','rest','phone'],['chat','phone','eat','read','home'],
    ['play','rest','phone','eat','home'],['work','rest','chat','read','home']];
  const defaults={phone:[10,22],chat:[20,42],read:[25,55],eat:[28,55],work:[35,75],
    smoke:[12,24],play:[25,50],wait:[10,20],rest:[25,50],home:[40,85],paint:[35,65]};
  let now=0;
  function material(color){
    if(!materials.has(color))materials.set(color,new T.MeshStandardMaterial({color,roughness:.85}));
    return materials.get(color);
  }
  function box(parent,x,y,z,w,h,d,color){
    const mesh=new T.Mesh(cube,material(color));mesh.position.set(x,y,z);mesh.scale.set(w,h,d);
    mesh.castShadow=true;parent.add(mesh);return mesh;
  }
  function release(actor){
    if(actor.target)reservations.get(actor.target.id)?.delete(actor);
    actor.target=null;
  }
  function validFor(actor,location){
    return (!location.role||location.role===actor.person.role)&&
      !(location.activity==='smoke'&&(actor.person.child||actor.person.age==='child'));
  }
  function reserve(actor,activity){
    const eligible=locations.filter(l=>l.activity===activity&&validFor(actor,l)&&l.id!==actor.lastLocation);
    // Prefer nearby, less occupied places, with a little variation between people.
    const ranked=eligible.map(l=>({l,score:Math.hypot(l.x-actor.person.g.position.x,l.z-actor.person.g.position.z)+
      (reservations.get(l.id)?.size||0)*18+random()*20-(l.id===actor.person.initialDestination&&actor.next===1?1000:0)})).sort((a,b)=>a.score-b.score);
    for(const {l} of ranked){
      const assigned=reservations.get(l.id)||new Set(),capacity=Math.max(1,Math.min(6,l.capacity||1));
      if(assigned.size>=capacity)continue;
      const used=new Set([...assigned].map(a=>a.target.slot));
      for(let slot=0;slot<capacity;slot++){
        if(used.has(slot))continue;
        const spacing=l.slotSpacing||1.6,angle=l.rotation||0;
        const offset=(slot-(capacity-1)/2)*spacing;
        const x=l.x+Math.cos(angle)*offset,z=l.z-Math.sin(angle)*offset;
        if(!canStand(x,z,actor.person))continue;
        actor.target={...l,x,z,slot,centerX:l.x,centerZ:l.z};
        assigned.add(actor);reservations.set(l.id,assigned);return true;
      }
    }
    return false;
  }
  function choose(actor){
    release(actor);
    const requested=actor.program[actor.next++%actor.program.length];
    const options=[requested,...actor.program,'rest','phone','chat','wait','home'];
    for(const activity of [...new Set(options)])if(reserve(actor,activity)){
      actor.state='travel';actor.elapsed=0;actor.person.lifeActivity=actor.target.activity;actor.person.lifeState='travel';return;
    }
    actor.state='idle';actor.elapsed=0;actor.person.lifeActivity=null;actor.person.lifeState='idle';
  }
  function hideProps(actor){
    if(actor.props)for(const prop of Object.values(actor.props))prop.visible=false;
  }
  function propsFor(actor){
    if(actor.props)return actor.props;
    const g=actor.person.g,hand=g.userData.limbs?.[3]||g;
    const phone=box(hand,0,-.75,.12,.2,.34,.055,'#344c56');
    box(phone,0,0,.52,.8,.75,.04,'#a5d4cf');
    const book=new T.Group();g.add(book);book.position.set(0,1.2,.55);
    box(book,-.19,0,0,.36,.07,.45,'#e0bd88');box(book,.19,0,0,.36,.07,.45,'#efdfbd');
    const cup=box(hand,0,-.72,.12,.18,.25,.18,'#eee2bc');
    const broom=new T.Group();hand.add(broom);broom.position.set(0,-.8,.1);
    box(broom,0,-.25,0,.06,1.5,.06,'#99764e');box(broom,0,-1,0,.55,.18,.2,'#c7b07a');
    const cigarette=box(hand,0,-.8,.13,.045,.045,.24,'#e4d9c1');
    box(cigarette,0,0,.55,.85,.85,.15,'#c47b4f');
    const smoke=new T.Group();smoke.position.set(.35,1.85,.6);g.add(smoke);
    actor.smokeMaterial=new T.MeshBasicMaterial({color:'#c9cfca',transparent:true,opacity:.22,depthWrite:false});
    for(let i=0;i<3;i++){const puff=new T.Mesh(cube,actor.smokeMaterial);smoke.add(puff);}
    const ball=box(g,0,.22,.8,.42,.42,.42,'#d99b65');
    actor.props={phone,book,cup,broom,cigarette,smoke,ball};hideProps(actor);return actor.props;
  }
  function pose(actor,dt){
    const p=actor.person,g=p.g,activity=actor.target.activity,t=now+actor.phase;
    const limbs=g.userData.limbs||[],props=propsFor(actor);hideProps(actor);
    limbs.forEach(l=>l.rotation.x=0);
    const arm=(index,value)=>{if(limbs[index])limbs[index].rotation.x=value;};
    if(activity==='phone'){
      props.phone.visible=true;arm(3,-1.05+Math.sin(t*1.8)*.035);arm(2,-.25);
    }else if(activity==='read'){
      props.book.visible=true;arm(2,-.8);arm(3,-.8);
    }else if(activity==='eat'){
      props.cup.visible=true;arm(3,-.8-Math.max(0,Math.sin(t*1.3))*.65);
    }else if(activity==='work'){
      props.broom.visible=true;arm(3,-.35+Math.sin(t*2)*.2);arm(2,-.25);
      props.broom.rotation.z=Math.sin(t*2)*.18;props.broom.position.y=-.65;
    }else if(activity==='paint'){
      if(!props.brush){
        const hand=g.userData.limbs?.[3]||g,left=g.userData.limbs?.[2]||g;
        const brush=new T.Group();brush.name='routine-paint-brush';brush.position.set(0,-.78,.1);hand.add(brush);
        box(brush,0,0,.15,.07,.07,.3,'#99764e');box(brush,0,0,.34,.13,.08,.1,'#e4b759');
        const palette=box(left,0,-.77,.1,.34,.07,.46,'#c9aa77');palette.name='routine-paint-palette';
        for(const [index,color] of ['#df9874','#4d9eac','#edc86a'].entries())box(palette,(index-1)*.23,.65,.1,.17,.35,.17,color);
        props.brush=brush;props.palette=palette;
      }
      props.brush.visible=props.palette.visible=true;
      const facing=actor.target.rotation??g.rotation.y;
      g.rotation.y+=Math.atan2(Math.sin(facing-g.rotation.y),Math.cos(facing-g.rotation.y))*(1-Math.exp(-dt*5));
      arm(3,-1.3+Math.sin(t*2.1)*.16);arm(2,-.85);props.brush.rotation.z=Math.sin(t*1.4)*.15;
    }else if(activity==='smoke'){
      props.cigarette.visible=true;arm(3,-.9-Math.max(0,Math.sin(t*.85))*.65);
      props.smoke.visible=true;
      props.smoke.children.forEach((puff,i)=>{
        const phase=(t*.4+i/3)%1;puff.position.set(Math.sin(t+i)*.08,phase*.85,phase*.15);
        puff.scale.setScalar(.055+phase*.1);puff.rotation.y=t*.3+i;
      });
    }else if(activity==='play'){
      props.ball.visible=true;props.ball.position.set(Math.sin(t*1.3)*.55,.23+Math.abs(Math.sin(t*2))*.5,.9);
      props.ball.rotation.x=t;props.ball.rotation.z=t*.7;arm(2,-.35);arm(3,.2);
      if(limbs[1])limbs[1].rotation.x=Math.max(0,Math.sin(t*2))*.3;
    }else if(activity==='chat'){
      const partner=actors.find(other=>other!==actor&&other.state==='activity'&&other.target?.id===actor.target.id);
      if(partner){
        const yaw=Math.atan2(partner.person.g.position.x-g.position.x,partner.person.g.position.z-g.position.z);
        g.rotation.y+=Math.atan2(Math.sin(yaw-g.rotation.y),Math.cos(yaw-g.rotation.y))*(1-Math.exp(-dt*4));
      }
      arm(2,-.4+Math.sin(t*2.4)*.25);arm(3,Math.sin(t*1.5)*.12);
    }else if(activity==='wait'){
      arm(3,-.25);g.rotation.y+=Math.sin(t*.6)*dt*.12;
    }else if(activity==='rest'){
      arm(2,-.15);arm(3,-.15);
    }
  }
  const candidates=people.filter(p=>(!p.indoor||p.lifeResident)&&!p.stationary&&!p.sitting&&!p.role&&!p.debugId&&!p.busState);
  candidates.forEach((person,index)=>{
    const program=[...(person.routine||programs[index%programs.length])];
    if(!person.routine&&index%9===0&&!person.child&&person.age!=='child')program.splice(2,0,'smoke');
    const actor={person,program,next:person.routine?0:index%program.length,state:'idle',target:null,elapsed:0,
      phase:random()*20,lastLocation:null,props:null};
    // Keep every actor's feet at the same origin, varying height and accessories.
    const scale=.86+random()*.25;person.g.scale.multiplyScalar(person.fixedScale??scale);
    if(index%4===0)box(person.g,0,1.35,-.35,.62,.7,.24,['#6d8270','#b58864','#70899d'][index%3]);
    if(index%7===0){box(person.g,0,2.36,0,.72,.16,.65,'#b8a27b');box(person.g,0,2.49,-.02,.48,.2,.44,'#b8a27b');}
    if(index%6===1){box(person.g,0,2.12,-.23,.6,.6,.15,person.g.userData.hair||'#594942');}
    if(index%5===2)box(person.g,0,1,0,.84,.57,.53,person.g.userData.shirt||'#a595b9');
    if(index%6===3)for(const side of [-1,1])box(person.g,side*.16,2.05,.285,.22,.12,.045,'#425960');
    if(index%8===4&&!person.child)box(person.g,0,1.83,.25,.42,.18,.055,person.g.userData.hair||'#594942');
    person.lifeManaged=true;actors.push(actor);managed.add(person);choose(actor);
  });
  return {
    managed,
    update(dt,time){
      now=time;
      for(const actor of actors){
        const p=actor.person;
        if(p.routineFinished){p.routineFinished=false;actor.lastLocation=actor.target?.id;choose(actor);}
        // Visibility is only a rendering optimization: distant residents keep living.
        if(p.ragdoll||p.inBus||p.busState||isTalking(p)){hideProps(actor);continue;}
        actor.elapsed+=dt;
        if(actor.state==='idle'){if(actor.elapsed>2+actor.phase%3)choose(actor);continue;}
        if(!actor.target){choose(actor);continue;}
        if(actor.state==='travel'){
          hideProps(actor);
          const result=moveTo(p,actor.target,dt);
          if(result==='unreachable'||actor.elapsed>420){actor.lastLocation=actor.target.id;choose(actor);continue;}
          if(result===true){
            actor.state='activity';actor.elapsed=0;p.lifeState='activity';
            const duration=actor.target.duration||defaults[actor.target.activity]||[10,20];
            actor.duration=Array.isArray(duration)?duration[0]+random()*(duration[1]-duration[0]):duration;
            if(Number.isFinite(actor.target.rotation))p.g.rotation.y=actor.target.rotation;
          }
        }else{
          pose(actor,dt);p.g.position.y=groundHeight(p.g.position.x,p.g.position.z);
          if(actor.elapsed>=actor.duration){actor.lastLocation=actor.target.id;choose(actor);}
        }
      }
      return managed;
    },
    snapshot(){return actors.map(a=>({name:a.person.name,state:a.state,activity:a.target?.activity||null,
      destination:a.target?.id||null,x:a.person.g.position.x,z:a.person.g.position.z,slot:a.target?.slot??null,
      child:!!a.person.child,indoor:!!a.person.indoor,busState:a.person.busState||null,route:a.person.nav?{steps:a.person.nav.path?.length??0,index:a.person.nav.index,stuck:a.person.nav.stuck,
        crossing:a.person.nav.crossing,next:a.person.nav.path?.[a.person.nav.index]||null}:null}));},
    dispose(){
      for(const actor of actors){release(actor);if(actor.props)for(const prop of Object.values(actor.props))prop.removeFromParent();actor.smokeMaterial?.dispose();}
      for(const mat of materials.values())mat.dispose();cube.dispose();managed.clear();
    }
  };
}
