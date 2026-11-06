import test from 'node:test';
import assert from 'node:assert/strict';
import {stepCarDynamics} from '../vehicle-dynamics.js';

function drive(state,input,seconds,dt=1/60){
  for(let time=0;time<seconds-1e-8;time+=dt)state=stepCarDynamics(state,input,Math.min(dt,seconds-time));
  return state;
}

test('accelerates forward, brakes and engages reverse while S is held',()=>{
  const forward=drive({}, {throttle:1}, 3);
  assert.ok(forward.speed>20);
  const reverse=drive(forward,{throttle:-1},4);
  assert.ok(reverse.speed< -5);
  assert.equal(reverse.gear,-1);
});

test("steers right and left relative to the driver's view, including in reverse",()=>{
  const right=drive({speed:8},{throttle:1,steer:1},.4);
  const left=drive({speed:8},{throttle:1,steer:-1},.4);
  const backwards=drive({speed:-8},{throttle:-1,steer:1},.4);
  assert.ok(right.steering<0&&right.yawRate<0);
  assert.ok(left.steering>0&&left.yawRate>0);
  assert.ok(backwards.steering<0&&backwards.yawRate>0);
});

test('tire grip changes acceleration and braking without changing controls',()=>{
  const road=drive({}, {throttle:1,grip:1},2),grass=drive({}, {throttle:1,grip:.68},2);
  assert.ok(road.speed>grass.speed+3);
  const stopped=drive({speed:10},{brake:true,grip:1},1);
  assert.ok(Math.abs(stopped.speed)<.03);
});

test('acceleration is stable across common frame rates',()=>{
  const fine=drive({}, {throttle:1,steer:.3},3,1/120);
  const coarse=drive({}, {throttle:1,steer:.3},3,1/30);
  assert.ok(Math.abs(fine.speed-coarse.speed)<.2);
  assert.ok(Math.abs(fine.yawRate-coarse.yawRate)<.03);
});
