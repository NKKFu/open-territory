// Run with the local game: node tests/character-browser.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {chromium} from 'playwright';

const server=process.argv[2]||'http://127.0.0.1:8790';
const probe=`
window.characterFit=()=>{
  const v=new THREE.Vector3(),result={};
  if(vehicle?.driver){
    const car=vehicle,driver=car.driver,rim=car.steeringWheel.position,seat=driver.position;
    const grip=THREE.MathUtils.clamp(car.steeringWheel.rotation.z,-1.10,1.10);
    result.hands=driver.userData.grips.map(({hand,side})=>hand.position.distanceTo(v.set(rim.x-seat.x+side*.215*Math.cos(grip),rim.y-seat.y+side*.215*Math.sin(grip),rim.z-seat.z)));
    driver.updateWorldMatrix(true,true);let headTop=-Infinity;
    driver.userData.head.traverse(o=>{if(!o.isMesh)return;const p=o.geometry.attributes.position;for(let i=0;i<p.count;i++){v.fromBufferAttribute(p,i).applyMatrix4(o.matrixWorld);car.g.worldToLocal(v);headTop=Math.max(headTop,v.y);}});
    result.roofClearance=car.design?.roof-headTop;
  }
  if(vehicle?.kind==='bike')result.bike={shown:vehicle.rider.visible,phase:vehicle.pedalPhase,head:!!vehicle.rider.userData.head};
  result.seated=people.filter(p=>p.sitting).map(p=>{p.g.updateWorldMatrix(true,true);return p.g.userData.shoes.map(shoe=>new THREE.Box3().setFromObject(shoe).min.y-groundHeight(p.g.position.x,p.g.position.z));});
  return result;
};`;
const browser=await chromium.launch({args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:760,height:460}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/game.js',async route=>route.fulfill({contentType:'application/javascript',body:await readFile(new URL('../game.js',import.meta.url),'utf8')+probe}));
  await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});await page.clock.pauseAt(new Date('2026-11-06T12:00:01Z'));
  const state=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
  const start=async near=>{await page.goto(new URL('/?debug&near='+near,server).href);await page.clock.runFor(220);};
  let fit;
  if(process.argv[3]!=='seat'){
    await start('motores&modelo=sedan');await page.keyboard.press('e');await page.clock.runFor(1150);
    fit=await page.evaluate(()=>characterFit());
    assert.ok((await state()).car.driverShown);assert.ok(fit.hands.every(gap=>gap<.001));assert.ok(fit.roofClearance>.07);
    await page.keyboard.down('w');await page.keyboard.down('a');await page.clock.runFor(750);await page.keyboard.up('w');await page.keyboard.up('a');
    fit=await page.evaluate(()=>characterFit());assert.ok(fit.hands.every(gap=>gap<.001),'palms follow the rotating steering rim');
    console.log('Driver roof clearance:',fit.roofClearance.toFixed(3),'m; steering palm gap:',Math.max(...fit.hands).toFixed(4),'m.');
    await start('bike');await page.keyboard.press('e');await page.clock.runFor(450);
    const first=await page.evaluate(()=>characterFit());assert.ok(first.bike.shown&&first.bike.head);
    await page.keyboard.down('w');await page.clock.runFor(1200);await page.keyboard.up('w');fit=await page.evaluate(()=>characterFit());
    assert.ok((await state()).car.speed>.5);assert.ok(Math.abs(fit.bike.phase-first.bike.phase)>.2);
    await page.keyboard.down('Space');await page.clock.runFor(1200);await page.keyboard.up('Space');await page.keyboard.press('e');await page.clock.runFor(1100);assert.equal((await state()).car,null);
    console.log('Bicycle rider, pedalling and dismount passed.');
  }
  await start('seat');assert.equal((await state()).candidate,'bench');await page.keyboard.press('e');await page.clock.runFor(1000);assert.equal((await state()).sitting,true);
  fit=await page.evaluate(()=>characterFit());assert.ok(fit.seated.length>=8);assert.ok(fit.seated.flat().every(gap=>Math.abs(gap)<.045));
  await page.keyboard.press('e');await page.clock.runFor(1100);assert.equal((await state()).sitting,false);
  assert.deepEqual(errors,[]);console.log('Seated people and player sit/stand passed.');
}finally{await browser.close();}
