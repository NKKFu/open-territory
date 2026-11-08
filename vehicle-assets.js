import * as THREE from './vendor/three.module.js';

// All dimensions are in body space, with +Z toward the bonnet. The wheel,
// door, glazing and walkable surfaces use the same sections as the bodywork.
export const vehicleDesigns=[
  {id:'sedan',name:'sedã',model:'Aurora S',width:2.42,length:4.74,axles:[-1.46,1.48],tire:.47,
    belt:1.19,hood:1.115,deck:1.11,roof:2.07,windBase:1.08,windTop:.42,backBase:-1.68,backTop:-.98,
    pillar:-.39,roofHalf:.88,endCut:.20,drive:1,mass:1,lock:.64,frontWeight:.57,frontDrive:.94},
  {id:'estate',name:'perua',model:'Maré Touring',width:2.40,length:4.96,axles:[-1.52,1.48],tire:.47,
    belt:1.24,hood:1.14,deck:1.25,roof:2.22,windBase:1.11,windTop:.52,backBase:-2.29,backTop:-1.99,
    pillar:-.34,roofHalf:.94,endCut:.19,drive:.96,mass:1.14,lock:.60,frontWeight:.54,frontDrive:.82},
  {id:'pickup',name:'picape',model:'Caburé 4x2',width:2.52,length:5.00,axles:[-1.57,1.54],tire:.52,
    belt:1.34,hood:1.28,deck:.92,roof:2.29,windBase:1.04,windTop:.56,backBase:-.74,backTop:-.65,
    pillar:-.61,roofHalf:.97,endCut:.15,drive:.88,mass:1.31,lock:.55,frontWeight:.51,frontDrive:.10},
  {id:'hatch',name:'compacto',model:'Faísca',width:2.18,length:4.02,axles:[-1.24,1.25],tire:.43,
    belt:1.17,hood:1.02,deck:1.16,roof:2.02,windBase:1.18,windTop:.65,backBase:-1.79,backTop:-1.43,
    pillar:-.42,roofHalf:.84,endCut:.25,drive:.96,mass:.91,lock:.68,frontWeight:.59,frontDrive:.97},
  {id:'van',name:'furgão',model:'Foz Cargo',width:2.38,length:4.80,axles:[-1.45,1.44],tire:.47,
    belt:1.30,hood:1.19,deck:1.30,roof:2.49,windBase:1.39,windTop:.84,backBase:-2.26,backTop:-2.08,
    pillar:-.23,roofHalf:1.00,endCut:.16,drive:.87,mass:1.29,lock:.59,frontWeight:.59,frontDrive:.93}
];
export const carDesign=index=>vehicleDesigns[((index%vehicleDesigns.length)+vehicleDesigns.length)%vehicleDesigns.length];

const cache=new WeakMap(),paintCache=new Map();
const up=new THREE.Vector3(0,1,0),along=new THREE.Vector3();
const mix=(a,b,t)=>a+(b-a)*t;
const rubber='#253037',alloy='#a1aca9',gasket='#24343c',interior='#455458';

function bodyPaint(color){
  if(!paintCache.has(color))paintCache.set(color,new THREE.MeshStandardMaterial({color,metalness:.025,roughness:.78}));
  return paintCache.get(color);
}

// Keep glass, paint and black trim in separate reusable meshes. The winding is
// corrected for both sides of the car, so the lighting follows the stamped folds.
function panelSet(){
  const layers=new Map();
  function polygon(layer,points,normal){
    if(!layers.has(layer))layers.set(layer,[]);
    const data=layers.get(layer),a=points[0];
    for(let i=1;i<points.length-1;i++){
      const b=points[i],c=points[i+1],ux=b[0]-a[0],uy=b[1]-a[1],uz=b[2]-a[2],
        vx=c[0]-a[0],vy=c[1]-a[1],vz=c[2]-a[2];
      const dot=(uy*vz-uz*vy)*normal[0]+(uz*vx-ux*vz)*normal[1]+(ux*vy-uy*vx)*normal[2];
      if(Math.abs(dot)<1e-9)continue;
      if(dot>0)data.push(...a,...b,...c);else data.push(...a,...c,...b);
    }
  }
  return {polygon,finish(){
    const bundle=new Map();
    for(const [key,vertices] of layers){
      const geo=new THREE.BufferGeometry();geo.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));
      geo.computeVertexNormals();geo.computeBoundingSphere();geo.userData.cityBatch=true;bundle.set(key,geo);
    }
    return bundle;
  }};
}

