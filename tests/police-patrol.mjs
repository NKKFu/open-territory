// With the game server running: node tests/police-patrol.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const root=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({args:['--enable-unsafe-swiftshader','--autoplay-policy=no-user-gesture-required']});
const distance=(a,b)=>Math.hypot(a.x-b.x,a.z-b.z);
try{
  const page=await browser.newPage({viewport:{width:700,height:410}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-11-06T12:00:01Z'));
  await page.goto(new URL('/?debug&near=fuga',root).href);
  await page.clock.runFor(320);
  const state=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
  const initial=await state();assert.equal(initial.candidate,'car');assert.equal(initial.police.patrol.mode,'ronda');
  await page.keyboard.press('e');await page.clock.runFor(1750);
  let current=await state(),patrol=current.police.patrol;
  assert.ok(current.car&&patrol.siren&&patrol.lights&&current.audio.sirenLevel>0);
  const started={...patrol},firstGain=current.audio.sirenLevel;
  await page.keyboard.down('w');
  let chased=0,levels=[firstGain];
  for(let i=0;i<28;i++){
    await page.clock.runFor(500);current=await state();
    const next=current.police.patrol;
    assert.ok(distance(next,patrol)<7&&next.onRoad&&!next.inSolid,'the cruiser must move along a real street');
    if(next.siren){chased=Math.max(chased,distance(started,next));levels.push(current.audio.sirenLevel);}
    patrol=next;if(current.police.phase==='busca')break;
  }
  await page.keyboard.up('w');
  assert.ok(chased>8&&Math.max(...levels)-Math.min(...levels)>.002);
  assert.equal(current.police.phase,'busca');assert.equal(patrol.siren,false);assert.equal(current.audio.sirenLevel,0);
  assert.ok(distance(current.car,current.police.lastKnown)>20,'a report must stop following the hidden car');
  await page.keyboard.down('Space');await page.clock.runFor(1400);await page.keyboard.up('Space');
  for(let i=0;i<12&&current.police.phase!=='livre';i++){await page.clock.runFor(2000);current=await state();}
  assert.equal(current.police.phase,'livre');assert.equal(current.police.patrol.mode,'ronda');
  assert.equal(current.police.patrol.lights,false);assert.equal(current.audio.sirenLevel,0);
  assert.deepEqual(errors,[]);
  console.log(`Patrol followed the getaway car for ${chased.toFixed(1)} m, lost sight and returned to its route.`);
}finally{await browser.close();}
