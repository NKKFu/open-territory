const charges={disparo:2,agressao:2,roubo:2,furto:1,atropelamento:3,dano:1};
const distance=(a,b)=>Math.hypot(a.x-b.x,a.z-b.z);
const copy=p=>({x:p.x,y:p.y??0,z:p.z});
const present=p=>p&&!p.ragdoll&&!p.inBus&&!p.incapacitated;

// Only a description transmitted by a witness or a uniformed eyewitness can
// identify the player. Once sight is lost officers navigate the last seen place.
export function createCityPolice({people,position,canSee,move,notice,phone=()=>{},charge,grounded,
  down=()=>false,threatening=()=>false,custodyChanged=()=>{}}){
  const uniformed=people.filter(p=>p.role==='police'),officers=uniformed.filter(p=>!p.indoor);
  const homes=new Map(uniformed.map(p=>[p,copy(p.g.position)])),returning=new Set(),jammed=new WeakMap(),startled=new Map();
  let stage='livre',caseLevel=0,identified=false,lastKnown=null,lastSeen=-Infinity,call=null,unit=[];
  let now=0,until=0,scanAt=0,closeSince=0,lastEvent=-Infinity,eventWeight=0,lastType=null,visible=[],lastFine=0;
  const look=(p,where)=>{p.g.rotation.y=Math.atan2(where.x-p.g.position.x,where.z-p.g.position.z);};
  function sees(p,spot,radius,alert=false){
    if(!present(p))return false;
    const start=p.g.position,dx=spot.x-start.x,dz=spot.z-start.z,len=Math.hypot(dx,dz);
    if(len>radius)return false;
    if(!alert&&len>5&&(Math.sin(p.g.rotation.y)*dx+Math.cos(p.g.rotation.y)*dz)<-.12*len)return false;
    return canSee(p,spot);
  }
  function stopCalling(){
    if(call){phone(call.witness,false);call=null;}
  }
  function clear(){
    stopCalling();
    for(const p of unit){p.nav=null;returning.add(p);}
    if(stage==='rendicao')custodyChanged(false);
    unit=[];visible=[];stage='livre';caseLevel=0;closeSince=0;lastKnown=null;identified=false;lastEvent=-Infinity;eventWeight=0;
  }
  function dispatch(spot,knows,direct){
    stopCalling();identified||=knows;lastKnown=copy(spot);scanAt=0;closeSince=0;
    const ordered=officers.filter(present).sort((a,b)=>distance(a.g.position,spot)-distance(b.g.position,spot));
    const assigned=direct?[direct,...ordered.filter(p=>p!==direct)]:ordered;
    if(!unit.length)unit=assigned.slice(0,caseLevel>=2?2:1);
    else if(direct&&!unit.includes(direct))unit.push(direct);
    for(const p of unit){returning.delete(p);p.nav=null;}
    if(direct&&knows){
      lastSeen=now;stage='perseguicao';visible=[direct];
      notice(direct,'POLÍCIA','Parado! Mãos à vista.');
    }else{
      stage='despacho';until=now+Math.min(34,Math.max(8,(unit.length?distance(unit[0].g.position,spot):50)/3.3+4));
      notice(null,'RÁDIO DA PATRULHA',knows?'Recebemos a descrição. Equipe a caminho.':'Barulho de disparo. Verifiquem o local.');
    }
  }
  function report(type,spot,{victim=null,loud=false,cruiserWitness=false}={},time=now){
    now=time;
    if(!charges[type]||!spot||stage==='rendicao')return false;
    const direct=uniformed.filter(p=>sees(p,spot,30,loud||p===victim)).sort((a,b)=>distance(a.g.position,spot)-distance(b.g.position,spot))[0];
    const seen=people.filter(p=>present(p)&&p.role!=='police'&&!p.child&&p.age!=='child'&&(jammed.get(p)||0)<=now&&
      (p===victim&&distance(p.g.position,spot)<4||sees(p,spot,21,loud))).sort((a,b)=>distance(a.g.position,spot)-distance(b.g.position,spot));
    const heard=loud&&!seen.length?people.filter(p=>present(p)&&p.role!=='police'&&!p.child&&(jammed.get(p)||0)<=now&&distance(p.g.position,spot)<18)
      .sort((a,b)=>distance(a.g.position,spot)-distance(b.g.position,spot))[0]:null;
    if(!direct&&!seen.length&&!heard&&!cruiserWitness)return false;
    const culprit=position();
    const recognizes=p=>p&&((type==='roubo'&&p===victim&&distance(p.g.position,culprit)<4)||sees(p,culprit,31,loud||p===victim));
    for(const person of seen.slice(direct?0:1,4)){
      if(person.stationary||person.sitting||person.busState||person===call?.witness||person===victim)continue;
      const p=person.g.position,dx=p.x-spot.x,dz=p.z-spot.z,len=Math.hypot(dx,dz)||1;
      startled.set(person,{x:p.x+dx/len*4,z:p.z+dz/len*4,until:now+2.4});person.nav=null;
    }
    const weight=charges[type],sameBurst=now-lastEvent<.9;
    caseLevel=Math.min(3,caseLevel+(sameBurst?Math.max(0,weight-eventWeight):weight));
    eventWeight=sameBurst?Math.max(weight,eventWeight):weight;lastEvent=now;lastType=type;
    if(direct){
      const known=recognizes(direct);
      if(stage==='perseguicao'||stage==='abordagem'){
        if(known){lastSeen=now;lastKnown=copy(culprit);}return true;
      }
      dispatch(known?culprit:spot,known,direct);return true;
    }
    if(cruiserWitness){
      if(stage!=='perseguicao'&&stage!=='abordagem')dispatch(culprit,true,null);
      cruiserSight(culprit,time);return true;
    }
    const eyewitness=seen.find(recognizes);
    if(stage==='chamada'){
      if(recognizes(call.witness)){call.identified=true;call.spot=copy(culprit);}return true;
    }
    if(stage!=='livre')return true;
    const witness=eyewitness||seen[0]||heard;
    call={witness,identified:!!eyewitness,spot:copy(eyewitness?culprit:spot),device:{label:'INTERROMPER CHAMADA',unit:{type:'chamada',g:witness.g}}};
    stage='chamada';until=now+4.6;phone(witness,true);look(witness,spot);
    notice(witness,witness.name,'Alô, polícia? Preciso de uma viatura!');
    return true;
  }
  function interrupt(device,time=now){
    if(stage!=='chamada'||call?.device.unit!==device)return false;
    jammed.set(call.witness,time+14);clear();return true;
  }
  function approaching(){
    if(!identified||stage!=='abordagem'||!grounded())return null;
    const spot=position();
    return unit.find(p=>present(p)&&distance(p.g.position,spot)<4.1&&canSee(p,spot))??null;
  }
  function surrender(){
    const officer=approaching();if(!officer)return false;
    stage='rendicao';until=now+2.05;custodyChanged(true);look(officer,position());
    notice(officer,officer.name,'Fique parado enquanto registro a ocorrência.');
    return true;
  }
  function cruiserSight(spot,time){
    if(!lastKnown||stage==='rendicao'||!identified&&!threatening())return false;
    identified=true;lastSeen=time;lastKnown=copy(spot);
    if(stage==='busca'||stage==='despacho'){
      stage='perseguicao';notice(null,'RÁDIO DA VIATURA','Suspeito à vista. Acompanhando pela rua.');
    }
    return true;
  }
  function update(dt,time){
    now=time;
    for(const [p,escape] of startled){
      if(!present(p)||p===call?.witness||now>=escape.until||distance(p.g.position,escape)<.45){startled.delete(p);continue;}
      move(p,escape,dt,2.7,'afastar');
    }
    for(const p of returning){
      if(!present(p))continue;
      const home=homes.get(p);
      if(distance(p.g.position,home)<1.1){returning.delete(p);p.nav=null;}
      else move(p,home,dt,1.6,'retorno');
    }
    if(stage==='livre')return;
    if(stage==='rendicao'){
      if(now>=until){
        lastFine=charge(15+caseLevel*20);
        clear();notice(null,'AUTO DE INFRAÇÃO',lastFine>0?`Multa paga: R$ ${lastFine}. Você está liberado.`:'Ocorrência registrada. Você está liberado.');
      }
      return;
    }
    if(down()){clear();return;}
    if(stage==='chamada'){
      if(!present(call.witness)||!people.includes(call.witness)){clear();return;}
      if(now>=until)dispatch(call.spot,call.identified,null);
      return;
    }
    if(now>=scanAt){
      scanAt=now+.18;
      const target=position();
      visible=unit.filter(p=>sees(p,target,stage==='perseguicao'||stage==='abordagem'?35:25,true));
      if(identified&&!visible.length){
        const patrol=uniformed.find(p=>!unit.includes(p)&&sees(p,target,19));
        if(patrol){
          const relieved=unit.pop();if(relieved){relieved.nav=null;returning.add(relieved);}
          returning.delete(patrol);patrol.nav=null;unit.unshift(patrol);visible.push(patrol);
        }
      }
      if(visible.length&&(identified||threatening())){
        if(stage==='busca'||stage==='despacho')notice(visible[0],visible[0].name,'Ali! Pare onde está.');
        identified=true;lastSeen=now;lastKnown=copy(target);
        if(stage!=='abordagem')stage='perseguicao';
      }else visible=[];
    }
    if((stage==='perseguicao'||stage==='abordagem')&&now-lastSeen>2.1||
      stage==='despacho'&&(now>=until||unit.some(p=>distance(p.g.position,lastKnown)<5))){
      stage='busca';until=now+11+caseLevel*2;closeSince=0;
      notice(null,'RÁDIO DA PATRULHA','Sem contato visual. Procurem na última posição.');
    }
    if(stage==='busca'&&now>=until){
      clear();notice(null,'RÁDIO DA PATRULHA','Área verificada. Encerrando a busca.');return;
    }
    let close=null;
    for(const [index,p] of unit.entries()){
      if(!present(p)||p.push)continue;
      const pos=p.g.position,canFollow=visible.includes(p),gap=distance(pos,lastKnown);
      if(canFollow&&gap<3.35&&grounded()){close??=p;look(p,lastKnown);continue;}
      let destination=lastKnown;
      if(stage==='busca'&&gap<6){
        const phase=Math.floor((until-now)/3.5)+index*2;
        destination={x:lastKnown.x+Math.cos(phase*1.8)*3.5,z:lastKnown.z+Math.sin(phase*1.8)*3.5};
      }
      if(gap>1.8||stage==='busca')move(p,destination,dt,stage==='busca'?1.65:canFollow?(caseLevel>=3?6.9:5.7):3.7,stage);
    }
    if(close){
      if(stage!=='abordagem'){stage='abordagem';closeSince=now;notice(close,close.name,'Polícia! Pare e renda-se.');}
      if(now-closeSince>5&&!threatening())surrender();
    }else if(stage==='abordagem'){stage='perseguicao';closeSince=0;}
  }
  return {report,interrupt,update,surrender,approaching,cruiserSight,
    get order(){return lastKnown&&stage!=='rendicao'?{point:lastKnown,stage,identified,level:caseLevel}:null;},
    get device(){return call?.device??null;},get caller(){return call?.witness??null;},
    get custody(){return stage==='rendicao';},get tradeBlocked(){return ['perseguicao','abordagem','rendicao'].includes(stage);},
    controls:p=>call?.witness===p||unit.includes(p)||returning.has(p)||startled.has(p),
    snapshot:()=>({phase:stage,level:caseLevel,identified,lastKnown:lastKnown&&copy(lastKnown),offense:lastType,
      witness:call?.witness.name??null,officers:unit.map(p=>({name:p.name,x:p.g.position.x,z:p.g.position.z})),fine:lastFine})};
}

