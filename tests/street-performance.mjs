// Run against the local HTTP server. The inspection hook exists only in this browser.
import assert from 'node:assert/strict';
import {chromium} from 'playwright';
const server=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:820,height:520}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/game.js',async route=>{
    const response=await route.fetch();
    await route.fulfill({response,body:await response.text()+`
      window.musicInspection=()=>({mode:playerMotion.mode,
        gaps:streetMusician.performancePose.hands.map((target,i)=>streetMusician.g.userData.hands?.[i]?.getWorldPosition(new THREE.Vector3()).distanceTo(target)??null),
        fixtureBlocked:collides(-24.52,19.2,.35,groundHeight(-24.52,19.2)),
        guitarBlocked:collides(-25.45,19.65,.25,groundHeight(-25.45,19.65))});`});
  });
  await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-11-06T12:00:02Z'));
  const run=milliseconds=>page.clock.runFor(milliseconds);
  const state=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
  await page.goto(new URL('/?debug&near=musica',server).href);await run(500);
  assert.equal((await state()).interaction,'Aplaudir a roda');
  await page.keyboard.press('e');await run(600);
  assert.equal((await state()).music.encores,1);
  assert.equal((await state()).music.phrase,1);
  const pose=await page.evaluate(()=>window.musicInspection());
  assert.equal(pose.mode,'clap');assert.equal(pose.fixtureBlocked,true);assert.equal(pose.guitarBlocked,true);
  assert.ok(pose.gaps.every(gap=>gap!==null&&gap<.16),JSON.stringify(pose));
  await page.keyboard.press('e');await run(200);assert.equal((await state()).music.encores,1);
  await page.keyboard.down('s');await run(320);await page.keyboard.up('s');
  assert.notEqual((await page.evaluate(()=>window.musicInspection())).mode,'clap');
  await page.keyboard.press('Escape');const paused=(await state()).music.elapsed;await run(1200);
  assert.equal((await state()).music.elapsed,paused);await page.keyboard.press('Escape');await run(500);
  assert.ok((await state()).music.elapsed>paused);
  await page.goto(new URL('/?debug&near=musica&weather=squall',server).href);await run(600);
  assert.equal((await state()).music.playing,false);assert.equal((await state()).interaction,'Falar com o músico');
  await page.keyboard.press('e');await run(100);assert.match((await state()).speech.line,/chuva/);
  const mobile=await browser.newPage({viewport:{width:390,height:844},hasTouch:true,isMobile:true});
  mobile.on('pageerror',error=>errors.push(error.message));
  await mobile.clock.install({time:new Date('2026-11-06T12:00:00Z')});
  await mobile.clock.pauseAt(new Date('2026-11-06T12:00:02Z'));
  await mobile.goto(new URL('/?debug&near=musica',server).href);await mobile.clock.runFor(300);
  await mobile.locator('#touchE').tap();await mobile.clock.runFor(300);
  assert.equal(await mobile.evaluate(()=>JSON.parse(document.documentElement.dataset.city).music.encores),1);
  assert.deepEqual(errors,[]);
  console.log('Street guitar hand contact, keyboard/touch applause, original phrases, interruption, physical props and pause passed.');
}finally{await browser.close();}
