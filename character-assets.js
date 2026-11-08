import * as THREE from './vendor/three.module.js';

// Feet and palms belong to the rig. The costume only adds rigid pieces below
// those joints, so the same few geometries can be instanced in every pose.
const materials=new Map(),geometry=new Map();
const ink='#354044',sole='#c8c9b4',shoe='#38494e';
export const characterStyles=['jaqueta','malha','camisa','corta-vento','colete','cardigã','polo','bolsa'];
export const residentRig={hipHeight:.84,ankleHeight:.08,thighLength:.41,shinLength:.35,upperLength:.38,lowerLength:.40};
function matte(color){
  const key=color||'#b89976';
  if(!materials.has(key))materials.set(key,new THREE.MeshStandardMaterial({color:key,roughness:.90,metalness:0}));
  return materials.get(key);
}
function slabGeometry(kind='bevel'){
  if(geometry.has(kind))return geometry.get(kind);
  const lower=kind==='coat'?.87:kind==='limb'?.80:1,upper=kind==='coat'?1:kind==='limb'?1:1;
  const cut=kind==='bevel'?.13:kind==='hair'?.21:.07,vertices=[];
  const ring=[[-.5+cut,-.5],[.5-cut,-.5],[.5,-.5+cut],[.5,.5-cut],[.5-cut,.5],[-.5+cut,.5],[-.5,.5-cut],[-.5,-.5+cut]];
  const points=ring.map(([x,z])=>[x*lower,-.5,z]).concat(ring.map(([x,z])=>[x*upper,.5,z]));
  const tri=(a,b,c)=>vertices.push(...points[a],...points[c],...points[b]);
  for(let i=1;i<7;i++){tri(0,i+1,i);tri(8,8+i,9+i);}
  for(let i=0;i<8;i++){const j=(i+1)%8;tri(i,j,j+8);tri(i,j+8,i+8);}
  const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));g.computeVertexNormals();
  g.userData.cityBatch=true;g.userData.characterShape=kind;geometry.set(kind,g);return g;
}
function facet(parent,x,y,z,w,h,d,color,kind='bevel'){
  const mesh=new THREE.Mesh(slabGeometry(kind),matte(color));mesh.position.set(x,y,z);mesh.scale.set(w,h,d);
  mesh.castShadow=true;mesh.receiveShadow=true;parent.add(mesh);return mesh;
}
function box(part,parent,x,y,z,w,h,d,color){return part(parent,x,y,z,w,h,d,matte(color));}
function joint(parent,x,y,z,name){const g=new THREE.Group();g.position.set(x,y,z);g.name=name;parent.add(g);return g;}
function tone(color,light=.0){return '#'+new THREE.Color(color||'#b89976').offsetHSL(0,0,light).getHexString();}

