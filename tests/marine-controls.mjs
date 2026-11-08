// Run against the local game: node tests/marine-controls.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const url=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try{
  for(const touch of [false,true]){
    const page=await browser.newPage({viewport:touch?{width:420,height:800}:{width:800,height:470},hasTouch:touch,isMobile:touch});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
    await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});await page.clock.pauseAt(new Date('2026-11-06T12:00:01Z'));
    async function tick(ms){for(let left=ms;left>0;left-=140)await page.clock.fastForward(Math.min(left,140));}
    const read=()=>page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
    async function start(name){await page.goto(new URL('/?debug&near='+name,url).href);await tick(240);return read();}
    const use=()=>touch?page.locator('#touchE').tap():page.keyboard.press('KeyE');
    if(!touch){
      await start('nadar');await page.keyboard.down('KeyW');await tick(3000);await page.keyboard.up('KeyW');
      let d=await read();assert.equal(d.marine.mode,'swim');assert.equal(d.motion.pose,'swim');assert(d.marine.depth>1.7&&d.audio.events.splash>=1);
      await tick(950);assert.equal((await read()).motion.pose,'float');await page.keyboard.press('KeyR');await tick(300);
      assert((await read()).player.ragdoll);await tick(3600);d=await read();assert(!d.player.ragdoll&&d.marine.mode==='swim');
      await page.keyboard.down('KeyS');await tick(5100);await page.keyboard.up('KeyS');d=await read();assert.equal(d.marine.lastExit,'beach');assert.equal(d.marine.mode,'land');
      await start('escada');await use();await tick(250);assert.equal((await read()).motion.pose,'ladder');await tick(720);
      d=await read();assert.equal(d.marine.lastExit,'ladder');assert(Math.abs(d.player.y-.35)<.03);
      await start('lancha');await use();await tick(280);assert.equal((await read()).marine.boat,'Brisa');
      await page.keyboard.down('KeyS');await tick(4900);await page.keyboard.up('KeyS');d=await read();
      assert(d.marine.collisions>0&&d.player.z< -168,'the shallow coast must stop the hull');
      await start('lancha');await use();await tick(200);await use();await tick(350);assert.equal((await read()).marine.lastExit,'land');
    }
    await start('barco-porto&weather=squall');await use();await tick(220);let d=await read();assert.equal(d.marine.boat,'Guará');
    const initial=d.marine.heading;let client;
    async function pad(key,id){const r=await page.locator('[data-key="'+key+'"]').boundingBox();return {x:r.x+r.width/2,y:r.y+r.height/2,id};}
    if(touch){client=await page.context().newCDPSession(page);const w=await pad('w',1),a=await pad('a',2);
      await client.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[w]});await tick(2100);
      await client.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[w,a]});await tick(640);
      await client.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
    }else{await page.keyboard.down('KeyW');await tick(2100);await page.keyboard.down('KeyA');await tick(640);await page.keyboard.up('KeyA');await page.keyboard.up('KeyW');}
    d=await read();assert.equal(d.motion.pose,'pilot');assert(d.marine.speed>5&&d.marine.depth>4&&d.marine.seaState>.45);
    assert(Math.abs(d.marine.heading-initial)>.15&&d.marine.wake>0);
    await use();await tick(3000);d=await read();assert.equal(d.marine.lastExit,'swim');assert.equal(d.marine.mode,'swim');
    await use();await tick(350);assert.equal((await read()).marine.boat,'Guará');assert.deepEqual(errors,[]);await page.close();
  }
  console.log('Beach, floating, recovery, ladder, docking, coast collision, storm sailing and touch controls passed.');
}finally{await browser.close();}
