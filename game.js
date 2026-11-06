import * as THREE from './vendor/three.module.js';
import {createNpcLife} from './npc-life.js';

// Open Territory: geometria low-poly criada no próprio jogo, sem modelos externos.
const $ = (selector) => document.querySelector(selector);
const debugMode=new URLSearchParams(location.search).has('debug');
const scene = new THREE.Scene();
scene.background = new THREE.Color('#a9dce5');
scene.fog = new THREE.Fog('#c9e3df', 150, 370);
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.6));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.13;
$('#world').appendChild(renderer.domElement);
const camera = new THREE.PerspectiveCamera(58, innerWidth / innerHeight, 0.1, 1000);
const ambient=new THREE.HemisphereLight('#eaf9ff', '#738e69', 2.15);scene.add(ambient);
const sunlight = new THREE.DirectionalLight('#fff0d0', 2.7);
sunlight.position.set(-75, 130, 75);
sunlight.castShadow = true;
sunlight.shadow.mapSize.set(2048, 2048);
Object.assign(sunlight.shadow.camera, { left: -220, right: 220, top: 220, bottom: -220, near: 1, far: 460 });
sunlight.shadow.bias = -0.0004;
scene.add(sunlight);

const materials = new Map();
const cube = new THREE.BoxGeometry(1, 1, 1);
function material(color) {
  if (!materials.has(color)) materials.set(color, new THREE.MeshStandardMaterial({ color, roughness: 0.82 }));
  return materials.get(color);
}
const batches = new Map();
const dummy = new THREE.Object3D();
function block(x, y, z, w, h, d, color, rot = 0) {
  const key = color;
  if (!batches.has(key)) batches.set(key, []);
  dummy.position.set(x, y, z);
  dummy.rotation.set(0, rot, 0);
  dummy.scale.set(w, h, d);
  dummy.updateMatrix();
  batches.get(key).push(dummy.matrix.clone());
}
function flushBlocks() {
  for (const [color, transforms] of batches) {
    const mesh = new THREE.InstancedMesh(cube, material(color), transforms.length);
    transforms.forEach((matrix, i) => mesh.setMatrixAt(i, matrix));
    mesh.instanceMatrix.needsUpdate = true;
    mesh.castShadow = !['#60767b','#f8f4de','#f0dd9c','#fbf4df','#bcbdae'].includes(color);
    mesh.receiveShadow = true;
    mesh.frustumCulled = false;
    scene.add(mesh);
  }
}
function part(parent, x, y, z, w, h, d, color) {
  const mesh = new THREE.Mesh(cube, typeof color === 'string' ? material(color) : color);
  mesh.position.set(x, y, z);
  mesh.scale.set(w, h, d);
  mesh.castShadow = w*h*d > .12;
  mesh.receiveShadow = true;
  parent.add(mesh);
  return mesh;
}
function batchGroupCubes(group,excluded=[]){
  const batches=new Map(),protectedParts=new Set(excluded);
  for(const mesh of [...group.children]){
    if(!mesh.isMesh||mesh.geometry!==cube||protectedParts.has(mesh))continue;
    if(!batches.has(mesh.material))batches.set(mesh.material,[]);batches.get(mesh.material).push(mesh);
  }
  for(const [mat,parts] of batches){
    if(parts.length<2)continue;
    const batch=new THREE.InstancedMesh(cube,mat,parts.length);
    parts.forEach((mesh,i)=>{mesh.updateMatrix();batch.setMatrixAt(i,mesh.matrix);group.remove(mesh);});
    batch.instanceMatrix.needsUpdate=true;batch.castShadow=parts.some(mesh=>mesh.castShadow);batch.receiveShadow=true;batch.frustumCulled=false;group.add(batch);
  }
}
function sign(text, x, y, z, w, options = {}) {
  const c = document.createElement('canvas');
  c.width = 1024; c.height = 256;
  const ctx = c.getContext('2d');
  ctx.fillStyle = options.background || '#315c59'; ctx.fillRect(0, 0, c.width, c.height);
  if (options.border !== false) { ctx.strokeStyle = options.stroke || '#e7d7ac'; ctx.lineWidth = 12; ctx.strokeRect(9, 9, c.width - 18, c.height - 18); }
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = options.ink || '#fff5d9';
  ctx.font = `bold ${text.length > 15 ? 96 : 120}px system-ui, sans-serif`;
  ctx.fillText(text, 512, 134, 940);
  const texture = new THREE.CanvasTexture(c);
  texture.colorSpace = THREE.SRGBColorSpace;
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, w / 4), new THREE.MeshBasicMaterial({ map: texture, side: THREE.DoubleSide }));
  m.position.set(x, y, z); m.rotation.y = options.rotation || 0;
  scene.add(m); return m;
}
let seed = 45274;
function random() { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; }
function pick(values) { return values[Math.floor(random() * values.length)]; }
const solids = [], places = [], people = [], cars = [], destructibles = [], benches = [], cameraObstacles = [], servicePoints = [], waterDrops = [];
function solid(x, z, w, d) { const collider={x,z,w,d};solids.push(collider);return collider; }

// Relevo baixo, calculado pela mesma função usada pelos corpos sobre o terreno.
const hills=[[-205,-30,5,8,.6],[205,-30,5,8,.6],[180,105,24,20,1.5],[-180,105,23,20,1.35],[0,211,18,11,1.1],[-185,211,16,11,1.05],[185,211,16,11,1.05]];
function rawTerrainHeight(x,z){
  let height=0;
  for(const [hx,hz,rx,rz,peak] of hills){
    const distance=((x-hx)/rx)**2+((z-hz)/rz)**2;
    if(distance<1)height=Math.max(height,peak*(1-distance)**2);
  }
  return height;
}
const terrainGrid=new Float32Array(85*71);
for(let row=0;row<71;row++)for(let col=0;col<85;col++)terrainGrid[row*85+col]=rawTerrainHeight(-210+col*5,-130+row*5);
function terrainHeight(x,z){
  if(x < -210||x > 210||z < -130||z > 220)return 0;
  const gx=(x+210)/5,gz=(z+130)/5,col=Math.min(83,Math.floor(gx)),row=Math.min(69,Math.floor(gz));
  const tx=gx-col,tz=gz-row,h00=terrainGrid[row*85+col],h10=terrainGrid[row*85+col+1];
  const h01=terrainGrid[(row+1)*85+col],h11=terrainGrid[(row+1)*85+col+1];
  return tx+tz<=1?h00+tx*(h10-h00)+tz*(h01-h00):h11+(1-tx)*(h01-h11)+(1-tz)*(h10-h11);
}
function makeTerrain(){
  const geometry=new THREE.PlaneGeometry(420,350,84,70);geometry.rotateX(-Math.PI/2);
  const vertices=geometry.attributes.position;
  for(let i=0;i<vertices.count;i++){
    const x=vertices.getX(i),z=vertices.getZ(i)+45;vertices.setY(i,terrainHeight(x,z)-.05);
  }
  geometry.computeVertexNormals();
  const mesh=new THREE.Mesh(geometry,material('#a9c590'));mesh.position.z=45;mesh.receiveShadow=true;scene.add(mesh);
}

// Céu, sol cúbico, nuvens e mar.
const skySun = new THREE.Mesh(new THREE.BoxGeometry(30, 30, 1), new THREE.MeshBasicMaterial({ color: '#ffedb1', fog: false }));
skySun.position.set(-80, 45, -300); scene.add(skySun);
const sunHalo = new THREE.Mesh(new THREE.PlaneGeometry(42, 42), new THREE.MeshBasicMaterial({ color: '#ffdda1', transparent: true, opacity: .17, fog: false, depthWrite: false, side: THREE.DoubleSide }));
sunHalo.position.set(-80, 45, -302); scene.add(sunHalo);
const moon=new THREE.Mesh(new THREE.BoxGeometry(20,20,1),new THREE.MeshBasicMaterial({color:'#e7e9db',fog:false}));moon.position.set(-80,45,-300);moon.visible=false;scene.add(moon);
for (let i = 0; i < 16; i++) {
  const x = -300 + random() * 600, y = 72 + random() * 50, z = -180 - random() * 190;
  block(x, y, z, 20 + random() * 12, 4.5, 6, '#f7f3e4');
  block(x - 3, y + 3, z, 12, 4, 7, '#f7f3e4');
  block(x + 7, y + 2, z, 7, 3, 6, '#f7f3e4');
}
makeTerrain();
block(0, -.34, -145, 430, .3, 42, '#eed8a4');
block(0, -.55, -300, 1000, .5, 270, '#6abac2');
for (let i = 0; i < 80; i++) {
  const x = -280 + random() * 560, z = -166 - random() * 150;
  block(x, -.27, z, 4 + random() * 12, .04, .35, '#a8dbd5');
}

// Ruas, calçadas com juntas, meios-fios e travessias.
const roads = [-72, 0, 72];
function curvedRoadZ(x){return 155+12*Math.sin(x/39);}
// Traçados irregulares ligados à malha existente, com duas faixas de mão única
// nos bairros e acessos transversais de mão dupla.
function streetCurve(coords){return new THREE.CatmullRomCurve3(coords.map(([x,z])=>new THREE.Vector3(x,0,z)),false,'centripetal');}
const districtRoads=[
  {id:'oeste',oneWay:true,curve:streetCurve([[-130,-72],[-157,-72],[-186,-55],[-188,-23],[-184,7],[-178,29],[-163,52],[-130,72]])},
  {id:'leste',oneWay:true,curve:streetCurve([[130,72],[171,52],[194,20],[182,-6],[188,-40],[166,-65],[130,-72]])},
  {id:'travessa-oeste',oneWay:false,curve:streetCurve([[-130,0],[-155,6],[-184,7]])},
  {id:'travessa-leste',oneWay:false,curve:streetCurve([[130,0],[155,6],[181,14],[194,20]])}
];
for(const road of districtRoads)road.points=road.curve.getSpacedPoints(Math.ceil(road.curve.getLength()/4));
const pavedAreas=[
  {x:-36,z:-88.25,w:23,d:10.5,top:.23,kind:'forecourt'},
  {x:36,z:-88.25,w:23,d:10.5,top:.23,kind:'forecourt'},
  {x:-161,z:-103,w:34,d:22,top:.07,kind:'parking'},
  {x:-140,z:-103,w:15,d:5,top:.23,kind:'driveway'},
  {x:144,z:-103,w:12,d:3,top:.23,kind:'path'}
];
const vacantLots=[{x:164,z:-103,w:30,d:20}];
const busStops=[
  {id:'acacias',name:'Acácias',x:-61.7,z:-36,roadX:-68.7,roadZ:-36,rotation:-Math.PI/2},
  {id:'praca',name:'Praça',x:-61.7,z:36,roadX:-68.7,roadZ:36,rotation:-Math.PI/2},
  {id:'hospital',name:'Hospital',x:61.7,z:36,roadX:68.7,roadZ:36,rotation:Math.PI/2},
  {id:'livraria',name:'Livraria',x:61.7,z:-36,roadX:68.7,roadZ:-36,rotation:Math.PI/2}
];
// Passeios ligam as portas dos novos imóveis à calçada da avenida lateral.
for(const side of [-1,1])for(const front of [-38,-5,32.5])pavedAreas.push({x:side*147,z:front+1.8,w:18,d:3,top:.23,kind:'path'});
for(const [x,z] of [[-104,-104],[-104,-36],[-104,36],[-104,104],[-36,-36],[-36,104],[36,-36],[36,36],[36,104],[104,-104],[104,36]]){
  const front=z+10.5,end=z===104?curvedRoadZ(x)-11:z+25;
  pavedAreas.push({x,z:(front+end)/2,w:3.4,d:end-front,top:.23,kind:'path'});
}
function districtSurface(x,z,margin=0){
  for(const area of pavedAreas)if(Math.abs(x-area.x)<area.w/2+margin&&Math.abs(z-area.z)<area.d/2+margin)return area.top;
  if(Math.abs(x)<136-margin||z < -86-margin||z > 86+margin)return null;
  let nearest=Infinity;
  for(const road of districtRoads){
    if((road.id.includes('oeste')&&x>0)||(road.id.includes('leste')&&x<0))continue;
    for(let i=1;i<road.points.length;i++){
      const a=road.points[i-1],b=road.points[i];nearest=Math.min(nearest,distanceToSegment(x,z,a.x,a.z,b.x,b.z));
    }
  }
  if(nearest<5.5+margin)return .06;
  if(nearest<7.9+margin)return .23;
  return null;
}
function makeTrack(points){
  const clean=[];for(const p of points)if(!clean.length||Math.hypot(p.x-clean[clean.length-1].x,p.z-clean[clean.length-1].z)>.01)clean.push({x:p.x,z:p.z});
  if(Math.hypot(clean[0].x-clean[clean.length-1].x,clean[0].z-clean[clean.length-1].z)>.01)clean.push({...clean[0]});
  const cumulative=[0];for(let i=1;i<clean.length;i++)cumulative.push(cumulative[i-1]+Math.hypot(clean[i].x-clean[i-1].x,clean[i].z-clean[i-1].z));
  return {points:clean,cumulative,length:cumulative[cumulative.length-1],stops:[]};
}
function trackSample(track,distance){
  const d=(distance%track.length+track.length)%track.length;let i=1;
  while(i<track.cumulative.length-1&&track.cumulative[i]<d)i++;
  const a=track.points[i-1],b=track.points[i],length=track.cumulative[i]-track.cumulative[i-1]||1,t=(d-track.cumulative[i-1])/length;
  const dx=(b.x-a.x)/length,dz=(b.z-a.z)/length;
  return {x:a.x+(b.x-a.x)*t,z:a.z+(b.z-a.z)*t,dx,dz};
}
function trackNearest(track,x,z){
  let best=Infinity,result=0;
  for(let i=1;i<track.points.length;i++){
    const a=track.points[i-1],b=track.points[i],dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
    const t=den?THREE.MathUtils.clamp(((x-a.x)*dx+(z-a.z)*dz)/den,0,1):0;
    const d=(x-a.x-t*dx)**2+(z-a.z-t*dz)**2;
    if(d<best){best=d;result=track.cumulative[i-1]+Math.sqrt(den)*t;}
  }
  return result;
}
// Altura física dos vários pisos, compartilhada por pessoas, animais e veículos.
function groundHeight(x,z){
  for(const p of places){if(Math.abs(x-p.x)<p.w/2-.1&&Math.abs(z-p.z)<p.d/2-.1)return p.kind==='hospital'?.205:.18;}
  if(x>89&&x<119&&z>-44.9&&z<-10.5)return .255;
  const extension=districtSurface(x,z);
  if(extension!==null&&!(Math.abs(Math.abs(x)-130)<7.2&&z>-131&&z<curvedRoadZ(x)))return extension;
  if(vacantLots.some(l=>Math.abs(x-l.x)<l.w/2&&Math.abs(z-l.z)<l.d/2))return .08;
  if(Math.abs(x)<=3&&z<-147&&z>-179)return .33;
  if(z>=-149&&z<=-143)return .12;
  const ringDistance=Math.min(Math.abs(x-130),Math.abs(x+130)),coastDistance=Math.abs(z+131);
  if(Math.abs(x)<138&&coastDistance<11){
    if(coastDistance<7.2||ringDistance<7.2||roads.some(road=>Math.abs(x-road)<7.25))return .06;
    return .23;
  }
  if(z>=-131&&z<=curvedRoadZ(x<0?-130:130)&&ringDistance<11){
    const incoming=roads.some(road=>Math.abs(z-road)<7.2)&&((x<0&&x>-130)||(x>=0&&x<130));
    return ringDistance<7.2||incoming?.06:.23;
  }
  if(z<-130)return -.19;
  const xd=Math.min(...roads.map(r=>Math.abs(x-r))),zd=Math.min(...roads.map(r=>Math.abs(z-r)));
  if(z>122){
    for(const side of [-1,1]){
      const cx=side*200,cz=curvedRoadZ(cx),distance=Math.hypot(x-cx,z-cz);
      if(distance<9.3)return .06;
      if(distance<12){
        const slope=(12/39)*Math.cos(cx/39),gap=Math.atan2(side*slope,-side),angle=Math.atan2(-(z-cz),x-cx);
        return Math.abs(Math.atan2(Math.sin(angle-gap),Math.cos(angle-gap)))<.9?.06:.23;
      }
    }
    for(const road of roads)if(Math.abs(x-road)<11&&z<curvedRoadZ(road)+11){
      if(Math.abs(x-road)<7.25&&Math.abs(z-curvedRoadZ(road))<11)return .06;
      if(z<curvedRoadZ(road)-7)return Math.abs(x-road)<7?.06:.23;
    }
    const curveDistance=Math.abs(z-curvedRoadZ(x));
    if(curveDistance<7)return .06;if(curveDistance<11)return .23;
  }
  const coreZ=z>=-123&&z<=123,coreX=Math.abs(x)<=130;
  if((coreZ&&xd<7.25&&zd<11.25)||(coreX&&zd<7.25&&xd<11.25))return .06;
  const corner=coreX&&coreZ&&xd>=7&&xd<=11&&zd>=7&&zd<=11;
  if(corner||(coreZ&&xd>=7&&xd<=11&&zd>=11.25)||(coreX&&zd>=7&&zd<=11&&xd>=11.25))return .23;
  if((coreZ&&xd<7)||(coreX&&zd<7))return .06;
  if(Math.abs(x+36)<26&&Math.abs(z-36)<26)return Math.abs(x+36)<3.5||Math.abs(z-36)<3.5?.24:.08;
  if(Math.abs(x-104)<14.5&&Math.abs(z-104)<14.5)return Math.abs(x-104)<2||Math.abs(z-104)<2?.24:.09;
  return terrainHeight(x,z);
}
for (const a of roads) {
  block(a, -.02, 0, 14, .12, 246, '#60767b');
  block(0, -.01, a, 246, .12, 14, '#60767b');
  for (const side of [-1, 1]) {
    // As calçadas terminam antes da via perpendicular, sem atravessar a rua.
    for(const [start,end] of [[-123,-83.3],[-60.7,-11.3],[11.3,60.7],[83.3,123]]){
      const center=(start+end)/2,length=end-start;
      block(a+side*9,.12,center,4,.22,length,'#d9d2b9');
      block(center,.12,a+side*9,length,.22,4,'#d9d2b9');
      block(a+side*7.08,.23,center,.18,.08,length,'#eee6d1');
      block(center,.23,a+side*7.08,length,.08,.18,'#eee6d1');
    }
    for (let p = -120; p <= 120; p += 4) {
      if(roads.some(r=>Math.abs(p-r)<11.3))continue;
      block(a + side * 9, .235, p, 3.9, .012, .045, '#bcbdae');
      block(p, .235, a + side * 9, .045, .012, 3.9, '#bcbdae');
    }
  }
  for (let p = -116; p < 120; p += 8) {
    if (roads.some(r => Math.abs(p-r) < 12)) continue;
    block(a, .055, p, .18, .025, 3.3, '#f0dd9c');
    block(p, .06, a, 3.3, .025, .18, '#f0dd9c');
  }
}
for (const x of roads) for (const z of roads) {
  for(const sx of [-1,1])for(const sz of [-1,1])block(x+sx*9,.12,z+sz*9,4,.22,4,'#d9d2b9');
  block(x, .045, z, 14.1, .025, 14.1, '#60767b');
  for (const side of [-1,1]) for (let i=-3; i<=3; i++) {
    block(x + i*1.7, .072, z + side*8.8, .85, .025, 2.6, '#f8f4de');
    block(x + side*8.8, .072, z + i*1.7, 2.6, .025, .85, '#f8f4de');
  }
}

// Avenida das Colinas: um traçado curvo conecta o centro ao bairro-jardim.
for(let x=-205;x<205;x+=5){
  const nx=x+5,z1=curvedRoadZ(x),z2=curvedRoadZ(nx),dx=nx-x,dz=z2-z1;
  const length=Math.hypot(dx,dz),angle=-Math.atan2(dz,dx),cx=(x+nx)/2,cz=(z1+z2)/2,nxv=-dz/length,nzv=dx/length;
  block(cx,-.01,cz,length+.2,.12,14,'#60767b',angle);
  // Deixa livre a passagem dos três acessos perpendiculares.
  if(Math.abs(cx)<188&&![...roads,-130,130].some(road=>Math.abs(cx-road)<9))for(const side of [-1,1]){
    block(cx+nxv*side*9,.12,cz+nzv*side*9,length+.2,.22,4,'#d9d2b9',angle);
    block(cx+nxv*side*7.08,.23,cz+nzv*side*7.08,length+.2,.08,.18,'#eee6d1',angle);
  }
  if(Math.abs(cx)<188&&![...roads,-130,130].some(road=>Math.abs(cx-road)<12)&&Math.round((x+205)/5)%2===0)block(cx,.065,cz,2.7,.025,.18,'#f0dd9c',angle);
}
for(const road of roads){
  const end=curvedRoadZ(road),roadEnd=end+11.3,length=roadEnd-123;
  block(road,-.02,(123+roadEnd)/2,14,.12,length,'#60767b');
  const sidewalkEnd=end-11.3,sidewalkLength=Math.max(0,sidewalkEnd-123);
  if(sidewalkLength>0)for(const side of [-1,1]){
    block(road+side*9,.12,(123+sidewalkEnd)/2,4,.22,sidewalkLength,'#d9d2b9');
    block(road+side*7.08,.23,(123+sidewalkEnd)/2,.18,.08,sidewalkLength,'#eee6d1');
  }
  for(let z=126;z<end-12;z+=8)block(road,.055,z,.18,.025,3.3,'#f0dd9c');
  block(road,.045,end,14.1,.025,14.1,'#60767b');
  for(const side of [-1,1])for(let i=-3;i<=3;i++){
    block(road+i*1.7,.072,end+side*8.8,.85,.025,2.6,'#f8f4de');
    block(road+side*8.8,.072,end+i*1.7,2.6,.025,.85,'#f8f4de');
  }
}

// Avenidas laterais e via da orla conectam as ruas centrais numa malha contínua.
const RING_X=130, COAST_Z=-131;
for(const side of [-1,1]){
  const road=side*RING_X,end=curvedRoadZ(road),length=end-COAST_Z;
  block(road,-.02,(COAST_Z+end)/2,14,.12,length,'#60767b');
  // Liga cada rua do centro à avenida lateral, formando uma junção em T.
  for(const cross of roads){
    for(const sx of [-1,1])for(const sz of [-1,1])block(road+sx*9,.12,cross+sz*9,4,.22,4,'#d9d2b9');
    block(side*126.5,-.01,cross,7,.12,14,'#60767b');
    block(road,.045,cross,14.1,.025,14.1,'#60767b');
    for(let i=-3;i<=3;i++){
      block(road+i*1.7,.072,cross-8.8,.85,.025,2.6,'#f8f4de');
      block(road+i*1.7,.072,cross+8.8,.85,.025,2.6,'#f8f4de');
      block(road-side*8.8,.072,cross+i*1.7,2.6,.025,.85,'#f8f4de');
    }
  }
  const breaks=[COAST_Z,...roads,end];
  for(let i=0;i<breaks.length-1;i++){
    const begin=breaks[i]+11.3,finish=breaks[i+1]-11.3;
    if(finish<=begin)continue;
    for(const edge of [-1,1]){
      block(road+edge*9,.12,(begin+finish)/2,4,.22,finish-begin,'#d9d2b9');
      block(road+edge*7.08,.23,(begin+finish)/2,.18,.08,finish-begin,'#eee6d1');
    }
  }
  for(let z=COAST_Z+14;z<end-12;z+=8){if(roads.some(r=>Math.abs(z-r)<12))continue;block(road,.06,z,.18,.025,3.3,'#f0dd9c');}
  // Retorno arredondado onde a avenida das Colinas encontra a floresta.
  const x=side*200,z=curvedRoadZ(x);
  const slope=(12/39)*Math.cos(x/39),gap=Math.atan2(side*slope,-side);
  const curb=new THREE.Mesh(new THREE.RingGeometry(9.3,12,24,1,gap+.9,Math.PI*2-1.8),material('#d9d2b9'));
  curb.rotation.x=-Math.PI/2;curb.position.set(x,.23,z);curb.receiveShadow=true;scene.add(curb);
  const roadEnd=new THREE.Mesh(new THREE.CylinderGeometry(9.3,9.3,.04,16),material('#60767b'));roadEnd.position.set(x,.04,z);roadEnd.receiveShadow=true;scene.add(roadEnd);
}
block(0,-.01,COAST_Z,260,.12,14,'#60767b');
for(const side of [-1,1])for(let i=0;i<4;i++){
  const endpoints=[-130,...roads,130],start=endpoints[i]+11.3,end=endpoints[i+1]-11.3;
  if(end>start){block((start+end)/2,.12,COAST_Z+side*9,end-start,.22,4,'#d9d2b9');block((start+end)/2,.23,COAST_Z+side*7.08,end-start,.08,.18,'#eee6d1');}
}
for(const road of roads){
  for(const sx of [-1,1])for(const sz of [-1,1])block(road+sx*9,.12,COAST_Z+sz*9,4,.22,4,'#d9d2b9');
  block(road,-.02,-127,14,.12,8,'#60767b');
  block(road,.045,COAST_Z,14.1,.025,14.1,'#60767b');
  for(let i=-3;i<=3;i++){
    block(road+i*1.7,.072,COAST_Z+8.8,.85,.025,2.6,'#f8f4de');
    for(const side of [-1,1])block(road+side*8.8,.072,COAST_Z+i*1.7,2.6,.025,.85,'#f8f4de');
  }
}
for(let x=-115;x<=115;x+=8){if(roads.some(r=>Math.abs(x-r)<12))continue;block(x,.06,COAST_Z,3.3,.025,.18,'#f0dd9c');}
for(const side of [-1,1]){
  const road=side*RING_X,end=curvedRoadZ(road);
  for(const z of [COAST_Z,end]){
    block(road,.045,z,14.1,.025,14.1,'#60767b');
    for(let i=-3;i<=3;i++){
      block(road+i*1.7,.072,z+(z===COAST_Z?8.8:-8.8),.85,.025,2.6,'#f8f4de');
      block(road-side*8.8,.072,z+i*1.7,2.6,.025,.85,'#f8f4de');
    }
  }
}

// Máscara única para reservar todas as vias antes de posicionar vegetação.
function isPaved(x,z,margin=0){
  if(districtSurface(x,z,margin)!==null)return true;
  const width=11+margin;
  if(roads.some(r=>(Math.abs(x-r)<width&&z>=-131-width&&z<=123+width)||(Math.abs(z-r)<width&&Math.abs(x)<=130+margin)))return true;
  if(roads.some(r=>Math.abs(x-r)<width&&z>=123-margin&&z<=curvedRoadZ(r)+11+margin))return true;
  if(Math.abs(x)<=205+margin&&Math.abs(z-curvedRoadZ(x))<width)return true;
  if(Math.abs(z+131)<width&&Math.abs(x)<130+margin)return true;
  if(Math.abs(Math.abs(x)-130)<width&&z>=-131-margin&&z<=curvedRoadZ(x<0?-130:130)+margin)return true;
  if([-1,1].some(side=>Math.hypot(x-side*200,z-curvedRoadZ(side*200))<12+margin))return true;
  if(x>89-margin&&x<119+margin&&z>-62-margin&&z<-10+margin)return true;
  return false;
}

