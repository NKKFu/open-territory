// With the game server running: node tests/camera-steering.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const server=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
const distance=(a,b)=>Math.hypot(a.x-b.x,a.z-b.z);
const heading=(from,to)=>Math.atan2(to.x-from.x,to.z-from.z);
const angleError=(a,b)=>Math.abs(Math.atan2(Math.sin(a-b),Math.cos(a-b)));

async function state(page){
  return page.evaluate(()=>JSON.parse(document.documentElement.dataset.city));
}

async function start(page,scene){
  await page.goto(new URL('/?debug&near='+scene,server).href);
  await page.clock.runFor(192);
}

// A moving camera must not continuously rotate the world direction of a held key.
async function checkTurn(page,move){
  const before=await state(page);
  await move(true);await page.clock.runFor(650);
  const middle=await state(page);await page.clock.runFor(1150);
  const end=await state(page);await move(false);
  const straight=distance(before.player,end.player)/(distance(before.player,middle.player)+distance(middle.player,end.player));
  assert.ok(distance(before.player,end.player)>7.5&&straight>.995,'held movement must stay straight');
  const rear=heading(middle.player,end.player)+Math.PI;
  assert.ok(angleError(heading(end.player,end.camera),rear)<.13,'camera must come behind the new direction');
}

try{
  for(const touch of [false,true]){
    const page=await browser.newPage({viewport:{width:760,height:440},hasTouch:touch,isMobile:touch});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.clock.install({time:new Date('2026-01-01T00:00:00Z')});
    await page.clock.pauseAt(new Date('2026-01-01T00:00:02Z'));
    await start(page,'coast');
    if(touch){
      const button=await page.locator('[data-key="d"]').boundingBox();
      assert.ok(button,'right touch control must be visible');
      const cdp=await page.context().newCDPSession(page);
      await checkTurn(page,down=>cdp.send('Input.dispatchTouchEvent',{
        type:down?'touchStart':'touchEnd',
        touchPoints:down?[{x:button.x+button.width/2,y:button.y+button.height/2,id:1}]:[]
      }));
    }else{
      await checkTurn(page,down=>down?page.keyboard.down('d'):page.keyboard.up('d'));
      await checkTurn(page,down=>down?page.keyboard.down('s'):page.keyboard.up('s'));
      await start(page,'dirigir');await page.keyboard.press('e');await page.clock.runFor(1300);
      await page.keyboard.down('w');await page.clock.runFor(1370);
      const entering=(await state(page)).car;
      assert.ok(entering&&entering.speed>3);
      await page.keyboard.down('d');await page.clock.runFor(950);await page.keyboard.up('d');
      await page.clock.runFor(580);
      const exit=await state(page);
      assert.ok(angleError(exit.car.heading,entering.heading)>.35,'the car must complete an actual turn');
      assert.ok(angleError(heading(exit.car,exit.camera),exit.car.heading+Math.PI)<.24,'follow the road after the turn');
      await page.keyboard.up('w');
    }
    assert.deepEqual(errors,[]);
    await page.close();
  }
  console.log('Keyboard and touch direction changes, held trajectories and the driving camera passed.');
}finally{
  await browser.close();
}