function sections(d){
  const end=d.length/2,w=d.width/2,front=d.windBase,rear=d.backBase;
  const rearShoulder=d.id==='pickup'?1.43:d.deck;
  const stops=[[-end,w-d.endCut,rearShoulder-.16],[-end+.16,w-.075,rearShoulder-.055],
    [Math.min(rear,-end+.55),w-.035,rearShoulder],[rear,w-.02,d.belt],[front,w-.028,d.belt],
    [end-.65,w-.065,d.hood],[end-.18,w-d.endCut*.62,d.hood-.035],[end,w-d.endCut,d.hood-.13]]
    .sort((a,b)=>a[0]-b[0]);
  const unique=stops.filter((p,i)=>!i||p[0]-stops[i-1][0]>.001);
  return z=>{
    const i=unique.findIndex(p=>p[0]>=z-1e-8);
    const a=unique[Math.max(0,i<0?unique.length-1:i-1)],b=unique[i<0?unique.length-1:i];
    const t=a===b?0:Math.max(0,Math.min(1,(z-a[0])/(b[0]-a[0])));
    const width=mix(a[1],b[1],t),height=mix(a[2],b[2],t);
    let base=d.id==='pickup'?.45:.38;
    for(const axle of d.axles){
      const dz=Math.abs(z-axle),radius=d.tire+.062;
      if(dz<radius)base=Math.max(base,d.tire+.019+Math.sqrt(radius*radius-dz*dz));
    }
    return {z,width,height,base};
  };
}