// O mesmo eixo que desenha as vias também fornece o piso, a reserva de
// vegetação e, abaixo, as rotas dos veículos dos novos bairros.
function roadRibbon(road,from,to,y,color,sidewalk=false){
  const pts=road.points,vertices=[],indices=[];
  for(let i=0;i<pts.length;i++){
    const a=pts[Math.max(0,i-1)],b=pts[Math.min(pts.length-1,i+1)],dx=b.x-a.x,dz=b.z-a.z,len=Math.hypot(dx,dz)||1;
    const nx=-dz/len,nz=dx/len,p=pts[i];
    vertices.push(p.x+nx*from,y,p.z+nz*from,p.x+nx*to,y,p.z+nz*to);
  }
  const junctions=[new THREE.Vector3(-184,0,7),new THREE.Vector3(194,0,20)];
  for(let i=0;i<pts.length-1;i++){
    if(sidewalk&&(i<2||i>=pts.length-3||junctions.some(p=>p.distanceTo(pts[i])<9)))continue;
    const a=i*2,b=a+1,c=a+2,d=a+3;indices.push(a,b,c,c,b,d);
  }
  const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));geometry.setIndex(indices);geometry.computeVertexNormals();
  const mesh=new THREE.Mesh(geometry,material(color));mesh.receiveShadow=true;scene.add(mesh);
}
function roadSign(text,x,z,heading){
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=heading;scene.add(g);
  part(g,0,1.55,0,.13,3.1,.13,'#677b73');
  const c=document.createElement('canvas');c.width=256;c.height=256;const ctx=c.getContext('2d');
  ctx.fillStyle=text==='PARE'?'#b94f49':'#416f77';ctx.strokeStyle='#f3ebd9';ctx.lineWidth=11;
  ctx.beginPath();for(let i=0;i<8;i++){const a=Math.PI/8+i*Math.PI/4,px=128+113*Math.cos(a),py=128+113*Math.sin(a);if(i)ctx.lineTo(px,py);else ctx.moveTo(px,py);}ctx.closePath();ctx.fill();ctx.stroke();
  ctx.fillStyle='#fff8e8';ctx.textAlign='center';ctx.textBaseline='middle';ctx.font=`bold ${text==='PARE'?66:38}px system-ui`;ctx.fillText(text,128,132,190);
  const tex=new THREE.CanvasTexture(c);tex.colorSpace=THREE.SRGBColorSpace;
  const mesh=new THREE.Mesh(new THREE.PlaneGeometry(2,2),new THREE.MeshBasicMaterial({map:tex,transparent:true,side:THREE.DoubleSide}));mesh.position.set(0,2.7,.08);g.add(mesh);
}
for(const road of districtRoads){
  roadRibbon(road,-5.5,5.5,.061,'#60767b');
  roadRibbon(road,-7.9,-5.5,.23,'#d9d2b9',true);roadRibbon(road,5.5,7.9,.23,'#d9d2b9',true);
  const length=road.curve.getLength();
  for(let d=6;d<length-6;d+=9){
    const t=d/length,p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t),angle=-Math.atan2(v.z,v.x);
    block(p.x,.073,p.z,3,.025,.13,road.oneWay?'#f8f4de':'#f0dd9c',angle);
  }
  if(road.oneWay){
    for(const t of [.22,.42,.65]){
      const p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t),angle=-Math.atan2(v.z,v.x);
      for(const lane of [-2.7,2.7]){
        const x=p.x-v.z*lane,z=p.z+v.x*lane;
        block(x,.078,z,2.4,.02,.18,'#f8f4de',angle);
        block(x+v.x*.8-v.z*.38,.079,z+v.z*.8+v.x*.38,1,.02,.15,'#f8f4de',angle+.75);
        block(x+v.x*.8+v.z*.38,.079,z+v.z*.8-v.x*.38,1,.02,.15,'#f8f4de',angle-.75);
      }
    }
    for(const t of [.3,.7]){
      const p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t),angle=-Math.atan2(v.z,v.x);
      for(let i=-3;i<=3;i++)block(p.x-v.z*i*1.45,.076,p.z+v.x*i*1.45,2.6,.025,.8,'#f8f4de',angle);
    }
    const junction=road.id==='oeste'?new THREE.Vector3(-184,0,7):new THREE.Vector3(194,0,20);
    let closest=0,best=Infinity;for(let i=1;i<100;i++){const t=i/100,d=road.curve.getPointAt(t).distanceTo(junction);if(d<best){best=d;closest=t;}}
    road.stopMarkers=[Math.max(.05,closest-8/length),1-9/length].map(t=>{
      const p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t);
      roadSign('PARE',p.x+v.z*7.2,p.z-v.x*7.2,Math.atan2(-v.x,-v.z));
      return {x:p.x,z:p.z,junctionX:p.x+v.x*8,junctionZ:p.z+v.z*8};
    });
    const p=road.curve.getPointAt(.13),v=road.curve.getTangentAt(.13);
    roadSign('ÚNICA',p.x+v.z*7.1,p.z-v.x*7.1,Math.atan2(-v.x,-v.z));
  }
}
for(const area of pavedAreas){
  block(area.x,area.top/2+.005,area.z,area.w,area.top,area.d,area.kind==='parking'?'#82918a':'#d9d2b9');
  if(area.kind==='path')for(let x=area.x-area.w/2+3;x<area.x+area.w/2;x+=3)block(x,area.top+.012,area.z,.04,.02,area.d,'#bcbdae');
}
// As entradas das ruas novas possuem travessias junto à avenida lateral.
for(const side of [-1,1])for(const z of [-72,0,72])for(let i=-3;i<=3;i++)block(side*138.8,.075,z+i*1.7,2.6,.025,.85,'#f8f4de');
for(const x of [-166.5,-162.8,-159.1,-155.4]){
  for(const s of [-1,1])block(x+s*1.65,.085,-103,.09,.025,7,'#f8f4de');
  block(x,.085,-106.5,3.3,.025,.09,'#f8f4de');
}
// Terreno vago cercado, com passagem pelo passeio e vegetação esparsa.
block(164,.035,-103,30,.09,20,'#b9b38f');
for(const z of [-113,-93]){block(164,.55,z,30,.12,.12,'#aa9879');for(let x=149;x<=179;x+=5)block(x,.4,z,.14,.8,.14,'#9b896e');}
for(const [start,end] of [[-113,-105],[-101,-93]]){block(149,.55,(start+end)/2,.12,.12,end-start,'#aa9879');for(let z=start;z<=end;z+=4)block(149,.4,z,.14,.8,.14,'#9b896e');}
block(179,.55,-103,.12,.12,20,'#aa9879');for(let z=-113;z<=-93;z+=5)block(179,.4,z,.14,.8,.14,'#9b896e');
solid(164,-113,30,.16);solid(164,-93,30,.16);solid(179,-103,.16,20);
solid(149,-109,.16,8);solid(149,-97,.16,8);
sign('TERRENO',150,2,-108,3,{background:'#897a5e',rotation:Math.PI/2});
for(const [x,z] of [[157,-110],[171,-108],[162,-97],[175,-97]]){block(x,.22,z,.09,.38,.09,'#7d9a69');block(x+.15,.16,z+.1,.08,.26,.08,'#89a674');}

// Sinalização em ciclos sincronizados. Os carros obedecem ao mesmo ciclo.
const signalMats = {};
for (const axis of ['ns','ew']) {
  signalMats[axis] = ['#eb5a4b','#f6c851','#74d58d'].map(color => new THREE.MeshStandardMaterial({ color, emissive: color, emissiveIntensity: .05, roughness: .5 }));
}
function trafficLight(x, z, axis, rotation) {
  const group = new THREE.Group();group.position.set(x,.23,z);group.rotation.y=rotation;scene.add(group);solid(x,z,.24,.24);
  cameraObstacles.push({x,z,w:1,d:1,bottom:.2,top:5.9});
  part(group, 0, 2.5, 0, .24, 5, .24, '#465756');
  part(group, 0, 4.75, 0, .86, 2.05, .52, '#354744');
  for (let i=0; i<3; i++) {
    part(group, 0, 5.37 - i*.61, .28, .42, .42, .09, signalMats[axis][i]);
    part(group, 0, 5.37 - i*.61, -.28, .42, .42, .09, signalMats[axis][i]);
  }
}
for (const x of roads) for (const z of roads) {
  trafficLight(x-9,z+9,'ns',0);trafficLight(x+9,z-9,'ns',0);
  trafficLight(x-9,z-9,'ew',Math.PI/2);trafficLight(x+9,z+9,'ew',Math.PI/2);
}
for(const side of [-1,1])for(const z of roads){
  trafficLight(side*130-side*9,z+9,'ns',0);trafficLight(side*130-side*9,z-9,'ew',Math.PI/2);
}
for(const x of roads){trafficLight(x-9,COAST_Z+9,'ns',0);trafficLight(x+9,COAST_Z+9,'ew',Math.PI/2);}
function signalPhase(time) {
  const phase = time % 24;
  return { ns: phase < 9 ? 2 : phase < 11 ? 1 : 0, ew: phase >= 12 && phase < 21 ? 2 : phase >= 21 && phase < 23 ? 1 : 0 };
}
function updateSignals(time) {
  const phase = signalPhase(time);
  for (const axis of ['ns','ew']) for (let i=0; i<3; i++) {
    signalMats[axis][i].emissiveIntensity = i === phase[axis] ? 1.15 : .03;
    signalMats[axis][i].color.set(i === phase[axis] ? ['#ff624d','#ffd358','#83ef9c'][i] : ['#703a36','#78643b','#365e46'][i]);
  }
}

// Mobiliário urbano todo em blocos, com variações de cor e tamanho.
function tree(x,z,scale=1, variant=0) {
  const base=groundHeight(x,z);
  cameraObstacles.push({x,z,w:3.8*scale,d:3.7*scale,bottom:base+3*scale,top:base+7.1*scale});
  block(x,base+2*scale,z,.7*scale,4*scale,.7*scale,'#896c4e');solid(x,z,.7*scale,.7*scale);
  const greens=variant?['#678e65','#7da972','#8fb67a']:['#4e8666','#609b71','#79ae7b'];
  block(x,base+4.5*scale,z,3.4*scale,2.6*scale,3.2*scale,greens[0]);
  block(x-.7*scale,base+5.8*scale,z,2.4*scale,2.2*scale,2.5*scale,greens[1]);
  block(x+.9*scale,base+5.2*scale,z+.25*scale,2*scale,2*scale,2.1*scale,greens[2]);
}
function palm(x,z,scale=1) {
  const base=groundHeight(x,z);
  cameraObstacles.push({x,z,w:5.5*scale,d:5.5*scale,bottom:base+5.25*scale,top:base+6.2*scale});
  block(x,base+2.9*scale,z,.65*scale,5.8*scale,.65*scale,'#ad8b60');solid(x,z,.65*scale,.65*scale);
  block(x,base+5.8*scale,z,1.8*scale,.4*scale,1.8*scale,'#4d8b68');
  for(const a of [0,Math.PI/2])block(x,base+5.7*scale,z,5.4*scale,.3*scale,1*scale,'#559a6d',a);
  for(const a of [Math.PI/4,-Math.PI/4])block(x,base+5.55*scale,z,4.1*scale,.24*scale,.8*scale,'#70ad79',a);
}

function bench(x,z,rot=0) {
  const g = new THREE.Group(); g.position.set(x,groundHeight(x,z),z); g.rotation.y=rot; scene.add(g);
  const collider=solid(x,z,Math.abs(Math.cos(rot))>0.7?2.9:1.1,Math.abs(Math.cos(rot))>0.7?1.1:2.9);
  const occupied=[[-52,24],[-20,24],[-52,48],[-20,48],[94,97],[114,111]].some(([bx,bz])=>Math.abs(x-bx)<.1&&Math.abs(z-bz)<.1);
  const benchState={group:g,collider,x,z,rotation:rot,occupied};benches.push(benchState);
  if(!occupied)destructibles.push({group:g,collider,type:'bench',active:true,bench:benchState});
  for(let i=0;i<3;i++) part(g,0,.75,-.35+i*.32,2.9,.14,.25,'#b47b4f');
  for(let i=0;i<3;i++) part(g,0,1.06+i*.22,-.42,2.9,.15,.14,'#bd8758');
  for(const s of [-1,1]) {part(g,s*1.1,.38,0,.16,.76,.75,'#465f5a');part(g,s*1.1,1,-.42,.13,.9,.13,'#465f5a');}
}
function bin(x,z) {
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  part(g,0,.64,0,.75,1.25,.75,'#568375');part(g,0,1.32,0,.87,.15,.87,'#355d54');
  part(g,0,.85,.39,.3,.12,.03,'#b2cbb0');
  destructibles.push({group:g,collider:solid(x,z,.75,.75),type:'bin',active:true});
}
const streetLamps=[];
function lamp(x,z, rot=0) {
  const base=groundHeight(x,z);solid(x,z,.22,.22);
  block(x,base+2.9,z,.2,5.8,.2,'#49615c');block(x,base+5.82,z,1,.14,.17,'#49615c',rot);
  block(x+Math.cos(rot)*.4,base+5.6,z-Math.sin(rot)*.4,.45,.4,.45,'#ffefba');
  streetLamps.push({x,y:base+5.5,z});
}
function planter(x,z) {
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  part(g,0,.45,0,2.1,.9,1.1,'#cd9c72');part(g,0,.95,0,1.85,.12,.86,'#688b61');
  for(let i=0;i<5;i++)part(g,-.7+i*.36,1.2,0,.2,.45,.2,i%2?'#e9a185':'#f1d78b');
  destructibles.push({group:g,collider:solid(x,z,2.1,1.1),type:'planter',active:true});
}
function busStop(x,z,rot=0) {
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=rot;scene.add(g);
  part(g,0,3,0,6,.28,2.6,'#578780');part(g,0,1.5,-1.1,6,3,.12,'#a3cbd0');
  for(const s of [-1,1])part(g,s*2.8,1.5,0,.15,3,2.3,'#53756e');
  part(g,0,.7,-.3,3.7,.2,.7,'#bb895e');part(g,0,.35,-.3,.18,.7,.65,'#53756e');
  if(Math.abs(Math.sin(rot))>.9)solid(x-Math.sin(rot)*1.1,z,.16,6);
}
function hydrant(x,z){solid(x,z,.5,.5);block(x,.55,z,.5,1.1,.5,'#d86e55');block(x,1.15,z,.75,.2,.75,'#e57d5e');block(x,.65,z,.9,.22,.25,'#d86e55');}
function bikeRack(x,z) {
  for(let i=0;i<3;i++){const bx=x+i*1.4;block(bx,.55,z,.09,1.1,.1,'#6a7b70');block(bx+.9,.55,z,.09,1.1,.1,'#6a7b70');block(bx+.45,1.06,z,1,.1,.1,'#6a7b70');}
}

const buildingColors = ['#e8b494','#e8d5ab','#a6c8bd','#dbaba0','#bdcad2','#e6cfb7'];
const roofColors = ['#d27f67','#6c9290','#b39c83','#cba27d'];
const glassMaterial = new THREE.MeshStandardMaterial({ color:'#9bd1d2', transparent:true, opacity:.34, roughness:.22, metalness:.08, depthWrite:false });
function doorAt(x,z,w,automatic=false) {
  const hinge=new THREE.Group(); hinge.position.set(x+w/2,0,z+.24); scene.add(hinge);
  part(hinge,-w/2,1.65,0,w,3.3,.16,'#477c75');
  part(hinge,-w/2,2.1,.095,w-.3,1.6,.04,glassMaterial);
  part(hinge,-w+.33,1.35,.15,.13,.18,.08,'#f0d18e');
  const door={x,z:z+.24,width:w,hinge,angle:0,angularVelocity:0,hold:0,automatic,offset:0,open:false};
  return door;
}
function facadeWindow(x,y,z,w=2.2) {
  block(x,y,z,w,2.2,.13,'#5a878a');
  block(x,y,z+.085,w+.24,.12,.08,'#f4e7cc');
  block(x,y,z+.085,.12,2.25,.08,'#f4e7cc');
  block(x,y-1.2,z+.14,w+.35,.15,.32,'#f7ead2');
}
function upperFloors(place, color, floors) {
  const {x,z,w,d}=place;
  const g = new THREE.Group();g.position.set(x,0,z);scene.add(g);place.upper=g;
  // As janelas repetidas de cada prédio compartilham desenhos, sem impedir
  // que os andares superiores desapareçam quando a câmera entra no térreo.
  const pieces=new Map();
  function upperPart(px,py,pz,pw,ph,pd,shade){
    if(!pieces.has(shade))pieces.set(shade,[]);
    dummy.position.set(px,py,pz);dummy.rotation.set(0,0,0);dummy.scale.set(pw,ph,pd);dummy.updateMatrix();
    pieces.get(shade).push({matrix:dummy.matrix.clone(),shadow:pw*ph*pd>.12});
  }
  const h=5.2+floors*3.8;
  upperPart(0,(5+h)/2,0,w,h-5,d,color);
  for(let level=0;level<floors;level++){
    const yy=7.15+level*3.8;
    for(let dx=-w/2+2.2;dx<w/2-1;dx+=3.5){
      upperPart(dx,yy,d/2+.09,2,2.15,.15,'#618e90');upperPart(dx,yy,d/2+.19,.1,2.15,.07,'#efe4cd');
      upperPart(dx,yy-1.14,d/2+.22,2.3,.13,.25,'#f4e8ce');
    }
    for(let dz=-d/2+2.2;dz<d/2-1;dz+=4){
      upperPart(w/2+.09,yy,dz,.14,2.15,2,'#638b8d');upperPart(-w/2-.09,yy,dz,.14,2.15,2,'#638b8d');
    }
    upperPart(0,5.3+level*3.8,d/2+.18,w+.3,.2,.3,'#f0dfc3');
  }
  upperPart(0,h+.1,0,w+.8,.45,d+.8,'#f1dfbf');
  upperPart(0,h+.5,0,w-.7,.4,d-.7,pick(roofColors));
  upperPart(-w*.24,h+1.1,-d*.15,2.3,1,1.5,'#d9d8c5');
  upperPart(-w*.24,h+1.68,-d*.15,1.9,.1,1.1,'#8a9c98');
  for(const [shade,instances] of pieces){
    const mesh=new THREE.InstancedMesh(cube,material(shade),instances.length);
    instances.forEach((item,i)=>mesh.setMatrixAt(i,item.matrix));mesh.instanceMatrix.needsUpdate=true;
    mesh.castShadow=instances.some(item=>item.shadow);mesh.receiveShadow=true;mesh.frustumCulled=false;g.add(mesh);
  }
}
function closedBuilding(x,z,w,d,color,floors) {
  const height=5+floors*3.8;
  block(x,.2,z,w+2,.35,d+2,'#d9d0b8');
  block(x,height/2+.25,z,w,height,d,color);solid(x,z,w,d).opaque=true;
  block(x,height+.4,z,w+1,.6,d+1,'#f2e5ca');block(x,height+.8,z,w-1,.25,d-1,pick(roofColors));
  for(let y=4.2;y<height-1;y+=3.8){
    for(let dx=-w/2+2.2;dx<w/2-1;dx+=3.5) facadeWindow(x+dx,y,z+d/2+.08,2);
    for(let dz=-d/2+2;dz<d/2-1;dz+=4){
      block(x+w/2+.08,y,z+dz,.14,2,1.9,'#5f898b');
      block(x-w/2-.08,y,z+dz,.14,2,1.9,'#5f898b');
    }
  }
  block(x,1.75,z+d/2+.1,2.8,3.5,.16,'#52756f');
  block(x,2.1,z+d/2+.21,2,1.8,.1,'#8eb4b0');
  block(x,3.65,z+d/2+.35,5,.18,1,'#e8dfc5');
  for(const dx of [-w/2+2.2,w/2-2.2]){
    block(x+dx,1.75,z+d/2+.15,2.8,2.8,.15,'#719b98');
    block(x+dx,1.75,z+d/2+.26,.12,2.8,.08,'#dcd9c4');
  }
  block(x-w*.28,height+1.3,z-d*.1,2,1,1.5,'#dddcc7');
}
function enterableBuilding(x,z,w,d,color,kind,name,floors=1) {
  const place={x,z,w,d,kind,name};places.push(place);
  const front=z+d/2, back=z-d/2, doorW=3.2, side=(w-doorW)/2;
  block(x,.09,z,w,.18,d,kind==='hospital'?'#e6e9dc':'#d8bc91');
  block(x-w/2,2.6,z,.4,5.2,d,color);block(x+w/2,2.6,z,.4,5.2,d,color);
  block(x,2.6,back,w,5.2,.4,color);
  // Fachadas voltadas para as ruas curvas também têm janelas e floreiras.
  if(['home','restaurant','residence'].includes(kind))for(const side of [-1,1]){
    block(x+side*w*.27,2.55,back-.24,3,2,.1,'#86ada8');
    block(x+side*w*.27,2.55,back-.31,.1,2,.08,'#eadfc9');
    block(x+side*w*.27,1.43,back-.34,3.3,.18,.35,'#e6d7bc');
    block(x+side*w*.27,1.58,back-.48,2,.18,.25,'#89a875');
  }
  // Frente com grandes vitrines: o ambiente pode ser visto da rua.
  for(const s of [-1,1]){
    const sx=x+s*(doorW/2+side/2);
    block(sx,.55,front,side,1.1,.4,color);block(sx,4.65,front,side,1.1,.4,color);
    block(x+s*(w/2-.25),2.6,front,.5,3,.4,color);
    block(x+s*(doorW/2+.25),2.6,front,.5,3,.4,color);
    const pane=new THREE.Mesh(new THREE.BoxGeometry(side-.8,2.9,.12),glassMaterial);pane.position.set(sx,2.6,front+.05);scene.add(pane);
    block(sx,2.6,front+.17,.1,3,.1,'#dfe5d3');
  }
  block(x,4.5,front,doorW,1.4,.4,color);
  for(const wall of [solid(x-w/2,z,.45,d),solid(x+w/2,z,.45,d),solid(x,back,w,.45),
    solid(x-(doorW/2+side/2),front,side,.45),solid(x+(doorW/2+side/2),front,side,.45)])wall.opaque=true;
  place.door=doorAt(x,front,doorW-.16,kind==='hospital'||kind==='station');
  upperFloors(place,color,floors);
  const awning=kind==='police'?'#54809b':kind==='fire'?'#c76350':kind==='hospital'?'#e6e5d6':kind==='market'?'#e7ac79':kind==='gallery'?'#668f82':kind==='book'?'#8b9eb8':kind==='flower'?'#a6be8e':'#df907a';
  block(x,4.15,front+.95,w+.4,.18,1.9,awning);
  cameraObstacles.push({x,z:front+.95,w:w+.4,d:1.9,bottom:4.02,top:4.3});
  for(let k=-w/2+.4;k<w/2;k+=1.6)block(x+k,4.06,front+1.6,.8,.14,.65,'#f8e6c2');
  sign(name,x,5.75,front+.45,Math.min(w-1,11.5),{background:kind==='hospital'?'#d46155':kind==='police'?'#315a78':kind==='fire'?'#ad5044':'#315f58'});
  // Garagem visual alinhada à vaga dos veículos; a entrada de pessoas segue livre.
  if(kind==='police'||kind==='fire'){
    block(x-6,1.75,front+.22,5.4,3.5,.14,kind==='police'?'#7797a4':'#c27b65');
    for(let y=.55;y<3.2;y+=.55)block(x-6,y,front+.32,5.2,.06,.06,'#e7dcc4');
    sign(kind==='police'?'VIATURA':'GARAGEM',x-6,3.6,front+.36,4,{background:kind==='police'?'#416d8b':'#a45145'});
  }
  // Plantas e pequenos detalhes junto à entrada.
  for(const side of [-1,1]) {block(x+side*3.5,.35,front+1.05,.7,.7,.7,'#bd8364');block(x+side*3.5,1.05,front+1.05,.85,.85,.85,'#72a477');}
  return place;
}

const sites=[
  {x:-104,z:-104,type:'flower',name:'FLOR DO MAR',color:'#d7b4ac'},
  {x:-104,z:-36,type:'bakery',name:'PADARIA PÃO & SOL',color:'#ead1a3'},
  {x:-104,z:36,type:'market',name:'MERCADO DA PRAÇA',color:'#e6aa87'},
  {x:-104,z:104,type:'house'},
  {x:-36,z:-104,type:'fire',name:'BOMBEIROS',color:'#e5d4c1'},
  {x:-36,z:-36,type:'gallery',name:'GALERIA SOL',color:'#c4d0bd'},
  {x:-36,z:36,type:'park'},
  {x:-36,z:104,type:'house'},
  {x:36,z:-104,type:'police',name:'DELEGACIA',color:'#c8d6d6'},
  {x:36,z:-36,type:'book',name:'LIVRARIA MARÉ',color:'#c6c6d0'},
  {x:36,z:36,type:'cafe',name:'CAFÉ AURORA',color:'#e9b08e'},
  {x:36,z:104,type:'house'},
  {x:104,z:-104,type:'house'},
  {x:104,z:-36,type:'station'},
  {x:104,z:36,type:'hospital',name:'HOSPITAL BAÍA',color:'#e8e9de'},
  {x:104,z:104,type:'park2'},
  {x:-155,z:-45,type:'home',name:'CASA DA LIA',color:'#e4b899',w:18,d:14,floors:0},
  {x:-155,z:-13,type:'restaurant',name:'COZINHA DA PRAÇA',color:'#e3b38e',w:20,d:16,floors:1},
  {x:-155,z:25,type:'residence',name:'VILA DAS ACÁCIAS',color:'#bccac0',w:20,d:15,floors:2},
  {x:155,z:-45,type:'home',name:'CASA DO TOMÁS',color:'#b5cbbd',w:18,d:14,floors:0},
  {x:155,z:-13,type:'restaurant',name:'CANTINA DO BAIRRO',color:'#ddc295',w:20,d:16,floors:1},
  {x:155,z:25,type:'residence',name:'EDIFÍCIO MARÉS',color:'#c2c8d2',w:20,d:15,floors:3},
];
for(const site of sites){
  const {x,z,type}=site;
  if(type==='house')closedBuilding(x,z,23,21,pick(buildingColors),2+Math.floor(random()*2));
  else if(!['park','park2','station'].includes(type)) enterableBuilding(x,z,site.w??23,site.d??21,site.color,type,site.name,site.floors??(type==='hospital'?2:1+Math.floor(random()*2)));
}
// Casas baixas, jardins e uma praça com lago ampliam a área explorável.
function gardenHouse(x,z,color){
  block(x,.08,z,21,.16,19,'#b0ca96');
  block(x,3.25,z,18,6.5,14,color);solid(x,z,18,14);
  block(x,6.65,z,19,.45,15,'#ede0c3');block(x,7.2,z,17.8,.8,13.8,pick(roofColors));
  block(x,7.8,z,15,.5,12,pick(roofColors));
  block(x,1.65,z-7.12,2.7,3.3,.18,'#567970');
  for(const dx of [-5.2,5.2]){
    block(x+dx,2.65,z-7.12,3.2,2.4,.16,'#7eaaa8');block(x+dx,2.65,z-7.25,.12,2.5,.08,'#ebdfc6');
    block(x+dx,1.35,z-7.3,3.5,.2,.3,'#f2e5cd');
    for(const f of [-1,0,1])block(x+dx+f*.7,1.6,z-7.5,.3,.35,.3,f?'#e9aa94':'#f3d898');
  }
  block(x-6,8.5,z+2,1,2,1,'#d4c1aa');
  for(const side of [-1,1]){
    block(x+side*10,.48,z-6,.14,.95,5,'#eee2c6');
    for(let dz=-8;dz<=-4;dz+=2)block(x+side*10,.5,z+dz,.25,1,.25,'#eee2c6');
  }
}
for(const x of [-180,-140,-100,-60,60,100,140,180]){
  const z=curvedRoadZ(x)+23;gardenHouse(x,z,pick(buildingColors));
  const treeX=x+18,treeZ=curvedRoadZ(treeX)+14;tree(treeX,treeZ,.85,1);
  if(x%80===20)bin(x+10,curvedRoadZ(x)+13);
}
const pond=new THREE.Mesh(new THREE.CylinderGeometry(1,1,.18,14),material('#7dc2c4'));
pond.position.set(0,.12,curvedRoadZ(0)+27);pond.scale.set(12,1,6);pond.receiveShadow=true;scene.add(pond);
solid(0,curvedRoadZ(0)+27,22,10);
for(const x of [-18,18])for(const z of [curvedRoadZ(0)+18,curvedRoadZ(0)+36]){tree(x,z,.95,1);bench(x,z+(z<curvedRoadZ(0)+27?-3:3),z<curvedRoadZ(0)+27?0:Math.PI);}
sign('BAIRRO JARDIM',0,3.5,curvedRoadZ(0)+13,8,{background:'#558273',rotation:Math.PI});

