import test from 'node:test';
import assert from 'node:assert/strict';
import {createBirdLife} from '../bird-life.js';

function bird(x=0,z=0){return {originX:x,originZ:z,g:{rotation:{y:0},position:{x,y:0,z,set(x,y,z){Object.assign(this,{x,y,z});}}}};}
function setup(birds,options={}){
  const life=createBirdLife({birds,groundHeight:()=>.23,canLand:()=>true,canTravel:()=>true,random:()=>.5,...options});
  life.update(10,{x:200,z:200,speed:0});return life;
}
test('running past the flock causes staggered flights without teleporting',()=>{
  const birds=[bird(),bird(2,0)],life=setup(birds);
  life.update(.01,{x:0,z:-3,speed:8});
  assert.ok(birds[0].flight);assert.ok(birds[1].pendingFlight);
  assert.equal(birds[0].g.position.y,0);assert.equal(birds[1].g.position.x,2);
  let previous={...birds[0].g.position};
  for(let t=0;t<1;t+=.025){
    life.update(.025,{x:0,z:-3,speed:8});
    const p=birds[0].g.position;assert.ok(Math.hypot(p.x-previous.x,p.y-previous.y,p.z-previous.z)<.2);previous={...p};
  }
  assert.ok(birds[0].g.position.y>1);assert.ok(birds[1].flight);
  assert.ok(Math.hypot(birds[0].flight.goal.x-birds[1].flight.goal.x,birds[0].flight.goal.z-birds[1].flight.goal.z)>.9);
});
test('walls prevent a flight instead of letting birds cut through props',()=>{
  const b=bird(),life=setup([b],{canTravel:()=>false});
  life.update(.1,{x:0,z:-1,speed:4});assert.equal(b.flight,null);assert.equal(b.g.position.z,0);
});
test('a bird waits above an occupied landing then touches down on the correct floor',()=>{
  let blocked=false;const b=bird(),life=setup([b],{canLand:()=>!blocked});
  life.update(.01,{x:0,z:-1,speed:4});
  for(let t=0;t<1.7;t+=.025)life.update(.025,{x:200,z:200,speed:0});
  blocked=true;
  for(let t=0;t<3;t+=.025)life.update(.025,{x:200,z:200,speed:0});
  assert.ok(b.flight);assert.ok(b.g.position.y>.6);
  blocked=false;
  for(let t=0;t<3;t+=.025)life.update(.025,{x:200,z:200,speed:0});
  assert.equal(b.flight,null);assert.equal(b.g.position.y,.23);
});
test('petting a grounded bird suspends takeoff, including nearby flock reactions',()=>{
  const birds=[bird(),bird(2,0)],life=setup(birds,{isPetting:b=>b===birds[1]});
  life.update(.01,{x:0,z:-1,speed:8});
  assert.ok(birds[0].flight);assert.equal(birds[1].flight,null);assert.ok(!birds[1].pendingFlight);
});
