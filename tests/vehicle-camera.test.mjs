import test from 'node:test';
import assert from 'node:assert/strict';
import {createVehicleCamera} from '../vehicle-camera.js';

const angle=value=>Math.atan2(Math.sin(value),Math.cos(value));
const dt=1/60;
function simulate(camera,seconds,inputs,step=dt){
  const shots=[];
  for(let frame=1;frame<=Math.round(seconds/step);frame++){
    const input=typeof inputs==='function'?inputs(frame*step,frame):inputs;
    shots.push({...camera.update(step,input)});
  }
  return shots;
}

test('follows a bend and looks into it across the ±π boundary without a camera cut',()=>{
  const camera=createVehicleCamera(),heading=2.6,rate=.72;
  let last=camera.update(dt,{heading,speed:12}).yaw;
  const shots=simulate(camera,2.4,t=>({heading:angle(heading+t*rate),speed:12}));
  for(const shot of shots){
    assert.ok(shot.yaw>=last-.0001);
    assert.ok(shot.yaw-last<.024);
    last=shot.yaw;
  }
  const endHeading=heading+rate*2.4,shot=shots.at(-1);
  assert.ok(Math.abs(angle(shot.yaw-(endHeading+Math.PI)))<.13);
  assert.ok(angle(Math.atan2(shot.lookX,shot.lookZ)-endHeading)>-.05);
  assert.ok(Math.hypot(shot.lookX,shot.lookZ)>3);
});

test('taps of reverse and a change of gear keep the current direction; sustained reverse turns to the route',()=>{
  const camera=createVehicleCamera();
  simulate(camera,2,{heading:0,speed:9});
  const tapping=simulate(camera,.45,{heading:0,speed:-4});
  assert.ok(tapping.every(s=>!s.reversing&&Math.abs(s.yaw-Math.PI)<.001));
  const rolling=simulate(camera,1.3,{heading:0,speed:-.35});
  assert.ok(rolling.every(s=>!s.reversing));

  const backward=simulate(camera,4,(_,i)=>({heading:i%2?.006:-.006,speed:-5}));
  assert.ok(!backward[30].reversing&&backward[40].reversing);
  assert.ok(Math.abs(angle(backward.at(-1).yaw))<.03);
  assert.ok(backward.at(-1).lookZ< -1.45);
  const turning=backward.slice(38,85).map((s,i,a)=>i?s.yaw-a[i-1].yaw:0);
  assert.ok(turning.every(s=>Math.abs(s)<2.21*dt));
  assert.ok(turning.every(s=>s<=.0001)||turning.every(s=>s>=-.0001));

  assert.ok(simulate(camera,.22,{heading:0,speed:4}).at(-1).reversing);
  const forward=simulate(camera,4,{heading:0,speed:4}).at(-1);
  assert.ok(!forward.reversing&&Math.abs(angle(forward.yaw-Math.PI))<.02);
  assert.ok(forward.lookZ>1.15);
});

test('manual orbit holds world yaw through a turn, remains free at a stop and hands back gradually',()=>{
  const camera=createVehicleCamera();
  simulate(camera,1,{heading:0,speed:10});
  const drag=simulate(camera,.5,t=>({heading:t,speed:10,manual:true,viewYaw:Math.PI+t*2}));
  const held=drag.at(-1).yaw;
  assert.equal(held,Math.PI+1);
  assert.ok(simulate(camera,.85,t=>({heading:.5-t,speed:9})).every(s=>s.yaw===held));
  const resumed=simulate(camera,.8,{heading:-.35,speed:9});
  assert.ok(Math.abs(resumed.at(-1).yaw-held)>.3);
  assert.ok(resumed.every((s,i)=>!i||Math.abs(s.yaw-resumed[i-1].yaw)<2.21*dt));

  const parked=camera.update(dt,{heading:0,speed:0,manual:true,viewYaw:-1.23}).yaw;
  assert.ok(simulate(camera,6,t=>({heading:t*.08,speed:.04})).every(s=>s.yaw===parked));
  camera.reset();
  const next=camera.update(dt,{heading:1.4,speed:0,kind:'bus',viewYaw:.7});
  assert.equal(next.yaw,.7);
  assert.equal(next.lookX,0);
  assert.equal(next.lookZ,0);
  assert.equal(next.reversing,false);
});

test('frames a persistent side slip in world coordinates without shaking at alternating impulses',()=>{
  const camera=createVehicleCamera(),heading=Math.PI/2;
  const drift=simulate(camera,2,{heading,speed:10,lateralSpeed:4}).at(-1);
  assert.ok(angle(drift.yaw-(heading+Math.PI))>.23);
  assert.ok(drift.lookX>2.6&&drift.lookZ<-.90);
  const jitter=simulate(camera,2.5,(_,i)=>({heading,speed:10,lateralSpeed:i%2?4:-4})).slice(-30);
  assert.ok(Math.max(...jitter.map(s=>s.yaw))-Math.min(...jitter.map(s=>s.yaw))<.003);
});

test('uses a shorter lead on a bicycle, frames the bus route and gives consistent results at 30/120 Hz',()=>{
  function route(kind,step){
    return simulate(createVehicleCamera(),3,t=>({heading:t*.55,speed:8,kind}),step).at(-1);
  }
  const bicycle=route('bike',dt),sedan=route('car',dt),bus=route('bus',dt);
  const distance=s=>Math.hypot(s.lookX,s.lookZ);
  assert.ok(distance(bus)>distance(sedan)+.5&&distance(sedan)>distance(bicycle)+.4);
  const fast=route('bike',1/120),slow=route('bike',1/30);
  assert.ok(Math.abs(slow.yaw-fast.yaw)<.032);
  assert.ok(Math.hypot(slow.lookX-fast.lookX,slow.lookZ-fast.lookZ)<.04);
  const lowFps=createVehicleCamera();
  lowFps.update(.125,{heading:0,speed:0});
  const backing=simulate(lowFps,.625,{heading:0,speed:-3},.125);
  assert.ok(!backing[3].reversing&&backing[4].reversing);
});