export function buildCharacterHead(head,{part,skin='#e7b896',hair='#4c4641',style=0,compact=false}){
  const s=((style%8)+8)%8,face=(...args)=>box(part,head,...args),hairParts=[];
  const hairPiece=(x,y,z,w,h,d,rotation=0)=>{const p=facet(head,x,y,z,w,h,d,hair,'hair');p.rotation.z=rotation;hairParts.push(p);return p;};
  facet(head,0,.26,0,.55,.57,.50,skin);
  face(0,-.012,-.005,.22,.15,.24,skin);
  for(const side of [-1,1]){
    facet(head,side*.286,.255,-.005,.084,.15,.12,skin);
    face(side*.143,.29,.253,.063,.061,.024,ink);
    const brow=face(side*.145,.367,.26,.127,.033,.028,hair);brow.rotation.z=side*(s===2?-.075:.035);
  }
  facet(head,0,.22,.287,.09,.13,.108,skin,'limb');
  face(0,.115,.252,.133,.025,.022,tone(skin,-.18));
  if(s===0||compact){
    hairPiece(0,.55,-.036,.57,.15,.49);hairPiece(-.105,.478,.188,.34,.09,.15,-.12);
    hairPiece(-.245,.41,-.06,.09,.16,.36);
  }else if(s===1){
    hairPiece(0,.53,-.08,.61,.20,.51);hairPiece(0,.285,-.246,.61,.51,.14);
    for(const side of [-1,1])hairPiece(side*.277,.315,-.028,.12,.39,.42,side*.025);
    hairPiece(-.135,.453,.19,.32,.12,.15,-.16);
  }else if(s===2){
    for(const [x,y,z,w,h,d] of [[-.19,.535,-.02,.23,.23,.41],[.08,.575,-.055,.31,.24,.46],[.255,.49,-.10,.17,.23,.34],[-.1,.515,.19,.25,.15,.20]])hairPiece(x,y,z,w,h,d);
  }else if(s===3){
    hairPiece(0,.515,-.035,.57,.12,.50);hairPiece(0,.435,-.245,.54,.22,.07);
    for(const side of [-1,1])hairPiece(side*.255,.435,-.02,.055,.14,.34);
  }else if(s===4){
    hairPiece(0,.52,-.075,.57,.18,.46);hairPiece(0,.43,-.31,.29,.28,.25);
    hairPiece(-.15,.438,.17,.29,.15,.15,-.17);
  }else if(s===5){
    hairPiece(0,.54,-.075,.60,.17,.46);hairPiece(0,.19,-.245,.57,.63,.17);
    hairPiece(-.265,.25,-.04,.14,.48,.36);hairPiece(.26,.29,-.08,.13,.43,.30);
    hairPiece(-.15,.45,.18,.32,.15,.13,-.13);
  }else if(s===6){
    hairPiece(0,.52,-.10,.57,.20,.41);hairPiece(.055,.59,.09,.43,.17,.28,.08);
    hairPiece(.245,.42,-.065,.08,.17,.34);
  }else{
    hairPiece(0,.51,-.055,.56,.13,.47);hairPiece(0,.395,-.23,.55,.26,.08);
    face(0,.085,.24,.32,.135,.055,hair);face(0,.136,.278,.14,.018,.025,tone(skin,-.22));
  }
  if(!compact&&(s===4||s===7)){
    for(const side of [-1,1]){
      face(side*.149,.292,.278,.227,.032,.036,ink);face(side*.149,.219,.277,.227,.025,.035,ink);
      face(side*.251,.256,.276,.026,.081,.035,ink);face(side*.047,.256,.276,.026,.081,.035,ink);
      face(side*.276,.29,.133,.025,.025,.28,ink);
    }
    face(0,.28,.29,.073,.024,.028,ink);
  }
  if(!compact&&(s===1||s===4))for(const side of [-1,1])face(side*.292,.16,.005,.04,.092,.04,'#d4b776');
  head.userData.hairParts=hairParts;head.userData.characterStyle=s;
  return head;
}

