// Street-facing details on the residential blocks. Everything fits inside
// the paved building apron; balconies are above the sidewalk headroom.
export function residentialFacade({block,sign},x,z,w,d,height) {
  const back=z-d/2,balconies=x<0,tower=x===36,cream='#eee1c2';
  const trim=balconies?'#4b7d78':tower?'#be7562':'#788999';
  const bays=[-w*.32,0,w*.32];
  block(x,1.45,back-.09,w,2.40,.22,balconies?'#b5b59c':'#d9c7ac');
  for(let level=0;4.5+level*3.8<height;level++){
    const y=4.5+level*3.8;
    for(const [i,offset] of bays.entries()){
      const cx=x+offset;
      block(cx,y,back-.15,3.45,2.3,.24,cream);
      block(cx,y+.02,back-.30,3.15,2.07,.08,'#466d73');
      block(cx,y,back-.37,.10,2.18,.08,cream);
      block(cx,y+.53,back-.37,3.25,.085,.08,cream);
      if(balconies && (i+level)%2===0){
        block(cx,y-1.08,back-.65,4.14,.21,1.42,cream);
        block(cx,y-.24,back-1.26,4.06,.11,.12,trim);
        for(const side of [-1,1])block(cx+side*1.96,y-.24,back-.7,.11,.11,1.15,trim);
        for(let j=-3;j<=3;j++)block(cx+j*.62,y-.67,back-1.26,.075,.82,.075,trim);
        block(cx+1.12,y-.79,back-.69,.62,.35,.52,'#b76e52');
        block(cx+1.12,y-.44,back-.69,.73,.46,.62,'#5e8766');
      }else{
        for(const side of [-1,1]){
          block(cx+side*2.0,y,back-.22,.64,2.27,.14,trim);
          for(const dy of [-.68,0,.68])block(cx+side*2.0,y+dy,back-.31,.56,.08,.06,cream);
        }
        block(cx,y-1.20,back-.26,3.78,.13,.60,cream);
      }
    }
  }
  // The central bay changes the silhouette: a stair tower or a stepped parapet.
  if(tower){
    block(x+w*.39,height*.55,back+.82,2.22,height*1.1,2.18,trim);
    for(let i=-1;i<=1;i++)block(x+w*.39+i*.47,height*.64,back-.32,.19,height*.76,.20,cream);
    block(x+w*.39,height*1.1+.18,back+.82,2.60,.38,2.48,cream);
  }else{
    block(x,height+.67,back+.10,w*.52,.92,.72,trim);
    block(x,height+1.18,back+.10,w*.38,.42,.70,cream);
  }
  block(x,1.34,back-.24,2.45,2.66,.21,'#46635f');
  block(x,1.44,back-.38,.10,2.40,.07,cream);
  block(x,2.88,back-.50,4.2,.19,1.0,trim);
  sign(balconies?'VILA IPÊ':tower?'EDIFÍCIO MARÉ':'CASA DO PORTO',x,2.96,back-1.02,3.0,{rotation:Math.PI,background:trim,border:false});
}