function template(d){
  if(cache.has(d))return cache.get(d);
  const fixed=panelSet(),doors=new Map([-1,1].map(s=>[s,panelSet()]));
  const end=d.length/2,half=d.width/2,foot=half-.13,top=d.roof-.105,section=sections(d);
  const doorFront=d.windBase-.035,doorBack=d.pillar+.018,floor=.72;
  const cuts=[-end,-end+.16,-end+.46,d.backBase,d.backTop,doorBack,d.windTop,doorFront,d.windBase,end-.65,end-.18,end];
  for(const axle of d.axles){
    const arch=d.tire+.062;
    cuts.push(axle-arch-.012,axle+arch+.012);
    for(let i=0;i<=8;i++)cuts.push(axle+Math.cos(i*Math.PI/8)*arch);
  }
  const zs=[...new Set(cuts.map(z=>Math.round(z*100000)/100000))].sort((a,b)=>a-b);
  const sideVert=(s,v,h,edge)=>[s*(v.width+edge),h,v.z];
  for(let i=1;i<zs.length;i++){
    const a=section(zs[i-1]),b=section(zs[i]),middle=(a.z+b.z)/2;
    const open=middle>doorBack&&middle<doorFront,bed=d.id==='pickup'&&middle<d.backBase;
    for(const s of [-1,1]){
      function rings(v){
        const high=open?Math.max(floor,v.base+.035):v.height;
        const low=Math.min(v.base,high-.035);
        return [sideVert(s,v,low,-.068),sideVert(s,v,mix(low,high,.18),0),
          sideVert(s,v,high-.048,-.012),sideVert(s,v,high,-.115)];
      }
      const u=rings(a),v=rings(b);
      for(let j=0;j<3;j++){
        fixed.polygon(j===0?'rocker':'paint',[u[j],v[j],v[j+1],u[j+1]],[s,j===2?.55:0,0]);
      }
      // The driver can be seen through the open door above the low sill.
      if(open){
        const door=doors.get(s);
        const lowA=sideVert(s,a,Math.max(floor+.018,a.base+.025),-.012),lowB=sideVert(s,b,Math.max(floor+.018,b.base+.025),-.012);
        const shoulderA=sideVert(s,a,a.height-.085,.002),shoulderB=sideVert(s,b,b.height-.085,.002);
        door.polygon('paint',[lowA,lowB,shoulderB,shoulderA],[s,0,0]);
        door.polygon('paint',[shoulderA,shoulderB,sideVert(s,b,b.height,-.11),sideVert(s,a,a.height,-.11)],[s,1,0]);
        door.polygon('inside',[[s*(half-.18),lowA[1],a.z],[s*(half-.18),lowB[1],b.z],
          [s*(half-.18),d.belt,b.z],[s*(half-.18),d.belt,a.z]],[-s,0,0]);
      }
      const cabin=middle>d.backBase&&middle<d.windBase;
      if(!open&&!bed){
        const innerA=cabin?foot-.035:0,innerB=cabin?foot-.035:0;
        fixed.polygon('paint',[u[3],v[3],[s*innerB,b.height,b.z],[s*innerA,a.height,a.z]],[0,1,0]);
      }
    }
  }
  for(const s of [-1,1]){
    const z=s*end,v=section(z),w=v.width-.055;
    fixed.polygon('paint',[[-w+.10,.43,z],[w-.10,.43,z],[w,.62,z],[w,v.height-.035,z],
      [w-.09,v.height,z],[-w+.09,v.height,z],[-w,v.height-.035,z],[-w,.62,z]],[0,0,s]);
  }
  function pane(s,z,y,extra=0){
    return [s*(mix(foot,d.roofHalf,(y-d.belt)/(top-d.belt))+extra),y,z];
  }
  const topCurve=.032;
  for(const s of [-1,1]){
    fixed.polygon('roof',[[s*d.roofHalf,top,d.backTop],[s*d.roofHalf,top,d.windTop],
      [s*(d.roofHalf-.075),d.roof-topCurve,d.windTop-.055],[s*(d.roofHalf-.075),d.roof-topCurve,d.backTop+.055]],[s,1,0]);
    fixed.polygon('roof',[[s*(d.roofHalf-.075),d.roof-topCurve,d.backTop+.055],[s*(d.roofHalf-.075),d.roof-topCurve,d.windTop-.055],
      [0,d.roof,d.windTop-.055],[0,d.roof,d.backTop+.055]],[0,1,0]);
  }
  for(const face of [-1,1]){
    const footZ=face>0?d.windBase:d.backBase,topZ=face>0?d.windTop:d.backTop;
    const low=d.belt+.015;
    fixed.polygon(face<0&&d.id==='van'?'paint':'glass',[[-foot+.05,low,footZ],[foot-.05,low,footZ],
      [d.roofHalf-.04,top,topZ],[-d.roofHalf+.04,top,topZ]],[0,.35,face]);
    for(const side of [-1,1])fixed.polygon('roof',[[0,top,topZ],[side*d.roofHalf,top,topZ],
      [side*(d.roofHalf-.075),d.roof-topCurve,topZ-face*.055],[0,d.roof,topZ-face*.055]],[0,.65,face]);
  }
  for(const s of [-1,1]){
    const paneSection=(set,mat,lowerFront,upperFront,lowerRear,upperRear,pad=.025)=>{
      set.polygon(mat,[pane(s,lowerRear,d.belt+pad),pane(s,lowerFront,d.belt+pad),pane(s,upperFront,top-pad),
        pane(s,upperRear,top-pad)],[s,.13,0]);
    };
    paneSection(doors.get(s),'glass',doorFront-.02,d.windTop-.036,doorBack+.024,d.pillar-.02);
    if(d.id==='van')paneSection(fixed,'paint',d.pillar-.01,d.pillar-.046,d.backBase+.035,d.backTop+.045,0);
    else if(d.id!=='pickup')paneSection(fixed,'glass',d.pillar-.08,d.pillar-.11,d.backBase+.05,d.backTop+.045);
    else paneSection(fixed,'paint',d.pillar,d.pillar-.015,d.backBase,d.backTop,0);
  }
  const noseWidth=section(end).width-.03,tailWidth=section(-end).width-.035;
  const fascia=d.id==='pickup'?1.00:d.id==='van'?.94:d.id==='hatch'?.83:.90;
  // Sheet-metal lamp recesses and inlaid lenses turn onto the fenders. They
  // share the face of the panel rather than hanging rectangular boxes off it.
  for(const s of [-1,1]){
    const indicator=s<0?'left':'right';
    function frontFace(layer,uv,depth){
      fixed.polygon(layer,uv.map(([x,y])=>[s*x,y,end+depth]),[0,0,1]);
    }
    function tailFace(layer,uv,depth){
      fixed.polygon(layer,uv.map(([x,y])=>[s*x,y,-end-depth]),[0,0,-1]);
    }
    function liftgate(layer,uv,depth){
      const z=y=>mix(d.backBase,d.backTop,(y-d.belt)/(top-d.belt));
      fixed.polygon(layer,uv.map(([x,y])=>[s*x,y,z(y)-depth]),[0,.12,-1]);
    }
    if(d.id!=='hatch'){
      const outer=noseWidth,inner=outer-(d.id==='pickup'?.43:.46);
      const high=fascia+(d.id==='van'?.18:.10),low=fascia-(d.id==='van'?.17:.13);
      frontFace('gasket',[[inner,high-.035],[outer-.07,high],[outer,high-.038],[outer,low+.052],[outer-.08,low],[inner+.064,low+.032]],.022);
      frontFace('lamp',[[inner+.025,high-.048],[outer-.07,high-.023],[outer-.023,high-.052],
        [outer-.032,high-.096],[inner+.055,high-.110]],.028);
      frontFace('lamp',[[outer-.14,high-.10],[outer-.036,high-.096],[outer-.04,low+.065],[outer-.13,low+.044]],.029);
      frontFace(indicator,[[inner+.08,low+.075],[inner+.21,low+.065],[inner+.21,low+.102],[inner+.07,low+.114]],.030);
      const aft=end-.22,wing=section(aft).width;
      fixed.polygon('gasket',[[s*outer,high-.04,end+.023],[s*(wing+.006),high-.042,aft],
        [s*(wing+.004),low+.10,aft+.02],[s*outer,low+.055,end+.023]],[s,0,1]);
      fixed.polygon(indicator,[[s*(outer+.002),high-.078,end+.025],[s*(wing+.009),high-.083,aft+.028],
        [s*(wing+.009),high-.112,aft+.035],[s*(outer+.002),high-.117,end+.025]],[s,0,1]);
    }
    const x=tailWidth,inside=x-(d.id==='pickup'?.25:.44);
    const low=d.id==='pickup'?.70:.75,high=d.id==='pickup'?1.10:.99;
    tailFace('tailBase',[[inside,high],[x-.03,high],[x,high-.04],[x,low+.025],[inside+.045,low]],.015);
    tailFace('brake',[[inside+.025,high-.023],[x-.03,high-.023],[x-.028,high-.097],[inside+.065,high-.105]],.021);
    tailFace(indicator,[[x-.135,low+.036],[x-.034,low+.036],[x-.034,low+.09],[x-.135,low+.09]],.022);
    tailFace('reverse',[[inside+.066,low+.04],[inside+.14,low+.04],[inside+.14,low+.092],[inside+.066,low+.092]],.022);
    if(d.id==='estate'){
      liftgate('tailBase',[[foot-.15,d.belt],[foot-.014,d.belt],[d.roofHalf-.016,top-.105],[d.roofHalf-.13,top-.105]],.045);
      liftgate('brake',[[foot-.122,d.belt+.06],[foot-.040,d.belt+.07],[d.roofHalf-.037,top-.155],[d.roofHalf-.092,top-.167]],.051);
    }
    if(d.id==='van'){
      const l=d.belt+.23,h=top-.15;
      liftgate('gasket',[[.075,l],[.81,l],[.86,l+.10],[.84,h-.04],[.76,h],[.075,h]],.016);
      liftgate('cargoGlass',[[.11,l+.04],[.77,l+.04],[.814,l+.12],[.805,h-.073],[.74,h-.04],[.11,h-.04]],.023);
    }
  }
  const value={fixed:fixed.finish(),doors:new Map([...doors].map(([s,p])=>[s,p.finish()])),section};
  cache.set(d,value);return value;
}