// A cruz do hospital aparece tanto da praça como das avenidas.
block(104,7,46.66,1.1,4,.25,'#dc695b');block(104,7,46.68,4,1.1,.28,'#dc695b');
block(115,1.9,47,1.8,3.8,.15,'#ca6d5d');block(115,2.1,47.15,.38,1.7,.1,'#fff1db');block(115,2.1,47.17,1.3,.38,.1,'#fff1db');

function pump(x,z) {
  block(x,.15,z,2.5,.3,3.5,'#d5d2bd');block(x,1.15,z,1.2,2.1,1.1,'#e8e8dc');
  block(x,2.05,z,1.3,.35,1.2,'#d77860');block(x,1.45,z+.57,.8,.55,.05,'#547a78');
  block(x+.74,1,z,.12,1.7,.12,'#4e5b55');block(x+.74,.4,z+.35,.12,.12,.75,'#4e5b55');
  block(x-.3,.75,z+.58,.3,.18,.04,'#b5bfb0');solid(x,z,1.4,1.4);
}
// Posto integrado ao bairro, com loja, bombas e cobertura.
block(104,.13,-27.7,30,.25,34.4,'#ded8c6');
const station=enterableBuilding(104,-50,22,10,'#ecdfc5','station','POSTO SOL',1);
for(const x of [98,110])for(const z of [-35,-26])pump(x,z);
for(const x of [93,115])for(const z of [-40,-21])block(x,3,z,.5,6,.5,'#d1d6c7');
for(const x of [98,110])for(const z of [-35,-26])block(x,5.9,z,2,.12,1.2,'#ffefba');
block(104,6.15,-30.5,25,.45,22,'#e8e8d6');block(104,6.55,-30.5,25,.45,22,'#d9765e');
block(104,6.86,-30.5,24,.17,21,'#f2dcab');
sign('POSTO SOL',104,6.65,-19.3,10,{background:'#c96452'});
block(91,2.5,-16,1,5,1,'#677b72');block(91,4.8,-16,4,2.7,.7,'#d9765e');
sign('SOL',91,5,-15.59,3.5,{background:'#cf6c57'});
block(91,3.3,-15.57,2.8,.36,.05,'#f7e7be');

// Praças e calçadão, acessíveis a pé.
function plaza() {
  const x=-36,z=36;
  block(x,.035,z,52,.08,52,'#a2c18b');
  block(x,.14,z,7,.18,52,'#e5d9bf');block(x,.15,z,52,.18,7,'#e5d9bf');
  for(let p=-24;p<=24;p+=3){block(x+p,.25,z,.045,.02,7,'#c3bea8');block(x,.25,z+p,7,.02,.045,'#c3bea8');}
  // Fonte em degraus quadrados e pequenos jatos facetados.
  block(x,.35,z,9.4,.7,9.4,'#e9ddc7');solid(x,z,9.4,9.4);block(x,.76,z,8.6,.25,8.6,'#a7d2ce');block(x,.88,z,7.8,.08,7.8,'#72bfca');
  block(x,1.7,z,1,1.7,1,'#e6d9be');block(x,2.65,z,2.2,.24,2.2,'#e6d9be');
  for(const dx of [-1,1])for(const dz of [-1,1])block(x+dx*2.3,1.05,z+dz*2.3,.28,.4,.28,'#c8e5df');
  for(const dx of [-16,16])for(const dz of [-16,16]){tree(x+dx,z+dz,1.1,1);bench(x+dx,z+dz+(dz>0?-4:4),dz>0?Math.PI:0);bin(x+dx+3,z+dz+2);}
  for(const side of [-1,1]){planter(x+side*6,z+14);planter(x+side*6,z-14);lamp(x+side*6,z+7);lamp(x+side*6,z-7);}
  block(-57,.18,53,5,.3,3,'#d8c5a4');block(-57,1.4,53,3.5,2.5,1.6,'#e7c896');block(-57,2.9,53,4.2,.4,2,'#d77d68');
  sign('SORVETES',-57,2.6,53.85,3.5,{background:'#ca7865'});
}
function smallPlaza() {
  block(104,.04,104,29,.1,29,'#a6c58e');
  block(104,.14,104,4,.18,29,'#e3d6ba');block(104,.15,104,29,.18,4,'#e3d6ba');
  for(const x of [94,114])for(const z of [94,114]){tree(x,z,.9);bench(x,z+(z<104?3:-3),z<104?0:Math.PI);}
  block(104,.25,97,7,.25,4,'#e9d29d');block(104,.45,97,6,.22,3,'#e7bd77');
  for(const x of [101,107]){block(x,1.6,97,.18,3.2,.18,'#d87e65');block(x,3.15,97,2.7,.18,.18,'#d87e65');}
  block(104,1,112,3,2,1.3,'#78a89a');block(104,2.2,112,3.5,.4,1.8,'#e5b178');
  sign('PRAÇA',104,2.1,112.7,2.7,{background:'#57877b'});
  planter(108,99);bin(99,106);
}
plaza();smallPlaza();
const fountainDrops=[];
for(let i=0;i<10;i++){
  const drop=part(scene,-36,2.8,36,.18,.18,.18,'#b9e2dd');fountainDrops.push({mesh:drop,phase:i/10});
}

// Calçadas ganham bancos, lixeiras, árvores, postes, pontos de ônibus e serviços.
for(const road of roads)for(const side of [-1,1]){
  for(let v=-111;v<=111;v+=18){
    if(roads.some(r=>Math.abs(v-r)<14))continue;
    if(((road===-72&&side===1)||(road===72&&side===-1))&&busStops.some(stop=>Math.abs(stop.z-v)<8))continue;
    tree(road+side*12,v,.7+random()*.35,random()>.5?1:0);
    lamp(road+side*10.7,v+4,side<0?Math.PI:0);
    if(Math.round(v)%3===0)bin(road+side*10.6,v-4);
  }
}
for(const x of [-105,-35,35,105])for(const z of [-72,0,72]){
  if(Math.abs(x-104)<2&&z===0)continue;
  bench(x+7,z+13.4,Math.PI);bin(x+10,z+13.4);
}
for(const stop of busStops){
  busStop(stop.x,stop.z,stop.rotation);
  sign(`PONTO · ${stop.name.toUpperCase()}`,stop.x,3.25,stop.z,5.4,{background:'#4c8179',rotation:stop.rotation});
}
for(const road of districtRoads.filter(r=>r.oneWay))for(const t of [.18,.34,.55,.73,.85]){
  const p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t),nx=-v.z,nz=v.x,side=Math.sign(p.x*nx)||1;
  const lx=p.x+nx*side*7.4,lz=p.z+nz*side*7.4,tx=p.x+nx*side*11.5,tz=p.z+nz*side*11.5;
  if(Math.abs(lx)<208&&!collides(lx,lz,.4))lamp(lx,lz);
  if(Math.abs(tx)<207&&!collides(tx,tz,1)&&!isPaved(tx,tz,1)&&!sites.some(site=>Math.abs(tx-site.x)<(site.w??23)/2+3&&Math.abs(tz-site.z)<(site.d??21)/2+3))tree(tx,tz,.75,1);
}
for(const [x,z] of [[-58.5,17],[14,50],[86,-99],[-58.5,-90]])hydrant(x,z);
bikeRack(-54,58);bikeRack(17,51);bikeRack(91,51);
// Cabines telefônicas, caixas eletrônicos e caixas de correio.
function phoneBooth(x,z){
  solid(x,z,1.7,1.6);
  block(x,1.6,z,1.7,3.2,1.6,'#d26c5d');block(x,1.8,z+.84,1.2,1.8,.1,'#91c2c1');
  block(x,1.8,z+.91,.09,1.8,.08,'#e5ded0');block(x,1.8,z+.91,1.2,.09,.08,'#e5ded0');
  block(x,3.3,z,2,.3,1.9,'#e6cfaf');sign('FONE',x,3.03,z+.88,1.7,{background:'#b9554c'});
}
function atm(x,z){
  solid(x,z,1.55,.9);
  block(x,1.4,z,1.55,2.8,.9,'#5c8f85');block(x,1.9,z+.47,1.1,.8,.06,'#bde0d8');
  block(x,1.15,z+.48,.7,.24,.06,'#e4e1cc');block(x,.87,z+.48,.8,.1,.06,'#395d59');
  sign('CAIXA',x,2.9,z+.5,1.5,{background:'#487b70'});
}
phoneBooth(-58.5,57);phoneBooth(86,15);phoneBooth(-86,-90);
atm(30,47.4);atm(-110,47.4);atm(111,47.4);
for(const [x,z] of [[-58,-37],[14,32],[86,53]]){block(x,.7,z,.75,1.4,.75,'#6995a1');block(x,1.45,z,.9,.16,.86,'#5b818c');block(x,1.1,z+.4,.4,.08,.04,'#e4e2c9');}
// Tampas de bueiro, grelhas e placas de rua.
for(let i=-110;i<115;i+=22){for(const road of roads){block(road-5,.07,i,.9,.03,.9,'#455e60');block(i,.08,road+5,.9,.03,.9,'#455e60');}}
for(const [x,z] of [[-62,62],[10,62],[82,-10],[-82,-62]]){
  block(x,2.1,z,.12,4.2,.12,'#526b65');block(x,4.25,z,3,.5,.15,'#528078');
  sign('BAÍA DO SOL',x,4.25,z+.1,2.8,{background:'#528078',border:false});
}

// Orla de areia com deque, quiosques, cadeiras e guarda-sóis.
block(0,.04,-146,246,.16,6,'#e1c49a');
for(let x=-120;x<121;x+=3)block(x,.13,-146,.1,.02,6,'#c5a77c');
for(let x=-114;x<=114;x+=14){palm(x,-151,.9+random()*.2);bench(x+4,-154,Math.PI);bin(x+7,-154);}
for(let x=-105;x<=110;x+=30){
  block(x,.05,-160,7,.1,5,'#dcbf94');block(x,1.7,-160,.15,3.4,.15,'#e8ddc6');
  block(x,3.25,-160,5,.22,5,x%2?'#e3a27e':'#85b5a9');block(x,3.4,-160,2.4,.15,2.4,'#f4dfbe');
  block(x+3,.5,-156,2.5,.2,1.2,'#faf0d6');block(x+3,.8,-156.6,2.5,1,.15,'#f6e4c3');
}
// Píer e amarras; os barcos ficam no mesmo mundo e balançam na água.
block(0,.15,-163,6,.35,32,'#bc986e');
for(let z=-178;z<=-148;z+=2)block(0,.35,z,6,.05,.08,'#9e7d5b');
for(const x of [-3,3])for(let z=-176;z<=-150;z+=6){block(x,.6,z,.3,1.2,.3,'#967c60');block(x,1.2,z,.4,.15,.4,'#efe1c4');}
const boats=[];
function boat(x,z,color,sail=false){
  const g=new THREE.Group();g.position.set(x,0,z);scene.add(g);
  part(g,0,.1,0,4,.8,9,color);part(g,0,.58,0,3.5,.18,7.9,'#efe3c8');
  part(g,0,.9,-.5,2.2,.65,2.5,'#d3c5aa');part(g,0,1.2,-.5,1.8,.12,2.1,'#faf0db');
  if(sail){
    part(g,0,3.4,0,.18,5.6,.18,'#8a755d');part(g,.9,3.4,0,1.8,4,.08,'#f8f1df');part(g,-.7,2.9,-.08,1.3,3,.08,'#eed9ba');
  }else{part(g,0,1.75,-.5,1.6,1.1,1.8,'#9bc5c4');part(g,0,2.35,-.5,1.9,.15,2.1,'#f5eedc');}
  boats.push({g,baseX:x,baseZ:z,phase:random()*8});return g;
}
boat(-11,-173,'#dc8f75',true);boat(12,-182,'#6caaa0');boat(48,-204,'#d7aa75',true);boat(-56,-215,'#88aeb4');boat(91,-230,'#e6b488',true);

// Os móveis estão fisicamente dentro dos prédios: não há outra cena nem teleporte.
function furniture(x,z,w,d,h,color){block(x,h/2,z,w,h,d,color);solid(x,z,w,d);}
function chair(x,z,rot=0){
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=rot;scene.add(g);
  part(g,0,.68,0,1.1,.18,1.1,'#b4855e');part(g,0,1.2,-.48,1.1,1,.16,'#b4855e');
  for(const sx of [-.43,.43])for(const sz of [-.38,.38])part(g,sx,.33,sz,.12,.66,.12,'#687a6a');
}
function table(x,z){block(x,1,z,2.3,.16,2.3,'#e8d7b3');block(x,.5,z,.22,1,.22,'#758574');solid(x,z,2.3,2.3);}
function wallArt(x,z,color){block(x,2.7,z,3,2.1,.14,'#a28060');block(x,2.7,z+.09,2.65,1.75,.09,color);block(x-.4,2.6,z+.16,1,1,.05,'#f0d6a2');}
function bookshelf(x,z,rot=0, color='#9e795a'){
  block(x,1.55,z,4,3.1,1,color,rot);
  for(let i=0;i<9;i++)block(x-1.6+i*.4,1.6,z+.54,.26,1.1,.15,['#b46f62','#d6b374','#91a994','#71949a'][i%4]);
  block(x,1.05,z+.58,3.9,.12,.15,'#e1c29a');
  solid(x,z,4,1.1);
}
function flowers(x,z){
  block(x,.4,z,.8,.8,.8,'#b9775d');
  for(let i=0;i<4;i++){const dx=(i%2-.5)*.35,dz=(Math.floor(i/2)-.5)*.3;block(x+dx,1.15,z+dz,.08,.8,.08,'#568764');block(x+dx,1.6,z+dz,.3,.28,.3,['#e19b8b','#f4d496','#eab3c4','#f2e4bc'][i]);}
}
function interior(place){
  const {x,z,w,d,kind}=place, back=z-d/2+.25;
  // Rodapés, quadros e iluminação no térreo.
  block(x, .28,back+.15,w-.5,.25,.15,'#b99b78');
  block(x,4.7,back+.3,3.5,.18,.35,'#ffe8ae');
  // Duas luminárias espalham a luz pelo salão, inclusive nas vitrines e balcões.
  for(const side of [-1,1]){
    block(x+side*w*.23,4.9,z,3.1,.16,1.05,'#e5ddca');
    block(x+side*w*.23,4.78,z,2.7,.12,.75,'#ffefba');
  }
  for(const side of [-1,1])block(x+side*(w/2-.3),.28,z,.15,.25,d-.5,'#bb9c7b');
  if(kind==='cafe'){
    furniture(x, z-5.8, 14, 2, 1.5, '#b77d5a');
    block(x,1.56,z-5.8,14.4,.12,2.25,'#ead6b4');
    for(let i=-2;i<=2;i++){block(x+i*2.4,1.85,z-5.5,.35,.55,.35,'#efe7d0');block(x+i*2.4,2.15,z-5.5,.28,.1,.28,'#8b6d56');}
    block(x-5,1.9,z-8.7,2.4,3.7,1,'#587a70');
    for(let i=0;i<4;i++)block(x-5.8+i*.5,2.8,z-8.1,.3,.5,.15,'#d6a16f');
    sign('CAFÉ • BOLOS',x+2,3.6,back+.22,6,{background:'#475f55'});
    for(const dx of [-6,6]){table(x+dx,z+2.5);chair(x+dx,z+4.3);chair(x+dx,z+.7,Math.PI);block(x+dx,1.25,z+2.5,.22,.35,.22,'#f5e6c8');}
    flowers(x+9,z-7);wallArt(x-3,back+.15,'#a7c7b3');
  }else if(kind==='book'){
    for(const dx of [-7,0,7]) bookshelf(x+dx,z-8);
    bookshelf(x-7,z-1);bookshelf(x+7,z-1);
    furniture(x-6,z+5,4,1.3,1.25,'#ab805c');
    block(x+4,.65,z+4,2.4,1.3,1.5,'#80aaa0');block(x+4,1.35,z+3.4,2.4,1.2,.35,'#80aaa0');
    block(x+4,.06,z+4,4,.06,3,'#d4aa7e');flowers(x+8,z-8);
  }else if(kind==='gallery'){
    for(let i=-1;i<=1;i++){
      const px=x+i*6;wallArt(px,back+.18,['#dba485','#9cbeb7','#d5bd94'][i+1]);
      block(px,2.7,back+.29,.6,.8,.07,'#668e89');
    }
    for(const dx of [-5,5]){block(x+dx,.65,z-3,1.5,1.3,1.5,'#eee4ce');block(x+dx,1.8,z-3,.8,1,.8,dx<0?'#d5a987':'#7ea7a0');}
    bench(x,z+4,Math.PI);flowers(x+8,z+6);
  }else if(kind==='market'){
    for(const dx of [-5,5]){
      furniture(x+dx,z-2,2,8,1.25,'#c7a477');
      for(let i=0;i<5;i++)for(let j=0;j<2;j++)block(x+dx+(j-.5)*.7,1.4,z-5+i*1.4,.45,.3,.6,['#da8d71','#a8bc84','#e5c87d'][i%3]);
    }
    for(let dx=-7;dx<=7;dx+=7)bookshelf(x+dx,z-8,0, '#a7a18a');
    furniture(x-6,z+6,4,1.3,1.3,'#84a89a');block(x-6,1.5,z+6,.8,.4,.6,'#4e6660');
  }else if(kind==='bakery'){
    furniture(x,z-5.5,15,2,1.2,'#c4936a');block(x,1.55,z-5.5,15,.7,1.7,'#b9d2c8');
    for(let i=-3;i<=3;i++){block(x+i*1.8,1.45,z-5.3,.9,.28,.55,'#d69c60');block(x+i*1.8,1.65,z-5.3,.7,.12,.35,'#f1c580');}
    sign('PÃO QUENTINHO',x,3.6,back+.2,8,{background:'#9b6b50'});
    for(const dx of [-6,6]){table(x+dx,z+3);chair(x+dx,z+4.7);}
  }else if(kind==='flower'){
    furniture(x,z-7,15,1.1,1.1,'#af8668');
    for(let i=-3;i<=3;i++)flowers(x+i*2.1,z-6.7);
    furniture(x-7,z+1,3,1.3,1.2,'#c19376');furniture(x+7,z+1,3,1.3,1.2,'#c19376');
    for(const dx of [-8,-6,6,8])flowers(x+dx,z+1);
    wallArt(x,back+.2,'#d6b4a6');
  }else if(kind==='home'){
    // A casa tem passagem central entre sala, cama e cozinha compacta.
    furniture(x-5,z-3.7,3.3,4,.75,'#a9866d');
    block(x-5,.85,z-4,3,.2,3.5,'#efe6d5');block(x-5,1.05,z-4.8,2.8,.25,1,'#b5cebd');
    furniture(x+5,z-4.6,4.2,1.2,1.1,'#90a99a');block(x+5,1.25,z-4.6,4.3,.15,1.3,'#e9dec6');
    block(x+5,1.55,z-4.6,.8,.5,.7,'#567b74');
    block(x+5,.65,z+1,3,1.3,1.5,'#91b0a5');block(x+5,1.35,z+.4,3,1,.3,'#91b0a5');
    block(x+5,.2,z+2.6,4,.04,2.8,'#d4ad8c');flowers(x-7,z+3);
    wallArt(x,back+.2,'#b7cbbb');
  }else if(kind==='residence'){
    furniture(x-6,z-4.6,3.5,1.2,1.2,'#8da89d');block(x-6,1.35,z-4.6,3.6,.16,1.3,'#e6e2d0');
    for(let i=0;i<4;i++){block(x+5+i*.8,2.2,back+.3,.65,.65,.2,'#b3b8ab');block(x+5+i*.8,2.2,back+.43,.4,.08,.04,'#506b65');}
    block(x+5,.65,z+1.3,3,1.3,1.4,'#8ca9a0');block(x+5,1.35,z+.7,3,1,.25,'#8ca9a0');
    flowers(x-7,z+3);wallArt(x,back+.2,'#c5b5a3');
  }else if(kind==='restaurant'){
    furniture(x,z-5.5,13,1.4,1.25,'#a8795c');block(x,1.35,z-5.5,13.2,.15,1.6,'#e7d9bd');
    for(let i=-2;i<=2;i++){block(x+i*1.7,1.55,z-5.5,.5,.3,.5,'#e7d8b9');block(x+i*1.7,1.75,z-5.5,.25,.2,.25,'#cc9273');}
    sign('COZINHA',x,3.6,back+.25,6,{background:'#9a6951'});
    for(const dx of [-5,5]){table(x+dx,z+1.5);chair(x+dx,z+3.2,Math.PI);chair(x+dx,z-.2);block(x+dx,1.18,z+1.5,.4,.12,.4,'#f5e9d5');}
    flowers(x-8,z-5);
  }else if(kind==='hospital'){
    for(let dx=-9;dx<=9;dx+=3)for(let dz=-8;dz<=8;dz+=3)block(x+dx,.19,z+dz,2.9,.03,2.9,(dx+dz)%2?'#e9e9dc':'#d6e3d9');
    furniture(x-6,z-2,7,1.8,1.3,'#a6c1b6');block(x-6,1.4,z-2,7.2,.14,2,'#f3eee0');
    block(x-6,1.67,z-2,.75,.5,.55,'#5a7774');
    for(const dx of [-5,5]){
      furniture(x+dx,z-7,3,4,1,'#eef0e5');block(x+dx,1.1,z-7.8,2.7,.2,1.5,'#d6e4dd');
      block(x+dx,1.19,z-5.9,2.5,.25,1.3,'#a9c9c2');block(x+dx+2,1.7,z-7.8,.12,3.4,.12,'#9aaca2');
      block(x+dx+2,3,z-7.8,.5,.7,.2,'#d9eae2');
    }
    for(let i=0;i<3;i++){block(x+4+i*1.5,.6,z+5,1.2,1.2,1.2,'#86ada7');block(x+4+i*1.5,1.2,z+4.5,1.2,.9,.15,'#86ada7');}
    block(x,3,back+.3,.65,2.5,.12,'#d8796b');block(x,3,back+.32,2.5,.65,.12,'#d8796b');
  }else if(kind==='police'){
    furniture(x+4,z-4.8,7,1.6,1.25,'#7799a1');block(x+4,1.36,z-4.8,7.2,.15,1.8,'#e6e6d6');
    block(x+4,1.65,z-4.8,.8,.55,.5,'#496c78');sign('ATENDIMENTO',x+3,3.5,back+.22,7,{background:'#426c83'});
    for(let i=0;i<3;i++){block(x-7+i*1.6,.6,z+4,1.25,1.2,1.2,'#829fa5');block(x-7+i*1.6,1.15,z+3.5,1.25,.8,.18,'#829fa5');}
    block(x-7,2.7,back+.3,5,2.5,.18,'#d1dfd5');
    for(const [dx,dy] of [[-1,.3],[0,-.2],[1,.2]])block(x-7+dx,2.7+dy,back+.42,.7,.55,.08,'#e5c991');
    sign('INFORMAÇÕES · E',x-7,4.4,back+.45,5,{background:'#426c83'});
    servicePoints.push({kind:'police-info',x:x-7,z:back+1.8,name:'DELEGACIA · INFORMAÇÕES',display:{x:x-7,y:3.1,z:back+.6}});
  }else if(kind==='fire'){
    furniture(x+4,z-5,7,1.6,1.25,'#a26f58');block(x+4,1.35,z-5,7.2,.15,1.8,'#e6d9c0');
    for(let i=0;i<3;i++){
      block(x-8+i*2.2,1.55,z-8,1.8,3.1,1,'#8c9a91');block(x-8+i*2.2,2.65,z-7.4,1.2,.3,.2,'#efd079');
      block(x-8+i*2.2,1.6,z-7.4,1.2,.08,.2,'#d6ba82');
    }
    sign('EQUIPAMENTOS',x-5.7,4.4,back+.2,7,{background:'#a45145'});
    block(x+10.65,1.5,z,.25,2,2.2,'#bb6153');block(x+10.45,1.6,z,.12,.6,1.4,'#eed99f');
    sign('E · ÁGUA',x+10.25,3,z,3.5,{background:'#a45145',rotation:-Math.PI/2});
    servicePoints.push({kind:'fire-water',x:x+8.5,z:z+1.1,name:'BOMBEIROS · TREINO',active:false});
    for(let i=0;i<10;i++){
      const drop=part(scene,x+10,1.7,z,.16,.16,.16,'#9ad5d9');drop.visible=false;waterDrops.push({mesh:drop,phase:i/10,originX:x+10,originZ:z-.8});
    }
  }else if(kind==='station'){
    furniture(x-5,z-2.5,5,1.3,1.2,'#bba283');block(x-5,1.5,z-2.5,.9,.5,.6,'#567a74');
    for(const dx of [2,7]){
      furniture(x+dx,z-2.8,3,1.1,1.5,'#9fae98');
      for(let i=0;i<5;i++)block(x+dx-1+i*.48,1.8,z-2.15,.3,.5,.25,['#deb472','#c8876c','#89aca4'][i%3]);
    }
    sign('CONVENIÊNCIA',x,3.7,back+.2,8,{background:'#bb6e56'});
  }
}
for(const p of places)interior(p);

