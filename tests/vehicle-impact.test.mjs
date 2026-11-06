import test from 'node:test';
import assert from 'node:assert/strict';
import {resolveVehicleImpact,vehicleSupportArm} from '../vehicle-impact.js';

test('a rear-end impact transfers momentum to a parked car',()=>{
  const result=resolveVehicleImpact({x:0,z:12,mass:1},{x:0,z:0,mass:1},{x:0,z:1});
  assert.ok(result.b.z>5);
  assert.ok(result.a.z<result.b.z);
  assert.ok(Math.abs(result.a.z+result.b.z-12)<1e-9);
});

test('a heavy bus moves less than a car from the same impact',()=>{
  const driver={x:10,z:0,mass:1},normal={x:1,z:0};
  const car=resolveVehicleImpact(driver,{x:0,z:0,mass:1},normal);
  const bus=resolveVehicleImpact(driver,{x:0,z:0,mass:5},normal);
  assert.ok(bus.b.x<car.b.x);
  assert.ok(bus.a.x<car.a.x);
});

test('side impacts transfer motion sideways without changing tangential velocity',()=>{
  const result=resolveVehicleImpact({x:8,z:3,mass:1},{x:0,z:-2,mass:1},{x:1,z:0});
  assert.ok(result.b.x>0);
  assert.equal(result.a.z,3);
  assert.equal(result.b.z,-2);
});

test('separating vehicles do not receive another impulse',()=>{
  assert.equal(resolveVehicleImpact({x:0,z:2,mass:1},{x:0,z:5,mass:1},{x:0,z:1}),null);
});

test('an off-center impact makes both vehicles rotate',()=>{
  const a={x:0,z:10,mass:1,inertia:2},b={x:0,z:0,mass:1,inertia:2};
  const result=resolveVehicleImpact(a,b,{x:0,z:1},.12,{a:{x:.7,z:2.3},b:{x:-.7,z:-2.3}});
  assert.ok(Math.abs(result.a.angular)>.2);
  assert.ok(Math.abs(result.b.angular)>.2);
  assert.ok(Math.abs(result.a.z+result.b.z-10)<1e-9);
});

test('a centered rear-end impact does not create artificial spin',()=>{
  const body={x:0,z:0,mass:1,inertia:2};
  const result=resolveVehicleImpact({...body,z:10},body,{x:0,z:1},.12,{a:{x:0,z:2.3},b:{x:0,z:-2.3}});
  assert.equal(result.a.angular,0);assert.equal(result.b.angular,0);
});

test('a glancing impact on a wall applies torque without moving the wall',()=>{
  const normal={x:0,z:1},arm=vehicleSupportArm(.25,2.5,4.6,normal);
  const result=resolveVehicleImpact({x:1,z:9,mass:1,inertia:2.3},{x:0,z:0,mass:Infinity},normal,.05,{a:arm});
  assert.ok(Math.abs(result.a.angular)>.2);
  assert.equal(result.b.z,0);
  const centered=vehicleSupportArm(0,2.5,4.6,normal);
  assert.equal(centered.x,0);
});
