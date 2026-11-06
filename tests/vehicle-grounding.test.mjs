import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {vehicleGroundPose} from '../vehicle-grounding.js';
import {createSurfaceSampler} from '../surface-sampler.js';

const wheels=[-1.22,1.22].flatMap(x=>[-1.5,1.5].map(z=>({x,z,y:.52,radius:.5,width:.34})));
function clearance(pose,x,z,yaw,contacts,heightAt){
  const rotation=new THREE.Euler(pose.pitch,yaw,pose.roll,'YXZ');let minimum=Infinity;
  contacts.forEach((w,i)=>{
    for(const side of [-1,1])for(let n=0;n<=32;n++){
      const angle=-Math.PI/2+n*Math.PI/32;
      const p=new THREE.Vector3(w.x+side*w.width/2,w.y+pose.offsets[i]-w.radius*Math.cos(angle),w.z+w.radius*Math.sin(angle)).applyEuler(rotation);
      minimum=Math.min(minimum,pose.height+p.y-heightAt(x+p.x,z+p.z));
    }
  });
  return minimum;
}
test('rendered surfaces resolve rotations, overlapping pavement and lowered entrances',()=>{
  const s=createSurfaceSampler({baseHeight:()=>-.05});
  s.addRectangle({x:0,z:0,w:12,d:4,top:.23,rotation:Math.PI/4});
  s.addRectangle({x:0,z:0,w:4,d:4,top:.07});
  s.addRectangle({x:12,z:0,w:4,d:4,top:.07});
  assert.equal(s.heightAt(0,0),.23);assert.equal(s.heightAt(3,-3),.23);
  assert.equal(s.heightAt(4,4),-.05);assert.equal(s.heightAt(12,0),.07);
  s.addTriangle([{x:20,z:0},{x:24,z:0},{x:20,z:4}],.08);
  s.addTriangle([{x:26,z:0},{x:26,z:4},{x:30,z:0}],.09);
  assert.equal(s.heightAt(21,1),.08);assert.equal(s.heightAt(23,3),-.05);
  assert.equal(s.heightAt(27,1),.09);assert.equal(s.heightAt(29,3),-.05);
});
test('cars, buses and bikes rest their actual tire radii on the surface',()=>{
  for(const contacts of [wheels,wheels.map(w=>({...w,y:.58,radius:.62})),[-1.25,1.25].map(z=>({x:0,z,y:.65,radius:.62,width:.15}))]){
    const pose=vehicleGroundPose({x:0,z:0,yaw:0,wheels:contacts,heightAt:()=>.06});
    assert.ok(Math.abs(clearance(pose,0,0,0,contacts,()=>.06)-.008)<.0001);
    assert.ok(Math.abs(pose.pitch)<.001&&Math.abs(pose.roll)<.001);
  }
});
test('tires stay above a curb while the body and suspension follow the transition',()=>{
  const heightAt=(x,z)=>z>=0?.23:.05;let previous=null,tilted=false;
  for(let z=-4;z<=4;z+=.08){
    const pose=vehicleGroundPose({x:0,z,yaw:0,wheels,heightAt,previous,dt:1/30});
    assert.ok(clearance(pose,0,z,0,wheels,heightAt)>-.014,`tire penetrated curb at z=${z}`);
    assert.ok(pose.offsets.every(y=>y>=-.181&&y<=.161));
    tilted ||= Math.abs(pose.pitch)>.02;previous=pose;
  }
  assert.ok(tilted);
});
test('rotated vehicles follow a hillside without frame-rate dependent sinking',()=>{
  const heightAt=(x,z)=>.09*x+.06*z;
  for(const dt of [1/60,1/20,.1]){
    let previous=null;
    for(let time=0;time<3;time+=dt){
      const x=time*3,z=time*4,yaw=1.1;
      const pose=vehicleGroundPose({x,z,yaw,wheels,heightAt,previous,dt});
      assert.ok(clearance(pose,x,z,yaw,wheels,heightAt)>-.014);
      previous=pose;
    }
  }
});
test('sideways and diagonal curbs do not cut through a tilted tire',()=>{
  const heightAt=(x,z)=>x>.2?.235:.055;
  for(const yaw of [0,.6,Math.PI/2]){
    let previous=null;
    for(let x=-3;x<=3;x+=.075){
      const pose=vehicleGroundPose({x,z:0,yaw,wheels,heightAt,previous,dt:1/30,rollBias:.03,pitchBias:-.02});
      assert.ok(clearance(pose,x,0,yaw,wheels,heightAt)>-.014,`tire penetrated at x=${x}, yaw=${yaw}`);
      previous=pose;
    }
  }
});
