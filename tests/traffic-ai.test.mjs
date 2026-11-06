import test from 'node:test';
import assert from 'node:assert/strict';
import {passingLaneClear} from '../traffic-ai.js';

const car={x:0,z:0,fx:0,fz:1,rx:1,rz:0};
const vehicle=(x,z,vz=0)=>({x,z,vx:0,vz,width:2.5,length:4.6});

test('a clear opposite lane permits passing a stationary obstacle',()=>{
  assert.equal(passingLaneClear(car,[vehicle(0,22)],[],40),true);
});
test('oncoming traffic and a fast car approaching from behind prevent passing',()=>{
  assert.equal(passingLaneClear(car,[vehicle(-6.6,60,-7)],[],40),false);
  assert.equal(passingLaneClear(car,[vehicle(-6.6,-18,6)],[],40),false);
  assert.equal(passingLaneClear(car,[vehicle(-6.6,110,-7)],[],40),true);
});
test('a pedestrian in the lane-change corridor prevents the maneuver',()=>{
  assert.equal(passingLaneClear(car,[],[{x:-3,z:16}],40),false);
  assert.equal(passingLaneClear(car,[],[{x:15,z:16}],40),true);
});
