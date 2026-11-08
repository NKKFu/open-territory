// The west gable of Galeria Sol doubles as a small neighbourhood cinema.
export function installStreetCinema({THREE:T,scene,block,part,solid,bench,sign,renderedGround,cameraObstacles,servicePoints}){
  const ink='#243c48',cream='#e8d7aa',brick='#bd795d',mint='#75b1a6';
  const x=-47.96,z=-36.9,projector={x:-57.43,z:-36.9};
  block(-54.7,.06,z,12.8,.20,18.4,'#c4bb9e');
  renderedGround.addRectangle({x:-54.7,z,w:12.8,d:18.4,top:.16});
  for(const end of [-1,1])block(-54.6,.166,z+end*8.67,12.4,.008,.20,'#a28876');
  function object(px,py,pz,w,h,d,color,opaque=true){
    block(px,py,pz,w,h,d,color);
    const s=solid(px,pz,w,d);Object.assign(s,{bottom:py-h/2,supportTop:py+h/2,opaque});
    cameraObstacles.push({x:px,z:pz,w,d,bottom:s.bottom,top:s.supportTop});
  }
  object(x,2.74,z,.23,3.83,8.94,ink);
  for(const edge of [-1,1])block(x-.12,2.74,z+edge*4.46,.19,4.11,.17,brick);
  sign('CINE DA PASSAGEM',x-.17,5.00,z,5.65,{background:'#704b49',ink:cream,rotation:-Math.PI/2});
  const paper=document.createElement('canvas');paper.width=480;paper.height=208;
  const ctx=paper.getContext('2d'),texture=new T.CanvasTexture(paper);texture.colorSpace=T.SRGBColorSpace;
  const screen=new T.Mesh(new T.PlaneGeometry(8.57,3.52),new T.MeshBasicMaterial({map:texture,toneMapped:false}));
  screen.position.set(x-.14,2.75,z);screen.rotation.y=-Math.PI/2;scene.add(screen);
  bench(-53.56,z-2.61,Math.PI/2);bench(-53.56,z+2.61,Math.PI/2);
  object(projector.x,.69,z,1.22,1.06,1.14,'#8d7860',false);
  object(projector.x,1.56,z,1,.63,.61,ink,false);
  block(projector.x+.61,1.60,z,.31,.28,.28,'#475957');
  const light=new T.MeshBasicMaterial({color:'#b3b39b'});
  part(scene,projector.x+.78,1.60,z,.035,.18,.18,light);
  const spools=[];
  for(const off of [-.27,.27]){
    const wheel=new T.Group();wheel.position.set(projector.x+off,1.99,z+.08);scene.add(wheel);
    for(let n=0;n<3;n++)part(wheel,0,0,0,.085,.46,.09,'#ad8870').rotation.z=n*Math.PI/3;
    spools.push(wheel);
  }
  const projectorReels=solid(projector.x,z+.08,.98,.13);
  Object.assign(projectorReels,{bottom:1.77,supportTop:2.22});
  cameraObstacles.push({x:projector.x,z:z+.08,w:.98,d:.13,bottom:1.77,top:2.22});
  const unit={type:'cinema',...projector,antenna:{x:projector.x,y:2.25,z:z+.25}};
  const device={label:'LIGAR PROJETOR',unit};
  servicePoints.push({kind:'cinema',x:projector.x,z:z+1.0,anchor:{x:projector.x,z:z+.61,height:2.36},
    display:{x:projector.x,y:2.92,z:z+.64}});
  let playing=false,age=0,frame=-1,sessions=0;
  function draw(t,rain){
    const w=paper.width,h=paper.height;
    if(!playing){
      ctx.fillStyle='#e2ca9c';ctx.fillRect(0,0,w,h);ctx.fillStyle='#b97c65';ctx.fillRect(0,0,17,h);
      ctx.fillStyle='#303f45';ctx.font='bold 40px system-ui';ctx.fillText('A TRAVESSIA',35,100);
      ctx.font='18px system-ui';ctx.fillText('Curta da Rádio Onda Curta',38,136);
      texture.needsUpdate=true;return;
    }
    const phase=t%22/22,sun=Math.sin(phase*Math.PI);
    ctx.fillStyle=rain>.35?'#a7b7b6':sun>.63?'#edbe85':'#cca69c';ctx.fillRect(0,0,w,h);
    ctx.fillStyle='#e9d7a1';ctx.beginPath();ctx.arc(380-phase*50,70-sun*35,28,0,Math.PI*2);ctx.fill();
    ctx.fillStyle='#678181';ctx.beginPath();ctx.moveTo(0,111);for(let i=0;i<=14;i++)ctx.lineTo(i*35,82+Math.sin(i*.79)*21);
    ctx.lineTo(w,144);ctx.lineTo(0,144);ctx.fill();
    ctx.fillStyle='#36565c';ctx.fillRect(0,120,w,90);ctx.fillStyle=mint;
    for(let i=0;i<5;i++)for(let j=0;j<12;j++)ctx.fillRect(((j*51+i*13+t*(5+i))%525)-40,132+i*13+4*Math.sin(j+t),16+i*2,2);
    const boatX=-43+phase*570,boatY=127+Math.sin(t*2.5)*2;
    ctx.save();ctx.translate(boatX,boatY);ctx.fillStyle=cream;ctx.fillRect(-13,-16,26,13);
    ctx.fillStyle=brick;ctx.beginPath();ctx.moveTo(-30,-4);ctx.lineTo(32,-4);ctx.lineTo(20,7);ctx.lineTo(-20,7);ctx.fill();
    ctx.fillStyle=ink;ctx.fillRect(-2,-30,2,19);ctx.fillRect(0,-30,11,4);ctx.restore();
    ctx.fillStyle='#c7856b';ctx.fillRect(51,71,15,49);ctx.fillStyle=ink;ctx.fillRect(48,67,21,6);ctx.fillRect(51,57,15,11);
    ctx.fillStyle=cream;ctx.fillRect(54,61,9,5);
    if(rain>.35){ctx.strokeStyle='#dae4d8';ctx.beginPath();for(let i=0;i<23;i++){
      const rx=(i*101+t*42)%w,ry=(i*39+t*105)%h;ctx.moveTo(rx,ry);ctx.lineTo(rx-2,ry+7);
    }ctx.stroke();}
    texture.needsUpdate=true;
  }
  draw(0,0);
  function toggle(play){
    playing=!playing;age=0;frame=-1;if(playing)sessions++;
    device.label=playing?'DESLIGAR PROJETOR':'LIGAR PROJETOR';
    light.color.set(playing?'#ffdc8a':'#b3b39b');draw(age,0);play('film-reel',projector.x,z);
    return playing?'SESSÃO INICIADA':'SESSÃO ENCERRADA';
  }
  function update(dt,viewer,rain){
    if(!playing)return;
    age+=dt;
    if(Math.hypot(viewer.x-x,viewer.z-z)>65)return;
    spools.forEach(s=>s.rotation.z=age*3.8);
    const index=Math.floor(age*8);if(index!==frame){frame=index;draw(age,rain);}
  }
  return {device,toggle,update,get playing(){return playing;},snapshot:()=>({playing,sessions,seconds:Math.floor(age)})};
}
