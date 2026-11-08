import test from 'node:test';
import assert from 'node:assert/strict';
import {stepTrafficDrive} from '../traffic-driving.js';
import {approachSpeed} from '../traffic-ai.js';

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

test('a driver brakes progressively down to the line rather than hitting a hard distance cap',()=>{
  for(const fps of [30,60,120]){
    let state={x:0,z:0,heading:0,speed:7,commandAcceleration:0};
    let peakBrake=0,changes=0,braking=false;
    for(let i=0;i<fps*8;i++){
      const gap=22-state.z,speed=Math.min(7,approachSpeed(gap));
      const step=stepTrafficDrive(state,{target:{x:0,z:100},speed},1/fps);
      if(step.braking!==braking){changes++;braking=step.braking;}
      peakBrake=Math.max(peakBrake,-step.acceleration);
      state={...step,x:state.x+step.dx,z:state.z+step.dz};
      assert.ok(state.z<=22);
    }
    assert.ok(peakBrake<3.5&&peakBrake>1.7);
    assert.ok(state.z>21.9&&state.speed<.055);
    assert.ok(changes<=3);
  }
});

test('a pedestrian stepping out at short range can override the normal brake ramp',()=>{
  const state={x:0,z:0,heading:0,speed:7,commandAcceleration:0,targetSpeed:7,pedal:.1};
  const input={target:{x:0,z:25},speed:1.8};
  const usual=stepTrafficDrive(state,input,1/60);
  const sudden=stepTrafficDrive(state,{...input,urgent:true},1/60);
  assert.ok(sudden.braking&&sudden.acceleration< -6);
  assert.ok(sudden.acceleration<usual.acceleration-4);
});
