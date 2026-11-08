import test from 'node:test';
import assert from 'node:assert/strict';
import {traceShot} from '../city-combat.js';
import {authorizedPractice} from '../practice-range.js';
const floor=4.53,feet={x:54,y:floor,z:107.9},origin={...feet,y:floor+1.8},dir={x:0,y:0,z:-1};
const wall={x:54,z:96.82,w:7.1,d:.82,bottom:floor,top:floor+4.14,rangeBackstop:true};
const shoot=(cover,actors=[])=>traceShot(origin,dir,40,cover,actors,()=>floor);
test('a live shot must finish on the front of the actual backstop from a grounded bay',()=>{
  const hit=shoot([wall]);assert.ok(authorizedPractice(feet,hit,floor));
  for(const position of [{...feet,z:114},{...feet,x:50},{...feet,y:floor+2.2}])assert.equal(authorizedPractice(position,hit,floor),false);
  assert.equal(authorizedPractice(feet,{...hit,point:{...hit.point,z:wall.z-.41}},floor),false);
  assert.equal(authorizedPractice(feet,shoot([]),floor),false);
  assert.equal(authorizedPractice(feet,shoot([{...wall,rangeBackstop:false}]),floor),false);
});
test('a person or vehicle between the gun and target never becomes a training hit',()=>{
  const target={g:{visible:true,position:{x:54,y:floor,z:100}}};
  const hit=shoot([wall],[target]);assert.equal(hit.actor,target);assert.equal(hit.cover,null);
  assert.equal(authorizedPractice(feet,hit,floor),false);
  const car={...wall,z:101,d:4.6,w:2,rangeBackstop:false};assert.equal(shoot([car,wall]).cover,car);
  assert.equal(authorizedPractice(feet,shoot([car,wall]),floor),false);
});
