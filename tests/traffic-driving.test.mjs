import test from 'node:test';
import assert from 'node:assert/strict';
import {stepTrafficDrive} from '../traffic-driving.js';

function run(state,target,speed,seconds,dt=1/60){
  for(let t=0;t<seconds-1e-8;t+=dt){
    const step=stepTrafficDrive(state,{target,speed},dt);
    state={x:state.x+step.dx,z:state.z+step.dz,heading:step.heading,speed:step.speed,steering:step.steering,lateral:step.lateral};
  }
  return state;
}

test('a car follows a straight target without sliding sideways',()=>{
  const result=run({x:0,z:0,heading:0}, {x:0,z:100},6,2);
  assert.ok(result.z>6);assert.ok(Math.abs(result.x)<1e-9);assert.ok(Math.abs(result.heading)<1e-9);
});

test('a lane change turns the car and moves in the direction it faces',()=>{
  const state={x:0,z:0,heading:0,speed:4};
  const first=stepTrafficDrive(state,{target:{x:-6.6,z:12},speed:4},1/60);
  assert.ok(first.heading<0&&first.steering<0);
  assert.ok(first.dz>0&&first.dx<0);
  const result=run(state,{x:-6.6,z:12},4,1);
  assert.ok(result.x<-.4&&result.z>2);
  assert.ok(result.heading<-.1);
});

test('brakes to a stop when the route or signal requires it',()=>{
  const result=run({x:0,z:0,heading:0,speed:7},{x:0,z:100},0,1);
  assert.ok(Math.abs(result.speed)<.05);assert.ok(result.z<3);
});

test('a car facing away from its route steers to turn back',()=>{
  const step=stepTrafficDrive({x:0,z:0,heading:Math.PI,speed:2},{target:{x:0,z:20},speed:3},1/60);
  assert.ok(Math.abs(step.steering)>.01);
  assert.ok(Math.abs(step.yawRate)>.001);
});
