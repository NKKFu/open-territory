import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createNpcLife} from '../npc-life.js';

function person(name,x=0){
  const g=new THREE.Group();g.position.x=x;
  g.userData.limbs=Array.from({length:4},()=>new THREE.Group());
  g.userData.limbs.forEach(limb=>g.add(limb));
  return {name,g};
}
function setup(people,locations,extra={}){
  let talking=false,calls=0;
  const life=createNpcLife({THREE,scene:new THREE.Scene(),people,player:{position:new THREE.Vector3()},
    groundHeight:()=>0,locations,random:()=>.4,isTalking:()=>talking,
    moveTo(p,goal,dt){calls++;const delta=new THREE.Vector3(goal.x,0,goal.z).sub(p.g.position);
      const distance=delta.length();p.g.position.addScaledVector(delta,distance?Math.min(1,dt/distance):0);
      return distance<.1;},...extra});
  return {life,talk(value){talking=value;},calls:()=>calls};
}

test('reserves separate slots and never exceeds a gathering capacity',()=>{
  const people=Array.from({length:8},(_,i)=>person(`Person ${i}`,i));
  const {life}=setup(people,[{id:'square',x:10,z:0,activity:'chat',capacity:3}]);
  const booked=life.snapshot().filter(a=>a.destination==='square');
  assert.equal(booked.length,3);
  assert.equal(new Set(booked.map(a=>a.slot)).size,3);
  assert.equal(life.snapshot().filter(a=>a.state==='idle').length,5);
  // Scheduling only chooses destinations; it never teleports residents to them.
  people.forEach((p,i)=>assert.equal(p.g.position.x,i));
});

test('conversation suspends navigation and resumes the same destination afterwards',()=>{
  const resident=person('Resident');
  const ctx=setup([resident],[{id:'cafe',x:10,z:0,activity:'eat',capacity:1}]);
  ctx.life.update(1,1);const x=resident.g.position.x;
  ctx.talk(true);ctx.life.update(2,3);
  assert.equal(resident.g.position.x,x);assert.equal(ctx.calls(),1);
  ctx.talk(false);ctx.life.update(1,4);
  assert.ok(resident.g.position.x>x);assert.equal(ctx.life.snapshot()[0].destination,'cafe');
});

test('finishing an activity releases its slot for another resident',()=>{
  const ctx=setup([person('A'),person('B',2)],[
    {id:'phone-a',x:0,z:0,activity:'phone',capacity:1,duration:.1},
    {id:'phone-b',x:4,z:0,activity:'phone',capacity:1,duration:.1}]);
  const visits=new Map([['A',new Set()],['B',new Set()]]);
  for(let time=0;time<20;time+=.1){
    ctx.life.update(.1,time);
    for(const actor of ctx.life.snapshot())if(actor.state==='activity')visits.get(actor.name).add(actor.destination);
  }
  const state=ctx.life.snapshot();
  assert.ok(state.every(a=>Number.isFinite(a.x)&&Number.isFinite(a.z)));
  assert.equal(new Set(state.filter(a=>a.destination).map(a=>`${a.destination}:${a.slot}`)).size,
    state.filter(a=>a.destination).length);
  assert.equal(visits.get('A').size,2);
  assert.equal(visits.get('B').size,2);
});

test('children are never assigned smoking activities',()=>{
  const child=person('Child');child.child=true;
  const {life}=setup([child],[{id:'smoking',x:0,z:0,activity:'smoke',capacity:1}]);
  for(let time=0;time<20;time++)life.update(1,time);
  assert.equal(life.snapshot()[0].activity,null);
});
