// Surveyed deck and ladder. The same dimensions drive dry footing, mooring and swimming.
export const harborPier={x:174,z:-169,w:5,d:17.6,top:.42};
export const harborLadder={x:176.68,z:-174,landX:175.42,landZ:-174,top:.42};

const cream='#f0ddb2',teal='#3c786f',navy='#2d5055',orange='#c97848',gold='#e6b65f';
const cargo={x:164,z:-154.5,w:7.5,d:3.16,h:2.78};
const loadingHeight=.20,travel=4.25;

export function createHarborLift(isOccupied){
  let position=0,destination=0,waiting=false;
  return {
    toggle(){destination=destination===0?1:0;waiting=false;return destination===1?'IÇANDO CARGA':'BAIXANDO CARGA';},
    update(dt){
      const step=Math.sign(destination-position)*Math.min(Math.abs(destination-position),dt*.34);
      waiting=Boolean(step&&isOccupied(cargo,loadingHeight+travel*Math.min(position,position+step),
        loadingHeight+travel*Math.max(position,position+step)+cargo.h));
      if(!waiting)position+=step;
    },
    get height(){return loadingHeight+travel*position;},
    get label(){return destination===0?'IÇAR CARGA':'BAIXAR CARGA';},
    snapshot:()=>({position,destination,waiting})
  };
}

function makeTriptych(T,scene,x,y,z){
  const sheet=document.createElement('canvas');sheet.width=768;sheet.height=3*304;
  const g=sheet.getContext('2d');
  const palette=[['#205362','#83c6bb','#f29168'],['#b9554d','#f0bb76','#244e58'],['#254658','#dfce8b','#6db3ac']];
  for(let k=0;k<3;k++){
    const [base,sea,accent]=palette[k];g.save();g.translate(0,k*304);
    g.fillStyle=base;g.fillRect(0,0,768,304);
    g.fillStyle=accent;g.beginPath();g.arc(k===1?136:595,89,k===1?63:89,0,2*Math.PI);g.fill();
    g.fillStyle=sea;g.beginPath();g.moveTo(0,190);
    for(let i=0;i<=12;i++)g.lineTo(i*64,170+Math.sin(i*1.6+k)*29);
    g.lineTo(768,304);g.lineTo(0,304);g.fill();
    g.fillStyle=cream;
    const shape=points=>{g.beginPath();points.forEach(([a,b],i)=>i?g.lineTo(a,b):g.moveTo(a,b));g.closePath();g.fill();};
    if(k===0){
      shape([[130,178],[236,104],[316,122],[377,83],[426,82],[456,105],[495,107],[452,121],[425,115],[374,139],[303,157],[240,147]]);
      shape([[287,140],[240,57],[274,60],[349,123]]);
      shape([[335,135],[395,45],[432,33],[393,111]]);
    }else if(k===1){
      shape([[220,171],[459,171],[420,207],[266,198]]);
      shape([[303,160],[348,36],[348,160]]);
      shape([[356,160],[356,59],[426,160]]);
    }else{
      g.strokeStyle=cream;g.lineWidth=13;g.beginPath();g.moveTo(57,154);
      const signal=[[124,154],[149,119],[169,179],[198,79],[226,201],[256,45],[286,192],[310,105],[334,154],[440,154]];
      for(const [a,b] of signal)g.lineTo(a,b);g.stroke();
    }
    g.fillStyle=base;for(let i=0;i<7;i++)g.fillRect(49+i*96,239+((i+k)%3)*10,45,4);
    g.fillStyle=cream;g.font='bold 19px system-ui';g.fillText(['GARÇA DA BAÍA','VELAS NO CANAL','ONDA CURTA'][k],23,29);
    g.restore();
  }
  const texture=new T.CanvasTexture(sheet);texture.colorSpace=T.SRGBColorSpace;
  const count=15,width=8.4,height=2.78,step=width/count,r=step/Math.sqrt(3);
  const geometry=new T.BufferGeometry(),coords=new Float32Array(count*3*6*3),uvs=[];
  const bary=[[0,1],[0,0],[1,1],[1,1],[0,0],[1,0]];
  for(let column=0;column<count;column++)for(let face=0;face<3;face++)for(const [u,v] of bary){
    uvs.push((column+.017+u*.966)/count,1-(face+.014+(1-v)*.972)/3);
  }
  const positions=new T.BufferAttribute(coords,3);positions.setUsage(T.DynamicDrawUsage);
  geometry.setAttribute('position',positions);geometry.setAttribute('uv',new T.Float32BufferAttribute(uvs,2));
  const mesh=new T.Mesh(geometry,new T.MeshBasicMaterial({map:texture,toneMapped:false,side:T.DoubleSide}));
  mesh.position.set(x,y,z);mesh.frustumCulled=false;scene.add(mesh);
  let rotation=0,settled=0,elapsed=2,dirty=true;
  function update(dt){
    elapsed+=dt;if(!dirty)return;
    let vertex=0;
    for(let column=0;column<count;column++){
      const t=Math.max(0,Math.min(1,(elapsed-column*.032)/.73)),mix=t*t*(3-2*t);
      const angle=(settled+(rotation-settled)*mix)*2*Math.PI/3;
      for(let face=0;face<3;face++)for(const [u,v] of bary){
        const a=angle-face*2*Math.PI/3+(u?Math.PI/3:-Math.PI/3);
        positions.setXYZ(vertex++,(column+.5)*step-width/2+Math.sin(a)*r,(v-.5)*height,Math.cos(a)*r);
      }
    }
    positions.needsUpdate=true;
    if(elapsed>=1.2){settled=rotation;dirty=false;}
  }
  update(0);
  return {update,advance(){if(dirty)return false;rotation++;elapsed=0;dirty=true;return true;},get scene(){return rotation%3;}};
}