function attach(group,bundle,colors,offset=null){
  for(const [key,geo] of bundle){
    const mesh=new THREE.Mesh(geo,colors[key]);mesh.castShadow=key!=='glass';mesh.receiveShadow=true;
    if(offset)mesh.position.set(-offset.x,-offset.y,-offset.z);
    group.add(mesh);
  }
}

function beam(group,a,b,width,thickness,tint,part){
  along.set(b[0]-a[0],b[1]-a[1],b[2]-a[2]);
  const mesh=part(group,(a[0]+b[0])/2,(a[1]+b[1])/2,(a[2]+b[2])/2,width,along.length(),thickness,tint);
  mesh.quaternion.setFromUnitVectors(up,along.normalize());return mesh;
}

const wheelGeometry=new Map();
function tireGeometry(radius,width){
  const key=radius+':'+width;if(wheelGeometry.has(key))return wheelGeometry.get(key);
  const line=[[.58,-.50],[.86,-.50],[1,-.28],[1,.28],[.86,.50],[.58,.50]];
  const geo=new THREE.LatheGeometry(line.map(([r,x])=>new THREE.Vector2(radius*r,width*x)),12).toNonIndexed();
  geo.rotateZ(Math.PI/2);geo.computeVertexNormals();geo.userData.cityBatch=true;wheelGeometry.set(key,geo);return geo;
}
const wheelCylinders=new Map();
function disc(radius,depth,sides=12){
  const key=radius+':'+depth+':'+sides;if(wheelCylinders.has(key))return wheelCylinders.get(key);
  const geo=new THREE.CylinderGeometry(radius,radius,depth,sides);geo.rotateZ(Math.PI/2);
  geo.userData.cityBatch=true;wheelCylinders.set(key,geo);return geo;
}