function torso(rig,{part,shirt,skin,trousers,style,player=false,driver=false}){
  const {hips,chest}=rig,s=style%8,boxAt=(...args)=>box(part,chest,...args);
  const center=driver?.30:player?.39:.44,height=driver?.58:player?.79:.92,width=driver?.52:player?.74:.75,depth=driver?.38:.46;
  const pale=player?'#eee3c9':'#e8ddc3',edge=tone(shirt,-.09),front=depth/2;
  facet(chest,0,center,0,width,height,depth,shirt,'coat');
  if(hips)facet(hips,0,.04,0,driver?.52:.64,.22,driver?.40:.42,trousers,'coat');
  const yTop=center+height/2,yBottom=center-height/2;
  boxAt(0,yTop+.015,0,.22,.13,.25,skin);
  const details=joint(chest,0,0,0,'costume-details');rig.outerwear=details;
  const detail=(...args)=>box(part,details,...args),w=width/.75;
  if([0,2,4,5].includes(s)){
    detail(0,center,front+.008,.32*w,height*.94,.026,pale);
    for(const side of [-1,1]){
      const panel=detail(side*width*.30,center,front+.025,width*.30,height,.055,shirt);panel.rotation.z=side*(s===5?.028:.012);
      const lapel=detail(side*.13*w,yTop-.11,.265*depth/.46,.11*w,.23,.055,edge);lapel.rotation.z=side*.25;
    }
    if(s===0||s===2||s===4)for(const side of [-1,1]){
      detail(side*width*.31,center+.04,front+.064,.17*w,.17,.036,edge);
      detail(side*width*.31,center+.10,front+.089,.18*w,.044,.023,shirt);
    }
    if(s===5)detail(0,yBottom-.06,-.035,width*.94,.23,depth*.92,shirt);
  }else if(s===1){
    detail(0,center+.03,front+.011,width*.99,.14,.028,pale);
    detail(0,center-.19,front+.011,width*.99,.065,.028,edge);
    detail(0,yBottom+.045,0,width*1.015,.09,depth*1.015,edge);
  }else if(s===3){
    facet(details,0,yTop-.08,-depth*.55,width*.65,.32,.26,edge,'hair');
    detail(0,center,front+.02,.036,height*.93,.03,pale);
    detail(0,center-.19,front+.025,width*.66,.20,.065,edge);
    for(const side of [-1,1])detail(side*.1*w,yTop-.14,front+.04,.025,.25,.03,pale);
  }else if(s===6){
    for(const side of [-1,1]){const collar=detail(side*.10*w,yTop-.05,front+.025,.14*w,.13,.04,pale);collar.rotation.z=side*.3;}
    detail(0,yTop-.15,front+.02,.04,.20,.035,pale);detail(-width*.25,center+.15,front+.02,.10,.11,.03,edge);
  }
  if(s===7||player){
    const side=player?-1:1;
    const strap=detail(-.03,center,side*(front+.07),.075,height*1.14,.041,'#405451');strap.rotation.z=-.54;
    facet(details,-width*.23,center-.10,side*(front+.12),width*.58,height*.40,.17,'#425a55','coat');
    detail(-width*.23,center+.03,side*(front+.215),width*.48,.042,.032,'#8ba697');
    if(player){
      detail(0,yTop-.045,-front-.025,width*.61,.095,.046,edge);
      detail(0,yBottom+.045,-front-.016,width*.94,.069,.033,edge);
    }
  }
  if(!driver&&(s===2||s===3)&&!player){
    facet(details,0,center,-.34,.51,.55,.22,s===3?'#a08060':'#6b817a','coat');
    detail(0,center-.13,-.468,.37,.18,.045,s===3?'#b09370':'#82958a');
    for(const side of [-1,1])detail(side*.24,center+.07,front+.024,.055,height*.78,.03,'#485b53');
  }
  return {center,height,width};
}

