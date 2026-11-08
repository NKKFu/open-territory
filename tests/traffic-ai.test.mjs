import test from 'node:test';
import assert from 'node:assert/strict';
import {passingLaneClear,requestCrossing,signalPhase,clearsAmber,approachSpeed,pedestrianConflict,routeBends,routeAdvice} from '../traffic-ai.js';

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

test('a crossing request clears the moving traffic, holds both reds locally and resumes with a full green',()=>{
  const local={offset:0,amberUntil:0,reopenAt:0}, neighbor={...local};
  requestCrossing(local,5);
  assert.deepEqual(signalPhase(5,local),{ns:1,ew:0});
  assert.deepEqual(signalPhase(6.5,local),{ns:0,ew:0});
  assert.deepEqual(signalPhase(6.5,neighbor),{ns:2,ew:0});
  requestCrossing(local,9);
  assert.equal(local.reopenAt,14.5);
  assert.deepEqual(signalPhase(14.49,local),{ns:0,ew:0});
  assert.deepEqual(signalPhase(14.5,local),{ns:0,ew:2});
  assert.deepEqual(signalPhase(23.4,local),{ns:0,ew:2});
  requestCrossing(local,23.8);
  assert.deepEqual(signalPhase(23.8,local),{ns:0,ew:1});
  assert.deepEqual(signalPhase(local.reopenAt,local),{ns:2,ew:0});
});

test('following speed accounts for the pace of the car ahead and leaves a time gap',()=>{
  assert.equal(approachSpeed(0),0);
  assert.equal(approachSpeed(-2),0);
  const clear=approachSpeed(8,6,1.05),stopped=approachSpeed(8,0,1.05);
  assert.ok(clear>stopped+1.2);
  assert.ok(Math.abs(approachSpeed(1.05*7,7,1.05)-7)<1e-8);
});

test('a driver close to the line clears only a yellow long enough to reach the paint',()=>{
  const junction={offset:0,reopenAt:0,amberUntil:0};
  assert.equal(clearsAmber(2,7,21,junction,'ew'),true);
  assert.equal(clearsAmber(14,7,21,junction,'ew'),false);
  assert.equal(clearsAmber(2,7,22.9,junction,'ew'),false);
  assert.equal(clearsAmber(.2,0,21,junction,'ew'),false);
  requestCrossing(junction,14);
  assert.equal(clearsAmber(2,7,14,junction,'ew'),true);
  assert.equal(clearsAmber(2,7,15.45,junction,'ew'),false);
});

test('a walker entering the lane is seen in advance while a parallel or departing walker stays clear',()=>{
  const approaching=pedestrianConflict(14,3,0,-1.8,7,2.5,4.6);
  assert.ok(approaching&&!approaching.occupied&&approaching.gap>12);
  assert.equal(pedestrianConflict(14,3,1.7,0,7,2.5,4.6),null);
  assert.equal(pedestrianConflict(14,3,0,1.8,7,2.5,4.6),null);
  assert.equal(pedestrianConflict(16,0,0,3.5,7,2.5,4.6),null);
  assert.equal(pedestrianConflict(-3,0,0,0,7,2.5,4.6),null);
  assert.equal(pedestrianConflict(2,0,0,0,2,2.5,4.6).occupied,true);
});

test('junction turns signal before entry, slow the approach and cancel once the rear is clear',()=>{
  const track={points:[{x:0,z:0},{x:0,z:30},{x:32,z:30},{x:32,z:0},{x:0,z:0}],
    cumulative:[0,30,62,92,124],length:124};
  const bends=routeBends(track,(x,z)=>x===0&&z===30);
  assert.equal(routeAdvice(bends,124,7,7,8).signal,null);
  assert.equal(routeAdvice(bends,124,17,7,8).signal,'right');
  const inside=routeAdvice(bends,124,30,5,8);
  assert.equal(inside.signal,'right');assert.ok(inside.speed<4);
  assert.equal(routeAdvice(bends,124,36,4,8).signal,null);
  assert.equal(routeAdvice(bends,124,60,4,8).signal,null);
  track.turns=[{start:59,end:64,signal:'right'}];
  const service=routeBends(track,()=>true);
  assert.equal(routeAdvice(service,124,30,5,8).signal,null,'a bend in the same street is not a service turn');
  assert.equal(routeAdvice(service,124,48,7,8).signal,'right');
});
