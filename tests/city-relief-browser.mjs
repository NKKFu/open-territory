// Run against a local game server: node tests/city-relief-browser.mjs http://127.0.0.1:8790
import assert from 'node:assert/strict';
import {chromium} from 'playwright';

const url=new URL('/?debug&near=subida',process.argv[2]||'http://127.0.0.1:8790');
const browser=await chromium.launch({args:['--enable-unsafe-swiftshader']});
try{
  const page=await browser.newPage({viewport:{width:720,height:440}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.route('**/game.js',async route=>{
    const source=await route.fetch();
    const probes=`
    window.terrainAudit={
      firstRoadHits(){
        scene.updateMatrixWorld(true);
        const ray=new THREE.Raycaster(),rows=[],down=new THREE.Vector3(0,-1,0),opaque=[];
        scene.traverseVisible(obj=>{if(obj.isMesh&&!obj.material.transparent)opaque.push(obj);});
        for(const [x,z] of [[4.6,3.3],[14.26,3.3],[25.69,3.3],[36.3,3.3],[-21,-3.3],
          [3.3,63],[3.3,84],[3.3,128],[3.3,146],[-126.7,101],[133.3,112]]){
          if(cars.some(c=>Math.hypot(c.g.position.x-x,c.g.position.z-z)<6)||
            people.some(p=>Math.hypot(p.g.position.x-x,p.g.position.z-z)<1))continue;
          ray.set(new THREE.Vector3(x,28,z),down);
          const first=ray.intersectObjects(opaque,false)[0];
          rows.push({x,z,floor:groundHeight(x,z),first:first?.point.y});
        }
        return rows;
      },
      roadAndSidewalk(){
        const ray=new THREE.Raycaster(),color=new THREE.Color(),rows=[];
        const earth=scene.children.find(mesh=>mesh.geometry?.type==='PlaneGeometry'&&mesh.material===material('#a9c590'));
        const candidates=scene.children.filter(mesh=>mesh.isMesh&&
          (mesh.userData.followsGrade||mesh.material?.userData.weatherFloor||mesh.material?.userData.citySurface==='atlas'||mesh.material===material('#60767b')));
        for(const [x,shade] of [[3.3,'#60767b'],[9,'#d9d2b9']])for(const z of [124.4,131.7,142.3,148.1]){
          ray.set(new THREE.Vector3(x,19,z),new THREE.Vector3(0,-1,0));
          const wanted=material(shade).color.getHex();
          const hit=ray.intersectObjects(candidates,false).find(h=>{
            if(h.face.normal.y<.4)return false;
            if(!h.object.isInstancedMesh)return h.object.material.color.getHex()===wanted;
            if(!h.object.instanceColor)return false;
            h.object.getColorAt(h.instanceId,color);return color.getHex()===wanted;
          });
          rows.push({x,z,physical:groundHeight(x,z),visible:hit?.point.y,
            soil:earth&&ray.intersectObject(earth,false)[0]?.point.y});
        }
        return rows;
      },
      placePedestrian(downhill){const z=downhill?146:124;player.position.set(9,groundHeight(9,z),z);player.rotation.y=downhill?Math.PI:0;
        yaw=targetYaw=downhill?0:Math.PI;velocity.set(0,0,0);walkCamera.reset();cameraReady=false;},
      state(){const p=(vehicle?.g||player).position;
        return {y:p.y,z:p.z,floor:groundHeight(p.x,p.z),slope:vehicle?.groundPose?.slopePitch||0,driving:!!vehicle};}
    };`;
    await route.fulfill({response:source,body:await source.text()+probes});
  });
  await page.clock.install({time:new Date('2026-01-01T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-01-01T12:00:02Z'));
  const load=async()=>{await page.goto(url.href);await page.waitForFunction(()=>!!window.terrainAudit);await page.clock.runFor(96);};
  const state=()=>page.evaluate(()=>window.terrainAudit.state());
  await load();
  const roadHits=await page.evaluate(()=>window.terrainAudit.firstRoadHits());
  assert.ok(roadHits.length>=5&&roadHits.some(p=>p.z===3.3),'Must sample the main avenue and the hills');
  for(const s of roadHits)assert.ok(Math.abs(s.first-s.floor)<.025,'The first opaque hit must be the road '+JSON.stringify(s));
  for(const s of await page.evaluate(()=>window.terrainAudit.roadAndSidewalk())){
    assert.ok(Number.isFinite(s.visible)&&Math.abs(s.visible-s.physical)<.008,'Missing or misplaced surface '+JSON.stringify(s));
    assert.ok(Number.isFinite(s.soil)&&s.soil<s.visible-.025,'Earth must stay below the paving '+JSON.stringify(s));
  }
  for(const downhill of [false,true]){
    await page.evaluate(d=>window.terrainAudit.placePedestrian(d),downhill);
    const start=await state();
    await page.keyboard.down('Shift');await page.keyboard.down('w');
    for(let i=0;i<15;i++){
      await page.clock.runFor(160);const foot=await state();
      assert.ok(Math.abs(foot.y-foot.floor)<.015,'The feet must follow a continuous grade');
    }
    await page.keyboard.up('w');await page.keyboard.up('Shift');
    const end=await state();assert.ok((end.floor-start.floor)*(downhill?-1:1)>2,'Run along the sloped sidewalk in both directions');
  }
  await load();await page.keyboard.press('e');await page.clock.runFor(1200);
  const car=await state();assert.ok(car.driving);
  await page.keyboard.down('w');await page.clock.runFor(4000);await page.keyboard.up('w');
  const uphill=await state();assert.ok(uphill.floor-car.floor>2&&Math.abs(uphill.slope)>.08,'Drive up the same slope');
  assert.deepEqual(errors,[]);
  console.log('Rendered road, sidewalk, pedestrian and vehicle elevation passed.');
}finally{await browser.close();}