// Moradores com roupas, tons de pele, cabelo e braços/pernas articulados.
function human(shirt='#d8a968', skin='#e7b896', trousers='#415b62', hair='#4c4641'){
  const g=new THREE.Group();
  part(g,0,1.3,0,.75,.95,.46,shirt);
  part(g,0,2.02,0,.55,.57,.5,skin);part(g,0,2.34,-.03,.58,.17,.53,hair);
  part(g,-.16,2.05,.255,.075,.075,.035,'#3e4d49');part(g,.16,2.05,.255,.075,.075,.035,'#3e4d49');
  part(g,0,1.88,.257,.16,.035,.035,'#805b50');
  const arms=[],legs=[];
  for(const side of [-1,1]){
    const arm=new THREE.Group();arm.position.set(side*.52,1.65,0);g.add(arm);
    part(arm,0,-.35,0,.23,.72,.25,shirt);part(arm,0,-.78,0,.21,.2,.23,skin);arms.push(arm);
    const leg=new THREE.Group();leg.position.set(side*.2,.84,0);g.add(leg);
    part(leg,0,-.41,0,.27,.82,.3,trousers);part(leg,0,-.76,.07,.31,.16,.46,'#42514e');legs.push(leg);
  }
  g.userData={shirt,skin,trousers,hair,limbs:[...legs,...arms],shoes:[]};
  batchGroupCubes(g);return g;
}
const player=human('#e9b857','#e8b896','#3c5862','#594942');player.position.set(-26,0,55);player.rotation.y=Math.PI;scene.add(player);
const locals=[
  ['Lia','O Café Aurora sempre tem um bolo fresquinho no fim da tarde.'],
  ['Tomás','A galeria é um bom lugar para passar um fim de tarde.'],
  ['Bia','O calçadão e o píer ficam na direção do mar.'],
  ['Rui','A livraria tem um canto calmo para descansar.'],
  ['Nina','Tem um posto de gasolina na avenida leste e um hospital ao lado da praça.'],
  ['Pedro','De manhã, a orla é um ótimo lugar para passear.'],
  ['Clara','Hoje a cidade está bonita, não está?'],
  ['Davi','As duas praças são meus lugares preferidos para passear.']
];
const shirts=['#da8573','#689cad','#e1b568','#a595b9','#8dac8b','#d28c9b','#e8c6a3'];
const skins=['#edc09b','#d59d76','#b97e5c','#8c624d'];
for(let i=0;i<42;i++){
  const alongZ=i%2===0, road=roads[i%3], side=(Math.floor(i/3)%2?1:-1);
  let location=0,x=0,z=0;
  for(let attempt=0;attempt<100;attempt++){
    location=-112+random()*224;x=alongZ?road+side*8.25:location;z=alongZ?location:road+side*8.25;
    if(!collides(x,z,.42)&&people.every(p=>Math.hypot(p.g.position.x-x,p.g.position.z-z)>2.5))break;
  }
  const g=human(shirts[i%shirts.length],skins[i%skins.length],i%2?'#4a6568':'#6c7776',i%3?'#574c45':'#3d3736');
  g.position.set(x,groundHeight(x,z),z);g.rotation.y=alongZ?0:Math.PI/2;scene.add(g);
  people.push({g,name:locals[i%locals.length][0],line:locals[i%locals.length][1],axis:alongZ?'z':'x',origin:location,phase:random()*8,range:7+random()*7,dir:i%4<2?1:-1,speed:.85+random()*.6,wait:0,indoor:false});
}
for(let i=0;i<12;i++){
  const x=-180+i*32,z=curvedRoadZ(x)+(i%2?8.6:-8.6);
  const g=human(pick(shirts),pick(skins));g.position.set(x,groundHeight(x,z),z);scene.add(g);
  people.push({g,name:locals[i%locals.length][0],line:'A avenida das Colinas leva ao bairro-jardim e à praça do lago.',axis:'x',origin:x,phase:random()*8,range:5+random()*5,dir:i%2?1:-1,speed:.9+random()*.4,wait:0,indoor:false,curved:true,side:i%2?1:-1});
}
function serviceUniform(g,kind){
  if(kind==='police'){
    part(g,0,2.4,0,.72,.18,.58,'#334f69');part(g,0,2.34,.32,.8,.08,.24,'#334f69');
    part(g,-.19,1.48,.24,.16,.2,.05,'#e6cc80');part(g,0,.91,.24,.7,.1,.05,'#33464e');
  }else if(kind==='fire'){
    part(g,0,2.43,0,.74,.22,.62,'#e2bd65');part(g,0,2.3,.3,.78,.08,.23,'#e2bd65');
    part(g,0,1.13,.24,.72,.13,.05,'#f1cf72');part(g,0,1.48,-.31,.4,.64,.16,'#a9b5ac');
    for(const arm of g.userData.limbs.slice(2))part(arm,0,-.4,.14,.24,.12,.04,'#f1cf72');
  }
}
const staff={
  cafe:['Lia · barista','Entra, fica à vontade. O café está sempre quentinho.'],
  bakery:['Cida · padeirа','Pão fresquinho todo dia. Pode olhar à vontade!'],
  market:['Dona Rosa · comerciante','Bem-vindo! Temos frutas, verduras e coisas para a casa.'],
  gallery:['Tomás · artista','Essas cores todas foram inspiradas na nossa baía.'],
  book:['Rui · livreiro','Tem histórias para todos os gostos por aqui.'],
  flower:['Flor · florista','As flores gostam do sol de Baía do Sol tanto quanto a gente.'],
  hospital:['Joana · enfermeira','Olá! Aqui a gente cuida dos moradores da cidade.'],
  station:['Caio · atendente','Passe no posto para dar uma olhada e descansar da viagem.'],
  police:['Marina · policial','Bem-vindo à delegacia. Pode conferir o quadro de informações ali no fundo.'],
  fire:['Henrique · bombeiro','Olá! Os equipamentos estão prontos; o painel de treino fica perto da parede.'],
  home:['Morador','Gosto de passar a manhã aqui antes de sair pelo bairro.'],
  residence:['Porteiro','Bem-vindo! Os vizinhos sempre se encontram por aqui.'],
  restaurant:['Cozinheira','A comida está saindo fresquinha da cozinha.']
};
for(const p of places){
  const [name,line]=staff[p.kind];const shirt=p.kind==='hospital'?'#f1eee0':p.kind==='police'?'#507591':p.kind==='fire'?'#bd5e4c':'#d6917a';
  const g=human(shirt,pick(skins));if(p.kind==='police'||p.kind==='fire')serviceUniform(g,p.kind);
  const staffX=p.kind==='station'?p.x-6:p.kind==='home'?p.x+4:p.kind==='residence'?p.x-6:p.x+8;
  const staffZ=p.kind==='station'?p.z-4:p.kind==='home'?p.z-3:p.kind==='residence'?p.z-5:p.kind==='restaurant'?p.z-7:p.z-7.8;
  g.position.set(staffX,groundHeight(staffX,staffZ),staffZ);g.rotation.y=0;scene.add(g);people.push({g,name,line,indoor:true,place:p,role:p.kind});
}

// Motorista desenhado na escala da cabine, com rosto e mãos no volante.
function makeDriver(shirt,skin){
  const g=new THREE.Group();
  part(g,0,1.47,-.12,.5,.56,.38,shirt);
  part(g,0,1.91,-.08,.5,.46,.44,skin);part(g,0,2.15,-.1,.52,.1,.46,'#53463d');
  part(g,-.11,1.96,.155,.065,.07,.03,'#314946');part(g,.11,1.96,.155,.065,.07,.03,'#314946');
  part(g,0,1.82,.155,.12,.025,.03,'#865f51');
  for(const side of [-1,1])part(g,side*.255,1.96,.025,.03,.07,.065,'#314946');
  for(const side of [-1,1]){
    part(g,side*.28,1.56,-.08,.14,.42,.18,shirt);
    part(g,side*.25,1.59,.47,.12,.12,.9,shirt);
    part(g,side*.22,1.59,.89,.14,.14,.16,skin);
  }
  batchGroupCubes(g);return g;
}

// Carros com faróis e lanternas; o veículo dirigido ilumina a rua de verdade.
const headMat=new THREE.MeshStandardMaterial({color:'#ffefbf',emissive:'#ffdd8a',emissiveIntensity:1.0});
const carGlass=new THREE.MeshStandardMaterial({color:'#abd6d7',transparent:true,opacity:.18,roughness:.18,metalness:.05,depthWrite:false});
function makeCar(x,z,color,options={}){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=options.rotation||0;scene.add(g);
  part(g,0,.66,0,2.5,.8,4.6,color);part(g,0,1.15,-.15,2.32,.4,3.8,color);
  // Cabine aberta por dentro, com vidros translúcidos e componentes visíveis.
  part(g,0,2.24,-.3,2.05,.14,2.35,'#eadfcb');
  for(const side of [-1,1])for(const zz of [-1.42,.82])part(g,side*.95,1.69,zz,.16,1.05,.16,color);
  part(g,0,1.68,.88,1.75,.82,.04,carGlass);part(g,0,1.68,-1.5,1.75,.82,.04,carGlass);
  for(const side of [-1,1]){
    part(g,side*1.02,1.68,-.3,.035,.82,1.95,carGlass);
    part(g,side*1.02,1.68,-.3,.09,.95,.1,color);
    part(g,side*1.26,1.05,.4,.08,.08,.42,'#e3dec9');
  }
  for(const side of [-1,1]){
    part(g,side*.48,1.35,-.35,.75,.28,.78,'#8b7968');
    part(g,side*.48,1.68,-.72,.75,.72,.16,'#8b7968');
  }
  part(g,0,1.45,.7,1.8,.25,.35,'#667970');
  const wheel=new THREE.Mesh(new THREE.TorusGeometry(.22,.055,5,10),material('#394a47'));
  wheel.position.set(-.48,1.61,.58);g.add(wheel);
  const driver=makeDriver(options.auto?'#76958e':'#e9b857',options.auto?'#d9ac88':'#e8b896');
  driver.position.set(-.48,0,-.32);driver.visible=!!options.auto;g.add(driver);
  const wheels=[];
  for(const side of [-1,1]){
    for(const zz of [-1.5,1.5]){
      const tire=new THREE.Mesh(new THREE.CylinderGeometry(.5,.5,.34,10),material('#343f3f'));
      tire.rotation.z=Math.PI/2;tire.position.set(side*1.22,.52,zz);tire.castShadow=true;g.add(tire);wheels.push(tire);
      const hub=new THREE.Mesh(new THREE.CylinderGeometry(.22,.22,.36,8),material('#a8b4a6'));hub.rotation.z=Math.PI/2;hub.position.copy(tire.position);g.add(hub);
    }
    part(g,side*.83,.78,2.33,.55,.25,.08,headMat);
  }
  part(g,0,.4,2.36,2.25,.13,.12,'#d8daca');part(g,0,.43,-2.36,2.25,.13,.12,'#cbd1c2');
  const brake=new THREE.MeshStandardMaterial({color:'#a44d43',emissive:'#c73f35',emissiveIntensity:.35});
  const reverse=new THREE.MeshStandardMaterial({color:'#b8c3b6',emissive:'#fff5d8',emissiveIntensity:0});
  const indicators={left:new THREE.MeshStandardMaterial({color:'#926c42',emissive:'#ffb13b',emissiveIntensity:0}),right:new THREE.MeshStandardMaterial({color:'#926c42',emissive:'#ffb13b',emissiveIntensity:0})};
  for(const side of [-1,1]){
    part(g,side*.72,.78,-2.34,.38,.24,.08,brake);part(g,side*.35,.78,-2.34,.22,.22,.08,reverse);
    const indicator=side<0?indicators.left:indicators.right;
    part(g,side*1.08,.77,-2.35,.2,.2,.08,indicator);part(g,side*1.08,.77,2.35,.2,.2,.08,indicator);
  }
  const spots=[];
  for(const side of [-1,1]){
    const light=new THREE.SpotLight('#ffe8b5',80,28,.38,.5,1.4);light.position.set(side*.82,.85,2.38);
    const target=new THREE.Object3D();target.position.set(side*1.7,.05,18);g.add(target);light.target=target;light.visible=false;g.add(light);spots.push(light);
  }
  batchGroupCubes(g);
  const c={g,auto:!!options.auto,axis:options.axis||'z',direction:options.direction||1,laneRoad:options.road,velocity:0,brake,reverse,indicators,signal:null,spots,driver,wheels,stoppedReason:null,width:2.5,length:4.6,centerOffset:0};cars.push(c);return c;
}
function districtTrack(road){
  const points=[];for(let i=4;i<=56;i++){const p=road.curve.getPointAt(i/60);points.push({x:p.x,z:p.z});}
  const branchEndIndex=points.length-1,west=road.id==='oeste',side=west?-1:1;
  for(const [x,z] of west?[[-133.3,66],[-133.3,42],[-133.3,12],[-133.3,-12],[-133.3,-42],[-133.3,-66],[-135,-70]]:[[133.3,-66],[133.3,-42],[133.3,-12],[133.3,12],[133.3,42],[133.3,66],[135,70]])points.push({x,z});
  const track=makeTrack(points);track.id=road.id;track.branchEnd=track.cumulative[branchEndIndex];track.oneWay=true;track.ringX=side*133.3;
  track.stops=road.stopMarkers.map((stop,index)=>({...stop,id:`${road.id}-${index}`,progress:trackNearest(track,stop.x,stop.z)}));
  return track;
}
function districtTrackPosition(track,progress,lane){
  const d=(progress%track.length+track.length)%track.length,p=trackSample(track,d);
  const blend=d<track.branchEnd?THREE.MathUtils.smoothstep(d,0,12)*THREE.MathUtils.smoothstep(track.branchEnd-d,0,12):0;
  return {...p,x:p.x+p.dz*lane*blend,z:p.z-p.dx*lane*blend,blend};
}
const districtTracks=districtRoads.filter(r=>r.oneWay).map(districtTrack);
makeCar(-18,60,'#e7b157');makeCar(22,61,'#8cb8ab');makeCar(-57,-17,'#d58a76',{rotation:Math.PI/2});makeCar(103,-21,'#7fa9b6');
for(const [x,color] of [[-166.5,'#d9ab73'],[-159.1,'#8caea8'],[-155.4,'#c88d83']])makeCar(x,-103,color);
for(const track of districtTracks)for(let lane of [-2.6,2.6]){
  const progress=track.branchEnd*(lane<0?.2:.62),p=districtTrackPosition(track,progress,lane);
  const car=makeCar(p.x,p.z,lane<0?'#d7a37c':'#86ada8',{auto:true,axis:'district',rotation:Math.atan2(p.dx,p.dz)});
  Object.assign(car,{track,routeProgress:progress,lane,laneTarget:lane,homeLane:lane,stopDone:new Set(),stopWait:0,stopCount:0,avoids:0,avoidTime:0});
}
for(let i=0;i<10;i++){
  const axis=i%2?'x':'z',dir=i%4<2?1:-1,road=roads[i%3],along=-110+((i*37)%220);
  const x=axis==='z'?road+(dir>0?3.3:-3.3):along;
  const z=axis==='x'?road+(dir>0?-3.3:3.3):along;
  makeCar(x,z,['#dec37f','#80aeb1','#d28977','#8ea393','#bb9cb1'][i%5],{auto:true,axis,direction:dir,rotation:axis==='z'?(dir>0?0:Math.PI):(dir>0?Math.PI/2:-Math.PI/2)});
}

// Trânsito das vias laterais e da orla, sobre o circuito conectado.
for(const side of [-1,1]){
  const lane=side*130;makeCar(lane+3.3,-96,side<0?'#b9967d':'#83aaa1',{auto:true,axis:'z',direction:1,rotation:0,road:lane});
}
makeCar(-60,-134.3,'#d0a877',{auto:true,axis:'x',direction:1,rotation:Math.PI/2,road:-131});
makeCar(54,-127.7,'#8ca2b3',{auto:true,axis:'x',direction:-1,rotation:-Math.PI/2,road:-131});

// Carros adicionais percorrem a avenida curva em ambos os sentidos.
for(let i=0;i<4;i++){
  const x=-150+i*100,dir=i%2?1:-1,z=curvedRoadZ(x)+(dir>0?-3.2:3.2);
  const heading=Math.atan2(dir,dir*(12/39)*Math.cos(x/39));
  const car=makeCar(x,z,['#d48670','#8db2a2','#e0be79','#89a3b3'][i],{auto:true,axis:'curve',direction:dir,rotation:heading});
  car.curve=true;
}

// Bicicletas estacionadas que também podem ser usadas com E.
function bikeTube(g,a,b,color,width=.1){
  const start=new THREE.Vector3(...a),end=new THREE.Vector3(...b),delta=end.clone().sub(start);
  const tube=part(g,0,0,0,width,delta.length(),width,color);
  tube.position.copy(start).add(end).multiplyScalar(.5);
  tube.quaternion.setFromUnitVectors(new THREE.Vector3(0,1,0),delta.normalize());
}
function makeBike(x,z,color,rotation=0){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=rotation;scene.add(g);
  for(const zz of [-1.25,1.25]){
    const tire=new THREE.Mesh(new THREE.CylinderGeometry(.62,.62,.15,14),material('#394746'));
    tire.rotation.z=Math.PI/2;tire.position.set(0,.65,zz);g.add(tire);
    const rim=new THREE.Mesh(new THREE.CylinderGeometry(.45,.45,.17,14),material('#c8d2c4'));
    rim.rotation.z=Math.PI/2;rim.position.copy(tire.position);g.add(rim);
    const hub=new THREE.Mesh(new THREE.CylinderGeometry(.1,.1,.19,10),material('#65776e'));
    hub.rotation.z=Math.PI/2;hub.position.copy(tire.position);g.add(hub);
  }
  for(const [a,b] of [
    [[0,.65,-1.25],[0,.85,0]],[[0,.85,0],[0,1.55,-.65]],[[0,1.55,-.65],[0,.65,-1.25]],
    [[0,1.55,-.65],[0,1.48,.8]],[[0,1.48,.8],[0,.85,0]],[[0,1.48,.8],[0,.65,1.25]]
  ])bikeTube(g,a,b,color,.13);
  bikeTube(g,[0,1.48,.8],[0,1.75,1], '#546a61',.11);
  part(g,0,1.76,1,.9,.1,.12,'#546a61');part(g,0,1.58,-.68,.6,.12,.38,'#584f46');
  part(g,0,1.18,.95,.18,.2,.16,headMat);
  const rider=human('#e9b857','#e8b896','#3c5862','#594942');rider.scale.setScalar(.78);rider.position.set(0,.62,-.35);rider.visible=false;g.add(rider);
  const brake=new THREE.MeshStandardMaterial({color:'#ab5448',emissive:'#c24e40',emissiveIntensity:.35});
  part(g,0,1.25,-1.2,.18,.18,.1,brake);
  const vehicle={g,auto:false,axis:'z',direction:1,velocity:0,brake,spots:[],rider,kind:'bike'};
  cars.push(vehicle);return vehicle;
}
makeBike(-51,58,'#d98269',.16);makeBike(19,53,'#7daea5',-.08);makeBike(91,53,'#e6ba74',.2);

// Floresta baixa e montanhas quadradas prolongam a paisagem ao sul e aos lados.
block(0,-.65,80,800,.5,410,'#91ad78');
function forestTree(x,z,scale){
  const base=terrainHeight(x,z);
  cameraObstacles.push({x,z,w:5*scale,d:5*scale,bottom:base+3.4*scale,top:base+8.8*scale});
  const shade=pick(['#557e59','#5f8c62','#6d9a6b','#7da777']);
  block(x,base+2.7*scale,z,.9*scale,5.4*scale,.9*scale,'#887151');
  block(x,base+5.5*scale,z,4.8*scale,4.2*scale,4.6*scale,shade);
  block(x-.6*scale,base+7.4*scale,z,3.2*scale,2.6*scale,3.2*scale,shade);
}
for(let z=-120;z<=225;z+=5){forestTree(-213-random()*3,z+(random()-.5)*2,.8+random()*.45);forestTree(213+random()*3,z+(random()-.5)*2,.8+random()*.45);}
for(let x=-210;x<=210;x+=5)forestTree(x+(random()-.5)*2,229+random()*3,.8+random()*.45);
for(let i=0;i<420;i++){
  const edge=i%3,side=i%2?1:-1;
  const x=edge===0?side*(225+random()*135):-300+random()*600;
  const z=edge===0?-120+random()*480:240+random()*130;
  forestTree(x,z,.7+random()*.9);
}
for(const side of [-1,1])for(let i=0;i<4;i++){
  const x=side*(260+i*50),z=260+i*18;
  block(x,10+i*3,z,70,20+i*6,60,'#829f7d');
  block(x,22+i*3,z,45,8,40,'#97af89');
}

// Gramados, canteiros, arbustos e flores nos espaços livres do bairro.
function canPlant(x,z){
  if(isPaved(x,z,1.5))return false;
  if(x>88&&z>-62&&z<-10)return false;
  if(sites.some(site=>!['park','park2'].includes(site.type)&&Math.abs(x-site.x)<14&&Math.abs(z-site.z)<13))return false;
  if(Math.abs(x+36)<26&&Math.abs(z-36)<26&&(Math.abs(x+36)<5||Math.abs(z-36)<5))return false;
  if(Math.abs(x-104)<15&&Math.abs(z-104)<15&&(Math.abs(x-104)<3||Math.abs(z-104)<3))return false;
  return !solids.some(s=>Math.abs(x-s.x)<s.w/2+1&&Math.abs(z-s.z)<s.d/2+1);
}
const grassColors=['#81a974','#8db57d','#759e6d','#a4c18b'],vegetationPositions=[];
for(let i=0;i<2300;i++){
  const x=-118+random()*236,z=-118+random()*236;if(!canPlant(x,z))continue;
  vegetationPositions.push({x,z});
  const base=groundHeight(x,z),color=pick(grassColors),height=.18+random()*.27;
  block(x,base+height/2,z,.09,height,.09,color);
  block(x+.16,base+height*.35,z+.1,.07,height*.7,.07,color);
  if(i%7===0){
    block(x-.12,base+.25,z-.15,.06,.5,.06,'#68965f');
    block(x-.12,base+.53,z-.15,.23,.17,.23,pick(['#f2db9c','#e9a5a0','#f5eee0','#d7b4cf']));
  }
  if(i%23===0){
    block(x,base+.45,z,1.2,.9,1,'#709d70');block(x+.4,base+.62,z+.1,.85,.9,.8,'#8eb47d');
  }
}
// Pétalas e arbustos organizados em pequenas bordas de jardim.
for(const [x,z] of [[-54,29],[-50,42],[-22,30],[-23,44],[95,98],[113,110],[94,112],[115,96]]){
  if(isPaved(x,z,1.5)||collides(x,z,.55))continue;
  vegetationPositions.push({x,z});
  const base=groundHeight(x,z);
  for(let i=0;i<6;i++){
    const dx=(i-2.5)*.32;block(x+dx,base+.26,z,.07,.52,.07,'#648f62');
    block(x+dx,base+.55,z,.22,.18,.22,i%2?'#e7a6a0':'#f2d99c');
  }
}
// Iluminação e gramados acompanham a avenida curva, sem ocupar a faixa livre.
for(let x=-190;x<=190;x+=20){
  if(roads.some(road=>Math.abs(x-road)<12))continue;
  const side=(Math.round((x+190)/20)%2?1:-1);lamp(x,curvedRoadZ(x)+side*10.5);
}
for(let i=0;i<900;i++){
  const x=-199+random()*398,z=128+random()*85;
  if(isPaved(x,z,1.5)||solids.some(s=>Math.abs(x-s.x)<s.w/2+.7&&Math.abs(z-s.z)<s.d/2+.7))continue;
  vegetationPositions.push({x,z});
  const base=groundHeight(x,z),height=.15+random()*.25,color=pick(grassColors);
  block(x,base+height/2,z,.08,height,.08,color);block(x+.15,base+height*.35,z+.08,.07,height*.7,.07,color);
  if(i%10===0){block(x-.1,base+.25,z-.1,.06,.5,.06,'#68965f');block(x-.1,base+.52,z-.1,.2,.16,.2,pick(['#f2db9c','#e9a5a0','#f5eee0']));}
}

// Um pouco de lixo nos cantos das ruas, sem transformar a cidade num depósito.
for(let i=0;i<115;i++){
  const road=roads[i%3],side=i%2?1:-1,along=-115+random()*230;
  const x=i%4<2?road+side*(5.6+random()*.7):along;
  const z=i%4<2?along:road+side*(5.6+random()*.7);
  const y=groundHeight(x,z)+.025;
  if(i%5===0){block(x,y+.1,z,.13,.25,.13,pick(['#dba284','#d9d8c3','#86a6a1']));}
  else block(x,y,z,.28,.035,.2,pick(['#f4ecd8','#d8d9cc','#e9c2a6']),random()*Math.PI);
}

// Pessoas sentadas em bancos da praça e conversando em duplas.
function seatedHuman(shirt,skin){
  const g=new THREE.Group();
  part(g,0,1.3,0,.75,.95,.46,shirt);part(g,0,2.02,0,.55,.57,.5,skin);part(g,0,2.34,-.03,.58,.17,.53,'#52483f');
  part(g,-.16,2.05,.255,.075,.075,.035,'#3e4d49');part(g,.16,2.05,.255,.075,.075,.035,'#3e4d49');part(g,0,1.88,.257,.16,.035,.035,'#805b50');
  for(const side of [-1,1]){
    part(g,side*.51,1.24,.05,.22,.68,.24,shirt);part(g,side*.51,.9,.06,.2,.2,.22,skin);
    part(g,side*.2,.72,.35,.27,.25,.72,'#526871');part(g,side*.2,.36,.66,.27,.72,.28,'#526871');
    part(g,side*.2,.08,.73,.31,.16,.45,'#44534f');
  }
  g.userData={shirt,skin,trousers:'#526871',hair:'#52483f'};batchGroupCubes(g);return g;
}
for(const [bx,bz,rot] of [[-52,24,0],[-20,24,0],[-52,48,Math.PI],[-20,48,Math.PI],[94,97,0],[114,111,Math.PI]]){
  const g=seatedHuman(pick(shirts),pick(skins));g.position.set(bx,groundHeight(bx,bz),bz);g.rotation.y=rot;scene.add(g);
  people.push({g,name:pick(['Ana','Elisa','Samuel','Lúcia','Rafa']),line:'Gosto de sentar um pouco e ver a cidade passar.',stationary:true,sitting:true,indoor:false,phase:random()*6});
}
for(const place of places.filter(p=>p.kind==='restaurant'))for(const dx of [-5,5]){
  const x=place.x+dx,z=place.z+3.2,g=seatedHuman(pick(shirts),pick(skins));
  g.position.set(x,groundHeight(x,z),z);g.rotation.y=Math.PI;scene.add(g);
  people.push({g,name:pick(['Luana','João','Rita','Daniel']),line:'A comida daqui é uma delícia.',stationary:true,sitting:true,indoor:true,place});
}
function socialPair(x,z){
  for(const side of [-1,1]){
    const g=human(pick(shirts),pick(skins));g.position.set(x+side*.7,groundHeight(x+side*.7,z),z);g.rotation.y=side<0?Math.PI/2:-Math.PI/2;scene.add(g);
    people.push({g,name:pick(['André','Marina','Tainá','Caio','João']),line:'A gente estava colocando a conversa em dia!',stationary:true,chatting:true,indoor:false,phase:random()*6});
  }
}
socialPair(-47,32);socialPair(-25,51);socialPair(26,54);socialPair(96,53);socialPair(96,101);

