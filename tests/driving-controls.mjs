// Run against a locally served game: node tests/driving-controls.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const server=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
async function state(page){return page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));}
async function start(page,scene){
  await page.goto(new URL('/?debug&near='+scene,server).href);
  await page.clock.runFor(180);
}
try{
  for(const touch of [false,true]){
    const page=await browser.newPage({viewport:{width:820,height:450},hasTouch:touch,isMobile:touch}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.clock.install({time:new Date('2026-01-01T00:00:00Z')});
    await page.clock.pauseAt(new Date('2026-01-01T00:00:02Z'));
    await start(page,'dirigir');
    const before=await state(page);
    assert.equal(before.interaction,'Entrar no carro');
    if(touch)await page.locator('#touchE').tap();else await page.keyboard.press('e');
    await page.clock.runFor(16);
    const eye=(await state(page)).camera;
    assert.ok(Math.hypot(eye.x-before.camera.x,eye.y-before.camera.y,eye.z-before.camera.z)<1,'the camera should move smoothly when boarding');
    await page.clock.runFor(1220);
    if(touch){
      const cdp=await page.context().newCDPSession(page);
      async function point(selector,id){
        const box=await page.locator(selector).boundingBox();assert.ok(box,selector);
        return {id,x:box.x+box.width/2,y:box.y+box.height/2};
      }
      const w=await point('[data-key="w"]',1),d=await point('[data-key="d"]',2),lever=await point('#touchLink',3);
      assert.equal(await page.locator('#touchLink').getAttribute('aria-label'),'Segurar freio de mão');
      await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[w]});
      await page.clock.runFor(2400);
      await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[w,d,lever]});
      await page.clock.runFor(480);
      const slide=await state(page);
      assert.ok(slide.handling.handbrake>.96&&slide.handling.slip>.15&&slide.car.steering<-.07);
      await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
      await page.clock.runFor(820);
      assert.ok((await state(page)).handling.handbrake<.01);
    }else{
      await page.keyboard.down('w');await page.clock.runFor(2400);await page.keyboard.up('w');
      const rolling=await state(page);
      assert.ok(rolling.car.speed>6&&rolling.car.speed<12);
      await page.keyboard.down('d');await page.keyboard.down('Shift');await page.clock.runFor(550);
      const slide=await state(page);
      assert.ok(slide.handling.handbrake>.98&&slide.handling.slip>.18);
      assert.ok(slide.audio.tireLevel>0,'audible tire scrub during a slide on asphalt');
      await page.keyboard.up('Shift');await page.keyboard.up('d');
      await page.keyboard.down('Space');await page.clock.runFor(2100);await page.keyboard.up('Space');
      assert.ok(Math.abs((await state(page)).car.speed)<.05);
      await page.keyboard.down('s');await page.clock.runFor(100);assert.equal((await state(page)).car.gear,1);
      await page.clock.runFor(650);await page.keyboard.up('s');assert.ok((await state(page)).car.speed<-.25);

      await start(page,'impact-solid-glance');await page.keyboard.press('e');await page.clock.runFor(1150);
      await page.keyboard.down('w');let closest=Infinity;
      for(let i=0;i<20;i++){
        await page.clock.runFor(210);const {car}=await state(page);
        assert.ok(car&&Number.isFinite(car.speed));
        if(Math.abs(car.x-3.3)>7)break;
        const nose=car.z+Math.abs(Math.cos(car.heading))*2.3+Math.abs(Math.sin(car.heading))*1.25;
        const gap=-44-.15-nose;closest=Math.min(closest,gap);
        assert.ok(gap>=-.015,'vehicle corner passed through the wall');
      }
      await page.keyboard.up('w');assert.ok(closest<.40,'car should reach the wall');
    }
    assert.deepEqual(errors,[]);await page.close();
  }
  console.log('Contextual E, boarding camera, driving, reverse, tire grip, keyboard/touch handbrake and wall contact passed.');
}finally{await browser.close();}

