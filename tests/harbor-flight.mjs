// Run against the local game server, e.g. node tests/harbor-flight.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';
const server=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:920,height:570}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  // Only this browser can sample the human collision capsule inside the real gantry.
  await page.route('**/game.js',async route=>{
    const res=await route.fetch();
    await route.fulfill({response:res,body:await res.text()+'\nwindow.underLoad=()=>collides(164,-154.5,.46,groundHeight(164,-154.5));'});
  });
  await page.clock.install({time:new Date('2026-11-06T12:00Z')});
  await page.clock.pauseAt(new Date('2026-11-06T12:00:02Z'));
  const run=ms=>page.clock.runFor(ms);
  const state=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
  async function go(spot){await page.goto(new URL('/?debug&near='+spot,server).href);await run(240);}
  async function pulse(label){
    await page.keyboard.down('f');await run(64);
    const highlighted=(await state()).hack;assert.equal(highlighted,label);
    await page.keyboard.up('f');await run(48);
  }
  async function aimDrone(x,y,z){
    const d=(await state()).drone,turn=Math.atan2(d.x-x,d.z-z),tilt=Math.atan2(d.y+.13-y,Math.hypot(d.x-x,d.z-z))+.107;
    let dx=-Math.atan2(Math.sin(turn-d.yaw),Math.cos(turn-d.yaw))/.006,dy=(tilt-d.pitch)/.004;
    while(Math.abs(dx)+Math.abs(dy)>1){
      const stepX=Math.max(-280,Math.min(280,dx)),stepY=Math.max(-170,Math.min(170,dy));
      await page.mouse.move(440,260);await page.mouse.down();await page.mouse.move(440+stepX,260+stepY,{steps:3});await page.mouse.up();
      dx-=stepX;dy-=stepY;
    }
    await run(100);
  }
  await go('guindaste');assert.ok(await page.evaluate(()=>window.underLoad()));
  await pulse('IÇAR CARGA');await run(3200);assert.equal((await state()).harbor.lift.position,1);
  assert.equal(await page.evaluate(()=>window.underLoad()),false);
  await go('drone');assert.equal((await state()).interaction,'Pilotar drone');
  const original=(await state()).player;await page.keyboard.press('e');await run(250);
  const launched=await state();assert.equal(launched.drone.mode,'flying');
  await page.mouse.wheel(0,-360);await run(96);assert.ok((await state()).drone.lensScale<.75);
  await page.mouse.wheel(0,400);await run(96);
  await page.keyboard.down('Space');await page.keyboard.down('d');await run(720);
  await page.keyboard.up('Space');await page.keyboard.up('d');await run(250);
  let now=await state();assert.ok(now.drone.y>launched.drone.y+1.7);
  assert.ok(Math.hypot(now.player.x-original.x,now.player.z-original.z)<.02);
  await aimDrone(159,1.68,-149.60);await pulse('IÇAR CARGA');assert.equal((await state()).harbor.lift.destination,1);
  await aimDrone(145.1,1.81,-152.14);await pulse('SINTONIZAR RÁDIO');await run(1250);
  now=await state();assert.equal(now.harbor.mural,1);assert.equal(now.harbor.broadcasts,1);
  await page.keyboard.press('e');await run(2300);assert.equal((await state()).drone.mode,'stowed');
  await go('cinema');assert.equal((await state()).interaction,'Projetar curta');
  await page.keyboard.press('e');await run(240);assert.equal((await state()).cinema.playing,true);
  await page.keyboard.press('e');await run(80);assert.equal((await state()).cinema.playing,false);
  await go('boletim');assert.equal((await state()).interaction,'Consultar o tempo');
  await page.keyboard.press('e');await run(100);assert.match((await state()).speech.line,/Vento de \d+ km\/h/);
  // Both entry points exercise browser fullscreen, while the world and its controls resize together.
  await page.keyboard.press('Escape');assert.ok(await page.locator('#settings').isVisible());
  await page.locator('#toggleFullscreen').click();await page.waitForFunction(()=>document.fullscreenElement===document.documentElement);
  assert.equal(await page.locator('#settings').isVisible(),false);
  await page.keyboard.press('Escape');await run(250);assert.ok(await page.locator('#settings').isVisible());
  if(await page.evaluate(()=>!!document.fullscreenElement))await page.locator('#toggleFullscreen').click();
  else await page.locator('#resumeGame').click();
  await page.waitForFunction(()=>!document.fullscreenElement);
  assert.equal(await page.locator('#settings').isVisible(),false);
  await page.keyboard.press('Alt+Enter');await page.waitForFunction(()=>document.fullscreenElement===document.documentElement);
  await page.keyboard.press('Alt+Enter');await page.waitForFunction(()=>!document.fullscreenElement);
  assert.equal(await page.locator('#toggleFullscreen').getAttribute('aria-pressed'),'false');
  const mobile=await browser.newPage({viewport:{width:390,height:844},hasTouch:true,isMobile:true});
  mobile.on('pageerror',e=>errors.push(e.message));await mobile.goto(new URL('/?debug&near=drone',server).href);
  await mobile.locator('#touchFullscreen').tap();await mobile.waitForFunction(()=>!!document.fullscreenElement);
  await mobile.locator('#touchDroneLaunch').tap();await mobile.waitForFunction(()=>window.cityDebug.drone.mode==='flying');
  assert.ok(await mobile.locator('[data-key="droneup"]').isVisible());await mobile.locator('#touchE').tap();
  await mobile.waitForFunction(()=>window.cityDebug.drone.mode==='stowed');
  assert.deepEqual(errors,[]);
  console.log('Crane clearance, drone flight/remote hacks/recall, animated radio, cinema, weather bulletin, touch and native fullscreen passed.');
}finally{await browser.close();}