// Profissionais da cidade: segurança, bombeiros, limpeza e venda de pipoca.
function worker(x,z,name,line,color,hat,role=null,lines=null){
  const g=human(color,pick(skins),'#465e64','#494039');g.position.set(x,groundHeight(x,z),z);g.rotation.y=Math.PI/2;scene.add(g);
  if(role)serviceUniform(g,role);else part(g,0,2.38,0,.68,.22,.58,hat);
  const person={g,name,line,lines,role,stationary:true,chatting:true,phase:random()*6,indoor:false};people.push(person);return person;
}
function servicePatrol(x,z,role,name,lines){
  const g=human(role==='fire'?'#bd5e4c':'#507591',pick(skins),'#465e64','#494039');serviceUniform(g,role);
  g.position.set(x,groundHeight(x,z),z);scene.add(g);
  people.push({g,name,line:lines[0],lines,role,axis:'x',origin:x,phase:random()*6,range:3,dir:1,speed:.65,wait:0,indoor:false});
}
worker(-29,-88,'Bia · bombeira','Estamos de plantão para cuidar da cidade.','#bd5e4c','#e8c36d','fire',[
  'Estamos conferindo os equipamentos antes da próxima saída.','A equipe treina com a mangueira para estar pronta quando for preciso.'
]);
servicePatrol(-25,-84,'fire','Rafael · bombeiro',['Bom dia! A equipe fica de plantão no quartel.','O caminhão está estacionado na vaga ao lado da entrada.']);
worker(42,-88,'Davi · policial','Pode passear tranquilo.','#507591','#344f62','police',[
  'A delegacia fica bem aqui; você pode entrar para conhecer.','Estamos cuidando das travessias e fazendo a ronda pelo bairro.'
]);
servicePatrol(44,-84,'police','Mara · policial',['Estou fazendo a ronda por este quarteirão.','As faixas ficam nas esquinas; olhe os dois lados antes de atravessar.']);
worker(-44,53,'Zeca · vendedor de pipoca','Pipoca quentinha para passear pela praça!','#e7b875','#f5dec0');
worker(-86,31,'Célia · limpeza urbana','Ajudo a deixar a praça e as calçadas cuidadas.','#7ea38a','#729781');
// Carrinho do vendedor, com rodas, toldo e grãos de pipoca.
block(-47,.8,53,2.1,1.6,1.2,'#f2d5ad');solid(-47,53,2.1,1.2);
block(-47,1.65,53,2.3,.12,1.4,'#d57962');
for(const x of [-47.8,-46.2]){block(x,2.25,53,.1,1.2,.1,'#a87960');block(x,.3,53,.4,.4,.4,'#56645c');}
block(-47,2.9,53,2.7,.22,1.7,'#e79479');
for(let i=0;i<6;i++)block(-47.7+i*.28,1.85,53,.18,.25,.18,'#f6e5af');
sign('PIPOCA',-47,2.55,53.75,2.2,{background:'#c86c58'});
function vehicleSideLabel(g,text,side,z,width=3.3,depth=1.31){
  const c=document.createElement('canvas');c.width=512;c.height=128;const ctx=c.getContext('2d');
  ctx.fillStyle='#f5f0dd';ctx.font='bold 78px system-ui';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(text,256,68,470);
  const texture=new THREE.CanvasTexture(c);texture.colorSpace=THREE.SRGBColorSpace;
  const m=new THREE.Mesh(new THREE.PlaneGeometry(width,width/4),new THREE.MeshBasicMaterial({map:texture,transparent:true,side:THREE.DoubleSide}));
  m.position.set(side*depth,1.55,z);m.rotation.y=side*Math.PI/2;g.add(m);
}
function addEmergencyLights(c){
  const red=new THREE.MeshStandardMaterial({color:'#ad514b',emissive:'#ff5146',emissiveIntensity:0});
  const blue=new THREE.MeshStandardMaterial({color:'#4c768d',emissive:'#65bdff',emissiveIntensity:0});
  part(c.g,0,2.34,-.3,1.05,.12,.36,'#e2e0d0');part(c.g,-.28,2.48,-.3,.4,.19,.34,red);part(c.g,.28,2.48,-.3,.4,.19,.34,blue);
  c.emergency={red,blue,on:false};
}
// Viatura e caminhão ficam nas vagas das respectivas unidades, sem bloquear a porta.
const policeCar=makeCar(30.5,-87,'#e5e8df');
for(const side of [-1,1]){part(policeCar.g,side*1.26,.8,0,.06,.34,4.25,'#527c99');vehicleSideLabel(policeCar.g,'POLÍCIA',side,-.15,2.8);}
addEmergencyLights(policeCar);policeCar.role='police';
const fireTruck=makeCar(-41.5,-87,'#c8594b');fireTruck.width=2.6;fireTruck.length=7.7;fireTruck.centerOffset=-1.55;fireTruck.role='fire';
part(fireTruck.g,0,1.45,-3.35,2.6,1.8,4,'#c8594b');part(fireTruck.g,0,2.4,-3.35,2.5,.15,4,'#e1daca');
for(const side of [-1,1]){
  part(fireTruck.g,side*1.32,1.35,-3.35,.08,.9,3.7,'#bdc5b9');
  part(fireTruck.g,side*1.38,1.35,-3.35,.07,.1,3.7,'#637b76');
  part(fireTruck.g,side*1.35,.65,-3.35,.08,.16,3.8,'#f0dcad');
  for(const zz of [-4.1]){
    const tire=new THREE.Mesh(new THREE.CylinderGeometry(.5,.5,.34,10),material('#343f3f'));tire.rotation.z=Math.PI/2;tire.position.set(side*1.28,.52,zz);tire.castShadow=true;fireTruck.g.add(tire);fireTruck.wheels.push(tire);
  }
  vehicleSideLabel(fireTruck.g,'BOMBEIROS',side,-3.4,3.5,1.43);
  part(fireTruck.g,side*.8,.76,-5.38,.38,.22,.08,fireTruck.brake);
  part(fireTruck.g,side*.4,.76,-5.38,.22,.2,.08,fireTruck.reverse);
  part(fireTruck.g,side*1.1,.76,-5.38,.22,.2,.08,side<0?fireTruck.indicators.left:fireTruck.indicators.right);
}
for(const sx of [-.55,.55])part(fireTruck.g,sx,2.7,-3.35,.1,.12,4,'#dbe0d5');
for(let z=-5;z<-1.5;z+=.5)part(fireTruck.g,0,2.7,z,1.2,.1,.1,'#dbe0d5');
addEmergencyLights(fireTruck);

// Linha circular utiliza as pistas existentes e faz curvas dentro dos cruzamentos.
function makeBusTrack(){
  const points=[{x:-68.7,z:-62.7}];
  function line(x,z){const a=points[points.length-1],n=Math.ceil(Math.hypot(x-a.x,z-a.z)/3);for(let i=1;i<=n;i++)points.push({x:a.x+(x-a.x)*i/n,z:a.z+(z-a.z)*i/n});}
  function arc(x,z,start,end){for(let i=1;i<=12;i++){const a=start+(end-start)*i/12;points.push({x:x+6*Math.cos(a),z:z+6*Math.sin(a)});}}
  line(-68.7,62.7);arc(-62.7,62.7,Math.PI,Math.PI/2);
  line(62.7,68.7);arc(62.7,62.7,Math.PI/2,0);
  line(68.7,-62.7);arc(62.7,-62.7,0,-Math.PI/2);
  line(-62.7,-68.7);arc(-62.7,-62.7,-Math.PI/2,-Math.PI);
  const track=makeTrack(points);track.id='circular';
  track.stops=busStops.map(stop=>({...stop,progress:trackNearest(track,stop.roadX,stop.roadZ)})).sort((a,b)=>a.progress-b.progress);
  return track;
}
function makeBus(track){
  const progress=trackNearest(track,-68.7,-52),p=trackSample(track,progress),g=new THREE.Group();
  g.position.set(p.x,groundHeight(p.x,p.z),p.z);g.rotation.y=Math.atan2(p.dx,p.dz);scene.add(g);
  part(g,0,.72,0,3.1,1.28,9,'#dfbd79');part(g,0,1.39,-4.35,3,.55,.3,'#d0a968');
  part(g,0,3.1,0,3.1,.2,9,'#f1e9d4');
  for(const side of [-1,1]){
    for(const z of [-4,-2,0,2,4])part(g,side*1.45,2.15,z,.15,1.85,.15,'#a7865e');
    part(g,side*1.5,2.12,-1,0.04,1.45,5.7,carGlass);
    part(g,side*1.55,.9,0,.05,.2,8.6,'#f4e6c9');
    vehicleSideLabel(g,'BAÍA · CIRCULAR',side,-1.1,4.7,1.58);
  }
  part(g,0,2.12,4.44,2.8,1.45,.05,carGlass);part(g,0,2.12,-4.44,2.8,1.45,.05,carGlass);
  const doors=[];for(const z of [2,2.9])doors.push(part(g,1.56,1.75,z,.08,2.3,.85,carGlass));
  for(const [x,z] of [[-.68,2.9],[.6,-.2],[-.65,-1.4],[.65,-2.4],[-.65,-2.8]]){
    part(g,x,1.12,z,.8,.18,.85,'#799b90');part(g,x,1.55,z-.4,.8,.8,.14,'#799b90');
    part(g,x,.7,z,.1,.75,.1,'#6b7f75');
  }
  part(g,0,2.85,-.3,.09,.09,7.3,'#ddd6bd');part(g,-.2,1.95,1.2,.08,1.8,.08,'#ddd6bd');
  part(g,-.68,1.3,3.85,1,.22,.55,'#687f78');
  for(const x of [-1,1]){part(g,x,.87,4.53,.6,.32,.12,headMat);part(g,x,.88,-4.53,.5,.27,.12,'#bb6254');}
  const wheels=[];for(const x of [-1.56,1.56])for(const z of [-2.9,2.9]){
    const wheel=new THREE.Mesh(new THREE.CylinderGeometry(.62,.62,.35,12),material('#344747'));wheel.rotation.z=Math.PI/2;wheel.position.set(x,.58,z);g.add(wheel);wheels.push(wheel);
    const hub=new THREE.Mesh(new THREE.CylinderGeometry(.3,.3,.37,10),material('#a5b6a5'));hub.rotation.z=Math.PI/2;hub.position.copy(wheel.position);g.add(hub);
  }
  const driver=makeDriver('#6c8e82','#d7a582');driver.position.set(-.68,.1,2.9);g.add(driver);
  const rider=makeDriver('#e9b857','#e8b896');rider.position.set(.6,.1,-.2);rider.visible=false;g.add(rider);
  const brake=new THREE.MeshStandardMaterial({color:'#ab5448',emissive:'#c24e40',emissiveIntensity:.35});
  part(g,-1,.84,-4.58,.45,.22,.08,brake);part(g,1,.84,-4.58,.45,.22,.08,brake);
  batchGroupCubes(g,doors);
  const bus={g,track,routeProgress:progress,kind:'bus',auto:true,velocity:0,width:3.1,length:9,centerOffset:0,brake,driver,rider,wheels,doors,spots:[],atStop:null,dwell:0,stoppedReason:null,passengers:[],stopCount:0};
  cars.push(bus);return bus;
}
const busTrack=makeBusTrack(),cityBus=makeBus(busTrack);
for(const stop of busStops){
  const g=human(pick(shirts),pick(skins));g.position.set(stop.x+(stop.x<0?-1.2:1.2),groundHeight(stop.x,stop.z),stop.z+2.2);
  g.rotation.y=stop.x<0?-Math.PI/2:Math.PI/2;scene.add(g);
  people.push({g,name:pick(['Iara','Miguel','Cris','Valéria']),line:'Estou esperando o circular do bairro.',stationary:true,indoor:false,phase:random()*5,busState:'waiting',busStop:stop});
}

// Cães, gatos e pombos de praça com movimentação própria.
const animals=[];
function pet(x,z,type,color){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  part(g,0,.62,0,.6,.54,1.2,color);part(g,0,.86,.68,.58,.55,.55,color);
  part(g,0,.75,.99,.32,.24,.28,type==='dog'?'#e5c5a6':color);
  part(g,-.17,1.2,.68,.15,.32,.18,color);part(g,.17,1.2,.68,.15,.32,.18,color);
  part(g,-.16,.9,.97,.06,.07,.04,'#35453f');part(g,.16,.9,.97,.06,.07,.04,'#35453f');
  const legs=[];for(const sx of [-1,1])for(const sz of [-1,1]){
    const leg=new THREE.Group();leg.position.set(sx*.2,.48,sz*.4);g.add(leg);part(leg,0,-.24,0,.16,.48,.18,color);legs.push(leg);
  }
  const tail=part(g,0,.93,-.75,.13,.13,.7,color);tail.rotation.x=type==='cat'?.9:.35;
  animals.push({g,type,legs,tail,originX:x,originZ:z,heading:random()*Math.PI*2,speed:type==='cat'?.55:.8,phase:random()*6,turn:0});
}
pet(-44,51,'dog','#b89470');pet(-24,27,'dog','#ddc2a1');pet(97,95,'dog','#987f69');
pet(-52,38,'cat','#b8aaa1');pet(112,101,'cat','#d1aa80');pet(27,56,'cat','#9d9991');
function pigeon(x,z,i){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  const body=part(g,0,.25,0,.34,.3,.48,i%2?'#8e9e9e':'#aab4b1');
  const head=part(g,0,.46,.22,.23,.23,.23,'#7e9292');
  part(g,0,.42,.35,.08,.07,.12,'#d2a477');
  for(const side of [-1,1]){part(g,side*.21,.24,0,.11,.17,.4,'#738c8c');part(g,side*.09,.06,0,.05,.12,.05,'#c78e78');}
  animals.push({g,type:'pigeon',body,head,originX:x,originZ:z,heading:random()*Math.PI*2,speed:.35,phase:random()*6,turn:0});
}
for(let i=0;i<12;i++){
  let x,z;for(let tries=0;tries<50;tries++){x=-55+random()*38;z=18+random()*36;if(!collides(x,z,.35))break;}
  pigeon(x,z,i);
}

// Conclui os blocos estáticos antes da simulação começar.
flushBlocks();

// Partículas quadradas, usadas em arrancadas, aterrissagens e colisões.
const MAX_PARTICLES=180, particlePositions=new Float32Array(MAX_PARTICLES*3), particleState=[];
const particleGeo=new THREE.BufferGeometry();particleGeo.setAttribute('position',new THREE.BufferAttribute(particlePositions,3));
const particleMesh=new THREE.Points(particleGeo,new THREE.PointsMaterial({color:'#ebd5ad',size:.3,transparent:true,opacity:.7,depthWrite:false}));
particleMesh.frustumCulled=false;scene.add(particleMesh);
for(let i=0;i<MAX_PARTICLES;i++)particleState.push({life:0,p:new THREE.Vector3(),v:new THREE.Vector3()});
let nextParticle=0;
function dust(x,y,z,count=4,force=1){
  for(let i=0;i<count;i++){
    const p=particleState[nextParticle++%MAX_PARTICLES];p.life=.35+Math.random()*.45;
    p.p.set(x+(Math.random()-.5)*.8,y,z+(Math.random()-.5)*.8);
    p.v.set((Math.random()-.5)*force,Math.random()*force+1,(Math.random()-.5)*force);
  }
}
function updateParticles(dt){
  particleState.forEach((p,i)=>{
    p.life-=dt;
    if(p.life>0){p.v.y-=2.8*dt;p.p.addScaledVector(p.v,dt);particlePositions.set([p.p.x,p.p.y,p.p.z],i*3);}
    else particlePositions.set([0,-1000,0],i*3);
  });
  particleGeo.attributes.position.needsUpdate=true;
}

function distanceToSegment(px,pz,ax,az,bx,bz){
  const dx=bx-ax,dz=bz-az,den=dx*dx+dz*dz;
  const u=den ? THREE.MathUtils.clamp(((px-ax)*dx+(pz-az)*dz)/den,0,1):0;
  return Math.hypot(px-(ax+u*dx),pz-(az+u*dz));
}
function collides(x,z,r=.45){
  if(x < -210+r || x > 210-r || z > 220-r || z < -178+r) return true;
  if(z < -163 && Math.abs(x)>3-r) return true;
  for(const s of solids){
    const dx=Math.max(Math.abs(x-s.x)-s.w/2,0),dz=Math.max(Math.abs(z-s.z)-s.d/2,0);
    if(dx*dx+dz*dz < r*r) return true;
  }
  for(const p of places){
    const door=p.door, ax=door.x+door.width/2+door.offset, az=door.z;
    const bx=ax-Math.cos(door.angle)*door.width, bz=az+Math.sin(door.angle)*door.width;
    if(distanceToSegment(x,z,ax,az,bx,bz)<r+.12)return true;
  }
  return false;
}
function vehicleBlocks(x,z,r,ignore){
  for(const car of cars){
    if(car.g===ignore)continue;
    const dx=x-car.g.position.x,dz=z-car.g.position.z,a=car.g.rotation.y;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    const halfW=(car.width??(car.kind==='bike'?.7:2.5))/2,halfD=(car.length??(car.kind==='bike'?3:4.6))/2;
    const nx=Math.max(Math.abs(lx)-halfW,0),nz=Math.max(Math.abs(lz-(car.centerOffset||0))-halfD,0);
    if(nx*nx+nz*nz<r*r)return true;
  }
  return false;
}
function vehicleBoundsOverlap(a,ax,az,b,bx,bz){
  const aw=a.width??(a.kind==='bike'?.7:2.5),ad=a.length??(a.kind==='bike'?3:4.6),bw=b.width??(b.kind==='bike'?.7:2.5),bd=b.length??(b.kind==='bike'?3:4.6);
  const ar=a.g.rotation.y,br=b.g.rotation.y;
  const axes=[[Math.cos(ar),-Math.sin(ar)],[Math.sin(ar),Math.cos(ar)],[Math.cos(br),-Math.sin(br)],[Math.sin(br),Math.cos(br)]];
  const au=axes[0],av=axes[1],bu=axes[2],bv=axes[3];
  const dx=bx+Math.sin(br)*(b.centerOffset||0)-ax-Math.sin(ar)*(a.centerOffset||0);
  const dz=bz+Math.cos(br)*(b.centerOffset||0)-az-Math.cos(ar)*(a.centerOffset||0);
  return axes.every(([x,z])=>{
    const distance=Math.abs(dx*x+dz*z);
    const ra=aw*.5*Math.abs(au[0]*x+au[1]*z)+ad*.5*Math.abs(av[0]*x+av[1]*z);
    const rb=bw*.5*Math.abs(bu[0]*x+bu[1]*z)+bd*.5*Math.abs(bv[0]*x+bv[1]*z);
    return distance<ra+rb+.08;
  });
}
function vehicleFootprintBlocked(car,x,z){
  const w=car.width??(car.kind==='bike'?.7:2.5),d=car.length??(car.kind==='bike'?3:4.6),a=car.g.rotation.y;
  for(let ix=-2;ix<=2;ix++)for(let iz=-4;iz<=4;iz++){
    const lx=ix*w/4,lz=(car.centerOffset||0)+iz*d/8;
    const px=x+Math.cos(a)*lx+Math.sin(a)*lz,pz=z-Math.sin(a)*lx+Math.cos(a)*lz;
    if(collides(px,pz,.08))return true;
  }
  return cars.some(other=>other!==car&&vehicleBoundsOverlap(car,x,z,other,other.g.position.x,other.g.position.z));
}
function personBlocks(x,z,ignore){
  if(ignore!==player)return false;
  return people.some(person=>!person.ragdoll&&!person.inBus&&Math.hypot(x-person.g.position.x,z-person.g.position.z)<.8)||animals.some(a=>Math.hypot(x-a.g.position.x,z-a.g.position.z)<(a.type==='pigeon'?.5:.7));
}
function moveWithCollision(g,dx,dz,r){
  const distance=Math.hypot(dx,dz), steps=Math.max(1,Math.ceil(distance/.25));let hitX=false,hitZ=false;
  const movingVehicle=cars.find(car=>car.g===g);
  const blocked=(x,z)=>movingVehicle?vehicleFootprintBlocked(movingVehicle,x,z):collides(x,z,r)||vehicleBlocks(x,z,r,g)||personBlocks(x,z,g);
  for(let i=0;i<steps;i++){
    if(!blocked(g.position.x+dx/steps,g.position.z))g.position.x+=dx/steps;else hitX=true;
    if(!blocked(g.position.x,g.position.z+dz/steps))g.position.z+=dz/steps;else hitZ=true;
  }
  return {hitX,hitZ};
}

// Algumas lixeiras, bancos e canteiros se partem em pedaços com gravidade.
const debris=[];
function breakProp(prop,speed,angle){
  if(!prop.active)return;
  prop.active=false;scene.remove(prop.group);
  const index=solids.indexOf(prop.collider);if(index>=0)solids.splice(index,1);
  const palette=prop.type==='bin'?['#568375','#355d54','#c9d4bc']:prop.type==='bench'?['#b47b4f','#bd8758','#465f5a']:['#cd9c72','#688b61','#e9a185'];
  for(let i=0;i<6;i++){
    const size=.2+Math.random()*.35;
    const mesh=part(scene,prop.collider.x+(Math.random()-.5)*.6,groundHeight(prop.collider.x,prop.collider.z)+.4+Math.random(),prop.collider.z+(Math.random()-.5)*.5,size,size,size,palette[i%palette.length]);
    const velocity=new THREE.Vector3(Math.sin(angle)*speed*.32+(Math.random()-.5)*2,2+Math.random()*3,Math.cos(angle)*speed*.32+(Math.random()-.5)*2);
    debris.push({mesh,velocity,size,age:0,spin:(Math.random()-.5)*7});
  }
  dust(prop.collider.x,groundHeight(prop.collider.x,prop.collider.z)+.3,prop.collider.z,10,2.5);
}
function breakAhead(car,x,z){
  if(Math.abs(car.velocity)<5)return;
  const a=car.g.rotation.y;
  for(const prop of destructibles){
    if(!prop.active||prop.bench?.occupied)continue;
    const dx=prop.collider.x-x,dz=prop.collider.z-z;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    const reach=Math.max(prop.collider.w,prop.collider.d)*.5;
    if(Math.abs(lx)<(car.width??(car.kind==='bike'?.7:2.5))/2+reach&&Math.abs(lz-(car.centerOffset||0))<(car.length??(car.kind==='bike'?3:4.6))/2+reach)breakProp(prop,Math.abs(car.velocity),a);
  }
}
function updateDebris(dt){
  for(let i=debris.length-1;i>=0;i--){
    const d=debris[i];d.age+=dt;d.velocity.y-=14*dt;
    const nx=d.mesh.position.x+d.velocity.x*dt,nz=d.mesh.position.z+d.velocity.z*dt;
    if(!collides(nx,nz,d.size*.35)){d.mesh.position.x=nx;d.mesh.position.z=nz;}else{d.velocity.x*=-.2;d.velocity.z*=-.2;}
    d.mesh.position.y+=d.velocity.y*dt;d.mesh.rotation.x+=d.spin*dt;d.mesh.rotation.z+=d.spin*.7*dt;
    const floor=groundHeight(d.mesh.position.x,d.mesh.position.z)+d.size*.5;
    if(d.mesh.position.y<floor){d.mesh.position.y=floor;d.velocity.y=Math.abs(d.velocity.y)*.25;d.velocity.x*=.8;d.velocity.z*=.8;d.spin*=.8;}
    if(d.age>18){scene.remove(d.mesh);debris.splice(i,1);}
  }
}

// Ragdoll por partículas e restrições de distância (Verlet), com chão e paredes.
const ragdolls=[];
const RAG_STEP=1/90;
const upVector=new THREE.Vector3(0,1,0);
function createRagdoll(actor,impulse=new THREE.Vector3()){
  if(actor.ragdoll||!actor.g.visible)return;
  actor.ragdoll=true;actor.g.visible=false;if(actor.isPlayer){petting=null;actor.g.rotation.x=0;}
  const g=actor.g, p=g.position, angle=g.rotation.y;
  const offsets=[
    [0,.95,0],[0,1.58,0],[0,2.13,0],[-.48,1.35,0],[-.65,.95,0],[.48,1.35,0],[.65,.95,0],[-.2,.55,0],[-.2,.12,.04],[.2,.55,0],[.2,.12,.04]
  ];
  const joints=offsets.map(([x,y,z])=>{
    const v=new THREE.Vector3(x,y,z).applyAxisAngle(upVector,angle).add(p);
    const previous=v.clone().addScaledVector(impulse,-RAG_STEP);
    return {v,previous};
  });
  const links=[[0,1,.63],[1,2,.55],[1,3,.53],[3,4,.44],[1,5,.53],[5,6,.44],[0,7,.45],[7,8,.45],[0,9,.45],[9,10,.45],[3,5,.96],[7,9,.4]];
  const group=new THREE.Group();scene.add(group);
  const colors=g.userData;
  const segments=[
    [0,1,.7,.44,colors.shirt],[1,3,.25,.25,colors.shirt],[3,4,.22,.22,colors.skin],
    [1,5,.25,.25,colors.shirt],[5,6,.22,.22,colors.skin],[0,7,.28,.3,colors.trousers],
    [7,8,.26,.28,colors.trousers],[0,9,.28,.3,colors.trousers],[9,10,.26,.28,colors.trousers]
  ].map(([a,b,w,d,color])=>({a,b,w,d,mesh:part(group,0,0,0,w,1,d,color)}));
  const head=new THREE.Group();group.add(head);
  part(head,0,0,0,.56,.58,.5,colors.skin);part(head,0,.31,-.03,.58,.16,.52,colors.hair);
  part(head,-.16,.04,.255,.075,.075,.035,'#3e4d49');part(head,.16,.04,.255,.075,.075,.035,'#3e4d49');
  ragdolls.push({actor,joints,links,group,segments,head,age:0,accumulator:0,start:p.clone()});
  dust(p.x,.2,p.z,10,2.5);
}
function updateRagdolls(dt){
  for(let k=ragdolls.length-1;k>=0;k--){
    const r=ragdolls[k];r.age+=dt;r.accumulator=Math.min(r.accumulator+dt,RAG_STEP*5);
    while(r.accumulator>=RAG_STEP){
      r.accumulator-=RAG_STEP;
      for(const j of r.joints){
        const v=j.v.clone().sub(j.previous).multiplyScalar(.988);
        j.previous.copy(j.v);j.v.add(v);j.v.y-=17*RAG_STEP*RAG_STEP;
        if(collides(j.v.x,j.v.z,.1)){j.v.x=j.previous.x;j.v.z=j.previous.z;}
        const floor=groundHeight(j.v.x,j.v.z)+.13;
        if(j.v.y<floor){
          const vy=j.v.y-j.previous.y;j.v.y=floor;j.previous.y=floor+vy*.16;
          j.previous.x=j.v.x-(j.v.x-j.previous.x)*.75;j.previous.z=j.v.z-(j.v.z-j.previous.z)*.75;
        }
      }
      for(let iteration=0;iteration<8;iteration++){
        for(const [a,b,len] of r.links){
          const ja=r.joints[a].v,jb=r.joints[b].v,delta=jb.clone().sub(ja),length=delta.length()||.001;
          delta.multiplyScalar((length-len)/length*.5);ja.add(delta);jb.sub(delta);
        }
        for(const joint of r.joints){
          joint.v.y=Math.max(groundHeight(joint.v.x,joint.v.z)+.13,joint.v.y);
          if(collides(joint.v.x,joint.v.z,.1)){joint.v.x=joint.previous.x;joint.v.z=joint.previous.z;}
        }
      }
    }
    for(const segment of r.segments){
      const a=r.joints[segment.a].v,b=r.joints[segment.b].v,dir=b.clone().sub(a);segment.mesh.position.copy(a).add(b).multiplyScalar(.5);
      segment.mesh.scale.set(segment.w,dir.length(),segment.d);segment.mesh.quaternion.setFromUnitVectors(upVector,dir.normalize());
    }
    r.head.position.copy(r.joints[2].v);
    const headDirection=r.joints[2].v.clone().sub(r.joints[1].v).normalize();r.head.quaternion.setFromUnitVectors(upVector,headDirection);
    if(r.age>3.2){
      const hip=r.joints[0].v;let rx=r.start.x,rz=r.start.z;
      for(const [dx,dz] of [[0,0],[.7,0],[-.7,0],[0,.7],[0,-.7],[1.4,0],[-1.4,0]]){
        const x=hip.x+dx,z=hip.z+dz;if(!collides(x,z,.45)&&!vehicleBlocks(x,z,.45,null)){rx=x;rz=z;break;}
      }
      r.actor.g.position.set(rx,groundHeight(rx,rz),rz);if(r.actor.isPlayer){jumpHeight=0;verticalVelocity=0;}
      r.actor.g.visible=true;r.actor.ragdoll=false;
      if(!r.actor.isPlayer&&!r.actor.indoor&&!r.actor.stationary)r.actor.origin=r.actor.g.position[r.actor.axis];
      scene.remove(r.group);ragdolls.splice(k,1);
    }
  }
}

