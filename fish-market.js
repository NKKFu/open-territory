// Galpão da Maré: an open workshop on the unused eastern coastal parcel.
// All interaction state stays here; the city supplies its existing E and speech APIs.
export function createFishMarket({THREE:T,scene,block,part,solid,sign,human,groundHeight,solids,cameraObstacles,canServe=()=>true}){
  const x=156,z=-123,floor=.23,wood='#9e7959',teal='#467e7b',cream='#eee1bd';
  const survey=solids.filter(s=>s.active&&Math.abs(s.x-x)<s.w/2+9&&Math.abs(s.z-z)<s.d/2+8)
    .map(s=>({x:s.x,z:s.z,w:s.w,d:s.d}));
  // The footprint ends before the fenced vacant lot (z=-113) and stays east
  // of the side avenue and its sidewalk (x<=141). No road is extended here.
  block(x,.12,z,21,.22,18,'#ded8c6');
  block(143,.12,-120,10,.22,3.2,'#ded8c6');
  block(168,.01,-137,3.2,.22,14,'#e1c49a');
  function box(px,py,pz,w,h,d,color,collision=false,opaque=false){
    block(px,floor+py,pz,w,h,d,color);
    if(collision){const s=solid(px,pz,w,d);s.opaque=opaque;}
  }
  function volume(px,pz,w,d,top){
    const collider=solid(px,pz,w,d);collider.vaultTop=floor+top;return collider;
  }
  // Slatted north wall, partial east wall, two wide open physical entrances.
  box(156,1.7,-115.8,17,3.4,.3,teal,true,true);
  box(164.4,1.7,-118.4,.3,3.4,5.2,teal,true,true);
  for(const px of [147.6,164.4])for(const pz of [-129.8,-115.8])box(px,2.1,pz,.32,4.2,.32,wood,true,true);
  for(const px of [147.6,164.4])box(px,4.1,z,.35,.24,14.8,wood);
  // Separate lean-to roofs preserve a view into the open central court.
  for(const [cx,w] of [[150.1,5.7],[162,5.7]]){
    box(cx,4.25,z,w,.22,15.3,teal);
    for(let dz=-130;dz<=-116;dz+=.72)box(cx,4.39,dz,w,.08,.12,'#659b94');
    cameraObstacles.push({x:cx,z,w,d:15.3,bottom:floor+4.1,top:floor+4.5});
  }
  for(const cx of [150.1,162])box(cx,4.05,-130,5.7,.35,.2,wood);
  cameraObstacles.push(
    {x:156,z:-115.8,w:17,d:.3,bottom:floor,top:floor+3.4},
    {x:164.4,z:-118.4,w:.3,d:5.2,bottom:floor,top:floor+3.4});
  sign('GALPÃO DA MARÉ',150.1,floor+5.1,-130.15,5.5,{rotation:Math.PI,background:teal});
  sign('GALPÃO DA MARÉ',150.1,floor+5.1,-129.85,5.5,{background:teal});
  sign('PEIXE · BALANÇA · REDES',156,floor+2.7,-115.98,3.8,{rotation:Math.PI,background:wood,border:false});
  box(143.5,1.55,-118,.18,3.1,.18,wood,true);
  sign('PEIXARIA →',143.5,2.85,-117.88,3.4,{background:teal});
  sign('← PEIXARIA',143.5,2.85,-118.12,3.4,{rotation:Math.PI,background:teal});
  // The vendor's ice counter has room to walk around either end.
  box(151, .65,-120,4.5,1.3,1.15,teal);
  box(151,1.33,-120,4.65,.12,1.35,cream);
  volume(151,-120,4.65,1.35,1.39);
  for(let i=0;i<15;i++)box(149.1+(i%5)*.9,1.43,-120.35+Math.floor(i/5)*.32,.38,.12,.22,'#d9edf0');
  function fish(parent,px,py,pz,size=1){
    const g=new T.Group();g.position.set(px,py,pz);parent.add(g);
    part(g,0,0,0,.67*size,.16*size,.23*size,'#9ec1bd');
    part(g,-.38*size,0,0,.18*size,.10*size,.37*size,'#507e86');
    part(g,.22*size,.04,.126*size,.045,.045,.025,'#294c53');
    part(g,0,.12*size,0,.21*size,.13*size,.05,'#507e86');return g;
  }
  const fixed=new T.Group();scene.add(fixed);
  for(let i=0;i<5;i++)fish(fixed,149.5+i*.75,floor+1.55,-120,.83);
  const vendor=human('#e5c485','#c28d6e','#3b5e65','#d0c8ac');
  vendor.position.set(151,floor,-117.9);vendor.rotation.y=Math.PI;scene.add(vendor);
  part(vendor,0,1.15,.26,.63,.95,.08,'#537d8a');
  const head=vendor.userData.head||vendor,neck=head===vendor?0:head.position.y;
  part(head,0,2.48-neck,0,.76,.13,.66,cream);
  part(head,0,2.56-neck,0,.54,.18,.48,cream);
  const worker={g:vendor,name:'Seu Nilo',role:'fishvendor',stationary:true,counterWorker:true,indoor:false,phase:0};
  // The standing body occupies the aisle in the walking/vehicle grid. Shots
  // use the resident's hit volume, and a fallen body vacates this footprint.
  const bodyCollider=solid(151,-117.9,.7,.65);bodyCollider.bodyOwner=worker;worker.bodyCollider=bodyCollider;
  // Net rack, floats, coiled ropes and stacked vented crates.
  for(const px of [155,159])box(px,1.65,-116.6,.13,3.3,.13,wood,true);
  box(157,3.1,-116.6,4.3,.12,.12,wood);
  for(let i=0;i<12;i++)box(155+i*.36,1.95,-116.65,.035,2.25,.04,'#78958c');
  for(let i=0;i<8;i++)box(157, .9+i*.29,-116.65,4,.035,.04,'#78958c');
  // This net hangs down to shin height, so its cloth blocks the body.
  // The high crossbar and floats above it do not create ground-level walls.
  solid(157,-116.65,4,.06);
  for(let i=0;i<5;i++)box(155.2+i*.82,3,-116.6,.24,.35,.24,i%2?cream:'#d48963');
  function crate(px,pz,y,color,height=.55){
    if(y===0)volume(px,pz,1.26,.95,height);
    box(px,y+.08,pz,1.25,.16,.95,color);
    for(const side of [-1,1])for(const dy of [.22,.47]){
      box(px+side*.58,y+dy,pz,.1,.16,.95,color);
      box(px,y+dy,pz+side*.43,1.25,.16,.09,color);
    }
  }
  for(let i=0;i<3;i++)crate(163,-117.2,i*.6,wood,1.75);
  for(let i=0;i<3;i++){
    const rope=new T.Mesh(new T.TorusGeometry(.48-i*.1,.045,4,12),new T.MeshStandardMaterial({color:wood}));
    rope.rotation.x=Math.PI/2;rope.position.set(149,floor+.1+i*.08,-125.7);scene.add(rope);
  }
  volume(149,-125.7,1.05,1.05,.305);
  // Working balance: the fish load and the iron weights can be changed
  // independently. The beam and both pans visibly react; there is no score.
  box(160,.65,-120.6,3.5,1.3,1.35,wood);
  box(160,1.36,-120.6,3.7,.13,1.5,cream);
  volume(160,-120.6,3.7,1.5,1.425);
  box(160,1.9,-120.6,.14,1,.14,teal);
  const beam=new T.Group();beam.position.set(160,floor+2.3,-120.6);scene.add(beam);
  part(beam,0,0,0,2.6,.12,.12,teal);
  const pans=[-1,1].map(side=>{
    const g=new T.Group();scene.add(g);
    part(g,0,-.48,0,1.1,.1,.85,'#a7bcb6');
    for(const dx of [-.45,.45])part(g,dx,-.24,0,.025,.48,.025,teal);
    return g;
  });
  const load=Array.from({length:4},(_,i)=>fish(pans[0],(i%2-.5)*.43,-.35+Math.floor(i/2)*.16,0,.58));
  const weights=Array.from({length:4},(_,i)=>part(pans[1],(i%2-.5)*.35,-.29+Math.floor(i/2)*.22,0,.26,.25,.28,'#4e6267'));
  crate(162.6,-124,0,teal);crate(157.4,-124,0,wood);
  for(let i=0;i<3;i++)fish(fixed,162.25+i*.35,floor+.67,-124,.55);
  for(let i=0;i<3;i++)box(157.05+i*.35,.67,-124,.24,.3,.25,'#4e6267');
  sign('E · ESCOLHER PEIXES',162.6,floor+1.4,-124.51,2.6,{rotation:Math.PI,background:teal});
  sign('E · TROCAR PESOS',157.4,floor+1.4,-124.51,2.6,{rotation:Math.PI,background:wood});
  sign('CADA PEIXE / PESO = 500 g',160,floor+2.9,-119.7,3.7,{rotation:Math.PI,background:teal});
  // The weigh ticket sits at the free end of the scale counter, facing the crates.
  box(158.58,1.57,-120.97,.63,.28,.44,'#415f57');
  box(158.58,1.71,-121.07,.46,.075,.27,cream);
  sign('EXPEDIÇÃO',158.57,floor+1.08,-121.365,1.22,{rotation:Math.PI,background:teal});
  sign('E · SEU NILO',151,floor+2.9,-118.4,2.7,{rotation:Math.PI,background:teal});
  let fishCount=2,weightCount=0,angle=0,talkCount=0,respondUntil=0;
  const targets=[{id:'vendor',attendant:worker,x:151,z:-120.9,display:{x:151,y:floor+3.2,z:-121.4}},
    {id:'fish',x:162.6,z:-124,display:{x:160,y:floor+3.2,z:-120.6}},
    {id:'weights',x:157.4,z:-124,display:{x:160,y:floor+3.2,z:-120.6}},
    {id:'dispatch',attendant:worker,x:158.58,z:-121.6,display:{x:158.58,y:floor+2.82,z:-121.3}}];
  const lines=['Sou o Nilo. A pesca chega da praia; os caixotes seguem para o bairro por esta passagem.',
    'Quer sentir o peso? E no caixote azul troca os peixes; E na caixa de madeira troca os pesos. Cada um vale meio quilo.',
    'Quando os pratos ficam na mesma altura, o peso bate. Pode mexer à vontade: a balança é de todo mundo.',
    'Precisa ganhar dinheiro? O recibo ao lado da balança indica o peso do caixote. Iguale os pratos e entregue ali.'];
  return {
    worker,
    nearest(position,reachable,maxDistance){
      let best=null,distance=maxDistance;
      for(const t of targets){if(t.attendant&&!canServe(worker))continue;const d=Math.hypot(position.x-t.x,position.z-t.z);
        if(d<distance&&d<(t.id==='vendor'?2.6:1.85)&&reachable(t.x,t.z)){best=t;distance=d;}}
      return best?{type:'fish-market',data:best,distance}:null;
    },
    interact(target,speak,time,dispatch){
      if(target.attendant&&!canServe(worker))return;
      if(target.id==='dispatch'){if(dispatch)speak(target,'Expedição',dispatch(fishCount,weightCount).line);return;}
      if(target.id==='vendor'){respondUntil=time+7;speak(target,'Seu Nilo',lines[talkCount++%lines.length]);return;}
      if(target.id==='fish')fishCount=fishCount%4+1;else weightCount=(weightCount+1)%5;
      const difference=(fishCount-weightCount)*.5;
      speak(target,'Balança da Maré',`${fishCount*.5} kg de peixe; ${weightCount*.5} kg de pesos. ${difference===0?'Os pratos estão equilibrados.':difference>0?'O prato dos peixes desce.':'O prato dos pesos desce.'}`);
    },
    update(dt,time,position){
      const desired=T.MathUtils.clamp((fishCount-weightCount)*.10,-.35,.35);
      angle+=(desired-angle)*(1-Math.exp(-dt*5));beam.rotation.z=angle;
      pans.forEach((g,i)=>{const side=i===0?-1:1;g.position.set(160+side*1.12*Math.cos(angle),floor+2.3+side*1.12*Math.sin(angle),-120.6);});
      load.forEach((g,i)=>g.visible=i<fishCount);weights.forEach((g,i)=>g.visible=i<weightCount);
      if(!canServe(worker)){respondUntil=0;return;}
      const close=time<respondUntil&&Math.hypot(position.x-vendor.position.x,position.z-vendor.position.z)<7;
      const heading=close?Math.atan2(position.x-151,position.z+117.9):Math.PI;
      vendor.rotation.y+=Math.atan2(Math.sin(heading-vendor.rotation.y),Math.cos(heading-vendor.rotation.y))*(1-Math.exp(-dt*5));
      vendor.userData.limbs[2].rotation.x=close?-.35+Math.sin(time*3)*.13:Math.sin(time*.7)*.04;
    },
    snapshot:()=>({x,z,survey,fishKg:fishCount*.5,weightsKg:weightCount*.5,angle,talkCount,serving:canServe(worker)}),
    spawn:{x:155,z:-134,yaw:Math.PI,pitch:.28},
    debugSpawns:{'fish-stack':{x:163,z:-119},'fish-net':{x:157,z:-118.5},'fish-clearance':{x:155,z:-127}}
  };
}