export function dressCharacterRig(rig,{part,shirt='#d8a968',skin='#e7b896',trousers='#415b62',hair='#4c4641',style=0,player=false}){
  const s=((style%8)+8)%8,{upperLength,lowerLength,thighLength,shinLength}=rig.measurements;
  torso(rig,{part,shirt,skin,trousers,style:s,player});
  buildCharacterHead(rig.head,{part,skin,hair,style:s});
  const short=[1,4,6,7].includes(s),armWidth=player?.25:.24;
  rig.arms.forEach(({shoulder,elbow,hand},index)=>{
    facet(shoulder,0,-upperLength*.48,0,armWidth,upperLength+.03,.27,shirt,'limb');
    if(short){
      box(part,shoulder,0,-upperLength+.04,0,armWidth+.012,.07,.28,tone(shirt,-.08));
      facet(elbow,0,-lowerLength*.43,0,.19,lowerLength*.88,.21,skin,'limb');
    }else{
      facet(elbow,0,-lowerLength*.33,0,.218,lowerLength*.72,.245,shirt,'limb');
      box(part,elbow,0,-lowerLength*.73,0,.224,.072,.25,tone(shirt,-.09));
      facet(elbow,0,-lowerLength*.88,0,.18,.13,.205,skin);
    }
    facet(hand,0,0,.012,.205,.15,.20,skin);
    box(part,hand,index===0?.106:-.106,.006,.039,.05,.09,.095,skin);
    if(s===4&&index===0)box(part,elbow,0,-lowerLength*.84,0,.20,.055,.23,'#48534e');
  });
  rig.legs.forEach(({hip,knee,shoe:ankle})=>{
    facet(hip,0,-thighLength*.46,0,.285,thighLength+.035,.325,trousers,'limb');
    facet(knee,0,-shinLength*.45,0,.25,shinLength+.025,.285,trousers,'limb');
    const soleY=player?-.1:-.055,shoeY=player?-.035:.005;
    facet(ankle,0,shoeY,.063,.30,.12,.43,s%3===1?'#aeb2a6':shoe);
    box(part,ankle,0,soleY,.069,.305,.05,.44,sole);
    box(part,ankle,0,shoeY+.047,.13,.17,.025,.135,s%3===1?'#e2e0cc':'#83948c');
    if(s===2||s===4)box(part,knee,0,-shinLength*.79,.005,.259,.074,.293,tone(trousers,.08));
  });
  rig.root.userData.characterStyle=s;rig.root.userData.characterModel=characterStyles[s];
  rig.root.userData.outerwear=rig.outerwear;return rig;
}

export function createCharacter({part,shirt,skin,trousers,hair,style=0,seatHeight=null}){
  const root=new THREE.Group(),m={...residentRig};
  if(seatHeight!==null){m.hipHeight=seatHeight+.10;m.thighLength=.46;m.shinLength=Math.max(.38,seatHeight-.02);m.seated=true;m.seatHeight=seatHeight;}
  const hips=joint(root,0,m.hipHeight,0,'hips'),chest=joint(hips,0,.02,0,'chest'),head=joint(chest,0,.90,0,'head');
  const arms=[],legs=[];
  for(const side of [-1,1]){
    const label=side<0?'left':'right',hip=joint(hips,side*.2,0,0,label+'-hip'),knee=joint(hip,0,-m.thighLength,0,label+'-knee'),shoe=joint(knee,0,-m.shinLength,0,label+'-ankle');
    legs.push({hip,knee,shoe});
    const shoulder=joint(chest,side*.52,.79,0,label+'-shoulder'),elbow=joint(shoulder,0,-m.upperLength,0,label+'-elbow'),hand=joint(elbow,0,-m.lowerLength,0,label+'-hand');
    arms.push({shoulder,elbow,hand,side});
  }
  const rig={root,hips,chest,head,arms,legs,measurements:m};
  root.userData={shirt,skin,trousers,hair,hips,chest,head,limbs:[...legs.map(l=>l.hip),...arms.map(a=>a.shoulder)],
    arms:arms.map(a=>a.shoulder),knees:legs.map(l=>l.knee),elbows:arms.map(a=>a.elbow),hands:arms.map(a=>a.hand),
    ankles:legs.map(l=>l.shoe),shoes:legs.map(l=>l.shoe),rig:m};
  dressCharacterRig(rig,{part,shirt,skin,trousers,hair,style});
  if(seatHeight!==null){
    const y=m.hipHeight-m.ankleHeight,z=.56,d=Math.hypot(y,z),k=Math.acos(THREE.MathUtils.clamp((d*d-m.thighLength*m.thighLength-m.shinLength*m.shinLength)/(2*m.thighLength*m.shinLength),-1,1));
    const h=-Math.atan2(z,y)-Math.atan2(m.shinLength*Math.sin(k),m.thighLength+m.shinLength*Math.cos(k));
    for(const leg of legs){leg.hip.rotation.x=h;leg.knee.rotation.x=k;leg.shoe.rotation.x=-h-k;}
    for(const arm of arms){arm.shoulder.rotation.x=-.30;arm.elbow.rotation.x=-.90;}
    root.userData.seated=true;
  }
  return root;
}

