// Painted wall panels fit between existing windows; no extra building or scene.
export function addStreetArt({THREE:T,scene,places}){
  const panels=[];
  for(const [kind,side,title] of [['cafe',-1,'MARÉ VIVA'],['gallery',1,'ARTE DA BAÍA'],['residence',-1,'NOSSO BAIRRO']]){
    const place=places.find(p=>p.kind===kind&&(kind!=='residence'||p.x>0));if(!place)continue;
    const canvas=document.createElement('canvas');canvas.width=512;canvas.height=256;const c=canvas.getContext('2d');
    c.fillStyle=kind==='gallery'?'#d47d57':'#278d98';c.fillRect(0,0,512,256);
    c.fillStyle='#f5d375';c.beginPath();for(let i=0;i<8;i++){const angle=i*Math.PI/4;c.lineTo(390+Math.cos(angle)*43,62+Math.sin(angle)*43);}c.fill();
    c.fillStyle='#6bb9b1';c.beginPath();c.moveTo(0,160);c.lineTo(100,58);c.lineTo(173,143);c.lineTo(246,80);c.lineTo(344,166);c.lineTo(512,124);c.lineTo(512,256);c.lineTo(0,256);c.fill();
    const colors=['#e7ad68','#f1d3a0','#e79376','#81b8ba'];
    for(let i=0;i<7;i++){
      const x=23+i*68,y=105+(i%3)*14;c.fillStyle=colors[i%4];c.fillRect(x,y,45,80);
      c.fillStyle='#356f78';for(let j=0;j<2;j++)for(let row=0;row<2;row++)c.fillRect(x+8+j*17,y+12+row*22,9,13);
    }
    c.fillStyle='#226b85';c.fillRect(0,182,512,74);
    c.strokeStyle='#9bd5cc';c.lineWidth=4;for(let i=0;i<4;i++){c.beginPath();c.moveTo(-20+i*152,191);c.lineTo(32+i*152,200);c.lineTo(95+i*152,192);c.stroke();}
    c.fillStyle='#fff1c9';c.font='bold 31px sans-serif';c.textAlign='center';c.fillText(title,256,239);
    const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
    const width=kind==='residence'?4.1:6.8,height=kind==='residence'?2.8:3.4;
    const panel=new T.Mesh(new T.PlaneGeometry(width,height),new T.MeshStandardMaterial({map:texture,roughness:.95}));
    panel.position.set(place.x+side*(place.w/2+.24),2.65,place.z);panel.rotation.y=side*Math.PI/2;panel.receiveShadow=true;scene.add(panel);panels.push(panel);
  }
  return panels;
}
