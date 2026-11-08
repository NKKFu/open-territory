import * as THREE from './vendor/three.module.js';
import {createNpcLife} from './npc-life.js';
import {createResidentMotion} from './resident-motion.js';
import {createCityPolice,createPoliceRadio} from './city-police.js';
import {createPoliceRoute} from './police-road.js';
import {createPedestrianLife,trafficBlocksCrossing} from './pedestrian-life.js';
import {createCitySocial,createSocialProps,umbrellaClear} from './city-social.js';
import {createCombat,traceShot,weaponIds} from './city-combat.js';
import {createCombatView} from './combat-view.js';
import {createMarine,seaBounds,shoreZ,seabed} from './marine.js';
import {createMarineArt} from './marine-art.js';
import {harborPier as marineHarborPier,harborLadder as marineHarborLadder} from './harbor-district.js';
import {createVehicleIncidents} from './vehicle-incidents.js';
import {createActorBatches} from './actor-batches.js';
import {createSurfaceSampler} from './surface-sampler.js';
import {createCityWeather,createRainCover,wetRoadGrip} from './city-weather.js';
import {createRainField} from './weather-effects.js';
import {cityElevation,gradedBetween,addGradedFace} from './city-relief.js';
import {vehicleGroundPose} from './vehicle-grounding.js';
import {stepCarDynamics,integrateCarPose,wheelSteeringAngle} from './vehicle-dynamics.js';
import {resolveVehicleImpact,vehicleSupportArm} from './vehicle-impact.js';
import {passingLaneClear,signalPhase,requestCrossing,clearsAmber,approachSpeed,pedestrianConflict,routeBends,routeAdvice} from './traffic-ai.js';
import {stepTrafficDrive} from './traffic-driving.js';
import {createAmbientOcclusion} from './ambient-occlusion.js';
import {createCityAudio} from './city-audio.js';
import {createBirdLife} from './bird-life.js';
import {createCitySurfaces,surfaceKind,cityPigment} from './city-surfaces.js';
import {createWorldEffects} from './world-effects.js';
import {addStreetArt} from './street-art.js';
import {createPlayerMotion} from './player-motion.js';
import {cameraClearance,createCameraBoom} from './follow-camera.js';
import {createWalkCamera,frameCloseActor} from './walk-camera.js';
import {createVehicleCamera} from './vehicle-camera.js';
import {installPublicCameras} from './surveillance.js';
import {planParkour,parkourPosition} from './parkour.js';
import {createSettings} from './game-settings.js';
import {cutPavement,footprint,rectangle,overlapFootprints} from './street-pavement.js';
import {carDesign,buildCarShell,buildRoadWheel} from './vehicle-assets.js';
import {createCharacter,buildCharacterHead,addCharacterUniform,addCharacterHat,buildDriverCharacter,dressCyclist} from './character-assets.js';
import {residentialFacade,lighthouse,beachDecks,seasideKiosks} from './city-landmarks.js';
import {createFishMarket} from './fish-market.js';
import {createInteractionPrompt} from './interaction-prompt.js';
import {createCityEconomy,cashText,weightText} from './city-economy.js';
import {furnishArmory,installDistrictGuide} from './district-commerce.js';
import {installPracticeRange} from './practice-range.js';
import {installHarbor} from './harbor-district.js';
import {createSurveyDrone} from './survey-drone.js';
import {installStreetCinema} from './street-cinema.js';
import {installStreetPerformance} from './street-performance.js';

// Open Territory: geometria low-poly criada no próprio jogo, sem modelos externos.
const $ = (selector) => document.querySelector(selector);
const debugMode=new URLSearchParams(location.search).has('debug');
const cityWeather=createCityWeather(debugMode?new URLSearchParams(location.search).get('weather')||'clear':'clear');
let weatherCover=null;
const scene = new THREE.Scene();
scene.background = new THREE.Color('#a9dce5');
scene.fog = new THREE.Fog('#c9e3df', 150, 370);
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
const rendererContext=renderer.getContext(),rendererInfo=rendererContext.getExtension('WEBGL_debug_renderer_info');
const softwareRendering=rendererInfo?/swiftshader|llvmpipe|software|swrast/i.test(rendererContext.getParameter(rendererInfo.UNMASKED_RENDERER_WEBGL)):false;
renderer.setPixelRatio(Math.min(window.devicePixelRatio, softwareRendering ? .7 : 1.6));
renderer.setSize(innerWidth, innerHeight,false);
renderer.shadowMap.enabled = !softwareRendering;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.08;
$('#world').appendChild(renderer.domElement);
renderer.domElement.tabIndex=-1;
const camera = new THREE.PerspectiveCamera(58, innerWidth / innerHeight, 0.1, 1000);
const ambientOcclusion=createAmbientOcclusion({THREE,renderer,scene,camera,software:softwareRendering});
const cityAudio=createCityAudio();
const citySurfaces=createCitySurfaces({THREE,enabled:renderer.capabilities.maxVaryings>=12,software:softwareRendering});
const ambient=new THREE.HemisphereLight('#eaf9ff', '#738e69', 2.15);scene.add(ambient);
const sunlight = new THREE.DirectionalLight('#fff0d0', 2.7);
sunlight.position.set(-75, 130, 75);
sunlight.castShadow = true;
sunlight.shadow.mapSize.set(1536, 1536);
Object.assign(sunlight.shadow.camera, { left: -65, right: 65, top: 65, bottom: -65, near: 1, far: 310 });
sunlight.shadow.camera.updateProjectionMatrix();
sunlight.shadow.bias = -0.0004;
sunlight.shadow.normalBias = .05;
scene.add(sunlight);
scene.add(sunlight.target);

const materials = new Map();
const cube = new THREE.BoxGeometry(1, 1, 1);
function material(color) {
  if (!materials.has(color)) materials.set(color,citySurfaces.decorate(new THREE.MeshStandardMaterial({color:cityPigment(color),roughness:surfaceKind(color)===7?.35:.82,metalness:surfaceKind(color)===7?.06:0}),surfaceKind(color)));
  return materials.get(color);
}
function outdoorFloorMaterial(color){
  const kind=surfaceKind(color);
  if(kind!==1&&kind!==3&&kind!==4)return material(color);
  const key='floor:'+color;
  if(!materials.has(key)){
    const floor=citySurfaces.decorate(material(color).clone(),kind+8);
    floor.userData.weatherFloor=true;materials.set(key,floor);
  }
  return materials.get(key);
}
const batches = new Map();
const dummy = new THREE.Object3D();
let buildingHeightOffset=0;
const groundColors=new Set(['#60767b','#d9d2b9','#82918a','#eee6d1','#d8bc91','#e6e9dc','#a2c18b','#e5d9bf','#a6c58e','#e3d6ba','#e1c49a','#bc986e','#d9d0b8','#b0ca96','#b9b38f','#ded8c6','#eed8a4','#d5d2bd','#e9e9dc','#d6e3d9']);
groundColors.add('#dcbf94');
groundColors.add('#d8c5a4');
groundColors.add('#91ad78');
const paintColors=new Set(['#f8f4de','#f0dd9c','#fbf4df','#bcbdae']);
// The generator describes buildings relative to the local street. During play,
// every sampler returns the absolute height of the surface at the query point.
let cityPlaced=false;
function surveyedSurface(baseHeight){
  const local=createSurfaceSampler({baseHeight});
  return {...local,heightAt:(x,z)=>local.heightAt(x,z)+(cityPlaced?cityElevation(z):0)};
}
const renderedGround=surveyedSurface((x,z)=>Math.abs(x)<=210&&z>=-130&&z<=220?localTerrainHeight(x,z)-.05:-.4);
const streetSurface=surveyedSurface(()=>-Infinity);
const streetMasks=[],pavingPanels=[],streetMarks=[];
let layingStreets=false;
function pavingPanel(shape,top,bottom,color){pavingPanels.push({shape,top,bottom,color});}
function block(x, y, z, w, h, d, color, rot = 0, anchorZ=z) {
  y+=buildingHeightOffset;
  if(layingStreets){
    if(color==='#60767b')streetMasks.push({...rectangle(x,z,w,d,rot),height:y+h/2});
    if(color==='#d9d2b9'||color==='#eee6d1'){
      pavingPanel(rectangle(x,z,w,d,rot),y+h/2,y-h/2,color);return;
    }
    if(color==='#82918a'&&h<=.12){
      pavingPanel(rectangle(x,z,w,d,rot),y+h/2,y-h/2,color);return;
    }
    if(paintColors.has(color)&&h<=.03&&y<.3){streetMarks.push([x,y,z,w,h,d,color,rot]);return;}
  }
  if(paintColors.has(color)&&h<=.03&&y<.3){y=renderedGround.heightAt(x,z)+.003;h=.003;}
  if(groundColors.has(color)&&h<=.5&&y+h/2<=.5)renderedGround.addRectangle({x,z,w,d,top:y+h/2,rotation:rot});
  const key = color;
  if (!batches.has(key)) batches.set(key, []);
  // An offset branch belongs to its trunk, not to the ground underneath its tip.
  const anchorOffset=anchorZ===z?0:cityElevation(anchorZ)-cityElevation(z);
  dummy.position.set(x, y+anchorOffset, z);
  dummy.rotation.set(0, rot, 0);
  dummy.scale.set(w, h, d);
  dummy.updateMatrix();
  batches.get(key).push(dummy.matrix.clone());
}
function finishPaving(){
  layingStreets=false;
  const covered=[],chunks=new Map(),walkable=[],drivable=[...streetMasks];
  for(const {points,height} of streetMasks){
    for(let i=1;i<points.length-1;i++)streetSurface.addTriangle([points[0],points[i],points[i+1]],height);
  }
  function chunk(shape,color){
    const {bounds}=shape,key=`${color}:${Math.floor(bounds.x0/48)}:${Math.floor(bounds.z0/48)}`;
    if(!chunks.has(key))chunks.set(key,{color,vertices:[]});
    return chunks.get(key).vertices;
  }
  for(const panel of pavingPanels.sort((a,b)=>b.top-a.top)){
    const {shape,top,bottom,color}=panel;
    const pieces=cutPavement(shape,[...streetMasks,...covered]);
    for(const piece of pieces){
      const p=piece.points,vertices=chunk(piece,color);
      (color==='#82918a'?drivable:walkable).push({...piece,height:top});
      for(let i=1;i<p.length-1;i++){
        const tri=[p[0],p[i+1],p[i]];
        for(const v of tri)vertices.push(v.x,top,v.z);
        renderedGround.addTriangle(tri,top);
      }
      for(let i=0;i<p.length;i++){
        const a=p[i],b=p[(i+1)%p.length];
        vertices.push(a.x,top,a.z,b.x,top,b.z,a.x,bottom,a.z,b.x,top,b.z,b.x,bottom,b.z,a.x,bottom,a.z);
      }
    }
    covered.push(shape);
  }
  drivable.sort((a,b)=>b.height-a.height);
  for(const [x,,z,w,,d,color,rot] of streetMarks){
    const mark=rectangle(x,z,w,d,rot),drawn=[];
    for(const surface of color==='#bcbdae'?walkable:drivable){
      const overlap=overlapFootprints(mark,surface);if(!overlap)continue;
      for(const piece of cutPavement(overlap,drawn)){
        const p=piece.points,vertices=chunk(piece,color);
        for(let i=1;i<p.length-1;i++)for(const v of [p[0],p[i+1],p[i]])vertices.push(v.x,surface.height+.005,v.z);
      }
      drawn.push(overlap);
    }
  }
  for(const {color,vertices} of chunks.values()){
    const geo=new THREE.BufferGeometry();geo.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));geo.computeVertexNormals();
    const mesh=new THREE.Mesh(geo,outdoorFloorMaterial(color));mesh.receiveShadow=true;scene.add(mesh);drapeSurface(mesh);
  }
  pavingPanels.length=streetMasks.length=streetMarks.length=0;
}
function flushBlocks() {
  const grouped=new Map(),graded=new Map(),unshadowed=new Set(['#60767b','#f8f4de','#f0dd9c','#fbf4df','#bcbdae']);
  for(const [color,transforms] of batches){
    const kind=color==='#ffefba'?'lamps':unshadowed.has(color)?'ground':'solid';
    for(const matrix of transforms){
      const m=matrix.elements,extent=(Math.abs(m[2])+Math.abs(m[10]))/2;
      if(m[13]+m[5]/2<.7&&(groundColors.has(color)||paintColors.has(color))&&gradedBetween(m[14]-extent,m[14]+extent)){
        const key=color+':'+Math.floor(m[12]/64)+':'+Math.floor(m[14]/64);
        if(!graded.has(key))graded.set(key,{color,vertices:[]});
        appendGradedGeometry(cube,matrix,graded.get(key).vertices);continue;
      }
      const style=surfaceKind(color),level=m[13]+m[5]/2;
      const wetFloor=(style===1||style===3||style===4)&&level<.7&&
        (groundColors.has(color)||Math.abs(renderedGround.heightAt(m[12],m[14])-level)<.025);
      m[13]+=cityElevation(m[14]);
      const key=`${kind}:${Math.floor(matrix.elements[12]/64)}:${Math.floor(matrix.elements[14]/64)}`;
      if(!grouped.has(key))grouped.set(key,{kind,items:[]});grouped.get(key).items.push({color,matrix,style:style+(wetFloor?8:0)});
    }
  }
  const paletteMaterial=citySurfaces.decorate(new THREE.MeshStandardMaterial({color:'#ffffff',roughness:.82}),0,true);
  for(const {color,vertices} of graded.values()){
    const mesh=new THREE.Mesh(surfaceGeometry(vertices),outdoorFloorMaterial(color));mesh.receiveShadow=true;scene.add(mesh);
  }
  for (const {kind,items} of grouped.values()) {
    const geometry=kind==='lamps'?cube:cube.clone();
    if(kind!=='lamps')geometry.setAttribute('citySurface',new THREE.InstancedBufferAttribute(new Float32Array(items.map(item=>item.style)),1));
    const mesh = new THREE.InstancedMesh(geometry, kind==='lamps'?material('#ffefba'):paletteMaterial, items.length);
    items.forEach(({color,matrix},index)=>{mesh.setMatrixAt(index,matrix);if(kind!=='lamps')mesh.setColorAt(index,material(color).color);});
    mesh.instanceMatrix.needsUpdate = true;
    if(mesh.instanceColor)mesh.instanceColor.needsUpdate=true;
    mesh.castShadow = kind!=='ground';
    mesh.receiveShadow = true;
    mesh.computeBoundingSphere();
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
  y+=buildingHeightOffset;
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
function registerSurfaceMesh(mesh){
  if(mesh.material===material('#60767b'))mesh.material=outdoorFloorMaterial('#60767b');
  mesh.updateMatrixWorld(true);
  const positions=mesh.geometry.attributes.position,indices=mesh.geometry.index;
  for(let i=0;i<(indices?.count||positions.count);i+=3){
    const points=[0,1,2].map(j=>new THREE.Vector3().fromBufferAttribute(positions,indices?indices.getX(i+j):i+j).applyMatrix4(mesh.matrixWorld));
    if(Math.max(...points.map(p=>p.y))-Math.min(...points.map(p=>p.y))<.001)renderedGround.addTriangle(points,points[0].y);
  }
  drapeSurface(mesh);
}
function surfaceGeometry(vertices){
  const geo=new THREE.BufferGeometry();
  geo.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));geo.computeVertexNormals();return geo;
}
function appendGradedGeometry(geometry,matrix,vertices){
  const positions=geometry.attributes.position,indices=geometry.index;
  const face=[new THREE.Vector3(),new THREE.Vector3(),new THREE.Vector3()];
  for(let i=0;i<(indices?.count??positions.count);i+=3){
    face.forEach((p,j)=>p.fromBufferAttribute(positions,indices?indices.getX(i+j):i+j).applyMatrix4(matrix));
    addGradedFace(vertices,face);
  }
}
function drapeSurface(mesh){
  mesh.updateMatrixWorld(true);mesh.geometry.computeBoundingBox();
  const bounds=mesh.geometry.boundingBox.clone().applyMatrix4(mesh.matrixWorld);
  if(!gradedBetween(bounds.min.z,bounds.max.z))mesh.position.y+=cityElevation(bounds.min.z);
  else{
    const vertices=[];appendGradedGeometry(mesh.geometry,mesh.matrixWorld,vertices);
    mesh.geometry.dispose();mesh.geometry=surfaceGeometry(vertices);
    mesh.position.set(0,0,0);mesh.rotation.set(0,0,0);mesh.scale.set(1,1,1);
  }
  mesh.userData.followsGrade=true;
}
let seed = 45274;
function random() { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; }
function pick(values) { return values[Math.floor(random() * values.length)]; }
const solids = [], places = [], people = [], cars = [], destructibles = [], benches = [], cameraObstacles = [], servicePoints = [], waterDrops = [], kitchenSteam=[],cityFixtures=[];
let marine=null;
const solidCells=new Map(),SOLID_CELL=12;
function solid(x,z,w,d){
  const collider={x,z,w,d,active:true};solids.push(collider);
  for(let cx=Math.floor((x-w/2)/SOLID_CELL);cx<=Math.floor((x+w/2)/SOLID_CELL);cx++)for(let cz=Math.floor((z-d/2)/SOLID_CELL);cz<=Math.floor((z+d/2)/SOLID_CELL);cz++){
    const key=`${cx},${cz}`;if(!solidCells.has(key))solidCells.set(key,[]);solidCells.get(key).push(collider);
  }
  return collider;
}
function* nearbySolids(x,z,r){
  for(let cx=Math.floor((x-r)/SOLID_CELL);cx<=Math.floor((x+r)/SOLID_CELL);cx++)for(let cz=Math.floor((z-r)/SOLID_CELL);cz<=Math.floor((z+r)/SOLID_CELL);cz++){
    for(const s of solidCells.get(`${cx},${cz}`)||[])if(s.active)yield s;
  }
}

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
function localTerrainHeight(x,z){
  if(x < -210||x > 210||z < -130||z > 220)return 0;
  const gx=(x+210)/5,gz=(z+130)/5,col=Math.min(83,Math.floor(gx)),row=Math.min(69,Math.floor(gz));
  const tx=gx-col,tz=gz-row,h00=terrainGrid[row*85+col],h10=terrainGrid[row*85+col+1];
  const h01=terrainGrid[(row+1)*85+col],h11=terrainGrid[(row+1)*85+col+1];
  return tx+tz<=1?h00+tx*(h10-h00)+tz*(h01-h00):h11+(1-tx)*(h01-h11)+(1-tz)*(h10-h11);
}
function terrainHeight(x,z){return localTerrainHeight(x,z)+(cityPlaced?cityElevation(z):0);}
function makeTerrain(){
  const geometry=new THREE.PlaneGeometry(420,350,84,70);geometry.rotateX(-Math.PI/2);
  const vertices=geometry.attributes.position;
  for(let i=0;i<vertices.count;i++){
    const x=vertices.getX(i),z=vertices.getZ(i)+45;vertices.setY(i,localTerrainHeight(x,z)+cityElevation(z)-.05);
  }
  geometry.computeVertexNormals();
  const mesh=new THREE.Mesh(geometry,material('#a9c590'));mesh.position.z=45;mesh.receiveShadow=true;
  mesh.userData.followsGrade=true;scene.add(mesh);
}

// Céu, sol cúbico, nuvens e mar.
const skySun = new THREE.Mesh(new THREE.BoxGeometry(30, 30, 1), new THREE.MeshBasicMaterial({ color: '#ffedb1', fog: false,transparent:true,depthWrite:false }));
skySun.position.set(-80, 45, -300); scene.add(skySun);
const sunHalo = new THREE.Mesh(new THREE.PlaneGeometry(42, 42), new THREE.MeshBasicMaterial({ color: '#ffdda1', transparent: true, opacity: .17, fog: false, depthWrite: false, side: THREE.DoubleSide }));
sunHalo.position.set(-80, 45, -302); scene.add(sunHalo);
const moon=new THREE.Mesh(new THREE.BoxGeometry(20,20,1),new THREE.MeshBasicMaterial({color:'#e7e9db',fog:false,transparent:true,depthWrite:false}));moon.position.set(-80,45,-300);moon.visible=false;scene.add(moon);
for (let i = 0; i < 16; i++) {
  const x = -300 + random() * 600, y = 72 + random() * 50, z = -180 - random() * 190;
  block(x, y, z, 20 + random() * 12, 4.5, 6, '#f7f3e4');
  block(x - 3, y + 3, z, 12, 4, 7, '#f7f3e4');
  block(x + 7, y + 2, z, 7, 3, 6, '#f7f3e4');
}
makeTerrain();
block(0, -.34, -145, 430, .3, 42, '#eed8a4');
for (let i = 0; i < 80; i++) {
  const x = -280 + random() * 560, z = -166 - random() * 150;
  const length=4+random()*12;if(z< -267)block(x, -.24, z, length, .025, .24, '#94c8c7');
}

// Ruas, calçadas com juntas, meios-fios e travessias.
const roads = [-72, 0, 72];
function curvedRoadZ(x){return 155+12*Math.sin(x/39);}
// A avenida junto à praça faz uma curva visível já no ponto inicial.
function plazaRoadBend(x){return x>-61&&x<-11?8*Math.sin((x+61)/50*Math.PI)**2:0;}
function horizontalRoadZ(x,road){return road===72?road+plazaRoadBend(x):road;}
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
  {x:54,z:105.75,w:7.65,d:20,top:.23,kind:'forecourt'},
  {x:45,z:116.85,w:21.4,d:3.2,top:.23,kind:'path'},
  {x:-36,z:-88.25,w:23,d:10.5,top:.23,kind:'forecourt'},
  {x:36,z:-88.25,w:23,d:10.5,top:.23,kind:'forecourt'},
  {x:-161,z:-103,w:34,d:22,top:.07,kind:'parking'},
  {x:-18,z:67.5,w:12,d:5,top:.07,kind:'parking'},
  {x:-18,z:63.5,w:3.4,d:3,top:.23,kind:'path'},
  {x:22,z:64,w:6,d:16,top:.07,kind:'parking'},
  {x:-140,z:-103,w:15,d:5,top:.07,kind:'driveway'},
  {x:144,z:-103,w:12,d:3,top:.23,kind:'path'}
];
// Useful objects sit on connected paving instead of isolated patches of grass.
for(const [x,z,w,d] of [[-55,58,14,4],[20,53.5,12,7.5],[12.5,55.5,5,3],[25,50,4,3],[91,53.5,9,7.5],[85,55.5,7,3],[-36,65,3.4,8],[85.5,21,6.5,3],[-85,-90,6,3],[-61.5,-44,4,2.5]])pavedAreas.push({x,z,w,d,top:.23,kind:'forecourt'});
for(const [x,z] of [[-104,-104],[-104,-36],[-104,36],[-36,-36],[36,-36],[36,36],[104,36]])pavedAreas.push({x,z:z+12,w:23,d:3,top:.23,kind:'forecourt'});
pavedAreas.push({x:17.5,z:36,w:13,d:3.2,top:.23,kind:'path'});
const vacantLots=[{x:164,z:-103,w:30,d:20}];
const busStops=[
  {id:'acacias',name:'Acácias',x:-61.7,z:-36,roadX:-68.7,roadZ:-36,rotation:-Math.PI/2},
  {id:'praca',name:'Praça',x:-61.7,z:36,roadX:-68.7,roadZ:36,rotation:-Math.PI/2},
  {id:'hospital',name:'Hospital',x:61.7,z:36,roadX:68.7,roadZ:36,rotation:Math.PI/2},
  {id:'livraria',name:'Livraria',x:61.7,z:-36,roadX:68.7,roadZ:-36,rotation:Math.PI/2}
];
// Passeios ligam as portas dos novos imóveis à calçada da avenida lateral.
for(const side of [-1,1])for(const front of [-38,-5,32.5])pavedAreas.push({x:side*147,z:front+1.8,w:18,d:3,top:.23,kind:'path'});
const overlook={x:-18,z:164};
const overlookWalk=curvedRoadZ(overlook.x)+8;
pavedAreas.push({x:overlook.x,z:(overlookWalk+overlook.z)/2,w:3.2,d:overlook.z-overlookWalk,top:.23,kind:'path'},
  {x:overlook.x,z:overlook.z,w:13,d:5,top:.23,kind:'forecourt'});
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
function pavementSegments(start,end,gaps=[]){
  let sections=[[start,end]];
  for(const [lo,hi] of gaps)sections=sections.flatMap(([a,b])=>hi<=a||lo>=b?[[a,b]]:
    [[a,Math.min(lo,b)],[Math.max(hi,a),b]].filter(([x,y])=>y>x));
  return sections;
}
// A continuous strip joins neighboring samples without opening wedges at turns.
function plazaStrip(start,end,low,high,top,bottom,color,center=x=>horizontalRoadZ(x,72)){
  const xs=[start];for(let x=Math.ceil(start/2)*2;x<end;x+=2)if(x>start)xs.push(x);xs.push(end);
  const vertices=[],indices=[];
  const point=(x,offset,y)=>[x,y,center(x)+offset];
  function quad(a,b,c,d){
    const first=vertices.length/3;vertices.push(...a,...b,...c,...d);
    indices.push(first,first+1,first+2,first+2,first+1,first+3);
  }
  for(let i=1;i<xs.length;i++){
    const a=xs[i-1],b=xs[i];
    const shape=footprint([{x:a,z:center(a)+low},{x:a,z:center(a)+high},{x:b,z:center(b)+high},{x:b,z:center(b)+low}]);
    if(color!=='#60767b'){pavingPanel(shape,top,bottom,color);continue;}
    streetMasks.push({...shape,height:top});
    quad(point(a,low,top),point(a,high,top),point(b,low,top),point(b,high,top));
    quad(point(a,low,top),point(b,low,top),point(a,low,bottom),point(b,low,bottom));
    quad(point(b,high,top),point(a,high,top),point(b,high,bottom),point(a,high,bottom));
  }
  if(color!=='#60767b')return;
  quad(point(start,high,top),point(start,low,top),point(start,high,bottom),point(start,low,bottom));
  quad(point(end,low,top),point(end,high,top),point(end,low,bottom),point(end,high,bottom));
  const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));geometry.setIndex(indices);geometry.computeVertexNormals();
  const mesh=new THREE.Mesh(geometry,material(color));mesh.receiveShadow=true;scene.add(mesh);registerSurfaceMesh(mesh);
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
function trackNearestNear(track,x,z,progress,window=24){
  let best=Infinity,result=progress;
  for(let i=1;i<track.points.length;i++){
    const a=track.points[i-1],b=track.points[i],dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
    const t=den?THREE.MathUtils.clamp(((x-a.x)*dx+(z-a.z)*dz)/den,0,1):0;
    const candidate=track.cumulative[i-1]+Math.sqrt(den)*t;
    const delta=((candidate-progress+track.length*1.5)%track.length)-track.length/2;
    if(Math.abs(delta)>window)continue;
    const distance=(x-a.x-t*dx)**2+(z-a.z-t*dz)**2;
    if(distance<best){best=distance;result=candidate%track.length;}
  }
  return result;
}
// Altura física dos vários pisos, compartilhada por pessoas, animais e veículos.
function groundHeight(x,z){
  if(z< -158){const deck=marine?.dockAt(x,z);if(deck)return deck.top;if(z<shoreZ)return seabed(x,z);}
  const grade=cityPlaced?cityElevation(z):0;
  if(Math.abs(z+160)<=2.5&&beachDecks.some(cx=>Math.abs(x-cx)<=3.5))return -.01;
  for(const p of places){if(Math.abs(x-p.x)<p.w/2-.1&&Math.abs(z-p.z)<p.d/2-.1)return (p.kind==='hospital'?.205:.18)+grade;}
  if(x>89&&x<119&&z>-44.9&&z<-10.5)return .255+grade;
  return renderedGround.heightAt(x,z);
}
layingStreets=true;
for (const a of roads) {
  block(a, -.02, 0, 14, .12, 246, '#60767b');
  if(a!==72)block(0, -.01, a, 246, .12, 14, '#60767b');
  else{
    block(-97.5,-.01,72,51,.12,14,'#60767b');block(61.5,-.01,72,123,.12,14,'#60767b');
    plazaStrip(-72,0,-7,7,.05,-.07,'#60767b');
    for(const side of [-1,1]){
      for(const [start,end] of pavementSegments(-61,-11,side===-1?[[-24,-12]]:[])){
        plazaStrip(start,end,side<0?-11:7,side<0?-7:11,.23,.01,'#d9d2b9');
        plazaStrip(start,end,side<0?-7.17:6.99,side<0?-6.99:7.17,.27,.19,'#eee6d1');
      }
    }
  }
  for (const side of [-1, 1]) {
    // As calçadas terminam antes da via perpendicular, sem atravessar a rua.
    for(const [start,end] of [[-123,-83],[-61,-11],[11,61],[83,123]]){
      const center=(start+end)/2,length=end-start;
      block(a+side*9,.12,center,4,.22,length,'#d9d2b9');
      const horizontal=pavementSegments(start,end,a===72&&side===-1?[[19,25]]:[]);
      if(!(a===72&&start===-61))for(const [begin,finish] of horizontal)block((begin+finish)/2,.12,a+side*9,finish-begin,.22,4,'#d9d2b9');
      block(a+side*7.08,.23,center,.18,.08,length,'#eee6d1');
      if(!(a===72&&start===-61))for(const [begin,finish] of horizontal)block((begin+finish)/2,.23,a+side*7.08,finish-begin,.08,.18,'#eee6d1');
    }
    for (let p = -120; p <= 120; p += 4) {
      if(roads.some(r=>Math.abs(p-r)<11.3))continue;
      block(a + side * 9, .235, p, 3.9, .012, .045, '#bcbdae');
      if(!(a===72&&p>-72&&p<0)&&!(a===72&&side===-1&&p>=19&&p<=25))block(p, .235, horizontalRoadZ(p,a) + side * 9, .045, .012, 3.9, '#bcbdae');
    }
  }
  for (let p = -116; p < 120; p += 8) {
    if (roads.some(r => Math.abs(p-r) < 12)) continue;
    block(a, .055, p, .18, .025, 3.3, '#f0dd9c');
    block(p, .06, horizontalRoadZ(p,a), 3.3, .025, .18, '#f0dd9c',a===72?-Math.atan2(horizontalRoadZ(p+1,a)-horizontalRoadZ(p-1,a),2):0);
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
plazaStrip(-200,200,-7,7,.05,-.07,'#60767b',curvedRoadZ);
for(const side of [-1,1]){
  plazaStrip(-200,200,side<0?-11:7,side<0?-7:11,.23,.01,'#d9d2b9',curvedRoadZ);
  plazaStrip(-200,200,side<0?-7.17:7,side<0?-7:7.17,.27,.19,'#eee6d1',curvedRoadZ);
}
for(let x=-205;x<205;x+=5){
  const nx=x+5,z1=curvedRoadZ(x),z2=curvedRoadZ(nx),dx=nx-x,dz=z2-z1;
  const angle=-Math.atan2(dz,dx),cx=(x+nx)/2,cz=(z1+z2)/2;
  if(Math.abs(cx)<188&&![...roads,-130,130].some(road=>Math.abs(cx-road)<12)&&Math.round((x+205)/5)%2===0)block(cx,.065,cz,2.7,.025,.18,'#f0dd9c',angle);
}
for(const road of roads){
  const end=curvedRoadZ(road),roadEnd=end,length=roadEnd-123;
  block(road,-.02,(123+roadEnd)/2,14,.12,length,'#60767b');
  const sidewalkEnd=end,sidewalkLength=sidewalkEnd-123;
  if(sidewalkLength>0)for(const side of [-1,1]){
    block(road+side*9,.12,(123+sidewalkEnd)/2,4,.22,sidewalkLength,'#d9d2b9');
    block(road+side*7.08,.23,(123+sidewalkEnd)/2,.18,.08,sidewalkLength,'#eee6d1');
  }
  for(let z=126;z<end-12;z+=8)block(road,.055,z,.18,.025,3.3,'#f0dd9c');
  for(const side of [-1,1])for(let i=-3;i<=3;i++){
    if(side<0)block(road+i*1.7,.072,curvedRoadZ(road+i*1.7)-8.8,.85,.025,2.6,'#f8f4de');
    block(road+side*8.8,.072,curvedRoadZ(road+side*8.8)+i*1.7,2.6,.025,.85,'#f8f4de');
  }
}

// Avenidas laterais e via da orla conectam as ruas centrais numa malha contínua.
const RING_X=130, COAST_Z=-131;
for(const side of [-1,1]){
  const road=side*RING_X,end=curvedRoadZ(road),length=end-COAST_Z;
  block(road,-.02,(COAST_Z+end)/2,14,.12,length,'#60767b');
  // Liga cada rua do centro à avenida lateral, formando uma junção em T.
  for(const cross of roads){
    for(const sx of [-1,1])block(road+sx*9,.12,cross,4,.22,22,'#d9d2b9');
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
    const begin=breaks[i]+11,finish=breaks[i+1]===end?end:breaks[i+1]-11;
    if(finish<=begin)continue;
    for(const edge of [-1,1]){
      for(const [start,end] of pavementSegments(begin,finish,side===-1&&edge===-1?[[-105.5,-100.5]]:[])){
        block(road+edge*9,.12,(start+end)/2,4,.22,end-start,'#d9d2b9');
        block(road+edge*7.08,.23,(start+end)/2,.18,.08,end-start,'#eee6d1');
      }
    }
  }
  for(let z=COAST_Z+14;z<end-12;z+=8){if(roads.some(r=>Math.abs(z-r)<12))continue;block(road,.06,z,.18,.025,3.3,'#f0dd9c');}
  // Retorno arredondado onde a avenida das Colinas encontra a floresta.
  const x=side*200,z=curvedRoadZ(x);
  const slope=(12/39)*Math.cos(x/39),gap=Math.atan2(side*slope,-side);
  const rim=(a,r)=>({x:x+Math.cos(a)*r,z:z-Math.sin(a)*r});
  for(let i=0;i<24;i++){
    const a=gap+.9+i*(Math.PI*2-1.8)/24,b=a+(Math.PI*2-1.8)/24;
    pavingPanel(footprint([rim(a,9.3),rim(b,9.3),rim(b,12),rim(a,12)]),.23,.01,'#d9d2b9');
  }
  streetMasks.push({...footprint(Array.from({length:16},(_,i)=>({x:x+9.3*Math.sin(i*Math.PI/8),z:z+9.3*Math.cos(i*Math.PI/8)}))),height:.06});
  const roadEnd=new THREE.Mesh(new THREE.CylinderGeometry(9.3,9.3,.04,16),material('#60767b'));roadEnd.position.set(x,.04,z);roadEnd.receiveShadow=true;scene.add(roadEnd);registerSurfaceMesh(roadEnd);
}
block(0,-.01,COAST_Z,260,.12,14,'#60767b');
for(const side of [-1,1])for(let i=0;i<4;i++){
  const endpoints=[-130,...roads,130],start=endpoints[i]+11,end=endpoints[i+1]-11;
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
    if(z===COAST_Z)block(road,.045,z,14.1,.025,14.1,'#60767b');
    for(let i=-3;i<=3;i++){
      block(road+i*1.7,.072,z===COAST_Z?z+8.8:curvedRoadZ(road+i*1.7)-8.8,.85,.025,2.6,'#f8f4de');
      block(road-side*8.8,.072,(z===COAST_Z?z:curvedRoadZ(road-side*8.8))+i*1.7,2.6,.025,.85,'#f8f4de');
    }
  }
}

// Máscara única para reservar todas as vias antes de posicionar vegetação.
function isPaved(x,z,margin=0){
  if(districtSurface(x,z,margin)!==null)return true;
  const width=11+margin;
  if(roads.some(r=>(Math.abs(x-r)<width&&z>=-131-width&&z<=123+width)||(Math.abs(z-horizontalRoadZ(x,r))<width&&Math.abs(x)<=130+margin)))return true;
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
  for(let i=0;i<pts.length-1;i++){
    const a=i*2,b=a+1,c=a+2,d=a+3;
    const shape=footprint([a,b,d,c].map(n=>({x:vertices[3*n],z:vertices[3*n+2]})));
    if(sidewalk)pavingPanel(shape,y,.01,color);
    else{streetMasks.push({...shape,height:y});indices.push(a,b,c,c,b,d);}
  }
  if(sidewalk)return;
  const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));geometry.setIndex(indices);geometry.computeVertexNormals();
  const mesh=new THREE.Mesh(geometry,material(color));mesh.receiveShadow=true;scene.add(mesh);
  registerSurfaceMesh(mesh);
}
function roadSign(text,x,z,heading){
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=heading;scene.add(g);
  part(g,0,1.55,0,.13,3.1,.13,'#677b73');
  solid(x,z,.13,.13);
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
  block(area.x,area.top/2+.005,area.z,area.w,area.top,area.d,['parking','driveway'].includes(area.kind)?'#82918a':'#d9d2b9');
  if(area.kind==='path')for(let x=area.x-area.w/2+3;x<area.x+area.w/2;x+=3)block(x,area.top+.012,area.z,.04,.02,area.d,'#bcbdae');
}
// A faixa atravessa cada ramo no sentido perpendicular à própria pista.
const diagonalApproaches=[];
for(const road of districtRoads){
  for(let i=1;i<road.points.length;i++){
    const a=road.points[i-1],b=road.points[i],x=Math.sign(a.x)*140;
    if((a.x-x)*(b.x-x)>0||a.x===b.x)continue;
    const dx=b.x-a.x,dz=b.z-a.z,len=Math.hypot(dx,dz),z=a.z+(x-a.x)*dz/dx;
    for(let stripe=-3;stripe<=3;stripe++)block(x-dz/len*stripe*1.45,.075,z+dx/len*stripe*1.45,2.6,.025,.8,'#f8f4de',-Math.atan2(dz,dx));
    const inbound=-Math.sign(x*dx);
    if(road.oneWay&&inbound<0)continue;
    const fx=dx/len*inbound,fz=dz/len*inbound,lane=road.oneWay?0:2.75;
    diagonalApproaches.push({ring:Math.sign(x)*RING_X,row:roads.reduce((prev,r)=>Math.abs(r-z)<Math.abs(prev-z)?r:prev),
      x:x-fx*2.05+fz*lane,z:z-fz*2.05-fx*lane,fx,fz,width:road.oneWay?10.35:4.9});
  }
}
const acaciaBays=[-171,-166,-161,-156,-151],bayAngle=.28;
for(const x of acaciaBays){
  for(const s of [-1,1])block(x+s*1.89*Math.cos(bayAngle),.085,-103-s*1.89*Math.sin(bayAngle),.075,.025,6.4,'#f8f4de',bayAngle);
  block(x-3.2*Math.sin(bayAngle),.085,-103-3.2*Math.cos(bayAngle),3.79,.025,.075,'#f8f4de',bayAngle);
}
for(const x of [-165.4,-156.6]){block(x,1.55,-110.1,.17,3.1,.19,'#54655c');solid(x,-110.1,.20,.22);}
block(-161,3.15,-110.1,10.0,1.88,.30,'#354e4a');
sign('MOTORES ACÁCIAS',-161,3.17,-109.935,6.9,{background:'#35554d',ink:'#f9e9c4',border:false});
// Terreno vago cercado, com passagem pelo passeio e vegetação esparsa.
block(164,.035,-103,30,.09,20,'#b9b38f');
for(const z of [-113,-93]){block(164,.55,z,30,.12,.12,'#aa9879');for(let x=149;x<=179;x+=5)block(x,.4,z,.14,.8,.14,'#9b896e');}
for(const [start,end] of [[-113,-105],[-101,-93]]){block(149,.55,(start+end)/2,.12,.12,end-start,'#aa9879');for(let z=start;z<=end;z+=4)block(149,.4,z,.14,.8,.14,'#9b896e');}
block(179,.55,-103,.12,.12,20,'#aa9879');for(let z=-113;z<=-93;z+=5)block(179,.4,z,.14,.8,.14,'#9b896e');
for(const [x,z,w,d] of [[164,-113,30,.16],[164,-93,30,.16],[179,-103,.16,20],[149,-109,.16,8],[149,-97,.16,8]]){
  solid(x,z,w,d).vaultTop=.80;
}
sign('TERRENO',150,2,-108,3,{background:'#897a5e',rotation:Math.PI/2});
for(const [x,z] of [[157,-110],[171,-108],[162,-97],[175,-97]]){block(x,.22,z,.09,.38,.09,'#7d9a69');block(x+.15,.16,z+.1,.08,.26,.08,'#89a674');}

// Each intersection owns its phase. All lenses of the same color share materials.
const junctions=[],stopLines=[],stopLine=10.8;
const lenses=['#fb493f','#ffbf37','#48dd87'].map((color,i)=>({
  lit:new THREE.MeshStandardMaterial({color,emissive:color,emissiveIntensity:1.15,roughness:.5}),
  dark:material(['#452827','#493d25','#233d32'][i])
}));
function junctionAt(x,z){
  const junction={x,z,offset:0,amberUntil:0,reopenAt:0,phase:null,heads:[]};
  junctions.push(junction);
  function approach(axis,x,z,fx,fz,width){
    block(x,.07,z,.24,.02,width,'#fbf4df',-Math.atan2(fz,fx));
    stopLines.push({axis,junction,x,z,fx,fz,half:width/2});
  }
  for(const side of [-1,1]){
    if(z!==COAST_Z||side===1)approach('ns',x-side*3.3,z+side*stopLine,0,-side,5.9);
    if(Math.abs(x)!==RING_X||side!==Math.sign(x))approach('ew',x+side*stopLine,z+side*3.3,-side,0,5.9);
  }
  for(const a of diagonalApproaches)if(a.ring===x&&a.row===z)approach('ew',a.x,a.z,a.fx,a.fz,a.width);
  return junction;
}
function trafficLight(junction,x,z,axis,rotation) {
  const group = new THREE.Group();group.position.set(x,.23,z);group.rotation.y=rotation;scene.add(group);
  const collider=solid(x,z,.24,.24),cameraObstacle={x,z,w:1,d:1,bottom:.2,top:5.9};
  cameraObstacles.push(cameraObstacle);
  destructibles.push({group,collider,cameraObstacle,type:'signal',junction,active:true});
  part(group, 0, 2.5, 0, .24, 5, .24, '#465756');
  part(group,0,1.48,.17,.32,.45,.15,'#344c4b');
  part(group,0,1.48,.26,.16,.16,.06,'#e3b459');
  part(group, 0, 4.75, 0, .86, 2.05, .52, '#354744');
  for(const face of [-1,1]){
    const lamps=[];
    for(let i=0;i<3;i++){
      const y=5.37-i*.61;
      part(group,0,y+.26,face*.33,.62,.07,.24,'#303f3d');
      lamps.push(part(group,0,y,face*.28,.47,.44,.09,lenses[i].dark));
    }
    junction.heads.push({axis,lamps});
  }
}
for (const x of roads) for (const z of roads) {
  const j=junctionAt(x,z);
  trafficLight(j,x-9,z+9,'ns',0);trafficLight(j,x+9,z-9,'ns',0);
  trafficLight(j,x-9,z-9,'ew',Math.PI/2);trafficLight(j,x+9,z+9,'ew',Math.PI/2);
}
for(const side of [-1,1])for(const z of roads){
  const j=junctionAt(side*130,z);
  trafficLight(j,j.x-side*9,z+9,'ns',0);trafficLight(j,j.x-side*9,z-9,'ew',Math.PI/2);
}
for(const x of roads){
  const j=junctionAt(x,COAST_Z);
  trafficLight(j,x-9,COAST_Z+9,'ns',0);trafficLight(j,x+9,COAST_Z+9,'ew',Math.PI/2);
}
function updateSignals(time) {
  for(const j of junctions){
    const phase=signalPhase(time,j);
    if(j.phase===phase)continue;
    j.phase=phase;
    for(const {axis,lamps} of j.heads)lamps.forEach((lamp,i)=>{lamp.material=i===phase[axis]?lenses[i].lit:lenses[i].dark;});
  }
}

finishPaving();

// Mobiliário urbano todo em blocos, com variações de cor e tamanho.
function tree(x,z,scale=1, variant=0) {
  const base=groundHeight(x,z);
  cameraObstacles.push({x,z,w:4.4*scale,d:4.2*scale,bottom:base+3*scale,top:base+7.1*scale});
  cameraObstacles.push({x,z,w:.7*scale,d:.7*scale,bottom:base,top:base+4*scale});
  block(x,base+2*scale,z,.7*scale,4*scale,.7*scale,'#896c4e');solid(x,z,.7*scale,.7*scale);
  const seed=Math.abs(Math.round(x*3+z*7)),angle=(seed%15)*.19;
  const flowering=variant&&seed%4===0;
  const greens=flowering?['#78895a','#cb9b55','#e7b763']:variant?['#678e65','#7da972','#8fb67a']:['#477960','#5a906d','#76a77c'];
  block(x,base+4.45*scale,z,3.1*scale,2.3*scale,2.9*scale,greens[0],angle);
  for(let i=0;i<3;i++){
    const a=angle+i*2.09,dx=Math.cos(a)*scale,dz=Math.sin(a)*scale;
    block(x+dx*.58,base+3.49*scale,z+dz*.58,1.45*scale,.35*scale,.39*scale,'#896c4e',-a,z);
    block(x+dx*1.20,base+(4.85+i*.28)*scale,z+dz*1.10,1.95*scale,(1.70+i*.1)*scale,1.97*scale,greens[i],-a*.4,z);
  }
  block(x-.18*scale,base+6.07*scale,z,2.26*scale,1.38*scale,2.09*scale,greens[1],angle);
}
function palm(x,z,scale=1) {
  const base=groundHeight(x,z);
  cameraObstacles.push({x,z,w:5.5*scale,d:5.5*scale,bottom:base+5.25*scale,top:base+6.2*scale});
  cameraObstacles.push({x,z,w:.65*scale,d:.65*scale,bottom:base,top:base+5.8*scale});
  block(x,base+2.9*scale,z,.65*scale,5.8*scale,.65*scale,'#ad8b60');solid(x,z,.65*scale,.65*scale);
  block(x,base+5.8*scale,z,1.8*scale,.4*scale,1.8*scale,'#4d8b68');
  for(let i=0;i<7;i++){
    const a=i*Math.PI*2/7+x*.035,dx=Math.cos(a)*scale,dz=Math.sin(a)*scale;
    block(x+dx*.95,base+5.91*scale,z+dz*.95,1.9*scale,.26*scale,.75*scale,i%2?'#5f9d73':'#4c8a68',-a,z);
    block(x+dx*2.02,base+5.69*scale,z+dz*2.02,1.15*scale,.24*scale,.60*scale,'#72a978',-a,z);
    block(x+dx*2.75,base+5.39*scale,z+dz*2.75,.64*scale,.32*scale,.34*scale,'#78ad79',-a);
  }
}

function bench(x,z,rot=0) {
  const g = new THREE.Group(); g.position.set(x,groundHeight(x,z),z); g.rotation.y=rot; scene.add(g);
  const c=Math.abs(Math.cos(rot)),s=Math.abs(Math.sin(rot)),base=g.position.y;
  const collider=solid(x,z,2.9*c+.94*s,2.9*s+.94*c);
  collider.bottom=base;collider.supportTop=base+.82;
  const back=solid(x-Math.sin(rot)*.42,z-Math.cos(rot)*.42,2.9*c+.14*s,2.9*s+.14*c);
  back.bottom=base;back.supportTop=base+1.575;
  const occupied=[[-52,24],[-20,24],[-52,48],[-20,48],[94,97],[114,111]].some(([bx,bz])=>Math.abs(x-bx)<.1&&Math.abs(z-bz)<.1);
  const benchState={group:g,collider,x,z,rotation:rot,occupied,width:2.9,depth:1.1,seatTop:.82,openEnds:true};benches.push(benchState);
  if(!occupied)destructibles.push({group:g,collider,backCollider:back,type:'bench',active:true,bench:benchState});
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
  const base=groundHeight(x,z),g=new THREE.Group();g.position.set(x,base,z);g.rotation.y=rot;scene.add(g);
  part(g,0,2.9,0,.2,5.8,.2,'#49615c');part(g,0,5.82,0,1,.14,.17,'#49615c');part(g,.4,5.6,0,.45,.4,.45,'#ffefba');
  const lamp={x,y:base+5.5,z,active:true};streetLamps.push(lamp);
  const cameraObstacle={x,z,w:.22,d:.22,bottom:base,top:base+5.86};
  cameraObstacles.push(cameraObstacle);
  destructibles.push({group:g,collider:solid(x,z,.22,.22),type:'lamp',active:true,lamp,cameraObstacle});
}
function planter(x,z) {
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  part(g,0,.45,0,2.1,.9,1.1,'#cd9c72');part(g,0,.95,0,1.85,.12,.86,'#688b61');
  for(let i=0;i<5;i++)part(g,-.7+i*.36,1.2,0,.2,.45,.2,i%2?'#e9a185':'#f1d78b');
  destructibles.push({group:g,collider:solid(x,z,2.1,1.1),type:'planter',active:true});
}
function busStop(x,z,rot=0) {
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=rot;scene.add(g);
  cityFixtures.push(g);
  const c=Math.abs(Math.cos(rot)),s=Math.abs(Math.sin(rot));
  cameraObstacles.push({x,z,w:c*6+s*2.6,d:s*6+c*2.6,bottom:g.position.y+2.86,top:g.position.y+3.3});
  cameraObstacles.push({x:x-Math.sin(rot)*1.1,z:z-Math.cos(rot)*1.1,w:c*6+s*.12,d:s*6+c*.12,bottom:g.position.y,top:g.position.y+3});
  part(g,0,3,0,6,.28,2.6,'#578780');part(g,0,1.5,-1.1,6,3,.12,'#a3cbd0');
  const cs=Math.cos(rot),sn=Math.sin(rot);
  function shelterSolid(lx,lz,w,d){
    return solid(x+cs*lx+sn*lz,z-sn*lx+cs*lz,c*w+s*d,s*w+c*d);
  }
  for(const end of [-1,1])for(const edge of [-1,1]){
    part(g,end*2.8,1.5,edge*1.05,.15,3,.15,'#53756e');
    shelterSolid(end*2.8,edge*1.05,.15,.15);
  }
  part(g,0,.7,-.3,3.7,.2,.7,'#bb895e');part(g,0,.35,-.3,.18,.7,.65,'#53756e');
  shelterSolid(0,-1.1,6,.12);
  shelterSolid(0,-.3,3.7,.7).vaultTop=g.position.y+.8;
}
function hydrant(x,z){const previous=buildingHeightOffset;buildingHeightOffset=groundHeight(x,z);solid(x,z,.5,.5);block(x,.55,z,.5,1.1,.5,'#d86e55');block(x,1.15,z,.75,.2,.75,'#e57d5e');block(x,.65,z,.9,.22,.25,'#d86e55');buildingHeightOffset=previous;}
function bikeRack(x,z) {
  const previous=buildingHeightOffset;buildingHeightOffset=groundHeight(x,z);
  for(let i=0;i<3;i++){
    const bx=x+i*1.4;
    block(bx,.55,z,.09,1.1,.1,'#6a7b70');block(bx+.9,.55,z,.09,1.1,.1,'#6a7b70');
    block(bx+.45,1.06,z,1,.1,.1,'#6a7b70');
    solid(bx+.45,z,1,.1).vaultTop=buildingHeightOffset+1.11;
  }
  buildingHeightOffset=previous;
}

const buildingColors = ['#e8b494','#e8d5ab','#a6c8bd','#dbaba0','#bdcad2','#e6cfb7'];
const roofColors = ['#d27f67','#6c9290','#b39c83','#cba27d'];
const glassMaterial = new THREE.MeshStandardMaterial({ color:'#9bd1d2', transparent:true, opacity:.34, roughness:.22, metalness:.08, depthWrite:false });
function doorAt(x,z,w,automatic=false) {
  const hinge=new THREE.Group(); hinge.position.set(x+w/2,0,z+.24); scene.add(hinge);
  part(hinge,-w/2,.65,0,w,1.3,.16,'#477c75');
  part(hinge,-w/2,3.1,0,w,.4,.16,'#477c75');
  for(const edge of [-.075,-w+.075])part(hinge,edge,2.1,0,.15,1.6,.16,'#477c75');
  part(hinge,-w/2,2.1,0,w-.3,1.6,.04,glassMaterial);
  for(const face of [-1,1])part(hinge,-w+.33,1.35,face*.13,.13,.18,.10,'#f0d18e');
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
  // A laje fecha o térreo inclusive nas casas que têm somente um pavimento.
  const pieces=new Map();
  function upperPart(px,py,pz,pw,ph,pd,shade){
    if(!pieces.has(shade))pieces.set(shade,[]);
    dummy.position.set(px,py,pz);dummy.rotation.set(0,0,0);dummy.scale.set(pw,ph,pd);dummy.updateMatrix();
    pieces.get(shade).push({matrix:dummy.matrix.clone(),shadow:pw*ph*pd>.12});
  }
  const h=5.2+floors*3.8;
  upperPart(0,5.08,0,w,.16,d,'#efe5cf');
  if(floors>0)upperPart(0,(5.16+h)/2,0,w,h-5.16,d,color);
  for(let level=0;level<floors;level++){
    const yy=7.15+level*3.8;
    for(let dx=-w/2+2.2;dx<w/2-1;dx+=3.5){
      upperPart(dx,yy,d/2+.09,2,2.15,.15,'#618e90');upperPart(dx,yy,d/2+.19,.1,2.15,.07,'#efe4cd');
      upperPart(dx,yy-1.14,d/2+.22,2.3,.13,.25,'#f4e8ce');
      upperPart(dx,yy,-d/2-.09,2,2.15,.15,'#618e90');upperPart(dx,yy,-d/2-.19,.1,2.15,.07,'#efe4cd');
      upperPart(dx,yy-1.14,-d/2-.22,2.3,.13,.25,'#f4e8ce');
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
  const ceiling=solid(x,z,w,d);
  ceiling.bottom=5;ceiling.supportTop=h+.7;
  cameraObstacles.push({x,z,w,d,bottom:5,top:h},
    {x,z,w:w+.8,d:d+.8,bottom:h-.125,top:h+.325},
    {x,z,w:w-.7,d:d-.7,bottom:h+.3,top:h+.7});
  for(const [shade,instances] of pieces){
    const mesh=new THREE.InstancedMesh(cube,material(shade),instances.length);
    instances.forEach((item,i)=>mesh.setMatrixAt(i,item.matrix));mesh.instanceMatrix.needsUpdate=true;
    mesh.castShadow=instances.some(item=>item.shadow);mesh.receiveShadow=true;mesh.frustumCulled=false;g.add(mesh);
  }
}
function closedBuilding(x,z,w,d,color,floors) {
  const height=5+floors*3.8;
  cameraObstacles.push({x,z,w:w+.3,d:d+.3,bottom:0,top:height+1.4});
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
  residentialFacade({block,sign},x,z,w,d,height);
}
function enterableBuilding(x,z,w,d,color,kind,name,floors=1) {
  const place={x,z,w,d,kind,name};places.push(place);
  const front=z+d/2, back=z-d/2, doorW=3.04, side=(w-doorW)/2;
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
  for(const side of [-1,1])for(const dz of [-d*.24,d*.24]){
    block(x+side*(w/2+.23),2.55,z+dz,.08,1.9,2.6,'#7fa7a7');
    block(x+side*(w/2+.3),2.55,z+dz,.06,2.1,.1,'#efe0c7');
    for(const y of [1.5,3.6])block(x+side*(w/2+.3),y,z+dz,.06,.12,2.9,'#efe0c7');
  }
  const facade=new THREE.Group();facade.position.set(x,0,front);scene.add(facade);cityFixtures.push(facade);place.facade=facade;
  // The same frame, pane and sill are visible from the sidewalk and the room.
  const domestic=kind==='home'||kind==='residence',sill=domestic?1.68:1.12,windowTop=domestic?3.25:4.03;
  const jambWidth=.15,masonry=side-jambWidth;
  const glassWidth=domestic?Math.min(3.05,masonry*.48):masonry-1,glassHeight=windowTop-sill;
  const pier=(masonry-glassWidth)/2,windowY=(sill+windowTop)/2;
  for(const s of [-1,1]){
    const center=s*(doorW/2+jambWidth+masonry/2);
    part(facade,center,sill/2,0,masonry,sill,.4,color);
    part(facade,center,(5.2+windowTop)/2,0,masonry,5.2-windowTop,.4,color);
    for(const edge of [-1,1])part(facade,center+edge*(glassWidth+pier)/2,windowY,0,pier,glassHeight,.4,color);
    part(facade,center,windowY,0,glassWidth,glassHeight,.10,domestic?'#86a9a6':glassMaterial);
    part(facade,center,windowY,0,.11,glassHeight,.19,'#e4dfca');
    if(domestic)part(facade,center,windowY+.15,0,glassWidth,.10,.19,'#e4dfca');
    part(facade,center,sill,0,glassWidth+.16,.12,.54,'#e8d9bb');
    part(facade,center,windowTop,0,glassWidth+.12,.10,.43,'#e8d9bb');
    part(facade,center,.31,-.22,masonry,.22,.10,'#b99b78');
    part(facade,s*(doorW+jambWidth)/2,1.65,0,jambWidth,3.3,.50,'#ebdfc6');
  }
  part(facade,0,4.32,0,doorW+2*jambWidth,1.76,.4,color);
  part(facade,0,3.37,0,doorW+2*jambWidth,.14,.50,'#ebdfc6');
  const floor=kind==='hospital'?.205:.18,landingTop=Math.max(.235,groundHeight(x,front+.8))+.016;
  block(x,floor-.036,front,doorW,.10,.46,'#d5d2bd');
  block(x,landingTop-.12,front+.69,doorW,.24,.92,'#d9d0b8');
  for(const wall of [solid(x-w/2,z,.4,d),solid(x+w/2,z,.4,d),solid(x,back,w,.4),
    solid(x-(doorW/2+side/2),front,side,.4),solid(x+(doorW/2+side/2),front,side,.4)]){
    wall.opaque=true;
    cameraObstacles.push({x:wall.x,z:wall.z,w:wall.w,d:wall.d,bottom:0,top:5.2});
  }
  const lintel=solid(x,front,doorW,.5);lintel.bottom=3.3;lintel.supportTop=5.2;
  cameraObstacles.push({x,z:front,w:doorW,d:.5,bottom:3.3,top:5.2});
  place.door=doorAt(x,front,doorW,kind==='hospital'||kind==='station');
  upperFloors(place,color,floors);
  const awning=domestic?'#aa896c':kind==='police'?'#54809b':kind==='fire'?'#c76350':kind==='hospital'?'#e6e5d6':kind==='market'?'#e7ac79':kind==='gallery'?'#668f82':kind==='book'?'#8b9eb8':kind==='flower'?'#a6be8e':'#df907a';
  const awningWidth=domestic?doorW+1.1:w+.4,awningDepth=domestic?1.12:1.9,awningY=domestic?3.92:4.15;
  part(facade,0,awningY,awningDepth/2,awningWidth,.18,awningDepth,awning);
  cameraObstacles.push({x,z:front+awningDepth/2,w:awningWidth,d:awningDepth,bottom:awningY-(domestic?.09:.16),top:awningY+.09});
  if(!domestic)for(let k=-w/2+.4;k<w/2;k+=1.6)part(facade,k,4.06,1.6,.8,.14,.65,'#f8e6c2');
  place.outsideSign=sign(name,x,domestic?4.55:5.75,front+(domestic?.23:.45),domestic?3.5:Math.min(w-1,11.5),{background:kind==='hospital'?'#d46155':kind==='police'?'#315a78':kind==='fire'?'#ad5044':'#315f58'});
  // Garagem visual alinhada à vaga dos veículos; a entrada de pessoas segue livre.
  if(kind==='police'||kind==='fire'){
    part(facade,-6,1.75,.22,5.4,3.5,.14,kind==='police'?'#7797a4':'#c27b65');
    for(let y=.55;y<3.2;y+=.55)part(facade,-6,y,.32,5.2,.06,.06,'#e7dcc4');
    const garageSign=sign(kind==='police'?'VIATURA':'GARAGEM',x-6,3.6,front+.36,4,{background:kind==='police'?'#416d8b':'#a45145'});
    garageSign.position.set(-6,3.6,.36);facade.add(garageSign);
  }
  // Plantas e pequenos detalhes junto à entrada.
  for(const side of [-1,1]) {
    const px=x+side*3.5,pz=front+1.05,base=groundHeight(px,pz);
    block(px,base+.35,pz,.7,.7,.7,'#bd8364');block(px,base+1.05,pz,.85,.85,.85,'#72a477');solid(px,pz,.7,.7);
  }
  if(kind==='market'||kind==='flower'){
    for(const side of [-1,1]){
      const cx=x+side*w*.35,cz=front+1.1,base=groundHeight(cx,cz),flower=kind==='flower';
      block(cx,base+.44,cz,2.72,.82,1.04,'#9d7351');solid(cx,cz,2.72,1.04);
      for(let n=-1;n<=1;n++){
        const bx=cx+n*.85;
        block(bx,base+.93,cz,.8,.23,.92,'#d5aa74');
        for(let j=0;j<4;j++){
          const px=bx+(j%2-.5)*.31,pz=cz+(Math.floor(j/2)-.5)*.30;
          if(flower)block(px,base+1.19,pz,.065,.36,.065,'#648458');
          block(px,base+(flower?1.39:1.09),pz,.23,flower?.17:.22,.24,(flower?['#e59f9c','#d9bd63','#ad91bc']:['#c4664d','#e5ad57','#829450'])[n+1],j*.3);
        }
      }
    }
  }
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
  {x:36,z:104,type:'armory',name:'OFICINA FAROL',color:'#a5b8a9'},
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
  else if(!['park','park2','station'].includes(type)) enterableBuilding(x,z,site.w??23,site.d??21,type==='armory'?pick(['#a5b8a9','#a8b6ac','#a2b9ad']):site.color,type,site.name,site.floors??(type==='hospital'?2:1+Math.floor(random()*2)));
}
// Casas baixas, jardins e uma praça com lago ampliam a área explorável.
function gardenHouse(x,z,color){
  cameraObstacles.push({x,z,w:18.5,d:14.5,bottom:0,top:8.1});
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
    solid(x+side*10,z-6,.14,5).vaultTop=.955;
    for(let dz=-8;dz<=-4;dz+=2){
      block(x+side*10,.5,z+dz,.25,1,.25,'#eee2c6');
      solid(x+side*10,z+dz,.25,.25).vaultTop=1;
    }
  }
}
for(const x of [-180,-140,-100,-60,60,100,140,180]){
  const z=curvedRoadZ(x)+23;gardenHouse(x,z,pick(buildingColors));
  const treeX=x+18,treeZ=curvedRoadZ(treeX)+14;tree(treeX,treeZ,.85,1);
  if(x%80===20)bin(x+10,curvedRoadZ(x)+13);
}
const pond=new THREE.Mesh(new THREE.CylinderGeometry(1,1,.18,14),material('#7dc2c4'));
pond.position.set(0,.12,curvedRoadZ(0)+27);pond.scale.set(12,1,6);pond.receiveShadow=true;scene.add(pond);
// Narrow horizontal strips follow the actual fourteen-sided water polygon.
// Inscribed widths keep the dry corners free (rather than a bounding box).
const pondOutline=Array.from({length:14},(_,i)=>({x:12*Math.sin(i*Math.PI/7),z:6*Math.cos(i*Math.PI/7)}));
function pondHalfWidth(z){
  const hits=[];
  for(let i=0;i<14;i++){
    const a=pondOutline[i],b=pondOutline[(i+1)%14];
    if(z>=Math.min(a.z,b.z)-1e-9&&z<=Math.max(a.z,b.z)+1e-9&&Math.abs(a.z-b.z)>1e-9)
      hits.push(Math.abs(a.x+(b.x-a.x)*(z-a.z)/(b.z-a.z)));
  }
  return Math.max(0,...hits);
}
for(let i=0;i<120;i++){
  const low=-6+i*.1,high=low+.1,width=2*Math.min(pondHalfWidth(low),pondHalfWidth(high));
  if(width>0)solid(0,pond.position.z+(low+high)/2,width,.1);
}
for(const x of [-18,18])for(const z of [curvedRoadZ(0)+18,curvedRoadZ(0)+36]){tree(x,z,.95,1);bench(x,z+(z<curvedRoadZ(0)+27?-3:3),z<curvedRoadZ(0)+27?0:Math.PI);}
sign('BAIRRO JARDIM',0,3.5,curvedRoadZ(0)+13,8,{background:'#558273',rotation:Math.PI});
for(const side of [-1,1]){
  bench(overlook.x+side*3.7,overlook.z+.8,Math.PI);
  block(overlook.x+side*6.4,.5,overlook.z,.20,1,4.8,'#8c8068');
  solid(overlook.x+side*6.4,overlook.z,.20,4.8).vaultTop=1;
}
block(overlook.x-5.7,.77,overlook.z-2.1,.18,1.54,.18,'#8c8068');
solid(overlook.x-5.7,overlook.z-2.1,.18,.18);
block(overlook.x-5.7,1.82,overlook.z-2.1,3.75,1,.14,'#8c8068');
for(const side of [-1,1])sign('MIRANTE DAS COLINAS',overlook.x-5.7,1.82,overlook.z-2.1+side*.078,3.6,
  {rotation:side<0?Math.PI:0,background:'#476e61'});

// A cruz do hospital aparece tanto da praça como das avenidas.
const hospitalFacade=places.find(p=>p.kind==='hospital').facade;
part(hospitalFacade,0,7,.16,1.1,4,.25,'#dc695b');part(hospitalFacade,0,7,.18,4,1.1,.28,'#dc695b');
part(hospitalFacade,11,1.9,.5,1.8,3.8,.15,'#ca6d5d');part(hospitalFacade,11,2.1,.65,.38,1.7,.1,'#fff1db');part(hospitalFacade,11,2.1,.67,1.3,.38,.1,'#fff1db');

function pump(x,z) {
  const previous=buildingHeightOffset;buildingHeightOffset=groundHeight(x,z);
  block(x,.15,z,2.5,.3,3.5,'#d5d2bd');block(x,1.15,z,1.2,2.1,1.1,'#e8e8dc');
  block(x,2.05,z,1.3,.35,1.2,'#d77860');block(x,1.45,z+.57,.8,.55,.05,'#547a78');
  block(x+.74,1,z,.12,1.7,.12,'#4e5b55');block(x+.74,.4,z+.35,.12,.12,.75,'#4e5b55');
  block(x-.3,.75,z+.58,.3,.18,.04,'#b5bfb0');solid(x,z,1.4,1.4);
  buildingHeightOffset=previous;
}
// Posto integrado ao bairro, com loja, bombas e cobertura.
block(104,.13,-27.7,30,.25,34.4,'#ded8c6');
const station=enterableBuilding(104,-50,22,10,'#ecdfc5','station','POSTO SOL',1);
const streetArt=addStreetArt({THREE,scene,places});
for(const x of [98,110])for(const z of [-35,-26])pump(x,z);
for(const x of [93,115])for(const z of [-40,-21]){
  block(x,3,z,.5,6,.5,'#d1d6c7');solid(x,z,.5,.5);
  cameraObstacles.push({x,z,w:.5,d:.5,bottom:0,top:6});
}
for(const x of [98,110])for(const z of [-35,-26])block(x,5.9,z,2,.12,1.2,'#ffefba');
block(104,6.15,-30.5,25,.45,22,'#e8e8d6');block(104,6.55,-30.5,25,.45,22,'#d9765e');
block(104,6.86,-30.5,24,.17,21,'#f2dcab');
cameraObstacles.push({x:104,z:-30.5,w:25,d:22,bottom:5.88,top:6.95});
sign('POSTO SOL',104,6.65,-19.3,10,{background:'#c96452'});
block(91,2.5,-16,1,5,1,'#677b72');block(91,4.8,-16,4,2.7,.7,'#d9765e');
solid(91,-16,1,1);
cameraObstacles.push({x:91,z:-16,w:1,d:1,bottom:0,top:5},{x:91,z:-16,w:4,d:.7,bottom:3.45,top:6.15});
sign('SOL',91,5,-15.59,3.5,{background:'#cf6c57'});
block(91,3.3,-15.57,2.8,.36,.05,'#f7e7be');

// Praças e calçadão, acessíveis a pé.
function plaza() {
  const x=-36,z=36;
  block(x,.035,z,52,.08,52,'#a2c18b');
  block(x,.14,z,7,.18,52,'#e5d9bf');block(x,.15,z,52,.18,7,'#e5d9bf');
  // Duas muretas delimitam o gramado e deixam livre o passeio até a avenida.
  for(const wallX of [x-10,x+10]){
    const wallZ=z+24,base=.075;
    block(wallX,base+.49,wallZ,6.2,.98,.44,'#c4bb9f');
    block(wallX,base+1.05,wallZ,6.34,.14,.54,'#e9ddc7');
    solid(wallX,wallZ,6.34,.54).vaultTop=base+1.12;
  }
  for(let p=-24;p<=24;p+=3){block(x+p,.25,z,.045,.02,7,'#c3bea8');block(x,.25,z+p,7,.02,.045,'#c3bea8');}
  // Fonte em degraus quadrados e pequenos jatos facetados.
  block(x,.35,z,9.4,.7,9.4,'#e9ddc7');solid(x,z,9.4,9.4);block(x,.76,z,8.6,.25,8.6,'#a7d2ce');block(x,.88,z,7.8,.08,7.8,'#72bfca');
  block(x,1.7,z,1,1.7,1,'#e6d9be');block(x,2.65,z,2.2,.24,2.2,'#e6d9be');
  for(const dx of [-1,1])for(const dz of [-1,1])block(x+dx*2.3,1.05,z+dz*2.3,.28,.4,.28,'#c8e5df');
  for(const dx of [-16,16])for(const dz of [-16,16]){tree(x+dx,z+dz,1.1,1);bench(x+dx,z+dz+(dz>0?-4:4),dz>0?Math.PI:0);bin(x+dx+3,z+dz+2);}
  for(const side of [-1,1]){planter(x+side*6,z+14);planter(x+side*6,z-14);lamp(x+side*6,z+7);lamp(x+side*6,z-7);}
  block(-57,.18,53,5,.3,3,'#d8c5a4');block(-57,1.4,53,3.5,2.5,1.6,'#e7c896');block(-57,2.9,53,4.2,.4,2,'#d77d68');
  solid(-57,53,3.5,1.6).opaque=true;
  cameraObstacles.push({x:-57,z:53,w:4.2,d:2,bottom:.15,top:3.1});
  block(-57,1.76,53.83,2.91,1.03,.10,'#567974');
  block(-57,1.23,53.91,3.25,.13,.36,'#dbc19a');
  for(let i=0;i<3;i++)block(-57.9+i*.85,1.43,53.93,.63,.28,.23,['#ab7562','#ddab60','#b79bac'][i]);
  sign('SORVETES',-57,2.6,53.85,3.5,{background:'#ca7865'});
}
function smallPlaza() {
  block(104,.04,104,29,.1,29,'#a6c58e');
  block(104,.14,104,4,.18,29,'#e3d6ba');block(104,.15,104,29,.18,4,'#e3d6ba');
  for(const x of [94,114])for(const z of [94,114]){tree(x,z,.9);bench(x,z+(z<104?3:-3),z<104?0:Math.PI);}
  block(104,.25,97,7,.25,4,'#e9d29d');block(104,.45,97,6,.22,3,'#e7bd77');
  for(const x of [101,107]){block(x,1.6,97,.18,3.2,.18,'#d87e65');block(x,3.15,97,2.7,.18,.18,'#d87e65');}
  for(const x of [101,107])solid(x,97,.18,.18);
  // Sand is a walkable raised surface; only the actual posts obstruct it.
  renderedGround.addRectangle({x:104,z:97,w:7,d:4,top:.375});
  renderedGround.addRectangle({x:104,z:97,w:6,d:3,top:.56});
  block(104,1,112,3,2,1.3,'#78a89a');block(104,2.2,112,3.5,.4,1.8,'#e5b178');
  solid(104,112,3,1.3);
  sign('PRAÇA',104,2.1,112.7,2.7,{background:'#57877b'});
  planter(108,99);bin(99,106);
}
plaza();smallPlaza();
installDistrictGuide({THREE,scene,part,solid,groundHeight,cameraObstacles,servicePoints});
const harbor=installHarbor({THREE,scene,block,part,solid,sign,groundHeight,renderedGround,cameraObstacles,servicePoints,isOccupied:harborOccupied});
const streetCinema=installStreetCinema({THREE,scene,block,part,solid,bench,sign,renderedGround,cameraObstacles,servicePoints});
const streetPerformance=installStreetPerformance({THREE,scene,part,block,solid,sign,groundHeight,renderedGround,cameraObstacles,servicePoints});
const practiceRange=installPracticeRange({THREE,scene,block,groundHeight,solid,cameraObstacles,servicePoints,sign});

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
  if(x===-35&&z===72)continue;
  bench(x+7,z+13.4,Math.PI);bin(x+10,z+13.4);
}
for(const stop of busStops){
  busStop(stop.x,stop.z,stop.rotation);
  sign(`PONTO · ${stop.name.toUpperCase()}`,stop.x,3.25,stop.z,5.4,{background:'#4c8179',rotation:stop.rotation});
  cameraObstacles.push({x:stop.x,z:stop.z,w:.12,d:5.4,bottom:2.55,top:3.95});
}
for(const road of districtRoads.filter(r=>r.oneWay))for(const t of [.18,.34,.55,.73,.85]){
  const p=road.curve.getPointAt(t),v=road.curve.getTangentAt(t),nx=-v.z,nz=v.x,side=Math.sign(p.x*nx)||1;
  const lx=p.x+nx*side*7.4,lz=p.z+nz*side*7.4,tx=p.x+nx*side*11.5,tz=p.z+nz*side*11.5;
  if(Math.abs(lx)<208&&!collides(lx,lz,.4))lamp(lx,lz);
  if(Math.abs(tx)<207&&!collides(tx,tz,1)&&!isPaved(tx,tz,1)&&!sites.some(site=>Math.abs(tx-site.x)<(site.w??23)/2+3&&Math.abs(tz-site.z)<(site.d??21)/2+3))tree(tx,tz,.75,1);
}
for(const [x,z] of [[-62.4,20],[10.4,44],[82.5,-99],[-62.4,-90]])hydrant(x,z);
bikeRack(-56,58);bikeRack(17,51);bikeRack(91,51);
// Cabines telefônicas, caixas eletrônicos e caixas de correio.
const publicPhones=[];
function phoneBooth(x,z,rotation=0){
  const g=new THREE.Group(),base=groundHeight(x,z);g.position.set(x,base,z);g.rotation.y=rotation;scene.add(g);cityFixtures.push(g);
  cameraObstacles.push({x,z,w:2,d:2,bottom:base,top:base+3.5});
  solid(x,z,rotation?1.6:1.7,rotation?1.7:1.6);
  part(g,0,1.6,0,1.7,3.2,1.6,'#d26c5d');part(g,0,1.7,.82,1.28,1.63,.10,'#729891');
  const handset=new THREE.Group();handset.position.set(-.38,1.86,.97);g.add(handset);
  part(handset,0,0,0,.12,.55,.15,'#263e42');
  for(const side of [-1,1])part(handset,0,side*.25,.05,.19,.18,.19,'#263e42');
  for(let i=0;i<9;i++)part(g,.06+(i%3)*.12,1.85-Math.floor(i/3)*.11,.90,.085,.075,.035,'#d5d9c6');
  const lamp=new THREE.MeshStandardMaterial({color:'#56796e',emissive:'#ffd380',emissiveIntensity:0});
  part(g,.18,2.13,.91,.42,.13,.05,lamp);
  publicPhones.push({type:'phone',x:x+Math.sin(rotation)*1.12,z:z+Math.cos(rotation)*1.12,handset,lamp,ringUntil:-Infinity});
  part(g,0,3.3,0,2,.3,1.9,'#e6cfaf');sign('FONE',x+Math.sin(rotation)*.88,base+3.03,z+Math.cos(rotation)*.88,1.7,{background:'#b9554c',rotation});
}
function atm(x,z){
  const previous=buildingHeightOffset;buildingHeightOffset=groundHeight(x,z);
  solid(x,z,1.55,.9);
  block(x,1.4,z,1.55,2.8,.9,'#5c8f85');block(x,1.9,z+.47,1.1,.8,.06,'#bde0d8');
  block(x,1.15,z+.48,.7,.24,.06,'#e4e1cc');block(x,.87,z+.48,.8,.1,.06,'#395d59');
  sign('CAIXA',x,2.9,z+.5,1.5,{background:'#487b70'});
  servicePoints.push({kind:'bank',x,z:z+.85,frontOnly:true,anchor:{x,z:z+.55,height:2.36},display:{x,y:buildingHeightOffset+3.05,z:z+.60}});
  buildingHeightOffset=previous;
}
phoneBooth(-58.5,57);phoneBooth(86,21,-Math.PI/2);phoneBooth(-86,-90,Math.PI/2);
const surveillance=installPublicCameras({THREE,scene,part,solid,fixtures:cityFixtures,obstacles:cameraObstacles,groundHeight});
atm(30,47.4);atm(-110,47.4);atm(111,47.4);
for(const [x,z] of [[-59.5,-44],[10.3,30],[82.3,52]]){
  const base=groundHeight(x,z);solid(x,z,.9,.86);
  block(x,base+.7,z,.75,1.4,.75,'#6995a1');block(x,base+1.45,z,.9,.16,.86,'#5b818c');block(x,base+1.1,z+.4,.4,.08,.04,'#e4e2c9');
}
// Tampas de bueiro, grelhas e placas de rua.
for(let i=-110;i<115;i+=22){for(const road of roads){block(road-5,.07,i,.9,.03,.9,'#455e60');block(i,.08,road+5,.9,.03,.9,'#455e60');}}
for(const [x,z] of [[-62,62],[10,62],[82,-10],[-82,-62]]){
  block(x,2.1,z,.12,4.2,.12,'#526b65');block(x,4.25,z,3,.5,.15,'#528078');
  solid(x,z,.12,.12);
  sign('BAÍA DO SOL',x,4.25,z+.1,2.8,{background:'#528078',border:false});
}

// Orla de areia com deque, quiosques, cadeiras e guarda-sóis.
block(0,.04,-146,246,.16,6,'#e1c49a');
const beacon=lighthouse({THREE,scene,block,part,solid,cameraObstacles,fixtures:cityFixtures});
for(let x=-120;x<121;x+=3)block(x,.13,-146,.1,.02,6,'#c5a77c');
for(let x=-114;x<=114;x+=14){palm(x,-151,.9+random()*.2);bench(x+4,-154,Math.PI);bin(x+7,-154);}
seasideKiosks({block,solid,sign,cameraObstacles});
// Píer e amarras; os barcos ficam no mesmo mundo e balançam na água.
block(0,.15,-163,6,.35,32,'#bc986e');
for(let z=-178;z<=-148;z+=2)block(0,.35,z,6,.05,.08,'#9e7d5b');
for(const x of [-3,3])for(let z=-176;z<=-150;z+=6){
  block(x,.6,z,.3,1.2,.3,'#967c60');block(x,1.2,z,.4,.15,.4,'#efe1c4');
  solid(x,z,.3,.3);
}
const marineArt=createMarineArt({THREE,scene,part});
const outerBuoys=Array.from({length:5},(_,i)=>({x:-170+i*85,z:-246+random()*1.8}));
for(const z of [-173.6,-172.6])block(-3.26,-.08,z,.10,2.05,.10,'#718886');
for(let n=0;n<5;n++)block(-3.26,.49-n*.32,-173.1,.12,.07,.99,'#b9b9a2');

// As peças estruturais compartilham posição e dimensões com o modelo desenhado.
function furnitureBounds(x,z,w,d,bottom,top){
  const bounds=solid(x,z,w,d);
  bounds.bottom=bottom;bounds.supportTop=top;
  if(top-bottom>1.75)bounds.opaque=true;
  cameraObstacles.push({x,z,w,d,bottom,top});
  return bounds;
}
function fittedBlock(x,y,z,w,h,d,color){
  block(x,y,z,w,h,d,color);
  furnitureBounds(x,z,w,d,buildingHeightOffset+y-h/2,buildingHeightOffset+y+h/2);
}
function furniture(x,z,w,d,h,color){fittedBlock(x,h/2,z,w,h,d,color);}
function chair(x,z,rot=0,occupied=false){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=rot;scene.add(g);
  const floor=g.position.y;
  part(g,0,.54,0,1.1,.14,1.1,'#b4855e');part(g,0,1.02,-.48,1.1,.87,.16,'#b4855e');
  for(const sx of [-.43,.43])for(const sz of [-.38,.38])part(g,sx,.24,sz,.12,.48,.12,'#687a6a');
  furnitureBounds(x,z,1.1,1.1,floor,floor+.61);
  furnitureBounds(x,z-.48*Math.cos(rot),1.1,.16,floor+.585,floor+1.455);
  benches.push({group:g,x,z,rotation:rot,occupied,width:1.1,depth:1.1,seatTop:.61,sideEntry:true});
}
function loungeSeat(x,z,width,depth,color){
  const g=new THREE.Group(),floor=groundHeight(x,z),arms=width>1.5;
  g.position.set(x,floor,z);scene.add(g);
  function piece(dx,y,dz,w,h,d,shade){
    part(g,dx,y,dz,w,h,d,shade);
    furnitureBounds(x+dx,z+dz,w,d,floor+y-h/2,floor+y+h/2);
  }
  piece(0,.24,-.04,width-.12,.48,depth-.16,color);
  const cushions=width>2.6?2:1,innerWidth=width-(arms?.44:0);
  for(let i=0;i<cushions;i++){
    piece(innerWidth*((i+.5)/cushions-.5),.535,.065,innerWidth/cushions-.015,.19,depth-.13,color);
  }
  piece(0,.96,-depth/2+.12,width,1.06,.24,color);
  if(arms)for(const side of [-1,1])piece(side*(width/2-.11),.69,0,.22,.63,depth,color);
  benches.push({group:g,x,z,rotation:0,occupied:false,width,depth,seatTop:.63});
}
function table(x,z){fittedBlock(x,1,z,2.3,.16,2.3,'#e8d7b3');fittedBlock(x,.5,z,.22,1,.22,'#758574');}
function wallArt(x,z,color){block(x,2.7,z,3,2.1,.14,'#a28060');block(x,2.7,z+.09,2.65,1.75,.09,color);block(x-.4,2.6,z+.16,1,1,.05,'#f0d6a2');}
function stockShelf(x,z,groceries=false){
  const frame=groceries?'#a7a18a':'#9e795a';
  block(x,1.55,z-.43,4,3.1,.14,frame);
  for(const side of [-1,1])block(x+side*1.93,1.55,z,.14,3.1,1.06,frame);
  for(const y of [.19,1.13,2.07,3.03])block(x,y,z,4,.14,1.08,frame);
  const colors=['#71949a','#b46f62','#d6b374','#91a994','#597a77','#aa826e'];
  const pattern=Math.abs(Math.round(x*3+z));
  for(let row=0;row<3;row++){
    const floor=.26+row*.94,count=groceries?6:10;
    for(let i=0;i<count;i++){
      const index=pattern+row*11+i;
      if(index%8===0)continue;
      const px=x-1.73+(i+.5)*3.46/count,height=(groceries?.44:.57)+index%4*.065;
      const w=groceries?.44:.21+index%3*.03,depth=groceries?.52:.49;
      const front=z+.40-index%3*.025;
      block(px,floor+height/2,front-depth/2,w,height,depth,colors[index%colors.length]);
      if(groceries||index%3!==0)block(px,floor+height*(groceries?.5:.82),front+.005,w*.78,groceries?.18:.036,.014,'#e8d7b3');
    }
  }
  furnitureBounds(x,z,4,1.08,buildingHeightOffset,buildingHeightOffset+3.1);
}
function flowers(x,z,base=0){
  fittedBlock(x,base+.4,z,.8,.8,.8,'#b9775d');
  for(let i=0;i<4;i++){const dx=(i%2-.5)*.35,dz=(Math.floor(i/2)-.5)*.3;block(x+dx,base+1.15,z+dz,.08,.8,.08,'#568764');block(x+dx,base+1.6,z+dz,.3,.28,.3,['#e19b8b','#f4d496','#eab3c4','#f2e4bc'][i]);}
}
function interior(place){
  const previous=buildingHeightOffset;buildingHeightOffset=place.kind==='hospital'?.205:.18;
  const {x,z,w,d,kind}=place, back=z-d/2+.25;
  // Rodapés, quadros e iluminação no térreo.
  block(x, .125,back+.15,w-.5,.25,.15,'#b99b78');
  block(x,4.7,back+.3,3.5,.18,.35,'#ffe8ae');
  // Duas luminárias espalham a luz pelo salão, inclusive nas vitrines e balcões.
  for(const side of [-1,1]){
    part(place.upper,side*w*.23,buildingHeightOffset+4.9,0,3.1,.16,1.05,'#e5ddca');
    part(place.upper,side*w*.23,buildingHeightOffset+4.78,0,2.7,.12,.75,'#ffefba');
  }
  for(const side of [-1,1])block(x+side*(w/2-.3),.125,z,.15,.25,d-.5,'#bb9c7b');
  if(kind==='cafe'){
    furniture(x, z-5.8, 14, 2, 1.5, '#b77d5a');
    fittedBlock(x,1.56,z-5.8,14.4,.12,2.25,'#ead6b4');
    for(let i=-2;i<=2;i++){block(x+i*2.4,1.85,z-5.5,.35,.55,.35,'#efe7d0');block(x+i*2.4,2.15,z-5.5,.28,.1,.28,'#8b6d56');}
    fittedBlock(x-5,1.9,z-8.7,2.4,3.7,1,'#587a70');
    for(let i=0;i<4;i++)block(x-5.8+i*.5,2.8,z-8.1,.3,.5,.15,'#d6a16f');
    sign('CAFÉ • BOLOS',x+2,3.6,back+.22,6,{background:'#475f55'});
    for(const dx of [-6,6]){table(x+dx,z+2.5);chair(x+dx,z+4.3,Math.PI);chair(x+dx,z+.7);block(x+dx,1.25,z+2.5,.22,.35,.22,'#f5e6c8');}
    flowers(x+9,z-7);wallArt(x-3,back+.15,'#a7c7b3');
  }else if(kind==='book'){
    for(const dx of [-7,0,7])stockShelf(x+dx,z-8);
    for(const dx of [-7,7])stockShelf(x+dx,z-1);
    furniture(x-6,z+5,4,1.3,1.25,'#ab805c');
    loungeSeat(x+4,z+4,2.4,1.5,'#80aaa0');
    block(x+4,.01,z+4,4,.02,3,'#d4aa7e');flowers(x+9,z+5.8);
  }else if(kind==='gallery'){
    for(let i=-1;i<=1;i++){
      const px=x+i*6;wallArt(px,back+.18,['#dba485','#9cbeb7','#d5bd94'][i+1]);
      block(px,2.7,back+.29,.6,.8,.07,'#668e89');
    }
    for(const dx of [-5,5]){fittedBlock(x+dx,.65,z-3,1.5,1.3,1.5,'#eee4ce');fittedBlock(x+dx,1.8,z-3,.8,1,.8,dx<0?'#d5a987':'#7ea7a0');}
    bench(x,z+4,Math.PI);flowers(x+8,z+6);
  }else if(kind==='market'){
    for(const dx of [-5,5]){
      furniture(x+dx,z-2,2,8,1.25,'#c7a477');
      for(let i=0;i<5;i++)for(let j=0;j<2;j++)block(x+dx+(j-.5)*.7,1.4,z-5+i*1.4,.45,.3,.6,['#da8d71','#a8bc84','#e5c87d'][i%3]);
    }
    for(const dx of [-7,0,7])stockShelf(x+dx,z-8,true);
    furniture(x-6,z+6,4,1.3,1.3,'#84a89a');block(x-6,1.5,z+6,.8,.4,.6,'#4e6660');
  }else if(kind==='bakery'){
    furniture(x,z-5.5,15,2,1.2,'#c4936a');fittedBlock(x,1.55,z-5.5,15,.7,1.7,'#b9d2c8');
    for(let i=-3;i<=3;i++){block(x+i*1.8,1.45,z-5.3,.9,.28,.55,'#d69c60');block(x+i*1.8,1.65,z-5.3,.7,.12,.35,'#f1c580');}
    sign('PÃO QUENTINHO',x,3.6,back+.2,8,{background:'#9b6b50'});
    for(const dx of [-6,6]){table(x+dx,z+3);chair(x+dx,z+4.7,Math.PI);}
  }else if(kind==='flower'){
    furniture(x,z-7,15,1.1,1.1,'#af8668');
    for(let i=-3;i<=3;i++)flowers(x+i*2.1,z-7,1.1);
    furniture(x-7,z+1,3,1.3,1.2,'#c19376');furniture(x+7,z+1,3,1.3,1.2,'#c19376');
    for(const dx of [-8,-6,6,8])flowers(x+dx,z+1,1.2);
    wallArt(x,back+.2,'#d6b4a6');
  }else if(kind==='home'){
    // A casa tem passagem central entre sala, cama e cozinha compacta.
    furniture(x-5,z-3.7,3.3,4,.75,'#a9866d');
    fittedBlock(x-5,.85,z-4,3,.2,3.5,'#efe6d5');fittedBlock(x-5,1.05,z-4.8,2.8,.25,1,'#b5cebd');
    furniture(x+5,z-4.6,4.2,1.2,1.1,'#90a99a');fittedBlock(x+5,1.175,z-4.6,4.3,.15,1.3,'#e9dec6');
    block(x+5,1.55,z-4.6,.8,.5,.7,'#567b74');
    loungeSeat(x+5,z+1,3,1.5,'#91b0a5');
    block(x+5,.01,z+2.6,4,.02,2.8,'#d4ad8c');flowers(x-7,z+3);
    wallArt(x,back+.2,'#b7cbbb');
  }else if(kind==='residence'){
    furniture(x-6,z-4.6,3.5,1.2,1.2,'#8da89d');fittedBlock(x-6,1.28,z-4.6,3.6,.16,1.3,'#e6e2d0');
    for(let i=0;i<4;i++){block(x+5+i*.8,2.2,back+.3,.65,.65,.2,'#b3b8ab');block(x+5+i*.8,2.2,back+.43,.4,.08,.04,'#506b65');}
    loungeSeat(x+5,z+1.3,3,1.4,'#8ca9a0');
    flowers(x-7,z+3);wallArt(x,back+.2,'#c5b5a3');
  }else if(kind==='restaurant'){
    furniture(x,z-5.5,10.6,1.4,1.25,'#a8795c');fittedBlock(x,1.325,z-5.5,10.8,.15,1.6,'#e7d9bd');
    for(let i=-2;i<=2;i++){block(x+i*1.7,1.55,z-5.5,.5,.3,.5,'#e7d8b9');block(x+i*1.7,1.75,z-5.5,.25,.2,.25,'#cc9273');}
    sign('COZINHA',x,3.6,back+.25,6,{background:'#9a6951'});
    for(const dx of [-5,5]){table(x+dx,z+1.5);chair(x+dx,z+3.2,Math.PI,true);chair(x+dx,z-.2);block(x+dx,1.105,z+1.5,.4,.05,.4,'#f5e9d5');}
    furniture(x-4.5,z-7,3.1,1.2,1.3,'#657c77');
    for(const dx of [-.7,.7]){
      block(x-4.5+dx,1.37,z-7,.8,.07,.75,'#3d504c');
      block(x-4.5+dx,1.59,z-7,.6,.4,.6,'#c2c8b6');block(x-4.5+dx,1.83,z-7,.68,.08,.68,'#d9dbc8');
    }
    block(x-4.5,3.8,z-7.2,3.5,.45,1.6,'#a4b5a9');block(x-4.5,4.3,z-7.5,1.5,.65,.8,'#a4b5a9');
    furniture(x+7.8,z-5.8,1.5,1.3,3.2,'#c2cfc1');block(x+7.8,2.6,z-5.1,1.4,.06,.06,'#748d80');block(x+8.3,2.1,z-5.08,.08,.7,.08,'#6e877b');
    furniture(x+1,z-7.1,3.4,1.1,1.2,'#91a999');block(x+1,1.27,z-7.1,1.5,.06,.8,'#547d7c');block(x+1.6,1.65,z-7.3,.08,.75,.08,'#c7d0bf');
    const steamMat=new THREE.MeshBasicMaterial({color:'#e8e7da',transparent:true,opacity:.24,depthWrite:false});
    for(let i=0;i<3;i++){
      const mesh=part(scene,x-4.5,2,z-7,.15,.15,.15,steamMat);mesh.castShadow=false;
      kitchenSteam.push({mesh,x:x-4.5,z:z-7,phase:i/3});
    }
    for(const dx of [-5,5]){block(x+dx,1.19,z+1.5,.27,.12,.27,'#c98665');block(x+dx+.55,1.26,z+1.2,.2,.36,.2,'#e5c397');}
    flowers(x-8,z-5);
  }else if(kind==='armory'){
    furnishArmory(place,{THREE,scene,block,furniture,fittedBlock,furnitureBounds,groundHeight,servicePoints});
  }else if(kind==='hospital'){
    for(let dx=-9;dx<=9;dx+=3)for(let dz=-8;dz<=8;dz+=3)block(x+dx,-.015,z+dz,2.9,.03,2.9,(dx+dz)%2?'#e9e9dc':'#d6e3d9');
    furniture(x-6,z-2,7,1.8,1.3,'#a6c1b6');fittedBlock(x-6,1.37,z-2,7.2,.14,2,'#f3eee0');
    block(x-6,1.67,z-2,.75,.5,.55,'#5a7774');
    servicePoints.push({kind:'clinic',x:x-6,z:z-.67,place,frontOnly:true,anchor:{x:x-6,z:z-.94,height:2.04},
      display:{x:x-6,y:groundHeight(x,z)+2.8,z:z-.88}});
    for(const dx of [-5,5]){
      furniture(x+dx,z-7,3,4,1,'#eef0e5');fittedBlock(x+dx,1.1,z-7.8,2.7,.2,1.5,'#d6e4dd');
      fittedBlock(x+dx,1.125,z-5.9,2.5,.25,1.3,'#a9c9c2');fittedBlock(x+dx+2,1.7,z-7.8,.12,3.4,.12,'#9aaca2');
      block(x+dx+2,3,z-7.8,.5,.7,.2,'#d9eae2');
    }
    for(let i=0;i<3;i++)loungeSeat(x+4+i*1.5,z+5,1.2,1.2,'#86ada7');
    block(x,3,back+.3,.65,2.5,.12,'#d8796b');block(x,3,back+.32,2.5,.65,.12,'#d8796b');
  }else if(kind==='police'){
    furniture(x+4,z-4.8,7,1.6,1.25,'#7799a1');fittedBlock(x+4,1.325,z-4.8,7.2,.15,1.8,'#e6e6d6');
    block(x+4,1.65,z-4.8,.8,.55,.5,'#496c78');sign('ATENDIMENTO',x+3,3.5,back+.22,7,{background:'#426c83'});
    for(let i=0;i<3;i++)loungeSeat(x-7+i*1.6,z+4,1.25,1.2,'#829fa5');
    block(x-7,2.7,back+.3,5,2.5,.18,'#d1dfd5');
    for(const [dx,dy] of [[-1,.3],[0,-.2],[1,.2]])block(x-7+dx,2.7+dy,back+.42,.7,.55,.08,'#e5c991');
    sign('INFORMAÇÕES · E',x-7,4.4,back+.45,5,{background:'#426c83'});
    servicePoints.push({kind:'police-info',x:x-7,z:back+1.8,name:'DELEGACIA · INFORMAÇÕES',display:{x:x-7,y:3.1,z:back+.6}});
  }else if(kind==='fire'){
    furniture(x+4,z-5,7,1.6,1.25,'#a26f58');fittedBlock(x+4,1.325,z-5,7.2,.15,1.8,'#e6d9c0');
    for(let i=0;i<3;i++){
      fittedBlock(x-8+i*2.2,1.55,z-8,1.8,3.1,1,'#8c9a91');block(x-8+i*2.2,2.65,z-7.4,1.2,.3,.2,'#efd079');
      block(x-8+i*2.2,1.6,z-7.4,1.2,.08,.2,'#d6ba82');
    }
    sign('EQUIPAMENTOS',x-5.7,4.4,back+.2,7,{background:'#a45145'});
    fittedBlock(x+10.65,1.5,z,.25,2,2.2,'#bb6153');fittedBlock(x+10.45,1.6,z,.12,.6,1.4,'#eed99f');
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
  buildingHeightOffset=previous;
}
for(const p of places)interior(p);

// Moradores conservam os pivôs das mãos usados pelos ofícios e conversas.
let residentStyle=0;
function human(shirt='#d8a968', skin='#e7b896', trousers='#415b62', hair='#4c4641'){
  return createCharacter({part,shirt,skin,trousers,hair,style:residentStyle++});
}
const playerMotion=createPlayerMotion({THREE,part,shirt:'#e9b857',skin:'#e8b896',trousers:'#3c5862',hair:'#594942'});
const player=playerMotion.root;player.position.set(-26,0,55);player.rotation.y=Math.PI;scene.add(player);
marine=createMarine({player,buildBoat:marineArt.buildBoat,weather:()=>cityWeather.state,
  landBlocked:(x,z,y)=>collides(x,z,.38,y,2.0,true)||vehicleBlocks(x,z,.41,null,y)||pedestrianLife.occupied(playerActor,x,z),
  sound:(kind,x,z)=>cityAudio.play(kind,x,z),onChange(next,old){
    dropCombatAim();combat.holster();clearSpeech();cancelLink();
    // A held direction keeps its world heading through the surf even if the
    // camera has meanwhile turned behind the swimmer.
    if(next==='boat'||old==='boat'){walkCamera.reset();driveCamera.reset();targetYaw=yaw;}
    velocity.set(0,0,0);verticalVelocity=0;jumpHeight=0;jumpOriginY=null;playerPlatform=null;player.rotation.x=0;player.rotation.z=0;
  }});
const boats=marine.boats;
marine.addDock({x:0,z:-163,w:6,d:32,top:.35});marine.addLadder({x:-3.50,z:-173.1,landX:-2.12,landZ:-173.1,top:.35});
marine.addDock(marineHarborPier);marine.addLadder(marineHarborLadder);
for(const spec of [
  {model:'launch',name:'Brisa',x:4.72,z:-173.3,heading:Math.PI,color:'#4b9291'},
  {model:'fishing',name:'Estrela',x:-21,z:-191,heading:-.52,color:'#b5744e'},
  {model:'sail',name:'Andorinha',x:48,z:-204,heading:1.35,color:'#638496'},
  {model:'launch',name:'Mar Azul',x:-57,z:-215,heading:.82,color:'#709ba2'},
  {model:'rescue',name:'Guará',x:169.7,z:-173.5,heading:Math.PI,color:'#ca7246'}])marine.addBoat(spec);
for(const p of [...outerBuoys,{x:-8,z:-186,type:1},{x:8,z:-186,type:2},{x:159,z:-189,type:1},{x:177,z:-189,type:2},
  {x:-207,z:-192},{x:-207,z:-222},{x:207,z:-192},{x:207,z:-222}]){marine.addBuoy(p.x,p.z);cityFixtures.push(marineArt.buoy(p.x,p.z,p.type));}
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
    location=-112+random()*224;x=alongZ?road+side*8.25:location;z=alongZ?location:horizontalRoadZ(x,road)+side*8.25;
    if(!collides(x,z,.42)&&!pointOnRoad(x,z)&&people.every(p=>Math.hypot(p.g.position.x-x,p.g.position.z-z)>2.5))break;
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
function serviceUniform(g,kind){addCharacterUniform(g,kind,{part});}
const staff={
  armory:['Iara · armeira','Nas três vitrines você compra armas e cargas. A bancada marcada recompra seu equipamento. Quem precisa de dinheiro pode pesar peixe no Galpão da Maré.',6.6,3.15],
  cafe:['Lia · barista','Entra, fica à vontade. O café está sempre quentinho.',2,-7.65],
  bakery:['Cida · padeira','Pão fresquinho todo dia. Pode olhar à vontade!',2,-7.35],
  market:['Dona Rosa · comerciante','Bem-vindo! Temos frutas, verduras e coisas para a casa.',-6,4.4],
  gallery:['Tomás · artista','Essas cores todas foram inspiradas na nossa baía.',8,-7.8],
  book:['Rui · livreiro','Tem histórias para todos os gostos por aqui.',-6,3.5],
  flower:['Flor · florista','As flores gostam do sol de Baía do Sol tanto quanto a gente.',7.4,-5.35],
  hospital:['Joana · enfermeira','Olá! Aqui a gente cuida dos moradores da cidade.',-6,-3.85],
  station:['Caio · atendente','Passe no posto para dar uma olhada e descansar da viagem.',-5,-3.95],
  police:['Marina · policial','Bem-vindo à delegacia. Pode conferir o quadro de informações ali no fundo.',4,-6.4],
  fire:['Henrique · bombeiro','Olá! Os equipamentos estão prontos; o painel de treino fica perto da parede.',4,-6.6],
  home:['Morador','Gosto de passar a manhã aqui antes de sair pelo bairro.',4,-3],
  residence:['Porteiro','Bem-vindo! Os vizinhos sempre se encontram por aqui.',-6,-6.15],
  restaurant:['Cozinheira','A comida está saindo fresquinha da cozinha.',5.95,-6.78]
};
for(const p of places){
  const [name,line,dx,dz]=staff[p.kind];const shirt=p.kind==='hospital'?'#f1eee0':p.kind==='police'?'#507591':p.kind==='fire'?'#bd5e4c':'#d6917a';
  const g=human(shirt,pick(skins));if(p.kind==='police'||p.kind==='fire')serviceUniform(g,p.kind);
  const staffX=p.x+dx,staffZ=p.z+dz;
  g.position.set(staffX,groundHeight(staffX,staffZ),staffZ);g.rotation.y=0;scene.add(g);people.push({g,name,line,indoor:true,place:p,role:p.kind});
}

// Motorista desenhado na escala da cabine, com rosto e mãos no volante.
function makeDriver(shirt,skin){return buildDriverCharacter({part,shirt,skin,style:residentStyle++});}

// Carros com faróis e lanternas; o veículo dirigido ilumina a rua de verdade.
const headMat=new THREE.MeshStandardMaterial({color:'#ffefbf',emissive:'#ffdd8a',emissiveIntensity:1.0});
const carGlass=new THREE.MeshStandardMaterial({color:'#587785',transparent:true,opacity:.46,roughness:.53,metalness:0,depthWrite:false,side:THREE.DoubleSide});
carGlass.forceSinglePass=true;
function roadWheel(group,wheels,contacts,x,y,z,radius,width,steer,style='service'){
  buildRoadWheel(group,wheels,contacts,x,y,z,radius,width,steer,style,{part,material});
}
function makeCar(x,z,color,options={}){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=options.rotation||0;scene.add(g);
  g.rotation.order='YXZ';
  const design=carDesign(options.design??cars.length);
  const brake=new THREE.MeshStandardMaterial({color:'#a83637',emissive:'#ef3938',emissiveIntensity:.35});
  const reverse=new THREE.MeshStandardMaterial({color:'#b8c3b6',emissive:'#fff5d8',emissiveIntensity:0});
  const indicators={left:new THREE.MeshStandardMaterial({color:'#a37338',emissive:'#ffb13b',emissiveIntensity:0}),right:new THREE.MeshStandardMaterial({color:'#a37338',emissive:'#ffb13b',emissiveIntensity:0})};
  const {doors:carDoors,surfaces,seatMount,wheelMount,headlampY}=buildCarShell(g,design,color,
    {part,material,glass:carGlass,batch:batchGroupCubes,head:headMat,brake,reverse,indicators});
  const wheel=new THREE.Mesh(new THREE.TorusGeometry(.22,.051,6,12),material('#344248'));
  wheel.position.set(wheelMount.x,wheelMount.y,wheelMount.z);g.add(wheel);
  for(const angle of [Math.PI/2,-Math.PI/2,Math.PI]){
    part(wheel,Math.sin(angle)*.10,Math.cos(angle)*.10,0,.047,.23,.055,'#56615c').rotation.z=-angle;
  }
  const driver=makeDriver(options.service?'#496779':options.auto?'#76958e':'#e9b857',options.auto?'#d9ac88':'#e8b896');
  driver.position.set(seatMount.x,seatMount.y,seatMount.z);driver.visible=!!options.auto;g.add(driver);
  const wheels=[],wheelContacts=[],tireWidth=design.id==='pickup'?.34:.29;
  for(const side of [-1,1])for(const axle of design.axles){
    roadWheel(g,wheels,wheelContacts,side*(design.width/2-tireWidth*.45),design.tire+.019,axle,design.tire,tireWidth,axle>0,options.service?'police':design.id);
  }
  const spots=[];
  for(const side of [-1,1]){
    const light=new THREE.SpotLight('#ffe8b5',65,28,.5,.8,1.4);
    light.position.set(side*(design.width/2-.39),headlampY,design.length/2+.081);
    const target=new THREE.Object3D();target.position.set(side*1.7,.05,18);g.add(target);light.target=target;light.visible=false;g.add(light);spots.push(light);
  }
  batchGroupCubes(g);
  const handling={wheelbase:design.axles[1]-design.axles[0],rearOffset:-design.axles[0],maxSteering:design.lock,driveScale:design.drive,massScale:design.mass,
    frontWeight:design.frontWeight,frontDrive:design.frontDrive};
  const c={g,design,handling,surfaces,steeringWheel:wheel,auto:!!options.auto,axis:options.axis||'z',direction:options.direction||1,
    laneRoad:options.road,velocity:0,brake,reverse,indicators,signal:null,spots,driver,wheels,wheelContacts,carDoors,seatMount,
    stoppedReason:null,width:design.width+.03,length:design.length+.16,centerOffset:0};cars.push(c);return c;
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
makeCar(-18,67.5,'#cb9136',{rotation:Math.PI/2,design:0});makeCar(22,61,'#578d80',{design:1});makeCar(-57,-17,'#ae6046',{rotation:Math.PI/2,design:2});makeCar(103,-21,'#567f9a',{design:3});
const displayCars=acaciaBays.map((x,i)=>makeCar(x,-103,['#ba6e38','#387668','#aa4e39','#497fa9','#d1b783'][i],{rotation:bayAngle,design:i}));
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
function makeCyclist(){
  const rider=new THREE.Group(),skin='#e8b896',shirt='#e9b857',trousers='#3c5862';
  dressCyclist(rider,{part,skin,shirt,trousers});
  for(const side of [-1,1]){
    bikeTube(rider,[side*.37,2.33,.48],[side*.43,1.98,1],shirt,.21);
    bikeTube(rider,[side*.43,1.98,1],[side*.36,1.76,1.62],skin,.18);
    part(rider,side*.36,1.76,1.62,.2,.16,.22,skin);
  }
  const legs=[-1,1].map(side=>({side,thigh:part(rider,0,0,0,.23,1,.24,trousers),shin:part(rider,0,0,0,.21,1,.22,trousers),foot:part(rider,0,0,0,.28,.14,.38,'#42514e')}));
  function bone(mesh,a,b){
    const delta=b.clone().sub(a);mesh.position.copy(a).add(b).multiplyScalar(.5);mesh.scale.y=delta.length();
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0,1,0),delta.normalize());
  }
  rider.userData.pedal=phase=>{
    for(const leg of legs){
      const angle=phase+(leg.side<0?Math.PI:0),x=leg.side*.24;
      const hip=new THREE.Vector3(x,1.74,0),foot=new THREE.Vector3(x,.85+Math.cos(angle)*.27,.62+Math.sin(angle)*.27);
      const dy=foot.y-hip.y,dz=foot.z-hip.z,distance=Math.hypot(dy,dz),bend=Math.sqrt(Math.max(0,.7**2-distance**2/4));
      const knee=new THREE.Vector3(x,(hip.y+foot.y)/2+dz/distance*bend,(hip.z+foot.z)/2-dy/distance*bend);
      bone(leg.thigh,hip,knee);bone(leg.shin,knee,foot);leg.foot.position.copy(foot).add(new THREE.Vector3(0,0,.08));
    }
  };
  rider.userData.pedal(0);return rider;
}
function makeBike(x,z,color,rotation=0,commuter=false){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);g.rotation.y=rotation;scene.add(g);
  g.rotation.order='YXZ';const wheelContacts=[],wheels=[];
  for(const zz of [-1.25,1.25]){
    const pivot=new THREE.Group(),spin=new THREE.Group();pivot.position.set(0,.65,zz);pivot.add(spin);g.add(pivot);
    const tire=new THREE.Mesh(new THREE.TorusGeometry(.548,.072,6,12),material('#394746'));
    tire.rotation.y=Math.PI/2;spin.add(tire);
    const rim=new THREE.Mesh(new THREE.TorusGeometry(.511,.023,4,12),material('#afbcb4'));
    rim.rotation.y=Math.PI/2;spin.add(rim);
    for(let i=0;i<3;i++)part(spin,0,0,0,.035,.99,.026,'#8f9e98').rotation.x=i*Math.PI/3;
    const hub=new THREE.Mesh(new THREE.CylinderGeometry(.081,.081,.19,8),material('#65776e'));
    hub.rotation.z=Math.PI/2;spin.add(hub);
    wheels.push(tire);wheelContacts.push({x:0,z:zz,y:.65,radius:.62,width:.15,parts:[pivot],pivot,spin});
  }
  for(const [a,b] of [
    [[0,.65,-1.25],[0,.85,0]],[[0,.85,0],[0,1.55,-.65]],[[0,1.55,-.65],[0,.65,-1.25]],
    [[0,commuter?1.07:1.55,-.65],[0,1.48,.8]],[[0,1.48,.8],[0,.85,0]],[[0,1.48,.8],[0,.65,1.25]]
  ])bikeTube(g,a,b,color,.13);
  bikeTube(g,[0,1.48,.8],[0,1.75,1], '#546a61',.11);
  part(g,0,1.76,1,.9,.1,.12,'#546a61');part(g,0,1.58,-.68,.6,.12,.38,'#584f46');
  part(g,0,1.18,.95,.18,.2,.16,headMat);
  bikeTube(g,[.08,.70,-1.25],[.08,.99,0],'#64736b',.037);
  if(commuter){
    part(g,0,1.38,-1.2,.49,.07,.73,'#77867e');
    for(const s of [-1,1])bikeTube(g,[s*.09,.67,-1.25],[s*.20,1.37,-1.46],'#6d7c74',.045);
  }
  const rider=makeCyclist();rider.position.z=-.62;rider.visible=false;g.add(rider);
  const pedals=[-1,1].map(side=>part(g,side*.27,.85,0,.4,.1,.32,'#65776d'));
  const brake=new THREE.MeshStandardMaterial({color:'#ab5448',emissive:'#c24e40',emissiveIntensity:.35});
  part(g,0,1.25,-1.2,.18,.18,.1,brake);
  const vehicle={g,auto:false,axis:'z',direction:1,velocity:0,brake,spots:[],rider,wheels,wheelContacts,pedals,kind:'bike',width:.9,length:3.8};
  cars.push(vehicle);return vehicle;
}
makeBike(-51,58,'#d98269',.16);makeBike(19,54.1,'#7daea5',-.08,true);makeBike(91,54.1,'#e6ba74',.2);

// Extend the distant forest around the playable ground, with no second sheet
// of grass crossing the streets. Its edges follow the same grade as the city.
for(const x of [-305,305])block(x,-.65,80,190,.5,410,'#91ad78');
block(0,-.65,252.5,420,.5,65,'#91ad78');
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
const fishMarket=createFishMarket({THREE,scene,block,part,solid,sign,human,groundHeight,solids,cameraObstacles,
  canServe:p=>combat.canTalk(p)});
people.push(fishMarket.worker);
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
  if(i%23===0&&[[-52,20],[-20,20],[-52,52],[-20,52],[94,94],[114,94],[94,114],[114,114]].some(([tx,tz])=>Math.hypot(x-tx,z-tz)<4)){
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
  const side=(Math.round((x+190)/20)%2?1:-1);
  for(const edge of [side,-side]){
    const z=curvedRoadZ(x)+edge*10.5;
    if(pointOnRoad(x,z)||collides(x,z,.55))continue;
    lamp(x,z);break;
  }
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
function seatedHuman(shirt,skin,seatHeight=.61){
  return createCharacter({part,shirt,skin,trousers:'#526871',hair:'#52483f',style:residentStyle++,seatHeight});
}
for(const [bx,bz,rot] of [[-52,24,0],[-20,24,0],[-52,48,Math.PI],[-20,48,Math.PI],[94,97,0],[114,111,Math.PI]]){
  const g=seatedHuman(pick(shirts),pick(skins),.82);g.position.set(bx,groundHeight(bx,bz),bz);g.rotation.y=rot;scene.add(g);
  people.push({g,name:pick(['Ana','Elisa','Samuel','Lúcia','Rafa']),line:'Gosto de sentar um pouco e ver a cidade passar.',stationary:true,sitting:true,indoor:false,phase:random()*6});
}
for(const place of places.filter(p=>p.kind==='restaurant'))for(const dx of [-5,5]){
  const x=place.x+dx,z=place.z+3.2,g=seatedHuman(pick(shirts),pick(skins));
  g.position.set(x,groundHeight(x,z),z);g.rotation.y=Math.PI;scene.add(g);
  g.userData.seatedCup=part(g.userData.hands[1],0,.025,.10,.2,.3,.2,'#f1e4c9');
  people.push({g,name:pick(['Luana','João','Rita','Daniel']),line:'A comida daqui é uma delícia.',stationary:true,sitting:true,indoor:true,place,phase:random()*6});
}
function socialPair(x,z){
  for(const side of [-1,1]){
    const g=human(pick(shirts),pick(skins));g.position.set(x+side*.7,groundHeight(x+side*.7,z),z);g.rotation.y=side<0?Math.PI/2:-Math.PI/2;scene.add(g);
    people.push({g,name:pick(['André','Marina','Tainá','Caio','João']),line:'A gente estava colocando a conversa em dia!',stationary:true,chatting:true,indoor:false,phase:random()*6});
  }
}
socialPair(-47,32);socialPair(-16,19);socialPair(26,54);socialPair(96,53);socialPair(96,101);

// Plantão da rádio e corte de velas no cais, alcançáveis pela passarela da orla.
const radioOperator=worker(146.92,-152.65,'Bia · rádio do cais','Escuta a baía. Cada estação vira outro desenho ali na lata.','#689e99','#dda961',null,
  ['Aqui é a Onda Curta. Mire no toca-discos com F para trocar a faixa e virar o mural.',
    'A bancada da oficina lança seu drone. Do alto dá para operar o pórtico e enxergar a entrada do canal.']);
radioOperator.g.rotation.y=.35;
const sailmaker=worker(181.32,-149.67,'Jaci · veleira','A manga de vento mostra o rumo da rajada. Consulte o boletim antes de soltar a amarra.','#db9164','#dfd1ae');
sailmaker.g.rotation.y=-.75;
const projectionist=worker(-54.97,-30.2,'Sol · projecionista','O projetor funciona com a manivela ou com o celular. O curta muda quando chove.','#a87c94','#d9b88c');
projectionist.g.rotation.y=-2.06;
const streetMusician=worker(-26,19.2,'Ivo · violonista','A roda é aberta. Fica para ouvir um pouco.','#b76754','#dfc38a',null,
  ['Essa se chama Brisa do Ipê. É coisa nossa, feita aqui na praça.','Valeu pela companhia. A próxima vai para quem chegou agora.','Se a chuva apertar, a gente espera passar para proteger o violão.']);
streetMusician.g.rotation.y=0;streetPerformance.attach(streetMusician);
// Profissionais da cidade: segurança, bombeiros, limpeza e venda de pipoca.
function worker(x,z,name,line,color,hat,role=null,lines=null){
  const g=human(color,pick(skins),'#465e64','#494039');g.position.set(x,groundHeight(x,z),z);g.rotation.y=Math.PI/2;scene.add(g);
  if(role)serviceUniform(g,role);else addCharacterHat(g,{part,color:hat});
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
servicePatrol(-11,21,'police','Cíntia · policial',['Vou conferir as travessias da praça.','A ronda passa pela feira e pela avenida.']);
servicePatrol(-138,-85,'police','Caio · policial',['Estou de ronda nas Acácias.','Pode falar comigo se precisar de ajuda.']);
servicePatrol(82,14,'police','Lúcia · policial',['Estou a caminho do hospital.','O plantão da delegacia funciona o dia todo.']);
worker(-44,53,'Zeca · vendedor de pipoca','Pipoca quentinha para passear pela praça!','#e7b875','#f5dec0');
const urbanCleaner=worker(-86,31,'Célia · limpeza urbana','Ajudo a deixar a praça e as calçadas cuidadas.','#7ea38a','#729781');
Object.assign(urbanCleaner,{stationary:false,lifeResident:true,routine:['work','rest','work','phone'],initialDestination:'limpeza-mercado'});
const urbanPainter=worker(23.4,36,'Dora · artista de rua','A maré inspirou as cores deste mural.','#d77d68','#728e9f');
Object.assign(urbanPainter,{stationary:false,lifeResident:true,fixedScale:1,routine:['paint','rest','chat','paint'],initialDestination:'mural-aurora'});
const painterApron=new THREE.Group();urbanPainter.g.add(painterApron);
part(painterApron,0,1.17,.27,.65,.61,.05,'#dde0c2');
for(const [x,y,color] of [[-.15,1.3,'#4d9eac'],[.1,1.17,'#edc86a'],[-.1,.99,'#df9874']])part(painterApron,x,y,.302,.12,.09,.015,color);
urbanPainter.g.updateWorldMatrix(true,true);urbanPainter.g.userData.chest.attach(painterApron);
// Carrinho do vendedor, com rodas, toldo e grãos de pipoca.
block(-47,.8,53,2.1,1.6,1.2,'#f2d5ad');solid(-47,53,2.1,1.2);
block(-47,1.65,53,2.3,.12,1.4,'#d57962');
for(const x of [-47.8,-46.2]){block(x,2.25,53,.1,1.2,.1,'#a87960');block(x,.3,53,.4,.4,.4,'#56645c');}
block(-47,2.9,53,2.7,.22,1.7,'#e79479');
for(let i=0;i<6;i++)block(-47.7+i*.28,1.85,53,.18,.25,.18,'#f6e5af');
sign('PIPOCA',-47,2.55,53.75,2.2,{background:'#c86c58'});
function vehicleSideLabel(g,text,side,z,width=3.3,depth=1.31,y=1.55,ink='#f5f0dd'){
  const c=document.createElement('canvas');c.width=512;c.height=128;const ctx=c.getContext('2d');
  ctx.fillStyle=ink;ctx.font='bold 84px system-ui';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(text,256,67,472);
  const texture=new THREE.CanvasTexture(c);texture.colorSpace=THREE.SRGBColorSpace;
  const m=new THREE.Mesh(new THREE.PlaneGeometry(width,width/4),new THREE.MeshBasicMaterial({map:texture,transparent:true,side:THREE.DoubleSide}));
  m.position.set(side*depth,y,z);m.rotation.y=side*Math.PI/2;g.add(m);
}
function addEmergencyLights(c){
  const red=new THREE.MeshStandardMaterial({color:'#ad514b',emissive:'#ff5146',emissiveIntensity:0});
  const blue=new THREE.MeshStandardMaterial({color:'#4c768d',emissive:'#65bdff',emissiveIntensity:0});
  const y=c.design.roof,z=-.20;
  for(const s of [-1,1])part(c.g,s*.48,y+.049,z,.14,.096,.20,'#354148');
  part(c.g,0,y+.106,z,1.46,.09,.31,'#445258');part(c.g,0,y+.181,z,.27,.103,.29,'#d6dacb');
  for(const side of [-1,1])for(const center of [.24,.44,.64]){
    part(c.g,side*center,y+.18,z,.178,.138,.296,side<0?red:blue);
  }
  c.surfaces.push({w:1.46,d:.31,z,top:y+.249,fitting:true});
  c.emergency={red,blue,on:false};
}
// Viatura e caminhão ficam nas vagas das respectivas unidades, sem bloquear a porta.
const policeCar=makeCar(30.5,-87,'#e5e8df',{design:0,service:true});
const streetPatrol=makeCar(50,3.3,'#e5e8df',{design:0,service:true,auto:true,axis:'x',road:0,direction:-1,rotation:-Math.PI/2});
for(const unit of [policeCar,streetPatrol]){
  addEmergencyLights(unit);unit.role='police';
  const d=unit.design,half=d.width/2,nose=d.length/2;
  for(const side of [-1,1]){
    const door=unit.carDoors[side],offset=half-Math.abs(door.position.x);
    const front=d.windBase-.055,rear=d.pillar+.041,doorZ=(front+rear)/2-door.position.z;
    part(door,side*offset,.965,doorZ,.034,.294,front-rear,'#294659');
    part(door,side*(offset+.012),1.098,doorZ,.025,.032,front-rear,'#d5ad55');
    vehicleSideLabel(door,'POLÍCIA',side,doorZ,front-rear-.14,offset+.024,.958);
    part(unit.g,side*(half-.040),.965,-.77,.037,.294,.68,'#294659');
    part(unit.g,side*(half-.036),1.098,-.77,.039,.032,.68,'#d5ad55');
    part(unit.g,side*.47,.79,nose+.149,.099,.53,.102,'#36464e');
    part(unit.g,side*.40,.91,nose+.119,.21,.055,.052,side<0?unit.emergency.red:unit.emergency.blue);
    part(unit.g,side*.42,d.belt+.056,d.backBase+.07,.22,.08,.11,side<0?unit.emergency.blue:unit.emergency.red);
  }
  part(unit.g,0,.817,nose+.178,.99,.077,.065,'#394950');
  part(unit.g,0,d.roof+.075,d.backTop+.12,.075,.20,.063,'#485256').rotation.x=-.33;
}
streetPatrol.patrolRoute=createPoliceRoute(horizontalRoadZ);streetPatrol.patrolMode='ronda';streetPatrol.sawSuspectAt=-Infinity;
const fireTruck=makeCar(-41.5,-87,'#bf5347',{design:2});fireTruck.width=2.6;fireTruck.length=8.04;fireTruck.centerOffset=-1.40;fireTruck.role='fire';
// The tandem rear axles turn about their midpoint, 2.8 m behind the cab origin.
const fireRearArm=(4.1-fireTruck.design.axles[0])/2;
fireTruck.handling={wheelbase:fireTruck.design.axles[1]+fireRearArm,rearOffset:fireRearArm,maxSteering:.67,driveScale:.92,massScale:3,frontWeight:.43,frontDrive:.18};
fireTruck.surfaces.push({w:2.6,d:4,z:-3.35,top:2.475},{w:1.18,d:3.96,z:-3.35,top:2.76,fitting:true});
part(fireTruck.g,0,1.71,-3.35,2.6,1.28,4,'#bd5549');part(fireTruck.g,0,2.4,-3.35,2.53,.15,4,'#dfe3d5');
part(fireTruck.g,0,.69,-3.35,1.82,.42,4.0,'#39474b');
for(const side of [-1,1]){
  for(const z of [-2.37,-4.28]){
    part(fireTruck.g,side*1.303,1.59,z,.048,.81,1.65,'#a3b3ad');
    part(fireTruck.g,side*1.331,1.31,z,.044,.083,.44,'#455759');
  }
  part(fireTruck.g,side*1.21,.88,-2.78,.20,.39,1.27,'#bd5549');
  part(fireTruck.g,side*1.276,1.105,-3.35,.08,.080,3.77,'#e8d59f');
  roadWheel(fireTruck.g,fireTruck.wheels,fireTruck.wheelContacts,side*1.15,.52,-4.1,.5,.34,false);
  vehicleSideLabel(fireTruck.g,'BOMBEIROS',side,-3.35,2.50,1.316,2.12);
  part(fireTruck.g,side*.8,.76,-5.38,.38,.22,.08,fireTruck.brake);
  part(fireTruck.g,side*.4,.76,-5.38,.22,.2,.08,fireTruck.reverse);
  part(fireTruck.g,side*1.1,.76,-5.38,.22,.2,.08,side<0?fireTruck.indicators.left:fireTruck.indicators.right);
}
for(const z of [-1.97,-4.73])part(fireTruck.g,0,2.56,z,1.21,.16,.13,'#586765');
for(const sx of [-.55,.55])part(fireTruck.g,sx,2.7,-3.35,.1,.12,4,'#dbe0d5');
for(let z=-5;z<-1.5;z+=.5)part(fireTruck.g,0,2.7,z,1.2,.1,.1,'#dbe0d5');
addEmergencyLights(fireTruck);

// Linha circular utiliza as pistas existentes e faz curvas dentro dos cruzamentos.
function makeBusTrack(){
  const points=[{x:-68.7,z:-62.7}],turns=[];
  function line(x,z){const a=points[points.length-1],n=Math.ceil(Math.hypot(x-a.x,z-a.z)/3);for(let i=1;i<=n;i++)points.push({x:a.x+(x-a.x)*i/n,z:a.z+(z-a.z)*i/n});}
  function arc(x,z,start,end){
    const begin=points.length-1;
    for(let i=1;i<=12;i++){const a=start+(end-start)*i/12;points.push({x:x+6*Math.cos(a),z:z+6*Math.sin(a)});}
    turns.push({begin,end:points.length-1,signal:end<start?'right':'left'});
  }
  line(-68.7,62.7);arc(-62.7,62.7,Math.PI,Math.PI/2);
  for(let x=-60;x<=0;x+=3)line(x,horizontalRoadZ(x,72)-3.3);
  line(62.7,68.7);arc(62.7,62.7,Math.PI/2,0);
  line(68.7,-62.7);arc(62.7,-62.7,0,-Math.PI/2);
  line(-62.7,-68.7);arc(-62.7,-62.7,-Math.PI/2,-Math.PI);
  const track=makeTrack(points);track.id='circular';
  track.turns=turns.map(turn=>({start:track.cumulative[turn.begin],end:track.cumulative[turn.end],signal:turn.signal}));
  track.stops=busStops.map(stop=>({...stop,progress:trackNearest(track,stop.roadX,stop.roadZ)})).sort((a,b)=>a.progress-b.progress);
  return track;
}
function makeBus(track){
  const progress=trackNearest(track,-68.7,-52),p=trackSample(track,progress),g=new THREE.Group();
  g.position.set(p.x,groundHeight(p.x,p.z),p.z);g.rotation.y=Math.atan2(p.dx,p.dz);scene.add(g);
  g.rotation.order='YXZ';
  part(g,0,.43,0,3.1,.2,9,'#9c9d89');part(g,0,1.39,-4.35,3,.55,.3,'#d0a968');
  part(g,-1.5,.97,0,.12,.85,9,'#dfbd79');part(g,1.5,.97,-1.5,.12,.85,6,'#dfbd79');
  for(const z of [-4.4,4.4])part(g,0,.97,z,3.1,.85,.18,'#dfbd79');
  part(g,0,3.1,0,3.1,.2,9,'#f1e9d4');
  part(g,0,3.30,-1.1,1.77,.30,2,'#c4cbbd');
  part(g,0,3.454,-1.25,1.19,.028,.91,'#82918b');
  for(const side of [-1,1]){
    for(const z of [-4,-2,0,2,4])part(g,side*1.45,2.15,z,.15,1.85,.15,'#a7865e');
    part(g,side*1.5,2.12,-1,0.04,1.45,5.7,carGlass);
    part(g,side*1.55,.9,0,.05,.2,8.6,'#f4e6c9');
    part(g,side*1.521,1.42,-.76,.055,.30,5.86,'#436c6c');
    vehicleSideLabel(g,'BAÍA · CIRCULAR',side,-.79,3.42,1.556,1.43);
    part(g,side*1.57,2.72,3.51,.36,.073,.13,'#506260');
    part(g,side*1.735,2.54,3.48,.135,.39,.20,'#304443');
  }
  part(g,0,2.12,4.44,2.8,1.45,.05,carGlass);part(g,0,2.12,-4.44,2.8,1.45,.05,carGlass);
  part(g,0,2.78,4.477,2.66,.36,.080,'#283c3e');
  const destination=sign('CIRCULAR',0,0,0,1.50,{background:'#283c3e',ink:'#f4d885',border:false});
  destination.position.set(0,2.79,4.522);g.add(destination);
  part(g,0,.54,4.48,2.94,.16,.16,'#58716b');part(g,0,.54,-4.49,2.94,.16,.16,'#58716b');
  const doors=[];for(const z of [2,2.9])doors.push(part(g,1.56,1.75,z,.08,2.3,.85,carGlass));
  for(const [x,z] of [[-.68,2.9],[.6,-.2],[-.65,-1.4],[.65,-2.4],[-.65,-2.8]]){
    part(g,x,1.12,z,.8,.18,.85,'#799b90');part(g,x,1.55,z-.4,.8,.8,.14,'#799b90');
    part(g,x,.7,z,.1,.75,.1,'#6b7f75');
  }
  part(g,0,2.85,-.3,.09,.09,7.3,'#ddd6bd');part(g,-.2,1.95,1.2,.08,1.8,.08,'#ddd6bd');
  part(g,0,1.3,3.85,2.7,.22,.55,'#687f78');
  const steering=new THREE.Mesh(new THREE.TorusGeometry(.22,.055,5,10),material('#394a47'));steering.position.set(-.68,1.62,3.48);g.add(steering);
  for(const x of [-1,1]){part(g,x,.87,4.53,.6,.32,.12,headMat);part(g,x,.88,-4.53,.5,.27,.12,'#bb6254');}
  const wheels=[],wheelContacts=[];
  for(const x of [-1.56,1.56])for(const z of [-2.9,2.9])roadWheel(g,wheels,wheelContacts,x,.58,z,.62,.35,z>0);
  const driver=makeDriver('#6c8e82','#d7a582');driver.position.set(-.68,.1,2.9);g.add(driver);
  const rider=makeDriver('#e9b857','#e8b896');rider.position.set(.6,.1,-.2);rider.visible=false;g.add(rider);
  const brake=new THREE.MeshStandardMaterial({color:'#ab5448',emissive:'#c24e40',emissiveIntensity:.35});
  part(g,-1,.84,-4.58,.45,.22,.08,brake);part(g,1,.84,-4.58,.45,.22,.08,brake);
  const indicators={};
  for(const side of [-1,1]){
    const amber=new THREE.MeshStandardMaterial({color:'#926c42',emissive:'#ffb13b',emissiveIntensity:0});
    indicators[side<0?'left':'right']=amber;
    for(const z of [-4.58,4.54])part(g,side*1.37,.91,z,.23,.21,.09,amber);
    part(g,side*1.57,1.04,3.55,.06,.15,.23,amber);
  }
  batchGroupCubes(g,doors);
  const handling={wheelbase:5.8,rearOffset:2.9,maxSteering:.79,driveScale:1.02,massScale:5,frontWeight:.47,frontDrive:.08};
  const bus={g,track,handling,steeringWheel:steering,routeProgress:progress,kind:'bus',auto:true,velocity:0,width:3.1,length:9,centerOffset:0,brake,indicators,driver,rider,wheels,wheelContacts,doors,spots:[],atStop:null,dwell:0,stoppedReason:null,passengers:[],stopCount:0};
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
  const cat=type==='cat';if(cat)g.scale.setScalar(.74);
  const head=new THREE.Group();head.position.set(0,.84,.63);g.add(head);
  part(g,0,.62,0,.6,.54,1.2,color);part(head,0,.02,.05,.58,.55,.55,color);
  part(head,0,-.09,.35,.32,.22,cat?.16:.28,cat?color:'#e5c5a6');
  part(head,0,-.03,cat?.44:.50,.15,.085,.065,'#514a42');
  for(const side of [-1,1]){
    const ear=part(head,side*(cat?.18:.28),cat?.37:.15,.015,.15,cat?.25:.32,.18,color);
    ear.rotation.z=side*(cat?-.13:.23);
    part(head,side*.16,.09,.338,.06,.07,.04,'#35453f');
  }
  const legs=[];for(const sx of [-1,1])for(const sz of [-1,1]){
    const leg=new THREE.Group();leg.position.set(sx*.2,.48,sz*.4);g.add(leg);part(leg,0,-.24,0,.16,.48,.18,color);legs.push(leg);
  }
  const tail=new THREE.Group();tail.position.set(0,.76,-.51);g.add(tail);
  part(tail,0,0,-.34,.13,.13,.70,color);tail.rotation.x=cat?1.04:.35;
  animals.push({g,type,legs,head,tail,originX:x,originZ:z,heading:random()*Math.PI*2,speed:cat?.55:.8,phase:random()*6,turn:0});
}
pet(-44,51,'dog','#b89470');pet(-24,27,'dog','#ddc2a1');pet(97,95,'dog','#987f69');
pet(-52,38,'cat','#b8aaa1');pet(112,101,'cat','#d1aa80');pet(27,56,'cat','#9d9991');
function pigeon(x,z,i){
  const g=new THREE.Group();g.position.set(x,groundHeight(x,z),z);scene.add(g);
  const body=part(g,0,.25,0,.34,.3,.48,i%2?'#8e9e9e':'#aab4b1');
  const head=part(g,0,.46,.22,.23,.23,.23,'#7e9292');
  part(g,0,.42,.35,.08,.07,.12,'#d2a477');
  const wings=[];
  for(const side of [-1,1]){
    const wing=new THREE.Group();wing.position.set(side*.12,.3,0);wing.rotation.z=side*1.3;g.add(wing);part(wing,side*.2,0,0,.45,.08,.4,'#738c8c');wings.push({group:wing,side});
    part(g,side*.09,.06,0,.05,.12,.05,'#c78e78');
  }
  animals.push({g,type:'pigeon',body,head,wings,originX:x,originZ:z,heading:random()*Math.PI*2,speed:.35,phase:random()*6,turn:0});
}
for(let i=0;i<12;i++){
  for(let tries=0;tries<50;tries++){
    const x=-55+random()*38,z=18+random()*36;
    if(collides(x,z,.48)||vehicleBlocks(x,z,.45,null)||
      people.some(p=>Math.hypot(p.g.position.x-x,p.g.position.z-z)<.95)||
      animals.some(a=>Math.hypot(a.g.position.x-x,a.g.position.z-z)<.85))continue;
    pigeon(x,z,i);break;
  }
}

// Conclui os blocos estáticos antes da simulação começar.
flushBlocks();
// Plots are horizontal terraces. The street meshes already follow their
// surveyed contours; actors and assembled fixtures stand upright at their base.
for(const object of scene.children){
  if((object.isGroup||object.isMesh)&&!object.userData.followsGrade)object.position.y+=cityElevation(object.position.z);
}
for(const bounds of [...solids,...cameraObstacles]){
  const rise=cityElevation(bounds.z);
  for(const field of ['bottom','top','vaultTop','supportTop'])if(bounds[field]!==undefined)bounds[field]+=rise;
}
for(const p of places)p.elevation=cityElevation(p.z);
for(const lamp of streetLamps)lamp.y+=cityElevation(lamp.z);
for(const point of servicePoints)if(point.display)point.display.y+=cityElevation(point.display.z);
for(const device of surveillance.devices){
  device.pivot.y+=cityElevation(device.pivot.z);device.hint.y+=cityElevation(device.hint.z);
  const span=Math.hypot(device.hint.x-device.pivot.x,device.hint.z-device.pivot.z);
  device.homeTilt=Math.atan2(device.pivot.y-device.hint.y,span);device.head.rotation.x=device.homeTilt;
}
cityPlaced=true;

// Partículas quadradas, usadas em arrancadas, aterrissagens e colisões.
const MAX_PARTICLES=180, particlePositions=new Float32Array(MAX_PARTICLES*3),particleColors=new Float32Array(MAX_PARTICLES*3), particleState=[];
const particleGeo=new THREE.BufferGeometry();particleGeo.setAttribute('position',new THREE.BufferAttribute(particlePositions,3));
particleGeo.setAttribute('color',new THREE.BufferAttribute(particleColors,3));
const particleMesh=new THREE.Points(particleGeo,new THREE.PointsMaterial({vertexColors:true,size:.3,transparent:true,opacity:.8,depthWrite:false}));
particleMesh.frustumCulled=false;scene.add(particleMesh);
for(let i=0;i<MAX_PARTICLES;i++)particleState.push({life:0,p:new THREE.Vector3(),v:new THREE.Vector3()});
let nextParticle=0;
function dust(x,y,z,count=4,force=1,color='#ebd5ad'){
  const tint=new THREE.Color(color);
  for(let i=0;i<count;i++){
    const index=nextParticle++%MAX_PARTICLES,p=particleState[index];p.life=.35+Math.random()*.45;particleColors.set([tint.r,tint.g,tint.b],index*3);
    p.p.set(x+(Math.random()-.5)*.8,y,z+(Math.random()-.5)*.8);
    p.v.set((Math.random()-.5)*force,Math.random()*force+1,(Math.random()-.5)*force);
  }
  particleGeo.attributes.color.needsUpdate=true;
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
function collides(x,z,r=.45,feetY=null,bodyHeight=2.4,allowSea=false){
  if(x < -210+r || x > 210-r || z > 220-r || z < (allowSea?seaBounds.outer:-178)+r) return true;
  if(!allowSea&&z < -163&&!marine?.dockAt(x,z,-r))return true;
  for(const s of nearbySolids(x,z,r)){
    if(s.supportTop!==undefined){
      const feet=feetY??groundHeight(x,z);
      if(feet>=s.supportTop-.015||feet+bodyHeight<=s.bottom+.005)continue;
    }else if(feetY!==null&&s.vaultTop!==undefined&&feetY>=s.vaultTop-.015)continue;
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
function wallFloorAt(x,z,maxY,r=.55){
  let floor=-Infinity;
  for(const s of nearbySolids(x,z,r)){
    const top=s.supportTop??s.vaultTop;
    if(top===undefined||top>maxY+.025)continue;
    const dx=Math.max(0,Math.abs(x-s.x)-s.w/2),dz=Math.max(0,Math.abs(z-s.z)-s.d/2);
    if(dx*dx+dz*dz<r*r)floor=Math.max(floor,top);
  }
  return floor;
}
const busSurfaces=[{w:3.1,d:9,z:0,top:3.2},{w:1.77,d:2.0,z:-1.1,top:3.45,fitting:true}];
function vehicleSurfaces(car){return car.surfaces??(car.kind==='bus'?busSurfaces:[]);}
function vehicleBlocks(x,z,r,ignore,feetY=null){
  for(const car of cars){
    if(car.g===ignore)continue;
    const dx=x-car.g.position.x,dz=z-car.g.position.z,a=car.g.rotation.y;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    const halfW=(car.width??(car.kind==='bike'?.7:2.5))/2,halfD=(car.length??(car.kind==='bike'?3:4.6))/2;
    const nx=Math.max(Math.abs(lx)-halfW,0),nz=Math.max(Math.abs(lz-(car.centerOffset||0))-halfD,0);
    if(nx*nx+nz*nz>=r*r)continue;
    if(feetY===null||car.kind==='bike')return true;
    for(const surface of vehicleSurfaces(car)){
      const sx=Math.max(Math.abs(lx-(surface.x||0))-surface.w/2,0),sz=Math.max(Math.abs(lz-surface.z)-surface.d/2,0);
      if(sx*sx+sz*sz<r*r&&feetY<car.g.position.y+surface.top-.025)return true;
    }
  }
  return false;
}
function vehicleFloorAt(x,z,maxY){
  let result=null;
  for(const car of cars){
    const dx=x-car.g.position.x,dz=z-car.g.position.z,a=car.g.rotation.y;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    for(const surface of vehicleSurfaces(car)){
      const sx=Math.max(Math.abs(lx-(surface.x||0))-surface.w/2,0),sz=Math.max(Math.abs(lz-surface.z)-surface.d/2,0);
      if(sx*sx+sz*sz>.42*.42)continue;
      const y=car.g.position.y+surface.top;
      if(y<=maxY+.06&&(!result||y>result.y))result={y,car};
    }
  }
  return result;
}
function playerAboveVehicle(car){
  const surfaces=vehicleSurfaces(car),top=surfaces.length?Math.max(...surfaces.map(surface=>surface.top)):1.8;
  return playerPlatform?.car===car||player.position.y>=car.g.position.y+top-.03;
}
function vehicleContact(a,ax,az,b,bx,bz){
  const aw=a.width??(a.kind==='bike'?.7:2.5),ad=a.length??(a.kind==='bike'?3:4.6),bw=b.width??(b.kind==='bike'?.7:2.5),bd=b.length??(b.kind==='bike'?3:4.6);
  const ar=a.g.rotation.y,br=b.g.rotation.y;
  const axes=[[Math.cos(ar),-Math.sin(ar)],[Math.sin(ar),Math.cos(ar)],[Math.cos(br),-Math.sin(br)],[Math.sin(br),Math.cos(br)]];
  const au=axes[0],av=axes[1],bu=axes[2],bv=axes[3];
  const dx=bx+Math.sin(br)*(b.centerOffset||0)-ax-Math.sin(ar)*(a.centerOffset||0);
  const dz=bz+Math.cos(br)*(b.centerOffset||0)-az-Math.cos(ar)*(a.centerOffset||0);
  let contact=null;
  for(const [x,z] of axes){
    const distance=Math.abs(dx*x+dz*z);
    const ra=aw*.5*Math.abs(au[0]*x+au[1]*z)+ad*.5*Math.abs(av[0]*x+av[1]*z);
    const rb=bw*.5*Math.abs(bu[0]*x+bu[1]*z)+bd*.5*Math.abs(bv[0]*x+bv[1]*z);
    const penetration=ra+rb+.08-distance;
    if(penetration<=0)return null;
    if(!contact||penetration<contact.penetration){const sign=dx*x+dz*z<0?-1:1;contact={x:x*sign,z:z*sign,penetration};}
  }
  return contact;
}
function vehicleBoundsOverlap(a,ax,az,b,bx,bz){return !!vehicleContact(a,ax,az,b,bx,bz);}
function vehicleFootprintBlocked(car,x,z){
  if(vehicleSolidOverlap(car,x,z))return true;
  const w=car.width??(car.kind==='bike'?.7:2.5),d=car.length??(car.kind==='bike'?3:4.6),a=car.g.rotation.y;
  for(let ix=-2;ix<=2;ix++)for(let iz=-4;iz<=4;iz++){
    const lx=ix*w/4,lz=(car.centerOffset||0)+iz*d/8;
    const px=x+Math.cos(a)*lx+Math.sin(a)*lz,pz=z-Math.sin(a)*lx+Math.cos(a)*lz;
    if(collides(px,pz,.08))return true;
  }
  return cars.some(other=>other!==car&&vehicleBoundsOverlap(car,x,z,other,other.g.position.x,other.g.position.z));
}
function vehicleSolidOverlap(car,x,z){
  const a=car.g.rotation.y,c=Math.cos(a),s=Math.sin(a),hw=(car.width??2.5)/2,hd=(car.length??4.6)/2;
  const cx=x+s*(car.centerOffset||0),cz=z+c*(car.centerOffset||0),ac=Math.abs(c),as=Math.abs(s);
  for(const wall of nearbySolids(cx,cz,Math.hypot(hw,hd)+.1)){
    const dx=wall.x-cx,dz=wall.z-cz,w=wall.w/2,d=wall.d/2;
    if(Math.abs(dx)<hw*ac+hd*as+w+.04&&Math.abs(dz)<hw*as+hd*ac+d+.04&&
      Math.abs(c*dx-s*dz)<hw+w*ac+d*as+.04&&Math.abs(s*dx+c*dz)<hd+w*as+d*ac+.04)return true;
  }
  return false;
}
function personBlocks(x,z,ignore,feetY=player.position.y){
  if(ignore!==player)return false;
  return people.some(person=>!person.ragdoll&&!person.inBus&&feetY<person.g.position.y+2.4&&Math.hypot(x-person.g.position.x,z-person.g.position.z)<.8)||animals.some(a=>!a.flight&&feetY<a.g.position.y+1.2&&Math.hypot(x-a.g.position.x,z-a.g.position.z)<(a.type==='pigeon'?.5:.7));
}
function residentPushSpace(person,x,z,checkPlayer=true){
  return !collides(x,z,.4)&&!vehicleBlocks(x,z,.4,null)&&Math.abs(groundHeight(x,z)-person.g.position.y)<.6&&
    !pedestrianLife.occupied(person,x,z)&&
    !animals.some(a=>!a.flight&&Math.hypot(x-a.g.position.x,z-a.g.position.z)<.7)&&
    (!checkPlayer||vehicle||playerActor.ragdoll||player.position.y>person.g.position.y+2.4||Math.hypot(x-player.position.x,z-player.position.z)>=.8);
}
function pushResidents(x,z){
  if(animals.some(a=>!a.flight&&player.position.y<a.g.position.y+1.2&&Math.hypot(x-a.g.position.x,z-a.g.position.z)<(a.type==='pigeon'?.5:.7)))return false;
  for(const person of people){
    if(person.ragdoll||person.inBus||player.position.y>=person.g.position.y+2.4)continue;
    const dx=person.g.position.x-x,dz=person.g.position.z-z,distance=Math.hypot(dx,dz);
    if(distance>=.8)continue;
    if(person.sitting||person.counterWorker||person.incidentCar)return false;
    const nx=distance>.001?dx/distance:1,nz=distance>.001?dz/distance:0,amount=.805-distance;
    const px=person.g.position.x+nx*amount,pz=person.g.position.z+nz*amount;
    if(!residentPushSpace(person,px,pz,false))return false;
    if(activeSpeech?.target===person)clearSpeech();
    pedestrianLife.place(person,px,pz);person.indoor=!!placeContaining(px,pz);
    const speed=Math.min(3.6,Math.max(1.2,Math.hypot(velocity.x,velocity.z)*.7));
    person.push={vx:nx*speed,vz:nz*speed,time:.45};
  }
  return true;
}
function updateResidentPushes(dt){
  for(const person of people){
    const push=person.push;if(!push)continue;
    if(person.ragdoll||person.inBus){person.push=null;person.g.rotation.x=0;continue;}
    const x=person.g.position.x+push.vx*dt,z=person.g.position.z+push.vz*dt;
    if(residentPushSpace(person,x,z)){pedestrianLife.place(person,x,z);person.indoor=!!placeContaining(x,z);}
    else push.vx=push.vz=0;
    const speed=Math.hypot(push.vx,push.vz);
    if(speed>.1){
      const facing=Math.atan2(push.vx,push.vz),turn=Math.atan2(Math.sin(facing-person.g.rotation.y),Math.cos(facing-person.g.rotation.y));
      person.g.rotation.y+=turn*(1-Math.exp(-dt*9));
    }
    const reaction=Math.min(1,push.time*3);person.g.rotation.x=.12*reaction;
    person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=(i<2?Math.sin(gameTime*11+i*Math.PI)*.25:-.55)*reaction);
    const damping=Math.exp(-5*dt);push.vx*=damping;push.vz*=damping;push.time-=dt;
    if(push.time<=0){
      person.push=null;person.g.rotation.x=0;person.g.userData.limbs?.forEach(limb=>limb.rotation.x=0);
      if(person.axis&&!npcLife?.managed.has(person)){person.origin=person.g.position[person.axis];if(person.curved)person.curveOffset=person.g.position.z-curvedRoadZ(person.g.position.x)-person.side*8.6;}
    }
  }
}
function vehicleObstacleAt(car,x,z){
  if(!vehicleFootprintBlocked(car,x,z))return null;
  const other=cars.find(v=>v!==car&&vehicleBoundsOverlap(car,x,z,v,v.g.position.x,v.g.position.z));
  if(other)return {hitVehicle:{other,x,z}};
  const a=car.g.rotation.y,offset=car.centerOffset||0;
  const cx=x+Math.sin(a)*offset,cz=z+Math.cos(a)*offset;
  for(const wall of nearbySolids(cx,cz,Math.hypot(car.width,car.length)/2+.15)){
    const shape={g:{rotation:{y:0}},width:wall.w,length:wall.d};
    const normal=vehicleContact(car,x,z,shape,wall.x,wall.z);
    if(normal)return {hitSolid:normal};
  }
  return {}; // World boundary or a moving door.
}
function moveWithCollision(g,dx,dz,r){
  const distance=Math.hypot(dx,dz), steps=Math.max(1,Math.ceil(distance/.25));let hitX=false,hitZ=false,hitVehicle=null,hitSolid=null;
  const movingVehicle=cars.find(car=>car.g===g);
  const blocked=(x,z)=>{
    if(movingVehicle){
      const contact=vehicleObstacleAt(movingVehicle,x,z);if(!contact)return false;
      if(contact.hitVehicle&&!hitVehicle)hitVehicle=contact.hitVehicle;
      if(contact.hitSolid&&!hitSolid)hitSolid=contact.hitSolid;
      return true;
    }
    return collides(x,z,r,g===player?g.position.y:null,2.4,g===player)||
      (g===player&&z<shoreZ&&!marine.dockAt(x,z)&&boats.some(b=>marine.nearHull(b,x,z)<r))||vehicleBlocks(x,z,r,g,g===player?g.position.y:null)||
      (g===player?!pushResidents(x,z):personBlocks(x,z,g));
  };
  for(let i=0;i<steps;i++){
    if(dx&&!blocked(g.position.x+dx/steps,g.position.z))g.position.x+=dx/steps;else if(dx)hitX=true;
    if(dz&&!blocked(g.position.x,g.position.z+dz/steps))g.position.z+=dz/steps;else if(dz)hitZ=true;
  }
  return {hitX,hitZ,hitVehicle,hitSolid};
}

function incidentPoint(car,x,z){
  const a=car.g.rotation.y,p=car.g.position;
  return {x:p.x+Math.cos(a)*x+Math.sin(a)*z,z:p.z-Math.sin(a)*x+Math.cos(a)*z};
}
function incidentSpace(car,x,z,actor=null){
  return !collides(x,z,.35)&&!vehicleBlocks(x,z,.35,car.g)&&
    !pedestrianLife.occupied(actor,x,z)&&
    (vehicle||playerActor.ragdoll||Math.hypot(player.position.x-x,player.position.z-z)>.9);
}
function incidentSegment(car,a,b){
  const count=Math.max(1,Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/.2));
  for(let i=0;i<=count;i++)if(!incidentSpace(car,a.x+(b.x-a.x)*i/count,a.z+(b.z-a.z)*i/count))return false;
  return true;
}
const vehicleIncidents=createVehicleIncidents({
  findExit(car){
    for(const side of [-1,1]){
      const seatZ=car.seatMount?.z??-.32,seat=incidentPoint(car,car.seatMount?.x??-.48,seatZ);
      const edge=incidentPoint(car,side*(car.width/2+.4),seatZ),exit=incidentPoint(car,side*2.8,seatZ);
      const inspection=[incidentPoint(car,side*2.8,1.8)];
      if(!incidentSegment(car,edge,exit)||!incidentSegment(car,exit,inspection[0]))continue;
      const front=incidentPoint(car,side*2.8,car.length/2+.6);
      if(incidentSegment(car,inspection[0],front))inspection.push(front);
      return {side,seat,exit,inspection};
    }
    return null;
  },
  makeActor(car,seat){
    const g=human('#76958e','#d9ac88');g.position.set(seat.x,groundHeight(seat.x,seat.z),seat.z);g.rotation.y=car.g.rotation.y;scene.add(g);
    const actor={g,name:'Motorista',role:'driver',stationary:true,incidentCar:car,line:'Vou conferir a batida e já volto para o carro.',phase:0};
    people.push(actor);return actor;
  },
  removeActor(actor){actor.g.removeFromParent();const index=people.indexOf(actor);if(index>=0)people.splice(index,1);},
  moveActor(actor,goal,dt,car){
    const p=actor.g.position,dx=goal.x-p.x,dz=goal.z-p.z,distance=Math.hypot(dx,dz);
    if(distance<.08){actor.g.userData.limbs.forEach(l=>l.rotation.x=0);return true;}
    const step=Math.min(distance,dt*1.65),x=p.x+dx/distance*step,z=p.z+dz/distance*step;
    if(!incidentSpace(car,x,z,actor)){actor.g.userData.limbs.forEach(l=>l.rotation.x=0);return false;}
    pedestrianLife.place(actor,x,z);actor.g.rotation.y=Math.atan2(dx,dz);
    actor.g.userData.limbs.forEach((l,i)=>l.rotation.x=Math.sin(gameTime*7+(i===0||i===3?0:Math.PI))*.35);return false;
  },
  setDoor(car,side,amount){if(car.carDoors?.[side])car.carDoors[side].rotation.y=-side*amount*1.05;},
  isTalking:actor=>activeSpeech?.target===actor||!!actor.push,
  lookAt(actor,car){actor.g.rotation.y=Math.atan2(car.g.position.x-actor.g.position.x,car.g.position.z-actor.g.position.z);actor.g.userData.limbs[2].rotation.x=-.35+Math.sin(gameTime*2)*.1;},
  impact(car,speed){car.debugHoldUntil=0;dust(car.g.position.x,car.g.position.y+.4,car.g.position.z,8,Math.min(3,speed*.2));}
});

function vehicleMass(car){return car.kind==='bus'?5:car.role==='fire'?3:car.kind==='bike'?.22:car.design?.mass??1;}
function vehicleWorldVelocity(car){
  const mass=vehicleMass(car),width=car.width??2.5,length=car.length??4.6,inertia=mass*(width*width+length*length)/12;
  if(car.impact)return {x:car.impact.x,z:car.impact.z,angular:car.impact.angular||0,mass,inertia};
  const angle=car.g.rotation.y,lateral=car===vehicle||car.auto?(car.lateralSpeed||0):0;
  return {x:Math.sin(angle)*car.velocity+Math.cos(angle)*lateral,z:Math.cos(angle)*car.velocity-Math.sin(angle)*lateral,
    angular:car===vehicle||car.auto?(car.yawRate||0)+(car.impactYaw||0):0,mass,inertia};
}
function vehicleImpactContact(a,ax,az,b,bx,bz){
  function center(car,x,z){const angle=car.g.rotation.y;return {x:x+Math.sin(angle)*(car.centerOffset||0),z:z+Math.cos(angle)*(car.centerOffset||0)};}
  function nearest(car,p,other){
    const angle=car.g.rotation.y,c=Math.cos(angle),s=Math.sin(angle),dx=other.x-p.x,dz=other.z-p.z;
    const x=THREE.MathUtils.clamp(c*dx-s*dz,-(car.width??2.5)/2,(car.width??2.5)/2);
    const z=THREE.MathUtils.clamp(s*dx+c*dz,-(car.length??4.6)/2,(car.length??4.6)/2);
    return {x:p.x+c*x+s*z,z:p.z-s*x+c*z};
  }
  const ca=center(a,ax,az),cb=center(b,bx,bz),pa=nearest(a,ca,cb),pb=nearest(b,cb,ca);
  const x=(pa.x+pb.x)/2,z=(pa.z+pb.z)/2;
  return {a:{x:x-ca.x,z:z-ca.z},b:{x:x-cb.x,z:z-cb.z}};
}
function setVehicleImpactVelocity(car,value){
  if(car===vehicle&&car.kind!=='bus'){
    const angle=car.g.rotation.y;
    car.velocity=value.x*Math.sin(angle)+value.z*Math.cos(angle);
    car.lateralSpeed=value.x*Math.cos(angle)-value.z*Math.sin(angle);
    car.impactYaw=THREE.MathUtils.clamp(value.angular-(car.yawRate||0),-2.5,2.5);
    return;
  }
  if(car.auto&&!car.impact){
    car.rejoinRoad=car.laneRoad??(car.axis==='z'?Math.round(car.g.position.x/72)*72:Math.round(car.g.position.z/72)*72);
    car.turning=null;car.overtake=null;
  }
  car.velocity=0;
  car.impact=Math.hypot(value.x,value.z)>.08||Math.abs(value.angular)>.04?{x:value.x,z:value.z,angular:THREE.MathUtils.clamp(value.angular,-2.5,2.5)}:null;
  if(car.auto&&!car.impact)car.recoveringRoute=true;
}
function collideVehicles(car,hit){
  const other=hit.other,normal=vehicleContact(car,hit.x,hit.z,other,other.g.position.x,other.g.position.z);
  if(!normal)return false;
  const contact=vehicleImpactContact(car,hit.x,hit.z,other,other.g.position.x,other.g.position.z);
  const response=resolveVehicleImpact(vehicleWorldVelocity(car),vehicleWorldVelocity(other),normal,.12,contact);
  if(!response)return false;
  if(response.closing>3){
    cityAudio.play('impact',car.g.position.x,car.g.position.z);
    dust((car.g.position.x+other.g.position.x)/2,(car.g.position.y+other.g.position.y)/2+.4,(car.g.position.z+other.g.position.z)/2,7,Math.min(3,response.closing*.2));
  }
  vehicleIncidents.begin(other,response.closing,gameTime);
  if(car.auto)vehicleIncidents.begin(car,response.closing,gameTime);
  setVehicleImpactVelocity(car,response.a);setVehicleImpactVelocity(other,response.b);
  return true;
}
function collideSolid(car,normal){
  const arm=vehicleSupportArm(car.g.rotation.y,car.width??2.5,car.length??4.6,normal);
  const response=resolveVehicleImpact(vehicleWorldVelocity(car),{x:0,z:0,mass:Infinity},normal,.05,{a:arm});
  if(!response)return false;
  setVehicleImpactVelocity(car,response.a);
  return true;
}
function updateVehicleImpacts(dt){
  for(const car of cars){
    const impact=car.impact;if(!impact)continue;
    const oldX=car.g.position.x,oldZ=car.g.position.z,oldAngle=car.g.rotation.y,dx=impact.x*dt,dz=impact.z*dt;
    if(pedestrianOverlaps(car,oldX+dx,oldZ+dz)){impact.x=0;impact.z=0;}
    else{
      const result=moveWithCollision(car.g,dx,dz,car.kind==='bike'?.55:1.3);
      const transferred=result.hitVehicle&&collideVehicles(car,result.hitVehicle);
      if(!transferred){if(result.hitX)impact.x=0;if(result.hitZ)impact.z=0;}
    }
    const current=car.impact;if(!current)continue;
    if(current.angular){
      const previous=car.g.rotation.y;car.g.rotation.y+=current.angular*dt;
      if(vehicleFootprintBlocked(car,car.g.position.x,car.g.position.z)||pedestrianOverlaps(car,car.g.position.x,car.g.position.z)){
        car.g.rotation.y=previous;current.angular=0;
      }
    }
    if(car.incident?.route){
      const route=car.incident.route,angle=car.g.rotation.y-oldAngle,c=Math.cos(angle),s=Math.sin(angle);
      for(const point of [route.seat,route.exit,...route.inspection]){
        const x=point.x-oldX,z=point.z-oldZ;point.x=car.g.position.x+c*x+s*z;point.z=car.g.position.z-s*x+c*z;
      }
    }
    const forward=current.x*Math.sin(car.g.rotation.y)+current.z*Math.cos(car.g.rotation.y);
    rotateVehicleWheels(car,dt,0,0,forward);
    const damping=Math.exp(-(isPaved(car.g.position.x,car.g.position.z)?2:2.8)*dt);
    current.x*=damping;current.z*=damping;current.angular*=Math.exp(-2.1*dt);
    if(Math.hypot(current.x,current.z)<.12&&Math.abs(current.angular)<.04){
      car.impact=null;
      if(car.auto){if(car.track)car.routeProgress=trackNearest(car.track,car.g.position.x,car.g.position.z);car.recoveringRoute=true;}
    }
  }
}

// Mobiliário urbano se parte em pedaços com gravidade.
const debris=[];
function breakProp(prop,speed,angle){
  if(!prop.active)return;
  cityAudio.play('break',prop.collider.x,prop.collider.z);
  prop.active=false;scene.remove(prop.group);
  if(prop.lamp)prop.lamp.active=false;
  if(prop.cameraObstacle){const obstacleIndex=cameraObstacles.indexOf(prop.cameraObstacle);if(obstacleIndex>=0)cameraObstacles.splice(obstacleIndex,1);}
  for(const bounds of [prop.collider,prop.backCollider]){
    if(!bounds)continue;
    bounds.active=false;
    const index=solids.indexOf(bounds);if(index>=0)solids.splice(index,1);
  }
  const palette=prop.type==='signal'?['#465756','#354744','#ff624d','#ffd358','#83ef9c']:prop.type==='lamp'?['#49615c','#a8bdab','#fff0bc']:prop.type==='bin'?['#568375','#355d54','#c9d4bc']:prop.type==='bench'?['#b47b4f','#bd8758','#465f5a']:['#cd9c72','#688b61','#e9a185'];
  const tall=prop.type==='lamp'||prop.type==='signal';
  for(let i=0;i<(prop.type==='signal'?14:tall?12:9);i++){
    const size=.2+Math.random()*.35;
    const height=tall?Math.random()*5.5:.4+Math.random();
    const mesh=part(scene,prop.collider.x+(Math.random()-.5)*.6,groundHeight(prop.collider.x,prop.collider.z)+height,prop.collider.z+(Math.random()-.5)*.5,size,size,size,palette[i%palette.length]);
    const velocity=new THREE.Vector3(Math.sin(angle)*speed*.32+(Math.random()-.5)*2,2+Math.random()*3,Math.cos(angle)*speed*.32+(Math.random()-.5)*2);
    debris.push({mesh,velocity,size,age:0,spin:(Math.random()-.5)*7});
  }
  dust(prop.collider.x,groundHeight(prop.collider.x,prop.collider.z)+.6,prop.collider.z,18,3.5,tall?'#ffdf88':'#dfc8a0');
}
function breakAhead(car,x,z){
  if(Math.abs(car.velocity)<2.5)return;
  const a=car.g.rotation.y;
  for(const prop of destructibles){
    if(!prop.active||prop.bench?.occupied)continue;
    const dx=prop.collider.x-x,dz=prop.collider.z-z;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    // Match the small collision buffer so an impact can break the prop before
    // the solid footprint stops the vehicle at its edge.
    const reach=Math.max(prop.collider.w,prop.collider.d)*.5+.12;
    if(Math.abs(lx)<(car.width??(car.kind==='bike'?.7:2.5))/2+reach&&Math.abs(lz-(car.centerOffset||0))<(car.length??(car.kind==='bike'?3:4.6))/2+reach){
      breakProp(prop,Math.abs(car.velocity),a+(car.velocity<0?Math.PI:0));
      if(car===vehicle&&(prop.type==='signal'||prop.type==='lamp'))reportCityCrime('dano',prop.group.position);
    }
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
  if(actor.isPlayer&&marine.mode!=='land')marine.fall();
  if(actor.bodyCollider)actor.bodyCollider.active=false;
  actor.ragdoll=true;actor.g.visible=false;if(actor.isPlayer){petting=null;vault=null;playerPlatform=null;cancelLink();velocity.set(0,0,0);}
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
  const face=new THREE.Group();face.position.y=-.26;head.add(face);
  buildCharacterHead(face,{part,skin:colors.skin,hair:colors.hair,style:colors.characterStyle??0});
  ragdolls.push({actor,joints,links,group,segments,head,age:0,accumulator:0,start:p.clone()});
  dust(p.x,groundHeight(p.x,p.z)+.2,p.z,10,2.5);
}
function updateRagdolls(dt){
  for(let k=ragdolls.length-1;k>=0;k--){
    const r=ragdolls[k];r.age+=dt;r.accumulator=Math.min(r.accumulator+dt,RAG_STEP*5);
    const waterBody=r.actor.isPlayer&&marine.inSea(r.start.x,r.start.z)&&!marine.dockAt(r.start.x,r.start.z);
    const support=(x,z,y)=>Math.max(groundHeight(x,z),wallFloorAt(x,z,y,.13),waterBody?marine.surface(x,z)-.34:-Infinity);
    while(r.accumulator>=RAG_STEP){
      r.accumulator-=RAG_STEP;
      for(const j of r.joints){
        const v=j.v.clone().sub(j.previous).multiplyScalar(.988);
        j.previous.copy(j.v);j.v.add(v);j.v.y-=17*RAG_STEP*RAG_STEP;
        if(collides(j.v.x,j.v.z,.13,j.previous.y-.13,.26,waterBody)){j.v.x=j.previous.x;j.v.z=j.previous.z;}
        const floor=support(j.v.x,j.v.z,j.previous.y-.13)+.13;
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
          if(collides(joint.v.x,joint.v.z,.13,joint.previous.y-.13,.26,waterBody)){joint.v.x=joint.previous.x;joint.v.z=joint.previous.z;}
          const floor=support(joint.v.x,joint.v.z,joint.previous.y-.13);
          joint.v.y=Math.max(floor+.13,joint.v.y);
        }
      }
    }
    for(const segment of r.segments){
      const a=r.joints[segment.a].v,b=r.joints[segment.b].v,dir=b.clone().sub(a);segment.mesh.position.copy(a).add(b).multiplyScalar(.5);
      segment.mesh.scale.set(segment.w,dir.length(),segment.d);segment.mesh.quaternion.setFromUnitVectors(upVector,dir.normalize());
    }
    r.head.position.copy(r.joints[2].v);
    const headDirection=r.joints[2].v.clone().sub(r.joints[1].v).normalize();r.head.quaternion.setFromUnitVectors(upVector,headDirection);
    if(r.age>3.2&&!combat.isDown(r.actor)){
      const hip=r.joints[0].v;let landing=null;
      const spots=r.actor.counterWorker?[[r.start.x,r.start.z]]:[[hip.x,hip.z],[hip.x+.7,hip.z],[hip.x-.7,hip.z],[hip.x,hip.z+.7],[hip.x,hip.z-.7],
        [hip.x+1.4,hip.z],[hip.x-1.4,hip.z],[r.start.x,r.start.z]];
      for(const [x,z] of spots){
        if(!collides(x,z,.45,null,2.4,waterBody)&&!vehicleBlocks(x,z,.45,null)&&!pedestrianLife.occupied(r.actor,x,z)&&
          (r.actor.isPlayer||vehicle||playerActor.ragdoll||Math.hypot(x-player.position.x,z-player.position.z)>.92)){landing={x,z};break;}
      }
      if(!landing)continue;
      if(r.actor.isPlayer){const water=waterBody&&!marine.dockAt(landing.x,landing.z)&&marine.depth(landing.x,landing.z)>.68;
        r.actor.g.position.set(landing.x,water?marine.surface(landing.x,landing.z)-1.61:groundHeight(landing.x,landing.z),landing.z);jumpHeight=0;verticalVelocity=0;}
      else pedestrianLife.place(r.actor,landing.x,landing.z);
      r.actor.g.visible=true;r.actor.ragdoll=false;if(r.actor.bodyCollider)r.actor.bodyCollider.active=true;
      if(!r.actor.isPlayer&&!r.actor.indoor&&!r.actor.stationary)r.actor.origin=r.actor.g.position[r.actor.axis];
      scene.remove(r.group);ragdolls.splice(k,1);
    }
  }
}

const keys=Object.create(null), playerActor={g:player,isPlayer:true,ragdoll:false};
const combatView=createCombatView({THREE,part,scene,player:playerActor,hand:playerMotion.weaponHand,camera});
const combatRay=new THREE.Raycaster(),aimScreen=new THREE.Vector2(0,.10),aimWorld=new THREE.Vector3();
const aimShoulder=new THREE.Vector3();
let aimFraming=0;
const muzzlePoint=new THREE.Vector3(),shotDirection=new THREE.Vector3(),chestPoint=new THREE.Vector3();
const fixedShotCover=new WeakMap();
let aimSample=null,nextAimSample=0,aimToggle=false,rightAim=false,firePointer=null,recoilUntil=0;
const defendingResidents=new Map();
const combat=createCombat({player:playerActor,onShot:shootFromHand,
  onState(event,info){
    const ammo=`${info.loaded} / ${info.reserve}`,label=event==='equip'?`${info.name} · ${ammo}`:
      event==='reload'?'Recarregando…':event==='ready'?`${info.name} · ${ammo}`:event==='shot'?ammo:info.reserve?'Recarregue':'Sem munição';
    combatView.notice(label);
    if(event==='reload')cityAudio.play('reload',player.position.x,player.position.z);
  },
  onDamage(actor,{hp,down,stun,source}){
    if(actor.isPlayer&&surveyDrone.active)endDroneFlight();
    if(activeSpeech?.target===actor||activeSpeech?.target?.attendant===actor)clearSpeech();
    if(!actor.isPlayer)pedestrianLife.interrupt(actor,gameTime);
    actor.combatFlinch=gameTime+.72;
    if(!down&&!actor.isPlayer&&source===playerActor&&!actor.child&&!actor.stationary&&!actor.sitting&&
      actor.g.position.distanceTo(player.position)<2.2&&!defendingResidents.has(actor))defendingResidents.set(actor,gameTime+.28);
    if(down){
      if(actor.isPlayer){dropCombatAim();if(sitting){sitting.bench.occupied=false;sitting=null;}}
      const origin=source?.g?.position||source;const hit=new THREE.Vector3();
      if(origin)hit.copy(actor.g.position).sub(origin).setY(0).normalize().multiplyScalar(stun?.65:1.6);
      hit.y=stun?.2:1.1;createRagdoll(actor,hit);
    }
    combatView.notice(down?(actor.isPlayer?'Caído · recuperando':stun?'Atordoado':'Incapacitado'):`${actor.isPlayer?'Vida':actor.name||'Morador'} · ${hp}/100`,actor,down?2.2:1.25);
  },onRecover(actor,hp){if(actor.isPlayer)combatView.notice(hp===100?'Vida recuperada':`Recuperando · ${Math.round(hp)}/100`,actor);}
});
function combatCanUse(){return !settings.open&&!surveyDrone.active&&!surveillance.active&&!vehicle&&!vehicleTransition&&!sitting&&!petting&&!vault&&!scanning&&
  marine.mode==='land'&&!playerActor.ragdoll&&!combat.isDown()&&(typeof policeCustody!=='function'||!policeCustody());}
function dropCombatAim(){aimToggle=false;rightAim=false;firePointer=null;combat.aim(false);}
function updateSelfDefense(){
  for(const [resident,at] of defendingResidents){
    if(resident.ragdoll||combat.isDown(resident)||!combatCanUse()){defendingResidents.delete(resident);continue;}
    if(gameTime<at)continue;defendingResidents.delete(resident);
    if(resident.g.position.distanceTo(player.position)>2.2||!interactionReachable(player.position.x,player.position.z,resident.g.position))continue;
    combat.damage(playerActor,15,{source:resident});cityAudio.play('impact',player.position.x,player.position.z);
  }
}
function setCombatAim(){
  if(!combatCanUse()){dropCombatAim();return;}
  if((rightAim||aimToggle)&&!combat.equipped)combat.select('pulse');
  combat.aim(rightAim||aimToggle);nextAimSample=0;
}
function shotCover(origin,range){
  const boxes=viewObstacles(origin,range,null,true);
  for(const s of new Set(nearbySolids(origin.x,origin.z,range))){
    if(s.bodyOwner)continue;
    if(!fixedShotCover.has(s)){
      const bottom=s.bottom??groundHeight(s.x,s.z),top=s.supportTop??s.vaultTop??(bottom+(s.opaque?3.4:1.08));
      fixedShotCover.set(s,{x:s.x,z:s.z,w:s.w,d:s.d,bottom,top,rangeBackstop:s.rangeBackstop});
    }
    boxes.push(fixedShotCover.get(s));
  }
  return boxes;
}
function sampleCombatAim(){
  combatRay.setFromCamera(aimScreen,camera);
  const ray=combatRay.ray,reach=(combat.weaponInfo(combat.equipped)?.range||18)+camera.position.distanceTo(player.position);
  return traceShot(ray.origin,ray.direction,reach,shotCover(ray.origin,reach),people,groundHeight);
}
function startleWitnesses(origin,hit,w){
  const range=w.id==='pulse'?4.5:13;
  for(const p of people){
    if(p.ragdoll||p.inBus||p.role==='police'||p===hit.actor)continue;
    const at=p.g.position,nearShooter=Math.hypot(at.x-origin.x,at.z-origin.z)<range;
    const nearImpact=hit.kind!=='air'&&Math.hypot(at.x-hit.point.x,at.z-hit.point.z)<3.4;
    if(!(nearShooter&&interactionReachable(at.x,at.z,origin))&&!(nearImpact&&interactionReachable(at.x,at.z,hit.point)))continue;
    combat.startle(p,4);p.combatFlinch=gameTime+.72;pedestrianLife.interrupt(p,gameTime);
    if(activeSpeech?.target===p||activeSpeech?.target?.attendant===p)clearSpeech();
  }
}
function shootFromHand(w){
  const aimed=sampleCombatAim();aimWorld.copy(aimed.point);combatView.pose(w.id,true,aimWorld);
  combatView.barrel(w.id,muzzlePoint);chestPoint.copy(player.position);chestPoint.y+=1.55;
  const cover=shotCover(player.position,w.range+2),barrelLength=chestPoint.distanceTo(muzzlePoint);
  shotDirection.copy(muzzlePoint).sub(chestPoint).normalize();
  let hit=traceShot(chestPoint,shotDirection,barrelLength,cover,[],groundHeight),origin=chestPoint;
  if(hit.kind==='air'){
    origin=muzzlePoint;shotDirection.copy(aimWorld).sub(muzzlePoint).normalize();
    hit=traceShot(origin,shotDirection,w.range,cover,people,groundHeight);
  }
  combatView.burst(origin,hit.point,w.id,hit.kind!=='air',true);recoilUntil=gameTime+.14;
  cityAudio.play(w.id,origin.x,origin.z);
  const practice=practiceRange.impact(player.position,hit);
  if(!practice)startleWitnesses(origin,hit,w);
  if(practice==='plate')cityAudio.play('plate',hit.point.x,hit.point.z);
  if(w.id!=='pulse'&&!practice&&typeof reportCityCrime==='function')reportCityCrime('disparo',player.position,{loud:true});
  if(hit.actor&&combat.damage(hit.actor,w.damage,{stun:w.stun,source:playerActor})&&typeof reportCityCrime==='function')
    reportCityCrime('agressao',hit.actor.g.position,{victim:hit.actor});
  aimSample=hit;
}
function applyCityHit(source,actor,value){
  const p=source?.g?.position||source;
  if(!p||!actor||actor.ragdoll||actor.inBus||actor.g.visible===false)return false;
  const start=new THREE.Vector3(p.x,p.y+(source.g?1.55:0),p.z),end=actor.g.position.clone();end.y+=1.34;
  const length=start.distanceTo(end);if(length>65||length<.02)return false;
  const dir=end.sub(start).normalize(),result=traceShot(start,dir,length+.3,shotCover(start,length+.45),[actor],groundHeight);
  combatView.burst(start,result.point,'sidearm',true);cityAudio.play('sidearm',start.x,start.z);
  return result.actor===actor&&combat.damage(actor,value,{source});
}
let vehicle=null, vehicleTransition=null, petting=null, sitting=null, playerPlatform=null, velocity=new THREE.Vector3(), verticalVelocity=0, jumpHeight=0, yaw=.8, pitch=.24, zoom=7.1;
const jumpGravity=22;
let jumpOriginY=null;
let vault=null, strideSpeed=0;
const actionPoint=new THREE.Vector3(),petContact=new THREE.Vector3();
let targetYaw=yaw,targetPitch=pitch,targetZoom=zoom,lastManualLook=-Infinity;
const walkCamera=createWalkCamera(),driveCamera=createVehicleCamera();
const cameraPivot=new THREE.Vector3(),cameraTarget=new THREE.Vector3(),smoothLook=new THREE.Vector3(),placeCamera=createCameraBoom();
let gameTime=0, walkPhase=0, currentPlace=null, stepTimer=0, cameraReady=false,interiorView=0,vehicleView=0;
const isTouch=window.matchMedia('(pointer: coarse)').matches;
if(isTouch)$('#touch').classList.remove('hidden');

// Uma fala acompanha o morador ou o quadro no próprio mundo, sem pausar o jogo.
const speechCanvas=document.createElement('canvas');speechCanvas.width=1024;speechCanvas.height=384;
const speechContext=speechCanvas.getContext('2d'),speechTexture=new THREE.CanvasTexture(speechCanvas);
speechTexture.colorSpace=THREE.SRGBColorSpace;
const speechSprite=new THREE.Sprite(new THREE.SpriteMaterial({map:speechTexture,transparent:true,depthWrite:false}));
speechSprite.scale.set(6.2,2.325,1);speechSprite.visible=false;scene.add(speechSprite);
let activeSpeech=null,ambientLine=null,ambientSpeaker=null;
function clearSpeech(){
  const person=activeSpeech?.target;
  if(person?.g&&activeSpeech.pose){
    person.g.rotation.y=activeSpeech.pose.yaw;
    person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=activeSpeech.pose.limbs[i]);
  }
  activeSpeech=null;ambientLine=null;ambientSpeaker=null;speechSprite.visible=false;
}
function showSpeech(target,name,line){
  if(target.g)pedestrianLife.interrupt(target,gameTime);
  clearSpeech();
  const pose=target.g?{yaw:target.g.rotation.y,limbs:target.g.userData.limbs?.map(limb=>limb.rotation.x)||[]}:null;
  paintSpeech(name,line,!!target.display);
  speechSprite.material.opacity=1;speechSprite.visible=true;
  activeSpeech={target,name,line,pose,until:gameTime+Math.max(5.5,Math.min(9,line.length/11))};
  updateSpeech();
}
function paintSpeech(name,line,display,ambient=false){
  const ctx=speechContext;ctx.clearRect(0,0,1024,384);
  const bodyFont=ambient?'550 57px system-ui, sans-serif':'500 45px system-ui, sans-serif';
  ctx.font=bodyFont;
  const lines=[];let current='';
  for(const word of line.split(' ')){
    const next=current?`${current} ${word}`:word;
    if(ctx.measureText(next).width>925&&current){lines.push(current);current=word;}else current=next;
  }
  if(current)lines.push(current);
  const top=ambient?Math.max(12,320-108-lines.length*66):12;
  ctx.fillStyle='#faf4dc';ctx.strokeStyle='#668479';ctx.lineWidth=7;
  ctx.beginPath();ctx.moveTo(12,top);ctx.lineTo(1012,top);ctx.lineTo(1012,320);
  if(!display){ctx.lineTo(568,320);ctx.lineTo(512,371);ctx.lineTo(456,320);}
  ctx.lineTo(12,320);ctx.closePath();ctx.fill();ctx.stroke();
  ctx.textAlign='left';ctx.textBaseline='top';ctx.fillStyle='#557a6b';ctx.font=ambient?'700 45px system-ui, sans-serif':'700 39px system-ui, sans-serif';
  if(!display)ctx.fillText(name.toUpperCase(),48,top+22,928);
  ctx.fillStyle='#263d38';ctx.font=bodyFont;
  lines.forEach((text,i)=>ctx.fillText(text,48,top+(ambient?80:93)+i*(ambient?66:59),928));
  speechTexture.needsUpdate=true;
}
function updateSpeech(){
  if(!activeSpeech){
    const ambient=pedestrianLife.utterance(gameTime);
    if(!ambient||playerActor.ragdoll||vehicle){ambientLine=null;ambientSpeaker=null;speechSprite.visible=false;return;}
    const {speaker,line,until}=ambient,p=speaker.g.position;
    if(speaker!==ambientSpeaker||line!==ambientLine){ambientSpeaker=speaker;ambientLine=line;paintSpeech(speaker.name,line,false,true);}
    speechSprite.position.set(p.x,p.y+2.45*speaker.g.scale.y,p.z);
    const worldPerPixel=2*Math.tan(camera.fov*Math.PI/360)*camera.position.distanceTo(speechSprite.position)/innerHeight;
    const width=Math.min(innerWidth*.75,270)*worldPerPixel;
    speechSprite.position.y+=width*.1875;speechSprite.scale.set(width,width*.375,1);
    speechSprite.material.opacity=THREE.MathUtils.clamp((until-gameTime)*3,0,1);speechSprite.visible=true;
    return;
  }
  const {target,until}=activeSpeech,person=target.g,bus=target.bus;
  const x=person?person.position.x:target.x,z=person?person.position.z:target.z;
  if(gameTime>=until||playerActor.ragdoll||target.ragdoll||
    (bus?vehicle!==bus:vehicle||Math.hypot(player.position.x-x,player.position.z-z)>7)){clearSpeech();return;}
  if(bus)speechSprite.position.set(bus.g.position.x,bus.g.position.y+4.5,bus.g.position.z);
  else if(person)speechSprite.position.set(x,person.position.y+3.8,z);
  else speechSprite.position.set(target.display.x,target.display.y,target.display.z);
  // Mantém as letras legíveis ao variar o zoom, sem deixar o balão ocupar a tela.
  const worldPerPixel=2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2))*camera.position.distanceTo(speechSprite.position)/innerHeight;
  const minWidth=Math.min(350,innerWidth*.85)*worldPerPixel,maxWidth=Math.min(460,innerWidth*.95)*worldPerPixel;
  const width=THREE.MathUtils.clamp(6.2,minWidth,maxWidth),height=width*384/1024;
  speechSprite.scale.set(width,height,1);
  if(person)speechSprite.position.y=person.position.y+2.5+height/2;
  speechSprite.material.opacity=Math.min(1,(until-gameTime)*2);
}
function interactionReachable(x,z,start=player.position){
  const steps=Math.max(1,Math.ceil(Math.hypot(x-start.x,z-start.z)/.18));
  for(let i=1;i<steps;i++){
    const px=start.x+(x-start.x)*i/steps,pz=start.z+(z-start.z)*i/steps;
    for(const w of nearbySolids(px,pz,.05))if(w.opaque&&Math.abs(px-w.x)<w.w/2+.04&&Math.abs(pz-w.z)<w.d/2+.04)return false;
    for(const p of places){
      const d=p.door,ax=d.x+d.width/2+d.offset;
      if(distanceToSegment(px,pz,ax,d.z,ax-Math.cos(d.angle)*d.width,d.z+Math.sin(d.angle)*d.width)<.14)return false;
    }
  }
  return true;
}
function seatPoint(b,side,front){
  return {x:b.x+Math.cos(b.rotation)*side+Math.sin(b.rotation)*front,
    z:b.z-Math.sin(b.rotation)*side+Math.cos(b.rotation)*front};
}
function seatLandings(b){
  const front=b.depth/2+.68,side=b.width/2+.68;
  const points=b.sideEntry?[]:[seatPoint(b,0,front)];
  if(b.sideEntry||b.openEnds)points.push(seatPoint(b,-side,0),seatPoint(b,side,0));
  return points;
}
function seatSpaceFree({x,z}){
  return !collides(x,z,.55)&&!vehicleBlocks(x,z,.55,null)&&!personBlocks(x,z,player,groundHeight(x,z));
}
function seatPathFree(from,to){
  const steps=Math.max(1,Math.ceil(Math.hypot(to.x-from.x,to.z-from.z)/.15));
  for(let i=1;i<=steps;i++){
    if(!seatSpaceFree({x:from.x+(to.x-from.x)*i/steps,z:from.z+(to.z-from.z)*i/steps}))return false;
  }
  return true;
}
function nearestInteraction(){
  // E continues the current conversation even if another passerby comes closer.
  const talking=activeSpeech?.target;
  if(talking?.g&&combat.canTalk(talking)&&Math.hypot(player.position.x-talking.g.position.x,player.position.z-talking.g.position.z)<2.9&&interactionReachable(talking.g.position.x,talking.g.position.z))return {type:'person',data:talking};
  if(cityBus.atStop){
    const door=busDoorPoint(cityBus);
    if(Math.hypot(player.position.x-door.x,player.position.z-door.z)<2.8&&interactionReachable(door.x,door.z))return {type:'bus',data:cityBus};
  }
  let best=null, bestDistance=3.4;
  for(const person of people){
    if(person.counterWorker||person.inBus||!combat.canTalk(person))continue;
    const distance=Math.hypot(player.position.x-person.g.position.x,player.position.z-person.g.position.z);
    if(distance<bestDistance&&distance<2.9&&interactionReachable(person.g.position.x,person.g.position.z)){bestDistance=distance;best={type:'person',data:person};}
  }
  if(cityBus.atStop){
    const door=busDoorPoint(cityBus),distance=Math.hypot(player.position.x-door.x,player.position.z-door.z);
    if(distance<bestDistance&&distance<3.4&&interactionReachable(door.x,door.z)){bestDistance=distance;best={type:'bus',data:cityBus};}
  }
  for(const car of cars){
    if(car.kind==='bus'||car.incident||car.impact||Math.abs(car.velocity)>.7)continue;
    const dx=player.position.x-car.g.position.x,dz=player.position.z-car.g.position.z,a=car.g.rotation.y;
    const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    let target=car.g.position,distance=Math.hypot(dx,dz);
    if(car.kind!=='bike'){
      const halfWidth=car.width/2;
      if(Math.abs(lx)<halfWidth+.3||Math.abs(lx)>halfWidth+1.8||Math.abs(lz+.3)>1.45||player.position.y>car.g.position.y+1)continue;
      target=incidentPoint(car,Math.sign(lx)*halfWidth,-.3);distance=Math.hypot(player.position.x-target.x,player.position.z-target.z);
    }
    if(distance<bestDistance&&distance<3.4&&interactionReachable(target.x,target.z)){
      const side=Math.sign(lx)||1,displaced=car.auto?driverExitFor(car,side):null;
      if(car.auto&&!displaced)continue;
      bestDistance=distance;best={type:'car',data:car,side,displaced};
    }
  }
  for(const animal of animals){
    if(animal.flight)continue;
    const distance=Math.hypot(player.position.x-animal.g.position.x,player.position.z-animal.g.position.z);
    if(distance<bestDistance&&distance<2.4&&interactionReachable(animal.g.position.x,animal.g.position.z)){bestDistance=distance;best={type:'animal',data:animal};}
  }
  for(const bench of benches){
    if(bench.occupied||!bench.group.parent)continue;
    const distance=Math.hypot(player.position.x-bench.x,player.position.z-bench.z);
    if(distance>=bestDistance||distance>=2.4||!interactionReachable(bench.x,bench.z))continue;
    const landing=seatLandings(bench).find(p=>Math.hypot(player.position.x-p.x,player.position.z-p.z)<1.45&&seatPathFree(player.position,p));
    if(!landing)continue;
    bestDistance=distance;best={type:'bench',data:bench,landing};
  }
  for(const service of servicePoints){
    if(service.frontOnly&&player.position.z<service.z-.6)continue;
    if(service.place&&(placeContaining(player.position.x,player.position.z)!==service.place||Math.abs(player.position.y-groundHeight(service.x,service.z))>1))continue;
    if(service.kind==='district-guide'&&(player.position.z-54.90)*(service.z-54.90)<0)continue;
    const distance=Math.hypot(player.position.x-service.x,player.position.z-service.z);
    if(distance<bestDistance&&distance<2.4&&interactionReachable(service.x,service.z)){bestDistance=distance;best={type:'service',data:service};}
  }
  const marketCandidate=fishMarket.nearest(player.position,interactionReachable,bestDistance);
  return marketCandidate||best;
}
function busDoorPoint(bus){
  const a=bus.g.rotation.y,p=bus.g.position;
  return {x:p.x+Math.cos(a)*1.6+Math.sin(a)*2.4,z:p.z-Math.sin(a)*1.6+Math.cos(a)*2.4};
}
function busLanding(bus,actor){
  const a=bus.g.rotation.y,p=bus.g.position,rx=Math.cos(a),rz=-Math.sin(a),fx=Math.sin(a),fz=Math.cos(a);
  for(const longitudinal of [2.4,1,-1,3.2]){
    for(let side=bus.width/2+.5;side<=4.91;side+=.1){
      const x=p.x+rx*side+fx*longitudinal,z=p.z+rz*side+fz*longitudinal;
      if(collides(x,z,.42)||vehicleBlocks(x,z,.42,bus.g)||pedestrianLife.occupied(actor,x,z)||
        (actor!==playerActor&&!vehicle&&!playerActor.ragdoll&&Math.hypot(x-player.position.x,z-player.position.z)<.8))break;
      if(side>=4.4&&!pointOnRoad(x,z))return {x,z};
    }
  }
  return null;
}
function exitBusAtStop(bus,available=null){
  if(!bus.atStop||bus.impact||bus.recoveringRoute)return false;
  const landing=available||busLanding(bus,playerActor);if(!landing)return false;
  player.position.set(landing.x,groundHeight(landing.x,landing.z),landing.z);player.rotation.y=bus.g.rotation.y;
  player.visible=true;bus.rider.visible=false;bus.boardedStop=null;bus.exitRequested=false;vehicle=null;targetYaw=yaw;clearSpeech();
  velocity.set(0,0,0);jumpHeight=0;verticalVelocity=0;return true;
}
function moveAtSeat(via,to,exiting){
  const from=player.position.clone(),first=Math.hypot(from.x-via.x,from.z-via.z);
  const distance=first+Math.hypot(to.x-via.x,to.z-via.z);
  sitting.move={from,via,to,exiting,knot:distance?first/distance:0,fromBlend:sitting.blend,elapsed:0,
    duration:THREE.MathUtils.clamp(distance/4,.30,exiting?.40:.65)};
}
function standUp(){
  if(!sitting||sitting.move?.exiting)return;
  const exit=[sitting.landing,...seatLandings(sitting.bench)].find(seatSpaceFree);
  if(exit)moveAtSeat(player.position.clone(),exit,true);
}
function updateSeat(dt){
  const s=sitting,{forward,sideways}=movementInput();
  if(!forward&&!sideways)s.heldMovement=false;
  else if(!s.heldMovement&&!s.move)standUp();
  const m=s.move;if(!m)return;
  if(m.exiting&&!seatSpaceFree(m.to))return;
  m.elapsed=Math.min(m.duration,m.elapsed+dt);
  const t=THREE.MathUtils.smoothstep(m.elapsed,0,m.duration),first=t<m.knot;
  const a=first?m.from:m.via,b=first?m.via:m.to,f=first?t/m.knot:(t-m.knot)/Math.max(.0001,1-m.knot);
  player.position.x=THREE.MathUtils.lerp(a.x,b.x,f);player.position.z=THREE.MathUtils.lerp(a.z,b.z,f);
  player.position.y=groundHeight(player.position.x,player.position.z);
  faceToward(player,s.bench.rotation,dt,15);
  s.blend=m.exiting?(1-t)*m.fromBlend:THREE.MathUtils.smoothstep(t,m.knot*.7,.96);
  if(m.elapsed<m.duration)return;
  s.move=null;player.rotation.y=s.bench.rotation;
  if(m.exiting){s.bench.occupied=false;sitting=null;}
}
function driverExitFor(car,side){
  for(const exitSide of [-side,side])for(const along of [-1.7,1.5]){
    const edge=incidentPoint(car,exitSide*(car.width/2+.4),along),landing=incidentPoint(car,exitSide*(car.width/2+1.1),along);
    if(incidentSegment(car,edge,landing))return {side:exitSide,landing};
  }
  return null;
}
function vehicleExitFor(c){
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
    return {x,z,side};
  }
  return null;
}
function exitVehicle(c,landing=vehicleExitFor(c)){
  c.exitRequested=false;if(!landing)return false;
  const {x,z,side}=landing;c.velocity=0;
  if(c.kind==='bike'){
    player.position.set(x,groundHeight(x,z),z);player.rotation.y=c.g.rotation.y;jumpHeight=0;player.visible=true;
    c.spots.forEach(l=>l.visible=false);if(c.rider)c.rider.visible=false;c.lateralSpeed=0;c.yawRate=0;c.impactYaw=0;vehicle=null;targetYaw=yaw;velocity.set(0,0,0);
  }else{
    cityAudio.play('door',c.g.position.x,c.g.position.z);
    vehicleTransition={kind:'exit',car:c,side,elapsed:0,duration:1,start:incidentPoint(c,c.seatMount.x,c.seatMount.z),end:{x,z}};
  }
  return true;
}
function currentInteraction(){
  if(surveyDrone.active)return {type:'recall-drone',data:surveyDrone};
  if(surveillance.active)return {type:'leave-camera',data:surveillance.active};
  if(policeCustody())return null;
  const officer=marine.mode==='land'&&cityPolice.approaching();if(officer)return {type:'surrender',data:officer};
  if(scanning||vehicleTransition||vault||petting||playerActor.ragdoll)return null;
  if(!vehicle&&!sitting){const waterAction=marine.nearestAction();if(waterAction||marine.mode!=='land')return waterAction;}
  if(sitting){
    if(sitting.move?.exiting||![sitting.landing,...seatLandings(sitting.bench)].some(seatSpaceFree))return null;
    return {type:'stand',data:sitting.bench};
  }
  if(vehicle?.kind==='bus'){
    if(!vehicle.atStop)return vehicle.exitRequested?null:{type:'request-stop',data:vehicle};
    const landing=!vehicle.impact&&!vehicle.recoveringRoute&&busLanding(vehicle,playerActor);
    if(landing)return {type:'leave-bus',data:vehicle,landing};
    return vehicle.exitRequested?null:{type:'wait-bus-exit',data:vehicle};
  }
  if(vehicle){
    if(vehicle.exitRequested)return null;
    if(Math.hypot(vehicle.velocity,vehicle.lateralSpeed||0)>1||Math.abs(vehicle.impactYaw||0)>.12)return {type:'stop-exit',data:vehicle};
    const landing=vehicleExitFor(vehicle);
    return landing?{type:'exit-vehicle',data:vehicle,landing}:null;
  }
  const candidate=nearestInteraction();
  if((candidate?.type==='bench'||candidate?.type==='animal')&&(jumpHeight>.01||verticalVelocity>0||playerPlatform))return null;
  return candidate;
}
function interact(){
  if(combat.aiming)dropCombatAim();
  const candidate=currentInteraction();if(!candidate)return;
  if(candidate.type.startsWith('marine-')){marine.interact(candidate);return;}
  if(candidate.type==='recall-drone'){endDroneFlight();return;}
  if(candidate.type==='surrender'){cityPolice.surrender();return;}
  if(candidate.type==='leave-camera'){leavePublicCamera();return;}
  if(candidate.type==='stand'){standUp();return;}
  if(candidate.type==='request-stop'||candidate.type==='wait-bus-exit'||candidate.type==='leave-bus'){
    vehicle.exitRequested=true;
    if(candidate.type==='request-stop')showSpeech({bus:vehicle,display:true},'','Saída solicitada. Desembarque na próxima parada.');
    else if(candidate.type==='wait-bus-exit')showSpeech({bus:vehicle,display:true},'','Aguardando espaço livre para desembarcar.');
    else exitBusAtStop(vehicle,candidate.landing);
    return;
  }
  if(candidate.type==='stop-exit'){vehicle.exitRequested=true;return;}
  if(candidate.type==='exit-vehicle'){exitVehicle(vehicle,candidate.landing);return;}
  if(candidate.type==='fish-market'){fishMarket.interact(candidate.data,showSpeech,gameTime,(fish,weights)=>{
    if(!publicCounterOpen())return {ok:false,line:'A expedição espera. Volte quando a situação acalmar.'};
    const receipt=economy.dispatch(fish,weights);
    if(receipt.ok)cityAudio.play('register',candidate.data.x,candidate.data.z);
    return receipt;
  });return;}
  if(candidate.type==='bench'){
    clearSpeech();cancelLink();
    const bench=candidate.data,hip=seatPoint(bench,0,bench.depth/2-.30);
    bench.occupied=true;sitting={bench,landing:candidate.landing,blend:0,heldMovement:true};playerPlatform=null;
    moveAtSeat(candidate.landing,hip,false);
    velocity.set(0,0,0);jumpHeight=0;verticalVelocity=0;
  }else if(candidate.type==='animal'){
    clearSpeech();cancelLink();
    const animal=candidate.data,pos=animal.g.position;
    const facing=Math.atan2(pos.x-player.position.x,pos.z-player.position.z);
    const side=Math.sin(yaw-facing);
    petting={animal,elapsed:0,approachTime:0,viewYaw:facing+(side<-.18?-1:1)*Math.PI*.46};
    velocity.set(0,0,0);
  }else if(candidate.type==='service'){
    const point=candidate.data;
    if(point.anchor)useCityCounter(point);
    else if(point.kind==='fire-water')point.active=!point.active;
    else{
      showSpeech(point,point.name,'A equipe atende na recepção e faz a ronda pelas ruas da cidade.');
    }
  }else if(candidate.type==='person'){
    const person=candidate.data;
    const activities={home:['Vou passar em casa antes de continuar o dia.','Nada como um pouco de sossego em casa.'],
      work:['Vou cuidar de umas coisas no trabalho.','Estou cuidando desse cantinho para deixar tudo em ordem.'],
      paint:['Vou voltar ao mural depois desta pausa.','A maré inspirou as cores deste mural. Estou terminando alguns detalhes.'],
      eat:['Vou procurar alguma coisa para comer.','Uma pausa para comer e depois continuo o passeio.'],
      read:['Vou achar um lugar tranquilo para ler.','Esse livro está me prendendo a atenção.'],
      phone:['Vou parar um instante para ver uma mensagem.','Só estou respondendo uma mensagem. Já volto ao passeio.'],
      chat:['Vou encontrar os vizinhos para conversar.','Estávamos colocando a conversa em dia.'],
      smoke:['Vou fazer uma pausa ao ar livre.','Estou fazendo uma pausa por aqui, longe das entradas.'],
      play:['Vou brincar um pouco na praça.','Olha como a bola quica!'],rest:['Vou descansar um pouco por ali.','É bom parar e olhar a cidade de vez em quando.'],
      wait:['Vou esperar o circular na parada.','O circular passa por aqui e segue até a outra praça.']};
    const activityLine=activities[person.lifeActivity]?.[person.lifeState==='travel'?0:1];
    const lines=person.lines?.length?person.lines:[activityLine||person.line,person.line,'Bom passeio por Baía do Sol!'];
    showSpeech(person,person.name,lines[(person.talkCount||0)%lines.length]);person.talkCount=(person.talkCount||0)+1;
  }else if(candidate.type==='bus'){
    cityAudio.play('automatic',candidate.data.g.position.x,candidate.data.g.position.z);
    clearSpeech();cancelLink();
    vehicle=candidate.data;playerPlatform=null;vehicle.boardedStop=vehicle.atStop.id;vehicle.exitRequested=false;vehicle.rider.visible=true;
    player.visible=false;velocity.set(0,0,0);verticalVelocity=0;jumpHeight=0;
  }else{
    clearSpeech();cancelLink();
    const car=candidate.data,a=car.g.rotation.y,p=car.g.position,{side,displaced}=candidate;
    if(displaced){
      const g=human('#76958e','#d9ac88');const seat=incidentPoint(car,car.seatMount.x,car.seatMount.z);
      g.position.set(seat.x,p.y,seat.z);g.rotation.y=a;scene.add(g);
      const driver={g,name:'Motorista',line:'Que susto! Esse era meu carro.',stationary:false,axis:'x',origin:displaced.landing.x,range:3,dir:1,speed:.8,wait:1,phase:0,indoor:false,exitingVehicle:true};
      people.push(driver);displaced.driver=driver;displaced.start=seat;
      reportCityCrime('roubo',player.position,{victim:driver});
      const previousDriver=car.driver;car.driver=makeDriver(player.userData.shirt,player.userData.skin);car.driver.position.copy(previousDriver.position);car.g.add(car.driver);previousDriver.removeFromParent();
    }
    if(!displaced&&car.role==='police')reportCityCrime('furto',player.position);
    if(car.patrolRoute){car.pursuitSiren=false;car.emergency.on=false;}
    vehicle=car;playerPlatform=null;car.entrySide=side;car.auto=false;car.velocity=0;car.lateralSpeed=0;car.yawRate=0;car.bodyAcceleration=0;car.spots.forEach(l=>l.visible=true);
    car.driveDirection=1;car.shiftTime=0;car.pedal=0;car.handbrakeLevel=0;car.cornerAcceleration=0;car.tireSlip=0;
    velocity.set(0,0,0);verticalVelocity=0;jumpHeight=0;
    if(car.kind==='bike'){if(car.rider)car.rider.visible=true;player.visible=false;}
    else{
      cityAudio.play('door',p.x,p.z);
      vehicleTransition={kind:'enter',car,side,elapsed:0,duration:displaced?1.5:.9,start:{x:player.position.x,z:player.position.z},end:incidentPoint(car,car.seatMount.x,car.seatMount.z),displaced};
      if(car.driver)car.driver.visible=false;
    }
  }
}
function movementInput(){
  const forward=(keys.w||keys.arrowup?1:0)-(keys.s||keys.arrowdown?1:0);
  const sideways=(keys.d||keys.arrowright?1:0)-(keys.a||keys.arrowleft?1:0);
  return {forward,sideways};
}
function actionSpace(point){
  return !collides(point.x,point.z,.42,point.y)&&!vehicleBlocks(point.x,point.z,.42,player,point.y)&&
    !personBlocks(point.x,point.z,player,point.y);
}
function tryParkour(forward,sideways){
  if((!forward&&!sideways)||playerPlatform)return false;
  // The plan owns its heading even if the controls change during the vault.
  const dir={...walkCamera.direction(forward,sideways,yaw)};
  const start=player.position;let best=null;
  function consider(boxes,car){
    const plan=planParkour(start,dir,boxes,groundHeight);
    if(plan&&(!best||plan.near<best.near)){plan.car=car;best=plan;}
  }
  for(const wall of new Set(nearbySolids(start.x,start.z,2.2))){
    if(wall.vaultTop!==undefined)consider([{...wall,top:wall.vaultTop,yaw:0}],null);
  }
  for(const car of cars){
    const p=car.g.position,a=car.g.rotation.y;
    if(car.kind==='bike'||car.kind==='bus'||car.impact||car.incident||Math.abs(car.velocity)>.25||
       Math.hypot(p.x-start.x,p.z-start.z)>8)continue;
    consider(vehicleSurfaces(car).filter(s=>!s.fitting&&!s.glazing).map(s=>({x:p.x+Math.sin(a)*s.z+Math.cos(a)*(s.x||0),z:p.z+Math.cos(a)*s.z-Math.sin(a)*(s.x||0),
      w:s.w,d:s.d,yaw:a,top:p.y+s.top})),car);
  }
  if(!best)return false;
  // Sample the capsule before committing; keep checking it as the city moves.
  for(let step=1;step<=36;step++){
    if(!actionSpace(parkourPosition(best,step/36,actionPoint)))return false;
  }
  vault=best;clearSpeech();cancelLink();velocity.set(0,0,0);verticalVelocity=0;
  return true;
}
function updateParkour(dt){
  const plan=vault;
  plan.elapsed=Math.min(plan.duration,plan.elapsed+dt);
  const t=plan.elapsed/plan.duration;
  parkourPosition(plan,t,actionPoint);
  if(plan.car?.impact||!actionSpace(actionPoint)){
    vault=null;verticalVelocity=0;
    jumpHeight=Math.max(0,player.position.y-groundHeight(player.position.x,player.position.z));return;
  }
  player.position.copy(actionPoint);
  faceToward(player,Math.atan2(plan.dir.x,plan.dir.z),dt,18);
  jumpHeight=Math.max(.02,actionPoint.y-groundHeight(actionPoint.x,actionPoint.z));
  if(t<1)return;
  vault=null;jumpHeight=0;
  if(plan.climb&&plan.car){
    const p=plan.car.g.position;
    playerPlatform={car:plan.car,x:p.x,z:p.z,y:p.y,angle:plan.car.g.rotation.y};
  }
  const {forward,sideways}=movementInput();
  if(forward||sideways)velocity.set(plan.dir.x*3.4,0,plan.dir.z*3.4);
  playerMotion.land(plan.climb?3:6);
  cityAudio.play('step',actionPoint.x,actionPoint.z);
}
function faceToward(model,angle,dt,rate){
  const delta=angle-model.rotation.y;
  model.rotation.y+=Math.atan2(Math.sin(delta),Math.cos(delta))*(1-Math.exp(-dt*rate));
}
function updatePetting(dt){
  const interaction=petting,animal=interaction.animal,target=animal.g.position;
  const dx=target.x-player.position.x,dz=target.z-player.position.z,distance=Math.hypot(dx,dz);
  if(distance>2.8||animal.flight){petting=null;return;}
  const yawToPet=Math.atan2(dx,dz);
  faceToward(player,yawToPet,dt,12);
  const reachDistance=animal.type==='dog'?1.66:animal.type==='cat'?1.40:1.02;
  const remaining=distance-reachDistance;
  if(Math.abs(remaining)>.06 && !interaction.elapsed){
    interaction.approachTime+=dt;
    if(interaction.approachTime>1.1){petting=null;return;}
    const step=THREE.MathUtils.clamp(remaining,-2.2*dt,2.2*dt);
    const x=player.position.x+Math.sin(yawToPet)*step,z=player.position.z+Math.cos(yawToPet)*step;
    if(collides(x,z,.42)||vehicleBlocks(x,z,.42,null)||personBlocks(x,z,player)){petting=null;return;}
    player.position.set(x,groundHeight(x,z),z);
    strideSpeed=Math.abs(step)/dt;walkPhase+=strideSpeed*dt*3.4;
    return;
  }
  interaction.elapsed+=dt;
  if(interaction.elapsed>=2.45)petting=null;
}
function jump(){
  if(marine.mode==='boat'){keys[' ']=true;return;}if(marine.mode==='swim')return;
  if(surveyDrone.active)return;
  if(policeCustody()||surveillance.active||sitting||vault||vehicleTransition)return;
  if(vehicle){keys[' ']=true;return;}
  if(playerActor.ragdoll||jumpHeight>.01||verticalVelocity>0)return;
  petting=null;
  const {forward,sideways}=movementInput();
  if(tryParkour(forward,sideways))return;
  const runUp=THREE.MathUtils.smoothstep(Math.hypot(velocity.x,velocity.z),4.8,8.2);
  jumpOriginY=player.position.y;
  verticalVelocity=Math.sqrt(2*jumpGravity*(.32+.14*runUp));playerPlatform=null;
  footDust(3);
}
function footDust(count){
  const {x,y,z}=player.position;
  if(currentPlace||playerPlatform||isPaved(x,z)||y>groundHeight(x,z)+.04)return;
  if(Math.abs(x+36)<26&&Math.abs(z-36)<26&&(Math.abs(x+36)<3.5||Math.abs(z-36)<3.5))return;
  dust(x,y+.06,z,count,.55,z< -130?'#c7ac82':'#9d9b73');
}
const devices=[
  ...harbor.network,
  streetCinema.device,
  ...surveillance.devices.map(unit=>({label:'ACESSAR CÂMERA',unit})),
  ...publicPhones.map(unit=>({label:'TOCAR TELEFONE',unit})),
  ...places.filter(p=>p.door.automatic).map(p=>({label:'PORTA AUTOMÁTICA',unit:p.door})),
  ...cars.filter(c=>c.indicators&&c.kind!=='bus').map(c=>({label:'FREIO REMOTO',unit:c})),
  ...destructibles.filter(p=>p.type==='signal').map(p=>({label:'FECHAR CRUZAMENTO',unit:p})),
  ...servicePoints.filter(p=>p.kind==='fire-water').map(p=>({label:'PAINEL DE ÁGUA',unit:p}))
];
function harborOccupied(box,bottom,top){
  const touches=(pos,r,bodyHeight)=>pos.y+bodyHeight>bottom-.10&&pos.y<top+.11&&
    Math.abs(pos.x-box.x)<box.w/2+r&&Math.abs(pos.z-box.z)<box.d/2+r;
  if(touches(player.position,.49,2.39)||people.some(p=>!p.inBus&&touches(p.g.position,.47,2.35*p.g.scale.y)))return true;
  if(animals.some(a=>!a.flight&&touches(a.g.position,.48,.94)))return true;
  if(surveyDrone.mode!=='stowed'&&touches(surveyDrone.position,.47,.22))return true;
  return cars.some(c=>{
    const pos=c.g.position,a=c.g.rotation.y,cs=Math.abs(Math.cos(a)),sn=Math.abs(Math.sin(a));
    const w=(c.width||1.2)/2,d=(c.length||2.4)/2;
    return pos.y+(c.design?.roof||2.7)>bottom-.1&&pos.y<top+.1&&
      Math.abs(pos.x-box.x)<box.w/2+w*cs+d*sn&&Math.abs(pos.z-box.z)<box.d/2+d*cs+w*sn;
  });
}
function localAirspace(position,range,ignore=null){
  const boxes=viewObstacles(position,range,ignore,true);
  for(const s of new Set(nearbySolids(position.x,position.z,range))){
    const bottom=s.bottom??groundHeight(s.x,s.z);
    boxes.push({x:s.x,z:s.z,w:s.w,d:s.d,bottom,top:s.supportTop??s.vaultTop??bottom+(s.opaque?3.3:1.18)});
  }
  for(const p of people){
    const feet=p.g.position;
    if(!p.inBus&&!p.ragdoll&&Math.abs(feet.x-position.x)<range&&Math.abs(feet.z-position.z)<range)
      boxes.push({x:feet.x,z:feet.z,w:.63,d:.56,bottom:feet.y,top:feet.y+2.24*p.g.scale.y});
  }
  for(const boat of boats){
    const p=boat.g.position;
    if(Math.abs(p.x-position.x)>range+boat.d||Math.abs(p.z-position.z)>range+boat.d)continue;
    const rotation={cos:Math.cos(boat.heading),sin:Math.sin(boat.heading)};
    boxes.push({x:p.x,z:p.z,w:boat.w,d:boat.d,bottom:p.y-.35,top:p.y+.76,...rotation});
    if(boat.model==='sail')boxes.push({x:p.x,z:p.z,w:.45,d:boat.d*.44,bottom:p.y+.75,top:p.y+5.5,...rotation});
    else boxes.push({x:p.x,z:p.z,w:boat.w*.73,d:boat.d*.51,bottom:p.y+.74,top:p.y+(boat.model==='rescue'?2.40:1.72),...rotation});
  }
  return boxes;
}
const surveyDrone=createSurveyDrone({THREE,scene,part,groundHeight,obstacles:localAirspace});
const deviceRay=new THREE.Vector3();
const handsetEye=new THREE.Vector3();
function receiverVisible(start,point,unit){
  const length=deviceRay.subVectors(point,start).length();
  if(length<.2)return true;
  const boxes=viewObstacles(start,length,unit,true);
  return cameraClearance(start,deviceRay.divideScalar(length),length,boxes,.025)>=length-.15;
}
function clearFlightControls(){
  for(const key of Object.keys(keys))keys[key]=false;
  walkCamera.reset();touchRunKey=null;dropCombatAim();cancelLink();
  tagUntil=0;tag.visible=false;visibilityTimer=0;nextLampUpdate=0;
}
function startDroneFlight(){
  if(surveyDrone.mode==='returning'){combatView.notice('Drone voltando à mochila');return;}
  if(settings.open||surveyDrone.mode!=='stowed'||surveillance.active||vehicle||sitting||petting||vault||vehicleTransition||
    playerPlatform||playerActor.ragdoll||jumpHeight>.02||verticalVelocity>0||policeCustody()||marine.mode!=='land')return;
  if(!surveyDrone.launch(player.position,yaw)){combatView.notice('Sem espaço para lançar');return;}
  clearFlightControls();clearSpeech();combat.holster();velocity.set(0,0,0);
  cityAudio.play('drone-start',player.position.x,player.position.z);
  world.classList.add('drone-feed');
}
function endDroneFlight(){
  if(!surveyDrone.recall())return;
  restoreGroundControls();
}
function restoreGroundControls(){
  clearFlightControls();surveyDrone.showBody();world.classList.remove('drone-feed');cameraReady=false;
  camera.fov=settings.values.fov;camera.updateProjectionMatrix();
}
const tagCanvas=document.createElement('canvas');tagCanvas.width=640;tagCanvas.height=100;
const tagTexture=new THREE.CanvasTexture(tagCanvas);tagTexture.colorSpace=THREE.SRGBColorSpace;
const tag=new THREE.Sprite(new THREE.SpriteMaterial({map:tagTexture,transparent:true,depthTest:false,depthWrite:false,toneMapped:false}));
tag.renderOrder=20;tag.center.set(.5,0);scene.add(tag);tag.visible=false;
const antenna=new THREE.Vector3(),screenPoint=new THREE.Vector3(),sightRay=new THREE.Vector3();
let scanning=false,linked=null,tagUntil=0,tagText='',receipt='',feedHintUntil=0;
function devicePosition(unit){
  if(unit.antenna)antenna.copy(unit.antenna);
  else if(unit.type==='camera')antenna.copy(unit.pivot);
  else if(unit.type==='signal'){antenna.copy(unit.group.position);antenna.y+=3.1;}
  else if(unit.g){antenna.copy(unit.g.position);antenna.y+=2.6;}
  else antenna.set(unit.x,groundHeight(unit.x,unit.z)+2.8,unit.z);
}
function sightLineClear(point,unit){
  const origin=camera.position,length=sightRay.subVectors(point,origin).length();
  if(length<.25)return false;
  sightRay.divideScalar(length);
  const blockers=viewObstacles(origin,length,unit);
  return cameraClearance(origin,sightRay,length,blockers,0)>=length-.18;
}
function canJamFromCar(){return vehicle&&vehicle.kind!=='bus'&&cityPolice.device&&Math.abs(vehicle.velocity)<.6&&!vehicleTransition;}
function updateLink(){
  if(scanning&&((vehicle&&!canJamFromCar())||sitting||petting||vault||playerActor.ragdoll||vehicleTransition))cancelLink();
  if(scanning){
    const previous=linked;linked=null;let best=Infinity;
    const activeDevices=cityPolice.device?[cityPolice.device,...devices]:devices;
    for(const device of activeDevices){
      if(vehicle&&device!==cityPolice.device)continue;
      if(device.unit===surveillance.active||(device.unit.type==='signal'&&!device.unit.active))continue;
      devicePosition(device.unit);
      const remoteEye=surveyDrone.active?surveyDrone.position:surveillance.active?camera.position:null;
      const distance=remoteEye?remoteEye.distanceTo(antenna):Math.hypot(antenna.x-player.position.x,antenna.z-player.position.z);
      if(distance>(surveyDrone.active?28:surveillance.active?42:24))continue;
      const {x,y,z}=screenPoint.copy(antenna).project(camera);
      if(z< -1||z>1||Math.abs(x)>.94||Math.abs(y)>.9)continue;
      const score=x*x*camera.aspect*camera.aspect+y*y+distance*.004-(previous===device ? .035 : 0)-(device.unit.type==='chamada'?.08:0);
      const needsSight=remoteEye||device.unit.antenna||device.unit.type==='camera'||device.unit.type==='chamada';
      if(score>=best||needsSight&&!sightLineClear(antenna,device.unit))continue;
      if(surveyDrone.active&&!receiverVisible(surveyDrone.position,antenna,device.unit))continue;
      if(!remoteEye&&device.unit.antenna){
        handsetEye.copy(player.position);handsetEye.y+=1.68;
        if(!receiverVisible(handsetEye,antenna,device.unit))continue;
      }
      best=score;linked=device;
    }
  }
  const hint=!scanning&&surveillance.active&&gameTime<feedHintUntil&&gameTime>=tagUntil;
  tag.visible=!!hint||!!linked&&(scanning||gameTime<tagUntil)&&(linked.unit.type!=='signal'||linked.unit.active);
  if(!tag.visible)return;
  if(hint)antenna.copy(surveillance.active.hint);else devicePosition(linked.unit);
  tag.position.copy(antenna);
  const width=Math.min(innerWidth*.8,215)*2*Math.tan(camera.fov*Math.PI/360)*camera.position.distanceTo(antenna)/innerHeight;
  tag.scale.set(width,width*100/640,1);
  const label=hint?'E · VOLTAR AO CORPO':scanning?linked.label:receipt;
  if(label===tagText)return;
  tagText=label;const ctx=tagCanvas.getContext('2d');
  ctx.clearRect(0,0,640,100);ctx.fillStyle='#103538';ctx.fillRect(0,0,640,80);
  ctx.fillStyle='#82ffcf';ctx.fillRect(0,0,8,80);ctx.beginPath();ctx.moveTo(302,80);ctx.lineTo(320,99);ctx.lineTo(338,80);ctx.fill();
  ctx.font='bold 48px monospace';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(label,320,42,600);
  tagTexture.needsUpdate=true;
}
function beginLink(){
  if(marine.mode!=='land')return;
  if(combat.aiming)dropCombatAim();
  if(policeCustody()||(vehicle&&!canJamFromCar())||playerActor.ragdoll||vehicleTransition||vault||sitting||petting)return;
  scanning=true;updateLink();
}
function releaseLink(){
  if(!scanning)return;
  updateLink();scanning=false;
  if(!linked||(vehicle&&!canJamFromCar())||playerActor.ragdoll||vehicleTransition){linked=null;return;}
  const unit=linked.unit;
  if(unit.type==='camera'){
    if(!surveillance.active&&(jumpHeight>.01||verticalVelocity>0||playerPlatform||pointOnRoad(player.position.x,player.position.z))){
      receipt='ACESSE DO PASSEIO';tagUntil=gameTime+2;updateLink();return;
    }
    if(surveyDrone.active)endDroneFlight();
    setPublicCamera(unit);return;
  }else if(unit.type==='harbor-crane'||unit.type==='harbor-radio'){
    receipt=operateHarborDevice(unit);
  }else if(unit.type==='cinema'){
    receipt=streetCinema.toggle((kind,x,z)=>cityAudio.play(kind,x,z));
  }else if(unit.type==='chamada'){
    receipt=cityPolice.interrupt(unit,gameTime)?'CHAMADA INTERROMPIDA':'CHAMADA ENCERRADA';
  }else if(unit.type==='phone'){
    if(gameTime>=unit.ringUntil){
      unit.ringUntil=gameTime+2.8;cityAudio.play('phone',unit.x,unit.z);
      pedestrianLife.reactToSound(unit.x,unit.z,gameTime);
    }
    receipt='TELEFONE TOCANDO';
  }else if(unit.type==='signal'){requestCrossing(unit.junction,gameTime);receipt='TRAVESSIA SOLICITADA';}
  else if(unit.kind==='fire-water'){unit.active=!unit.active;receipt=unit.active?'ÁGUA LIGADA':'ÁGUA DESLIGADA';}
  else{unit.remoteUntil=gameTime+(unit.automatic?8:5);receipt=unit.automatic?'ACESSO LIBERADO':'FREIO ATIVADO';}
  tagUntil=gameTime+2;updateLink();
}
function cancelLink(){scanning=false;linked=null;}
function setPublicCamera(unit){
  for(const key of Object.keys(keys))keys[key]=false;
  walkCamera.reset();
  touchRunKey=null;velocity.set(0,0,0);cancelLink();tagUntil=0;tag.visible=false;
  if(cameraPointer!==null&&renderer.domElement.hasPointerCapture(cameraPointer))renderer.domElement.releasePointerCapture(cameraPointer);
  cameraPointer=null;
  if(unit){
    clearSpeech();surveillance.connect(unit);feedHintUntil=gameTime+3.3;
    camera.fov=settings.values.fov;camera.updateProjectionMatrix();
  }else{
    surveillance.disconnect();feedHintUntil=0;
  }
  const exit=$('#touchE');exit.textContent=unit?'↩':'E';
  exit.setAttribute('aria-label',unit?'Voltar ao personagem':'Interagir');
  world.classList.toggle('camera-feed',!!unit);
  $('#touchJump').classList.toggle('hidden',!!unit);
  visibilityTimer=0;nextLampUpdate=0;
}
function leavePublicCamera(){setPublicCamera(null);}
$('#touchDroneLaunch').addEventListener('click',startDroneFlight);
const touchLink=$('#touchLink');
const touchBrake=$('#touchJump'),touchHorn=$('#touchH');
let touchDriveMode='';
function syncTouchDriving(){
  if(!isTouch)return;
  $('#touchDroneFlight').classList.toggle('hidden',!surveyDrone.active);
  if(surveyDrone.active){
    if(touchDriveMode==='drone')return;
    touchDriveMode='drone';touchLink.classList.remove('hidden');touchLink.textContent='F';touchLink.setAttribute('aria-label','Hackear pelo drone');
    touchBrake.classList.add('hidden');touchHorn.classList.add('hidden');return;
  }
  const mode=surveillance.active?'feed':marine.mode!=='land'?marine.mode:vehicle?.kind==='bus'?'passenger':vehicle?.kind==='bike'?'bike':vehicle?'car':'foot';
  if(mode===touchDriveMode)return;
  touchDriveMode=mode;keys.handbrake=false;
  const drive=mode==='car'||mode==='bike';
  touchLink.classList.toggle('hidden',mode==='passenger'||mode==='bike'||mode==='swim'||mode==='boat');
  touchLink.textContent=mode==='car'?'Mão':'F';
  touchLink.setAttribute('aria-label',mode==='car'?'Segurar freio de mão':'Segurar para mirar, soltar para hackear');
  touchBrake.classList.toggle('hidden',mode==='feed'||mode==='passenger'||mode==='swim');
  touchBrake.textContent=drive||mode==='boat'?'Freio':'↑';
  touchBrake.setAttribute('aria-label',mode==='boat'?'Frear barco':drive?'Frear':'Pular');
  touchHorn.classList.toggle('hidden',!drive);
}
touchLink.addEventListener('pointerdown',e=>{
  touchLink.setPointerCapture(e.pointerId);
  if(touchDriveMode==='car'){keys.handbrake=true;return;}
  beginLink();
});
touchLink.addEventListener('pointerup',()=>{if(keys.handbrake)keys.handbrake=false;else releaseLink();});
touchLink.addEventListener('pointercancel',()=>{keys.handbrake=false;cancelLink();});
touchLink.addEventListener('lostpointercapture',()=>{keys.handbrake=false;});
addEventListener('blur',cancelLink);
addEventListener('keyup',e=>{if(e.code==='KeyF')releaseLink();});
function soundVehicleSignal(){
  if(settings.open||!vehicle||vehicleTransition||vehicle.kind==='bus'||gameTime<(vehicle.nextHorn||0))return;
  vehicle.nextHorn=gameTime+(vehicle.kind==='bike'?.8:.9);
  cityAudio.play(vehicle.kind==='bike'?'bell':'horn',vehicle.g.position.x,vehicle.g.position.z);
  pedestrianLife.reactToHorn(vehicle,gameTime);
}
$('#touchH').addEventListener('click',soundVehicleSignal);
const touchInteract=$('#touchE');
const interactionSign=createInteractionPrompt(THREE,scene,touchInteract,sightLineClear),interactionPoint=new THREE.Vector3();
function publicCounterOpen(){return !combat.isThreatening()&&!combat.isDown()&&!policeTradeBlocked();}
function clerkReady(place){
  if(!publicCounterOpen())return false;
  const clerk=people.find(p=>p.place===place&&p.role===place.kind);
  return !!clerk&&placeContaining(clerk.g.position.x,clerk.g.position.z)===place&&combat.canTalk(clerk);
}
function counterLabel(point){
  if(point.kind==='street-music')return streetPerformance.active?'Aplaudir a roda':'Falar com o músico';
  if(point.kind==='harbor-crane')return harbor.network[0].label==='IÇAR CARGA'?'Içar carga':'Baixar carga';
  if(point.kind==='harbor-radio')return 'Sintonizar a rádio';
  if(point.kind==='cinema')return streetCinema.playing?'Encerrar sessão':'Projetar curta';
  if(point.kind==='harbor-drone'||point.kind==='harbor-weather')return point.label;
  if(point.kind==='range-card')return 'Ler ficha do estande';
  if(point.kind==='district-guide')return 'Ler mapa do bairro';
  if(point.kind==='bank')return 'Consultar saldo';
  if(point.kind==='clinic')return 'Pedir atendimento';
  if(point.mode==='ledger')return 'Consultar extrato';
  return clerkReady(point.place)?economy.quote(point.mode,point.id)?.label:'Chamar atendente';
}
function operateHarborDevice(unit){
  const result=harbor.hack(unit,(kind,x,z)=>cityAudio.play(kind,x,z));
  if(unit.type==='harbor-radio'&&result!=='TROCA EM ANDAMENTO')pedestrianLife.reactToSound(unit.x,unit.z,gameTime);
  return result;
}
function useCityCounter(point){
  const balanceLine=()=>cashText(economy.snapshot().balance);
  const counterSpeech=(name,line)=>{
    point.display.y=groundHeight(point.x,point.z)+(point.kind==='district-guide'?3.60:2.85);
    showSpeech(point,name,line);
  };
  if(point.kind==='street-music'){
    if(streetPerformance.applaud(gameTime,(kind,x,z)=>cityAudio.play(kind,x,z))){
      combat.holster();
      applauseUntil=gameTime+1.5;
      counterSpeech('Ivo','Valeu! Vamos emendar mais uma.');
    }else if(!streetPerformance.active)counterSpeech('Ivo',cityWeather.state.rain>.6?'Vou proteger o violão até a chuva passar.':'Já volto a tocar quando estiver tranquilo.');
    return;
  }
  if(point.kind==='cinema'){streetCinema.toggle((kind,x,z)=>cityAudio.play(kind,x,z));return;}
  if(point.kind==='harbor-radio'||point.kind==='harbor-crane'){
    const entry=harbor.network.find(d=>d.unit.type===point.kind);
    operateHarborDevice(entry.unit);return;
  }
  if(point.kind==='harbor-drone'){startDroneFlight();return;}
  if(point.kind==='harbor-weather'){
    const {current,next}=cityWeather.forecast(),wind=Math.round(cityWeather.state.windSpeed*3.6);
    counterSpeech('Estação do cais',current+'. Vento de '+wind+' km/h. A seguir, '+next.toLocaleLowerCase('pt-BR')+'.');return;
  }
  if(point.kind==='district-guide'){counterSpeech('Guia do bairro','A Oficina Farol fica na avenida das Colinas. Siga a orla para leste até a peixaria, o Cais das Antenas e a rádio Onda Curta.');return;}
  if(point.kind==='range-card'){
    const v=practiceRange.snapshot();
    counterSpeech('Estande Farol',v.rounds?'Esta série: '+v.hits+' acertos nos alvos em '+v.rounds+' disparos no anteparo.':'Entre nas faixas amarelas e mire nos três alvos. Só contam como treino os tiros que terminarem no anteparo.');return;
  }
  if(point.kind==='bank'||point.mode==='ledger'){
    const items=['pulse','sidearm','carbine'].filter(id=>combat.owns(id)).map(id=>combat.weaponInfo(id).name);
    counterSpeech('Baía · Extrato','Seu saldo: '+balanceLine()+'. Equipamento: '+items.join(', ')+'.');return;
  }
  if(!clerkReady(point.place)){
    counterSpeech(point.place.name,combat.isThreatening()?'Guarde a arma para ser atendido.':'Atendimento interrompido. Volte quando a situação estiver tranquila.');return;
  }
  if(point.kind==='clinic'){
    const restored=combat.healAtClinic();
    counterSpeech('Joana',restored>0?'Pronto, cuidei dos ferimentos. Sua vida foi recuperada. O atendimento é gratuito.':'Você está bem. Se se ferir, volte à recepção.');return;
  }
  const receipt=economy.trade(point.mode,point.id);
  if(receipt.ok)cityAudio.play('register',point.x,point.z);
  counterSpeech('Oficina Farol',receipt.line);
}
function payPoliceFine(amount){return economy.fine(amount);}
function interactionLabel({type,data,side,landing}){
  const point=interactionPoint,p=data.g?.position;
  if(type==='marine-ladder'){point.set(data.x,data.top+1.15,data.z);return 'Subir a escada';}
  if(type==='marine-board'||type==='marine-exit'){point.copy(p);point.y+=1.85;return type==='marine-board'?'Pilotar '+data.name:'Desembarcar';}
  if(type==='recall-drone'){point.copy(surveyDrone.position);point.y+=.42;return 'Recolher drone';}
  if(type==='surrender'){point.copy(p);point.y+=2.85;return 'Render-se';}
  if(type==='person'||type==='animal'){
    point.copy(p);point.y+=(type==='person'?2.65:data.type==='cat'?.96:data.type==='pigeon'?.8:1.45)*data.g.scale.y;
    if(type==='animal')return 'Acariciar';
    return activeSpeech?.target===data?'Continuar conversa':'Conversar';
  }
  if(type==='bench'||type==='stand'){
    const front=seatPoint(data,0,data.depth*.42);
    point.set(front.x,data.group.position.y+data.seatTop+1.02,front.z);
    return type==='bench'?'Sentar':'Levantar';
  }
  if(type==='bus'||type==='request-stop'||type==='wait-bus-exit'||type==='leave-bus'){
    const door=busDoorPoint(data);
    point.set(door.x,p.y+2.55,door.z);
    if(type==='bus')return 'Embarcar';
    return type==='request-stop'?'Pedir parada':type==='wait-bus-exit'?'Solicitar saída':'Desembarcar';
  }
  if(type==='car'||type==='stop-exit'||type==='exit-vehicle'){
    const bike=data.kind==='bike',door=incidentPoint(data,(side||landing?.side||data.entrySide||1)*(data.width/2+.18),-.3);
    point.set(door.x,p.y+(bike?1.62:data.design.roof+.25),door.z);
    if(type==='car')return bike?'Pedalar':'Entrar no carro';
    if(type==='stop-exit')return 'Parar e sair';
    return bike?'Descer da bicicleta':'Sair do carro';
  }
  if(type==='fish-market'){
    point.set(data.x,groundHeight(data.x,data.z)+(data.id==='vendor'?2.25:1.85),data.z);
    if(data.id==='vendor')return 'Conversar com Nilo';
    if(data.id==='dispatch')return 'Entregar '+weightText(economy.snapshot().nextWeight);
    return data.id==='fish'?'Escolher peixes':'Trocar pesos';
  }
  if(type==='service'){
    if(data.anchor){
      point.set(data.anchor.x,groundHeight(data.x,data.z)+data.anchor.height,data.anchor.z);
      return counterLabel(data);
    }
    if(data.kind==='police-info'){
      point.set(data.display.x,data.display.y-.48,data.display.z);
      return 'Ler informações';
    }
    point.set(data.x+1.45,groundHeight(data.x,data.z)+2.08,data.z-.75);
    return data.active?'Desligar água':'Ligar água';
  }
  if(type==='leave-camera'){point.copy(data.hint);return 'Voltar ao corpo';}
  return null;
}
function updateInteractionSign(){
  if(activeSpeech)interactionSign.fitSpeech(camera,speechSprite);
  const candidate=scanning||tag.visible?null:currentInteraction();
  const label=candidate?interactionLabel(candidate):null;
  interactionSign.update(label,interactionPoint,camera,speechSprite,player,candidate?.type==='person'?candidate.data.g:null,candidate?.data,
    candidate?.type==='marine-exit'&&Math.abs(candidate.data.speed)>2);
}
touchInteract.addEventListener('pointerup',e=>{if(e.button===0)interact();});
touchInteract.addEventListener('click',e=>{if(e.detail===0&&!e.pointerType)interact();});
$('#touchJump').addEventListener('pointerdown',event=>{event.preventDefault();jump();});
$('#touchJump').addEventListener('pointerup',()=>keys[' ']=false);
$('#touchJump').addEventListener('pointercancel',()=>keys[' ']=false);
addEventListener('keydown',event=>{
  if(settings.open||policeCustody())return;
  if(['Space','ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(event.code))event.preventDefault();
  const key=event.key.toLowerCase();
  // Entering a feed, leaving it or opening settings releases held inputs.
  if(event.repeat&&!keys[key])return;
  keys[key]=true;
  if(event.code==='KeyV'&&!event.repeat){if(surveyDrone.active)endDroneFlight();else startDroneFlight();return;}
  if(surveyDrone.active){
    if(event.code==='KeyE'&&!event.repeat)endDroneFlight();
    else if(event.code==='KeyF'&&!event.repeat)beginLink();
    return;
  }
  if(event.code==='KeyH'&&!event.repeat)soundVehicleSignal();
  if(event.code==='KeyF'&&!event.repeat)beginLink();
  if(event.code==='KeyE'&&!event.repeat)interact();
  if(vehicle&&vehicle.kind!=='bus'&&!event.repeat&&(event.code==='KeyQ'||event.code==='KeyZ')){const side=event.code==='KeyQ'?'left':'right';vehicle.signal=vehicle.signal===side?null:side;}
  if(vehicle?.emergency&&!event.repeat&&event.code==='KeyL')vehicle.emergency.on=!vehicle.emergency.on;
  if(event.code==='Space'&&!event.repeat)jump();
  if(!event.repeat&&/^Digit[1-3]$/.test(event.code)&&combatCanUse()){
    dropCombatAim();const id=weaponIds[Number(event.code.at(-1))-1];if(!combat.select(id))combatView.notice('Disponível na Oficina Farol');
  }
  if(event.code==='KeyX'&&!event.repeat&&combat.equipped){dropCombatAim();combat.holster();}
  if(event.code==='KeyG'&&!event.repeat&&combatCanUse()){aimToggle=!aimToggle;setCombatAim();}
  if(event.code==='KeyR'&&!event.repeat){
    if(combat.equipped){if(combatCanUse())combat.reload();}
    else if(!surveillance.active&&!vehicle&&!sitting&&!playerActor.ragdoll&&!policeCustody())
      createRagdoll(playerActor,new THREE.Vector3(-Math.sin(yaw)*3,3.8,-Math.cos(yaw)*3));
  }
});
function releaseKey(key){
  keys[key]=false;
  const {forward,sideways}=movementInput();
  if(!forward&&!sideways)walkCamera.release();
}
addEventListener('keyup',event=>releaseKey(event.key.toLowerCase()));
addEventListener('blur',()=>{
  dropCombatAim();
  for(const key of Object.keys(keys))keys[key]=false;
  touchRunKey=null;walkCamera.reset();
});
let cameraPointer=null,previousPointerX=0,previousPointerY=0,draggedView=false,touchRunKey=null;
const combatButtons={equip:$('#combatEquip'),aim:$('#combatAim'),fire:$('#combatFire'),reload:$('#combatReload')};
function aimAtPointer(event){const rect=renderer.domElement.getBoundingClientRect();
  aimScreen.set((event.clientX-rect.left)/rect.width*2-1,1-(event.clientY-rect.top)/rect.height*2);nextAimSample=0;}
renderer.domElement.addEventListener('contextmenu',e=>{if(combatCanUse())e.preventDefault();});
renderer.domElement.addEventListener('mousedown',e=>{
  if(!combatCanUse())return;
  if(e.button===2){aimAtPointer(e);rightAim=true;setCombatAim();}
  if(e.button===0&&combat.aiming){aimAtPointer(e);combat.trigger(true);}
});
addEventListener('mouseup',e=>{
  if(e.button===0)combat.trigger(false);
  if(e.button===2&&rightAim){rightAim=false;setCombatAim();}
});
combatButtons.equip.addEventListener('click',()=>{
  if(!combatCanUse())return;const owned=weaponIds.filter(id=>combat.owns(id));
  const id=owned[owned.indexOf(combat.equipped)+1];dropCombatAim();if(id)combat.select(id);else combat.holster();
});
combatButtons.aim.addEventListener('click',()=>{if(!combatCanUse())return;aimScreen.set(0,.12);aimToggle=!aimToggle;setCombatAim();});
combatButtons.reload.addEventListener('click',()=>{if(combatCanUse())combat.reload();});
combatButtons.fire.addEventListener('pointerdown',e=>{if(!combatCanUse())return;e.preventDefault();combatButtons.fire.setPointerCapture(e.pointerId);
  firePointer=e.pointerId;combat.trigger(true);});
for(const type of ['pointerup','pointercancel','lostpointercapture'])combatButtons.fire.addEventListener(type,e=>{
  if(firePointer===e.pointerId){firePointer=null;combat.trigger(false);}
});
let aimTouch=null;
renderer.domElement.addEventListener('pointerdown',event=>{
  if(event.pointerType==='mouse'&&(event.button===2||event.button===0&&combat.aiming))return;
  if(cameraPointer!==null)return;
  if(event.pointerType==='touch'&&combat.aiming)aimTouch={id:event.pointerId,x:event.clientX,y:event.clientY};
  cameraPointer=event.pointerId;previousPointerX=event.clientX;previousPointerY=event.clientY;draggedView=false;
  renderer.domElement.setPointerCapture(cameraPointer);
});
renderer.domElement.addEventListener('pointerup',e=>{
  if(aimTouch?.id===e.pointerId){if(combat.aiming&&combatCanUse()&&Math.hypot(e.clientX-aimTouch.x,e.clientY-aimTouch.y)<8){
    aimAtPointer(e);combat.trigger(true);combat.trigger(false);}
    aimTouch=null;
  }
});
renderer.domElement.addEventListener('pointercancel',()=>{aimTouch=null;});
renderer.domElement.addEventListener('lostpointercapture',event=>{
  if(cameraPointer!==event.pointerId)return;
  if(draggedView)lastManualLook=gameTime;
  cameraPointer=null;draggedView=false;
});
renderer.domElement.addEventListener('pointermove',event=>{
  if(event.pointerType==='mouse'&&combat.aiming&&cameraPointer===null)aimAtPointer(event);
  if(cameraPointer!==event.pointerId)return;
  if(petting)petting.manualView=true;
  const dx=event.clientX-previousPointerX,dy=event.clientY-previousPointerY;
  previousPointerX=event.clientX;previousPointerY=event.clientY;
  const sensitivity=settings.values.sensitivity/100;
  if(surveyDrone.active){surveyDrone.turn(-dx*.006*sensitivity,dy*.004*sensitivity*(settings.values.invertY?-1:1));return;}
  if(surveillance.active){
    surveillance.turn(-dx*.005*sensitivity,dy*.0035*sensitivity*(settings.values.invertY?-1:1));return;
  }
  if(!dx&&!dy)return;
  lastManualLook=gameTime;draggedView=true;
  const delta=-dx*.006*sensitivity;
  targetYaw+=delta;
  if(!vehicle)walkCamera.orbit(delta);
  targetPitch=THREE.MathUtils.clamp(targetPitch+dy*.004*sensitivity*(settings.values.invertY?-1:1),.08,1.12);
});
addEventListener('wheel',event=>{
  if(settings.open||surveillance.active)return;
  if(surveyDrone.active)surveyDrone.zoom(event.deltaY);
  else targetZoom=THREE.MathUtils.clamp(targetZoom+event.deltaY*.012,4.4,25);
},{passive:true});
for(const b of document.querySelectorAll('[data-key]')){
  const key=b.dataset.key;let lastPress=-Infinity;
  b.addEventListener('pointerdown',event=>{
    event.preventDefault();keys[key]=true;b.setPointerCapture(event.pointerId);
    const now=performance.now();if(now-lastPress<320)touchRunKey=key;lastPress=now;
  });
  for(const type of ['pointerup','pointercancel','lostpointercapture'])b.addEventListener(type,()=>{
    releaseKey(key);if(touchRunKey===key)touchRunKey=null;
  });
}
function updateDoors(dt){
  for(const p of places){
    const door=p.door;
    if(door.automatic){
      const nearby=!vehicle&&Math.hypot(player.position.x-door.x,player.position.z-door.z)<3.5;
      const pedestrian=people.some(person=>!person.ragdoll&&!person.inBus&&Math.hypot(person.g.position.x-door.x,person.g.position.z-door.z)<2.5);
      door.open=door.remoteUntil>gameTime||nearby||pedestrian;
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
      if(closing&&((!vehicle&&distanceToSegment(player.position.x,player.position.z,hingeX,door.z,endX,endZ)<.67)||personInDoor))door.angularVelocity=0;
      else door.angle=proposed;
      door.hinge.rotation.y=door.angle;door.open=Math.abs(door.angle)>.2;
    }
  }
}
function pushDoors(x,z,dx,dz,actor=player){
  for(const p of places){
    const d=p.door;if(d.automatic)continue;
    const ax=d.x+d.width/2,bx=ax-Math.cos(d.angle)*d.width,bz=d.z+Math.sin(d.angle)*d.width;
    // Keep applying pressure at the outside of the capsule, including the door leaf's thickness.
    const reach=(actor===player?.55:.42)+.12+.06;
    if(distanceToSegment(x,z,ax,d.z,bx,bz)>reach)continue;
    const nx=Math.sin(d.angle),nz=Math.cos(d.angle),side=(actor.position.x-ax)*nx+(actor.position.z-d.z)*nz;
    const push=dx*nx+dz*nz;
    if(push*side>=0)continue;
    d.angularVelocity=THREE.MathUtils.clamp(d.angularVelocity+push*22,-5,5);d.hold=1.5;
  }
}
function updateVehicleTransition(dt){
  const transition=vehicleTransition,car=transition.car;
  transition.elapsed=Math.min(transition.duration,transition.elapsed+dt);
  const t=transition.elapsed/transition.duration,smooth=v=>v*v*(3-2*v);
  let movement=t;
  if(transition.kind==='enter'&&transition.displaced){
    const driver=transition.displaced.driver,progress=smooth(Math.min(1,t/.45));
    if(driver.exitingVehicle){
      driver.g.position.set(THREE.MathUtils.lerp(transition.displaced.start.x,transition.displaced.landing.x,progress),car.g.position.y,
        THREE.MathUtils.lerp(transition.displaced.start.z,transition.displaced.landing.z,progress));
      driver.g.userData.limbs.forEach((limb,i)=>limb.rotation.x=Math.sin(t*15+(i===0||i===3?0:Math.PI))*.3);
      if(t>=.45){
        driver.exitingVehicle=false;driver.g.position.y=groundHeight(driver.g.position.x,driver.g.position.z);
        const a=car.g.rotation.y,side=transition.displaced.side;driver.push={vx:Math.cos(a)*side*2,vz:-Math.sin(a)*side*2,time:.45};
      }
    }
    if(car.carDoors?.[transition.displaced.side])car.carDoors[transition.displaced.side].rotation.y=-transition.displaced.side*Math.sin(Math.PI*Math.min(1,t/.55))*1.05;
    movement=THREE.MathUtils.clamp((t-.42)/.58,0,1);
  }
  const progress=smooth(movement);
  if(car.carDoors?.[transition.side]&&(!transition.displaced||transition.displaced.side!==transition.side||movement>0))car.carDoors[transition.side].rotation.y=-transition.side*Math.sin(Math.PI*movement)*1.05;
  if(transition.kind==='enter'){
    player.visible=movement<.78;
    if(car.driver)car.driver.visible=movement>=.78;
    player.position.set(THREE.MathUtils.lerp(transition.start.x,transition.end.x,progress),
      THREE.MathUtils.lerp(groundHeight(transition.start.x,transition.start.z),car.g.position.y,progress),
      THREE.MathUtils.lerp(transition.start.z,transition.end.z,progress));
  }else{
    player.visible=movement>.2;
    if(car.driver)car.driver.visible=movement<=.2;
    player.position.set(THREE.MathUtils.lerp(transition.start.x,transition.end.x,progress),
      THREE.MathUtils.lerp(car.g.position.y,groundHeight(transition.end.x,transition.end.z),progress),
      THREE.MathUtils.lerp(transition.start.z,transition.end.z,progress));
  }
  player.rotation.y=car.g.rotation.y;
  transition.poseProgress=movement;
  if(t<1)return;
  if(car.carDoors?.[transition.side])car.carDoors[transition.side].rotation.y=0;
  if(transition.displaced&&car.carDoors?.[transition.displaced.side])car.carDoors[transition.displaced.side].rotation.y=0;
  vehicleTransition=null;
  if(transition.kind==='exit'){
    player.position.set(transition.end.x,groundHeight(transition.end.x,transition.end.z),transition.end.z);player.visible=true;
    car.spots.forEach(l=>l.visible=false);if(car.driver)car.driver.visible=false;if(car.reverse)car.reverse.emissiveIntensity=0;
    car.signal=null;car.lateralSpeed=0;car.yawRate=0;car.impactYaw=0;vehicle=null;targetYaw=yaw;velocity.set(0,0,0);jumpHeight=0;verticalVelocity=0;
  }
}
function tireGripAt(x,z){
  const road=streetSurface.heightAt(x,z);
  const base=Number.isFinite(road)?1:isPaved(x,z)?.91:z< -141?.48:.64;
  const wet=cityWeather.state.wetness;
  if(wet<.002||!weatherCover)return base;
  const floor=Number.isFinite(road)?road:groundHeight(x,z);
  return wetRoadGrip(base,wet,weatherCover.exposed(x,z,floor+.09)?1:0);
}
function wheelGrip(car){
  const a=car.g.rotation.y,c=Math.cos(a),s=Math.sin(a),p=car.g.position;
  let front=0,rear=0,frontCount=0,rearCount=0;
  for(const wheel of car.wheelContacts){
    const grip=tireGripAt(p.x+c*wheel.x+s*wheel.z,p.z-s*wheel.x+c*wheel.z);
    if(wheel.z>0){front+=grip;frontCount++;}else{rear+=grip;rearCount++;}
  }
  const surface=car.tireSurface??={grip:1,frontGrip:1,rearGrip:1};
  surface.frontGrip=frontCount?front/frontCount:1;
  surface.rearGrip=rearCount?rear/rearCount:1;
  surface.grip=(front+rear)/Math.max(1,frontCount+rearCount);
  return surface;
}
function rotateVehicleWheels(car,dt,steering=0,yawRate=0,speed=car.velocity){
  if(car.steeringWheel){
    const angle=-steering*4.15;
    car.steeringWheel.rotation.z=angle;
    if(car.g.visible&&car.driver.visible){
      const grip=THREE.MathUtils.clamp(angle,-1.10,1.10),rim=car.steeringWheel.position,seat=car.driver.position;
      for(const {arm,hand,side} of car.driver.userData.grips){
        hand.position.set(rim.x-seat.x+side*.215*Math.cos(grip),rim.y-seat.y+side*.215*Math.sin(grip),rim.z-seat.z);
        const dx=hand.position.x-side*.27,dy=hand.position.y-1.41,dz=hand.position.z+.03;
        arm.position.set(side*.27+dx*.5,1.41+dy*.5,-.03+dz*.5);
        arm.scale.z=Math.hypot(dx,dy,dz);arm.rotation.order='YXZ';
        arm.rotation.y=Math.atan2(dx,dz);arm.rotation.x=-Math.atan2(dy,Math.hypot(dx,dz));
      }
    }
  }
  for(const w of car.wheelContacts||[]){
    const angle=w.steer?wheelSteeringAngle(steering,car.handling.wheelbase,w.x):0;
    if(w.pivot&&w.steer)w.pivot.rotation.y=angle;
    const along=(speed-yawRate*w.x)*Math.cos(angle)+((car.lateralSpeed||0)+yawRate*w.z)*Math.sin(angle);
    const spin=along*dt/w.radius*(w.z<0?1-(car.handbrakeLevel||0):1);
    if(w.spin)w.spin.rotation.x+=spin;else if(w.parts?.[0])w.parts[0].rotation.x+=spin;
  }
}
function moveDrivenVehicle(car,lateral,turn,dt){
  const g=car.g,p=g.position,reach=Math.hypot(car.width,car.length)/2+Math.abs(car.centerOffset||0);
  const path=Math.hypot(car.velocity,lateral),steps=Math.max(1,Math.ceil((path+Math.abs(turn)*reach)*dt/.19));
  if(path<.00001&&Math.abs(turn)<.00001)return null;
  for(let n=0;n<steps;n++){
    const oldYaw=g.rotation.y,step=integrateCarPose(oldYaw,car.velocity,lateral,turn,dt/steps);
    const x=p.x+step.dx,z=p.z+step.dz;
    g.rotation.y=step.heading;
    breakAhead(car,x,z);
    let hit=vehicleObstacleAt(car,x,z);
    if(!hit&&path<2.8&&pedestrianOverlaps(car,x,z)&&!pedestrianOverlaps(car,p.x,p.z))hit={person:true};
    if(hit){g.rotation.y=oldYaw;return hit;}
    p.x=x;p.z=z;
  }
  return null;
}
function updatePlayer(dt){
  if(updateMarinePlayer(dt)){strideSpeed=marine.mode==='swim'?marine.effort*2.4:0;return;}
  if(surveyDrone.mode!=='stowed'){
    if(surveyDrone.active&&(policeCustody()||playerActor.ragdoll||vehicle||marine.mode!=='land'))endDroneFlight();
    const piloting=surveyDrone.active;
    const {forward,sideways}=movementInput();
    surveyDrone.update(dt,{forward,sideways,lift:(keys[' ']||keys.droneup?1:0)-(keys.shift||keys.dronedown?1:0)},player.position,cityWeather.state);
    if(piloting&&!surveyDrone.active)restoreGroundControls();
    if(surveyDrone.active){strideSpeed=0;return;}
  }
  if(policeCustody()){velocity.set(0,0,0);strideSpeed=0;return;}
  strideSpeed=0;
  if(surveillance.active){
    if(playerActor.ragdoll||vehicle||sitting||vehicleTransition)leavePublicCamera();
    else return;
  }
  if(playerActor.ragdoll)return;
  if(vehicleTransition){updateVehicleTransition(dt);return;}
  if(vault){updateParkour(dt);return;}
  if(sitting){updateSeat(dt);return;}
  const {forward,sideways}=movementInput();
  if(petting&&(forward||sideways))petting=null;
  if(petting){updatePetting(dt);return;}
  if(playerPlatform){
    const {car,x,z,y,angle}=playerPlatform,p=car.g.position,dx=player.position.x-x,dz=player.position.z-z;
    const lx=Math.cos(angle)*dx-Math.sin(angle)*dz,lz=Math.sin(angle)*dx+Math.cos(angle)*dz,a=car.g.rotation.y;
    const nx=p.x+Math.cos(a)*lx+Math.sin(a)*lz,nz=p.z-Math.sin(a)*lx+Math.cos(a)*lz;
    if(!collides(nx,nz,.42)&&!vehicleBlocks(nx,nz,.42,car.g,player.position.y)){
      player.position.set(nx,player.position.y+p.y-y,nz);
    }else playerPlatform=null;
  }
  if(vehicle){
    const c=vehicle;
    if(c.kind==='bus'){player.position.copy(c.g.position);return;}
    let lateral=0;
    if(c.kind==='bike'){
      if(c.exitRequested)c.velocity*=Math.exp(-6*dt);
      else if(forward)c.velocity+=forward*(forward*c.velocity<0?16:8)*dt;
      else c.velocity*=Math.exp(-.7*dt);
      if(keys[' '])c.velocity*=Math.exp(-5.5*dt);
      c.velocity=THREE.MathUtils.clamp(c.velocity,-4,12);
      c.steering=THREE.MathUtils.damp(c.steering||0,-sideways,8,dt);
      c.yawRate=c.steering*1.6*THREE.MathUtils.clamp(Math.abs(c.velocity)/3,0,1)*Math.sign(c.velocity||1);
      c.braking=!!keys[' ']||!!c.exitRequested||forward*c.velocity<0;
      c.lateralSpeed=THREE.MathUtils.damp(c.lateralSpeed||0,0,3,dt);lateral=c.lateralSpeed;
    }else{
      const motion=stepCarDynamics({speed:c.velocity,steering:c.steeringAngle,lateral:c.lateralSpeed,yawRate:c.yawRate,
        gear:c.gear,pedal:c.pedal,driveDirection:c.driveDirection,shiftTime:c.shiftTime,handbrakeLevel:c.handbrakeLevel},
        {...c.handling,...wheelGrip(c),throttle:c.exitRequested?0:forward,steer:sideways,
          topSpeed:48,powerScale:1.3,brake:!!keys[' ']||!!c.exitRequested,handbrake:!!(keys.shift||keys.handbrake)&&!c.exitRequested,pitch:c.groundPose?.slopePitch||0},dt);
      c.velocity=motion.speed;c.steeringAngle=motion.steering;c.lateralSpeed=motion.lateral;c.yawRate=motion.yawRate;
      c.pedal=motion.pedal;c.driveDirection=motion.driveDirection;c.shiftTime=motion.shiftTime;
      c.handbrakeLevel=motion.handbrakeLevel;c.tireSlip=motion.tireSlip;c.cornerAcceleration=motion.cornerAcceleration;
      c.bodyAcceleration=THREE.MathUtils.damp(c.bodyAcceleration||0,motion.acceleration,6,dt);
      c.braking=motion.braking;c.gear=motion.gear;c.engineRpm=motion.rpm;lateral=motion.lateral;
    }
    if(c.exitRequested&&Math.hypot(c.velocity,c.lateralSpeed||0)<.4&&Math.abs(c.impactYaw||0)<.12){exitVehicle(c);return;}
    const impact=moveDrivenVehicle(c,lateral,c.yawRate+(c.impactYaw||0),dt);
    c.impactYaw=(c.impactYaw||0)*Math.exp(-2.1*dt);
    const collisionSpeed=Math.hypot(c.velocity,lateral);
    if(impact?.person){
      c.velocity=c.lateralSpeed=c.yawRate=c.impactYaw=0;c.braking=true;
    }else if(impact){
      if(!(impact.hitVehicle&&collideVehicles(c,impact.hitVehicle))){
        if(collisionSpeed>3)cityAudio.play('impact',c.g.position.x,c.g.position.z);
        if(collisionSpeed>3)dust(c.g.position.x,c.g.position.y+.3,c.g.position.z,5,1.6);
        if(!(impact.hitSolid&&collideSolid(c,impact.hitSolid))){c.velocity=c.lateralSpeed=c.yawRate=c.impactYaw=0;}
      }
    }
    c.brake.emissiveIntensity=c.braking?1.7:.35;
    if(c.reverse)c.reverse.emissiveIntensity=c.velocity<-.3?1.5:0;
    rotateVehicleWheels(c,dt,c.steeringAngle||0,c.yawRate||0);
    player.position.copy(c.g.position);
    if(c.kind!=='bike'&&Math.abs(c.velocity)>3){
      const loose=(c.tireSurface?.rearGrip??1)<.8,sliding=(c.tireSlip||0)>.19;
      if((loose||sliding)&&gameTime>=(c.nextTireDust||0)){
        const a=c.g.rotation.y,side=(c.lastDustSide||1)*-1,z=-c.handling.rearOffset;
        const x=c.g.position.x+Math.cos(a)*side*c.width*.43+Math.sin(a)*z;
        const pz=c.g.position.z-Math.sin(a)*side*c.width*.43+Math.cos(a)*z;
        dust(x,groundHeight(x,pz)+.12,pz,loose?2:1,.45,loose?'#baa67f':'#b8b8ab');
        c.lastDustSide=side;c.nextTireDust=gameTime+(loose?.16:.12);
      }
    }
    return;
  }
  const travel=walkCamera.direction(forward,sideways,yaw);
  const moving=!!(forward||sideways),maxSpeed=combat.aiming?2.8:keys.shift||(touchRunKey&&keys[touchRunKey])?10:6;
  const targetX=travel.x*maxSpeed,targetZ=travel.z*maxSpeed;
  const acceleration=jumpHeight>.01?4:10;
  velocity.x=THREE.MathUtils.damp(velocity.x,targetX,acceleration,dt);
  velocity.z=THREE.MathUtils.damp(velocity.z,targetZ,acceleration,dt);
  pushDoors(player.position.x+velocity.x*dt,player.position.z+velocity.z*dt,velocity.x*dt,velocity.z*dt);
  const oldX=player.position.x,oldZ=player.position.z;
  const collision=moveWithCollision(player,velocity.x*dt,velocity.z*dt,.55);
  if(collision.hitX)velocity.x=0;if(collision.hitZ)velocity.z=0;
  const distanceWalked=Math.hypot(player.position.x-oldX,player.position.z-oldZ);
  const walkSpeed=distanceWalked/Math.max(.0001,dt);strideSpeed=walkSpeed;
  if(combat.aiming&&aimSample){faceToward(player,Math.atan2(aimWorld.x-player.position.x,aimWorld.z-player.position.z),dt,13);}
  else if(moving&&walkSpeed>.1){
    faceToward(player,Math.atan2(player.position.x-oldX,player.position.z-oldZ),dt,14);
  }else if(scanning&&linked){
    devicePosition(linked.unit);
    faceToward(player,Math.atan2(antenna.x-player.position.x,antenna.z-player.position.z),dt,8);
  }
  if(jumpHeight<.02){
    const stride=THREE.MathUtils.lerp(1.83,2.56,THREE.MathUtils.smoothstep(walkSpeed,4.8,8.1));
    walkPhase=(walkPhase+distanceWalked/stride*Math.PI*2)%(Math.PI*2);
  }
  const previousJump=jumpHeight,previousY=player.position.y;
  const nextY=previousY+(verticalVelocity-.5*jumpGravity*dt)*dt;verticalVelocity-=jumpGravity*dt;
  const ground=groundHeight(player.position.x,player.position.z),surface=vehicleFloorAt(player.position.x,player.position.z,previousY);
  const floor=Math.max(ground,wallFloorAt(player.position.x,player.position.z,previousY),surface?.y??-Infinity);
  const drop=previousY-ground;
  const walkingDownhill=previousJump===0&&!playerPlatform&&floor===ground&&drop>0&&drop<=distanceWalked*.23+.001&&
    Math.abs(previousY-groundHeight(oldX,oldZ))<.015;
  if(verticalVelocity<=0&&(nextY<=floor||walkingDownhill)){
    player.position.y=floor;jumpHeight=0;jumpOriginY=null;
    if(previousJump>0&&verticalVelocity<-2){
      playerMotion.land(-verticalVelocity);cityAudio.play('step',player.position.x,player.position.z);
      footDust(3);
    }
    verticalVelocity=0;
    playerPlatform=surface&&surface.y===floor&&floor>ground?{car:surface.car,x:surface.car.g.position.x,y:surface.car.g.position.y,z:surface.car.g.position.z,angle:surface.car.g.rotation.y}:null;
    stepTimer-=dt;if(walkSpeed>6&&stepTimer<0){footDust(1);stepTimer=.28;}
  }else{player.position.y=Math.max(ground,nextY);jumpHeight=Math.max(.02,player.position.y-ground);playerPlatform=null;}
}
const trafficWalkers=[],walkerSamples=new WeakMap(),trackBends=new WeakMap();
function sampleTrafficWalkers(dt){
  trafficWalkers.length=0;
  function sample(actor){
    const pos=actor.g.position;
    let k=walkerSamples.get(actor);
    if(!k){k={x:pos.x,z:pos.z,vx:0,vz:0,at:gameTime};walkerSamples.set(actor,k);}
    const dx=pos.x-k.x,dz=pos.z-k.z;
    if(gameTime-k.at<dt*1.6&&Math.hypot(dx,dz)<dt*12){
      k.vx=THREE.MathUtils.damp(k.vx,dx/dt,16,dt);k.vz=THREE.MathUtils.damp(k.vz,dz/dt,16,dt);
    }else k.vx=k.vz=0;
    k.x=pos.x;k.y=pos.y;k.z=pos.z;k.at=gameTime;k.actor=actor;
    trafficWalkers.push(k);
  }
  for(const p of people)if(!p.indoor&&!p.ragdoll&&!p.inBus)sample(p);
  if(!vehicle&&!playerActor.ragdoll&&!sitting)sample(playerActor);
}
function pedestrianAhead(car){
  const p=car.g.position,s=Math.sin(car.g.rotation.y),c=Math.cos(car.g.rotation.y);
  const length=car.length??4.6,front=length/2+(car.centerOffset||0);
  let first=null;
  for(const person of trafficWalkers){
    const dx=person.x-p.x,dz=person.z-p.z;
    if(Math.abs(dx)>40||Math.abs(dz)>40||Math.abs(person.y-p.y)>1.8||
      person.actor===playerActor&&playerAboveVehicle(car))continue;
    const conflict=pedestrianConflict(dx*s+dz*c-front,dx*c-dz*s,person.vx*s+person.vz*c,
      person.vx*c-person.vz*s,Math.max(0,car.velocity),car.width??2.5,length);
    if(conflict&&(!first||conflict.gap<first.gap))first=conflict;
  }
  return first;
}
function pedestrianOverlaps(car,x,z){
  const a=car.g.rotation.y;
  return trafficWalkers.some(pos=>{
    if(Math.abs(pos.y-car.g.position.y)>1.8||pos.actor===playerActor&&playerAboveVehicle(car))return false;
    const dx=pos.x-x,dz=pos.z-z,lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
    const across=Math.max(0,Math.abs(lx)-(car.width??2.5)/2),along=Math.max(0,Math.abs(lz-(car.centerOffset||0))-(car.length??4.6)/2);
    return across*across+along*along<.35*.35;
  });
}
function redSignalGap(car){
  const tangent=car.track?trackSample(car.track,car.routeProgress):null;
  const heading=car.patrolRoute?car.g.rotation.y:tangent?Math.atan2(tangent.dx,tangent.dz):car.overtake||(!car.curve&&!car.turning&&car.auto)?trafficBasis(car).angle:car.g.rotation.y;
  const p=car.g.position,fx=Math.sin(heading),fz=Math.cos(heading);
  const front=(car.length??4.6)/2+(car.centerOffset||0);
  let gap=Infinity;
  for(const line of stopLines){
    if(line.junction.phase[line.axis]===2||fx*line.fx+fz*line.fz<.88)continue;
    const dx=line.x-p.x,dz=line.z-p.z;
    if(Math.abs(dx*line.fz-dz*line.fx)>line.half+.35)continue;
    const distance=dx*line.fx+dz*line.fz-front-.32;
    if(line.junction.phase[line.axis]===1&&clearsAmber(distance,car.velocity,gameTime,line.junction,line.axis))continue;
    if(distance>=-.08&&distance<Math.max(24,Math.abs(car.velocity)*3.6))gap=Math.min(gap,Math.max(0,distance));
  }
  return Number.isFinite(gap)?gap:null;
}
function leaderLimits(car,cruise,passing){
  const a=car.g.rotation.y,fx=Math.sin(a),fz=Math.cos(a),rx=Math.cos(a),rz=-Math.sin(a),p=car.g.position;
  let desired=cruise,emergency=false;
  for(const other of cars){
    if(other===car)continue;
    const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z,ahead=dx*fx+dz*fz;
    if(ahead<=0||ahead>Math.max(26,Math.abs(car.velocity)*3.5+5))continue;
    const angle=other.g.rotation.y-a,otherWidth=other.width??.7,otherLength=other.length??3;
    const lateral=Math.abs(Math.cos(angle))*otherWidth/2+Math.abs(Math.sin(angle))*otherLength/2;
    if(Math.abs(dx*rx+dz*rz)>(car.width??2.5)/2+lateral+.25)continue;
    const longitudinal=Math.abs(Math.cos(angle))*otherLength/2+Math.abs(Math.sin(angle))*otherWidth/2;
    const gap=ahead+(other.centerOffset||0)*Math.cos(angle)-(car.centerOffset||0)-(car.length??4.6)/2-longitudinal;
    const motion=vehicleWorldVelocity(other),along=motion.x*fx+motion.z*fz;
    const safe=approachSpeed(gap-1.75,along,1.05);
    // Once the adjacent lane has been checked, keep enough rolling speed to
    // steer around this particular obstacle. Other vehicles still constrain us.
    desired=Math.min(desired,other===passing?Math.max(2.25,safe):safe);
    if(other!==passing&&car.velocity>0&&car.velocity**2-Math.max(0,along)**2>14*Math.max(.01,gap-.4))emergency=true;
  }
  return {speed:desired,emergency};
}
function trafficLimits(car,cruise,passing=null){
  const leader=leaderLimits(car,cruise,passing);
  let speed=leader.speed,reason=speed<cruise-.1?'veiculo':null;
  const signal=redSignalGap(car),pedestrian=pedestrianAhead(car);
  let distance=signal??Infinity;
  if(signal!==null){
    const limit=approachSpeed(signal);
    if(limit<speed+.015){speed=Math.min(speed,limit);reason='semaforo';}
  }
  if(pedestrian){
    const limit=approachSpeed(pedestrian.gap,0,.7);
    if(limit<speed+.015){speed=Math.min(speed,limit);reason='pedestre';}
    if(pedestrian.occupied)distance=Math.min(distance,pedestrian.contactGap);
  }
  return {speed,reason,distance,emergency:leader.emergency};
}
function trackAdvice(car,cruise){
  const track=car.track;
  if(!trackBends.has(track)){
    trackBends.set(track,routeBends(track,(x,z)=>junctions.some(j=>(j.x-x)**2+(j.z-z)**2<18**2)));
  }
  return routeAdvice(trackBends.get(track),track.length,car.routeProgress,Math.abs(car.velocity),cruise,car.kind==='bus'?2.0:2.7);
}
function leaveKerbSpace(car,goal,dt){
  // A long bus cuts slightly inside a bend. Give the corner of a vehicle
  // protruding from a parking bay clearance, still within our own lane.
  const p=car.g.position,dx=goal.x-p.x,dz=goal.z-p.z,len=Math.hypot(dx,dz);
  if(len<.5)return;
  const fx=goal.dx??dx/len,fz=goal.dz??dz/len;
  let offset=0;
  for(const parked of cars){
    if(parked===car||parked.auto||parked.kind==='bike'||Math.abs(parked.velocity)>.20)continue;
    const x=parked.g.position.x-goal.x,z=parked.g.position.z-goal.z;
    if(Math.abs(x)>17||Math.abs(z)>17)continue;
    const heading=parked.g.rotation.y,along=x*fx+z*fz,across=x*fz-z*fx;
    const parallel=Math.abs(Math.sin(heading)*fx+Math.cos(heading)*fz);
    if(parallel<.65||Math.abs(across)<1.05)continue;
    const half=(parallel*parked.width+Math.sqrt(Math.max(0,1-parallel*parallel))*parked.length)/2;
    const room=(car.width??2.5)/2+half+.78-Math.abs(across);
    const near=1-THREE.MathUtils.smoothstep(Math.abs(along),5,14);
    const correction=THREE.MathUtils.clamp(room,0,1.5)*near;
    if(correction>Math.abs(offset))offset=-Math.sign(across)*correction;
  }
  car.kerbOffset=THREE.MathUtils.damp(car.kerbOffset||0,offset,4,dt);
  goal.x+=fz*car.kerbOffset;goal.z-=fx*car.kerbOffset;
}
function trafficBasis(car){
  const angle=car.axis==='z'?(car.direction>0?0:Math.PI):(car.direction>0?Math.PI/2:-Math.PI/2);
  return {angle,fx:Math.sin(angle),fz:Math.cos(angle),rx:Math.cos(angle),rz:-Math.sin(angle)};
}
function trafficObstacle(car,basis){
  const p=car.g.position;let best=null;
  for(const other of cars){
    if(other===car)continue;
    const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z,ahead=dx*basis.fx+dz*basis.fz,across=Math.abs(dx*basis.rx+dz*basis.rz);
    const angle=other.g.rotation.y-basis.angle,halfWidth=(other.width??2.5)*.5*Math.abs(Math.cos(angle))+(other.length??4.6)*.5*Math.abs(Math.sin(angle));
    const speed=vehicleWorldVelocity(other),forward=speed.x*basis.fx+speed.z*basis.fz;
    if(ahead>0&&ahead<30&&across<(car.width??2.5)/2+halfWidth+.3&&forward<2&&(!best||ahead<best.ahead))best={object:other,ahead,halfLength:(other.length??4.6)/2};
  }
  const seen=new Set(),cx=p.x+basis.fx*15,cz=p.z+basis.fz*15;
  for(const solid of nearbySolids(cx,cz,22)){
    if(seen.has(solid))continue;seen.add(solid);
    const dx=solid.x-p.x,dz=solid.z-p.z,ahead=dx*basis.fx+dz*basis.fz,across=Math.abs(dx*basis.rx+dz*basis.rz);
    const halfWidth=(solid.w*Math.abs(basis.rx)+solid.d*Math.abs(basis.rz))/2;
    const halfLength=(solid.w*Math.abs(basis.fx)+solid.d*Math.abs(basis.fz))/2;
    if(ahead>0&&ahead<30&&across<(car.width??2.5)/2+halfWidth+.3&&(!best||ahead<best.ahead))best={object:solid,ahead,halfLength};
  }
  return best;
}
function trafficObstacleGap(car,basis){
  const obstacle=car.trafficObstacle;if(!obstacle||obstacle.object.active===false)return null;
  const obj=obstacle.object,pos=obj.g?.position||obj,p=car.g.position;
  const dx=pos.x-p.x,dz=pos.z-p.z,ahead=dx*basis.fx+dz*basis.fz,across=Math.abs(dx*basis.rx+dz*basis.rz);
  let halfWidth;
  if(obj.g){const angle=obj.g.rotation.y-basis.angle;halfWidth=(obj.width??2.5)*.5*Math.abs(Math.cos(angle))+(obj.length??4.6)*.5*Math.abs(Math.sin(angle));}
  else halfWidth=(obj.w*Math.abs(basis.rx)+obj.d*Math.abs(basis.rz))/2;
  if(ahead<=0||across>(car.width??2.5)/2+halfWidth+.3)return null;
  return ahead-(car.length??4.6)/2-obstacle.halfLength;
}
function distanceToTrafficJunction(car){
  const current=car.axis==='z'?car.g.position.z:car.g.position.x;
  const crossings=car.axis==='z'?[COAST_Z,...roads,curvedRoadZ(car.g.position.x)]:[-130,...roads,130];
  let next=Infinity,previous=Infinity;
  for(const crossing of crossings){const d=(crossing-current)*car.direction;if(d>=0)next=Math.min(next,d);else previous=Math.min(previous,-d);}
  return {next,previous};
}
function passLaneAvailable(car,basis,distance){
  const p=car.g.position,laneOffset=-6.6-(car.overtake?.shift||0),vehicles=cars.filter(other=>other!==car).map(other=>{
    const speed=vehicleWorldVelocity(other),angle=other.g.rotation.y-basis.angle,c=Math.abs(Math.cos(angle)),s=Math.abs(Math.sin(angle));
    const width=other.width??2.5,length=other.length??4.6;
    return {x:other.g.position.x,z:other.g.position.z,vx:speed.x,vz:speed.z,width:width*c+length*s,length:length*c+width*s};
  });
  const pedestrians=people.filter(person=>!person.ragdoll&&!person.inBus).map(person=>person.g.position);
  if(!vehicle&&!playerActor.ragdoll&&!sitting)pedestrians.push(player.position);
  if(!passingLaneClear({x:p.x,z:p.z,...basis,laneOffset},vehicles,pedestrians,distance))return false;
  const old=car.g.rotation.y;car.g.rotation.y=basis.angle;
  let clear=true;
  for(let ahead=0;ahead<=distance;ahead+=3){
    if(vehicleSolidOverlap(car,p.x+basis.fx*ahead+basis.rx*laneOffset,p.z+basis.fz*ahead+basis.rz*laneOffset)){clear=false;break;}
  }
  car.g.rotation.y=old;return clear;
}
function homeLaneAvailable(car,basis){
  const p=car.g.position,shift=-(car.overtake?.shift||0);
  for(const other of cars){
    if(other===car)continue;
    const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z,ahead=dx*basis.fx+dz*basis.fz,across=dx*basis.rx+dz*basis.rz;
    if(ahead>-8&&ahead<12&&Math.abs(across-shift)<(other.width??2.5)/2+2)return false;
  }
  const pedestrians=people.filter(person=>!person.ragdoll&&!person.inBus).map(person=>person.g.position);
  if(!vehicle&&!playerActor.ragdoll&&!sitting)pedestrians.push(player.position);
  return !pedestrians.some(person=>Math.abs((person.x-p.x)*basis.fx+(person.z-p.z)*basis.fz)<8&&
    Math.abs((person.x-p.x)*basis.rx+(person.z-p.z)*basis.rz-shift)<2.5);
}
function updateOvertake(car,basis,dt){
  car.overtakeCooldown=Math.max(0,(car.overtakeCooldown||0)-dt);
  if(car.turning||car.curve||car.track)return;
  let maneuver=car.overtake;
  if(maneuver) maneuver.shift=(car.g.position.x-maneuver.origin.x)*basis.rx+(car.g.position.z-maneuver.origin.z)*basis.rz;
  if(!maneuver&&!car.overtakeCooldown){
    car.avoidScanTimer=(car.avoidScanTimer||0)-dt;if(car.avoidScanTimer>0)return;
    car.avoidScanTimer=.35;
    const road=car.laneRoad??(car.axis==='x'?Math.round(car.g.position.z/72)*72:0);
    if(car.axis==='x'&&road===72&&car.g.position.x>-95&&car.g.position.x<15)return;
    const obstacle=trafficObstacle(car,basis),junction=distanceToTrafficJunction(car);car.trafficObstacle=obstacle;
    const queued=obstacle?.object.auto&&['semaforo','pedestre','veiculo','pare','preferencia','ponto'].includes(obstacle.object.stoppedReason);
    if(obstacle&&!queued&&junction.previous>14&&junction.next>obstacle.ahead+obstacle.halfLength+17&&
      passLaneAvailable(car,basis,obstacle.ahead+18)){
      maneuver=car.overtake={object:obstacle.object,halfLength:obstacle.halfLength,shift:0,indicateUntil:gameTime+.65,origin:{x:car.g.position.x,z:car.g.position.z},stage:obstacle.ahead<14?'reverse':'out'};
      car.avoids=(car.avoids||0)+1;
    }
  }
  if(!maneuver)return;
  const target=maneuver.object.g?.position||maneuver.object,p=car.g.position;
  const ahead=(target.x-p.x)*basis.fx+(target.z-p.z)*basis.fz;
  if(maneuver.stage==='reverse'&&ahead>=13.85)maneuver.stage='wait';
  if(maneuver.stage==='wait'){
    if(Math.abs(car.velocity)<.08&&passLaneAvailable(car,basis,Math.max(12,ahead+18)))maneuver.stage='out';else return;
  }
  if(maneuver.stage==='out'&&maneuver.shift<=-6.1)maneuver.stage='pass';
  if(maneuver.stage==='pass'&&ahead<-(car.length??4.6)/2-maneuver.halfLength-3&&homeLaneAvailable(car,basis))maneuver.stage='return';
  if(maneuver.stage==='return'&&maneuver.shift>=-.18){car.overtake=null;car.overtakeCooldown=3;return;}
  if(gameTime>=(maneuver.nextCheck||0)){
    maneuver.yield=maneuver.stage==='return'?!homeLaneAvailable(car,basis):!passLaneAvailable(car,basis,Math.max(12,ahead+18));
    maneuver.nextCheck=gameTime+.35;
  }
  if(maneuver.yield&&maneuver.stage==='out'&&maneuver.shift> -2&&homeLaneAvailable(car,basis)){maneuver.stage='return';maneuver.yield=false;}
}
function reverseForOvertake(car,basis,dt){
  const blocked=distanceToTrafficJunction(car).previous<15;
  car.stoppedReason=blocked?'veiculo':'manobra';car.signal='left';
  const obstacle=car.overtake.object.g?.position||car.overtake.object,p=car.g.position;
  const ahead=(obstacle.x-p.x)*basis.fx+(obstacle.z-p.z)*basis.fz;
  driveNpcVehicle(car,null,blocked?0:-Math.min(1.5,approachSpeed(14-ahead)),dt);
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
  const track=car.track,p=car.g.position;
  car.routeProgress=trackNearestNear(track,p.x,p.z,car.routeProgress);
  const path=trackSample(track,car.routeProgress),lanePath=districtTrackPosition(track,car.routeProgress,car.lane);
  if(lanePath.blend>.3)car.lane=((p.x-path.x)*path.dz-(p.z-path.z)*path.dx)/lanePath.blend;
  const heading=car.g.rotation.y,fx=Math.sin(heading),fz=Math.cos(heading),rx=Math.cos(heading),rz=-Math.sin(heading);
  let obstacle=Infinity,blocker=null;
  for(const other of cars){
    if(other===car)continue;const dx=other.g.position.x-p.x,dz=other.g.position.z-p.z,ahead=dx*fx+dz*fz,across=Math.abs(dx*rx+dz*rz);
    if(ahead>0&&ahead<obstacle&&across<2.7&&Math.abs(other.velocity)<car.velocity+2){obstacle=ahead;blocker=other;}
  }
  let nearest=null,remaining=Infinity;
  for(const stop of track.stops){
    if(car.stopDone.has(stop.id))continue;
    const d=(stop.progress-car.routeProgress+track.length)%track.length;if(d<remaining){remaining=d;nearest=stop;}
  }
  const branch=districtTrackPosition(track,car.routeProgress,car.lane),canChange=branch.blend>.98&&remaining>16;
  car.avoidTime=Math.max(0,car.avoidTime-dt);
  let nextLane=car.laneTarget;
  if(obstacle<22&&canChange&&Math.abs(car.laneTarget-car.lane)<.35&&laneClear(car,-car.laneTarget))nextLane=-car.laneTarget;
  else if(car.avoidTime<=0&&car.laneTarget!==car.homeLane&&canChange&&laneClear(car,car.homeLane))nextLane=car.homeLane;
  if(nextLane===car.laneTarget)car.pendingLane=null;
  else if(car.pendingLane!==nextLane){car.pendingLane=nextLane;car.laneSignalAt=gameTime;}
  else if(gameTime-car.laneSignalAt>.65){
    car.laneSignal=nextLane>car.laneTarget?'right':'left';
    car.laneTarget=nextLane;car.pendingLane=null;
    if(nextLane!==car.homeLane){car.avoidTime=5;car.avoids++;car.laneObstacle=blocker;}
    else car.laneObstacle=null;
  }
  const road=trackAdvice(car,car.track.id==='oeste'?10:11),limits=trafficLimits(car,road.speed,car.laneSignal?car.laneObstacle:null);
  let desired=limits.speed;car.stoppedReason=limits.reason;
  if(Math.abs(car.laneTarget-car.lane)>.4)desired=Math.min(4.5,desired);
  if(nearest&&remaining<12){
    desired=Math.min(desired,approachSpeed(remaining));
    if(remaining<.20)desired=0;
    if(remaining<.20&&Math.abs(car.velocity)<.032){
      car.velocity=0;car.stopWait+=dt;car.stoppedReason='pare';desired=0;
      const busy=cars.some(other=>other!==car&&Math.hypot(other.g.position.x-nearest.junctionX,other.g.position.z-nearest.junctionZ)<5&&Math.abs(other.velocity)>.3)||
        people.some(person=>!person.inBus&&!person.ragdoll&&Math.hypot(person.g.position.x-nearest.junctionX,person.g.position.z-nearest.junctionZ)<4);
      if(car.stopWait>=1.25&&!busy){car.stopDone.add(nearest.id);car.stopCount++;car.stopWait=0;nearest=null;remaining=Infinity;desired=limits.speed;}
      else if(car.stopWait>=1.25&&busy)car.stoppedReason='preferencia';
    }
  }
  const lookahead=car.laneSignal?THREE.MathUtils.clamp(2.8+Math.abs(car.velocity)*.4,3.5,6):
    THREE.MathUtils.clamp(4+Math.abs(car.velocity)*.8,6,10);
  if(car.laneSignal&&(Math.abs(car.laneTarget-car.lane)<.28||branch.blend<.8))car.laneSignal=null;
  car.signal=car.pendingLane!=null?(car.pendingLane>car.laneTarget?'right':'left'):(car.laneSignal||road.signal);
  const goal=districtTrackPosition(track,car.routeProgress+lookahead,car.laneTarget),previous=car.routeProgress;
  driveNpcVehicle(car,goal,desired,dt,{maxDistance:Math.min(limits.distance,remaining),emergency:limits.emergency});
  const next=trackNearestNear(track,p.x,p.z,previous);
  if(next<previous&&previous-next>track.length/2)car.stopDone.clear();
  car.routeProgress=next;
}
function updateCityBus(bus,dt){
  const track=bus.track;
  if(bus.atStop){
    bus.velocity=0;bus.lateralSpeed=0;bus.yawRate=0;bus.trafficAcceleration=0;bus.signal=null;bus.stoppedReason='ponto';bus.dwell-=dt;
    bus.doors[0].position.z=1.55;bus.doors[1].position.z=3.35;
    if(vehicle===bus&&bus.exitRequested&&!exitBusAtStop(bus))bus.dwell=Math.max(bus.dwell,.5);
    if(bus.passengers.some(p=>p.boardedStop!==bus.atStop.id))bus.dwell=Math.max(bus.dwell,.5);
    if(bus.dwell<=0&&!people.some(p=>p.busState==='boarding')){bus.lastStop=bus.atStop.id;bus.atStop=null;bus.doors[0].position.z=2;bus.doors[1].position.z=2.9;}
    return;
  }
  bus.routeProgress=trackNearestNear(track,bus.g.position.x,bus.g.position.z,bus.routeProgress);
  let nextStop=null,remaining=Infinity;
  for(const stop of track.stops){
    if(stop.id===bus.lastStop)continue;
    const d=(stop.progress-bus.routeProgress+track.length)%track.length;if(d<remaining){remaining=d;nextStop=stop;}
  }
  const road=trackAdvice(bus,8),limits=trafficLimits(bus,road.speed);
  const desired=remaining<.20?0:Math.min(limits.speed,approachSpeed(remaining,0,.8,2.15));
  bus.stoppedReason=limits.reason;bus.signal=road.signal;
  const lookahead=THREE.MathUtils.clamp(4+Math.abs(bus.velocity)*.8,5.7,10);
  const goal=trackSample(track,bus.routeProgress+lookahead),previous=bus.routeProgress;
  leaveKerbSpace(bus,goal,dt);
  driveNpcVehicle(bus,goal,desired,dt,{maxDistance:Math.min(remaining,limits.distance),emergency:limits.emergency});
  bus.routeProgress=trackNearestNear(track,bus.g.position.x,bus.g.position.z,previous);
  if(nextStop&&remaining<.20&&Math.abs(bus.velocity)<.032&&bus.stoppedReason===null){
    bus.velocity=0;bus.lateralSpeed=0;bus.atStop=nextStop;bus.dwell=7;bus.stopCount++;bus.brake.emissiveIntensity=1.5;
  }
}
function animateVehicleLights(car){
  if(!car.indicators)return;
  const lit=Math.floor(gameTime*2.5)%2===0;
  for(const side of ['left','right']){
    const active=(car.hazards||car.remoteUntil>gameTime||car.signal===side)&&lit;car.indicators[side].emissiveIntensity=active?1.7:0;
    car.indicators[side].color.set(active?'#ffc66e':'#926c42');
  }
  if(car.emergency){const pulse=Math.floor(gameTime*4)%2;car.emergency.red.emissiveIntensity=car.emergency.on&&pulse===0?2:0;car.emergency.blue.emissiveIntensity=car.emergency.on&&pulse===1?2:0;}
}
function driveNpcVehicle(car,target,speed,dt,{maxDistance=Infinity,turn=null,emergency=false,maxSteering=car.handling?.maxSteering??.62}={}){
  speed*=1-cityWeather.state.wetness*.15-Math.max(0,cityWeather.state.fog-.35)*.09;
  const remote=car.remoteUntil>gameTime;
  if(remote){speed=0;car.stoppedReason='freio remoto';}
  const p=car.g.position,oldHeading=car.g.rotation.y;
  const motion=stepTrafficDrive({x:p.x,z:p.z,heading:oldHeading,speed:car.velocity,steering:car.steeringAngle,lateral:car.lateralSpeed,yawRate:car.yawRate,
    commandAcceleration:car.trafficAcceleration??0,targetSpeed:car.trafficTargetSpeed,targetRate:car.trafficTargetRate,
    gear:car.gear,pedal:car.pedal,driveDirection:car.driveDirection,shiftTime:car.shiftTime,handbrakeLevel:car.handbrakeLevel},
    {target,speed,turn,...wheelGrip(car),...car.handling,maxSteering,pitch:car.groundPose?.slopePitch||0,
      urgent:remote||emergency||maxDistance<car.velocity*car.velocity/14},dt);
  let {dx,dz}=motion;const distance=Math.hypot(dx,dz);
  const limited=distance>maxDistance;
  if(limited&&distance>0){const scale=Math.max(0,maxDistance)/distance;dx*=scale;dz*=scale;}
  car.g.rotation.y=motion.heading;
  let person=pedestrianOverlaps(car,p.x,p.z),blocked=person||vehicleFootprintBlocked(car,p.x,p.z);
  if(blocked)car.g.rotation.y=oldHeading;
  else{
    const steps=Math.max(1,Math.ceil(Math.hypot(dx,dz)/.2));
    for(let i=0;i<steps;i++){
      const x=p.x+dx/steps,z=p.z+dz/steps;
      person=pedestrianOverlaps(car,x,z);
      if(person||vehicleFootprintBlocked(car,x,z)){blocked=true;break;}
      p.x=x;p.z=z;
    }
  }
  car.velocity=blocked||limited?0:motion.speed;car.lateralSpeed=blocked||limited?0:motion.lateral;
  car.trafficAcceleration=blocked||limited?0:motion.commandAcceleration;
  car.trafficTargetSpeed=motion.targetSpeed;car.trafficTargetRate=motion.targetRate;
  car.pedal=motion.pedal;car.handbrakeLevel=motion.handbrakeLevel;
  car.tireSlip=blocked||limited?0:motion.tireSlip;car.cornerAcceleration=blocked||limited?0:motion.cornerAcceleration;
  car.driveDirection=motion.driveDirection;car.shiftTime=motion.shiftTime;
  car.steeringAngle=motion.steering;car.yawRate=blocked||limited?0:motion.yawRate;car.gear=motion.gear;car.engineRpm=motion.rpm;
  car.bodyAcceleration=THREE.MathUtils.damp(car.bodyAcceleration||0,motion.acceleration,6,dt);
  car.braking=blocked||limited||motion.braking||speed===0;
  if(blocked)car.stoppedReason=person?'pedestre':'veiculo';
  car.brake.emissiveIntensity=car.braking?1.5:.35;
  if(car.reverse)car.reverse.emissiveIntensity=car.velocity<-.3?1.5:0;
  rotateVehicleWheels(car,dt,car.steeringAngle,car.yawRate);
  return !blocked;
}
function recoverVehicleRoute(car,dt){
  if(car.track)car.routeProgress=trackNearestNear(car.track,car.g.position.x,car.g.position.z,car.routeProgress);
  let target;
  if(car.track)target=car.kind==='bus'?trackSample(car.track,car.routeProgress):districtTrackPosition(car.track,car.routeProgress,car.lane);
  else if(car.curve)target={x:car.g.position.x,z:curvedRoadZ(car.g.position.x)+(car.direction>0?-3.2:3.2)};
  else if(car.axis==='z')target={x:car.rejoinRoad+car.direction*3.3,z:car.g.position.z};
  else target={x:car.g.position.x,z:horizontalRoadZ(car.g.position.x,car.rejoinRoad)-car.direction*3.3};
  const dx=target.x-car.g.position.x,dz=target.z-car.g.position.z,distance=Math.hypot(dx,dz);
  const routeHeading=car.track?Math.atan2(target.dx,target.dz):car.curve?Math.atan2(car.direction,car.direction*(12/39)*Math.cos(car.g.position.x/39)):trafficBasis(car).angle;
  const headingError=Math.abs(Math.atan2(Math.sin(routeHeading-car.g.rotation.y),Math.cos(routeHeading-car.g.rotation.y)));
  if(distance<.35&&headingError<.25){car.recoveringRoute=false;return false;}
  const lookahead=THREE.MathUtils.clamp(4+Math.abs(car.velocity)*.8,6,9);
  let goal;
  if(car.track)goal=car.kind==='bus'?trackSample(car.track,car.routeProgress+lookahead):districtTrackPosition(car.track,car.routeProgress+lookahead,car.laneTarget);
  else{
    if(!car.curve&&car.laneRoad===undefined)car.laneRoad=car.rejoinRoad;
    goal=simpleTrafficGoal(car,trafficBasis(car),lookahead);
  }
  const limits=trafficLimits(car,2.8);car.stoppedReason=limits.reason||'retornando';
  driveNpcVehicle(car,goal,limits.speed,dt,{maxDistance:limits.distance,emergency:limits.emergency});
  return true;
}
function simpleTrafficGoal(car,basis,lookahead){
  const p=car.g.position;
  if(car.overtake){
    const target=car.overtake.stage==='return'||gameTime<car.overtake.indicateUntil?0:-6.6,shift=target-car.overtake.shift;
    return {x:p.x+basis.fx*lookahead+basis.rx*shift,z:p.z+basis.fz*lookahead+basis.rz*shift};
  }
  if(car.curve){
    const slope=(12/39)*Math.cos(p.x/39),x=p.x+car.direction*lookahead/Math.hypot(1,slope);
    return {x,z:curvedRoadZ(x)+(car.direction>0?-3.2:3.2)};
  }
  if(car.axis==='z'){
    const road=car.laneRoad??Math.round(p.x/72)*72;
    return {x:road+car.direction*3.3,z:p.z+car.direction*lookahead};
  }
  const road=car.laneRoad??Math.round(p.z/72)*72,x=p.x+car.direction*lookahead;
  return {x,z:horizontalRoadZ(x,road)-car.direction*3.3};
}
function updateTraffic(dt){
  sampleTrafficWalkers(dt);
  for(const c of cars){
    if(!c.auto)continue;
    if(c.impact)continue;
    if(vault?.car===c){c.velocity=0;c.stoppedReason='pedestre';c.brake.emissiveIntensity=1.5;continue;}
    if(vehicleIncidents.update(c,dt,gameTime))continue;
    if(c.recoveringRoute&&recoverVehicleRoute(c,dt))continue;
    if(c.debugHoldUntil&&gameTime<c.debugHoldUntil){c.velocity=0;c.brake.emissiveIntensity=.35;continue;}
    if(c.patrolRoute){updateMobilePolice(c,dt);continue;}
    if(c.kind==='bus'){updateCityBus(c,dt);continue;}
    if(c.track){updateDistrictCar(c,dt);continue;}
    const basis=trafficBasis(c);updateOvertake(c,basis,dt);
    if(c.overtake?.stage==='reverse'){reverseForOvertake(c,basis,dt);continue;}
    if(c.overtake?.stage==='wait'){
      c.stoppedReason='preferencia';c.signal='left';driveNpcVehicle(c,null,0,dt);continue;
    }
    const roadSpeed=c.curve?10:Math.abs(c.laneRoad??(c.axis==='z'?Math.round(c.g.position.x/72)*72:Math.round(c.g.position.z/72)*72))>=100?9:Math.abs(c.laneRoad??(c.axis==='z'?Math.round(c.g.position.x/72)*72:Math.round(c.g.position.z/72)*72))<1?10:11;
    const current=c.curve?c.g.position.x:c.g.position[c.axis],limits=trafficLimits(c,roadSpeed);
    let desired=limits.speed;c.stoppedReason=limits.reason;
    const obstacleGap=trafficObstacleGap(c,basis);
    if(obstacleGap!==null&&!c.trafficObstacle.object.g){
      const safe=approachSpeed(obstacleGap-1.25);
      if(safe<desired){desired=safe;c.stoppedReason='veiculo';}
    }
    if(c.overtake){desired=Math.min(desired,c.overtake.yield?0:4.5);if(c.overtake.yield)c.stoppedReason='preferencia';}
    let endDistance=Infinity;
    if(!c.turning){
      const road=c.curve?0:(c.laneRoad??(c.axis==='z'?Math.round(c.g.position.x/72)*72:Math.round(c.g.position.z/72)*72));
      const end=c.curve?(c.direction>0?196:-196):c.axis==='z'?(c.direction>0?curvedRoadZ(road)-12:-116):(c.direction>0?116:-116);
      endDistance=(end-current)*c.direction;
      c.signal=endDistance<24?'left':null;
      if(endDistance<12)desired=Math.min(desired,Math.sqrt(Math.max(0,endDistance+.5)*2));
      if(endDistance<=.8&&!c.overtake)c.turning={progress:0,road,end,direction:c.direction};
    }
    if(c.turning){
      const previous=c.g.rotation.y;c.signal='left';desired=Math.min(desired,2);
      driveNpcVehicle(c,null,desired,dt,{turn:1,maxSteering:.82,maxDistance:limits.distance,emergency:limits.emergency});
      c.turning.progress+=Math.max(0,previous-c.g.rotation.y);
      if(c.turning.progress>=Math.PI-.12){c.direction*=-1;c.turning=null;}
    }else{
      if(c.overtake)c.signal=c.overtake.stage==='out'?'left':c.overtake.stage==='return'?'right':null;
      const goal=simpleTrafficGoal(c,basis,THREE.MathUtils.clamp(4+Math.abs(c.velocity)*.8,6,10));
      if(!c.overtake)leaveKerbSpace(c,goal,dt);
      driveNpcVehicle(c,goal,desired,dt,{maxDistance:limits.distance,emergency:limits.emergency});
    }
  }
}

function updateBusPassengers(dt){
  const bus=cityBus;
  if(bus.impact||bus.recoveringRoute)return;
  for(const person of people){
    if(!person.busState||person.ragdoll||person.push)continue;
    if(person.inBus){
      if(!bus.atStop||bus.atStop.id===person.boardedStop)continue;
      const landing=busLanding(bus,person);if(!landing)continue;
      person.inBus=false;pedestrianLife.place(person,landing.x,landing.z);person.g.visible=true;
      person.busAvatar?.removeFromParent();person.busAvatar=null;bus.passengers=bus.passengers.filter(p=>p!==person);
      person.busStop=busStops.find(stop=>stop.id===bus.atStop.id);person.busState='settling';
      const platformX=person.busStop.x+(person.busStop.x<0?-1.2:1.2);
      person.waitSlot=[-2.2,-4.4,0,4.4,6.3].find(offset=>people.every(other=>other===person||other.inBus||
        (Math.hypot(other.g.position.x-platformX,other.g.position.z-person.busStop.z-offset)>1.2&&
          !(other.busStop?.id===person.busStop.id&&other.busState&&Math.abs((other.waitSlot??2.2)-offset)<1.1))))??-6.3;
      continue;
    }
    if(activeSpeech?.target===person)continue;
    if(person.busState==='waiting'){
      const door=busDoorPoint(bus),alighting=(vehicle===bus&&bus.exitRequested)||bus.passengers.some(p=>p.boardedStop!==bus.atStop?.id)||
        people.some(p=>p.busState==='settling'&&p.busStop?.id===bus.atStop?.id&&Math.hypot(p.g.position.x-door.x,p.g.position.z-door.z)<4);
      if(bus.atStop?.id===person.busStop.id&&bus.dwell>3&&bus.passengers.length<3&&!alighting&&!people.some(p=>p!==person&&p.busState==='boarding')){
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
      }else{
        if(person.lifeBusTrip){person.busState=null;person.lifeBusTrip=false;person.routineFinished=true;}
        else person.busState='waiting';
        person.g.userData.limbs?.forEach(limb=>limb.rotation.x=0);
      }
      continue;
    }
    const moved=pedestrianLife.walk(person,dx,dz,Math.min(distance,1.7*dt),{allowRoad:true,ignore:bus.g});
    person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=moved?Math.sin(gameTime*7+(i===0||i===3?0:Math.PI))*.35:0);
  }
}

function impactActors(){
  for(const c of cars){
    const speed=Math.hypot(c.velocity,c.lateralSpeed||0);
    if(speed<2.8)continue;
    const motion=vehicleWorldVelocity(c),impulse=new THREE.Vector3(motion.x*.8,4,motion.z*.8);
    const hits=(position)=>{
      const dx=position.x-c.g.position.x,dz=position.z-c.g.position.z,a=c.g.rotation.y;
      const lx=Math.cos(a)*dx-Math.sin(a)*dz,lz=Math.sin(a)*dx+Math.cos(a)*dz;
      return Math.abs(lx)<(c.width??(c.kind==='bike'?.7:2.5))/2+.3&&Math.abs(lz-(c.centerOffset||0))<(c.length??(c.kind==='bike'?3:4.6))/2+.3;
    };
    if(!vehicle&&!playerActor.ragdoll&&!playerAboveVehicle(c)&&hits(player.position)){
      if(c.auto){c.velocity=0;c.stoppedReason='pedestre';}else{combat.damage(playerActor,speed*4.5,{source:c});createRagdoll(playerActor,impulse);velocity.set(0,0,0);c.velocity*=.5;}
    }
    for(const person of people){
      if(person.indoor||person.sitting||person.ragdoll||person.inBus)continue;
      if(hits(person.g.position)){if(c.auto){c.velocity=0;c.stoppedReason='pedestre';}else{
        if(c===vehicle)reportCityCrime('atropelamento',person.g.position,{victim:person});
        combat.damage(person,speed*4.5,{source:c});createRagdoll(person,impulse);c.velocity*=.6;
      }}
    }
  }
}

// Rede de passeios: esquinas conectadas por calçadas e faixas, mais os
// passeios das ruas sinuosas. Cada aresta de travessia tem sua própria regra.
function buildSidewalkGraph(){
  const nodes=[],lookup=new Map(),columns=[-130,-72,0,72,130],rows=[-131,-72,0,72],offset=8.2;
  function node(x,z){
    const key=`${x.toFixed(2)},${z.toFixed(2)}`;if(lookup.has(key))return lookup.get(key);
    const rawZ=z;if(Math.abs(Math.abs(z-72)-offset)<.01)z+=plazaRoadBend(x);
    const n={id:nodes.length,x,z,rawZ,edges:[]};nodes.push(n);lookup.set(key,n);return n;
  }
  function edge(a,b,crossing=null){
    if(a===b||a.edges.some(e=>e.to===b))return;
    const cost=Math.hypot(a.x-b.x,a.z-b.z)+(crossing?4:0);
    a.edges.push({to:b,cost,crossing});b.edges.push({to:a,cost,crossing});
  }
  function chain(a,b){
    const count=Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/12);let previous=a;
    for(let i=1;i<count;i++){const n=node(a.x+(b.x-a.x)*i/count,a.rawZ+(b.rawZ-a.rawZ)*i/count);edge(previous,n);previous=n;}edge(previous,b);
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
  // Both sides of the northern curved avenue join every access from the center.
  const curveXs=[...new Set([...Array.from({length:39},(_,i)=>-190+i*10),...columns.flatMap(x=>[x-offset,x+offset])])].sort((a,b)=>a-b);
  const curveSides=[-1,1].map(side=>curveXs.map(x=>node(x,curvedRoadZ(x)+side*8.6)));
  for(const side of curveSides)for(let i=1;i<side.length;i++)edge(side[i-1],side[i],segmentOnRoad(side[i-1],side[i])?'ns':null);
  for(const x of columns)for(const side of [-1,1]){
    const px=x+side*offset,index=curveXs.indexOf(px),south=curveSides[0][index],north=curveSides[1][index];
    chain(node(px,Math.min(124,curvedRoadZ(px)-11)),south);edge(south,north,'ew');
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
function pointOnRoad(x,z){
  return Number.isFinite(streetSurface.heightAt(x,z));
}
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
let npcLife,citySocial;
const policeRadio=createPoliceRadio(THREE,scene),policeEye=new THREE.Vector3(),policeRay=new THREE.Vector3();
function policeSightFrom(point,ignore=null){
  policeRay.set(point.x-policeEye.x,(point.y??groundHeight(point.x,point.z))+1.35-policeEye.y,point.z-policeEye.z);
  const range=policeRay.length();if(range<.15)return true;
  policeRay.divideScalar(range);
  return cameraClearance(policeEye,policeRay,range,viewObstacles(policeEye,range,ignore),0)>=range-.16;
}
function policeCanSee(actor,point){
  const p=actor.g.position;policeEye.set(p.x,p.y+1.65*actor.g.scale.y,p.z);
  return policeSightFrom(point);
}
function cruiserSees(point,alert){
  if(!streetPatrol.auto||streetPatrol.incident||streetPatrol.impact||streetPatrol===vehicle)return false;
  const p=streetPatrol.g.position,h=streetPatrol.g.rotation.y,fx=Math.sin(h),fz=Math.cos(h);
  const dx=point.x-p.x,dz=point.z-p.z,range=Math.hypot(dx,dz);
  if(range>(alert?43:25)||range>7&&dx*fx+dz*fz<range*(alert?-.45:.05))return false;
  policeEye.set(p.x+fx*.63,p.y+1.58,p.z+fz*.63);
  return policeSightFrom(point,streetPatrol);
}
function updateMobilePolice(car,dt){
  let order=cityPolice.order;
  if(order&&gameTime>=(car.observationAt||0)){
    car.observationAt=gameTime+.18;const suspect=vehicle?.g.position??player.position;
    if(cruiserSees(suspect,true)&&cityPolice.cruiserSight(suspect,gameTime))car.sawSuspectAt=gameTime;
    order=cityPolice.order;
  }
  const visible=!!order&&gameTime-car.sawSuspectAt<.75;
  const gap=order?Math.hypot(car.g.position.x-order.point.x,car.g.position.z-order.point.z):Infinity;
  car.patrolMode=!order?'ronda':visible?'acompanhamento':order.stage==='busca'?'busca':'deslocamento';
  car.emergency.on=!!order;car.pursuitSiren=visible&&!!vehicle&&vehicle!==car;
  const route=car.patrolRoute.update(car.g.position,Math.abs(car.velocity),order?.point,gameTime);
  car.axis=route.axis;car.direction=route.direction;car.laneRoad=route.road;car.signal=route.signal;
  let pace=route.limit;
  if(order&&!visible&&gap<19)pace=Math.min(pace,4.2);
  if(visible&&(!vehicle||Math.abs(vehicle.velocity)<1.3)&&gap<16)pace=Math.min(pace,approachSpeed(Math.max(0,gap-8)));
  const limits=trafficLimits(car,pace);car.stoppedReason=limits.reason;
  leaveKerbSpace(car,route.aim,dt);
  driveNpcVehicle(car,route.aim,limits.speed,dt,{maxDistance:limits.distance,emergency:limits.emergency});
}
function patrolSnapshot(){
  const c=streetPatrol;
  return {x:c.g.position.x,z:c.g.position.z,heading:c.g.rotation.y,speed:c.velocity,mode:c.auto?c.patrolMode:'fora de serviço',
    lights:!!c.emergency.on,siren:!!(c.auto&&c.pursuitSiren),seenAgo:Number.isFinite(c.sawSuspectAt)?gameTime-c.sawSuspectAt:null,
    onRoad:pointOnRoad(c.g.position.x,c.g.position.z),inSolid:vehicleSolidOverlap(c,c.g.position.x,c.g.position.z)};
}
function policeMove(person,goal,dt,speed,mode){
  const p=person.g.position,dx=goal.x-p.x,dz=goal.z-p.z,dist=Math.hypot(dx,dz);
  if(dist<.25)return true;
  if(mode==='afastar'){pedestrianLife.walk(person,dx,dz,Math.min(dist,speed*dt));return false;}
  if(dist<13&&clearWalkSegment(p,goal,true)&&(!segmentOnRoad(p,goal)||!trafficBlocksCrossing(p,goal,cars))){
    if(pedestrianLife.walk(person,dx,dz,Math.min(dist,speed*dt),{allowRoad:true})){
      person.indoor=!!placeContaining(p.x,p.z);person.nav=null;return false;
    }
  }
  const target={x:goal.x,z:goal.z,id:`policia-${mode}-${Math.round(goal.x/4)}-${Math.round(goal.z/4)}`,slot:0};
  return moveResident(person,target,dt,speed);
}
const cityPolice=createCityPolice({people,position:()=>vehicle?.g.position??player.position,canSee:policeCanSee,move:policeMove,
  notice:(who,title,line)=>policeRadio.notify(who,title,line,gameTime),phone:policeRadio.phone,
  charge:payPoliceFine,
  grounded:()=>marine.mode==='land'&&!vehicle&&!vehicleTransition&&!vault&&!sitting&&!surveillance.active&&!playerActor.ragdoll&&jumpHeight<.04,
  down:()=>combat.isDown(),threatening:()=>combat.isThreatening(),
  custodyChanged(on){if(on){for(const key of Object.keys(keys))keys[key]=false;velocity.set(0,0,0);petting=null;cancelLink();dropCombatAim();combat.holster();walkCamera.reset();}}
});
function policeCustody(){return cityPolice.custody;}
function policeTradeBlocked(){return cityPolice.tradeBlocked;}
function reportCityCrime(type,point,options={}){
  if(options.offender&&options.offender!==playerActor)return false;
  const suspect=vehicle?.g.position??player.position;
  const cruiserWitness=cruiserSees(point,options.loud)&&cruiserSees(suspect,options.loud);
  if(cruiserWitness)streetPatrol.sawSuspectAt=gameTime;
  return cityPolice.report(type,point,{...options,cruiserWitness},gameTime);
}
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
      if(e.walkable===undefined)e.walkable=clearWalkSegment(current,e.to,!!e.crossing);
      if(!e.walkable)continue;
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
  if(Math.hypot(start.x-end.x,start.z-end.z)<70&&clearWalkSegment(start,end))steps.push({...end});
  else{
    const a=nearestWalkNode(start),b=nearestWalkNode(end);if(!a||!b)return null;
    const path=findWalkPath(a,b);if(!path)return null;
    steps.push({x:a.x,z:a.z},...path,{...end});
  }
  if(goalPlace)steps.push({...inside(goalPlace),door:true},{x:target.x,z:target.z,door:true});
  // Keep to the right when sharing a sidewalk in opposite directions.
  for(let i=steps.length-2;i>0;i--){
    const next=steps[i],previous=steps[i-1];if(next.door||next.crossing||previous.door||previous.crossing)continue;
    const dx=next.x-previous.x,dz=next.z-previous.z,length=Math.hypot(dx,dz);if(length<4)continue;
    const offset={x:next.x+dz/length*.52,z:next.z-dx/length*.52};
    if(!collides(offset.x,offset.z,.35)&&!pointOnRoad(offset.x,offset.z)){next.x=offset.x;next.z=offset.z;}
  }
  return steps;
}
function moveResident(person,target,dt,pace=null){
  const key=`${target.id}:${target.slot}`;
  if(!person.nav||person.nav.key!==key){person.nav={key,path:planPersonRoute(person,target),index:0,stuck:0,crossing:false};}
  const nav=person.nav;if(!nav.path)return 'unreachable';
  const arrived=()=>{
    person.g.userData.limbs?.forEach(l=>l.rotation.x=0);
    if(target.activity==='wait'&&target.stopId){person.busState='waiting';person.busStop=busStops.find(stop=>stop.id===target.stopId);person.lifeBusTrip=true;}
    return true;
  };
  const step=nav.path[nav.index];if(!step)return arrived();
  const pos=person.g.position,destination=nav.landing||step;
  let dx=destination.x-pos.x,dz=destination.z-pos.z,dist=Math.hypot(dx,dz);
  const next=nav.path[nav.index+1];
  const nearCorner=next&&!step.door&&!pointOnRoad(pos.x,pos.z)&&dist<(next.crossing?2.05:1.15)&&
    (next.crossing||(!next.door&&clearWalkSegment(pos,next)));
  if(dist<.24||nearCorner){nav.index++;nav.crossing=false;nav.landing=null;nav.bestDistance=Infinity;nav.stuck=0;if(nav.index>=nav.path.length)return arrived();return false;}
  if(step.crossing&&!nav.crossing){
    const mx=(pos.x+step.x)/2,mz=(pos.z+step.z)/2;
    const signal=(step.crossing==='ns'||step.crossing==='ew')&&junctions.find(j=>Math.abs(j.x-mx)<11&&Math.abs(j.z-mz)<11);
    const alongStripe=step.crossing==='ns'?'z':'x';
    const band=signal?signal[alongStripe]+Math.sign(step[alongStripe]-signal[alongStripe])*8.8:null;
    if(signal&&Math.abs(pos[alongStripe]-band)>1.05){
      // Join the marked strip from the sidewalk; a long queue can start
      // beside the painting, especially if people are passing in both directions.
      const shift=band-pos[alongStripe];
      pedestrianLife.walk(person,alongStripe==='x'?shift:0,alongStripe==='z'?shift:0,Math.min(Math.abs(shift),dt*1.25));
      return false;
    }
    if(!pedestrianLife.curbReady(person,dx,dz,dt,gameTime))return false;
    if(signal&&signal.phase[step.crossing]>0)return false;
    if(trafficBlocksCrossing(pos,step,cars))return false;
    // Reserve an exit beyond the opposite curb before entering the crossing.
    const fx=dx/dist,fz=dz/dist;
    for(const side of [.63,0,-.63,1.08,-1.08])for(const depth of [.45,1.4,2.4,3.3]){
      if(nav.landing)break;
      const x=(signal&&alongStripe==='x'?band:step.x)+fz*side+fx*depth;
      const z=(signal&&alongStripe==='z'?band:step.z)-fx*side+fz*depth;
      if(pointOnRoad(x,z)||!isPaved(x,z)||collides(x,z,.42)||vehicleBlocks(x,z,.42,null)||pedestrianLife.occupied(person,x,z))continue;
      if(people.some(q=>q!==person&&q.nav?.landing&&Math.hypot(x-q.nav.landing.x,z-q.nav.landing.z)<1.15))continue;
      nav.landing={x,z};
    }
    if(!nav.landing)return false;
    if(trafficBlocksCrossing(pos,nav.landing,cars)){nav.landing=null;return false;}
    nav.entry={x:pos.x,z:pos.z};
    nav.band=signal?{axis:alongStripe,center:band}:null;
    dx=nav.landing.x-pos.x;dz=nav.landing.z-pos.z;dist=Math.hypot(dx,dz);
    nav.crossing=true;
  }
  const speed=pace??(person.child?1.2:1.15+(person.phase%3)*.1);
  const amount=Math.min(dist,speed*pedestrianLife.pace(person,gameTime)*(person.ambientSpeed||1)*dt);
  if(step.door)pushDoors(pos.x+dx/dist*amount,pos.z+dz/dist*amount,dx/dist*amount,dz/dist*amount,person.g);
  const moved=pedestrianLife.walk(person,dx,dz,amount,{allowRoad:!!step.crossing||!!step.door});
  if(moved)person.indoor=!!placeContaining(pos.x,pos.z);
  if(dist<(nav.bestDistance??Infinity)-.16){nav.bestDistance=dist;nav.stuck=0;}else nav.stuck+=dt;
  if(moved){
    person.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=Math.sin(gameTime*6+(i===0||i===3?0:Math.PI))*.38);
  }else{
    person.g.userData.limbs?.forEach(limb=>limb.rotation.x=0);
  }
  if(nav.stuck>7&&!nav.crossing){person.nav=null;return 'unreachable';}
  if(nav.stuck>3&&nav.crossing&&nav.landing){
    for(const side of [-1.1,1.1]){
      const x=step.x+dz/dist*side,z=step.z-dx/dist*side;
      if(!pointOnRoad(x,z)&&!collides(x,z,.42)&&!vehicleBlocks(x,z,.42,null)&&!pedestrianLife.occupied(person,x,z)){
        nav.landing={x,z};nav.bestDistance=Infinity;nav.stuck=0;break;
      }
    }
  }
  return false;
}

function updatePeople(dt){
  for(const p of people){
    if(cityPolice.controls(p))continue;
    if(p.ragdoll||p.push||p.exitingVehicle)continue;
    if(activeSpeech?.target===p&&Math.hypot(player.position.x-p.g.position.x,player.position.z-p.g.position.z)<3.5){
      if(!p.sitting){
        const facing=Math.atan2(player.position.x-p.g.position.x,player.position.z-p.g.position.z);
        const delta=Math.atan2(Math.sin(facing-p.g.rotation.y),Math.cos(facing-p.g.rotation.y));
        p.g.rotation.y+=delta*(1-Math.exp(-dt*7));
        p.g.userData.limbs?.forEach((limb,i)=>limb.rotation.x=i===2?-.4+Math.sin(gameTime*3)*.1:0);
      }
      continue;
    }
    if(p.busState||pedestrianLife.isPaused(p)||citySocial?.isPaused(p))continue;
    if(npcLife?.managed.has(p))continue;
    if(p.stationary){
      if(p.sitting&&p.place?.kind==='restaurant')p.g.userData.arms[1].rotation.x=-.5-Math.max(0,Math.sin(gameTime*.8+p.phase))*1.05;
      if(p.chatting&&p.g.userData.limbs)p.g.userData.limbs[2].rotation.x=-.35+Math.sin(gameTime*2+p.phase)*.2;
      continue;
    }
    if(p.indoor)continue;
    p.wait=Math.max(0,p.wait-dt);
    const pos=p.g.position,end=p.origin+p.range*p.dir;
    p.pathLane??=p.axis==='x'?pos.z:pos.x;
    const advance=p.dir*Math.min(2.7,Math.abs(end-pos[p.axis]));
    const nx=p.axis==='x'?pos.x+advance:p.pathLane;
    const nz=p.curved?curvedRoadZ(nx)+p.side*8.6+(p.curveOffset||0):p.axis==='z'?pos.z+advance:p.pathLane;
    const moved=!p.wait&&pedestrianLife.walk(p,nx-pos.x,nz-pos.z,Math.min(p.speed*(p.ambientSpeed||1)*dt,Math.hypot(nx-pos.x,nz-pos.z)));
    p.blockedTime=moved||p.wait?0:(p.blockedTime||0)+dt;
    if(!p.wait&&((end-pos[p.axis])*p.dir<.16||p.blockedTime>.9)){p.dir*=-1;p.wait=.45;p.blockedTime=0;}
    const pace=moved?Math.sin(gameTime*6+p.phase)*.4:0;
    p.g.userData.limbs.forEach((limb,i)=>limb.rotation.x=(i===0||i===3?pace:-pace));
  }
}
function updateMarinePlayer(dt){
  const {forward,sideways}=movementInput(),travel=marine.mode==='swim'?walkCamera.direction(forward,sideways,yaw):null;
  return marine.update(dt,gameTime,{forward,sideways,brake:!!keys[' '],moveX:travel?.x,moveZ:travel?.z,
    boost:!!(keys.shift||touchRunKey&&keys[touchRunKey]),suspended:!!(playerActor.ragdoll||vehicle||vehicleTransition||vault||petting||sitting||surveillance.active||surveyDrone.active||policeCustody())});
}
function updateBoats(dt){
  worldEffects.update(gameTime,vehicle?vehicle.g.position:player.position,nightAmount,Math.hypot(velocity.x,velocity.z),!vehicle&&!playerActor.ragdoll&&!sitting);
  marineArt.update(gameTime,dt,marine,cityWeather.state,surveyDrone.active?surveyDrone.position:player.position);
  cityAudio.watercraft(marine.activeBoat);
}
function updateIndoorVisibility(){
  const location=surveillance.active?.pivot??(vehicle?vehicle.g.position:player.position);
  currentPlace=places.find(p=>Math.abs(location.x-p.x)<p.w/2-.2&&Math.abs(location.z-p.z)<p.d/2-.2)||null;
}
function viewObstacles(origin,radius,ignore=null,physical=false){
  const nearby=cameraObstacles.filter(o=>o!==ignore?.cameraObstacle&&o!==ignore?.mast&&o!==surveillance.active?.mast&&
    (physical||!o.owner||o.owner.visible)&&Math.abs(o.x-origin.x)<radius+o.w/2+1&&Math.abs(o.z-origin.z)<radius+o.d/2+1);
  for(const car of cars){
    const p=car.g.position;
    if(car===ignore||(!physical&&(car===vehicle||car===vault?.car||car===playerPlatform?.car))||Math.hypot(p.x-origin.x,p.z-origin.z)>radius+6)continue;
    const cos=Math.cos(car.g.rotation.y),sin=Math.sin(car.g.rotation.y);
    for(const s of vehicleSurfaces(car)){
      nearby.push({x:p.x+cos*(s.x||0)+sin*s.z,z:p.z-sin*(s.x||0)+cos*s.z,w:s.w,d:s.d,bottom:p.y+.2,top:p.y+s.top,cos,sin});
    }
  }
  if(origin.z<shoreZ+radius){
    for(const b of boats){const p=b.g.position;
      if(b===marine.activeBoat&&!physical||Math.hypot(p.x-origin.x,p.z-origin.z)>radius+b.d)continue;
      nearby.push({x:p.x,z:p.z,w:b.w,d:b.d,bottom:p.y-.38,top:p.y+.62,cos:Math.cos(b.heading),sin:Math.sin(b.heading)});
      if(b.model==='fishing')nearby.push({x:p.x+Math.sin(b.heading)*1.18,z:p.z+Math.cos(b.heading)*1.18,w:1.7,d:1.48,
        bottom:p.y+.60,top:p.y+1.96,cos:Math.cos(b.heading),sin:Math.sin(b.heading)});
    }
    for(const d of marine.docks)nearby.push({x:d.x,z:d.z,w:d.w,d:d.d,cos:d.c,sin:d.s,top:d.top,bottom:d.top-.33});
  }
  for(const place of places){
    const door=place.door,hinge=door.hinge.position;
    if(door===ignore||(!physical&&!door.hinge.visible)||Math.abs(hinge.x-origin.x)>radius+door.width||Math.abs(hinge.z-origin.z)>radius+door.width)continue;
    const cos=Math.cos(door.angle),sin=Math.sin(door.angle);
    nearby.push({x:hinge.x-cos*door.width/2,z:hinge.z+sin*door.width/2,w:door.width,d:.16,bottom:hinge.y,top:hinge.y+3.3,cos,sin,motionKey:door});
  }
  return nearby;
}
function doorwayWeight(position){
  for(const place of places){
    const dx=Math.abs(position.x-place.door.x),dz=Math.abs(position.z-(place.z+place.d/2));
    if(dx>2.8||dz>4.4)continue;
    const alongside=1-THREE.MathUtils.smoothstep(dx,place.door.width*.38,2.8);
    return alongside*(1-THREE.MathUtils.smoothstep(dz,1.1,4.4));
  }
  return 0;
}
function updateCamera(dt){
  if(surveyDrone.active){surveyDrone.view(camera,settings.values.fov);return;}
  if(surveillance.active){
    const {sideways,forward}=movementInput();
    surveillance.view(camera,dt,-sideways,-forward);return;
  }
  const damping=THREE.MathUtils.damp,mix=THREE.MathUtils.lerp;
  aimFraming=damping(aimFraming,combat.aiming?1:0,8.2,dt);
  if(combat.aiming&&!isTouch&&cameraPointer===null){
    const edge=THREE.MathUtils.smoothstep(Math.abs(aimScreen.x),.81,.995);
    if(edge)targetYaw-=Math.sign(aimScreen.x)*edge*1.83*dt;
  }
  const ride=marine.activeBoat||vehicle,driving=ride&&!vehicleTransition,speed=driving?Math.abs(ride.velocity??ride.speed):0;
  const manual=cameraPointer!==null||gameTime-lastManualLook<.06;
  let roadView=null;
  if(driving){
    walkCamera.reset();
    roadView=driveCamera.update(dt,{heading:ride.g.rotation.y,speed:ride.velocity??ride.speed,
      lateralSpeed:ride.lateralSpeed||0,kind:ride.kind,viewYaw:targetYaw,manual});
    targetYaw=roadView.yaw;
  }else{
    driveCamera.reset();
    const walking=!vehicle&&!sitting&&!petting&&!vault&&!playerActor.ragdoll;
    if(!walking&&!vault)walkCamera.reset();
    const follow=walking&&!scanning&&!combat.aiming&&!manual&&gameTime-lastManualLook>.9;
    targetYaw=walkCamera.follow(dt,targetYaw,player.rotation.y,strideSpeed,follow);
    if(vehicleTransition?.kind==='enter'&&!manual&&gameTime-lastManualLook>.9){
      const delta=vehicle.g.rotation.y+Math.PI-targetYaw;
      const turn=Math.atan2(Math.sin(delta),Math.cos(delta))*(1-Math.exp(-dt*4.5));
      targetYaw+=THREE.MathUtils.clamp(turn,-2.4*dt,2.4*dt);
    }
  }
  const yawError=Math.atan2(Math.sin(targetYaw-yaw),Math.cos(targetYaw-yaw));
  yaw+=yawError*(1-Math.exp(-dt*(manual?18:16)));
  pitch=damping(pitch,targetPitch,12,dt);zoom=damping(zoom,targetZoom,8,dt);

  const subject=driving?ride.g.position:player.position;
  cameraTarget.copy(subject);
  if(driving){
    cameraTarget.x+=Math.sin(ride.g.rotation.y)*(ride.centerOffset||0);
    cameraTarget.z+=Math.cos(ride.g.rotation.y)*(ride.centerOffset||0);
  }else if(playerActor.ragdoll){
    const ragdoll=ragdolls.find(r=>r.actor===playerActor);
    if(ragdoll)cameraTarget.copy(ragdoll.joints[0].v);
  }else if(vault){
    // Follow the landing height. Most of a vault's temporary lift belongs to the actor.
    const phase=THREE.MathUtils.smoothstep(vault.elapsed/vault.duration,0,1);
    const support=mix(vault.start.y,vault.endY,phase);
    cameraTarget.y=mix(support,subject.y,vault.climb?.44:.23);
  }else if(jumpOriginY!==null&&jumpHeight>0){
    // Track the launch surface. A small hop crosses the frame instead of lifting the horizon.
    cameraTarget.y-=Math.max(0,cameraTarget.y-jumpOriginY)*.92;
  }
  cameraTarget.y+=playerActor.ragdoll ? .65 : vehicle?.kind==='bus' ? 2.0 : sitting?1.25:1.44;
  if(!playerActor.ragdoll&&marine.mode==='swim'&&!marine.climb)cameraTarget.y=marine.surface(subject.x,subject.z)+.59;
  interiorView=damping(interiorView,currentPlace?1:0,5,dt);
  let viewYaw=yaw;
  const doorway=vehicle?0:doorwayWeight(subject);
  if(aimFraming>.004&&!vehicle&&!playerActor.ragdoll){
    // Slide the sight line to the right of the head before bringing the lens in.
    // A wall on this side limits the shoulder independently of the boom behind it.
    cameraTarget.y+=.34*aimFraming;
    aimShoulder.set(Math.cos(viewYaw),0,-Math.sin(viewYaw));
    const want=1.24*aimFraming*(1-doorway*.68);
    const side=cameraClearance(cameraTarget,aimShoulder,want,viewObstacles(cameraTarget,want+1),.19);
    cameraTarget.addScaledVector(aimShoulder,side);
  }
  const framing=petting&&!petting.manualView?THREE.MathUtils.smoothstep(petting.approachTime+petting.elapsed,0,.7)*
    (1-THREE.MathUtils.smoothstep(petting.elapsed,2.02,2.45)):0;
  if(framing){
    const animal=petting.animal.g.position,delta=petting.viewYaw-yaw;
    viewYaw+=Math.atan2(Math.sin(delta),Math.cos(delta))*framing;
    cameraTarget.x+=(animal.x-subject.x)*.4*framing;cameraTarget.z+=(animal.z-subject.z)*.4*framing;
    cameraTarget.y-=.53*framing;
  }
  if(cameraReady){
    // At the threshold the sightline starts on the same side of the wall as the body.
    const followRate=driving?22:16+doorway*32;
    cameraPivot.x=damping(cameraPivot.x,cameraTarget.x,followRate,dt);
    cameraPivot.z=damping(cameraPivot.z,cameraTarget.z,followRate,dt);
    cameraPivot.y=damping(cameraPivot.y,cameraTarget.y,8,dt);
  }else cameraPivot.copy(cameraTarget);

  const roadSpeed=THREE.MathUtils.smoothstep(speed,3,25);
  let driveFraming=driving?1:0;
  if(vehicleTransition){
    const stage=THREE.MathUtils.smoothstep(vehicleTransition.elapsed/vehicleTransition.duration,0,1);
    driveFraming=vehicleTransition.kind==='enter'?stage:1-stage;
  }
  vehicleView=damping(vehicleView,driveFraming,5,dt);
  const exteriorZoom=zoom+vehicleView*(2.3+roadSpeed*1.7);
  const roomZoom=mix(exteriorZoom,Math.min(zoom,5.25),interiorView);
  const freeRadius=mix(mix(roomZoom,Math.min(roomZoom,3.55),doorway),Math.min(zoom,6.25),framing);
  const radius=mix(freeRadius,Math.min(freeRadius,3.95),aimFraming);
  const elevation=mix(pitch,Math.min(pitch,.23),doorway);
  const nearby=viewObstacles(cameraPivot,radius);
  if(framing)for(const person of people){
    const p=person.g.position;
    if(person.g.visible&&!person.ragdoll&&!person.inBus&&p.distanceToSquared(cameraPivot)<(radius+1)**2){
      nearby.push({x:p.x,z:p.z,w:.82,d:.82,bottom:p.y,top:p.y+2.5*person.g.scale.y});
    }
  }
  const arm=placeCamera(cameraPivot,viewYaw,elevation,radius,driving?Math.max(3.5,(ride.length||ride.d||4.6)*.65):3.3,nearby,dt,camera.position);
  cameraTarget.copy(cameraPivot);
  const room=THREE.MathUtils.smoothstep(arm,.6,4)*(1-framing);
  if(driving){
    cameraTarget.x+=roadView.lookX*room;cameraTarget.z+=roadView.lookZ*room;
  }else if(!playerActor.ragdoll&&!vehicle){
    const shoulder=.50*room*(1-interiorView*.62)*(1-doorway)*(1-aimFraming*.67);
    const lead=(.09-.055*interiorView)*room;
    cameraTarget.x+=Math.cos(viewYaw)*shoulder+velocity.x*lead;
    cameraTarget.z+=-Math.sin(viewYaw)*shoulder+velocity.z*lead;
  }
  const tight=!vehicle&&!petting&&!playerActor.ragdoll&&!vault?1-THREE.MathUtils.smoothstep(arm,2.55,3.65):0;
  const fov=frameCloseActor(camera.position,cameraTarget,subject,sitting?sitting.bench.seatTop+1.58:2.45,tight,
    mix(settings.values.fov,Math.max(46,settings.values.fov-6),aimFraming));
  const nextFov=damping(camera.fov,fov,fov>camera.fov?20:4.5,dt);
  if(Math.abs(fov-camera.fov)>.025){camera.fov=nextFov;camera.updateProjectionMatrix();}
  if(cameraReady)smoothLook.lerp(cameraTarget,1-Math.exp(-dt*(driving?13:16+30*tight)));
  else smoothLook.copy(cameraTarget);
  cameraReady=true;camera.lookAt(smoothLook);
}
// Modelos distantes continuam simulados, mas não precisam ser desenhados.
let visibilityTimer=0;
function updateActorVisibility(dt){
  visibilityTimer-=dt;if(visibilityTimer>0)return;visibilityTimer=.4;
  const pos=surveyDrone.active?surveyDrone.position:surveillance.active?.pivot??(vehicle?vehicle.g.position:player.position);
  for(const person of people){if(!person.ragdoll)person.g.visible=!person.inBus&&Math.hypot(person.g.position.x-pos.x,person.g.position.z-pos.z)<100;}
  for(const car of cars)car.g.visible=car===vehicle||Math.hypot(car.g.position.x-pos.x,car.g.position.z-pos.z)<115;
  for(const animal of animals)animal.g.visible=Math.hypot(animal.g.position.x-pos.x,animal.g.position.z-pos.z)<75;
}
function updateVehicleGrounding(dt){
  for(const car of cars){
    if(!car.wheelContacts?.length||(!car.g.visible&&car!==vehicle&&car.groundPose))continue;
    const input={x:car.g.position.x,z:car.g.position.z,yaw:car.g.rotation.y,
      pitchBias:(car===vehicle||car.auto)&&car.kind!=='bike'?THREE.MathUtils.clamp(-(car.bodyAcceleration||0)*.0048,-.055,.065):0,
      rollBias:(car===vehicle||car.auto)&&car.kind!=='bike'?THREE.MathUtils.clamp((car.cornerAcceleration??((car.yawRate||0)*car.velocity))*
        (car.design?.name==='picape'?.009:.0068),-.092,.092):0};
    const unchanged=car.groundInput&&Object.keys(input).every(key=>Math.abs(input[key]-car.groundInput[key])<.000001);
    // A settled parked car can reuse its support until it moves or leans again.
    if(!unchanged||car.groundingStable<.35){
      const previous=car.groundPose,pose=vehicleGroundPose({...input,wheels:car.wheelContacts,heightAt:renderedGround.heightAt,previous:previous||null,dt});
      const settled=previous&&['height','pitch','roll'].every(key=>Math.abs(pose[key]-previous[key])<.00001)&&pose.offsets.every((v,i)=>Math.abs(v-previous.offsets[i])<.00001);
      car.groundingStable=unchanged&&settled?(car.groundingStable||0)+dt:0;car.groundInput=input;
      car.groundPose=pose;car.g.position.y=pose.height;car.g.rotation.x=pose.pitch;car.g.rotation.z=pose.roll;
      car.wheelContacts.forEach((contact,i)=>contact.parts.forEach(part=>part.position.y=contact.y+pose.offsets[i]));
    }
    if(car.kind==='bike'){
      car.pedalPhase=(car.pedalPhase||0)+Math.sign(car.velocity)*Math.min(7,Math.abs(car.velocity)*.8)*dt;
      car.rider.userData.pedal(car.pedalPhase);
      car.pedals.forEach((pedal,i)=>{const angle=car.pedalPhase+(i===0?Math.PI:0);pedal.position.y=.85+Math.cos(angle)*.27-.12;pedal.position.z=Math.sin(angle)*.27;});
    }
  }
  if(vehicle&&!vehicleTransition)player.position.copy(vehicle.g.position);
}
let groundingDebugTimer=0,groundingDebug=[];
function updateGroundingDiagnostics(dt){
  if(!debugMode)return;
  groundingDebugTimer-=dt;if(groundingDebugTimer>0)return;groundingDebugTimer=.4;
  const point=new THREE.Vector3();
  groundingDebug=cars.filter(c=>c.g.visible&&c.wheelContacts?.length).map(c=>{
    c.g.updateMatrixWorld(true);let clearance=Infinity;
    for(const tire of c.wheels){
      const vertices=tire.geometry.attributes.position;
      for(let i=0;i<vertices.count;i++){
        point.fromBufferAttribute(vertices,i).applyMatrix4(tire.matrixWorld);
        clearance=Math.min(clearance,point.y-renderedGround.heightAt(point.x,point.z));
      }
    }
    return {id:c.debugId||null,kind:c.kind||c.role||'car',controlled:c===vehicle,x:c.g.position.x,y:c.g.position.y,z:c.g.position.z,
      pitch:c.g.rotation.x,roll:c.g.rotation.z,clearance,suspension:c.groundPose?.offsets,sleeping:c.groundingStable>=.35};
  });
}
function createContactShadows(){
  const canvas=document.createElement('canvas');canvas.width=canvas.height=64;
  const context=canvas.getContext('2d'),gradient=context.createRadialGradient(32,32,4,32,32,32);
  gradient.addColorStop(0,'rgba(35,55,44,.25)');gradient.addColorStop(.5,'rgba(35,55,44,.14)');gradient.addColorStop(1,'rgba(35,55,44,0)');
  context.fillStyle=gradient;context.fillRect(0,0,64,64);
  const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;
  const mesh=new THREE.InstancedMesh(new THREE.PlaneGeometry(1,1),new THREE.MeshBasicMaterial({map:texture,transparent:true,depthWrite:false}),512);
  mesh.frustumCulled=false;mesh.count=0;mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);scene.add(mesh);return mesh;
}
function updateContactShadows(){
  if(!contactShadows.visible)return;
  let index=0;
  const draw=(g,w,d,offset=0,side=0)=>{
    if(!g.visible||index>=contactShadows.instanceMatrix.count)return;
    const c=Math.cos(g.rotation.y),s=Math.sin(g.rotation.y),x=g.position.x+s*offset+c*side,z=g.position.z+c*offset-s*side;
    dummy.position.set(x,groundHeight(x,z)+.025,z);dummy.rotation.set(-Math.PI/2,0,-g.rotation.y);dummy.scale.set(w,d,1);dummy.updateMatrix();
    contactShadows.setMatrixAt(index++,dummy.matrix);
  };
  draw(player,1.3,1.3);
  for(const p of people)draw(p.g,1.2,1.2);
  for(const c of cars){
    draw(c.g,(c.width??.7)+.4,(c.length??3)+.4,c.centerOffset||0);
    if(c.g.visible&&Math.hypot(c.g.position.x-player.position.x,c.g.position.z-player.position.z)<32){
      for(const wheel of c.wheelContacts||[])draw(c.g,wheel.width+.17,wheel.radius*.99,wheel.z,wheel.x);
    }
  }
  for(const a of animals)if(!a.flight)draw(a.g,a.type==='pigeon'?.55:1,a.type==='pigeon'?.55:1.6);
  contactShadows.count=index;contactShadows.instanceMatrix.needsUpdate=true;
}

// Vizinhança se transforma devagar: céu, sol, lua, luz das ruas e interiores.
const nightSky=new THREE.Color('#1f344a'),daySky=new THREE.Color('#98d2e3'),skyColor=new THREE.Color();
const mistDay=new THREE.Color('#c0cdd0'),mistNight=new THREE.Color('#394552');
const showerDay=new THREE.Color('#768b99'),showerNight=new THREE.Color('#273648'),weatherSky=new THREE.Color();
const lampMount=new THREE.Vector3();
const streetLights=Array.from({length:3},()=>{const light=new THREE.PointLight('#ffe1a4',0,22,1.5);scene.add(light);return light;});
// Duas fontes em cada construção próxima mantêm o salão iluminado.
const shopLights=Array.from({length:4},()=>{const light=new THREE.PointLight('#ffe5bb',0,26,1.45);scene.add(light);return light;});
const canopyLight=new THREE.PointLight('#fff0c4',0,38,1.4);canopyLight.position.set(104,5.5,-30.5);scene.add(canopyLight);
const trafficHeadlights=Array.from({length:4},()=>{
  const light=new THREE.SpotLight('#ffe8b5',0,30,.54,.8,1.4),target=new THREE.Object3D();light.target=target;scene.add(light,target);return {light,target,car:null};
});
let nextLampUpdate=0,litPlaces=[],nightAmount=0;
function updateDayNight(time,dt){
  const cycle=time/240*Math.PI*2;
  const sunHeight=Math.cos(cycle);
  const daylight=THREE.MathUtils.smoothstep(sunHeight,-.22,.38);
  const weather=cityWeather.state,cloud=weather.cloud,fog=weather.fog,rain=weather.rain;
  skyColor.copy(nightSky).lerp(daySky,daylight);
  weatherSky.copy(mistNight).lerp(mistDay,daylight);skyColor.lerp(weatherSky,fog*.83);
  weatherSky.copy(showerNight).lerp(showerDay,daylight);skyColor.lerp(weatherSky,Math.min(.94,rain*1.12));
  scene.background.copy(skyColor);scene.fog.color.copy(skyColor);
  const haze=Math.max(fog*.99,rain*.85);
  scene.fog.near=150-142*haze;scene.fog.far=370-283*haze;
  // Approximate the room's bounced light so faces remain readable at night.
  ambient.intensity=(.7+1.45*daylight)*(1-cloud*.15)+(currentPlace?.2*(1-daylight):0);
  sunlight.intensity=(.22+2.48*daylight)*(1-cloud*.86);
  sunlight.color.set(sunHeight<-.2?'#a5c2e5':sunHeight<.4?'#ffd5a4':'#fff0d0');
  const disk=Math.max(0,1-cloud)**1.65;
  skySun.material.opacity=disk;sunHalo.material.opacity=.17*disk;moon.material.opacity=disk;
  skySun.visible=daylight>.25&&disk>.012;sunHalo.visible=skySun.visible;moon.visible=daylight<.75&&disk>.012;
  skySun.position.y=45+Math.sin(cycle)*13;sunHalo.position.y=skySun.position.y;
  moon.position.y=45-Math.sin(cycle)*13;
  const darkness=1-daylight;
  const roadLights=Math.max(darkness,Math.max(0,rain-.10)*.73,Math.max(0,fog-.38)*.93);
  const streetDarkness=Math.max(darkness,Math.max(0,cloud-.66)*.66);
  beacon.emissiveIntensity=.25+darkness*(1.1+2.4*Math.max(0,Math.cos(time*1.55))**8);
  const focus=surveyDrone.active?surveyDrone.position:surveillance.active?.pivot??(vehicle?vehicle.g.position:player.position),shadowStep=130/sunlight.shadow.mapSize.x;
  const sx=Math.round(focus.x/shadowStep)*shadowStep,sz=Math.round(focus.z/shadowStep)*shadowStep;
  const shadowBase=cityElevation(sz);
  sunlight.target.position.set(sx,shadowBase,sz);sunlight.position.set(sx-75,shadowBase+130,sz+75);
  nightAmount=darkness;headMat.emissiveIntensity=.4+roadLights*1.4;
  const bulb=material('#ffefba');bulb.emissive.set('#ffd37d');bulb.emissiveIntensity=streetDarkness*1.5;
  nextLampUpdate-=dt;
  if(nextLampUpdate<=0){
    nextLampUpdate=.6;
    const position=focus;
    const nearest=streetLamps.filter(lamp=>lamp.active).sort((a,b)=>(a.x-position.x)**2+(a.z-position.z)**2-(b.x-position.x)**2-(b.z-position.z)**2);
    streetLights.forEach((light,i)=>{const lamp=nearest[i];light.userData.hasLamp=!!lamp;if(lamp)light.position.set(lamp.x,lamp.y,lamp.z);});
    litPlaces=places.slice().sort((a,b)=>a===currentPlace?-1:b===currentPlace?1:(a.x-position.x)**2+(a.z-position.z)**2-(b.x-position.x)**2-(b.z-position.z)**2).slice(0,2);
    shopLights.forEach((light,i)=>{
      const shop=litPlaces[Math.floor(i/2)];
      if(shop){light.position.set(shop.x+(i%2?1:-1)*shop.w*.23,shop.elevation+4.3,shop.z);light.color.set(shop.kind==='hospital'?'#f1f3e4':'#ffe5bb');}
    });
    const nearbyCars=cars.filter(car=>car.auto&&car.kind!=='bike').sort((a,b)=>
      (a.g.position.x-position.x)**2+(a.g.position.z-position.z)**2-(b.g.position.x-position.x)**2-(b.g.position.z-position.z)**2);
    trafficHeadlights.forEach((beam,i)=>beam.car=nearbyCars[i]||null);
  }
  streetLights.forEach(light=>{light.intensity=streetDarkness*21;light.visible=streetDarkness>.025&&!currentPlace&&!!light.userData.hasLamp;});
  shopLights.forEach((light,i)=>{light.intensity=4+streetDarkness*29;light.visible=streetDarkness>.025||litPlaces[Math.floor(i/2)]===currentPlace;});
  canopyLight.intensity=4+streetDarkness*28;canopyLight.visible=(streetDarkness>.025&&Math.hypot(focus.x-104,focus.z+30.5)<65)||currentPlace?.kind==='station';
  for(const car of cars)if(car.spots?.length)for(const light of car.spots)light.intensity=18+roadLights*48;
  for(const {light,target,car} of trafficHeadlights){
    if(!car){light.visible=false;continue;}
    if(car.spots?.length){
      const first=car.spots[0],last=car.spots[car.spots.length-1];
      first.getWorldPosition(light.position);last.getWorldPosition(lampMount);light.position.lerp(lampMount,.5);
      first.target.getWorldPosition(target.position);last.target.getWorldPosition(lampMount);target.position.lerp(lampMount,.5);
    }else{
      const a=car.g.rotation.y,fx=Math.sin(a),fz=Math.cos(a),front=(car.length??4.6)/2;
      light.position.set(car.g.position.x+fx*front,car.g.position.y+.9,car.g.position.z+fz*front);
      target.position.set(light.position.x+fx*16,0,light.position.z+fz*16);
    }
    target.position.y=groundHeight(target.position.x,target.position.z)+.15;
    light.visible=roadLights>.03&&Math.hypot(car.g.position.x-focus.x,car.g.position.z-focus.z)<85;
    light.intensity=roadLights*65;
  }
}
function updateServiceEffects(){
  for(const phone of publicPhones){
    const ringing=gameTime<phone.ringUntil;
    phone.lamp.emissiveIntensity=ringing?1.2+.65*Math.sin(gameTime*18):0;
    phone.handset.rotation.z=ringing?.045*Math.sin(gameTime*34):0;
  }
  const active=servicePoints.some(point=>point.kind==='fire-water'&&point.active);
  for(const drop of waterDrops){
    drop.mesh.visible=active;if(!active)continue;
    const t=(gameTime*.85+drop.phase)%1;
    drop.mesh.position.set(drop.originX-3.5*t,groundHeight(drop.originX,drop.originZ)+1.75+1.2*t-2.4*t*t,drop.originZ+Math.sin(drop.phase*12)*.12);
  }
  for(const puff of kitchenSteam){
    const phase=(gameTime*.32+puff.phase)%1;
    puff.mesh.visible=Math.hypot(player.position.x-puff.x,player.position.z-puff.z)<85;
    puff.mesh.position.set(puff.x+Math.sin(gameTime+puff.phase)*.12,groundHeight(puff.x,puff.z)+1.9+phase*1.2,puff.z);puff.mesh.scale.setScalar(.1+phase*.24);
  }
}
function updateAnimals(dt){
  const threat=vehicle?vehicle.g.position:player.position;
  birdLife.update(dt,{x:threat.x,z:threat.z,speed:vehicle?Math.abs(vehicle.velocity):Math.hypot(velocity.x,velocity.z)},nightAmount);
  for(const animal of animals){
    const p=animal.g.position,distToOrigin=Math.hypot(p.x-animal.originX,p.z-animal.originZ);
    if(animal.wings)for(const wing of animal.wings)wing.group.rotation.z=wing.side*(animal.flight ? .15+Math.sin(gameTime*26+animal.phase)*.8 : 1.3);
    if(animal.flight)continue;
    if(petting?.animal===animal){
      const stroke=petting.elapsed,warmth=THREE.MathUtils.smoothstep(stroke,0,.45)*(1-THREE.MathUtils.smoothstep(stroke,1.95,2.45));
      faceToward(animal.g,Math.atan2(player.position.x-p.x,player.position.z-p.z),dt,8);
      animal.heading=animal.g.rotation.y;
      if(animal.legs){
        for(const leg of animal.legs)leg.rotation.x=THREE.MathUtils.damp(leg.rotation.x,0,15,dt);
        animal.head.rotation.x=(-.16+.09*Math.sin(stroke*8))*warmth;
        animal.head.rotation.z=(animal.type==='cat'?.27:.13)*Math.sin(stroke*5)*warmth;
        animal.tail.rotation.y=Math.sin(gameTime*(animal.type==='cat'?5:14))*(.22+.46*warmth);
      }else animal.head.position.y=.46+Math.sin(stroke*8)*.045*warmth;
      continue;
    }
    animal.turn-=dt;
    if(animal.turn<=0){animal.turn=1.5+Math.random()*3;animal.heading+=(Math.random()-.5)*1.5;}
    if(distToOrigin>7)animal.heading=Math.atan2(animal.originX-p.x,animal.originZ-p.z);
    const distanceToPlayer=Math.hypot(p.x-player.position.x,p.z-player.position.z);
    if(animal.type==='pigeon'&&distanceToPlayer<2.4&&!vehicle)animal.heading=Math.atan2(p.x-player.position.x,p.z-player.position.z);
    const speed=animal.speed*(animal.type==='pigeon'&&distanceToPlayer<2.4?2:1);
    const nx=p.x+Math.sin(animal.heading)*speed*dt,nz=p.z+Math.cos(animal.heading)*speed*dt;
    const radius=animal.type==='dog'?.58:.48;
    const approaches=(body,clearance)=>{
      const next=(nx-body.x)**2+(nz-body.z)**2;
      return next<clearance**2&&next<=(p.x-body.x)**2+(p.z-body.z)**2;
    };
    const nearHuman=(!vehicle&&!playerActor.ragdoll&&approaches(player.position,radius+.48))||
      people.some(person=>!person.ragdoll&&!person.inBus&&approaches(person.g.position,radius+.46*person.g.scale.x));
    const nearAnimal=animals.some(other=>other!==animal&&!other.flight&&approaches(other.g.position,radius+(other.type==='dog'?.58:.48)));
    if(collides(nx,nz,radius)||vehicleBlocks(nx,nz,radius,null)||nearHuman||nearAnimal){animal.heading+=1.8;animal.turn=.7;}
    else{p.x=nx;p.z=nz;}
    p.y=groundHeight(p.x,p.z);animal.g.rotation.y=animal.heading;
    if(animal.legs){animal.legs.forEach((leg,i)=>leg.rotation.x=Math.sin(timeForAnimal(animal)*6+(i===0||i===3?0:Math.PI))*.45);animal.tail.rotation.y=Math.sin(timeForAnimal(animal)*3)*.35;
      animal.head.rotation.x=THREE.MathUtils.damp(animal.head.rotation.x,.04*Math.sin(timeForAnimal(animal)*6),8,dt);
      animal.head.rotation.z=THREE.MathUtils.damp(animal.head.rotation.z,0,8,dt);
    }
    if(animal.type==='pigeon'){animal.head.position.y=.46+Math.sin(gameTime*5+animal.phase)*.035;animal.body.rotation.x=Math.sin(gameTime*5+animal.phase)*.04;}
  }
}
function timeForAnimal(animal){return gameTime+animal.phase;}

const residentMotion=createResidentMotion({THREE,groundHeight});
function animateResidentSteps(dt){
  for(const person of people){
    const {g}=person;
    residentMotion.update(person,dt,gameTime,{talking:activeSpeech?.target===person||!!(person.chatting&&person.stationary&&!person.routinePose&&!person.performance)||pedestrianLife.isPaused(person),
      external:!!person.push||!!person.exitingVehicle||cityPolice.controls(person)});
    if(g.visible&&!person.ragdoll&&!person.inBus){
      pedestrianLife.animateAttention(person,dt,gameTime);
      if(person.combatFlinch>gameTime){
        if(g.userData.head)g.userData.head.rotation.x=.26*Math.min(1,(person.combatFlinch-gameTime)*4);
        if(defendingResidents.has(person))faceToward(g,Math.atan2(player.position.x-g.position.x,player.position.z-g.position.z),dt,10);
        const arm=g.userData.limbs?.[2]||g.userData.arms?.[0];if(arm)arm.rotation.x=-1.1;
      }
    }
  }
}

let applauseUntil=0;
const pilotHands=[new THREE.Vector3(),new THREE.Vector3()],pilotPose={hands:pilotHands};
function animatePlayer(dt){
  if(!player.visible)return;
  if(applauseUntil>gameTime&&(strideSpeed>.2||combat.equipped||scanning||surveyDrone.active||vehicle||vault||petting||sitting||marine.mode!=='land'||jumpHeight>.01))applauseUntil=0;
  const wheel=marine.activeBoat?.wheel;
  if(wheel){wheel.updateWorldMatrix(true,false);wheel.localToWorld(pilotHands[0].set(-.16,0,0));wheel.localToWorld(pilotHands[1].set(.16,0,0));}
  if(petting){
    const bird=petting.animal.type==='pigeon';
    petContact.set(.045,bird?.14:.33,(bird?0:.10)+Math.sin(petting.elapsed*9)*(bird?.045:.14));
    petting.animal.head.localToWorld(petContact);
  }
  playerMotion.update(dt,{time:gameTime,phase:walkPhase,speed:strideSpeed,airborne:jumpHeight>.015&&!vault,
    clap:gameTime<applauseUntil&&strideSpeed<.2&&!combat.equipped&&!scanning&&!surveyDrone.active&&!vehicle&&!vault&&!petting&&!sitting&&marine.mode==='land',
    swim:marine.mode==='swim'?{effort:marine.effort,stroke:marine.stroke,climbing:!!marine.climb}:null,helm:wheel?pilotPose:null,
    weapon:combatCanUse()?combat.equipped:null,aimed:combat.aiming,aimPoint:aimSample?aimWorld:null,reloading:combat.reloading,recoil:Math.max(0,(recoilUntil-gameTime)/.14),
    verticalSpeed:verticalVelocity,vault,pet:petting,petPoint:petContact,scan:scanning||surveyDrone.active||!!surveillance.active,
    seatHeight:sitting?.bench.seatTop,seatBlend:sitting?.blend,
    boarding:vehicleTransition&&{progress:vehicleTransition.poseProgress||0,side:vehicleTransition.side}});
}

const timer=new THREE.Clock();let metricTime=performance.now(),metricFrames=0,metricFps=0;
let repaintPaused=false;
let combatControlsState='';
let aimedResident=null,noticeWeaponAt=0;
function updateCombatView(dt){
  const available=combatCanUse(),id=available?combat.equipped:null;
  if(!available&&(combat.aiming||rightAim||aimToggle))dropCombatAim();
  if(combat.aiming&&gameTime>=nextAimSample){aimSample=sampleCombatAim();aimWorld.copy(aimSample.point);nextAimSample=gameTime+.075;}
  const seen=combat.aiming&&aimSample?.actor,near=seen&&seen.g.position.distanceToSquared(player.position)<12*12?seen:null;
  if(near!==aimedResident){aimedResident=near;noticeWeaponAt=gameTime+.6;}
  if(near&&near.role!=='police'&&gameTime>=noticeWeaponAt&&interactionReachable(near.g.position.x,near.g.position.z)){
    combat.startle(near,2);near.combatFlinch=gameTime+.55;pedestrianLife.interrupt(near,gameTime);
    if(activeSpeech?.target===near||activeSpeech?.target?.attendant===near)clearSpeech();noticeWeaponAt=gameTime+.9;
  }
  combatView.pose(id,combat.aiming,aimWorld);combatView.sight(aimSample,combat.aiming);combatView.update(dt);
  const state=`${available}/${id}/${combat.aiming}`;
  if(state!==combatControlsState){combatControlsState=state;
    $('#touchDroneLaunch').classList.toggle('hidden',!!id);
    $('#touchCombat').classList.toggle('hidden',!available);combatButtons.aim.classList.toggle('hidden',!id);
    combatButtons.fire.classList.toggle('hidden',!combat.aiming);combatButtons.reload.classList.toggle('hidden',!id);
    combatButtons.aim.setAttribute('aria-pressed',String(combat.aiming));world.classList.toggle('combat-aim',combat.aiming);}
}
function prepareView(){
  syncTouchDriving();
  let shadowFrustum=null;
  if(renderer.shadowMap.enabled){
    sunlight.updateWorldMatrix(true,false);sunlight.target.updateWorldMatrix(true,false);
    sunlight.shadow.updateMatrices(sunlight);shadowFrustum=sunlight.shadow.getFrustum();
  }
  actorBatches.update(new Set([player,...people.map(p=>p.g),...cars.map(c=>c.g),...animals.map(a=>a.g),...boats.map(b=>b.g),
    ...destructibles.filter(p=>p.active).map(p=>p.group),...benches.filter(b=>b.group.parent).map(b=>b.group),...cityFixtures,...places.map(p=>p.upper)]),camera,shadowFrustum);
  updateContactShadows();
}
function animate(){
  requestAnimationFrame(animate);const dt=Math.min(timer.getDelta(),.25);
  if(settings.open){
    if(repaintPaused){prepareView();ambientOcclusion.render();repaintPaused=false;}
    return;
  }
  const steps=Math.max(1,Math.ceil(dt/.025)),step=dt/steps;
  for(let i=0;i<steps;i++){
    cityWeather.update(step);
    gameTime+=step;if(combat.aiming&&!combatCanUse())dropCombatAim();combat.update(step);updateSelfDefense();updateSignals(gameTime);updateDoors(step);updateVehicleImpacts(step);updatePlayer(step);updateTraffic(step);updateBusPassengers(step);citySocial.update(step,gameTime);npcLife.update(step,gameTime);updatePeople(step);updateResidentPushes(step);updateAnimals(step);impactActors();
    updateRagdolls(step);pedestrianLife.update(step,gameTime);cityPolice.update(step,gameTime);updateParticles(step);updateDebris(step);
  }
  fishMarket.update(dt,gameTime,player.position);
  harbor.update(dt,gameTime,cityWeather.state);
  streetCinema.update(dt,surveyDrone.active?surveyDrone.position:player.position,cityWeather.state.rain);
  streetPerformance.update(dt,gameTime,{rain:cityWeather.state.rain,
    interrupted:!!streetMusician.ragdoll||!combat.canTalk(streetMusician)||activeSpeech?.target===streetMusician||!!streetMusician.push||
      combat.isThreatening()&&player.position.distanceToSquared(streetMusician.g.position)<16**2,
    play:(kind,x,z)=>cityAudio.play(kind,x,z)});
  practiceRange.update(dt);
  if(gameTime>=nextWalletCheck){nextWalletCheck=gameTime+1;economy.checkpoint();}
  updateBoats(dt);updateServiceEffects();updateActorVisibility(dt);updateVehicleGrounding(dt);updateGroundingDiagnostics(dt);updateDayNight(gameTime,dt);citySurfaces.update(gameTime,cityWeather.state,skyColor);animatePlayer(dt);updateSpeech();
  animateResidentSteps(dt);socialProps.update(people);cars.forEach(animateVehicleLights);
  updateIndoorVisibility();updateCamera(dt);camera.updateMatrixWorld();updateLink();updateInteractionSign();updateCombatView(dt);
  policeRadio.update(gameTime,camera,player,vehicle,policeCustody());
  rainField.update(dt,camera.position,cityWeather.state,nightAmount,settings.values.quality,softwareRendering);
  cityAudio.update({dt,time:gameTime,position:surveyDrone.active?surveyDrone.position:vehicle?vehicle.g.position:player.position,
    yaw:surveyDrone.active?surveyDrone.yaw:yaw,vehicle,vehicles:cars,weather:cityWeather.state,exposure:listenerRainExposure(),
    walking:!surveyDrone.active&&marine.mode==='land'&&!vehicle&&!sitting&&!petting&&!vault&&!playerActor.ragdoll&&jumpHeight<.02,
    paved:!!currentPlace||isPaved(player.position.x,player.position.z),inside:!!currentPlace,doors:places,night:nightAmount});
  cityAudio.aircraft(surveyDrone.mode==='stowed'?null:surveyDrone.position,gameTime);
  prepareView();
  if(debugMode){
    metricFrames++;const now=performance.now();if(now-metricTime>=1000){metricFps=metricFrames*1000/(now-metricTime);metricFrames=0;metricTime=now;}
    document.documentElement.dataset.city=JSON.stringify({hack:tag.visible?tagText:null,time:gameTime,fps:metricFps,drawCalls:renderer.info.render.calls,camera:{x:camera.position.x,y:camera.position.y,z:camera.position.z},player:{x:player.position.x,y:player.position.y,z:player.position.z,ground:groundHeight(player.position.x,player.position.z),ragdoll:!!playerActor.ragdoll,platform:playerPlatform?.car.kind||playerPlatform?.car.role||(playerPlatform?'car':null),gait:walkPhase,limbs:player.userData.limbs.map(l=>l.rotation.x)},car:vehicle?{x:vehicle.g.position.x,z:vehicle.g.position.z,speed:vehicle.velocity,heading:vehicle.g.rotation.y,steering:vehicle.steeringAngle||0,gear:vehicle.gear||null,rpm:vehicle.engineRpm||null,kind:vehicle.kind||'car',role:vehicle.role||null,driverShown:!!vehicle.driver?.visible,emergencyLights:!!vehicle.emergency?.on,signal:vehicle.signal||null,reverse:vehicle.velocity<-.3,braking:!!vehicle.braking}:null,inside:currentPlace?.name||null,candidate:nearestInteraction()?.type||null,doors:places.map(p=>({name:p.name,x:p.door.x,z:p.door.z,angle:p.door.angle,offset:p.door.offset,automatic:p.door.automatic,open:p.door.open})),people:people.length,cars:cars.length,petting:!!petting,sitting:!!sitting,props:destructibles.filter(p=>p.active).length,debris:debris.length,
    weather:{...cityWeather.state,...rainField.snapshot(),forecast:cityWeather.forecast()},interaction:interactionSign.visibleLabel,vegetationOnPavement:vegetationPositions.filter(p=>isPaved(p.x,p.z)).length,terrainSamples:hills.map(([x,z])=>terrainHeight(x,z)),grounding:groundingDebug,routines:npcLife.snapshot(),birds:birdLife.snapshot(),audio:cityAudio.snapshot(),effects:worldEffects.snapshot(),rendering:{software:softwareRendering,pixelRatio:renderer.getPixelRatio(),shadows:renderer.shadowMap.enabled,surfaces:citySurfaces.snapshot(),ambientOcclusion:ambientOcclusion.snapshot()},
    incidents:cars.filter(c=>c.incident||c.incidentCount).map(c=>({id:c.debugId||null,state:c.incident?.state||null,count:c.incidentCount,speed:c.velocity,hazards:!!c.hazards,driverShown:c.driver.visible,actor:c.incident?.actor?{x:c.incident.actor.g.position.x,z:c.incident.actor.g.position.z}:null})),
    traffic:cars.filter(c=>c.auto).map(c=>({id:c.debugId||null,x:c.g.position.x,z:c.g.position.z,speed:c.velocity,heading:c.g.rotation.y,steering:c.steeringAngle||0,lateral:c.lateralSpeed||0,yawRate:c.yawRate||0,length:c.length||4.6,driverShown:!!c.driver?.visible,stoppedReason:c.stoppedReason,signal:c.signal,avoidStage:c.overtake?.stage||null,avoids:c.avoids||0,impactSpeed:c.impact?Math.hypot(c.impact.x,c.impact.z):0})),
    signals:junctions.map(j=>({x:j.x,z:j.z,...j.phase,override:j.reopenAt>gameTime})),feed:surveillance.snapshot(),
    pedestrians:{...pedestrianLife.snapshot(),speaker:ambientSpeaker?.name??null,line:ambientLine,
      nearby:people.filter(p=>!p.inBus&&!p.ragdoll&&Math.hypot(p.g.position.x-player.position.x,p.g.position.z-player.position.z)<18)
        .map(p=>({name:p.name,id:p.debugId,x:p.g.position.x,z:p.g.position.z,scale:p.g.scale.x,paused:pedestrianLife.isPaused(p),yaw:p.g.rotation.y}))},
    handling:vehicle?.handling?{body:vehicle.design?.name??vehicle.kind,wheelbase:vehicle.handling.wheelbase,
      handbrake:vehicle.handbrakeLevel||0,slip:vehicle.tireSlip||0,lateral:vehicle.lateralSpeed||0,yawRate:vehicle.yawRate||0,
      frontGrip:vehicle.tireSurface?.frontGrip,rearGrip:vehicle.tireSurface?.rearGrip}:null,
    marine:marine.snapshot(),combat:combat.snapshot(),motion:{pose:player.visible?playerMotion.mode:null,vault:vault?vault.elapsed/vault.duration:null,
      pet:petting?.animal.type||null,stroke:petting?.elapsed??null,
      palmGap:petting?playerMotion.hand.getWorldPosition(actionPoint).distanceTo(petContact):null},
    harbor:harbor.snapshot(),drone:surveyDrone.snapshot(),cinema:streetCinema.snapshot(),music:streetPerformance.snapshot(),social:{...citySocial.snapshot(),props:socialProps.snapshot()},
    services:servicePoints.map(p=>({kind:p.kind,x:p.x,z:p.z,active:!!p.active})),police:{...cityPolice.snapshot(),patrol:patrolSnapshot()},
    bus:{x:cityBus.g.position.x,z:cityBus.g.position.z,speed:cityBus.velocity,impactSpeed:cityBus.impact?Math.hypot(cityBus.impact.x,cityBus.impact.z):0,recoveringRoute:!!cityBus.recoveringRoute,stoppedReason:cityBus.stoppedReason,currentStop:cityBus.atStop?.id||null,doors:!!cityBus.atStop,dwell:cityBus.dwell,passengers:cityBus.passengers.length,playerOnBus:vehicle===cityBus,exitRequested:!!cityBus.exitRequested,stopCount:cityBus.stopCount},
    economy:economy.snapshot(),districts:districtTracks.map(track=>({id:track.id,oneWay:track.oneWay,length:track.length,stops:track.stops.length,connected:true})),
    districtCars:cars.filter(c=>c.track&&c.kind!=='bus').map(c=>({id:c.debugId||null,road:c.track.id,x:c.g.position.x,z:c.g.position.z,speed:c.velocity,lane:c.lane,targetLane:c.laneTarget,stoppedReason:c.stoppedReason,stopCount:c.stopCount,avoids:c.avoids})),
    placesByKind:{homes:places.filter(p=>p.kind==='home').length,residences:places.filter(p=>p.kind==='residence').length,restaurants:places.filter(p=>p.kind==='restaurant').length,parking:pavedAreas.filter(p=>p.kind==='parking').length,vacant:vacantLots.length},
    speech:activeSpeech?{name:activeSpeech.name,line:activeSpeech.line,remaining:activeSpeech.until-gameTime,speaker:activeSpeech.target.g?{x:activeSpeech.target.g.position.x,z:activeSpeech.target.g.position.z}:null}:null,
    lighting:{night:nightAmount,places:litPlaces.map(p=>p.name),shopIntensity:shopLights[0].intensity,canopyIntensity:canopyLight.intensity,trafficBeams:trafficHeadlights.filter(b=>b.light.visible&&b.light.intensity>1).length,playerBeams:vehicle?.spots?.filter(l=>l.visible&&l.intensity>1).length||0},
    brokenSignals:destructibles.filter(p=>p.type==='signal'&&!p.active).length,vehicleAnimation:vehicleTransition?.kind||null,impactTarget:(()=>{const c=cars.find(car=>car.debugId==='impact-target');return c?{x:c.g.position.x,z:c.g.position.z,heading:c.g.rotation.y,impactSpeed:c.impact?Math.hypot(c.impact.x,c.impact.z):0,angularSpeed:c.impact?.angular||0}:null;})(),debugPerson:people.find(p=>p.debugId==='yield')?.g.position||null,pushActor:(()=>{const p=people.find(p=>p.debugId==='push');return p?{x:p.g.position.x,z:p.g.position.z,pushing:!!p.push}:null;})(),conversationActor:(()=>{const p=people.find(p=>p.debugId==='conversation');return p?{x:p.g.position.x,z:p.g.position.z,yaw:p.g.rotation.y,responding:activeSpeech?.target===p}:null;})()});
  }
  ambientOcclusion.render();
}
// Dados somente de leitura para inspecionar o estado durante a revisão.
if(debugMode)Object.defineProperty(window,'cityDebug',{get:()=>({
  garage:{controlled:vehicle?.design?.id??null,street:[...new Set(cars.filter(c=>c.auto&&c.design).map(c=>c.design.model))],
    display:displayCars.map(c=>({id:c.design.id,model:c.design.model,x:c.g.position.x,z:c.g.position.z,
      width:c.width,length:c.length,wheelbase:c.handling.wheelbase,tire:c.design.tire,roof:c.design.roof}))},
  weather:{...cityWeather.state,forecast:cityWeather.forecast(),...rainField.snapshot()},social:{...citySocial.snapshot(),props:socialProps.snapshot()},
  police:{...cityPolice.snapshot(),patrol:patrolSnapshot()},
  fishMarket:fishMarket.snapshot(),economy:economy.snapshot(),practice:practiceRange.snapshot(),harbor:harbor.snapshot(),drone:surveyDrone.snapshot(),
  music:streetPerformance.snapshot(),
  feed:surveillance.snapshot(),
  player:{x:player.position.x,y:player.position.y,z:player.position.z,ground:groundHeight(player.position.x,player.position.z),ragdoll:!!playerActor.ragdoll},
  car:vehicle?{x:vehicle.g.position.x,z:vehicle.g.position.z,speed:vehicle.velocity}:null,
  inside:currentPlace?.name||null,
  candidate:(nearestInteraction()?.type)||null,
  doors:places.map(p=>({name:p.name,x:p.door.x,z:p.door.z,angle:p.door.angle,offset:p.door.offset,automatic:p.door.automatic,open:p.door.open})),
  people:people.length,cars:cars.length,objects:scene.children.length
})});
if(debugMode){
  const destination=new URLSearchParams(location.search).get('near');
  if(destination==='musica'){
    player.position.set(-26,groundHeight(-26,22.1),22.1);player.rotation.y=Math.PI;
    yaw=targetYaw=-.14;pitch=targetPitch=.22;zoom=targetZoom=7.5;
  }
  const docks={cais:[157,-141.65,-.36],drone:[155.35,-145.16,-.29],radio:[145.1,-149.38,0],cinema:[-57.7,-35.37,-1.44],
    guindaste:[159,-147.15,0],boletim:[180.1,-154.35,0],pontal:[173.8,-175.1,Math.PI]};
  if(Object.hasOwn(docks,destination)){
    const [x,z,a]=docks[destination];player.position.set(x,groundHeight(x,z),z);yaw=targetYaw=a;
    player.rotation.y=a+Math.PI;pitch=targetPitch=.25;zoom=targetZoom=destination==='cais'?9.5:6.3;
  }
  if(destination==='motores'){
    player.position.set(-160.6,groundHeight(-160.6,-96.3),-96.3);player.rotation.y=Math.PI;
    yaw=.025;pitch=.26;zoom=13.9;
    const model=new URLSearchParams(location.search).get('modelo'),selected=displayCars.find(car=>car.design.id===model);
    if(selected){
      const point=incidentPoint(selected,-(selected.width/2+.65),selected.seatMount.z);
      player.position.set(point.x,groundHeight(point.x,point.z),point.z);player.rotation.y=selected.g.rotation.y;
      yaw=selected.g.rotation.y-.7;pitch=.24;zoom=7;
    }
  }
  if(destination==='fuga'){
    const car=cars.find(c=>c.auto&&c.axis==='x'&&!c.patrolRoute&&!c.track&&!c.curve);
    car.g.position.set(31,groundHeight(31,3.3),3.3);car.g.rotation.y=-Math.PI/2;
    Object.assign(car,{laneRoad:0,direction:-1,velocity:0,debugHoldUntil:120,debugId:'fuga'});
    player.position.set(31,groundHeight(31,5.85),5.85);yaw=0;pitch=.25;zoom=8.5;
  }
  const counters={oficina:[36,101.85,.06],carabina:[42.6,101.85,.10],recompra:[27.78,111.3,.05],
    extrato:[30,49.05,.06],atendimento:[98,36.2,.05],expedicao:[158.58,-122.72,Math.PI],
    guia:[-22.65,56.65,.1],estande:[54,107.9,0],'oficina-frente':[37,122,.16]};
  if(Object.hasOwn(counters,destination)){
    const [x,z,a]=counters[destination];player.position.set(x,groundHeight(x,z),z);player.rotation.y=a+Math.PI;
    yaw=targetYaw=a;pitch=targetPitch=.21;zoom=6.35;
  }
  if(destination==='camera'||destination==='camera-hospital'){
    const plaza=destination==='camera',unit=surveillance.devices[plaza?0:1],x=plaza?-31.1:85.2,z=plaza?62.8:64;
    player.position.set(x,groundHeight(x,z),z);yaw=Math.atan2(x-unit.pivot.x,z-unit.pivot.z);pitch=.16;zoom=8.2;
  }
  if(destination==='fish-market'||destination==='fish-weights'||destination==='fish-load'||destination==='fish-vendor'||destination==='fish-access'||fishMarket.debugSpawns[destination]){
    const spawn=fishMarket.debugSpawns[destination]||(destination==='fish-weights'?{x:157.4,z:-125.5}:destination==='fish-load'?{x:162.6,z:-125.5}:destination==='fish-vendor'?{x:151,z:-121.8}:destination==='fish-access'?{x:139,z:-120}:fishMarket.spawn);
    player.position.set(spawn.x,groundHeight(spawn.x,spawn.z),spawn.z);yaw=targetYaw=destination==='fish-access'?-Math.PI/2:Math.PI;pitch=targetPitch=.28;
  }
  const place=places.find(p=>p.kind===destination);
  if(place){const d=place.door;player.position.set(d.x,groundHeight(d.x,d.z+4),d.z+4);yaw=0;pitch=.32;}
  if(destination==='mirante'){
    player.position.set(overlook.x,groundHeight(overlook.x,overlook.z),overlook.z);player.rotation.y=Math.PI;
    yaw=-.24;pitch=.16;zoom=6.1;
  }
  if(destination==='subida'){
    const c=cars[0],x=3.3,z=122;c.g.position.set(x,groundHeight(x,z),z);c.g.rotation.y=0;
    player.position.set(x+2.3,groundHeight(x+2.3,z-.3),z-.3);yaw=Math.PI;pitch=.25;zoom=6.6;
  }
  if(destination==='beach'){player.position.set(0,0,-145);yaw=0;pitch=.15;}
  if(['nadar','lancha','escada','barco-porto'].includes(destination)){
    const where=destination==='nadar'?[-25,-162.3]:destination==='lancha'?[2.06,-173.18]:destination==='escada'?[-4.43,-173.1]:[172.12,-173.5];
    player.position.set(where[0],destination==='escada'?-1.55:groundHeight(...where),where[1]);player.rotation.y=Math.PI;
    yaw=targetYaw=destination==='lancha'?-1.0:destination==='barco-porto'?1.02:0;pitch=targetPitch=.19;zoom=targetZoom=7.05;
  }
  if(destination==='farol'){player.position.set(-26,groundHeight(-26,-141),-141);yaw=.34;pitch=.11;zoom=13;}
  if(destination==='dirigir'){
    const c=cars[0];c.g.position.set(-38,groundHeight(-38,-134.3),-134.3);c.g.rotation.y=Math.PI/2;
    player.position.set(-38.3,groundHeight(-38.3,-136.55),-136.55);yaw=2.45;pitch=.32;zoom=7;
  }
  if(destination==='bike'){player.position.set(-51,groundHeight(-51,60.7),60.7);yaw=0;pitch=.32;}
  if(destination==='vault-wall'||destination==='vault-car'){
    const wall=destination==='vault-wall',x=wall?-26:-16.22,z=wall?58.85:65.3;
    player.position.set(x,groundHeight(x,z),z);player.rotation.y=0;yaw=Math.PI;pitch=.28;zoom=7.5;
  }
  if(destination==='cat'){
    const cat=animals.find(a=>a.type==='cat').g.position;
    player.position.set(cat.x+1.25,groundHeight(cat.x+1.25,cat.z+.35),cat.z+.35);
    yaw=.15;pitch=.25;zoom=6;
  }
  if(destination==='mural'){player.position.set(20.8,groundHeight(20.8,35.2),35.2);yaw=-Math.PI/2;pitch=.25;}
  if(destination==='telefone'){player.position.set(-58.5,groundHeight(-58.5,60.8),60.8);yaw=0;pitch=.27;zoom=7;}
  if(destination==='poltrona'){
    const book=places.find(p=>p.kind==='book'),x=book.x+4,z=book.z+6.05;
    player.position.set(x,groundHeight(x,z),z);player.rotation.y=Math.PI;yaw=.32;pitch=.36;zoom=5.6;
  }
  if(destination==='esquina-oeste'){player.position.set(-140,groundHeight(-140,9),9);yaw=-.55;pitch=.75;zoom=13;}
  if(destination==='fountain'){player.position.set(-36,groundHeight(-36,43.4),43.4);yaw=0;pitch=.42;}
  if(destination==='birds'){
    animals.filter(a=>a.type==='pigeon').slice(0,3).forEach((bird,i)=>{const x=-45+i*1.4,z=30+(i%2)*.8;bird.g.position.set(x,groundHeight(x,z),z);bird.originX=x;bird.originZ=z;});
    player.position.set(-45,groundHeight(-45,24),24);yaw=Math.PI;pitch=.32;
  }
  if(destination==='curve'){player.position.set(0,groundHeight(0,132),132);yaw=Math.PI;pitch=.32;}
  if(destination==='plaza-curve'){player.position.set(-12,groundHeight(-12,72),72);yaw=0;pitch=.9;zoom=13;}
  if(destination==='car'||destination==='jump-car'){
    const c=cars[0],x=destination==='jump-car'?-162.8:-150;c.g.position.set(x,groundHeight(x,-103),-103);c.g.rotation.y=0;
    if(destination==='jump-car'){player.position.set(x,groundHeight(x,-106.1),-106.1);yaw=Math.PI;}
    else{player.position.set(x+2.4,groundHeight(x+2.4,-103.3),-103.3);yaw=Math.PI/2;}
    pitch=.32;
  }
  if(destination==='curb'||destination==='vehicle-hill'){
    const c=cars[0],x=destination==='curb'?3.3:-180,z=destination==='curb'?24:97;
    c.g.position.set(x,groundHeight(x,z),z);c.g.rotation.y=destination==='curb'?Math.PI/2:0;
    player.position.set(x+(destination==='curb'?0:2.8),groundHeight(x,z),z+(destination==='curb'?2.8:0));
    yaw=destination==='curb'?-Math.PI/2:Math.PI;pitch=.25;
  }
  if(destination==='cross'){player.position.set(0,groundHeight(0,83),83);yaw=0;pitch=.32;}
  if(destination==='ring'){player.position.set(111,groundHeight(111,72),72);yaw=-Math.PI/2;pitch=.3;}
  if(destination==='coast'){player.position.set(0,groundHeight(0,-114),-114);yaw=0;pitch=.25;}
  if(destination==='loop'){player.position.set(190,groundHeight(190,curvedRoadZ(190)),curvedRoadZ(190));yaw=-Math.PI/2;pitch=.3;}
  if(destination==='hill'){player.position.set(0,groundHeight(0,195),195);yaw=Math.PI;pitch=.32;}
  if(destination==='fire-water'){const p=servicePoints.find(p=>p.kind==='fire-water');player.position.set(p.x,groundHeight(p.x,p.z),p.z);yaw=-Math.PI/2;pitch=.32;}
  if(destination==='police-info'){const p=servicePoints.find(p=>p.kind==='police-info');player.position.set(p.x,groundHeight(p.x,p.z),p.z);yaw=0;pitch=.32;}
  if(destination==='engine'){player.position.set(-38.5,groundHeight(-38.5,-87),-87);yaw=Math.PI/2;pitch=.32;}
  if(destination==='patrol'){player.position.set(27.5,groundHeight(27.5,-87),-87);yaw=-Math.PI/2;pitch=.32;}
  if(destination==='officer'){player.position.set(42,groundHeight(42,-86.2),-86.2);yaw=0;pitch=.32;}
  if(destination==='firefighter'){player.position.set(-29,groundHeight(-29,-86.2),-86.2);yaw=0;pitch=.32;}
  if(destination==='bus'||destination==='bus-stop'||destination==='routine-bus'){
    const stop=busTrack.stops.find(s=>s.id==='acacias');cityBus.routeProgress=stop.progress-(destination==='bus-stop'?12:0);
    const p=trackSample(busTrack,cityBus.routeProgress);cityBus.g.position.set(p.x,groundHeight(p.x,p.z),p.z);cityBus.g.rotation.y=Math.atan2(p.dx,p.dz);cityBus.velocity=destination==='bus-stop'?4:0;
    cityBus.lastStop=null;if(destination==='bus'){cityBus.atStop=stop;cityBus.dwell=7;}
    player.position.set(-65.2,groundHeight(-65.2,-33.6),-33.6);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='bus-curve'){
    cityBus.routeProgress=trackNearest(busTrack,-68.7,54);const p=trackSample(busTrack,cityBus.routeProgress);
    cityBus.g.position.set(p.x,groundHeight(p.x,p.z),p.z);cityBus.g.rotation.y=Math.atan2(p.dx,p.dz);
    Object.assign(cityBus,{velocity:3,atStop:null,lastStop:'praca',debugId:'bus-curve'});
    player.position.set(-82,groundHeight(-82,58),58);yaw=-Math.PI/2;pitch=.32;
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
  if(destination==='theft'){
    const c=cars.find(c=>c.auto&&c.axis==='z'&&!c.track&&c.kind!=='bus');c.g.position.set(-38,groundHeight(-38,16),16);c.g.rotation.y=0;
    Object.assign(c,{velocity:0,debugId:'theft',debugHoldUntil:100});player.position.set(-35.6,groundHeight(-35.6,16),16);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='signal'||destination==='travessia'){
    const z=destination==='travessia'?-31:-22;
    const c=cars.find(c=>c.auto&&c.axis==='z'&&!c.track&&c.kind!=='bus');c.g.position.set(3.3,groundHeight(3.3,z),z);c.g.rotation.y=0;
    Object.assign(c,{velocity:7,direction:1,laneRoad:0,debugId:'signal'});player.position.set(8,groundHeight(8,-18),-18);yaw=Math.PI/2;pitch=.32;
    if(destination==='travessia'){player.position.set(10.4,groundHeight(10.4,-9.85),-9.85);yaw=Math.PI/2;pitch=.23;zoom=8.5;}
  }
  if(destination==='signal-break'){
    const c=cars[0];c.g.position.set(-9,groundHeight(-9,-3),-3);c.g.rotation.y=0;
    player.position.set(-6.6,groundHeight(-6.6,-3.3),-3.3);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='boarding'){
    const c=cars.find(c=>c.auto&&c.axis==='z');c.g.position.set(3.3,groundHeight(3.3,56),56);c.g.rotation.y=0;c.velocity=7;c.direction=1;c.laneRoad=0;c.debugId='boarding';
    player.position.set(5.8,groundHeight(5.8,56),56);yaw=0;pitch=.32;
  }
  if(destination==='conversation'){
    const p=people.find(p=>!p.indoor&&!p.stationary);p.g.position.set(-36,groundHeight(-36,16),16);Object.assign(p,{axis:'x',origin:-36,range:8,dir:1,speed:1.2,wait:0,debugId:'conversation'});
    player.position.set(-36,groundHeight(-36,18.3),18.3);yaw=0;pitch=.32;
  }
  if(destination==='moradores'){
    const pair=people.filter(p=>!p.role&&!p.stationary&&!p.indoor).slice(0,2);
    pair.forEach((p,i)=>{
      const x=-46+(i?1.05:-1.05),z=36;p.g.position.set(x,groundHeight(x,z),z);
      Object.assign(p,{name:i?'Rosa':'Nilo',debugId:i?'morador-b':'morador-a',axis:'x',origin:-46,range:3.6,
        dir:i?-1:1,speed:1.12,wait:0});
      p.g.rotation.y=p.dir*Math.PI/2;
    });
    player.position.set(-46,groundHeight(-46,38.25),38.25);yaw=0;pitch=.22;zoom=7;
  }
  if(destination==='push'){
    const p=people.find(p=>!p.indoor&&!p.stationary&&!p.role);p.g.position.set(-38,groundHeight(-38,20),20);p.stationary=true;p.debugId='push';
    player.position.set(-38,groundHeight(-38,22.2),22.2);yaw=0;pitch=.32;
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
  if(destination==='break'||destination==='lamp-collision'){
    const testCar=cars[0],z=destination==='break'?16:26.4;testCar.g.position.set(-42,groundHeight(-42,z),z);testCar.g.rotation.y=0;
    player.position.set(-39.6,groundHeight(-39.6,z-.3),z-.3);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='yield'){
    const testCar=cars.find(c=>c.auto&&!c.curve&&c.axis==='z');testCar.g.position.set(3.3,groundHeight(3.3,56),56);testCar.g.rotation.y=0;testCar.direction=1;testCar.laneRoad=0;testCar.velocity=7;testCar.debugId='yield';
    const testPerson=people.find(p=>!p.indoor&&!p.stationary);testPerson.g.position.set(3.3,groundHeight(3.3,65),65);testPerson.stationary=true;testPerson.debugId='yield';
    player.position.set(12,groundHeight(12,58),58);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='turn'){
    const testCar=cars.find(c=>c.auto&&c.axis==='x');testCar.g.position.set(96,groundHeight(96,-3.3),-3.3);testCar.g.rotation.y=Math.PI/2;testCar.direction=1;testCar.laneRoad=0;testCar.velocity=5;testCar.debugId='turn';
    player.position.set(96,groundHeight(96,10),10);yaw=0;pitch=.32;
  }
  if(destination==='incident'){
    const target=cars.find(c=>c.auto&&c.axis==='z'&&!c.track);target.g.position.set(3.3,groundHeight(3.3,56),56);target.g.rotation.y=0;
    Object.assign(target,{direction:1,laneRoad:0,velocity:0,debugId:'incident',debugHoldUntil:10000});
    const car=cars[0];car.g.position.set(3.3,groundHeight(3.3,46),46);car.g.rotation.y=0;
    player.position.set(6.1,groundHeight(6.1,46),46);yaw=0;pitch=.32;
  }
  if(destination==='impact-parked'||destination==='impact-side'||destination==='impact-wall'||destination==='impact-glance'){
    const x=destination==='impact-glance'?2.1:3.3,car=cars[0],target=cars[1];car.g.position.set(x,groundHeight(x,-59),-59);car.g.rotation.y=0;
    target.g.position.set(3.3,groundHeight(3.3,-45),-45);target.g.rotation.y=destination==='impact-side'?Math.PI/2:0;target.debugId='impact-target';
    if(destination==='impact-wall'){solid(3.3,-40.6,8,.3);part(scene,3.3,1.5,-40.6,8,3,.3,'#c6c6b3');}
    player.position.set(x+2.8,groundHeight(x+2.8,-59),-59);yaw=0;pitch=.32;
  }
  if(destination==='impact-solid-glance'){
    const car=cars[0];car.g.position.set(3.3,groundHeight(3.3,-59),-59);car.g.rotation.y=.25;
    solid(3.3,-44,20,.3);part(scene,3.3,1.5,-44,20,3,.3,'#c6c6b3');
    player.position.set(6.1,groundHeight(6.1,-59),-59);yaw=0;pitch=.32;
  }
  if(destination==='impact-npc-side'){
    const target=cars.find(c=>c.auto&&c.axis==='z'&&!c.track&&c.kind!=='bus');target.g.position.set(3.3,groundHeight(3.3,-36),-36);target.g.rotation.y=0;
    Object.assign(target,{direction:1,laneRoad:0,velocity:0,debugId:'incident',debugHoldUntil:10000});
    const car=cars[0];car.g.position.set(-8,groundHeight(-8,-36),-36);car.g.rotation.y=Math.PI/2;
    player.position.set(-8,groundHeight(-8,-38.8),-38.8);yaw=0;pitch=.32;
  }
  if(destination==='impact-bus'){
    const stop=busTrack.stops.find(s=>s.id==='acacias'),p=trackSample(busTrack,stop.progress);
    cityBus.routeProgress=stop.progress;cityBus.g.position.set(p.x,groundHeight(p.x,p.z),p.z);cityBus.g.rotation.y=Math.atan2(p.dx,p.dz);
    Object.assign(cityBus,{velocity:0,atStop:stop,dwell:100,lastStop:null,debugId:'impact-bus'});
    const car=cars[0];car.g.position.set(p.x,groundHeight(p.x,p.z-14),p.z-14);car.g.rotation.y=0;
    player.position.set(p.x+2.8,groundHeight(p.x+2.8,p.z-14),p.z-14);yaw=0;pitch=.32;
  }
  if(['pass','pass-blocked','pass-delay','pass-person','pass-solid'].includes(destination)){
    const driver=cars.find(c=>c.auto&&c.axis==='z'&&!c.track&&c.kind!=='bus');
    driver.g.position.set(3.3,groundHeight(3.3,-58),-58);driver.g.rotation.y=0;
    Object.assign(driver,{direction:1,laneRoad:0,velocity:5,debugId:'pass',turning:null});
    if(destination==='pass-solid'){solid(3.3,-36,2.2,3.5);part(scene,3.3,.9,-36,2.2,1.8,3.5,'#9b8a6d');}
    else{const obstacle=cars[0];obstacle.g.position.set(3.3,groundHeight(3.3,-36),-36);obstacle.g.rotation.y=0;}
    if(destination==='pass-blocked'||destination==='pass-delay'){
      const oncoming=cars.find(c=>c!==driver&&c.auto&&c.axis==='z'&&!c.track&&c.kind!=='bus');
      oncoming.g.position.set(-3.3,groundHeight(-3.3,-24),-24);oncoming.g.rotation.y=Math.PI;
      Object.assign(oncoming,{direction:-1,laneRoad:0,velocity:0,debugId:'oncoming',debugHoldUntil:destination==='pass-delay'?6:10000,turning:null});
    }
    if(destination==='pass-person'){
      const g=human('#689cad','#d59d76');g.position.set(-3.3,groundHeight(-3.3,-45),-45);scene.add(g);
      people.push({g,name:'Morador',line:'Estou atravessando.',stationary:true,indoor:false,debugId:'pass-person'});
    }
    player.position.set(12,groundHeight(12,-48),-48);yaw=Math.PI/2;pitch=.32;
  }
  if(destination==='avoid'||destination==='avoid-blocked'){
    const track=districtTracks[0],car=cars.find(c=>c.track===track),obstacle=cars.find(c=>c.track===track&&c!==car),progress=track.branchEnd*.25;
    for(const [c,advance,lane] of [[car,0,-2.6],[obstacle,11,-2.6]]){
      c.routeProgress=progress+advance;c.lane=c.laneTarget=c.homeLane=lane;c.debugId=c===car?'avoid':'obstacle';c.debugHoldUntil=c===car?0:10000;
      const p=districtTrackPosition(track,c.routeProgress,lane);c.g.position.set(p.x,groundHeight(p.x,p.z),p.z);c.g.rotation.y=Math.atan2(p.dx,p.dz);c.velocity=c===car?5:0;
    }
    if(destination==='avoid-blocked'){
      const blocker=cars.find(c=>!c.auto&&!c.kind);blocker.g.position.copy(obstacle.g.position);const p=districtTrackPosition(track,progress+7,2.6);
      blocker.g.position.set(p.x,groundHeight(p.x,p.z),p.z);blocker.g.rotation.y=Math.atan2(p.dx,p.dz);
    }
    const p=districtTrackPosition(track,progress,-2.6);player.position.set(p.x-9,groundHeight(p.x-9,p.z),p.z);yaw=-Math.PI/2;pitch=.32;
  }
  targetYaw=yaw;targetPitch=pitch;targetZoom=zoom;
  const requestedTime=Number(new URLSearchParams(location.search).get('time'));if(Number.isFinite(requestedTime))gameTime=requestedTime;
}
const lifeLocations=[
  {id:'praca-celular',x:-28,z:54,activity:'phone',capacity:2,rotation:Math.PI},
  {id:'praca-leitura',x:-53,z:42,activity:'read',capacity:2},
  {id:'praca-conversa',x:-24,z:46,activity:'chat',capacity:3},
  {id:'praca-brincar',x:-45,z:44,activity:'play',capacity:2},
  {id:'praca-limpeza',x:-43,z:56,activity:'work',capacity:1},
  {id:'limpeza-mercado',x:-86,z:31,activity:'work',capacity:1},
  {id:'praca-descanso',x:-17,z:27,activity:'rest',capacity:2},
  {id:'cafe-terraco',x:23,z:51,activity:'eat',capacity:2},
  {id:'fumantes-jardim',x:51,z:53,activity:'smoke',capacity:2},
  {id:'lago-brincar',x:99,z:112,activity:'play',capacity:2},
  {id:'lago-celular',x:111,z:98,activity:'phone',capacity:2},
  {id:'lago-descanso',x:94,z:108,activity:'rest',capacity:2},
  {id:'orla-leitura',x:-30,z:-146,activity:'read',capacity:2},
  {id:'orla-conversa',x:31,z:-146,activity:'chat',capacity:3},
  {id:'orla-descanso',x:94,z:-146,activity:'rest',capacity:2},
  {id:'jardim-leitura',x:-17,z:172,activity:'read',capacity:2},
  {id:'jardim-conversa',x:18,z:172,activity:'chat',capacity:3},
  {id:'acacias-celular',x:-149,z:-32,activity:'phone',capacity:1},
  {id:'mares-celular',x:149,z:-32,activity:'phone',capacity:1},
  {id:'acacias-leitura',x:-159,z:-33,activity:'read',capacity:1},
  {id:'mares-leitura',x:159,z:-33,activity:'read',capacity:1},
  {id:'acacias-vizinhos',x:-154,z:37,activity:'chat',capacity:2},
  {id:'mares-vizinhos',x:154,z:37,activity:'chat',capacity:2},
  {id:'acacias-descanso',x:-147,z:36,activity:'rest',capacity:1},
  {id:'mares-descanso',x:147,z:36,activity:'rest',capacity:1},
  {id:'padaria-vizinhos',x:-95,z:-21,activity:'chat',capacity:2},
  {id:'livraria-celular',x:46,z:-22,activity:'phone',capacity:1},
  {id:'mercado-limpeza',x:-98,z:51,activity:'work',capacity:1},
  {id:'mural-aurora',x:23.4,z:36,activity:'paint',capacity:1,rotation:Math.PI/2}
];
for(const p of places){
  if(p.kind==='home'||p.kind==='residence')lifeLocations.push({id:`casa-${p.kind}-${p.x}`,x:p.x,z:p.z+2,activity:'home',capacity:2});
  if(p.kind==='restaurant'||p.kind==='cafe'||p.kind==='bakery')lifeLocations.push({id:`comer-${p.kind}-${p.x}`,x:p.x,z:p.z+3.8,activity:'eat',capacity:2,rotation:Math.PI});
  if(['market','flower','book','gallery'].includes(p.kind))lifeLocations.push({id:`trabalho-${p.kind}`,x:p.x,z:p.z+4.5,activity:'work',capacity:1});
}
for(const stop of busStops)lifeLocations.push({id:`espera-${stop.id}`,x:stop.x+(stop.x<0?1.1:-1.1),z:stop.z-3.5,activity:'wait',capacity:1,rotation:stop.rotation,stopId:stop.id});
// Initial residents are placed at valid destinations before the first frame;
// subsequent travel always follows the city's collision-checked paths.
const initialResidents=people.filter(p=>!p.stationary&&!p.indoor&&!p.role&&!p.debugId);
const initialSpots=lifeLocations.filter(l=>!['wait','home'].includes(l.activity));
for(let i=0;i<Math.min(22,initialResidents.length,initialSpots.length);i++){
  const person=initialResidents[i],spot=initialSpots[i];if(person===urbanCleaner||person===urbanPainter)continue;
  if(collides(spot.x,spot.z,.5)||vehicleBlocks(spot.x,spot.z,.5,null)||people.some(p=>p!==person&&Math.hypot(p.g.position.x-spot.x,p.g.position.z-spot.z)<1.3))continue;
  person.g.position.set(spot.x,groundHeight(spot.x,spot.z),spot.z);person.g.rotation.y=spot.rotation||0;
  person.lifeResident=true;person.indoor=!!placeContaining(spot.x,spot.z);person.initialDestination=spot.id;
  person.routine=[spot.activity,'chat','eat','home','phone','rest'];
  if(spot.activity==='play'){person.child=true;person.g.scale.setScalar(.72);person.routine=['play','rest','eat','home'];}
}
for(const person of initialResidents){
  const p=person.g.position;if(!pointOnRoad(p.x,p.z)&&!collides(p.x,p.z,.4))continue;
  const spot=sidewalkGraph.filter(n=>!pointOnRoad(n.x,n.z)&&!collides(n.x,n.z,.45)&&!vehicleBlocks(n.x,n.z,.5,null)&&
    people.every(other=>other===person||Math.hypot(other.g.position.x-n.x,other.g.position.z-n.z)>1.8))
    .sort((a,b)=>Math.hypot(p.x-a.x,p.z-a.z)-Math.hypot(p.x-b.x,p.z-b.z))[0];
  if(spot)p.set(spot.x,groundHeight(spot.x,spot.z),spot.z);
}
if(debugMode&&new URLSearchParams(location.search).get('near')==='routine-home'){
  const person=initialResidents.find(p=>!p.child),home=places.find(p=>p.kind==='home');
  person.name='Vizinha de teste';person.g.position.set(home.x,groundHeight(home.x,home.door.z+4),home.door.z+4);person.indoor=false;
  person.initialDestination=`casa-home-${home.x}`;person.routine=['home','phone','work'];
  player.position.set(home.x+5,groundHeight(home.x+5,home.door.z+6),home.door.z+6);yaw=targetYaw=0;
}
if(debugMode&&new URLSearchParams(location.search).get('near')==='routine-bus'){
  const person=initialResidents.find(p=>!p.child),stop=busStops[0];
  person.name='Passageira de teste';person.g.position.set(-60.6,groundHeight(-60.6,-40.3),-40.3);person.indoor=false;
  person.initialDestination=`espera-${stop.id}`;person.routine=['wait','phone','home'];
  player.position.set(-57,groundHeight(-57,-33),-33);yaw=targetYaw=Math.PI/2;
}
// The attendant faces the counter from its open end and can hear the nearby payphone.
const iceCreamStand=[[-54.72,53.55],[-55.4,54.58],[-56.4,54.58]].find(([x,z])=>
  !collides(x,z,.46)&&!vehicleBlocks(x,z,.46,null)&&people.every(p=>Math.hypot(x-p.g.position.x,z-p.g.position.z)>1.25));
if(iceCreamStand){
  const seller=worker(...iceCreamStand,'Inês · sorveteira','O de cajá saiu agora. Quer experimentar?','#91b3aa','#f2dfb9');
  seller.g.rotation.y=-.75;part(seller.g,0,1.21,.246,.61,.69,.034,'#e9dbc0');
}
if(debugMode&&new URLSearchParams(location.search).get('near')==='pedestres'){
  const car=cars[0];car.g.position.set(5.8,groundHeight(5.8,-16.4),-16.4);car.g.rotation.y=0;
  const p=initialResidents.find(resident=>!resident.role&&!resident.child&&!resident.busState);
  p.g.position.set(10.2,groundHeight(10.2,-8.8),-8.8);p.g.rotation.y=-Math.PI/2;
  Object.assign(p,{name:'Davi',trafficDemo:true,indoor:false,initialDestination:'revisao-faixa',routine:['read','rest','home']});
  lifeLocations.push({id:'revisao-faixa',x:-15,z:-8.8,activity:'read',capacity:1,duration:28});
  player.position.set(8.15,groundHeight(8.15,-16.75),-16.75);yaw=targetYaw=Math.PI+.32;pitch=targetPitch=.25;
}
const pedestrianLife=createPedestrianLife({people,cars,groundHeight,onRoad:pointOnRoad,
  canReact:p=>activeSpeech?.target!==p,focus:()=>surveillance.active?.pivot??(vehicle?vehicle.g.position:player.position),
  canSee:(a,b)=>interactionReachable(b.x,b.z,a),
  canStep(p,x,z,options={}){
    const pos=p.g.position;
    if(options.allowRoad&&p.nav?.crossing&&p.nav.landing&&pointOnRoad(x,z)){
      const {band,entry,landing}=p.nav;
      if(band?Math.abs((band.axis==='x'?x:z)-band.center)>1.22:
        distanceToSegment(x,z,entry.x,entry.z,landing.x,landing.z)>1.35)return false;
    }
    return !collides(x,z,.36)&&!vehicleBlocks(x,z,.42,options.ignore)&&
      (options.allowRoad||!pointOnRoad(x,z)||(options.contact&&pointOnRoad(pos.x,pos.z)))&&
      (vehicle||playerActor.ragdoll||Math.hypot(player.position.x-x,player.position.z-z)>.88)&&
      !animals.some(a=>!a.flight&&Math.hypot(a.g.position.x-x,a.g.position.z-z)<.70);
  },
  canPause(p){
    const pos=p.g.position,step=p.nav?.path?.[p.nav.index];
    if(!combat.canTalk(p)||p.performance||citySocial?.isPaused(p)||citySocial?.protectsFromRain(p))return false;
  return !cityPolice.controls(p)&&!p.busState&&!p.indoor&&!p.sitting&&!p.push&&!p.incidentCar&&!p.exitingVehicle&&
      (!p.debugId||p.debugId.startsWith('morador-'))&&(!p.stationary||p.chatting)&&
      activeSpeech?.target!==p&&!p.nav?.crossing&&!step?.crossing&&!step?.door&&!pointOnRoad(pos.x,pos.z)&&
      !junctions.some(j=>Math.abs(j.x-pos.x)<12&&Math.abs(j.z-pos.z)<12)&&
      !places.some(place=>Math.hypot(place.door.x-pos.x,place.door.z-pos.z)<3);
  }});
lifeLocations.push({id:'roda-do-ipe',x:-25.1,z:24.2,activity:'rest',capacity:2,slotSpacing:2.5,duration:[20,32],rotation:Math.PI});
for(const [name,x,z,shirt,skin] of [['Maya',-27.1,24.0,'#658e9a','#b97e5a'],['Nilo',-23.4,24.5,'#9b7659','#e4b58a']]){
  const g=human(shirt,skin,'#485565','#3c393c');g.position.set(x,groundHeight(x,z),z);g.rotation.y=Math.PI;scene.add(g);
  people.push({g,name,line:'A praça fica boa com um som ao vivo.',lifeResident:true,indoor:false,phase:random()*6,
    initialDestination:'roda-do-ipe',routine:['rest','phone','chat','eat','home'],musicGuest:true});
}
pedestrianLife.refresh();
npcLife=createNpcLife({THREE,scene,people,player,groundHeight,isTalking:p=>cityPolice.controls(p)||activeSpeech?.target===p||!!p.push||pedestrianLife.isPaused(p)||!!citySocial?.isPaused(p),
  moveTo:moveResident,locations:lifeLocations,random,canStand:(x,z)=>!collides(x,z,.45)&&!pointOnRoad(x,z)&&!vehicleBlocks(x,z,.45,null)});
if(debugMode&&new URLSearchParams(location.search).get('near')==='pet'){
  const dog=animals.find(a=>a.type==='dog'&&a.originX===-24),p=dog.g.position;
  const neighbors=[...animals.filter(a=>a!==dog),...people.filter(a=>!a.inBus)].map(a=>a.g.position);
  let best=null,space=-Infinity;
  for(let i=0;i<12;i++){
    const a=i*Math.PI/6,x=p.x+Math.sin(a)*1.55,z=p.z+Math.cos(a)*1.55;
    if(collides(x,z,.42)||vehicleBlocks(x,z,.42,null))continue;
    player.position.set(x,groundHeight(x,z),z);
    const clearance=Math.min(...neighbors.map(n=>Math.hypot(x-n.x,z-n.z)));
    if(clearance>space&&nearestInteraction()?.data===dog){space=clearance;best=player.position.clone();}
  }
  if(best)player.position.copy(best);else player.position.set(p.x,groundHeight(p.x,p.z+1.55),p.z+1.55);
  yaw=targetYaw=0;pitch=targetPitch=.32;
}
if(debugMode&&new URLSearchParams(location.search).get('near')==='seat'){
  const b=benches.find(b=>!b.occupied&&people.every(p=>Math.hypot(p.g.position.x-b.x,p.g.position.z-b.z)>5));
  const x=b.x+Math.sin(b.rotation)*1.7,z=b.z+Math.cos(b.rotation)*1.7;
  player.position.set(x,groundHeight(x,z),z);yaw=targetYaw=b.rotation;pitch=targetPitch=.32;
}
const actorBatches=createActorBatches({THREE,scene});
const contactShadows=createContactShadows();
const worldEffects=createWorldEffects({THREE,scene,groundHeight,
  lake:{x:pond.position.x,z:pond.position.z,rx:pond.scale.x,rz:pond.scale.z},
  trees:cameraObstacles.filter(o=>o.w===o.d&&o.w>=.49&&o.w<=.8&&Math.abs((o.top-o.bottom)/o.w-4/.7)<.01)});
const birdLife=createBirdLife({birds:animals.filter(a=>a.type==='pigeon'),groundHeight,random,isPetting:b=>petting?.animal===b,
  canTravel:(x,z)=>!collides(x,z,.48)&&!vehicleBlocks(x,z,.48,null),
  canLand:(x,z)=>!collides(x,z,.48)&&!pointOnRoad(x,z)&&!vehicleBlocks(x,z,.48,null)&&!personBlocks(x,z,player)&&(vehicle||Math.hypot(x-player.position.x,z-player.position.z)>1),
  onTakeoff:(x,z)=>cityAudio.play('flutter',x,z)});
if(debugMode&&new URLSearchParams(location.search).get('near')==='birds')animals.filter(a=>a.type==='pigeon').slice(0,3).forEach(b=>b.flightCooldown=0);
const world=$('#world');
function resizeWorld(){
  const width=world.clientWidth,height=world.clientHeight;
  if(!width||!height)return;
  camera.aspect=width/height;camera.updateProjectionMatrix();renderer.setSize(width,height,false);repaintPaused=true;
}
weatherCover=createRainCover(cameraObstacles,groundHeight);
citySurfaces.setWeatherCover(weatherCover.pavementMask());
const rainField=createRainField(THREE,scene,weatherCover,groundHeight);
const socialEye=new THREE.Vector3(),socialRay=new THREE.Vector3();
citySocial=createCitySocial({people,focus:()=>surveyDrone.active?surveyDrone.position:camera.position,
  drone:()=>({active:surveyDrone.active,x:surveyDrone.position.x,y:surveyDrone.position.y,z:surveyDrone.position.z}),
  weather:()=>cityWeather.state,music:()=>streetPerformance.source,
  exposed:p=>weatherCover.exposed(p.g.position.x,p.g.position.z,p.g.position.y+1.7*p.g.scale.y),
  canOpenUmbrella:(_p,c)=>!collides(c.x,c.z,c.radius,c.y,c.height,true)&&umbrellaClear(c,viewObstacles(c,c.radius,null,true)),
  canReact:p=>!cityPolice.controls(p)&&combat.canTalk(p)&&activeSpeech?.target!==p&&activeSpeech?.target?.attendant!==p&&
    !pedestrianLife.isPaused(p)&&!pedestrianLife.attentionPriority(p,gameTime)&&!defendingResidents.has(p)&&
    !(combat.isThreatening()&&p.g.position.distanceToSquared(player.position)<18**2),
  canPause(p){
    const pos=p.g.position,step=p.nav?.path?.[p.nav.index];
    return !p.stationary&&!p.nav?.crossing&&!step?.crossing&&!step?.door&&
      (p.lifeState!=='activity'||['wait','rest','chat'].includes(p.lifeActivity))&&
      ![[-.75,0],[.75,0],[0,-.75],[0,.75]].some(([dx,dz])=>pointOnRoad(pos.x+dx,pos.z+dz))&&
      !junctions.some(j=>Math.abs(j.x-pos.x)<12&&Math.abs(j.z-pos.z)<12)&&
      !places.some(place=>Math.hypot(place.door.x-pos.x,place.door.z-pos.z)<3);
  },
  canSee(p,source){
    const pos=p.g.position;socialEye.set(pos.x,pos.y+1.75*p.g.scale.y,pos.z);
    socialRay.set(source.x,(source.y??groundHeight(source.x,source.z)+1.5),source.z).sub(socialEye);
    const distance=socialRay.length();if(distance<.1)return true;socialRay.divideScalar(distance);
    return cameraClearance(socialEye,socialRay,distance,viewObstacles(socialEye,distance,p),0)>=distance-.13;
  }});
const socialProps=createSocialProps(THREE,{invalidate:root=>actorBatches.invalidate(root)});
function listenerRainExposure(){
  const remote=surveillance.active||surveyDrone.active;
  const p=remote?camera.position:vehicle?vehicle.g.position:player.position;
  const covered=!weatherCover.exposed(p.x,p.z,p.y+(remote?0:1.55));
  if(!remote&&vehicle&&vehicle.kind!=='bike')return covered?.09:.24;
  return covered?(placeContaining(p.x,p.z)?.12:.34):1;
}
const settings=createSettings({canvas:renderer.domElement,apply(values){
  const cap=values.quality==='low'?.8:values.quality==='high'?2:softwareRendering?.7:1.6;
  const ratio=Math.min(devicePixelRatio,cap);
  if(renderer.getPixelRatio()!==ratio)renderer.setPixelRatio(ratio);
  renderer.shadowMap.enabled=values.quality==='high'||(values.quality==='auto'&&!softwareRendering);
  contactShadows.visible=!renderer.shadowMap.enabled;
  camera.fov=values.fov;camera.updateProjectionMatrix();cityAudio.setVolume(values.volume/100);repaintPaused=true;
},onPause(paused){
  dropCombatAim();
  for(const key of Object.keys(keys))keys[key]=false;
  walkCamera.reset();
  touchRunKey=null;cancelLink();tag.visible=false;
  if(cameraPointer!==null&&renderer.domElement.hasPointerCapture(cameraPointer))renderer.domElement.releasePointerCapture(cameraPointer);
  cameraPointer=null;cityAudio.setPaused(paused);timer.getDelta();repaintPaused=true;
}});
let saveStorage;try{saveStorage=window.localStorage;}catch{}
const economy=createCityEconomy(combat,saveStorage);
let nextWalletCheck=1;
addEventListener('storage',e=>economy.onStorage(e));
addEventListener('pagehide',()=>economy.checkpoint());
document.addEventListener('visibilitychange',()=>{if(document.hidden)economy.checkpoint();});
resizeWorld();new ResizeObserver(resizeWorld).observe(world);
animate();
