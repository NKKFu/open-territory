import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../vendor/three.module.js';
import {vehicleDesigns,carDesign,buildCarShell,buildRoadWheel} from '../vehicle-assets.js';

const cube=new T.BoxGeometry(),mats=new Map();
function material(color){if(!mats.has(color))mats.set(color,new T.MeshStandardMaterial({color}));return mats.get(color);}
function part(g,x,y,z,w,h,d,tint){
  const mesh=new T.Mesh(cube,typeof tint==='string'?material(tint):tint);
  mesh.scale.set(w,h,d);mesh.position.set(x,y,z);g.add(mesh);return mesh;
}
const glass=new T.MeshStandardMaterial({color:'#587785',transparent:true,side:T.DoubleSide});
function assemble(d){
  const g=new T.Group(),shell=buildCarShell(g,d,'#968665',{part,material,glass,batch(){},
    head:material('#fae7ba'),brake:material('#ab3838'),reverse:material('#bec7b5'),
    indicators:{left:material('#c69b31'),right:material('#c69b31')}});
  return {g,...shell};
}

test('five reusable silhouettes provide an aligned seat, steering rim, bonnet and walkable roof',()=>{
  assert.equal(new Set(vehicleDesigns.map(d=>d.id)).size,5);assert.equal(carDesign(5),carDesign(0));
  assert.ok(carDesign(2).deck<carDesign(2).belt-.35);
  assert.ok(carDesign(1).backTop<carDesign(0).backTop-.65);
  assert.ok(carDesign(3).length<carDesign(0).length-.5);assert.ok(carDesign(4).roof>carDesign(1).roof+.23);
  for(const d of vehicleDesigns){
    const {g,surfaces,seatMount,wheelMount}=assemble(d);
    const roof=surfaces.find(s=>s.top===d.roof);assert.ok(roof&&roof.d>.9&&roof.w>1.5);
    assert.ok(surfaces.some(s=>s.top===d.hood&&s.z>d.windBase));
    assert.ok(seatMount.y+2.20<d.roof-.045,'the driver must fit beneath the roof');
    assert.equal(wheelMount.x,seatMount.x);assert.ok(Math.abs(wheelMount.z-seatMount.z-.90)<.001);
    const box=new T.Box3().setFromObject(g);
    assert.ok(box.max.z>=d.length/2&&box.max.z<d.length/2+.19);
    assert.ok(box.min.z<=-d.length/2+.02&&box.min.z> -d.length/2-.16);
  }
});

test('front door glass leaves its actual aperture when its hinge swings',()=>{
  for(const d of vehicleDesigns){
    const {g,doors}=assemble(d),origin=new T.Vector3(2.9,(d.roof+d.belt)/2,(d.pillar+d.windTop)/2);
    const ray=new T.Raycaster(origin,new T.Vector3(-1,0,0));
    g.updateMatrixWorld(true);
    const hit=ray.intersectObject(g,true)[0];assert.equal(hit.object.material,glass);
    doors[1].rotation.y=-1.06;g.updateMatrixWorld(true);
    const open=ray.intersectObject(g,true).filter(h=>h.object.material===glass&&h.point.x>0&&Math.abs(h.point.x-hit.point.x)<.07);
    assert.equal(open.length,0,'stationary bodywork must not leave a second window over the opening');
  }
});

test('twelve-sided rubber meets the suspension radius on every wheel and keeps front steering pivots',()=>{
  for(const d of vehicleDesigns){
    const g=new T.Group(),wheels=[],mounts=[];
    for(const s of [-1,1])for(const z of d.axles)buildRoadWheel(g,wheels,mounts,s*(d.width/2-.13),d.tire+.019,z,d.tire,.29,z>0,d.id,{part,material});
    assert.equal(wheels.length,4);assert.equal(mounts.filter(w=>w.steer).length,2);
    for(const [i,tire] of wheels.entries()){
      const p=tire.geometry.attributes.position,ys=new Set();
      let floor=Infinity;
      for(let n=0;n<p.count;n++){floor=Math.min(floor,p.getY(n));if(Math.hypot(p.getY(n),p.getZ(n))>d.tire-.00001)ys.add(Math.round(Math.atan2(p.getY(n),p.getZ(n))*180/Math.PI/30)%12);}
      assert.ok(ys.size>=12&&ys.size<=13);assert.ok(Math.abs(floor+mounts[i].radius)<1e-6);
      assert.ok(Math.abs(mounts[i].y+floor-.019)<.00001);assert.equal(tire.parent,mounts[i].spin);
    }
  }
});
