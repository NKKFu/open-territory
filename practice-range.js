const cx=54,back=96.82,half=3.55;
const lanes=[-2.08,0,2.08],zoneZ=[105.1,110.5];
export function authorizedPractice(feet,hit,floor){
  return hit.kind==='cover'&&hit.cover?.rangeBackstop===true&&
    Math.abs(feet.x-cx)<half-.28&&feet.z>=zoneZ[0]&&feet.z<=zoneZ[1]&&Math.abs(feet.y-floor)<.65&&
    Math.abs(hit.point.z-(back+.41))<.035;
}
export function installPracticeRange({THREE:T,scene,block,groundHeight,solid,cameraObstacles,servicePoints,sign}){
  const base=groundHeight(cx,104),width=2*half,front=back+.41;
  const teal='#476862',trim='#a4ab8c',yellow='#dfb56d',navy='#384f52';
  function wall(x,z,w,d,h,color,backstop=false){
    block(x,base+h/2,z,w,h,d,color);
    const bounds=solid(x,z,w,d);Object.assign(bounds,{bottom:base,supportTop:base+h,opaque:true,rangeBackstop:backstop});
    cameraObstacles.push({x,z,w,d,bottom:base,top:base+h,rangeBackstop:backstop});
  }
  wall(cx,back,width,.82,4.14,teal,true);
  for(const side of [-1,1]){
    const x=cx+side*half;
    wall(x,102.94,.26,12.55,3.03,teal);
    block(x,base+3.08,102.94,.31,.12,12.6,trim);
    for(let dz=0;dz<6;dz++)block(x-side*.16,base+1.57,97.65+dz*2.04,.055,2.74,.12,navy);
    wall(x,110.47,.22,2.5,1.18,teal);
  }
  block(cx,base+4.18,back,width+.2,.16,.97,trim);
  for(const z of [105.14,110.46])block(cx,base+.012,z,width-.4,.022,.14,yellow);
  for(const dx of [-1.05,1.05])block(cx+dx,base+.012,107.8,.065,.022,5.2,yellow);
  sign('ESTANDE FAROL',cx,base+3.59,front+.02,4.1,{background:navy});
  // One canvas texture is shared, but each steel plate has its own hinge and lamp.
  const c=document.createElement('canvas');c.width=256;c.height=316;const g=c.getContext('2d');
  g.fillStyle='#eee2b9';g.fillRect(0,0,256,316);g.strokeStyle='#596d63';g.lineWidth=7;g.strokeRect(9,9,238,298);
  for(const [r,color] of [[93,'#4b6c68'],[71,'#ece0bd'],[50,'#557976'],[30,'#eadfba'],[17,'#c57853']]){
    g.fillStyle=color;g.beginPath();g.arc(128,150,r,0,Math.PI*2);g.fill();
  }
  const tex=new T.CanvasTexture(c);tex.colorSpace=T.SRGBColorSpace;
  const targets=lanes.map(dx=>{
    const root=new T.Group();root.position.set(cx+dx,base+2.67,front+.058);scene.add(root);
    const face=new T.Mesh(new T.PlaneGeometry(1.49,1.78),new T.MeshBasicMaterial({map:tex,toneMapped:false,side:T.DoubleSide}));
    face.position.y=-.89;root.add(face);
    const lamp=new T.Mesh(new T.BoxGeometry(.38,.10,.09),new T.MeshBasicMaterial({color:'#6b9683'}));
    lamp.position.set(cx+dx,base+2.88,front+.04);scene.add(lamp);
    block(cx+dx,base+2.70,front+.02,1.70,.11,.09,navy);
    return {root,lamp,dx,age:2,hits:0};
  });
  const card={kind:'range-card',x:cx,z:112.28,anchor:{x:cx,z:111.80,height:1.58},display:{x:cx,y:base+2.43,z:112.1}};
  // Mount the card on one side; leave the three bays and the entrance open.
  card.x=card.anchor.x=card.display.x=cx+2.49;
  wall(card.x,111.45,.94,.58,1.13,navy);
  block(card.x,base+1.17,111.45,1.08,.09,.65,trim);
  sign('SÉRIE',card.x,base+.83,111.76,.81,{background:teal});
  servicePoints.push(card);
  let rounds=0,hits=0;
  function impact(feet,hit){
    const currentFloor=groundHeight(cx,104);
    if(!authorizedPractice(feet,hit,currentFloor))return false;
    rounds++;
    const plate=targets.find(t=>Math.abs(hit.point.x-cx-t.dx)<.745&&hit.point.y-currentFloor>.89&&hit.point.y-currentFloor<2.67);
    if(plate){plate.age=0;plate.hits++;hits++;plate.lamp.material.color.set('#ffc35e');}
    return plate?'plate':'backstop';
  }
  function update(dt){
    for(const t of targets){if(t.age>=1.2)continue;t.age+=dt;
      t.root.rotation.x=-.28*Math.sin(t.age*24)*Math.exp(-t.age*4.5);
      if(t.age>=1.2){t.root.rotation.x=0;t.lamp.material.color.set('#6b9683');}
    }
  }
  return {impact,update,snapshot:()=>({rounds,hits,plates:targets.map(t=>t.hits)})};
}