export function lighthouse({THREE:T,scene,block,part,solid,cameraObstacles,fixtures}) {
  const x=-32,z=-158,base=-.19,g=new T.Group();
  g.position.set(x,base,z);scene.add(g);fixtures.push(g);
  block(x,base+.15,z,5.2,.30,5.2,'#b9b5a1');
  solid(x,z,5.2,5.2).opaque=true;
  const cream='#eee1bd',coral='#bd6854',iron='#3f605f';
  const bands=[[.30,2.6,4.4,cream],[2.9,2.15,4.05,coral],[5.05,2.55,3.68,cream],[7.60,2.12,3.35,coral],[9.72,2.15,3.03,cream]];
  for(const [bottom,h,width,color] of bands){
    part(g,0,bottom+h/2,0,width,h,width,color);
    part(g,0,bottom+.08,0,width+.13,.16,width+.13,cream);
  }
  part(g,0,1.52,2.22,1.22,2.28,.12,iron);
  for(const [y,depth] of [[4,2.05],[6.7,1.85],[10.65,1.54]]){
    part(g,0,y,depth,.63,.93,.11,iron);part(g,0,y-.03,depth+.07,.43,.65,.065,'#8cbab4');
  }
  part(g,0,12,0,4.42,.30,4.42,iron);
  for(const side of [-1,1]){
    part(g,0,12.97,side*2.08,4.26,.09,.09,iron);
    part(g,side*2.08,12.97,0,.09,.09,4.26,iron);
    for(const n of [-1,0,1]){
      part(g,n*1.92,12.56,side*2.08,.075,.87,.075,iron);
      part(g,side*2.08,12.56,n*1.92,.075,.87,.075,iron);
    }
    for(const zz of [-1,1])part(g,side*1.12,13.16,zz*1.12,.12,2.08,.12,iron);
  }
  const lamp=new T.MeshStandardMaterial({color:'#ffe6a5',emissive:'#ffb950',emissiveIntensity:.35});
  part(g,0,13.24,0,1.12,1.11,1.12,lamp);
  part(g,0,14.27,0,2.84,.22,2.84,iron);
  part(g,0,14.55,0,2.39,.37,2.39,coral);part(g,0,14.91,0,1.49,.37,1.49,coral);
  part(g,0,15.38,0,.12,.79,.12,iron);
  cameraObstacles.push({x,z,w:5.2,d:5.2,bottom:base,top:base+15.8});
  return lamp;
}

export const beachDecks=[-105,-75,-45,-15,15,45,75,105];
export function seasideKiosks({block,solid,sign,cameraObstacles}){
  beachDecks.forEach((x,index)=>{
    const z=-160,deck=-.01,kind=index%3;
    const paint=['#d77f62','#669790','#548697'][kind],cream='#f0deb9';
    block(x,-.10,z,7,.18,5,'#dcbf94');
    for(let n=-3;n<=3;n++)block(x+n,deck+.004,z,.027,.008,4.91,'#b48b66');
    if(kind===0){
      block(x,1.47,z,.15,2.96,.15,'#ae865c');solid(x,z,.15,.15);
      for(let i=-2;i<=2;i++)block(x+i*.84,2.96-Math.abs(i)*.12,z,.84,.18,4.3-Math.abs(i)*.3,i%2?cream:paint);
      for(const side of [-1,1]){
        const cx=x+side*1.81;
        block(cx,.36,z+.20,1.08,.18,2.65,cream);
        block(cx,.71,z-.77,1.08,.72,.22,paint);
        for(const dz of [-.65,1.05])block(cx,.14,z+dz,.88,.28,.18,'#ad8a63');
        solid(cx,z+.20,1.08,2.65);
      }
    }else{
      for(const dx of [-2.46,2.46])for(const dz of [-1.65,1.65]){
        block(x+dx,1.43,z+dz,.16,2.86,.16,'#9c7856');solid(x+dx,z+dz,.16,.16);
      }
      for(let i=0;i<8;i++)block(x+(i-3.5)*.69,2.94,z,.69,.21,3.75,i%2?paint:cream);
      if(kind===1){
        block(x,.57,z+.30,3.91,1.12,1.05,'#83aaa0');
        block(x,1.18,z+.30,4.12,.15,1.15,cream);solid(x,z+.30,4.12,1.15);
        for(const dx of [-1.35,-.54,.31])block(x+dx,1.49,z+.28,.42,.48,.43,'#c5dabb');
        sign('ÁGUA DE COCO',x,2.53,z+1.70,3.5,{background:'#467d70',border:false});
      }else{
        block(x,.68,z-.67,3.58,.17,.54,'#ad865d');solid(x,z-.67,3.58,.54);
        for(let i=-1;i<=1;i++){
          const cx=x+i*1.13,color=['#d29363','#6daba9','#d6bb68'][i+1];
          block(cx,1.20,z-.67,.56,1.91,.16,color);block(cx,2.20,z-.67,.40,.25,.16,color);
          block(cx,2.38,z-.67,.22,.13,.16,color);block(cx,1.22,z-.57,.09,1.81,.035,cream);
        }
        sign('PRANCHAS',x,2.53,z+1.70,3.2,{background:'#476e83',border:false});
      }
    }
    cameraObstacles.push({x,z,w:kind===0?4.35:5.55,d:kind===0?4.35:3.85,bottom:2.59,top:3.08});
  });
}