const keys=Object.create(null), playerActor={g:player,isPlayer:true,ragdoll:false};
let vehicle=null, petting=null, sitting=null, velocity=new THREE.Vector3(), verticalVelocity=0, jumpHeight=0, yaw=.65, pitch=.36, zoom=18;
let targetYaw=yaw,targetPitch=pitch,targetZoom=zoom,vehicleOrbit=0;const smoothLook=new THREE.Vector3();
let gameTime=0, currentPlace=null, stepTimer=0, cameraReady=false, cameraSafetySnap=false;
const isTouch=window.matchMedia('(pointer: coarse)').matches;
if(isTouch)$('#touch').classList.remove('hidden');

// Uma fala acompanha o morador ou o quadro no próprio mundo, sem pausar o jogo.
const speechCanvas=document.createElement('canvas');speechCanvas.width=1024;speechCanvas.height=384;
const speechContext=speechCanvas.getContext('2d'),speechTexture=new THREE.CanvasTexture(speechCanvas);
speechTexture.colorSpace=THREE.SRGBColorSpace;
const speechSprite=new THREE.Sprite(new THREE.SpriteMaterial({map:speechTexture,transparent:true,depthWrite:false}));
speechSprite.scale.set(6.2,2.325,1);speechSprite.visible=false;scene.add(speechSprite);
let activeSpeech=null;
function clearSpeech(){
  const person=activeSpeech?.target;
  if(person?.g&&activeSpeech.pose){
    person.g.rotation.y=activeSpeech.pose.yaw;
    person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=activeSpeech.pose.limbs[i]);
  }
  activeSpeech=null;speechSprite.visible=false;
}
function showSpeech(target,name,line){
  clearSpeech();
  const pose=target.g?{yaw:target.g.rotation.y,limbs:target.g.userData.limbs?.map(limb=>limb.rotation.x)||[]}:null;
  const ctx=speechContext;ctx.clearRect(0,0,1024,384);
  ctx.fillStyle='#faf4dc';ctx.strokeStyle='#668479';ctx.lineWidth=7;
  ctx.beginPath();ctx.moveTo(12,12);ctx.lineTo(1012,12);ctx.lineTo(1012,320);
  if(!target.display){ctx.lineTo(568,320);ctx.lineTo(512,371);ctx.lineTo(456,320);}
  ctx.lineTo(12,320);ctx.closePath();ctx.fill();ctx.stroke();
  ctx.textAlign='left';ctx.textBaseline='top';ctx.fillStyle='#557a6b';ctx.font='700 39px system-ui, sans-serif';
  if(!target.display)ctx.fillText(name.toUpperCase(),48,34,928);
  ctx.fillStyle='#263d38';ctx.font='500 45px system-ui, sans-serif';
  const lines=[];let current='';
  for(const word of line.split(' ')){
    const next=current?`${current} ${word}`:word;
    if(ctx.measureText(next).width>925&&current){lines.push(current);current=word;}else current=next;
  }
  if(current)lines.push(current);
  lines.forEach((text,i)=>ctx.fillText(text,48,105+i*59,928));
  speechTexture.needsUpdate=true;speechSprite.material.opacity=1;speechSprite.visible=true;
  activeSpeech={target,name,line,pose,until:gameTime+Math.max(5.5,Math.min(9,line.length/11))};
  updateSpeech();
}
function updateSpeech(){
  if(!activeSpeech)return;
  const {target,until}=activeSpeech,person=target.g;
  const x=person?person.position.x:target.x,z=person?person.position.z:target.z;
  if(gameTime>=until||vehicle||playerActor.ragdoll||target.ragdoll||Math.hypot(player.position.x-x,player.position.z-z)>7){clearSpeech();return;}
  if(person)speechSprite.position.set(x,person.position.y+3.8,z);
  else speechSprite.position.set(target.display.x,target.display.y,target.display.z);
  // Mantém as letras legíveis ao variar o zoom, sem deixar o balão ocupar a tela.
  const worldPerPixel=2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2))*camera.position.distanceTo(speechSprite.position)/innerHeight;
  const minWidth=Math.min(350,innerWidth*.85)*worldPerPixel,maxWidth=Math.min(460,innerWidth*.95)*worldPerPixel;
  const width=THREE.MathUtils.clamp(6.2,minWidth,maxWidth),height=width*384/1024;
  speechSprite.scale.set(width,height,1);
  if(person)speechSprite.position.y=person.position.y+2.5+height/2;
  speechSprite.material.opacity=Math.min(1,(until-gameTime)*2);
}
function interactionReachable(x,z){
  const start=player.position,steps=Math.max(1,Math.ceil(Math.hypot(x-start.x,z-start.z)/.12));
  for(let i=1;i<steps;i++){
    const px=start.x+(x-start.x)*i/steps,pz=start.z+(z-start.z)*i/steps;
    if(solids.some(w=>w.opaque&&Math.abs(px-w.x)<w.w/2+.04&&Math.abs(pz-w.z)<w.d/2+.04))return false;
    for(const p of places){
      const d=p.door,ax=d.x+d.width/2+d.offset;
      if(distanceToSegment(px,pz,ax,d.z,ax-Math.cos(d.angle)*d.width,d.z+Math.sin(d.angle)*d.width)<.14)return false;
    }
  }
  return true;
}
function nearestInteraction(){
  // E continues the current conversation even if another passerby comes closer.
  const talking=activeSpeech?.target;
  if(talking?.g&&!talking.ragdoll&&Math.hypot(player.position.x-talking.g.position.x,player.position.z-talking.g.position.z)<2.9&&interactionReachable(talking.g.position.x,talking.g.position.z))return {type:'person',data:talking};
  if(cityBus.atStop){
    const door=busDoorPoint(cityBus);
    if(Math.hypot(player.position.x-door.x,player.position.z-door.z)<2.8&&interactionReachable(door.x,door.z))return {type:'bus',data:cityBus};
  }
  let best=null, bestDistance=3.4;
  for(const person of people){
    if(person.ragdoll||person.inBus)continue;
    const distance=Math.hypot(player.position.x-person.g.position.x,player.position.z-person.g.position.z);
    if(distance<bestDistance&&distance<2.9&&interactionReachable(person.g.position.x,person.g.position.z)){bestDistance=distance;best={type:'person',data:person};}
  }
  if(cityBus.atStop){
    const door=busDoorPoint(cityBus),distance=Math.hypot(player.position.x-door.x,player.position.z-door.z);
    if(distance<bestDistance&&distance<3.4&&interactionReachable(door.x,door.z)){bestDistance=distance;best={type:'bus',data:cityBus};}
  }
  for(const car of cars){
    if(car.auto||Math.abs(car.velocity)>.7)continue;
    const distance=Math.hypot(player.position.x-car.g.position.x,player.position.z-car.g.position.z);
    if(distance<bestDistance&&distance<3.4&&interactionReachable(car.g.position.x,car.g.position.z)){bestDistance=distance;best={type:'car',data:car};}
  }
  for(const animal of animals){
    const distance=Math.hypot(player.position.x-animal.g.position.x,player.position.z-animal.g.position.z);
    if(distance<bestDistance&&distance<2.4&&interactionReachable(animal.g.position.x,animal.g.position.z)){bestDistance=distance;best={type:'animal',data:animal};}
  }
  for(const bench of benches){
    if(bench.occupied||!bench.group.parent)continue;
    const distance=Math.hypot(player.position.x-bench.x,player.position.z-bench.z);
    if(distance<bestDistance&&distance<2.4&&interactionReachable(bench.x,bench.z)){bestDistance=distance;best={type:'bench',data:bench};}
  }
  for(const service of servicePoints){
    const distance=Math.hypot(player.position.x-service.x,player.position.z-service.z);
    if(distance<bestDistance&&distance<2.4&&interactionReachable(service.x,service.z)){bestDistance=distance;best={type:'service',data:service};}
  }
  return best;
}
function busDoorPoint(bus){
  const a=bus.g.rotation.y,p=bus.g.position;
  return {x:p.x+Math.cos(a)*1.6+Math.sin(a)*2.4,z:p.z-Math.sin(a)*1.6+Math.cos(a)*2.4};
}
function busLanding(bus,actor){
  const a=bus.g.rotation.y,p=bus.g.position,rx=Math.cos(a),rz=-Math.sin(a),fx=Math.sin(a),fz=Math.cos(a);
  for(const longitudinal of [2.4,1,-1,3.2]){
    let blocked=false;
    for(let side=bus.width/2+.5;side<=4.7;side+=.2){
      const x=p.x+rx*side+fx*longitudinal,z=p.z+rz*side+fz*longitudinal;
      if(collides(x,z,.42)||vehicleBlocks(x,z,.42,bus.g)||people.some(other=>other!==actor&&!other.ragdoll&&!other.inBus&&Math.hypot(x-other.g.position.x,z-other.g.position.z)<.8)||
        (actor!==playerActor&&!vehicle&&!playerActor.ragdoll&&Math.hypot(x-player.position.x,z-player.position.z)<.8)){blocked=true;break;}
    }
    if(!blocked){const x=p.x+rx*4.7+fx*longitudinal,z=p.z+rz*4.7+fz*longitudinal;return {x,z};}
  }
  return null;
}
function exitBusAtStop(bus){
  if(!bus.atStop)return false;
  const landing=busLanding(bus,playerActor);if(!landing)return false;
  player.position.set(landing.x,groundHeight(landing.x,landing.z),landing.z);player.rotation.y=bus.g.rotation.y;
  player.visible=true;bus.rider.visible=false;bus.boardedStop=null;bus.exitRequested=false;vehicle=null;targetYaw=yaw;
  velocity.set(0,0,0);jumpHeight=0;verticalVelocity=0;return true;
}
function standUp(){
  if(!sitting)return true;
  const b=sitting.bench;
  for(const [dx,dz] of [[0,2.2],[0,-2.2],[2.5,0],[-2.5,0]]){
    const x=b.x+Math.cos(b.rotation)*dx+Math.sin(b.rotation)*dz,z=b.z-Math.sin(b.rotation)*dx+Math.cos(b.rotation)*dz;
    if(!collides(x,z,.42)&&!vehicleBlocks(x,z,.42,null)&&!personBlocks(x,z,player)){
      player.position.set(x,groundHeight(x,z),z);jumpHeight=0;verticalVelocity=0;player.visible=true;scene.remove(sitting.avatar);b.occupied=false;sitting=null;return true;
    }
  }
  return false;
}
function exitVehicle(c){
  const p=c.g.position,angle=c.g.rotation.y;
  const halfWidth=(c.width??(c.kind==='bike'?.7:2.5))/2;
  for(const side of [c.entrySide||1,-(c.entrySide||1)]){
    const distance=halfWidth+.85,x=p.x+Math.cos(angle)*distance*side,z=p.z-Math.sin(angle)*distance*side;
    let blocked=false;
    // Check the complete step outside the cabin, not only the landing point.
    for(let offset=halfWidth+.15;offset<=distance+.001;offset+=.1){
      const sx=p.x+Math.cos(angle)*offset*side,sz=p.z-Math.sin(angle)*offset*side;
      if(collides(sx,sz,.45)||vehicleBlocks(sx,sz,.45,c.g)||personBlocks(sx,sz,player)){blocked=true;break;}
    }
    if(blocked||Math.abs(groundHeight(x,z)-groundHeight(p.x,p.z))>.6)continue;
    player.position.set(x,groundHeight(x,z),z);player.rotation.y=angle;jumpHeight=0;player.visible=true;
    c.velocity=0;c.exitRequested=false;c.spots.forEach(l=>l.visible=false);if(c.rider)c.rider.visible=false;if(c.driver)c.driver.visible=false;
    if(c.reverse)c.reverse.emissiveIntensity=0;c.signal=null;vehicle=null;targetYaw=yaw;velocity.set(0,0,0);return true;
  }
  c.exitRequested=false;return false;
}
function interact(){
  if(sitting){standUp();return;}
  if(playerActor.ragdoll)return;
  if(vehicle){
    if(vehicle.kind==='bus'){if(vehicle.atStop)exitBusAtStop(vehicle);else vehicle.exitRequested=true;return;}
    if(Math.abs(vehicle.velocity)>1){vehicle.exitRequested=true;return;}
    exitVehicle(vehicle);return;
  }
  const candidate=nearestInteraction();if(!candidate)return;
  if(candidate.type==='bench'){
    clearSpeech();
    const bench=candidate.data,avatar=seatedHuman(player.userData.shirt,player.userData.skin);
    avatar.position.set(bench.x,groundHeight(bench.x,bench.z),bench.z);avatar.rotation.y=bench.rotation;scene.add(avatar);
    bench.occupied=true;sitting={bench,avatar};player.position.set(bench.x,groundHeight(bench.x,bench.z),bench.z);player.visible=false;velocity.set(0,0,0);jumpHeight=0;verticalVelocity=0;
  }else if(candidate.type==='animal'){
    clearSpeech();
    petting={animal:candidate.data,time:1.8};velocity.set(0,0,0);
  }else if(candidate.type==='service'){
    const point=candidate.data;
    if(point.kind==='fire-water')point.active=!point.active;
    else{
      showSpeech(point,point.name,'A equipe atende na recepção e faz a ronda pelas ruas da cidade.');
    }
  }else if(candidate.type==='person'){
    const person=candidate.data,lines=person.lines?.length?person.lines:[person.line,'Bom passeio por Baía do Sol!'];
    showSpeech(person,person.name,lines[(person.talkCount||0)%lines.length]);person.talkCount=(person.talkCount||0)+1;
  }else if(candidate.type==='bus'){
    clearSpeech();petting=null;player.rotation.x=0;player.userData.limbs[2].rotation.x=0;
    vehicle=candidate.data;vehicle.boardedStop=vehicle.atStop.id;vehicle.exitRequested=true;vehicle.rider.visible=true;
    player.visible=false;velocity.set(0,0,0);verticalVelocity=0;jumpHeight=0;cameraSafetySnap=true;vehicleOrbit=0;
  }else{
    clearSpeech();
    petting=null;player.rotation.x=0;player.userData.limbs[2].rotation.x=0;
    vehicle=candidate.data;vehicle.entrySide=Math.sign((player.position.x-vehicle.g.position.x)*Math.cos(vehicle.g.rotation.y)-(player.position.z-vehicle.g.position.z)*Math.sin(vehicle.g.rotation.y))||1;vehicle.auto=false;vehicle.velocity=0;vehicleOrbit=0;cameraSafetySnap=true;vehicle.spots.forEach(l=>l.visible=true);if(vehicle.rider)vehicle.rider.visible=true;if(vehicle.driver)vehicle.driver.visible=true;
    player.visible=false;velocity.set(0,0,0);verticalVelocity=0;jumpHeight=0;
  }
}
function jump(){
  if(sitting||petting)return;
  if(vehicle){keys[' ']=true;return;}
  if(!playerActor.ragdoll&&jumpHeight<=.01){verticalVelocity=6.7;dust(player.position.x,groundHeight(player.position.x,player.position.z)+.25,player.position.z,8,1.6);}
}
$('#touchE').addEventListener('click',interact);
$('#touchJump').addEventListener('pointerdown',event=>{event.preventDefault();jump();});
$('#touchJump').addEventListener('pointerup',()=>keys[' ']=false);
$('#touchJump').addEventListener('pointercancel',()=>keys[' ']=false);
addEventListener('keydown',event=>{
  if(['Space','ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(event.code))event.preventDefault();
  keys[event.key.toLowerCase()]=true;
  if(event.code==='KeyE'&&!event.repeat)interact();
  if(vehicle&&vehicle.kind!=='bus'&&!event.repeat&&(event.code==='KeyQ'||event.code==='KeyZ')){const side=event.code==='KeyQ'?'left':'right';vehicle.signal=vehicle.signal===side?null:side;}
  if(vehicle?.emergency&&!event.repeat&&event.code==='KeyL')vehicle.emergency.on=!vehicle.emergency.on;
  if(event.code==='Space'&&!event.repeat)jump();
  if(event.code==='KeyR'&&!event.repeat&&!vehicle&&!sitting&&!playerActor.ragdoll){
    createRagdoll(playerActor,new THREE.Vector3(-Math.sin(yaw)*3,3.8,-Math.cos(yaw)*3));
  }
  if(event.code==='Escape')clearSpeech();
});
addEventListener('keyup',event=>keys[event.key.toLowerCase()]=false);
addEventListener('blur',()=>Object.keys(keys).forEach(key=>keys[key]=false));
let dragging=false;
renderer.domElement.addEventListener('pointerdown',event=>{dragging=true;renderer.domElement.setPointerCapture(event.pointerId);});
renderer.domElement.addEventListener('pointerup',()=>dragging=false);
renderer.domElement.addEventListener('pointercancel',()=>dragging=false);
renderer.domElement.addEventListener('pointermove',event=>{if(dragging){if(vehicle)vehicleOrbit-=event.movementX*.006;else targetYaw-=event.movementX*.006;targetPitch=THREE.MathUtils.clamp(targetPitch+event.movementY*.004,.08,1.12);}});
addEventListener('wheel',event=>targetZoom=THREE.MathUtils.clamp(targetZoom+event.deltaY*.012,6,25),{passive:true});
for(const b of document.querySelectorAll('[data-key]')){
  b.addEventListener('pointerdown',event=>{event.preventDefault();keys[b.dataset.key]=true;b.setPointerCapture(event.pointerId);});
  for(const type of ['pointerup','pointercancel','lostpointercapture'])b.addEventListener(type,()=>keys[b.dataset.key]=false);
}
function updateDoors(dt){
  for(const p of places){
    const door=p.door;
    if(door.automatic){
      const nearby=!vehicle&&Math.hypot(player.position.x-door.x,player.position.z-door.z)<3.5;
      const pedestrian=people.some(person=>!person.ragdoll&&!person.inBus&&Math.hypot(person.g.position.x-door.x,person.g.position.z-door.z)<2.5);
      door.open=nearby||pedestrian;
      door.offset=THREE.MathUtils.damp(door.offset,door.open?door.width*1.08:0,6,dt);
      door.hinge.position.x=door.x+door.width/2+door.offset;
    }else{
      door.hold=Math.max(0,door.hold-dt);
      const returnForce=door.hold>0?0:-door.angle*2.2;
      door.angularVelocity=(door.angularVelocity+returnForce*dt)*Math.exp(-2.6*dt);
      const proposed=THREE.MathUtils.clamp(door.angle+door.angularVelocity*dt,-1.5,1.5);
      const hingeX=door.x+door.width/2,endX=hingeX-Math.cos(proposed)*door.width,endZ=door.z+Math.sin(proposed)*door.width;
      const closing=Math.abs(proposed)<Math.abs(door.angle);
      const personInDoor=people.some(person=>!person.ragdoll&&!person.inBus&&distanceToSegment(person.g.position.x,person.g.position.z,hingeX,door.z,endX,endZ)<.55);
      if(closing&&((!vehicle&&distanceToSegment(player.position.x,player.position.z,hingeX,door.z,endX,endZ)<.55)||personInDoor))door.angularVelocity=0;
      else door.angle=proposed;
      door.hinge.rotation.y=door.angle;door.open=Math.abs(door.angle)>.2;
    }
  }
}
function pushDoors(x,z,dx,dz,actor=player){
  for(const p of places){
    const d=p.door;if(d.automatic)continue;
    const ax=d.x+d.width/2,bx=ax-Math.cos(d.angle)*d.width,bz=d.z+Math.sin(d.angle)*d.width;
    if(distanceToSegment(x,z,ax,d.z,bx,bz)>.65)continue;
    const side=actor.position.z>=d.z?-1:1;
    if(dz*side<=0)continue;
    d.angularVelocity=THREE.MathUtils.clamp(d.angularVelocity+side*Math.abs(dz)*20,-5,5);d.hold=1.2;
  }
}
function updatePlayer(dt){
  if(playerActor.ragdoll)return;
  if(sitting){if(keys.w||keys.a||keys.s||keys.d||keys.arrowup||keys.arrowdown||keys.arrowleft||keys.arrowright)standUp();return;}
  if(petting&&(keys.w||keys.a||keys.s||keys.d||keys.arrowup||keys.arrowdown||keys.arrowleft||keys.arrowright)){petting=null;player.rotation.x=0;player.userData.limbs[2].rotation.x=0;}
  if(petting){
    petting.time-=dt;const animal=petting.animal.g.position;
    player.rotation.y=Math.atan2(animal.x-player.position.x,animal.z-player.position.z);player.rotation.x=.25;
    player.userData.limbs[2].rotation.x=-1.05+Math.sin(gameTime*9)*.2;
    if(petting.time<=0){petting=null;player.rotation.x=0;player.userData.limbs[2].rotation.x=0;}
    return;
  }
  const forward=(keys.w||keys.arrowup?1:0)-(keys.s||keys.arrowdown?1:0);
  const sideways=(keys.d||keys.arrowright?1:0)-(keys.a||keys.arrowleft?1:0);
  if(vehicle){
    const c=vehicle;
    if(c.kind==='bus'){player.position.copy(c.g.position);targetYaw=c.g.rotation.y+Math.PI+vehicleOrbit;return;}
    if(c.exitRequested){c.velocity*=Math.exp(-6*dt);if(Math.abs(c.velocity)<.4){exitVehicle(c);return;}}
    if(!c.exitRequested&&forward!==0)c.velocity+=forward*(forward*c.velocity<0?20:(c.kind==='bike'?8:12))*dt;
    else if(!c.exitRequested)c.velocity*=Math.exp(-1.1*dt);
    if(keys[' '])c.velocity*=Math.exp(-5.5*dt);
    c.velocity=THREE.MathUtils.clamp(c.velocity,c.kind==='bike'?-4:-9,c.kind==='bike'?12:23);
    if(Math.abs(c.velocity)<.04)c.velocity=0;
    c.g.rotation.y+=sideways*1.6*dt*THREE.MathUtils.clamp(Math.abs(c.velocity)/4,0,1)*Math.sign(c.velocity||1);
    const dx=Math.sin(c.g.rotation.y)*c.velocity*dt,dz=Math.cos(c.g.rotation.y)*c.velocity*dt;
    breakAhead(c,c.g.position.x+dx,c.g.position.z+dz);
    const impact=moveWithCollision(c.g,dx,dz,c.kind==='bike'?.55:1.3);
    if((impact.hitX&&Math.abs(dx)>.005)||(impact.hitZ&&Math.abs(dz)>.005)){
      if(Math.abs(c.velocity)>3)dust(c.g.position.x,.3,c.g.position.z,5,1.6);
      c.velocity*=-.1;
    }
    c.braking=!!keys[' ']||!!c.exitRequested||forward*c.velocity<0;
    c.brake.emissiveIntensity=c.braking?1.7:.35;
    if(c.reverse)c.reverse.emissiveIntensity=c.velocity<-.3?1.5:0;
    if(c.wheels)c.wheels.forEach(w=>w.rotation.x+=c.velocity*dt*2);
    c.g.position.y=THREE.MathUtils.damp(c.g.position.y,groundHeight(c.g.position.x,c.g.position.z),14,dt);
    player.position.copy(c.g.position);
    targetYaw=c.g.rotation.y+Math.PI+vehicleOrbit;
    if(Math.abs(c.velocity)>12&&Math.random()<dt*3)dust(c.g.position.x,.25,c.g.position.z,2,.5);
    return;
  }
  const dx=-Math.sin(yaw)*forward+Math.cos(yaw)*sideways;
  const dz=-Math.cos(yaw)*forward-Math.sin(yaw)*sideways;
  const magnitude=Math.hypot(dx,dz),maxSpeed=keys.shift?8.5:5.1;
  const targetX=magnitude?dx/magnitude*maxSpeed:0,targetZ=magnitude?dz/magnitude*maxSpeed:0;
  const acceleration=jumpHeight>.01?4:10;
  velocity.x=THREE.MathUtils.damp(velocity.x,targetX,acceleration,dt);
  velocity.z=THREE.MathUtils.damp(velocity.z,targetZ,acceleration,dt);
  pushDoors(player.position.x+velocity.x*dt,player.position.z+velocity.z*dt,velocity.x*dt,velocity.z*dt);
  const collision=moveWithCollision(player,velocity.x*dt,velocity.z*dt,.42);
  if(collision.hitX)velocity.x=0;if(collision.hitZ)velocity.z=0;
  if(magnitude)player.rotation.y=Math.atan2(dx,dz);
  const walkSpeed=Math.hypot(velocity.x,velocity.z);
  player.userData.limbs.forEach((limb,i)=>limb.rotation.x=walkSpeed>.25?Math.sin(gameTime*walkSpeed*1.6+(i===0||i===3?0:Math.PI))*.45:0);
  player.userData.shoes.forEach((shoe,i)=>shoe.rotation.x=walkSpeed>.25?Math.sin(gameTime*walkSpeed*1.6+i*Math.PI)*.25:0);
  const previousJump=jumpHeight;verticalVelocity-=17*dt;jumpHeight=Math.max(0,jumpHeight+verticalVelocity*dt);
  const floor=groundHeight(player.position.x,player.position.z);player.position.y=floor+jumpHeight;
  if(jumpHeight===0){
    if(previousJump>.05&&verticalVelocity<-2)dust(player.position.x,floor+.2,player.position.z,8,1.7);
    verticalVelocity=0;
    stepTimer-=dt;if(walkSpeed>6&&stepTimer<0){dust(player.position.x,floor+.2,player.position.z,2,.5);stepTimer=.2;}
  }
}
function pedestrianAhead(car){
  const fx=Math.sin(car.g.rotation.y),fz=Math.cos(car.g.rotation.y),rx=Math.cos(car.g.rotation.y),rz=-Math.sin(car.g.rotation.y);
  const brakingDistance=Math.max((car.length??4.6)/2+4,car.velocity*car.velocity/10+(car.length??4.6)/2+2);
  const positions=people.filter(p=>!p.indoor&&!p.ragdoll&&!p.inBus).map(p=>p.g.position);
  if(!vehicle&&!playerActor.ragdoll&&!sitting)positions.push(player.position);
  for(const pos of positions){
    const dx=pos.x-car.g.position.x,dz=pos.z-car.g.position.z,ahead=dx*fx+dz*fz,across=Math.abs(dx*rx+dz*rz);
    if(ahead>-.25&&ahead<brakingDistance&&across<(car.width??2.5)/2+.6)return {distance:ahead};
  }
  return null;
}
function pedestrianOverlaps(car,x,z){
  const candidates=people.filter(p=>!p.indoor&&!p.ragdoll&&!p.inBus).map(p=>p.g.position);
  if(!vehicle&&!playerActor.ragdoll&&!sitting)candidates.push(player.position);
  const a=car.g.rotation.y;
  return candidates.some(pos=>{
    const dx=pos.x-x,dz=pos.z-z,lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    return Math.abs(lx)<(car.width??2.5)/2+.45&&Math.abs(lz-(car.centerOffset||0))<(car.length??4.6)/2+.45;
  });
}
function routedSignalStop(car){
  const p=car.g.position,fx=Math.sin(car.g.rotation.y),fz=Math.cos(car.g.rotation.y),phase=signalPhase(gameTime);
  if(Math.abs(fz)>.9&&[...roads,-130,130].some(x=>Math.abs(p.x-x)<6)){
    for(const z of roads){const ahead=(z-p.z)*Math.sign(fz);if(ahead>(car.length??4.6)/2&&ahead<(car.length??4.6)/2+9&&phase.ns!==2)return true;}
  }
  if(Math.abs(fx)>.9&&roads.some(z=>Math.abs(p.z-z)<6)){
    for(const x of roads){const ahead=(x-p.x)*Math.sign(fx);if(ahead>(car.length??4.6)/2&&ahead<(car.length??4.6)/2+9&&phase.ew!==2)return true;}
  }
  return false;
}
function safeRouteStep(car,position,heading){
  const previous=car.g.rotation.y;car.g.rotation.y=heading;
  const person=pedestrianOverlaps(car,position.x,position.z);
  const occupied=cars.some(other=>other!==car&&vehicleBoundsOverlap(car,position.x,position.z,other,other.g.position.x,other.g.position.z));
  if(person||occupied){car.g.rotation.y=previous;car.velocity=0;car.stoppedReason=person?'pedestre':'veiculo';return false;}
  car.g.position.x=position.x;car.g.position.z=position.z;
  car.g.position.y=THREE.MathUtils.damp(car.g.position.y,groundHeight(position.x,position.z),14,.035);
  return true;
}
function laneClear(car,lane){
  const p=districtTrackPosition(car.track,car.routeProgress+4,lane),fx=p.dx,fz=p.dz,rx=p.dz,rz=-p.dx;
  const blocked=cars.some(other=>{
    if(other===car)return false;const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z;
    return Math.abs(dx*fx+dz*fz)<11&&Math.abs(dx*rx+dz*rz)<3.1;
  });
  return !blocked&&!people.some(person=>!person.ragdoll&&!person.inBus&&Math.hypot(person.g.position.x-p.x,person.g.position.z-p.z)<5);
}
function updateDistrictCar(car,dt){
  const track=car.track,p=car.g.position,heading=car.g.rotation.y,fx=Math.sin(heading),fz=Math.cos(heading),rx=Math.cos(heading),rz=-Math.sin(heading);
  let obstacle=Infinity;
  for(const other of cars){
    if(other===car)continue;const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z,ahead=dx*fx+dz*fz,across=Math.abs(dx*rx+dz*rz);
    if(ahead>0&&ahead<obstacle&&across<2.7&&Math.abs(other.velocity)<car.velocity+2)obstacle=ahead;
  }
  let nearest=null,remaining=Infinity;
  for(const stop of track.stops){
    if(car.stopDone.has(stop.id))continue;
    const d=(stop.progress-car.routeProgress+track.length)%track.length;if(d<remaining){remaining=d;nearest=stop;}
  }
  const branch=districtTrackPosition(track,car.routeProgress,car.lane),canChange=branch.blend>.98&&remaining>16;
  car.avoidTime=Math.max(0,car.avoidTime-dt);
  if(obstacle<16&&canChange&&Math.abs(car.laneTarget-car.lane)<.2&&laneClear(car,-car.laneTarget)){
    car.laneTarget=-car.laneTarget;car.avoidTime=5;car.avoids++;
  }else if(car.avoidTime<=0&&car.laneTarget!==car.homeLane&&canChange&&laneClear(car,car.homeLane))car.laneTarget=car.homeLane;
  let desired=7;car.stoppedReason=null;
  if(nearest&&remaining<12){
    desired=Math.min(desired,Math.sqrt(Math.max(0,remaining)*8));
    if(remaining<.12){
      car.velocity=0;car.stopWait+=dt;car.stoppedReason='pare';desired=0;
      const busy=cars.some(other=>other!==car&&Math.hypot(other.g.position.x-nearest.junctionX,other.g.position.z-nearest.junctionZ)<5&&Math.abs(other.velocity)>.3)||
        people.some(person=>!person.inBus&&!person.ragdoll&&Math.hypot(person.g.position.x-nearest.junctionX,person.g.position.z-nearest.junctionZ)<4);
      if(car.stopWait>=1.25&&!busy){car.stopDone.add(nearest.id);car.stopCount++;car.stopWait=0;nearest=null;remaining=Infinity;desired=7;}
      else if(car.stopWait>=1.25&&busy)car.stoppedReason='preferencia';
    }
  }
  if(routedSignalStop(car)){desired=0;car.stoppedReason='semaforo';}
  const pedestrian=pedestrianAhead(car);if(pedestrian){desired=0;car.stoppedReason='pedestre';}
  if(obstacle<6&&Math.abs(car.laneTarget-car.lane)<.25){desired=0;car.stoppedReason='veiculo';}
  car.velocity=THREE.MathUtils.damp(car.velocity,desired,desired?1.8:5,dt);
  let advance=car.velocity*dt;if(nearest&&remaining<12)advance=Math.min(advance,remaining);
  const next=(car.routeProgress+advance)%track.length,newLane=THREE.MathUtils.damp(car.lane,car.laneTarget,1.5,dt);
  const nextPos=districtTrackPosition(track,next,newLane),nextHeading=Math.atan2(nextPos.dx,nextPos.dz);
  car.signal=Math.abs(car.laneTarget-newLane)>.15?(car.laneTarget>newLane?'right':'left'):null;
  if(safeRouteStep(car,nextPos,nextHeading)){
    if(next<car.routeProgress)car.stopDone.clear();car.routeProgress=next;car.lane=newLane;
  }
  car.braking=desired<1;car.brake.emissiveIntensity=car.braking?1.5:.35;
  car.wheels?.forEach(w=>w.rotation.x+=car.velocity*dt*2);
}
function updateCityBus(bus,dt){
  const track=bus.track;
  if(bus.atStop){
    bus.velocity=0;bus.stoppedReason='ponto';bus.dwell-=dt;
    bus.doors[0].position.z=1.55;bus.doors[1].position.z=3.35;
    if(vehicle===bus&&bus.boardedStop!==bus.atStop.id&&!exitBusAtStop(bus))bus.dwell=Math.max(bus.dwell,.5);
    if(bus.passengers.some(p=>p.boardedStop!==bus.atStop.id))bus.dwell=Math.max(bus.dwell,.5);
    if(bus.dwell<=0&&!people.some(p=>p.busState==='boarding')){bus.lastStop=bus.atStop.id;bus.atStop=null;bus.doors[0].position.z=2;bus.doors[1].position.z=2.9;}
    return;
  }
  let nextStop=null,remaining=Infinity;
  for(const stop of track.stops){
    if(stop.id===bus.lastStop)continue;
    const d=(stop.progress-bus.routeProgress+track.length)%track.length;if(d<remaining){remaining=d;nextStop=stop;}
  }
  let desired=8;bus.stoppedReason=null;
  if(remaining<15)desired=Math.min(desired,Math.sqrt(Math.max(0,remaining)*7));
  if(routedSignalStop(bus)){desired=0;bus.stoppedReason='semaforo';}
  if(pedestrianAhead(bus)){desired=0;bus.stoppedReason='pedestre';}
  bus.velocity=THREE.MathUtils.damp(bus.velocity,desired,desired?1.6:5,dt);
  const advance=Math.min(bus.velocity*dt,remaining),next=(bus.routeProgress+advance)%track.length,pos=trackSample(track,next);
  if(safeRouteStep(bus,pos,Math.atan2(pos.dx,pos.dz)))bus.routeProgress=next;
  if(nextStop&&remaining<.12&&bus.stoppedReason===null){
    bus.velocity=0;bus.atStop=nextStop;bus.dwell=7;bus.stopCount++;bus.brake.emissiveIntensity=1.5;
  }else bus.brake.emissiveIntensity=desired<1?1.5:.35;
  bus.wheels.forEach(w=>w.rotation.x+=bus.velocity*dt*1.6);
}
function animateVehicleLights(car){
  if(!car.indicators)return;
  const lit=Math.floor(gameTime*2.5)%2===0;
  for(const side of ['left','right']){
    const active=car.signal===side&&lit;car.indicators[side].emissiveIntensity=active?1.7:0;
    car.indicators[side].color.set(active?'#ffc66e':'#926c42');
  }
  if(car.emergency){const pulse=Math.floor(gameTime*4)%2;car.emergency.red.emissiveIntensity=car.emergency.on&&pulse===0?2:0;car.emergency.blue.emissiveIntensity=car.emergency.on&&pulse===1?2:0;}
}
function updateTraffic(dt){
  const phase=signalPhase(gameTime);
  for(const c of cars){
    if(!c.auto)continue;
    if(c.kind==='bus'){updateCityBus(c,dt);continue;}
    if(c.track){updateDistrictCar(c,dt);continue;}
    if(c.debugHoldUntil&&gameTime<c.debugHoldUntil){c.velocity=0;c.brake.emissiveIntensity=.35;continue;}
    const current=c.curve?c.g.position.x:c.g.position[c.axis];let stop=false;c.stoppedReason=null;
    for(const crossing of roads){
      const distance=(crossing-current)*c.direction;
      if(distance>4&&distance<12&&phase[c.axis==='z'?'ns':'ew']!==2){stop=true;c.stoppedReason='semaforo';}
    }
    const pedestrian=pedestrianAhead(c);
    if(pedestrian){stop=true;c.stoppedReason='pedestre';if(pedestrian.distance<3.4)c.velocity=0;}
    const desired=stop?0:(c.curve?6:7);
    c.velocity=THREE.MathUtils.damp(c.velocity,desired,stop?5:1.5,dt);
    c.braking=stop;c.brake.emissiveIntensity=stop?1.5:.35;
    if(c.reverse)c.reverse.emissiveIntensity=0;
    if(!c.turning){
      const road=c.curve?0:(c.laneRoad??(c.axis==='z'?Math.round(c.g.position.x/72)*72:Math.round(c.g.position.z/72)*72));
      const end=c.curve?(c.direction>0?196:-196):c.axis==='z'?(c.direction>0?curvedRoadZ(road)-12:-116):(c.direction>0?116:-116);
      c.signal=(end-current)*c.direction<24?'left':null;
      if((end-current)*c.direction<=0)c.turning={start:c.direction>0?0:Math.PI,progress:0,road,end,direction:c.direction};
    }
    let nx=c.g.position.x,nz=c.g.position.z,heading=c.g.rotation.y,nextProgress=null;
    if(c.turning){
      const turn=c.turning,theta=turn.start+turn.progress;
      nextProgress=Math.min(Math.PI,turn.progress+c.velocity/3.3*dt);c.signal='left';
      const t=turn.start+nextProgress;
      if(c.curve){
        const local=nextProgress,dir=turn.direction;nx=turn.end+dir*3.2*Math.sin(local);
        nz=curvedRoadZ(nx)-dir*3.2*Math.cos(local);
        const tx=dir*Math.cos(local),tz=dir*(12/39)*Math.cos(nx/39)*Math.cos(local)+dir*Math.sin(local);
        heading=Math.atan2(tx,tz);
      }else if(c.axis==='z'){
        nx=turn.road+3.3*Math.cos(t);nz=turn.end+3.3*Math.sin(t);heading=Math.atan2(-Math.sin(t),Math.cos(t));
      }else{
        nx=turn.end+3.3*Math.sin(t);nz=turn.road-3.3*Math.cos(t);heading=Math.atan2(Math.cos(t),Math.sin(t));
      }
    }else if(c.curve){
      const slope=(12/39)*Math.cos(current/39),step=c.velocity*dt*c.direction/Math.hypot(1,slope);
      nx=current+step;nz=curvedRoadZ(nx)+(c.direction>0?-3.2:3.2);heading=Math.atan2(c.direction,c.direction*slope);
    }else if(c.axis==='z')nz+=c.velocity*dt*c.direction;
    else nx+=c.velocity*dt*c.direction;
    const previousHeading=c.g.rotation.y;c.g.rotation.y=heading;
    const pedestrianInPath=pedestrianOverlaps(c,nx,nz);
    const occupied=cars.some(other=>other!==c&&vehicleBoundsOverlap(c,nx,nz,other,other.g.position.x,other.g.position.z));
    if(!occupied&&!pedestrianInPath){
      c.g.position.x=nx;c.g.position.z=nz;
      if(c.turning){c.turning.progress=nextProgress;if(nextProgress>=Math.PI){c.direction*=-1;c.turning=null;}}
    }else{c.g.rotation.y=previousHeading;c.velocity=0;c.stoppedReason=pedestrianInPath?'pedestre':'veiculo';}
    c.g.position.y=THREE.MathUtils.damp(c.g.position.y,groundHeight(c.g.position.x,c.g.position.z),14,dt);
    if(c.wheels)c.wheels.forEach(w=>w.rotation.x+=c.velocity*dt*2);
  }
}

function updateBusPassengers(dt){
  const bus=cityBus;
  for(const person of people){
    if(!person.busState||person.ragdoll)continue;
    if(person.inBus){
      if(!bus.atStop||bus.atStop.id===person.boardedStop)continue;
      const landing=busLanding(bus,person);if(!landing)continue;
      person.g.position.set(landing.x,groundHeight(landing.x,landing.z),landing.z);person.inBus=false;person.g.visible=true;
      person.busAvatar?.removeFromParent();person.busAvatar=null;bus.passengers=bus.passengers.filter(p=>p!==person);
      person.busStop=busStops.find(stop=>stop.id===bus.atStop.id);person.busState='settling';person.waitSlot=-2.2;
      continue;
    }
    if(activeSpeech?.target===person)continue;
    if(person.busState==='waiting'){
      if(bus.atStop?.id===person.busStop.id&&bus.dwell>3&&bus.passengers.length<3&&!people.some(p=>p!==person&&p.busState==='boarding')){
        person.busState='boarding';person.boardingTime=0;
      }else continue;
    }
    let tx,tz;
    if(person.busState==='boarding'){
      person.boardingTime+=dt;if(!bus.atStop||person.boardingTime>6){person.busState='waiting';continue;}
      const a=bus.g.rotation.y,p=bus.g.position;
      tx=p.x+Math.cos(a)*2.2+Math.sin(a)*2.4;tz=p.z-Math.sin(a)*2.2+Math.cos(a)*2.4;
    }else{
      tx=person.busStop.x+(person.busStop.x<0?-1.2:1.2);tz=person.busStop.z+(person.waitSlot??2.2);
    }
    const dx=tx-person.g.position.x,dz=tz-person.g.position.z,distance=Math.hypot(dx,dz);
    if(distance<.25){
      if(person.busState==='boarding'){
        person.inBus=true;person.g.visible=false;person.boardedStop=bus.atStop.id;person.busState='riding';
        const seats=[[-.65,-1.4],[.65,-2.4],[-.65,-2.8]],seat=seats[bus.passengers.length];
        person.busAvatar=makeDriver(person.g.userData.shirt,person.g.userData.skin);person.busAvatar.position.set(seat[0],.1,seat[1]);bus.g.add(person.busAvatar);bus.passengers.push(person);
      }else{person.busState='waiting';person.g.userData.limbs?.forEach(limb=>limb.rotation.x=0);}
      continue;
    }
    const step=Math.min(distance,1.7*dt),nx=person.g.position.x+dx/distance*step,nz=person.g.position.z+dz/distance*step;
    const other=people.some(p=>p!==person&&!p.inBus&&!p.ragdoll&&Math.hypot(nx-p.g.position.x,nz-p.g.position.z)<.85);
    const playerNear=!vehicle&&!playerActor.ragdoll&&Math.hypot(nx-player.position.x,nz-player.position.z)<.85;
    if(!collides(nx,nz,.35)&&!vehicleBlocks(nx,nz,.35,bus.g)&&!other&&!playerNear){
      person.g.position.set(nx,groundHeight(nx,nz),nz);person.g.rotation.y=Math.atan2(dx,dz);
      person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=Math.sin(gameTime*7+(i===0||i===3?0:Math.PI))*.35);
    }
  }
}

function impactActors(){
  for(const c of cars){
    const speed=c.auto?c.velocity:Math.abs(c.velocity);
    if(speed<2.8)continue;
    const angle=c.g.rotation.y,impulse=new THREE.Vector3(Math.sin(angle)*speed*.8,4,Math.cos(angle)*speed*.8);
    const hits=(position)=>{
      const dx=position.x-c.g.position.x,dz=position.z-c.g.position.z,a=c.g.rotation.y;
      const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
      return Math.abs(lx)<(c.width??(c.kind==='bike'?.7:2.5))/2+.3&&Math.abs(lz-(c.centerOffset||0))<(c.length??(c.kind==='bike'?3:4.6))/2+.3;
    };
    if(!vehicle&&!playerActor.ragdoll&&hits(player.position)){
      if(c.auto){c.velocity=0;c.stoppedReason='pedestre';}else{createRagdoll(playerActor,impulse);velocity.set(0,0,0);c.velocity*=.5;}
    }
    for(const person of people){
      if(person.indoor||person.sitting||person.ragdoll||person.inBus)continue;
      if(hits(person.g.position)){if(c.auto){c.velocity=0;c.stoppedReason='pedestre';}else{createRagdoll(person,impulse);c.velocity*=.6;}}
    }
  }
}

// Rede de passeios: esquinas conectadas por calçadas e faixas, mais os
// passeios das ruas sinuosas. Cada aresta de travessia tem sua própria regra.
function buildSidewalkGraph(){
  const nodes=[],lookup=new Map(),columns=[-130,-72,0,72,130],rows=[-131,-72,0,72],offset=8.2;
  function node(x,z){
    const key=`${x.toFixed(2)},${z.toFixed(2)}`;if(lookup.has(key))return lookup.get(key);
    const n={id:nodes.length,x,z,edges:[]};nodes.push(n);lookup.set(key,n);return n;
  }
  function edge(a,b,crossing=null){
    if(a===b||a.edges.some(e=>e.to===b))return;
    const cost=Math.hypot(a.x-b.x,a.z-b.z)+(crossing?4:0);
    a.edges.push({to:b,cost,crossing});b.edges.push({to:a,cost,crossing});
  }
  function chain(a,b){
    const count=Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/12);let previous=a;
    for(let i=1;i<count;i++){const n=node(a.x+(b.x-a.x)*i/count,a.z+(b.z-a.z)*i/count);edge(previous,n);previous=n;}edge(previous,b);
  }
  for(const x of columns)for(const side of [-1,1]){
    const px=x+side*offset;
    for(const z of rows)edge(node(px,z-offset),node(px,z+offset),'ew');
    for(let i=1;i<rows.length;i++)chain(node(px,rows[i-1]+offset),node(px,rows[i]-offset));
    const last=node(px,rows[rows.length-1]+offset),end=node(px,Math.min(124,curvedRoadZ(px)-11));chain(last,end);
  }
  for(const z of rows)for(const side of [-1,1]){
    const pz=z+side*offset;
    for(const x of columns)edge(node(x-offset,pz),node(x+offset,pz),'ns');
    for(let i=1;i<columns.length;i++)chain(node(columns[i-1]+offset,pz),node(columns[i]-offset,pz));
  }
  const gridNodes=[...nodes];
  for(const road of districtRoads.filter(r=>r.oneWay)){
    const sides=[[],[]];
    for(let i=2;i<=18;i++){
      const t=i/20,p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t);
      for(let s=0;s<2;s++){
        const side=s?1:-1,n=node(p.x-v.z*side*7,p.z+v.x*side*7);sides[s].push(n);
        if(sides[s].length>1)edge(sides[s][sides[s].length-2],n,segmentOnRoad(sides[s][sides[s].length-2],n)?'district':null);
      }
      if(i===6||i===14)edge(sides[0][sides[0].length-1],sides[1][sides[1].length-1],'district');
    }
    for(const side of sides)for(const n of [side[0],side[side.length-1]]){
      const nearest=gridNodes.filter(g=>Math.abs(Math.abs(g.x)-130)<13).sort((a,b)=>Math.hypot(a.x-n.x,a.z-n.z)-Math.hypot(b.x-n.x,b.z-n.z))[0];
      if(nearest)edge(n,nearest,segmentOnRoad(n,nearest)?'district':null);
    }
  }
  return nodes;
}
function pointOnRoad(x,z){return isPaved(x,z)&&groundHeight(x,z)<.15;}
function segmentOnRoad(a,b){
  const count=Math.max(1,Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/1.4));
  for(let i=1;i<count;i++)if(pointOnRoad(a.x+(b.x-a.x)*i/count,a.z+(b.z-a.z)*i/count))return true;
  return false;
}
function clearWalkSegment(a,b,allowRoad=false){
  const count=Math.max(1,Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/1.2));
  for(let i=1;i<=count;i++){
    const x=a.x+(b.x-a.x)*i/count,z=a.z+(b.z-a.z)*i/count;
    if(collides(x,z,.35)||(!allowRoad&&pointOnRoad(x,z)))return false;
  }
  return true;
}
const sidewalkGraph=buildSidewalkGraph();
function nearestWalkNode(point){
  const candidates=sidewalkGraph.map(n=>({n,d:Math.hypot(n.x-point.x,n.z-point.z)})).filter(v=>v.d<65).sort((a,b)=>a.d-b.d);
  for(const {n} of candidates.slice(0,24))if(clearWalkSegment(point,n))return n;
  return null;
}
function findWalkPath(start,end){
  if(start===end)return [];
  const scores=new Map([[start,0]]),previous=new Map(),open=new Set([start]);
  while(open.size){
    let current=null,best=Infinity;
    for(const n of open){const score=scores.get(n)+Math.hypot(n.x-end.x,n.z-end.z);if(score<best){best=score;current=n;}}
    if(current===end){
      const result=[];while(previous.has(current)){const step=previous.get(current);result.push({x:current.x,z:current.z,crossing:step.crossing});current=step.from;}return result.reverse();
    }
    open.delete(current);
    for(const e of current.edges){
      if(!clearWalkSegment(current,e.to,!!e.crossing))continue;
      const score=scores.get(current)+e.cost;if(score>=(scores.get(e.to)??Infinity))continue;
      scores.set(e.to,score);previous.set(e.to,{from:current,crossing:e.crossing});open.add(e.to);
    }
  }
  return null;
}
function placeContaining(x,z){return places.find(p=>Math.abs(x-p.x)<p.w/2-.3&&Math.abs(z-p.z)<p.d/2-.3)||null;}
function planPersonRoute(person,target){
  const p=person.g.position,startPlace=placeContaining(p.x,p.z),goalPlace=placeContaining(target.x,target.z),steps=[];
  if(startPlace===goalPlace&&startPlace&&clearWalkSegment(p,target))return [{x:target.x,z:target.z}];
  const outside=place=>({x:place.door.x,z:place.door.z+1.8}),inside=place=>({x:place.door.x,z:place.door.z-1.25});
  const start=startPlace?outside(startPlace):{x:p.x,z:p.z},end=goalPlace?outside(goalPlace):{x:target.x,z:target.z};
  if(startPlace){steps.push({...inside(startPlace),door:true},{...start,door:true});}
  if(Math.hypot(start.x-end.x,start.z-end.z)<12&&clearWalkSegment(start,end))steps.push({...end});
  else{
    const a=nearestWalkNode(start),b=nearestWalkNode(end);if(!a||!b)return null;
    const path=findWalkPath(a,b);if(!path)return null;
    steps.push({x:a.x,z:a.z},...path,{...end});
  }
  if(goalPlace)steps.push({...inside(goalPlace),door:true},{x:target.x,z:target.z,door:true});
  return steps;
}
function moveResident(person,target,dt){
  const key=`${target.id}:${target.slot}`;
  if(!person.nav||person.nav.key!==key){person.nav={key,path:planPersonRoute(person,target),index:0,stuck:0,crossing:false};}
  const nav=person.nav;if(!nav.path)return false;
  const step=nav.path[nav.index];if(!step)return true;
  const pos=person.g.position,dx=step.x-pos.x,dz=step.z-pos.z,dist=Math.hypot(dx,dz);
  if(dist<.24){nav.index++;nav.crossing=false;nav.stuck=0;if(nav.index>=nav.path.length){person.g.userData.limbs?.forEach(l=>l.rotation.x=0);return true;}return false;}
  if(step.crossing&&!nav.crossing){
    const phase=signalPhase(gameTime);
    if((step.crossing==='ns'||step.crossing==='ew')&&phase[step.crossing]!==0)return false;
    const mx=(pos.x+step.x)/2,mz=(pos.z+step.z)/2;
    if(cars.some(c=>Math.abs(c.velocity)>.5&&Math.hypot(c.g.position.x-mx,c.g.position.z-mz)<13))return false;
    nav.crossing=true;
  }
  const speed=person.child?1.2:1.15+(person.phase%3)*.1,amount=Math.min(dist,speed*dt),ux=dx/dist,uz=dz/dist;
  const attempts=[[ux,uz],[ux-uz*.65,uz+ux*.65],[ux+uz*.65,uz-ux*.65]];let moved=false;
  for(const [vx,vz] of attempts){
    const n=Math.hypot(vx,vz),mx=vx/n*amount,mz=vz/n*amount,x=pos.x+mx,z=pos.z+mz;
    if(step.door)pushDoors(x,z,mx,mz,person.g);
    const crowd=people.some(p=>p!==person&&!p.inBus&&!p.ragdoll&&Math.hypot(p.g.position.x-x,p.g.position.z-z)<.95);
    const playerNear=!vehicle&&!playerActor.ragdoll&&Math.hypot(player.position.x-x,player.position.z-z)<.95;
    if(collides(x,z,.34)||vehicleBlocks(x,z,.4,null)||crowd||playerNear||(!step.crossing&&!step.door&&pointOnRoad(x,z)))continue;
    pos.set(x,groundHeight(x,z),z);person.g.rotation.y=Math.atan2(vx,vz);person.indoor=!!placeContaining(x,z);moved=true;break;
  }
  if(moved){
    nav.stuck=0;person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=Math.sin(gameTime*6+(i===0||i===3?0:Math.PI))*.38);
  }else{
    nav.stuck+=dt;person.g.userData.limbs?.forEach(limb=>limb.rotation.x=0);
    if(nav.stuck>5&&!step.door){person.nav=null;}
  }
  return false;
}

