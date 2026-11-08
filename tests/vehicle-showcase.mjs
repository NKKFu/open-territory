// With the game server running: node tests/vehicle-showcase.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const server=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:700,height:420}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-11-06T12:00:01Z'));
  const state=()=>page.evaluate(()=>({fleet:window.cityDebug.garage,world:JSON.parse(document.documentElement.dataset.city)}));
  const start=async query=>{await page.goto(new URL('/?debug&near='+query,server).href);await page.clock.runFor(600);};
  await start('motores');
  const {fleet,world}=await state();assert.equal(fleet.display.length,5);
  for(const display of fleet.display){
    assert.ok(fleet.street.includes(display.model),'the model should also appear in normal traffic');
    const car=world.grounding.find(c=>Math.hypot(c.x-display.x,c.z-display.z)<.1);
    assert.ok(car&&Math.abs(car.clearance)<.035,'exhibit tires should rest on the lot');
    await start('motores&modelo='+display.id);
    assert.equal((await state()).world.candidate,'car');
    await page.keyboard.press('e');await page.clock.runFor(1130);
    const inside=await state();assert.equal(inside.fleet.controlled,display.id);assert.equal(inside.world.car.driverShown,true);
    await page.keyboard.press('e');await page.clock.runFor(1260);
    assert.equal((await state()).world.car,null);
  }
  assert.deepEqual(errors,[]);console.log('All five car bodies circulate, rest on their wheels and can be entered and exited.');
}finally{await browser.close();}
