// Repeats purchases with the same E shown in the street, then reloads the city.
// The fish and hospital starting positions only move the QA player between counters.
import assert from 'node:assert/strict';
import {chromium} from 'playwright';
const base=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try{
  const ctx=await browser.newContext({viewport:{width:840,height:540}}),p=await ctx.newPage(),errors=[];
  p.on('pageerror',e=>errors.push(e.message));
  await p.route('**/game.js',async route=>{
    const response=await route.fetch();
    const probe=`\nwindow.counterQA={move(x,z,a=0){player.position.set(x,groundHeight(x,z),z);yaw=targetYaw=a;player.rotation.y=a+Math.PI;velocity.set(0,0,0);},project(x,z,h){const v=new THREE.Vector3(x,groundHeight(x,z)+h,z).project(camera);return [(v.x+1)*innerWidth/2,(1-v.y)*innerHeight/2];},injure(){combat.damage(playerActor,39);}};`;
    await route.fulfill({response,body:await response.text()+probe});
  });
  await p.clock.install({time:new Date('2026-01-01')});await p.clock.pauseAt(new Date('2026-01-01T00:00:02Z'));
  const state=()=>p.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
  const go=async scene=>{await p.goto(new URL('/?debug&near='+scene,base).href);await p.clock.runFor(144);return state();};
  const act=async()=>{await p.keyboard.press('e');await p.clock.runFor(64);return state();};
  const at=async(x,z,a)=>{await p.evaluate(args=>window.counterQA.move(...args),[x,z,a]);await p.clock.runFor(240);};
  assert.equal((await go('carabina')).interaction,'Comprar C-12 · R$ 360');
  assert.match((await act()).speech.line,/Faltam R\$ 40/);
  let s=await go('oficina');assert.equal(s.interaction,'Comprar P-9 · R$ 180');
  assert.equal(s.inside,'OFICINA FAROL');assert.ok(s.player.y>4);
  s=await act();assert.equal(s.economy.balance,140);assert.ok(s.combat.loadout.sidearm);
  assert.equal((await act()).economy.balance,116);
  s=await go('estande');const freeEye=s.camera;
  await p.keyboard.press('2');await p.keyboard.press('g');await p.clock.runFor(16);
  const firstEye=(await state()).camera;assert.ok(Math.hypot(firstEye.x-freeEye.x,firstEye.y-freeEye.y,firstEye.z-freeEye.z)<.55,'aim transition should ease in');
  await p.clock.runFor(650);
  const plate=await p.evaluate(()=>window.counterQA.project(54,97.28,1.78));
  const face=await p.evaluate(()=>window.counterQA.project(54,107.9,2.08));
  assert.ok(plate[0]-face[0]>45,'clear centre plate beside the player silhouette');
  await p.mouse.click(...plate);await p.clock.runFor(80);
  assert.deepEqual(await p.evaluate(()=>window.cityDebug.practice),{rounds:1,hits:1,plates:[0,1,0]});
  s=await state();assert.equal(s.police.phase,'livre');assert.equal(s.combat.loadout.sidearm.loaded,7);
  await p.keyboard.press('x');
  s=await go('recompra');assert.equal(s.interaction,'Vender P-9 · R$ 105');s=await act();
  assert.equal(s.economy.balance,221);assert.equal(s.combat.loadout.sidearm,undefined);
  assert.equal((await act()).economy.balance,221);
  s=await go('extrato');assert.equal(s.interaction,'Consultar saldo');assert.match((await act()).speech.line,/R\$ 221/);
  s=await go('expedicao');assert.equal(s.interaction,'Entregar 1 kg');
  assert.equal((await act()).economy.shipments,0);
  await at(157.4,-125.5,Math.PI);assert.equal((await state()).interaction,'Trocar pesos');await act();await act();
  await at(158.58,-122.72,Math.PI);s=await act();assert.equal(s.economy.balance,277);assert.equal(s.economy.shipments,1);
  assert.equal((await act()).economy.shipments,1);
  s=await go('atendimento');assert.equal(s.interaction,'Pedir atendimento');
  await p.evaluate(()=>window.counterQA.injure());await p.clock.runFor(64);assert.equal((await state()).combat.hp,61);
  s=await act();assert.equal(s.combat.hp,100);assert.equal(s.economy.balance,277);
  await p.goto(new URL('/?debug&near=oficina-frente',base).href);await p.clock.runFor(180);
  await p.keyboard.down('w');let inside=false;
  for(let n=0;n<36;n++){await p.clock.runFor(130);if((await state()).inside==='OFICINA FAROL'){inside=true;break;}}
  await p.keyboard.up('w');assert.ok(inside,'shop entrance opens when pushed on the terrace');
  assert.deepEqual(errors,[]);
  const mobile=await browser.newContext({viewport:{width:390,height:844},hasTouch:true,isMobile:true});
  const phone=await mobile.newPage();phone.on('pageerror',e=>errors.push(e.message));
  await phone.goto(new URL('/?debug&near=oficina',base).href);
  await phone.waitForFunction(()=>document.querySelector('#touchE')?.getAttribute('aria-label')?.startsWith('Comprar P-9'));
  await phone.locator('#touchE').tap();await phone.waitForFunction(()=>window.cityDebug?.economy.balance===140);
  assert.deepEqual(errors,[]);
  console.log('Oficina: purchase, restock, refusal, over-shoulder shot on live range, resale, save, bank, paid fish shipment, clinic, real door and mobile E passed.');
}finally{await browser.close();}
