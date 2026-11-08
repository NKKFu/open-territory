// Start the game server, then run: node tests/interior-collisions.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const pot=(x,z)=>['vaso',x,z,.8,.8];
const shelf=(x,z)=>['estante',x,z,4,1.1];
const chair=(x,z)=>['cadeira',x,z,1.1,1.1];
const table=(x,z)=>['mesa',x,z,2.3,2.3];
const kitchen=(x,z)=>['balcão',x,z,4.2,1.2];
const both=(offset,make)=>[-offset,offset].flatMap(make);

// Independent footprints from the intended layout. A missing collider must still be tested.
const layouts={
  cafe:[['balcão',0,-5.8,14,2],['armário',-5,-8.7,2.4,1],pot(9,-7),
    ...both(6,x=>[table(x,2.5),chair(x,4.3),chair(x,.7)])],
  book:[...[-7,0,7].map(x=>shelf(x,-8)),shelf(-7,-1),shelf(7,-1),['caixa',-6,5,4,1.3],
    ['poltrona',4,4,2.4,1.5],pot(9,5.8)],
  gallery:[...both(5,x=>[['pedestal',x,-3,1.5,1.5]]),['banco',0,4,2.9,1.1],pot(8,6)],
  market:[...both(5,x=>[['gôndola',x,-2,2,8]]),...[-7,0,7].map(x=>shelf(x,-8)),['caixa',-6,6,4,1.3]],
  bakery:[['vitrine',0,-5.5,15,2],...both(6,x=>[table(x,3),chair(x,4.7)])],
  flower:[['bancada',0,-7,15,1.1],...[-3,-2,-1,0,1,2,3].map(i=>pot(i*2.1,-7)),
    ...both(7,x=>[['expositor',x,1,3,1.3]]),...[-8,-6,6,8].map(x=>pot(x,1))],
  home:[['cama',-5,-3.7,3.3,4],kitchen(5,-4.6),['sofá',5,1,3,1.5],pot(-7,3)],
  residence:[['recepção',-6,-4.6,3.5,1.2],['sofá',5,1.3,3,1.4],pot(-7,3)],
  restaurant:[['balcão',0,-5.5,10.8,1.6],...both(5,x=>[table(x,1.5),chair(x,3.2),chair(x,-.2)]),
    ['fogão',-4.5,-7,3.1,1.2],['geladeira',7.8,-5.8,1.5,1.3],['pia',1,-7.1,3.4,1.1],pot(-8,-5)],
  hospital:[['recepção',-6,-2,7,1.8],...both(5,x=>[['maca',x,-7,3,4],['suporte de soro',x+2,-7.8,.12,.12]]),
    ...[4,5.5,7].map(x=>['cadeira',x,5,1.2,1.2])],
  police:[['balcão',4,-4.8,7,1.6],...[0,1,2].map(i=>['cadeira',-7+i*1.6,4,1.25,1.2])],
  fire:[['balcão',4,-5,7,1.6],...[0,1,2].map(i=>['armário',-8+i*2.2,-8,1.8,1]),['painel',10.65,0,.25,2.2]],
  station:[['balcão',-5,-2.5,5,1.3],...[2,7].map(x=>['prateleira',x,-2.8,3,1.1])],
  armory:[...[-6.6,0,6.6].map(x=>['vitrine',x,-4.9,4.7,1.7]),['recompra',-6.45,4.8,6.9,1.55],['caixa',6.65,4.8,4,1.55]],
  fish:[['balcão',151,-120,4.5,1.15],['balança',160,-120.6,3.5,1.35],['corda',149,-125.7,1.05,1.05],
    ['pilha de caixotes',163,-117.2,1.25,.95],['peixes',162.6,-124,1.25,.95],['pesos',157.4,-124,1.25,.95],
    ['rede',157,-116.65,4,.04],...[155,159].map(x=>['poste da rede',x,-116.6,.13,.13]),
    ...[147.6,164.4].flatMap(x=>[-129.8,-115.8].map(z=>['pilar',x,z,.32,.32])),
    ['parede leste',164.4,-118.4,.3,5.2],['parede norte',156,-115.8,17,.3],
    ['placa',143.5,-118,.18,.18],['peixeiro',151,-117.9,.7,.65]]
};
const url=new URL('/?debug&near=poltrona',process.argv[2]||'http://127.0.0.1:8790');
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try {
  const page=await browser.newPage({viewport:{width:720,height:480}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/game.js',async route=>{
    const response=await route.fetch();
    // Only the browser used by this audit sees the probes and the starting positions.
    const probe = '\nwindow.interiorAudit={' +
      'sites:places.map(({kind,name,x,z,d})=>({kind,name,x,z,front:z+d/2})), blocked:collides,' +
      'vendorBlocks(x,z){const vendor=fishMarket.worker;return people.includes(vendor)&&!vendor.ragdoll&&' +
      'Math.hypot(x-vendor.g.position.x,z-vendor.g.position.z)<.8&&personBlocks(x,z,player,groundHeight(x,z));},' +
      'blockedStaff:people.filter(n=>n.place&&n.role&&!n.sitting&&collides(n.g.position.x,n.g.position.z,.55)).map(n=>[n.name,n.place.name]),' +
      'blockedBikes:cars.filter(c=>c.kind==="bike"&&vehicleFootprintBlocked(c,c.g.position.x,c.g.position.z)).map(c=>[c.g.position.x,c.g.position.z]),' +
      'start(x,z,yaw1){player.position.set(x,groundHeight(x,z),z);yaw=targetYaw=yaw1;velocity.set(0,0,0);' +
      'walkCamera.reset();cameraReady=false;lastManualLook=gameTime;},' +
      'state(){return {x:player.position.x,z:player.position.z,seated:!!sitting};}};' +
      `
      window.interiorAudit.shell = kind => {
        const p=places.find(site=>site.kind===kind);
        return {inside:currentPlace===p, depth:p.z+p.d/2-player.position.z, open:p.door.open,
          visible:p.facade.visible&&p.upper.visible&&p.door.hinge.visible&&p.outsideSign.visible};
      };
      window.interiorAudit.entranceGeometry = kind => {
        const p=places.find(site=>site.kind===kind),front=p.z+p.d/2,floor=groundHeight(p.x,p.z),ray=new THREE.Raycaster();
        // Sources that share an instanced draw are kept on a separate layer.
        ray.layers.enableAll();
        function hit(group,x,y,z,dx,dy,dz,far){
          group.updateWorldMatrix(true,true);
          ray.set(new THREE.Vector3(x,y,z),new THREE.Vector3(dx,dy,dz));ray.far=far;
          return ray.intersectObject(group,true).length>0;
        }
        return {
          open:[-1,1].every(side=>!hit(p.facade,p.x,floor+1.4,front+side*.8,0,0,-side,1.6)),
          wall:hit(p.facade,p.x+4,floor+.5,front-1,0,0,1,2),
          ceiling:hit(p.upper,p.x+.7,floor+1.3,p.z,0,1,0,4)
        };
      };`;
    await route.fulfill({response,body:(await response.text())+probe});
  });
  await page.clock.install({time:new Date('2026-01-01T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-01-01T12:00:02Z'));
  await page.goto(url.href);
  await page.waitForFunction(()=>!!window.interiorAudit);
  await page.clock.runFor(64);
  const sites=await page.evaluate(()=>window.interiorAudit.sites);
  assert.deepEqual(await page.evaluate(()=>window.interiorAudit.blockedStaff),[],'All staff must start on free floor');
  assert.deepEqual(await page.evaluate(()=>window.interiorAudit.blockedBikes),[],'Parked bikes must clear their stands');
  for(const site of sites.filter(p=>p.kind==='restaurant')){
    const open=await page.evaluate(({x,z})=>[-4.5,-5,-5.5,-6,-6.5]
      .every(dz=>!window.interiorAudit.blocked(x+6.25,z+dz,.55)),site);
    assert.ok(open,'Kitchen entrance must fit the character in '+site.name);
  }
  const items=[...sites,{kind:'fish',name:'Galpão da Maré',x:0,z:0}].flatMap(site=>{
    assert.ok(layouts[site.kind],'Missing audit layout for '+site.kind);
    return layouts[site.kind].map(([name,dx,dz,w,d])=>{
      assert.ok(w>0&&d>0,'Missing footprint dimensions for '+site.name+' / '+name);
      return {site:site.name,name,x:site.x+dx,z:site.z+dz,w,d};
    });
  });
  const leaks=await page.evaluate(items=>{
    const leaks=[];
    for(const {name,site,x,z,w,d} of items){
      const blocked=name==='peixeiro'?window.interiorAudit.vendorBlocks:window.interiorAudit.blocked;
      for(const sx of [-.47,0,.47])for(const sz of [-.47,0,.47]){
        if(!blocked(x+sx*w,z+sz*d,.42))leaks.push({site,name,x,z,sx,sz});
      }
    }
    return leaks;
  },items);
  assert.deepEqual(leaks,[]);
  const vendor=items.find(p=>p.name==='peixeiro');
  await page.evaluate(({x,z})=>window.interiorAudit.start(x-2,z,-Math.PI/2),vendor);
  await page.keyboard.down('w');await page.clock.runFor(850);await page.keyboard.up('w');
  const nearVendor=await page.evaluate(()=>window.interiorAudit.state());
  assert.ok(nearVendor.x>vendor.x-1&&nearVendor.x<=vendor.x-.79,'The standing vendor must block the player');
  const seat=items.find(p=>p.name==='poltrona');
  for(const [dx,dz] of [[-1,0],[1,0],[0,-1],[0,1]]){
    const edge=dx?seat.w/2:seat.d/2,dist=edge+1.45;
    await page.evaluate(([x,z,yaw])=>window.interiorAudit.start(x,z,yaw),[seat.x+dx*dist,seat.z+dz*dist,Math.atan2(dx,dz)]);
    await page.keyboard.down('w');
    await page.clock.runFor(700);
    const state=await page.evaluate(()=>window.interiorAudit.state());
    const clearance=(state.x-seat.x)*dx+(state.z-seat.z)*dz-edge;
    assert.ok(clearance>.45&&clearance<.7,'Character must stop at the visible edge');
    await page.keyboard.press('e');
    await page.clock.runFor(720);
    assert.equal(await page.evaluate(()=>window.interiorAudit.state().seated),dz===1);
    await page.keyboard.up('w');
    if(dz===1){
      await page.keyboard.press('e');
      await page.clock.runFor(500);
      const exit=await page.evaluate(()=>window.interiorAudit.state());
      assert.ok(!exit.seated&&exit.z>seat.z+seat.d/2+.5,'Stand up on clear floor at the front');
    }
  }
  // A free jump from the floor cannot lift the feet above the cushion.
  await page.keyboard.down('Shift');await page.keyboard.down('w');
  await page.clock.runFor(320);await page.keyboard.press('Space');
  const readPlayer=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city).player);
  let peak=0;
  for(let i=0;i<14;i++){
    await page.clock.runFor(45);
    const player=await readPlayer();peak=Math.max(peak,player.y-player.ground);
    assert.ok(player.z>seat.z+seat.d/2+.45,'Jumping must not push the body inside the armchair');
  }
  assert.ok(peak>.20&&peak<.48);
  await page.keyboard.up('w');await page.keyboard.up('Shift');
  await page.keyboard.press('r');await page.clock.runFor(160);assert.ok((await readPlayer()).ragdoll);
  await page.clock.runFor(3800);
  const recovered=await readPlayer();
  assert.ok(!recovered.ragdoll);
  assert.ok(await page.evaluate(p=>!window.interiorAudit.blocked(p.x,p.z,.45),recovered),'Recover on free floor');
  for(const kind of ['home','book','hospital','armory']){
    const {name,x,front}=sites.find(site=>site.kind===kind);
    await page.evaluate(([x,z])=>window.interiorAudit.start(x,z,0),[x,front+3.6]);
    await page.clock.runFor(250);
    for(const [key,inside] of [['w',true],['s',false]]){
      await page.keyboard.down(key);
      let reached=false;
      for(let step=0;step<44;step++){
        await page.clock.runFor(80);
        const shell=await page.evaluate(k=>window.interiorAudit.shell(k),kind);
        assert.ok(shell.visible,name+' must keep the facade, roof, sign and door at the threshold');
        if(shell.inside===inside&&(inside?shell.depth>2.5:shell.depth< -1.3)){
          reached=true;
          if(inside)assert.ok(shell.open,name+' door must open for the player');
          break;
        }
      }
      await page.keyboard.up(key);
      assert.ok(reached,name+' must allow walking '+(inside?'inside':'outside')+' through its door');
      if(inside)assert.deepEqual(await page.evaluate(k=>window.interiorAudit.entranceGeometry(k),kind),
        {open:true,wall:true,ceiling:true},name+' must enclose the room and leave the door opening clear');
    }
  }
  assert.deepEqual(errors,[]);
  console.log(items.length+' assemblies checked in '+sites.length+' interiors and the fish shed. Walking, sitting, jumping, ragdoll recovery and four building entrances passed.');
} finally {
  await browser.close();
}
