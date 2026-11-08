// Run against the local game; the fixture changes only the reviewer's position.
// Residents, drone launch, conversation and weather use the real live systems.
import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';

const base=process.argv[2]||'http://127.0.0.1:8790',output=process.argv[3];
if(output)await mkdir(output,{recursive:true});
const browser=await chromium.launch({args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:720,height:440}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/game.js',async route=>{
    const response=await route.fetch();
    await route.fulfill({response,body:await response.text()+`
    import {umbrellaSpace as auditUmbrellaSpace} from './city-social.js';
    window.socialAudit={
      snapshot:()=>({social:citySocial.snapshot(),props:socialProps.snapshot(),time:gameTime,
        speech:activeSpeech?.target?.name,music:streetPerformance.snapshot(),
        people:people.filter(p=>p.musicGuest||p.ambientPose).map(p=>({name:p.name,x:p.g.position.x,y:p.g.position.y,z:p.g.position.z,
          pose:p.ambientPose?.kind,motion:p.motionPose,paused:citySocial.isPaused(p),road:pointOnRoad(p.g.position.x,p.g.position.z)}))}),
      phones:()=>people.filter(p=>p.ambientPose?.kind==='photo').map(p=>{
        const prop=p.g.getObjectByName('Celular do observador'),pose=p.ambientPose;
        if(!prop)return {name:p.name,missing:true};
        const forward=new THREE.Vector3(0,0,1).applyQuaternion(prop.getWorldQuaternion(new THREE.Quaternion()));
        const direction=new THREE.Vector3(pose.lookX,pose.lookY,pose.lookZ).sub(prop.getWorldPosition(new THREE.Vector3())).normalize();
        return {name:p.name,dot:forward.dot(direction),headPitch:p.g.userData.head.rotation.x};
      }),
      canopies:()=>people.filter(p=>p.g.getObjectByName('Guarda-chuva do morador')).map(p=>{
        const c=auditUmbrellaSpace(p);return {...c,name:p.name,solid:collides(c.x,c.z,c.radius,c.y,c.height,true)||!umbrellaClear(c,viewObstacles(c,c.radius,null,true))};
      }),
      approach(name){
        const p=people.find(p=>p.musicGuest&&p.name===name);if(!p)return false;
        for(let i=0;i<16;i++){
          const a=i*Math.PI/8,x=p.g.position.x+Math.sin(a)*1.55,z=p.g.position.z+Math.cos(a)*1.55;
          if(collides(x,z,.42)||vehicleBlocks(x,z,.42,null)||people.some(q=>q!==p&&Math.hypot(q.g.position.x-x,q.g.position.z-z)<.95))continue;
          player.position.set(x,groundHeight(x,z),z);velocity.set(0,0,0);cameraReady=false;
          yaw=targetYaw=a;pitch=targetPitch=.2;
          if(nearestInteraction()?.data===p)return true;
        }return false;
      }
    };`});
  });
  await page.clock.install({time:new Date('2026-11-06T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-11-06T12:00:02Z'));
  const load=async weather=>{
    const url=new URL('/',base);url.search=new URLSearchParams({debug:'',near:'musica',weather}).toString();
    await page.goto(url.href);await page.waitForFunction(()=>!!window.socialAudit);await page.clock.runFor(2000);
  };
  const read=()=>page.evaluate(()=>socialAudit.snapshot());
  const shot=async name=>{if(output)await page.screenshot({path:output+'/'+name+'.png'});};
  const turn=async()=>{
    await page.mouse.move(120,205);await page.mouse.down();await page.mouse.move(644,205,{steps:12});await page.mouse.up();await page.clock.runFor(180);
  };
  await load('clear');const music=await read();
  assert.ok(music.social.active.length>=2&&music.social.active.length<=3);
  assert.ok(music.social.active.every(p=>p.type==='music'));
  await shot('social-music');
  await page.keyboard.press('v');await page.keyboard.down(' ');await page.clock.runFor(430);await page.keyboard.up(' ');
  await page.clock.runFor(1900);const drone=await read(),phones=await page.evaluate(()=>socialAudit.phones());
  assert.equal(drone.social.active.filter(p=>p.type==='drone').length,2);
  assert.ok(phones.length>0&&phones.every(p=>!p.missing&&p.dot>.95&&p.headPitch<-.08),JSON.stringify(phones));
  assert.ok(drone.people.filter(p=>p.paused).every(p=>!p.road));
  await turn();await shot('social-drone-photo');
  await page.keyboard.press('e');await page.clock.runFor(1300);
  assert.equal((await read()).social.active.filter(p=>p.type==='drone').length,0);
  assert.ok(await page.evaluate(()=>socialAudit.approach('Maya')));await page.keyboard.press('e');await page.clock.runFor(120);
  const talking=await read();assert.equal(talking.speech,'Maya');assert.equal(talking.people.find(p=>p.name==='Maya').pose,undefined);
  await load('squall');await turn();const rain=await read(),canopies=await page.evaluate(()=>socialAudit.canopies());
  assert.ok(rain.social.rain.some(p=>p.kind==='rainCover')&&rain.social.rain.some(p=>p.kind==='umbrella'));
  assert.equal(rain.music.playing,false);assert.ok(rain.social.rain.every(p=>p.pace>=1));
  assert.ok(canopies.length>0&&canopies.length<=10&&canopies.every(c=>!c.solid));
  for(let i=0;i<canopies.length;i++)for(let j=i+1;j<canopies.length;j++){
    const a=canopies[i],b=canopies[j];assert.ok(Math.hypot(a.x-b.x,a.z-b.z)>=a.radius+b.radius+.1);
  }
  await shot('social-rain');
  await page.clock.runFor(500);const walking=await read();
  const a=rain.people.find(p=>p.name==='Nilo'),b=walking.people.find(p=>p.name==='Nilo');
  assert.ok(Math.hypot(a.x-b.x,a.z-b.z)>.1&&Math.hypot(a.x-b.x,a.z-b.z)<1.8,'Rain changes pace along the regular path');
  await page.keyboard.press('Escape');await page.clock.runFor(500);assert.equal((await read()).time,walking.time);await page.keyboard.press('Escape');
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({music:music.social.active.map(p=>p.name),drone:drone.social.active,phones,rain:rain.social.rain,
    canopies:canopies.length,props:rain.props.created,conversation:talking.speech,errors}));
}finally{await browser.close();}
