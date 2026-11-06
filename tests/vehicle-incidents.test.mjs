import test from 'node:test';
import assert from 'node:assert/strict';
import {createVehicleIncidents} from '../vehicle-incidents.js';

function setup(){
  let blocked=false,talking=false,created=0,removed=0;
  const car={auto:true,velocity:7,driver:{visible:true},brake:{}};
  const controller=createVehicleIncidents({
    findExit:()=>blocked?null:{side:-1,seat:{x:0},exit:{x:-2},inspection:[{x:-3},{x:-4}]},
    makeActor:(_car,seat)=>{created++;return {x:seat.x};},removeActor:()=>removed++,
    moveActor(actor,goal,dt){const dx=goal.x-actor.x;actor.x+=Math.sign(dx)*Math.min(Math.abs(dx),dt*2);return Math.abs(actor.x-goal.x)<.01;},
    isTalking:()=>talking
  });
  return {car,controller,block:v=>blocked=v,talk:v=>talking=v,counts:()=>({created,removed})};
}

test('a collision creates one driver who walks out, inspects and returns before traffic resumes',()=>{
  const c=setup();assert.equal(c.controller.begin(c.car,6,0),true);
  assert.equal(c.controller.begin(c.car,9,0),false);
  const stages=new Set();let lastX=0;
  for(let time=0;time<25;time+=.05){
    c.controller.update(c.car,.05,time);
    if(c.car.incident){
      stages.add(c.car.incident.state);assert.equal(c.car.velocity,0);
      const actor=c.car.incident.actor;
      if(actor){assert.ok(Math.abs(actor.x-lastX)<=.101);lastX=actor.x;assert.equal(c.car.driver.visible,false);}
    }
  }
  for(const state of ['exiting','inspecting','returning','entering','closing'])assert.ok(stages.has(state));
  assert.deepEqual(c.counts(),{created:1,removed:1});
  assert.equal(c.car.incident,null);assert.equal(c.car.driver.visible,true);assert.equal(c.car.hazards,false);
  assert.equal(c.controller.begin(c.car,2,30),false);
});

test('blocked exits keep the driver seated and conversations suspend the inspection',()=>{
  const c=setup();c.block(true);c.controller.begin(c.car,6,0);
  for(let t=0;t<5;t+=.1)c.controller.update(c.car,.1,t);
  assert.equal(c.car.incident.state,'waiting');assert.equal(c.counts().created,0);
  c.block(false);
  for(let t=5;t<10;t+=.1)c.controller.update(c.car,.1,t);
  c.talk(true);const state=c.car.incident.state,elapsed=c.car.incident.elapsed,x=c.car.incident.actor.x;
  c.controller.update(c.car,5,15);
  assert.equal(c.car.incident.state,state);assert.equal(c.car.incident.elapsed,elapsed);assert.equal(c.car.incident.actor.x,x);
  c.talk(false);
  for(let t=15;t<35;t+=.1)c.controller.update(c.car,.1,t);
  assert.equal(c.car.incident,null);assert.deepEqual(c.counts(),{created:1,removed:1});
});
