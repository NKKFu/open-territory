// Painted wall panels fit between existing windows; no extra building or scene.
export function addStreetArt({THREE:T,scene,places}){
  const panels=[];
  for(const [kind,side,title] of [['cafe',-1,'MARÉ VIVA'],['gallery',1,'ARTE DA BAÍA'],['residence',-1,'NOSSO BAIRRO']]){
    const place=places.find(p=>p.kind===kind&&(kind!=='residence'||p.x>0));if(!place)continue;
    const canvas=document.createElement('canvas');canvas.width=512;canvas.height=256;const c=canvas.getContext('2d');
    c.fillStyle=kind==='gallery'?'#c67657':'#278d98';c.fillRect(0,0,512,256);
    function polygon(ink,points){
      c.fillStyle=ink;c.beginPath();c.moveTo(...points[0]);for(const p of points.slice(1))c.lineTo(...p);c.closePath();c.fill();
    }
    c.fillStyle='#f5d375';c.beginPath();for(let i=0;i<8;i++){const angle=i*Math.PI/4;c.lineTo(390+Math.cos(angle)*43,62+Math.sin(angle)*43);}c.fill();
    c.fillStyle='#6bb9b1';c.beginPath();c.moveTo(0,160);c.lineTo(100,58);c.lineTo(173,143);c.lineTo(246,80);c.lineTo(344,166);c.lineTo(512,124);c.lineTo(512,256);c.lineTo(0,256);c.fill();
    c.fillStyle='#226b85';c.fillRect(0,182,512,74);
    if(kind==='cafe'){
      polygon('#17586c',[[0,123],[68,102],[128,130],[174,126],[218,82],[252,68],[272,72],[248,91],[232,121],[250,155],[320,176],[380,164],[447,135],[512,128],[512,208],[0,208]]);
      polygon('#9bd7bf',[[35,112],[69,102],[126,127],[174,124],[218,82],[253,66],[276,74],[254,79],[234,98],[225,120],[207,127],[191,146],[153,150],[107,133]]);
      polygon('#f1cea1',[[321,142],[355,92],[355,144]]);polygon('#e8a269',[[361,87],[390,143],[361,143]]);
      polygon('#783f3a',[[311,150],[403,149],[385,161],[327,162]]);
    }else if(kind==='gallery'){
      // A white heron taking off over the mangrove; two faceted wings.
      polygon('#386667',[[24,183],[83,103],[158,131],[181,186]]);
      polygon('#d7e3cc',[[154,143],[110,33],[156,58],[220,121],[248,140]]);
      polygon('#fff0cc',[[147,156],[77,91],[151,107],[211,141],[249,130],[284,94],[284,64],[301,49],[334,53],[351,65],[308,66],[306,103],[272,151],[231,168],[182,175]]);
      polygon('#dba24d',[[327,56],[381,73],[342,70],[311,64]]);
      polygon('#587f7c',[[149,157],[203,146],[233,156],[195,167]]);
      polygon('#663e37',[[170,170],[152,196],[145,196],[159,168]]);
      c.fillStyle='#294b4d';c.fillRect(313,54,6,5);
    }else{
      const colors=['#e7ad68','#f1d3a0','#e79376','#81b8ba'];
      for(let i=0;i<7;i++){
        const x=23+i*68,y=105+(i%3)*14;c.fillStyle=colors[i%4];c.fillRect(x,y,45,182-y);
        polygon('#91594c',[[x-5,y],[x+22,y-18],[x+50,y]]);
        c.fillStyle='#356f78';for(let j=0;j<2;j++)for(let row=0;row<2;row++)c.fillRect(x+8+j*17,y+10+row*20,9,12);
      }
    }
    c.strokeStyle='#9bd5cc';c.lineWidth=4;for(let i=0;i<4;i++){c.beginPath();c.moveTo(-20+i*152,191);c.lineTo(32+i*152,200);c.lineTo(95+i*152,192);c.stroke();}
    c.fillStyle='#fff1c9';c.font='bold 31px sans-serif';c.textAlign='center';c.fillText(title,256,239);
    const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
    const width=kind==='residence'?4.1:6.8,height=kind==='residence'?2.8:3.4;
    const panel=new T.Mesh(new T.PlaneGeometry(width,height),new T.MeshStandardMaterial({map:texture,roughness:.95}));
    panel.position.set(place.x+side*(place.w/2+.24),2.65,place.z);panel.rotation.y=side*Math.PI/2;panel.receiveShadow=true;scene.add(panel);panels.push(panel);
  }
  return panels;
}