export function installHarbor({THREE:T,scene,block,part,solid,sign,groundHeight,renderedGround,cameraObstacles,servicePoints,isOccupied}){
  function collider(x,z,w,d,bottom,top,opaque=true){
    const body=solid(x,z,w,d);Object.assign(body,{bottom,supportTop:top,opaque});
    const view={x,z,w,d,bottom,top};cameraObstacles.push(view);return {body,view};
  }
  function fitted(x,y,z,w,h,d,color,opaque=true){
    block(x,y,z,w,h,d,color);return collider(x,z,w,d,y-h/2,y+h/2,opaque);
  }
  function pad(x,z,w,d,top,color){
    block(x,top-.105,z,w,.21,d,color);
    renderedGround.addRectangle({x,z,w,d,top});
  }
  function beam(group,a,b,thickness,color){
    const start=new T.Vector3(...a),end=new T.Vector3(...b),delta=end.clone().sub(start);
    const mesh=part(group,(start.x+end.x)/2,(start.y+end.y)/2,(start.z+end.z)/2,thickness,delta.length(),thickness,color);
    mesh.quaternion.setFromUnitVectors(new T.Vector3(0,1,0),delta.normalize());return mesh;
  }
  function counter(kind,x,z,anchorX,anchorZ,height,label){
    const p={kind,x,z,anchor:{x:anchorX,z:anchorZ,height},display:{x:anchorX,y:groundHeight(x,z)+2.8,z:anchorZ},label};
    servicePoints.push(p);return p;
  }
  pad(162.6,-152.9,45.2,20.5,.16,'#b6b3a5');
  pad(132,-146,19,3.4,.13,'#c3af91');
  pad(157.3,-142.15,5.8,.46,-.075,'#bda98d');
  pad(157.3,-142.52,5.8,.47,.045,'#c5ac89');
  for(let x=143;x<185;x+=4.8)block(x,.164,-152.9,.035,.008,20,'#928e82');
  // Painted guide marks turn from the coastal promenade to the gangway.
  for(let x=143;x<=172;x+=2)block(x,.174,-145.2,.95,.014,.12,gold);
  for(let z=-147;z>=-161;z-=2)block(174,.174,z,.13,.014,.95,gold);
  const p=harborPier;
  pad(p.x,p.z,p.w,p.d,p.top,'#af8a61');
  pad(174,-160,4.96,.9,.25,'#ad8962');
  pad(174,-160.55,4.96,.5,.34,'#af8a61');
  for(let z=-161;z> -177.8;z-=.67)block(174,p.top+.004,z,4.93,.008,.023,'#7d6852');
  const pierLamps=[];
  for(const [px,pz] of [[171.78,-162],[176.23,-162],[171.78,-177.4],[176.23,-177.4]]){
    fitted(px,.20,pz,.28,1.58,.28,'#7a6d55',false);
    pierLamps.push(part(scene,px,1.02,pz,.31,.15,.31,new T.MeshStandardMaterial({color:cream,emissive:'#ffbd56',emissiveIntensity:.15})));
  }
  for(const z of [-174.46,-173.54]){
    block(176.5,.14,z,.10,1.5,.10,'#b4c5bb');
    block(176.65,.74,z,.39,.09,.105,'#b4c5bb');
  }
  for(const y of [-.50,-.18,.14,.46])block(176.52,y,-174,.11,.055,.91,'#b4c5bb');
  // A former refrigerated container houses the local radio. Its folding tin mural
  // is a single animated mesh with three illustrated faces on every slat.
  fitted(149.5,1.96,-158.15,11.6,3.6,5.9,'#376b6b');
  for(let x=144.0;x<155.3;x+=.52){
    block(x,1.99,-161.14,.12,3.37,.09,'#63958a');
    if(x<144.9||x>154.0)block(x,1.91,-155.15,.13,3.4,.11,'#67958a');
  }
  block(149.5,.43,-155.08,11.6,.12,.17,cream);
  block(149.5,3.67,-155.08,11.6,.12,.17,cream);
  const mural=makeTriptych(T,scene,149.5,2.03,-155.035);
  collider(149.5,-155.035,8.44,.65,.63,3.44);
  sign('RÁDIO ONDA CURTA',149.5,4.04,-155.06,5.0,{background:navy,ink:cream});
  const mastX=144.8;
  fitted(mastX,6.05,-159.7,.16,4.8,.16,navy,false);
  for(const [i,w] of [2.3,1.85,1.43,.92].entries())fitted(mastX,7+i*.37,-159.7,w,.085,.09,cream,false);
  fitted(145.1,.86,-152.8,1.9,1.40,1.14,navy,false);
  block(145.1,1.62,-152.8,1.96,.10,1.20,cream);
  const platter=new T.Group();platter.position.set(144.68,1.69,-152.80);scene.add(platter);
  const record=new T.Mesh(new T.CylinderGeometry(.36,.36,.045,12),new T.MeshStandardMaterial({color:navy,roughness:.87}));
  platter.add(record);part(platter,-.043,.027,.014,.17,.015,.15,gold);
  for(let i=0;i<4;i++)block(145.25+i*.17,1.71,-152.67,.095,.11,.15,i%2?gold:orange);
  block(144.99,1.77,-153.03,.30,.035,.046,'#617879',-.5);
  collider(145.1,-152.8,1.96,1.2,1.56,1.73,false);
  sign('FM 86.4',145.1,1.10,-152.219,1.41,{background:navy,border:false});
  // The portal legs, trolley and actual moving load form a working passage.
  const truss=new T.Group();truss.position.set(cargo.x,0,cargo.z);scene.add(truss);
  for(const side of [-1,1])for(const end of [-1,1]){
    const x=cargo.x+side*5.05,z=cargo.z+end*2.63;
    fitted(x,.43,z,.91,.55,.99,navy);
    fitted(x,4.39,z,.44,7.43,.52,orange);
    for(let n=0;n<3;n++)block(x,.99+n*.42,z,.46,.21,.54,n%2?navy:gold);
    beam(truss,[side*5.05,6.01,end*2.63],[side*3.45,7.94,end*2.63],.18,orange);
  }
  for(const end of [-1,1]){
    fitted(cargo.x,8.02,cargo.z+end*2.63,10.53,.42,.53,orange,false);
    block(cargo.x,8.64,cargo.z+end*2.63,10.4,.14,.22,gold);
    for(let i=0;i<8;i++)beam(truss,[-5+i*1.25,8.21,end*2.63],[-4.375+i*1.25,8.61,end*2.63],.078,cream);
  }
  for(const side of [-1,1])fitted(cargo.x+side*5.05,8.02,cargo.z,.49,.39,5.5,orange,false);
  block(cargo.x,8.24,cargo.z,1.25,.42,5.36,navy);
  block(cargo.x-3.73,7.55,cargo.z+2.64,1.48,1.08,.82,'#c9ad79');
  block(cargo.x-3.73,7.65,cargo.z+3.06,1.06,.51,.04,'#557778');
  sign('CAIS DAS ANTENAS',cargo.x,8.45,cargo.z+2.96,6.6,{background:navy,ink:cream});
  const crate=new T.Group();crate.position.set(cargo.x,loadingHeight,cargo.z);scene.add(crate);
  part(crate,0,cargo.h/2,0,cargo.w,cargo.h,cargo.d,'#ac5b42');
  for(const side of [-1,1]){
    for(let i=-3.42;i<3.6;i+=.42)part(crate,i,1.39,side*1.60,.095,2.54,.065,'#c47650');
    for(const y of [.12,2.64])part(crate,0,y,side*1.60,7.5,.12,.09,'#e2a363');
    for(const dx of [-3.69,3.69])part(crate,dx,1.41,side*1.61,.11,2.69,.08,cream);
  }
  const cargoSolid=collider(cargo.x,cargo.z,cargo.w+.02,cargo.d+.07,loadingHeight,loadingHeight+cargo.h);
  const lift=createHarborLift(isOccupied),cables=[];
  for(const x of [-2.94,2.94])for(const z of [-1.28,1.28]){
    const cable=part(scene,cargo.x+x,5.2,cargo.z+z,.047,4,.047,navy);cables.push(cable);
  }
  const panelX=159.0,panelZ=-149.95;
  fitted(panelX,.78,panelZ,.72,1.23,.65,navy);
  const statusMat=new T.MeshStandardMaterial({color:'#d6aa5e',emissive:'#f2a956',emissiveIntensity:.8});
  part(scene,panelX,1.46,panelZ,.58,.18,.50,statusMat);
  block(panelX,.89,panelZ+.333,.48,.33,.025,'#acc4a5');
  // Sailmaker's awning and a live weather station on the other side of the yard.
  for(const x of [177.6,183.1])for(const z of [-147.9,-152.2])fitted(x,1.89,z,.15,3.46,.15,navy);
  for(let i=0;i<8;i++)block(177.55+i*.79,3.66+Math.sin(i*Math.PI/7)*.37,-150, .81,.16,4.75,i%2?cream:'#679e91');
  collider(180.35,-150,6.35,4.75,3.56,4.12,false);
  fitted(180.35,.85,-151.15,3.45,1.38,1.25,'#92785b');
  for(let i=0;i<4;i++)block(179.45+i*.64,1.64,-151.22,.43,.24,.94,i%2?cream:'#b96e58');
  const sx=181.35,sz=-157.55;
  fitted(sx,2.78,sz,.22,5.22,.22,navy);
  const sock=new T.Group();sock.position.set(sx,5.35,sz);scene.add(sock);
  for(let i=0;i<6;i++)part(sock,0,0,.18+i*.29,.61-i*.060,.56-i*.059,.30,i%2?cream:orange);
  fitted(180.1,1.04,-156,1.49,1.77,.53,teal);
  sign('ESTAÇÃO DO CAIS',180.1,1.45,-155.72,1.41,{background:navy,border:false});
  counter('harbor-weather',180.1,-155.05,180.1,-155.64,2.12,'Consultar o tempo');
  // A service bench with a painted landing ring is the dock's alternate drone launch.
  fitted(155.35,.69,-146.75,2.0,1.06,1.25,teal);
  block(155.35,1.25,-146.75,2.08,.09,1.30,cream);
  for(const side of [-1,1]){
    block(155.35+side*.56,1.304,-146.75,.09,.011,.77,navy);
    block(155.35,1.304,-146.75+side*.37,1.21,.011,.08,navy);
  }
  sign('OFICINA DE VOO',155.35,.88,-146.108,1.68,{background:navy,border:false});
  counter('harbor-drone',155.35,-145.67,155.35,-146.08,1.87,'Pilotar drone');
  const network=[
    {label:'IÇAR CARGA',unit:{type:'harbor-crane',x:panelX,z:panelZ,antenna:{x:panelX,y:1.68,z:panelZ+.35}}},
    {label:'SINTONIZAR RÁDIO',unit:{type:'harbor-radio',x:145.1,z:-152.8,antenna:{x:145.1,y:1.81,z:-152.14}}}
  ];
  for(const {unit} of network)counter(unit.type,unit.x,unit.z+1.02,unit.antenna.x,unit.antenna.z,unit.antenna.y+.36,'');
  let serial=0,lastHeight=NaN;
  function hack(unit,play){
    if(unit.type==='harbor-radio'){
      if(!mural.advance())return 'TROCA EM ANDAMENTO';
      play('radio-'+mural.scene,unit.x,unit.z);serial++;return ['GARÇA DA BAÍA','VELAS NO CANAL','ONDA CURTA'][mural.scene];
    }
    const response=lift.toggle();network[0].label=lift.label;play('harbor-winch',unit.x,unit.z);return response;
  }
  function update(dt,time,weather){
    lift.update(dt);const y=lift.height,op=lift.snapshot();
    if(y!==lastHeight){
      crate.position.y=y;cargoSolid.body.bottom=cargoSolid.view.bottom=y;
      cargoSolid.body.supportTop=cargoSolid.view.top=y+cargo.h;cargoSolid.body.opaque=y<2.15;
      for(const c of cables){const bottom=y+cargo.h,top=8.09;c.position.y=(bottom+top)/2;c.scale.y=top-bottom;}
      lastHeight=y;
    }
    statusMat.emissiveIntensity=op.waiting?.35+(Math.sin(time*12)+1)*1.8:op.position!==op.destination?1.3:.46;
    statusMat.color.set(op.waiting?'#cb6c45':'#d6aa5e');
    mural.update(dt);
    if(serial)platter.rotation.y+=dt*3.4;
    const wind=Math.max(.25,weather.windSpeed),angle=Math.atan2(weather.windX,weather.windZ);
    sock.rotation.order='YXZ';sock.rotation.y+=Math.atan2(Math.sin(angle-sock.rotation.y),Math.cos(angle-sock.rotation.y))*(1-Math.exp(-3*dt));
    sock.rotation.x=.82/(1+wind*.32)+.07*Math.sin(time*(2+wind*.28));
    for(const lamp of pierLamps)lamp.material.emissiveIntensity=.25+weather.fog*1.3+.75*(.5+.5*Math.cos(time*1.45));
  }
  return {network,hack,update,snapshot:()=>({lift:lift.snapshot(),mural:mural.scene,broadcasts:serial,pier:{...harborPier}})};
}