function updatePeople(dt){
  for(const p of people){
    if(p.ragdoll)continue;
    if(activeSpeech?.target===p&&Math.hypot(player.position.x-p.g.position.x,player.position.z-p.g.position.z)<3.5){
      if(!p.sitting){
        const facing=Math.atan2(player.position.x-p.g.position.x,player.position.z-p.g.position.z);
        const delta=Math.atan2(Math.sin(facing-p.g.rotation.y),Math.cos(facing-p.g.rotation.y));
        p.g.rotation.y+=delta*(1-Math.exp(-dt*7));
        p.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=i===2?-.4+Math.sin(gameTime*3)*.1:0);
      }
      continue;
    }
    if(p.busState)continue;
    if(npcLife?.managed.has(p))continue;
    if(p.stationary){
      if(p.chatting&&p.g.userData.limbs)p.g.userData.limbs[2].rotation.x=-.35+Math.sin(gameTime*2+p.phase)*.2;
      continue;
    }
    if(p.indoor)continue;
    p.wait=Math.max(0,p.wait-dt);
    const next=p.g.position[p.axis]+p.speed*p.dir*dt;
    const nx=p.axis==='x'?next:p.g.position.x,nz=p.curved?curvedRoadZ(nx)+p.side*8.6:(p.axis==='z'?next:p.g.position.z);
    const nearPerson=people.some(other=>other!==p&&!other.ragdoll&&!other.inBus&&Math.hypot(nx-other.g.position.x,nz-other.g.position.z)<.9);
    const nearPlayer=!vehicle&&!playerActor.ragdoll&&Math.hypot(nx-player.position.x,nz-player.position.z)<.9;
    const nearAnimal=animals.some(a=>Math.hypot(nx-a.g.position.x,nz-a.g.position.z)<.7);
    const blocked=collides(nx,nz,.36)||vehicleBlocks(nx,nz,.4,null)||nearPerson||nearPlayer||nearAnimal;
    if(Math.abs(next-p.origin)>p.range||blocked){if(!p.wait){p.dir*=-1;p.wait=.45;}}
    else if(!p.wait){p.g.position[p.axis]=next;if(p.curved)p.g.position.z=nz;}
    p.g.position.y=groundHeight(p.g.position.x,p.g.position.z);
    p.g.rotation.y=p.curved?Math.atan2(p.dir,p.dir*(12/39)*Math.cos(p.g.position.x/39)):p.axis==='z'?(p.dir>0?0:Math.PI):(p.dir>0?Math.PI/2:-Math.PI/2);
    const pace=p.wait?0:Math.sin(gameTime*6+p.phase)*.4;
    p.g.userData.limbs.forEach((limb,i)=>limb.rotation.x=(i===0||i===3?pace:-pace));
  }
}
function updateBoats(){
  fountainDrops.forEach((drop,i)=>{
    const t=(gameTime*.6+drop.phase)%1,angle=i*Math.PI*2/10,radius=.5+t*2.6;
    drop.mesh.position.set(-36+Math.cos(angle)*radius,2.6+t*2.5-t*t*3.8,36+Math.sin(angle)*radius);
  });
  for(const boat of boats){
    boat.g.position.x=boat.baseX+Math.sin(gameTime*.13+boat.phase)*.7;
    boat.g.position.y=.15+Math.sin(gameTime*.8+boat.phase)*.12;
    boat.g.position.z=boat.baseZ+Math.cos(gameTime*.13+boat.phase)*.9;
    boat.g.rotation.y=Math.sin(gameTime*.2+boat.phase)*.1;
    boat.g.rotation.z=Math.sin(gameTime*.8+boat.phase)*.025;
  }
}
function updateIndoorVisibility(){
  const location=vehicle?vehicle.g.position:player.position;
  let next=null;
  for(const p of places){
    const inside=Math.abs(location.x-p.x)<p.w/2-.2 && Math.abs(location.z-p.z)<p.d/2-.2;
    p.upper.visible=!inside;if(inside)next=p;
  }
  const changed=next!==currentPlace;currentPlace=next;return changed;
}
function updateCamera(dt,transition){
  const angleDelta=Math.atan2(Math.sin(targetYaw-yaw),Math.cos(targetYaw-yaw));
  yaw+=angleDelta*(1-Math.exp(-dt*7));pitch=THREE.MathUtils.damp(pitch,targetPitch,7,dt);zoom=THREE.MathUtils.damp(zoom,targetZoom,7,dt);
  let target=vehicle?vehicle.g.position.clone().add(new THREE.Vector3(Math.sin(vehicle.g.rotation.y)*(vehicle.centerOffset||0),0,Math.cos(vehicle.g.rotation.y)*(vehicle.centerOffset||0))):player.position;
  if(playerActor.ragdoll){const r=ragdolls.find(item=>item.actor===playerActor);if(r)target=r.joints[0].v;}
  const focus=target.clone().add(new THREE.Vector3(0,1.5,0));
  if(!cameraReady||transition)smoothLook.copy(focus);else smoothLook.lerp(focus,1-Math.exp(-dt*8));
  const look=smoothLook;
  const distance=currentPlace?Math.min(zoom,8):vehicle?zoom+3:zoom;
  const desired=look.clone().add(new THREE.Vector3(Math.sin(yaw)*Math.cos(pitch)*distance,Math.sin(pitch)*distance,Math.cos(yaw)*Math.cos(pitch)*distance));
  let cameraObstructed=false;
  if(currentPlace){
    const nearEntrance=target.z>currentPlace.z+currentPlace.d/2-1.2;
    desired.x=THREE.MathUtils.clamp(desired.x,currentPlace.x-currentPlace.w/2+.7,currentPlace.x+currentPlace.w/2-.7);
    desired.z=THREE.MathUtils.clamp(desired.z,currentPlace.z-currentPlace.d/2+.7,nearEntrance?target.z+1.1:currentPlace.z+currentPlace.d/2-.7);
    if(nearEntrance&&desired.z>target.z){desired.x=THREE.MathUtils.clamp(desired.x,currentPlace.x-1,currentPlace.x+1);desired.y=Math.min(desired.y,2.8);}
  }else{
    // Mantém a câmera na frente dos edifícios quando uma fachada bloqueia a visão.
    const path=desired.clone().sub(look);
    const nearbyTrees=cameraObstacles.filter(o=>Math.abs(o.x-look.x)<distance+o.w&&Math.abs(o.z-look.z)<distance+o.d);
    for(let i=1;i<=32;i++){
      const s=i/32, point=look.clone().addScaledVector(path,s);
      const canopy=point.x>91.5&&point.x<116.5&&point.z>-41.5&&point.z<-19.5&&point.y>5.8&&point.y<7.2;
      const treeInWay=nearbyTrees.some(o=>Math.abs(point.x-o.x)<o.w/2+.55&&Math.abs(point.z-o.z)<o.d/2+.55&&point.y>o.bottom&&point.y<o.top);
      const obstructed=canopy||treeInWay||sites.some(site=>{
        if(['park','park2','station'].includes(site.type))return false;
        return Math.abs(point.x-site.x)<11.7&&Math.abs(point.z-site.z)<10.7&&point.y<22&&point.y>1;
      });
      if(obstructed){desired.copy(look).addScaledVector(path,Math.max(.12,(i-1)/32));cameraObstructed=true;break;}
    }
  }
  // Ao sair com veículo comprido sob uma marquise, uma lateral livre mostra toda a carroceria.
  const byBuilding=vehicle&&vehicle.length>6&&sites.some(site=>Math.abs(vehicle.g.position.x-site.x)<13&&vehicle.g.position.z>site.z+10.5&&vehicle.g.position.z<site.z+22);
  if(vehicle&&(byBuilding||(cameraObstructed&&desired.distanceTo(look)<(vehicle.length??4.6)/2+2))){
    const angle=vehicle.g.rotation.y,sideDistance=vehicle.length>6?11:7;
    for(const side of [-1,1]){
      const candidate=look.clone().add(new THREE.Vector3(Math.cos(angle)*side*sideDistance,5,-Math.sin(angle)*side*sideDistance));
      const blocked=collides(candidate.x,candidate.z,.5)||cameraObstacles.some(o=>Math.abs(candidate.x-o.x)<o.w/2+.5&&Math.abs(candidate.z-o.z)<o.d/2+.5&&candidate.y>o.bottom&&candidate.y<o.top);
      if(!blocked){desired.copy(candidate);cameraSafetySnap=true;break;}
    }
  }
  if(!cameraReady||transition||cameraSafetySnap||(cameraObstructed&&camera.position.distanceTo(look)>desired.distanceTo(look)+.1)){camera.position.copy(desired);cameraReady=true;cameraSafetySnap=false;}
  else camera.position.lerp(desired,1-Math.exp(-dt*7));
  camera.lookAt(look);
}
// Modelos distantes continuam simulados, mas não precisam ser desenhados.
let visibilityTimer=0;
function updateActorVisibility(dt){
  visibilityTimer-=dt;if(visibilityTimer>0)return;visibilityTimer=.4;
  const pos=vehicle?vehicle.g.position:player.position;
  for(const person of people){if(!person.ragdoll)person.g.visible=!person.inBus&&Math.hypot(person.g.position.x-pos.x,person.g.position.z-pos.z)<100;}
  for(const car of cars)car.g.visible=car===vehicle||Math.hypot(car.g.position.x-pos.x,car.g.position.z-pos.z)<115;
  for(const animal of animals)animal.g.visible=Math.hypot(animal.g.position.x-pos.x,animal.g.position.z-pos.z)<75;
}

