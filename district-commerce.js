// Fixed, readable signs belong to the counters and streets: trading doesn't
// interrupt the simulation or require a second set of menu controls.
const navy='#233f47',ink='#203843',mint='#b2d1be',paper='#f2e6c7',sun='#e3ad5d',red='#ac6554';
function board(T,scene,x,y,z,w,h,paint,rotation=0){
  const canvas=document.createElement('canvas');canvas.width=850;canvas.height=Math.round(850*h/w);
  paint(canvas.getContext('2d'),canvas.width,canvas.height);
  const tex=new T.CanvasTexture(canvas);tex.colorSpace=T.SRGBColorSpace;tex.anisotropy=4;
  const m=new T.Mesh(new T.PlaneGeometry(w,h),new T.MeshBasicMaterial({map:tex,toneMapped:false}));
  m.position.set(x,y,z);m.rotation.y=rotation;scene.add(m);return m;
}
function letters(c,copy,x,y,size,color,heavy=true,maxWidth){
  c.fillStyle=color;c.font=(heavy?'750 ':'530 ')+size+'px "Segoe UI",system-ui,sans-serif';
  c.fillText(copy,x,y,maxWidth);
}
function plate(T,scene,x,y,z,w,h,kicker,title,caption,accent=mint,rotation=0){
  return board(T,scene,x,y,z,w,h,(c,a,b)=>{
    c.fillStyle=navy;c.fillRect(0,0,a,b);c.fillStyle=accent;c.fillRect(0,0,18,b);
    letters(c,kicker,44,b*.21,b*.13,accent);letters(c,title,40,b*.62,b*.35,paper,true,a-70);
    letters(c,caption,44,b*.85,b*.16,paper,false,a-72);
  },rotation);
}
export function furnishArmory(place,{THREE:T,scene,block,furniture,fittedBlock,furnitureBounds,groundHeight,servicePoints}){
  const {x,z,d}=place,floor=groundHeight(x,z),back=z-d/2+.25;
  const local=(dx,y,dz,ww,h,dd,c)=>block(x+dx,y,z+dz,ww,h,dd,c);
  // Mount examples in the rear wall and fit a smaller secured example to each tray.
  function sample(cx,y,cz,id,s=1){
    const piece=(dx,dy,ww,h,col,depth=.18)=>local(cx+s*dx,y+s*dy,cz,s*ww,s*h,s*depth,col);
    if(id==='pulse'){
      piece(-.16,0,.97,.28,'#83b4ab');piece(-.13,-.20,.22,.29,ink);
      for(const side of [-1,1])piece(.47,side*.10,.32,.09,sun);
      piece(-.27,.03,.24,.12,paper,.20);
    }else{
      const long=id==='carbine';
      piece(0,0,long?1.45:.9,.25,'#53676b');piece(-.16,-.19,.22,.28,'#ae896f');
      piece(long?.89:.53,.025,long?.69:.29,.105,ink);piece(.16,.17,.16,.085,ink);
      if(long){piece(-.90,-.05,.56,.22,'#b79070');piece(.22,-.19,.2,.28,ink);piece(.68,.015,.36,.20,'#9e8067');}
    }
  }
  function counter(dx,dz,width,depth=1.7){
    furniture(x+dx,z+dz,width,depth,1.04,navy);
    fittedBlock(x+dx,1.10,z+dz,width+.09,.12,depth+.12,'#c1b296');
    local(dx,.28,dz+depth/2+.018,width*.91,.075,.03,sun);
    for(const inset of [-1,1])local(dx+inset*width*.41,.67,dz+depth/2+.025,.06,.66,.05,'#68877e');
  }
  function target(kind,id,px,pz,anchorZ,height){
    const point={kind:'commerce',mode:kind,id,x:px,z:pz,place,frontOnly:true,
      anchor:{x:px,z:anchorZ,height},display:{x:px,y:floor+2.77,z:anchorZ}};
    servicePoints.push(point);return point;
  }
  const wares=[[-6.6,'pulse','01 / CARGAS','PULSOR MARÉ','Não letal  ·  4 cargas / R$ 16',mint],
    [0,'sidearm','02 / SEMIAUTOMÁTICA','P-9 FAROL','R$ 180  ·  12 cartuchos / R$ 24',sun],
    [6.6,'carbine','03 / AUTOMÁTICA','C-12 ORLA','R$ 360  ·  20 cartuchos / R$ 40','#e49982']];
  const glass=new T.MeshBasicMaterial({color:'#a8d9d4',transparent:true,opacity:.115,depthWrite:false});
  const caseGeo=new T.BoxGeometry(3.74,.73,1.07);
  for(const [dx,id,code,name,line,accent] of wares){
    const dz=-4.9;
    counter(dx,dz,4.7);
    local(dx,1.195,dz,3.65,.07,.98,'#5c7d77');
    sample(dx,1.50,dz,id,.80);
    const vitrine=new T.Mesh(caseGeo,glass);vitrine.position.set(x+dx,floor+1.565,z+dz);scene.add(vitrine);
    for(const side of [-1,1]){
      for(const edge of [-1,1])local(dx+side*1.85,1.57,dz+edge*.51,.038,.73,.038,'#79978a');
      local(dx,1.94,dz+side*.51,3.73,.044,.045,'#95b3a2');
      local(dx+side*1.85,1.94,dz,.045,.044,1.04,'#95b3a2');
    }
    furnitureBounds(x+dx,z+dz,3.75,1.08,floor+1.175,floor+1.965);
    local(dx,1.31,dz+.66,1.58,.28,.08,ink);
    plate(T,scene,x+dx,floor+1.32,z+dz+.705,1.52,.30,code,id==='pulse'?'CARGAS':'AQUISIÇÃO','OFICINA FAROL',accent);
    // Each permanently mounted rack lives at the back wall; the aisles around the displays stay open.
    local(dx,2.48,back-z+.28,5.32,3.38,.17,'#46625e');
    for(let i=0;i<6;i++)local(dx,1.15+i*.5,back-z+.38,5.06,.045,.06,'#789385');
    sample(dx,2.84,back-z+.48,id,1.35);
    furnitureBounds(x+dx,back+.33,5.32,.50,floor+.78,floor+4.17);
    plate(T,scene,x+dx,floor+1.47,back+.56,4.68,1.26,code,name,line,accent);
    target('buy',id,x+dx,z+dz+1.18,z+dz+.78,2.06);
  }
  // Two separate intake trays: tapping E again at a sales display can never sell the new gun.
  counter(-6.45,4.8,6.9,1.55);
  for(const [dx,id,title,value] of [[-8.22,'sidearm','P-9 FAROL','R$ 105'],[-4.67,'carbine','C-12 ORLA','R$ 205']]){
    local(dx,1.19,4.8,2.72,.065,1.11,'#a3b3a6');
    for(const off of [-1,1])local(dx+off*1.37,1.24,4.8,.06,.14,1.2,'#627670');
    plate(T,scene,x+dx,floor+.72,z+5.61,2.47,.57,'RECOMPRA',title,value,sun);
    target('sell',id,x+dx,z+5.96,z+5.58,1.88);
  }
  counter(6.65,4.8,4,1.55);
  local(6.73,1.45,4.74,1.02,.64,.23,'#375b5c');
  local(6.73,1.48,4.867,.78,.37,.035,'#b9ccab');
  local(5.67,1.21,4.9,.42,.1,.47,'#d9bb81');
  target('ledger',null,x+6.65,z+5.96,z+5.58,1.93);
  plate(T,scene,x+6.65,floor+.73,z+5.61,2.84,.57,'CAIXA','EXTRATO','Saldo e equipamento',mint);
  // Front and rear of the building are both identifiable from the city streets.
  plate(T,scene,x,floor+3.53,z+d/2+.275,3.05,.63,'OFICINA FAROL','ARMARIA','Compra e recompra',sun);
  plate(T,scene,x+7.35,floor+2.15,z+d/2+.29,4.0,1.08,'PÁTIO LATERAL','ESTANDE →','Três baias de prática',mint);
  plate(T,scene,x,floor+3.13,z-d/2-.225,8.1,2.36,'BAÍA DO SOL  /  04','OFICINA FAROL','Armas e cargas  ·  Entrada pela outra rua',sun,Math.PI);
}
export function installDistrictGuide({THREE:T,scene,part,solid,groundHeight,cameraObstacles,servicePoints}){
  const x=-22.65,z=54.90,base=groundHeight(x,z),w=3.85,h=2.48,y=base+2.02;
  for(const dx of [-1.54,1.54]){
    part(scene,x+dx,base+1.12,z,.14,2.24,.16,'#496260');solid(x+dx,z,.14,.16);
  }
  part(scene,x,y,z,w+.12,h+.12,.18,'#4c625f');
  const bounds=solid(x,z,w+.12,.18);bounds.bottom=y-(h+.12)/2;bounds.supportTop=y+(h+.12)/2;
  cameraObstacles.push({x,z,w:w+.12,d:.18,bottom:bounds.bottom,top:bounds.supportTop});
  const paint=(c,a,b)=>{
    c.fillStyle=paper;c.fillRect(0,0,a,b);c.fillStyle=navy;c.fillRect(0,0,a,b*.23);
    letters(c,'GUIA DE BAIRRO     /     01',26,31,18,mint);
    letters(c,'Baía do Sol',23,b*.189,57,paper);letters(c,'PRAÇA DO IPÊ',a*.70,b*.16,21,sun);
    const rect={x:24,y:b*.27,w:a*.55,h:b*.62},px=x=>rect.x+(x+195)/390*rect.w,pz=z=>rect.y+(165-z)/328*rect.h;
    c.fillStyle='#bdccb3';c.fillRect(rect.x,rect.y,rect.w,rect.h);c.fillStyle='#74aaa9';c.fillRect(rect.x,pz(-125),rect.w,rect.y+rect.h-pz(-125));
    c.lineCap='round';c.strokeStyle=paper;c.lineWidth=6;
    function street(points){c.beginPath();points.forEach(([x,z],i)=>i?c.lineTo(px(x),pz(z)):c.moveTo(px(x),pz(z)));c.stroke();}
    for(const axis of [-130,-72,0,72,130])street([[axis,-131],[axis,140]]);
    for(const axis of [-130,-72,0,72])street([[-133,axis],[134,axis]]);
    street([[-178,131],[-100,149],[5,139],[123,150],[176,137]]);
    const entries=[[-26,55,'A','Você está aqui',navy],[36,104,'B','Oficina Farol',red],[104,36,'C','Hospital Baía','#547a61'],
      [156,-123,'D','Galpão da Maré','#387f86'],[168,-153,'E','Cais das Antenas','#a16542'],[-53,-37,'F','Cine da Passagem','#825765']];
    for(const [i,[x,z,id,title,color]] of entries.entries()){
      const marker=(cx,cy,r)=>{c.fillStyle=color;c.beginPath();c.arc(cx,cy,r,0,2*Math.PI);c.fill();c.textAlign='center';letters(c,id,cx,cy+r*.42,r*1.28,'#fff8de');c.textAlign='left';};
      marker(px(x),pz(z),13);
      const cy=rect.y+rect.h*(i+.53)/entries.length;marker(a*.64,cy,14);letters(c,title,a*.68,cy+8,24,ink,true,a*.30);
    }
    letters(c,'ORLA   /   COLINAS   /   BAIRROS',26,b*.966,16,'#42605b');
  };
  board(T,scene,x,y,z+.096,w,h,paint);board(T,scene,x,y,z-.096,w,h,paint,Math.PI);
  for(const side of [-1,1])servicePoints.push({kind:'district-guide',x,z:z+side*.58,anchor:{x,z:z+side*.13,height:2.95},
    display:{x,y:base+3.64,z:z+side*.14}});
}