const wheelFaces=new Map();
function hubFace(side,r,width,style){
  const key=[side,r,width,style].join('/');
  if(wheelFaces.has(key))return wheelFaces.get(key);
  const panels=panelSet(),normal=[side,0,0],face=side*width*.505;
  const point=(rad,angle,lip=0)=>[face+side*lip,r*rad*Math.cos(angle),r*rad*Math.sin(angle)];
  const steel=style==='police'||style==='van'||style==='service';
  const spokes=steel?7:style==='hatch'?3:style==='pickup'?6:5;
  const disk=Array.from({length:12},(_,i)=>point(.70,i*Math.PI/6));
  panels.polygon(steel?'steel':'inset',disk,normal);
  for(let i=0;i<12;i++){
    const a=i*Math.PI/6,b=(i+1)*Math.PI/6;
    panels.polygon('lip',[point(.69,a,.006),point(.69,b,.006),point(.59,b,.007),point(.59,a,.007)],normal);
  }
  for(let i=0;i<spokes;i++){
    const a=i*2*Math.PI/spokes,sweep=style==='estate'?.27:0;
    if(steel)panels.polygon('inset',[point(.39,a-.13,.006),point(.48,a-.12,.006),point(.51,a+.11,.006),point(.392,a+.17,.006)],normal);
    else{
      const spread=style==='hatch'?.34:style==='pickup'?.19:.12;
      panels.polygon('spoke',[point(.18,a-spread,.011),point(.60,a+sweep-spread,.011),
        point(.60,a+sweep+spread,.011),point(.18,a+spread,.011)],normal);
    }
  }
  panels.polygon('lip',Array.from({length:7},(_,i)=>point(steel?.22:.24,i*Math.PI*2/7,.014)),normal);
  const bundle=panels.finish();wheelFaces.set(key,bundle);return bundle;
}

export function buildRoadWheel(group,wheels,contacts,x,y,z,radius,width,steer,style,{material}){
  const side=Math.sign(x),pivot=new THREE.Group(),spin=new THREE.Group();
  pivot.position.set(x,y,z);group.add(pivot);pivot.add(spin);
  const tire=new THREE.Mesh(tireGeometry(radius,width),material('#252d30'));tire.castShadow=true;spin.add(tire);wheels.push(tire);
  attach(spin,hubFace(side,radius,width,style),{lip:material('#a1aeab'),inset:material('#3b4649'),
    steel:material('#7e8d88'),spoke:material(style==='hatch'?'#c7c8b1':'#a6b0ad')});
  contacts.push({x,y,z,radius,width,parts:[pivot],pivot,spin,steer});
}