export function addCharacterUniform(g,kind,{part}){
  const {head,chest,outerwear}=g.userData;if(outerwear)outerwear.visible=false;
  for(const mesh of head.userData.hairParts||[])mesh.visible=false;
  const hat=kind==='fire'?'#dab96b':'#334f69',a=(...args)=>box(part,chest,...args);
  facet(head,0,.57,-.015,.64,.22,.57,hat,'hair');
  box(part,head,0,.48,.21,kind==='fire'?.74:.66,.055,.35,hat);
  box(part,head,0,.585,.289,.11,.12,.022,'#dec987');
  if(kind==='police'){
    facet(chest,0,.42,.255,.60,.55,.10,'#3c5869','coat');
    a(-.18,.61,.318,.12,.15,.03,'#d9c284');a(.15,.58,.325,.09,.17,.055,'#2c414c');
    a(0,.07,0,.71,.095,.47,'#33464e');a(-.27,.035,.25,.13,.19,.08,'#354952');
    for(const side of [-1,1])a(side*.17,.30,.326,.20,.13,.04,'#4a697b');
  }else{
    a(0,.30,.251,.73,.115,.035,'#e8cc75');a(0,.38,-.31,.40,.61,.17,'#9eaca4');
    for(const shoulder of g.userData.arms)box(part,shoulder,0,-.29,.145,.246,.11,.035,'#e8cc75');
  }
}

export function addCharacterHat(g,{part,color='#b9a27a'}){
  const head=g.userData.head;
  for(const mesh of head.userData.hairParts||[])mesh.visible=false;
  facet(head,0,.555,-.02,.61,.23,.52,color,'hair');
  box(part,head,0,.458,.055,.73,.055,.65,color);
  box(part,head,0,.48,.29,.54,.06,.035,tone(color,-.10));
}

export function buildDriverCharacter({part,shirt='#879f91',skin='#d6a17d',style=0}){
  const g=new THREE.Group(),hips=joint(g,0,1.19,-.075,'driver-hips'),chest=joint(g,0,1.17,-.12,'driver-chest');
  const head=joint(g,0,1.68,-.08,'driver-head');head.scale.setScalar(.80);
  const rig={hips,chest};torso(rig,{part,shirt,skin,trousers:'#45545b',style,driver:true});
  buildCharacterHead(head,{part,skin,hair:'#53463d',style,compact:true});
  for(const side of [-1,1]){
    facet(g,side*.145,1.15,.15,.21,.17,.52,'#45545b','limb');
    facet(g,side*.145,.99,.40,.16,.38,.17,'#45545b','limb').rotation.x=.23;
    facet(g,side*.145,.80,.48,.19,.12,.29,shoe);
    facet(g,side*.28,1.56,-.08,.16,.39,.19,shirt,'limb');
  }
  const belt=box(part,g,0,1.47,.085,.055,.58,.035,'#354744');belt.rotation.z=-.44;
  const grips=[-1,1].map(side=>({side,arm:box(part,g,side*.25,1.59,.47,.13,.14,.9,shirt),hand:facet(g,side*.22,1.59,.89,.15,.14,.17,skin)}));
  g.userData={head,chest,hips,grips,characterModel:'motorista',characterStyle:style%8};return g;
}

// Fixed upper body for the bicycle; its pedalling links remain owned by the
// vehicle animation and the existing grip points stay exactly on the handlebar.
export function dressCyclist(rider,{part,skin,shirt,trousers}){
  const chest=joint(rider,0,1.71,.11,'cyclist-chest');chest.rotation.x=.28;
  torso({chest},{part,shirt,skin,trousers,style:0,player:true});
  const head=joint(rider,0,2.55,.56,'cyclist-head');head.scale.setScalar(.94);
  buildCharacterHead(head,{part,skin,hair:'#594942',style:0});
  facet(rider,0,1.75,0,.58,.22,.40,trousers,'coat');
  rider.userData.head=head;rider.userData.chest=chest;
}