// Vizinhança se transforma devagar: céu, sol, lua, luz das ruas e interiores.
const nightSky=new THREE.Color('#1f344a'),daySky=new THREE.Color('#a9dce5'),skyColor=new THREE.Color();
const streetLights=Array.from({length:5},()=>{const light=new THREE.PointLight('#ffe1a4',0,18,1.5);scene.add(light);return light;});
// Um conjunto reutilizável cobre as quatro construções próximas com duas fontes cada.
const shopLights=Array.from({length:8},()=>{const light=new THREE.PointLight('#ffe5bb',0,26,1.45);scene.add(light);return light;});
const canopyLight=new THREE.PointLight('#fff0c4',0,38,1.4);canopyLight.position.set(104,5.5,-30.5);scene.add(canopyLight);
const trafficHeadlights=Array.from({length:4},()=>{
  const light=new THREE.SpotLight('#ffe8b5',0,30,.42,.55,1.4),target=new THREE.Object3D();light.target=target;scene.add(light,target);return {light,target,car:null};
});
let nextLampUpdate=0,litPlaces=[],nightAmount=0;
function updateDayNight(time,dt){
  const cycle=time/240*Math.PI*2;
  const sunHeight=Math.cos(cycle);
  const daylight=THREE.MathUtils.smoothstep(sunHeight,-.22,.38);
  skyColor.copy(nightSky).lerp(daySky,daylight);scene.background.copy(skyColor);scene.fog.color.copy(skyColor);
  ambient.intensity=.48+1.67*daylight;sunlight.intensity=.12+2.58*daylight;
  sunlight.color.set(sunHeight<.4&&sunHeight>-.2?'#ffd5a4':'#fff0d0');
  skySun.visible=daylight>.25;sunHalo.visible=skySun.visible;moon.visible=daylight<.75;
  skySun.position.y=45+Math.sin(cycle)*13;sunHalo.position.y=skySun.position.y;
  moon.position.y=45-Math.sin(cycle)*13;
  const darkness=1-daylight;
  nightAmount=darkness;headMat.emissiveIntensity=.4+darkness*1.4;
  const bulb=material('#ffefba');bulb.emissive.set('#ffd37d');bulb.emissiveIntensity=darkness*1.5;
  nextLampUpdate-=dt;
  if(nextLampUpdate<=0){
    nextLampUpdate=.6;
    const position=vehicle?vehicle.g.position:player.position;
    const nearest=streetLamps.slice().sort((a,b)=>(a.x-position.x)**2+(a.z-position.z)**2-(b.x-position.x)**2-(b.z-position.z)**2);
    streetLights.forEach((light,i)=>{const lamp=nearest[i];if(lamp)light.position.set(lamp.x,lamp.y,lamp.z);});
    litPlaces=places.slice().sort((a,b)=>(a.x-position.x)**2+(a.z-position.z)**2-(b.x-position.x)**2-(b.z-position.z)**2).slice(0,4);
    shopLights.forEach((light,i)=>{
      const shop=litPlaces[Math.floor(i/2)];
      if(shop){light.position.set(shop.x+(i%2?1:-1)*shop.w*.23,4.3,shop.z);light.color.set(shop.kind==='hospital'?'#f1f3e4':'#ffe5bb');}
    });
    const nearbyCars=cars.filter(car=>car.auto&&car.kind!=='bike').sort((a,b)=>
      (a.g.position.x-position.x)**2+(a.g.position.z-position.z)**2-(b.g.position.x-position.x)**2-(b.g.position.z-position.z)**2);
    trafficHeadlights.forEach((beam,i)=>beam.car=nearbyCars[i]||null);
  }
  streetLights.forEach(light=>light.intensity=darkness*18);
  shopLights.forEach(light=>light.intensity=4+darkness*29);
  canopyLight.intensity=4+darkness*28;
  for(const car of cars)if(car.spots?.length)for(const light of car.spots)light.intensity=18+darkness*62;
  for(const {light,target,car} of trafficHeadlights){
    if(!car){light.visible=false;continue;}
    const a=car.g.rotation.y,fx=Math.sin(a),fz=Math.cos(a),front=(car.length??4.6)/2;
    light.position.set(car.g.position.x+fx*front,car.g.position.y+.9,car.g.position.z+fz*front);
    target.position.set(car.g.position.x+fx*(front+16),groundHeight(car.g.position.x+fx*(front+16),car.g.position.z+fz*(front+16))+.15,car.g.position.z+fz*(front+16));
    light.visible=darkness>.03&&Math.hypot(car.g.position.x-(vehicle?vehicle.g.position.x:player.position.x),car.g.position.z-(vehicle?vehicle.g.position.z:player.position.z))<85;
    light.intensity=darkness*85;
  }
}
function updateServiceEffects(){
  const active=servicePoints.some(point=>point.kind==='fire-water'&&point.active);
  for(const drop of waterDrops){
    drop.mesh.visible=active;if(!active)continue;
    const t=(gameTime*.85+drop.phase)%1;
    drop.mesh.position.set(drop.originX-3.5*t,1.75+1.2*t-2.4*t*t,drop.originZ+Math.sin(drop.phase*12)*.12);
  }
}
function updateAnimals(dt){
  for(const animal of animals){
    const p=animal.g.position,distToOrigin=Math.hypot(p.x-animal.originX,p.z-animal.originZ);
    if(petting?.animal===animal){
      animal.g.rotation.y=Math.atan2(player.position.x-p.x,player.position.z-p.z);
      if(animal.tail)animal.tail.rotation.y=Math.sin(gameTime*16)*.65;
      if(animal.head)animal.head.position.y=.46+Math.sin(gameTime*8)*.04;
      continue;
    }
    animal.turn-=dt;
    if(animal.turn<=0){animal.turn=1.5+Math.random()*3;animal.heading+=(Math.random()-.5)*1.5;}
    if(distToOrigin>7)animal.heading=Math.atan2(animal.originX-p.x,animal.originZ-p.z);
    const distanceToPlayer=Math.hypot(p.x-player.position.x,p.z-player.position.z);
    if(animal.type==='pigeon'&&distanceToPlayer<2.4&&!vehicle)animal.heading=Math.atan2(p.x-player.position.x,p.z-player.position.z);
    const speed=animal.speed*(animal.type==='pigeon'&&distanceToPlayer<2.4?2:1);
    const nx=p.x+Math.sin(animal.heading)*speed*dt,nz=p.z+Math.cos(animal.heading)*speed*dt;
    const nearHuman=people.some(person=>!person.ragdoll&&Math.hypot(nx-person.g.position.x,nz-person.g.position.z)<(animal.type==='pigeon'?.5:.8));
    if(collides(nx,nz,.24)||vehicleBlocks(nx,nz,.25,null)||nearHuman){animal.heading+=1.8;animal.turn=.7;}
    else{p.x=nx;p.z=nz;}
    p.y=groundHeight(p.x,p.z);animal.g.rotation.y=animal.heading;
    if(animal.legs){animal.legs.forEach((leg,i)=>leg.rotation.x=Math.sin(timeForAnimal(animal)*6+(i===0||i===3?0:Math.PI))*.45);animal.tail.rotation.y=Math.sin(timeForAnimal(animal)*3)*.35;}
    if(animal.type==='pigeon'){animal.head.position.y=.46+Math.sin(gameTime*5+animal.phase)*.035;animal.body.rotation.x=Math.sin(gameTime*5+animal.phase)*.04;}
  }
}
function timeForAnimal(animal){return gameTime+animal.phase;}

const timer=new THREE.Clock();let metricTime=performance.now(),metricFrames=0,metricFps=0;
function animate(){
  requestAnimationFrame(animate);const dt=Math.min(timer.getDelta(),.035);
  gameTime+=dt;updateSignals(gameTime);updateDoors(dt);updatePlayer(dt);updateTraffic(dt);updateBusPassengers(dt);updatePeople(dt);updateAnimals(dt);impactActors();
  updateRagdolls(dt);updateParticles(dt);updateDebris(dt);updateBoats();updateServiceEffects();updateActorVisibility(dt);updateDayNight(gameTime,dt);updateSpeech();
  cars.forEach(animateVehicleLights);
  const transition=updateIndoorVisibility();updateCamera(dt,transition);
  if(debugMode){
    metricFrames++;const now=performance.now();if(now-metricTime>=1000){metricFps=metricFrames*1000/(now-metricTime);metricFrames=0;metricTime=now;}
    document.documentElement.dataset.city=JSON.stringify({fps:metricFps,drawCalls:renderer.info.render.calls,player:{x:player.position.x,y:player.position.y,z:player.position.z,ground:groundHeight(player.position.x,player.position.z),ragdoll:!!playerActor.ragdoll},car:vehicle?{x:vehicle.g.position.x,z:vehicle.g.position.z,speed:vehicle.velocity,kind:vehicle.kind||'car',role:vehicle.role||null,driverShown:!!vehicle.driver?.visible,emergencyLights:!!vehicle.emergency?.on,signal:vehicle.signal||null,reverse:vehicle.velocity<-.3,braking:!!vehicle.braking}:null,inside:currentPlace?.name||null,candidate:nearestInteraction()?.type||null,doors:places.map(p=>({name:p.name,x:p.door.x,z:p.door.z,angle:p.door.angle,offset:p.door.offset,automatic:p.door.automatic,open:p.door.open})),people:people.length,cars:cars.length,petting:!!petting,sitting:!!sitting,props:destructibles.filter(p=>p.active).length,debris:debris.length,
    vegetationOnPavement:vegetationPositions.filter(p=>isPaved(p.x,p.z)).length,terrainSamples:hills.map(([x,z])=>terrainHeight(x,z)),
    traffic:cars.filter(c=>c.auto).map(c=>({id:c.debugId||null,x:c.g.position.x,z:c.g.position.z,speed:c.velocity,driverShown:!!c.driver?.visible,stoppedReason:c.stoppedReason,signal:c.signal})),
    services:servicePoints.map(p=>({kind:p.kind,x:p.x,z:p.z,active:!!p.active})),
    bus:{x:cityBus.g.position.x,z:cityBus.g.position.z,speed:cityBus.velocity,currentStop:cityBus.atStop?.id||null,doors:!!cityBus.atStop,dwell:cityBus.dwell,passengers:cityBus.passengers.length,playerOnBus:vehicle===cityBus,stopCount:cityBus.stopCount},
    districts:districtTracks.map(track=>({id:track.id,oneWay:track.oneWay,length:track.length,stops:track.stops.length,connected:true})),
    districtCars:cars.filter(c=>c.track&&c.kind!=='bus').map(c=>({id:c.debugId||null,road:c.track.id,x:c.g.position.x,z:c.g.position.z,speed:c.velocity,lane:c.lane,targetLane:c.laneTarget,stoppedReason:c.stoppedReason,stopCount:c.stopCount,avoids:c.avoids})),
    placesByKind:{homes:places.filter(p=>p.kind==='home').length,residences:places.filter(p=>p.kind==='residence').length,restaurants:places.filter(p=>p.kind==='restaurant').length,parking:pavedAreas.filter(p=>p.kind==='parking').length,vacant:vacantLots.length},
    speech:activeSpeech?{name:activeSpeech.name,line:activeSpeech.line,remaining:activeSpeech.until-gameTime,speaker:activeSpeech.target.g?{x:activeSpeech.target.g.position.x,z:activeSpeech.target.g.position.z}:null}:null,
    lighting:{places:litPlaces.map(p=>p.name),shopIntensity:shopLights[0].intensity,canopyIntensity:canopyLight.intensity},
    debugPerson:people.find(p=>p.debugId==='yield')?.g.position||null,conversationActor:(()=>{const p=people.find(p=>p.debugId==='conversation');return p?{x:p.g.position.x,z:p.g.position.z,yaw:p.g.rotation.y,responding:activeSpeech?.target===p}:null;})()});
  }
  renderer.render(scene,camera);
}
// Dados somente de leitura para inspecionar o estado durante a revisão.
if(debugMode)Object.defineProperty(window,'cityDebug',{get:()=>({
  player:{x:player.position.x,y:player.position.y,z:player.position.z,ground:groundHeight(player.position.x,player.position.z),ragdoll:!!playerActor.ragdoll},
  car:vehicle?{x:vehicle.g.position.x,z:vehicle.g.position.z,speed:vehicle.velocity}:null,
  inside:currentPlace?.name||null,
  candidate:(nearestInteraction()?.type)||null,
  doors:places.map(p=>({name:p.name,x:p.door.x,z:p.door.z,angle:p.door.angle,offset:p.door.offset,automatic:p.door.automatic,open:p.door.open})),
  people:people.length,cars:cars.length,objects:scene.children.length
})});
if(debugMode){
  const destination=new URLSearchParams(location.search).get('near');
  const place=places.find(p=>p.kind===destination);
  if(place){player.position.set(place.door.x,0,place.door.z+4);yaw=0;pitch=.32;}
  if(destination==='beach'){player.position.set(0,0,-145);yaw=0;pitch=.15;}
  if(destination==='bike'){player.position.set(-51,groundHeight(-51,60.7),60.7);yaw=0;pitch=.32;}
  if(destination==='pet'){player.position.set(-24,groundHeight(-24,29),29);yaw=0;pitch=.32;}
  if(destination==='curve'){player.position.set(0,groundHeight(0,132),132);yaw=Math.PI;pitch=.32;}
  if(destination==='car'){player.position.set(-18,groundHeight(-18,56.9),56.9);yaw=0;pitch=.32;}
  if(destination==='cross'){player.position.set(0,groundHeight(0,83),83);yaw=0;pitch=.32;}
  if(destination==='ring'){player.position.set(111,groundHeight(111,72),72);yaw=-Math.PI/2;pitch=.3;}
  if(destination==='coast'){player.position.set(0,groundHeight(0,-114),-114);yaw=0;pitch=.25;}
  if(destination==='loop'){player.position.set(190,groundHeight(190,curvedRoadZ(190)),curvedRoadZ(190));yaw=-Math.PI/2;pitch=.3;}
  if(destination==='hill'){player.position.set(0,groundHeight(0,195),195);yaw=Math.PI;pitch=.32;}
  if(destination==='seat'){player.position.set(-28,groundHeight(-28,88),88);yaw=0;pitch=.32;}
  if(destination==='fire-water'){const p=servicePoints.find(p=>p.kind==='fire-water');player.position.set(p.x,groundHeight(p.x,p.z),p.z);yaw=-Math.PI/2;pitch=.32;}
  if(destination==='police-info'){const p=servicePoints.find(p=>p.kind==='police-info');player.position.set(p.x,groundHeight(p.x,p.z),p.z);yaw=0;pitch=.32;}
  if(destination==='engine'){player.position.set(-38.5,groundHeight(-38.5,-87),-87);yaw=Math.PI/2;pitch=.32;}
  if(destination==='patrol'){player.position.set(27.5,groundHeight(27.5,-87),-87);yaw=-Math.PI/2;pitch=.32;}
  if(destination==='officer'){player.position.set(42,groundHeight(42,-86.2),-86.2);yaw=0;pitch=.32;}
  if(destination==='firefighter'){player.position.set(-29,groundHeight(-29,-86.2),-86.2);yaw=0;pitch=.32;}
  if(destination==='bus'||destination==='bus-stop'){
    const stop=busTrack.stops.find(s=>s.id==='acacias');cityBus.routeProgress=stop.progress-(destination==='bus-stop'?12:0);
    const p=trackSample(busTrack,cityBus.routeProgress);cityBus.g.position.set(p.x,groundHeight(p.x,p.z),p.z);cityBus.g.rotation.y=Math.atan2(p.dx,p.dz);cityBus.velocity=destination==='bus-stop'?4:0;
    cityBus.lastStop=null;if(destination==='bus'){cityBus.atStop=stop;cityBus.dwell=7;}
    player.position.set(-65.2,groundHeight(-65.2,-33.6),-33.6);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='pare'){
    const car=cars.find(c=>c.track?.id==='oeste'&&c.kind!=='bus'),stop=car.track.stops[0];
    car.routeProgress=stop.progress-10;car.lane=car.laneTarget=car.homeLane=2.6;car.velocity=4;car.debugId='pare';
    const p=districtTrackPosition(car.track,car.routeProgress,car.lane);car.g.position.set(p.x,groundHeight(p.x,p.z),p.z);car.g.rotation.y=Math.atan2(p.dx,p.dz);
    player.position.set(stop.x+10,groundHeight(stop.x+10,stop.z),stop.z);yaw=-Math.PI/2;pitch=.32;
  }
  if(destination==='oneway'){
    const road=districtRoads.find(r=>r.id==='oeste'),p=road.curve.getPointAt(.35),v=road.curve.getTangentAt(.35);
    player.position.set(p.x+v.z*10,groundHeight(p.x+v.z*10,p.z-v.x*10),p.z-v.x*10);yaw=Math.atan2(-v.z,v.x);pitch=.32;
  }
  if(destination==='parking'){player.position.set(-145,groundHeight(-145,-100),-100);yaw=Math.PI/2;pitch=.32;}
  if(destination==='driver'){
    const testCar=cars.find(c=>c.auto&&c.axis==='x'&&!c.curve);testCar.g.position.set(-36,groundHeight(-36,-3.3),-3.3);testCar.g.rotation.y=Math.PI/2;testCar.direction=1;testCar.laneRoad=0;testCar.velocity=0;testCar.debugId='driver';testCar.debugHoldUntil=20;
    player.position.set(-40,groundHeight(-40,3.7),3.7);yaw=0;pitch=.32;zoom=10;
  }
  if(destination==='boarding'){
    const c=cars.find(c=>c.auto&&c.axis==='z');c.g.position.set(3.3,groundHeight(3.3,56),56);c.g.rotation.y=0;c.velocity=7;c.direction=1;c.laneRoad=0;c.debugId='boarding';
    player.position.set(5.8,groundHeight(5.8,56),56);yaw=0;pitch=.32;
  }
  if(destination==='conversation'){
    const p=people.find(p=>!p.indoor&&!p.stationary);p.g.position.set(-36,groundHeight(-36,16),16);Object.assign(p,{axis:'x',origin:-36,range:8,dir:1,speed:1.2,wait:0,debugId:'conversation'});
    player.position.set(-36,groundHeight(-36,18.3),18.3);yaw=0;pitch=.32;
  }
  if(destination==='conversation-wall'){
    const p=people.find(p=>!p.indoor&&!p.stationary);p.g.position.set(-36,groundHeight(-36,16),16);p.stationary=true;
    solid(-36,17.1,5,.3).opaque=true;part(scene,-36,1.5,17.1,5,3,.3,'#c6c6b3');
    player.position.set(-36,groundHeight(-36,18.3),18.3);yaw=0;pitch=.32;
  }
  if(destination==='exit-wall'){
    const c=cars[0];c.g.position.set(-18,groundHeight(-18,60),60);c.g.rotation.y=0;
    solid(-16.2,60,.35,6);part(scene,-16.2,1.5,60,.35,3,6,'#c6c6b3');
    player.position.set(-20.8,groundHeight(-20.8,60),60);yaw=0;pitch=.32;
  }
  if(destination==='break'){const testCar=cars[0];testCar.g.position.set(-49,groundHeight(-49,12),12);testCar.g.rotation.y=0;player.position.set(-49,groundHeight(-49,8.8),8.8);yaw=0;pitch=.32;}
  if(destination==='yield'){
    const testCar=cars.find(c=>c.auto&&!c.curve&&c.axis==='z');testCar.g.position.set(3.3,groundHeight(3.3,56),56);testCar.g.rotation.y=0;testCar.direction=1;testCar.laneRoad=0;testCar.velocity=7;testCar.debugId='yield';
    const testPerson=people.find(p=>!p.indoor&&!p.stationary);testPerson.g.position.set(3.3,groundHeight(3.3,65),65);testPerson.stationary=true;testPerson.debugId='yield';
    player.position.set(12,groundHeight(12,58),58);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='turn'){
    const testCar=cars.find(c=>c.auto&&c.axis==='x');testCar.g.position.set(96,groundHeight(96,-3.3),-3.3);testCar.g.rotation.y=Math.PI/2;testCar.direction=1;testCar.laneRoad=0;testCar.velocity=5;testCar.debugId='turn';
    player.position.set(96,groundHeight(96,10),10);yaw=0;pitch=.32;
  }
  targetYaw=yaw;targetPitch=pitch;targetZoom=zoom;
  const requestedTime=Number(new URLSearchParams(location.search).get('time'));if(Number.isFinite(requestedTime))gameTime=requestedTime;
}
animate();
addEventListener('resize',()=>{camera.aspect=innerWidth/innerHeight;camera.updateProjectionMatrix();renderer.setSize(innerWidth,innerHeight);});
