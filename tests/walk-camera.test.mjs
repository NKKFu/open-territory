import test from 'node:test';
import assert from 'node:assert/strict';
import {createWalkCamera,frameCloseActor} from '../walk-camera.js';

const angle=n=>Math.atan2(Math.sin(n),Math.cos(n));

test('a held sidestep stays straight while the camera comes behind the new heading',()=>{
  const camera=createWalkCamera();
  let yaw=0;
  for(let frame=0;frame<120;frame++){
    const dir=camera.direction(0,1,yaw);
    assert.ok(Math.abs(dir.x-1)<1e-9&&Math.abs(dir.z)<1e-9);
    yaw=camera.follow(1/60,yaw,Math.PI/2,5,true);
  }
  assert.ok(Math.abs(angle(yaw+Math.PI/2))<.01);
  camera.release();
  const forward=camera.direction(1,0,yaw);
  assert.ok(forward.x>.99,'W now continues along the new screen heading');
});

test('a half turn follows one arc at comparable speeds across frame rates',()=>{
  function reverse(fps){
    const camera=createWalkCamera();let yaw=0;
    for(let n=0;n<fps;n++){
      const dir=camera.direction(-1,0,yaw);
      assert.ok(dir.z>.999);
      const next=camera.follow(1/fps,yaw,n%2?.001:-.001,5,true);
      assert.ok(next>=yaw-.002&&next-yaw<4/fps);
      yaw=next;
    }
    return yaw;
  }
  assert.ok(reverse(30)>2.9);
  assert.ok(Math.abs(reverse(30)-reverse(120))<.025);
});

test('manual look can steer held movement and keeps its chosen orbit at rest',()=>{
  const camera=createWalkCamera();
  camera.direction(1,0,0);camera.orbit(.7);
  const dir=camera.direction(1,0,.55);
  assert.ok(Math.abs(dir.x+Math.sin(.7))<1e-9);
  camera.release();
  assert.equal(camera.follow(.1,.7,0,0,true),.7);
  camera.direction(-1,1,.7);
  assert.equal(camera.follow(.1,.7,1.9,6,false),.7);
});

test('a short camera arm keeps head and shoes in view without altering the open-street field of view',()=>{
  const feet={x:0,y:.18,z:0},eye={x:0,y:2.1,z:2.25},look={x:.4,y:1.65,z:0};
  assert.equal(frameCloseActor(eye,look,feet,2.45,0,58),58);
  assert.equal(look.x,.4);
  const fov=frameCloseActor(eye,look,feet,2.45,1,58);
  const aim=Math.atan2(look.y-eye.y,eye.z);
  for(const y of [feet.y,feet.y+2.45]){
    const screenAngle=Math.abs(Math.atan2(y-eye.y,eye.z)-aim);
    assert.ok(screenAngle<fov*Math.PI/360);
  }
  assert.ok(fov<=71);
});