export function buildCarShell(g,d,color,{part,material,glass,batch,head,brake,reverse,indicators}){
  const body=template(d),paint=bodyPaint(color),end=d.length/2,half=d.width/2,foot=half-.13,top=d.roof-.105;
  const roof=d.id==='hatch'?bodyPaint('#ecdfb9'):paint;
  const colors={paint,roof,rocker:material(d.id==='pickup'||d.id==='van'?'#35434a':'#4b5559'),glass,inside:material(interior),
    gasket:material(gasket),lamp:head,brake,reverse,left:indicators.left,right:indicators.right,
    tailBase:material('#6d343b'),cargoGlass:material('#506973')};
  attach(g,body.fixed,colors);
  const box=(x,y,z,w,h,l,c)=>part(g,x,y,z,w,h,l,c);
  const bar=(a,b,w,h,c)=>beam(g,a,b,w,h,c,part);
  box(0,d.id==='pickup'?.455:.37,0,d.width-.45,.32,d.length-.58,'#303b3f');
  const doors={},doorFront=d.windBase-.035,doorBack=d.pillar+.018;
  for(const s of [-1,1]){
    const door=new THREE.Group();door.position.set(s*foot,0,doorFront);g.add(door);doors[s]=door;
    attach(door,body.doors.get(s),colors,door.position);
    const doorPart=(x,y,z,w,h,l,c)=>part(door,x-door.position.x,y,z-doorFront,w,h,l,c);
    for(const [z0,z1] of [[d.windBase,d.windTop],[d.backBase,d.backTop]]){
      bar([s*foot,d.belt,z0],[s*d.roofHalf,top,z1],.095,.095,paint);
      bar([s*(foot-.047),d.belt+.012,z0],[s*(d.roofHalf-.047),top,z1],.042,.093,gasket);
    }
    bar([s*foot,d.belt,d.pillar-.024],[s*d.roofHalf,top,d.pillar-.061],.093,.105,gasket);
    bar([s*d.roofHalf,top+.01,d.backTop+.065],[s*d.roofHalf,top+.01,d.windTop-.06],.050,.056,gasket);
    const mid=(doorFront+doorBack)/2;
    doorPart(s*(foot+.012),d.belt+.012,mid,.051,.042,doorFront-doorBack,alloy);
    doorPart(s*(half-.016),d.belt-.154,doorBack+.23,.080,.065,.25,gasket);
    doorPart(s*(half+.021),d.belt-.143,doorBack+.24,.044,.035,.18,'#b2bab2');
    doorPart(s*(half-.19),d.belt-.17,mid,.084,.11,.39,'#39474a');
    if(d.id!=='van'&&d.id!=='pickup'){
      box(s*(half-.022),d.belt-.15,d.backBase*.79,.067,.052,.23,gasket);
      box(s*(foot+.006),d.belt+.009,(d.backBase+d.pillar)/2,.051,.045,d.pillar-d.backBase-.05,alloy);
    }
    const mirrorZ=d.windBase-.20;
    doorPart(s*(half-.01),d.belt+.122,mirrorZ,.20,.073,.13,gasket);
    doorPart(s*(half+.126),d.belt+.162,mirrorZ-.025,.25,.15,.24,d.id==='van'?gasket:paint);
    doorPart(s*(half+.127),d.belt+.165,mirrorZ-.153,.19,.10,.014,'#9ab5bb');
    if(d.id==='estate')bar([s*foot,d.belt,-1.52],[s*d.roofHalf,top,-1.59],.069,.092,gasket);
    batch(door);
  }
  for(const [s,z0,z1] of [[1,d.windBase,d.windTop],[-1,d.backBase,d.backTop]]){
    box(0,d.belt+.02,z0,foot*2-.02,.045,.080,gasket);
    box(0,top+.01,z1,d.roofHalf*2-.04,.040,.065,gasket);
    for(const x of s>0?[-.42,.46]:d.id==='estate'||d.id==='hatch'?[.05]:[]){
      bar([x-.25,d.belt+.046,z0+s*.031],[x+.27,d.belt+.112,z0+(z1-z0)*.07+s*.024],.034,.037,'#303c41');
    }
  }
  // Floor under the seats, seatbacks, door cards and instruments remain visible
  // when an occupant gets in or the sun catches the interior through the glass.
  const seatY=d.roof-2.26,seatZ=d.id==='van'?-.10:d.id==='pickup'?-.10:-.28;
  const seatX=d.id==='hatch'?-.43:-.48;
  box(0,.74,(d.windBase+d.backBase)/2,foot*1.98,.17,d.windBase-d.backBase,'#354144');
  const upholstery=d.id==='pickup'?'#766754':d.id==='hatch'?'#82695a':'#526368';
  function seat(x,z){
    box(x,1.21+seatY,z,.68,.17,.62,upholstery);
    box(x,1.50+seatY,z-.34,.66,.58,.17,upholstery).rotation.x=-.12;
    box(x,1.51+seatY,z-.222,.39,.36,.058,d.id==='hatch'?'#a18c6e':'#62757a').rotation.x=-.12;
    box(x,1.96+seatY,z-.40,.40,.21,.16,upholstery);
  }
  for(const s of [-1,1])seat(s*Math.abs(seatX),seatZ);
  if(d.id==='sedan'||d.id==='estate'||d.id==='hatch'){
    const rearZ=d.id==='hatch'?-.99:-1.11;
    box(0,1.19+seatY,rearZ,1.52,.19,.50,upholstery);
    box(0,1.48+seatY,rearZ-.23,1.52,.50,.15,upholstery).rotation.x=-.12;
    for(const s of [-1,1])box(s*.43,1.78+seatY,rearZ-.28,.33,.20,.14,upholstery);
  }
  const dashboardZ=d.windBase-.25;
  box(0,d.belt-.025,dashboardZ,foot*1.89,.22,.39,interior);
  box(seatX,d.belt+.106,dashboardZ-.065,.47,.097,.23,gasket);
  box(seatX,d.belt+.115,dashboardZ-.186,.29,.041,.012,'#91aca8');
  box(.16,d.belt-.018,dashboardZ-.21,.26,.125,.025,'#283b43');
  box(0,.94,seatZ+.19,.23,.22,.70,interior);box(0,1.09,seatZ+.44,.066,.16,.065,gasket);
  const seatMount={x:seatX,y:seatY-.018,z:seatZ};
  const wheelMount={x:seatX,y:1.61+seatY,z:seatZ+.90};
  const fascia=d.id==='pickup'?1.03:d.id==='van'?.98:d.id==='hatch'?.83:.88;
  // Bumpers have a narrower face and corners that turn back into the fenders.
  for(const s of [-1,1]){
    const z=s*end,w=body.section(z).width;
    box(0,.53,z,w*1.88,.19,.14,d.id==='pickup'||d.id==='van'?gasket:paint);
    box(0,.405,z,w*1.72,.091,.12,'#39464c');
    for(const edge of [-1,1]){
      const corner=box(edge*(w-.033),.55,z-s*.10,.23,.17,.32,d.id==='pickup'||d.id==='van'?gasket:paint);
      corner.rotation.y=edge*s*.43;
    }
    box(0,.555,z+s*.082,.51,.142,.018,'#e8e3c9');
    box(0,.608,z+s*.094,.48,.024,.009,'#6386a1');
  }
  const front=end+.018,back=-end-.021,grilleWidth=d.id==='pickup'?1.12:d.id==='hatch'?.73:1.03;
  if(d.id==='pickup'){
    box(0,.915,front,grilleWidth+.08,.44,.083,'#9ca69e');box(0,.91,front+.05,grilleWidth,.34,.043,gasket);
    for(const x of [-.32,0,.32])box(x,.91,front+.08,.067,.28,.02,'#87938e');
    box(0,.50,front+.10,.96,.15,.09,alloy);
  }else{
    box(0,fascia-.095,front,grilleWidth,.21,.055,gasket);
    box(0,fascia-.11,front+.033,grilleWidth-.095,.026,.024,'#778a8b');
    box(0,fascia+.012,front+.035,grilleWidth+.08,.04,.033,alloy);
    box(0,.54,front+.054,grilleWidth+.11,.12,.026,gasket);
    box(0,.50,front+.14,.47,.12,.022,'#dedeca');
  }
  for(const s of [-1,1]){
    const x=s*(half-(d.id==='pickup'?.41:.39)),outX=s*(half-d.endCut-.04);
    if(d.id==='hatch'){
      const lamp=(rad,depth,z,color)=>{
        const obj=new THREE.Mesh(disc(rad,depth,10),color);obj.rotation.y=Math.PI/2;obj.position.set(x,fascia,z);g.add(obj);
      };
      lamp(.207,.067,front,material(gasket));lamp(.15,.032,front+.045,head);
      box(outX,fascia-.14,front+.048,.115,.085,.032,s<0?indicators.left:indicators.right);
    }
  }
  box(0,.998,front+.035,.125,.073,.025,d.id==='pickup'?'#c9a75f':'#a7b8b6');
  const surfaces=[{w:d.width-.10,d:d.length,z:0,top:Math.min(d.hood,d.deck)-.06},
    {w:d.width-.22,d:end-d.windBase,z:(end+d.windBase)/2,top:d.hood},
    {w:foot*2,d:d.windBase-d.backBase,z:(d.windBase+d.backBase)/2,top:d.belt},
    {w:d.roofHalf*2-.08,d:d.windTop-d.backTop-.05,z:(d.backTop+d.windTop)/2,top:d.roof}];
  for(const [base,finish] of [[d.windBase,d.windTop],[d.backBase,d.backTop]]){
    const run=finish-base;
    if(Math.abs(run)<.36)continue;
    for(let i=0;i<3;i++){
      const t=(i+.5)/3;
      surfaces.push({w:2*mix(foot,d.roofHalf,t)-.10,d:Math.abs(run)/3,z:base+run*t,top:mix(d.belt,top,t),glazing:true});
    }
  }
  if(d.id==='pickup'){
    const rear=-end+.12,frontBed=d.backBase-.04,bedZ=(rear+frontBed)/2,bedLength=frontBed-rear,railTop=1.45;
    box(0,d.deck-.035,bedZ,d.width-.19,.11,bedLength,'#475251');
    for(let i=-2;i<=2;i++)box(i*.36,d.deck+.025,bedZ,.053,.036,bedLength-.13,'#64706b');
    for(const s of [-1,1]){
      box(s*(half-.190),(d.deck+railTop)/2,bedZ,.065,railTop-d.deck,bedLength,paint);
      box(s*(half-.114),railTop,bedZ,.20,.065,bedLength+.02,gasket);
      box(s*(half-.27),d.deck+.085,d.axles[0],.30,.20,.88,'#58635f');
      surfaces.push({x:s*(half-.112),w:.20,d:bedLength,z:bedZ,top:railTop+.032});
      bar([s*.95,railTop,frontBed-.35],[s*.93,1.98,frontBed-.12],.072,.072,alloy);
    }
    box(0,1.98,frontBed-.12,1.90,.075,.075,alloy);
    box(0,(d.deck+railTop)/2,rear,d.width-.24,railTop-d.deck,.095,paint);
    box(0,railTop,rear,d.width-.20,.069,.13,gasket);box(0,1.32,rear-.058,.34,.075,.027,gasket);
    surfaces.push({w:d.width-.42,d:bedLength,z:bedZ,top:d.deck+.043},{w:d.width-.20,d:.13,z:rear,top:railTop+.034});
  }else if(d.id==='estate'){
    for(const s of [-1,1]){
      for(const z of [d.backTop+.23,d.windTop-.24])box(s*.72,d.roof+.025,z,.12,.075,.19,gasket);
      box(s*.72,d.roof+.092,(d.backTop+d.windTop)/2,.081,.068,d.windTop-d.backTop-.29,alloy);
    }
    box(0,d.belt-.16,-end+.03,1.72,.075,.06,alloy);
  }else if(d.id==='van'){
    for(const s of [-1,1]){
      for(const y of [1.53,2.13])box(s*(foot-.036),y,-1.24,.045,.039,1.51,alloy);
      box(s*(foot+.013),1.40,-1.19,.055,.055,1.65,gasket);
      box(s*(half-.059),.75,-.91,.062,.12,2.50,gasket);
    }
    bar([0,d.belt,d.backBase-.035],[0,top,d.backTop-.035],.036,.034,gasket);
  }else if(d.id==='hatch'){
    box(0,d.roof+.009,d.backTop-.055,d.roofHalf*2+.09,.067,.23,roof);
    box(0,d.belt-.12,-end+.03,1.28,.08,.07,paint);
  }else{
    surfaces.push({w:d.width-.31,d:end+d.backBase,z:(-end+d.backBase)/2,top:d.deck});
    box(0,d.deck-.005,-end+.12,d.width-.59,.052,.17,paint);
  }
  return {doors,surfaces,seatMount,wheelMount,headlampY:fascia};
}
