// Start the local server, then run: node tests/city-weather-browser.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const base=process.argv[2]||'http://127.0.0.1:8790',browser=await chromium.launch({args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:720,height:440}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
  await page.route('**/game.js',async route=>{
    const source=await route.fetch();
    const probe=`
    window.weatherAudit={
      state(){return {id:cityWeather.state.id,rain:cityWeather.state.rain,wet:cityWeather.state.wetness,far:scene.fog.far,
        particles:rainField.snapshot().count,exposure:listenerRainExposure(),audio:cityAudio.snapshot()};},
      blockedRain(){
        const geometry=scene.getObjectByName('Chuva').geometry,positions=geometry.attributes.position;
        let belowCeiling=0,outside=0,throughScreen=0,firstLeak=null;
        for(let i=0;i<geometry.drawRange.count;i++){
          const x=positions.getX(i),y=positions.getY(i),z=positions.getZ(i),p=placeContaining(x,z);
          const ground=groundHeight(x,z),roof=weatherCover.roofHeight(x,z);
          if(y<ground-.01||y<roof-.01||(p&&y<p.elevation+5-.01)){
            belowCeiling++;firstLeak??={x,y,z,ground,roof,place:p?.name};
          }
          if(!p)outside++;
          if(i%2===0){
            const ex=positions.getX(i+1),t=(-47.96-x)/(ex-x);
            if(t>0&&t<1){
              const sy=y+(positions.getY(i+1)-y)*t,sz=z+(positions.getZ(i+1)-z)*t;
              if(sy>.825&&sy<4.655&&Math.abs(sz+36.9)<4.47)throughScreen++;
            }
          }
        }
        return {belowCeiling,outside,throughScreen,firstLeak};
      },
      move(x,z){player.position.set(x,groundHeight(x,z),z);velocity.set(0,0,0);cameraReady=false;},
      districtCover(){const dry=(x,z)=>!weatherCover.exposed(x,z,groundHeight(x,z)+1.6);
        return {workshop:dry(180.35,-150),radio:dry(149.5,-158.15),gallery:dry(-44,-36),
          outdoorAudience:!dry(-53.56,-34.29),screen:weatherCover.blocksStreak(-48.3,2.1,-36.9,-47.74,1.7,-36.9)};
      },
      gasGrip(){return [tireGripAt(104,-30.5),tireGripAt(104,-16.8)];},
      request:id=>cityWeather.request(id),
      startBrake(){
        const c=vehicle;c.g.position.set(-49,groundHeight(-49,-134.3),-134.3);c.g.rotation.y=Math.PI/2;
        c.velocity=50/3.6;c.driveDirection=1;c.steeringAngle=0;c.lateralSpeed=0;c.yawRate=0;c.impactYaw=0;c.gear=2;
      },
      car(){return {x:vehicle.g.position.x,speed:vehicle.velocity,grip:wheelGrip(vehicle).grip,impact:!!vehicle.impact};}
    };`;
    await route.fulfill({response:source,body:await source.text()+probe});
  });
  await page.clock.install({time:new Date('2026-01-01T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-01-01T12:00:02Z'));
  const load=async(scene,weather)=>{
    const url=new URL('/',base);url.search=new URLSearchParams({debug:'',near:scene,weather}).toString();
    await page.goto(url.href);await page.waitForFunction(()=>!!window.weatherAudit);await page.clock.runFor(100);
  };
  const state=()=>page.evaluate(()=>window.weatherAudit.state());
  await load('mirante','mist');const mist=await state();assert.equal(mist.particles,0);assert.ok(mist.far<130);
  await load('poltrona','squall');await page.keyboard.press('a');await page.clock.runFor(150);
  const inside=await state(),clipped=await page.evaluate(()=>window.weatherAudit.blockedRain());
  assert.equal(clipped.belowCeiling,0);assert.ok(clipped.outside>0&&inside.particles>0,'The exterior still receives rain while the player is inside');
  await page.evaluate(()=>window.weatherAudit.move(36,-16));await page.clock.runFor(300);
  const outside=await state();assert.ok(inside.exposure<outside.exposure&&inside.audio.rainLevel<outside.audio.rainLevel*.4);
  assert.deepEqual(await page.evaluate(()=>window.weatherAudit.districtCover()),{workshop:true,radio:true,gallery:true,outdoorAudience:true,screen:true});
  const [covered,exposed]=await page.evaluate(()=>window.weatherAudit.gasGrip());
  assert.ok(covered>exposed+.1,'Rain changes the exposed forecourt, while shelter protects the pump area');
  for(const [x,z] of [[180.35,-150],[-53.56,-34.29]]){
    await page.evaluate(([x,z])=>window.weatherAudit.move(x,z),[x,z]);await page.clock.runFor(210);
    const rain=await page.evaluate(()=>window.weatherAudit.blockedRain());
    assert.equal(rain.belowCeiling,0,'Roof or ground leak at '+JSON.stringify(rain.firstLeak));assert.equal(rain.throughScreen,0);assert.ok(rain.outside>0);
  }
  await page.evaluate(()=>window.weatherAudit.request('clear'));await page.clock.runFor(480);
  const fading=await state();await page.keyboard.press('Escape');await page.clock.runFor(640);
  assert.equal((await state()).rain,fading.rain,'Pause freezes the front');await page.keyboard.press('Escape');
  await page.clock.runFor(650);assert.ok((await state()).rain<fading.rain);
  const braking=[];
  for(const weather of ['clear','squall']){
    await load('dirigir',weather);await page.keyboard.press('e');await page.clock.runFor(1350);
    await page.evaluate(()=>window.weatherAudit.startBrake());await page.keyboard.down('s');
    let car;
    for(let i=0;i<90;i++){
      await page.clock.runFor(32);car=await page.evaluate(()=>window.weatherAudit.car());if(car.speed<.04)break;
    }
    await page.keyboard.up('s');assert.ok(!car.impact&&Math.abs(car.speed)<.10);braking.push(car.x+49);
  }
  assert.ok(braking[1]>braking[0]*1.15);assert.deepEqual(errors,[]);
  console.log('Rain cover, forecast transition, pause and stops from 50 km/h passed:',braking.map(m=>m.toFixed(2)+' m').join(' / '));
}finally{await browser.close();}