export function createPoliceRadio(T,scene){
  const canvas=document.createElement('canvas');canvas.width=768;canvas.height=146;
  const ink=canvas.getContext('2d'),texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
  const sign=new T.Sprite(new T.SpriteMaterial({map:texture,transparent:true,depthWrite:false,toneMapped:false}));
  sign.center.set(.5,0);sign.visible=false;scene.add(sign);
  const phone=new T.Group(),body=new T.Mesh(new T.BoxGeometry(.19,.34,.055),new T.MeshStandardMaterial({color:'#20323b'}));
  const screen=new T.Mesh(new T.BoxGeometry(.15,.26,.013),new T.MeshStandardMaterial({color:'#73c6bb',emissive:'#3e9288',emissiveIntensity:.6}));
  screen.position.z=.032;phone.add(body,screen);phone.visible=false;scene.add(phone);
  const ear=new T.Vector3();let anchor=null,expires=0,caller=null,handsUp=false;
  return {
    notify(actor,title,line,time){
      anchor=actor;expires=time+3.1;ink.clearRect(0,0,768,146);
      ink.fillStyle='#173439';ink.fillRect(8,5,752,124);
      ink.fillStyle='#8ad8c5';ink.fillRect(8,5,7,124);ink.font='bold 26px sans-serif';ink.fillText(title.toUpperCase(),35,44,690);
      ink.fillStyle='#f9f6e9';ink.font='bold 34px sans-serif';ink.fillText(line,35,98,696);texture.needsUpdate=true;
      sign.visible=true;
    },
    phone(actor,on){
      if(!on&&caller!==actor)return;
      phone.visible=on;caller=on?actor:null;
      if(!on&&actor.g.userData.limbs){const arm=actor.g.userData.limbs[3];arm.rotation.x=0;arm.rotation.z=0;}
    },
    update(time,camera,player,vehicle,surrendered){
      if(caller){
        const g=caller.g,arm=g.userData.limbs?.[3];
        if(arm){arm.rotation.x=-2.05;arm.rotation.z=-.28;}
        g.localToWorld(ear.set(.385,2.01,.045));phone.position.copy(ear);
        phone.rotation.set(0,g.rotation.y+1.4,-.12);phone.visible=g.visible;
      }
      const arms=player.userData.limbs;
      if(surrendered){
        for(const i of [2,3]){arms[i].rotation.x=-2.25;arms[i].rotation.z=(i===2?-1:1)*.42;}
      }else if(handsUp){arms[2].rotation.z=0;arms[3].rotation.z=0;}
      handsUp=surrendered;
      sign.visible=time<expires&&(!anchor||anchor.g.visible);
      if(!sign.visible)return;
      const subject=anchor?.g??vehicle?.g??player;
      sign.position.copy(subject.position);sign.position.y+=anchor?2.9*subject.scale.y:vehicle?3.1:3.15;
      const world=2*Math.tan(camera.fov*Math.PI/360)*camera.position.distanceTo(sign.position)/innerHeight;
      const width=Math.min(innerWidth*.76,300)*world;
      sign.scale.set(width,width*146/768,1);sign.material.opacity=Math.min(1,(expires-time)*3);
    }
  };
}
