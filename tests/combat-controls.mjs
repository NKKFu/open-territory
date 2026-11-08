// Start the game and run: node tests/combat-controls.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const base=process.argv[2]||'http://127.0.0.1:8790';
const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
try{
  for(const touch of [false,true]){
    const page=await browser.newPage({viewport:{width:touch?460:900,height:touch?800:540},isMobile:touch,hasTouch:touch});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.route('**/game.js',async route=>{
      const response=await route.fetch();
      // Read the live resident and project the torso onto the rendered camera.
      // No ability, ammunition, position or AI state is injected for this check.
      const probe=`window.combatControlProbe=()=>{
        const near=new URLSearchParams(location.search).get('near');
        const p=near==='fish-vendor'?fishMarket.worker:people.find(p=>p.debugId==='conversation'||
          p.stationary&&Math.abs(p.g.position.x+36)<.2&&Math.abs(p.g.position.z-16)<.2);
        const pos=p.g.position.clone();pos.y+=(near==='fish-vendor'?2.05:1.45)*p.g.scale.y;pos.project(camera);
        const r=renderer.domElement.getBoundingClientRect();
        return {x:r.left+(1+pos.x)*r.width/2,y:r.top+(1-pos.y)*r.height/2,hp:combat.hp(p),playerHp:combat.hp(),down:combat.isDown(p),
          rag:p.ragdoll===true,talk:combat.canTalk(p),shots:combat.snapshot().shots,rounds:combat.weaponInfo('pulse').loaded,
          dispatch:!!fishMarket.nearest({x:158.58,z:-121.6},()=>true,1.8)};
      };`;
      await route.fulfill({response,body:await response.text()+'\n'+probe});
    });
    await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});await page.clock.pauseAt(new Date('2026-11-06T12:00:01Z'));
    async function tick(ms){for(let left=ms;left>0;left-=120)await page.clock.fastForward(Math.min(120,left));}
    const actor=()=>page.evaluate(()=>window.combatControlProbe());
    for(const near of ['conversation-wall',touch?'conversation':'fish-vendor']){
      await page.goto(new URL('/?debug&near='+near,base).href);await page.waitForFunction(()=>!!window.combatControlProbe);await tick(140);
      if(touch){await page.locator('#combatEquip').tap();await tick(100);await page.locator('#combatAim').tap();}
      else{await page.keyboard.press('Digit1');const start=await actor();await page.mouse.move(start.x,start.y);await page.mouse.down({button:'right'});}
      await tick(640);const target=await actor();
      if(touch)await page.touchscreen.tap(target.x,target.y);else await page.mouse.click(target.x,target.y);
      await tick(160);const hit=await actor();assert.equal(hit.shots,1);assert.equal(hit.hp,100);
      if(near==='conversation-wall')assert.equal(hit.rag,false,'the wall takes the shot');
      else{assert(hit.rag&&hit.down&&!hit.talk);if(near==='fish-vendor')assert.equal(hit.dispatch,false);}
      if(touch)await page.locator('#combatReload').tap();else{await page.mouse.up({button:'right'});await page.keyboard.press('KeyR');}
      await tick(1240);assert.equal((await actor()).rounds,1);
      if(touch)await page.locator('#combatAim').tap();else await page.keyboard.press('KeyX');
      await tick(4650);const recovered=await actor();assert(!recovered.down&&!recovered.rag&&recovered.talk);
      if(near==='fish-vendor')assert(recovered.dispatch);
    }
    if(!touch){
      // Start from the same wallet format produced by buying a sidearm in the shop.
      await page.addInitScript(()=>{if(new URLSearchParams(location.search).has('owned'))localStorage.setItem('open-territory.carteira.v1',
        JSON.stringify({v:1,balance:140,shipments:0,loadout:{pulse:{loaded:1,reserve:8},sidearm:{loaded:8,reserve:16}}}));});
      await page.goto(new URL('/?debug&near=conversation&owned',base).href);await page.waitForFunction(()=>!!window.combatControlProbe);
      await page.keyboard.down('KeyW');await tick(310);await page.keyboard.up('KeyW');
      await page.keyboard.down('KeyD');await tick(170);await page.keyboard.up('KeyD');await page.keyboard.press('Digit2');
      let target=await actor();await page.mouse.move(target.x,target.y);await page.mouse.down({button:'right'});await tick(490);
      target=await actor();await page.mouse.click(target.x,target.y);await page.mouse.up({button:'right'});await tick(720);
      const hurt=await actor();assert.equal(hurt.hp,64,'the sidearm wounds the resident');assert.equal(hurt.talk,false);
      assert(hurt.playerHp<100,'an assaulted resident in reach can retaliate');
      await page.keyboard.press('KeyX');await tick(10500);assert.equal((await actor()).playerHp,100);
    }
    assert.deepEqual(errors,[]);await page.close();
  }
  console.log('Desktop and touch aim, cover, stun, reload, civilian and player health, and fishmonger recovery passed.');
}finally{await browser.close();}
