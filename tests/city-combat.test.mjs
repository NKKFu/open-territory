import test from 'node:test';
import assert from 'node:assert/strict';
import {createCombat,traceShot,coverDistance} from '../city-combat.js';

const body=(x,z)=>({g:{position:{x,y:0,z},scale:{y:1},visible:true}});
const eyes={x:0,y:1.5,z:0},front={x:0,y:0,z:1};
const wall={x:0,z:4,w:3,d:.08,bottom:0,top:3};

test('a thin wall, a rotated car and the ground stop shots before an actor',()=>{
  const victim=body(0,7);
  assert.equal(traceShot(eyes,front,16,[],[victim]).actor,victim);
  const blocked=traceShot(eyes,front,16,[wall],[victim]);assert.equal(blocked.kind,'cover');assert.equal(blocked.actor,null);
  assert.ok(Math.abs(blocked.distance-3.96)<.0001);
  assert.equal(coverDistance({...eyes,z:4},front,wall,2),0);
  const car={...wall,w:2.1,d:4.1,cos:0,sin:1};assert.ok(Math.abs(coverDistance(eyes,front,car,16)-2.95)<.0001);
  const ground=traceShot(eyes,{x:0,y:-.30,z:Math.sqrt(.91)},16,[],[victim],()=>.26);
  assert.equal(ground.kind,'ground');assert(Math.abs(ground.point.y-.26)<.01);
  victim.ragdoll=true;assert.equal(traceShot(eyes,front,16,[],[victim]).kind,'air');
});

test('nonlethal incapacitation preserves health; ballistic damage, protection and recovery share the simulation clock',()=>{
  const player=body(0,0),npc=body(0,1),recovered=[],c=createCombat({player,onRecover:a=>recovered.push(a)});
  assert(c.damage(npc,0,{stun:4}));assert(c.isDown(npc));assert.equal(c.hp(npc),100);assert.equal(c.canTalk(npc),false);
  c.update(3.9);assert(c.isDown(npc));c.update(.12);assert(!c.isDown(npc));assert.deepEqual(recovered,[npc]);
  assert.equal(c.damage(npc,80),false);c.update(1.6);
  assert(c.damage(player,36));assert.equal(c.healAtClinic(),36);assert.equal(c.healAtClinic(),0);
  c.damage(player,100);assert(c.isDown());assert.equal(c.healAtClinic(),0);assert(!c.select('pulse'));
  c.update(7.1);assert(!c.isDown());assert(c.hp()>=54);assert(c.select('pulse'));
});

test('semi-automatic triggers require release; automatic fire and interrupted reloads respect the same clock',()=>{
  const fired=[],c=createCombat({player:body(0,0),onShot:w=>fired.push(w.id)});
  c.grant('sidearm');c.grant('carbine');c.select('sidearm');assert(!c.trigger(true));c.aim(true);
  assert(c.trigger(true));c.update(.4);assert(!c.trigger(true));assert.equal(fired.length,1);
  c.trigger(false);assert(c.trigger(true));c.trigger(false);assert(c.reload());c.update(.5);c.select('carbine');c.update(2);
  assert.equal(c.weaponInfo('sidearm').loaded,6);c.aim(true);assert(c.trigger(true));for(let i=0;i<20;i++)c.update(.05);
  c.trigger(false);assert(fired.filter(v=>v==='carbine').length>=6);const stored=c.weaponInfo('carbine');
  assert(c.reload());c.update(1.86);const now=c.weaponInfo('carbine');assert.equal(now.loaded,16);
  assert.equal(now.reserve+now.loaded,stored.reserve+stored.loaded);
});

test('trade and sanitized persistence conserve ammunition and restore an unarmed player',()=>{
  const c=createCombat({player:body(0,0)});assert(c.owns('pulse'));assert(!c.remove('pulse'));
  assert(c.grant('sidearm'));assert(!c.grant('sidearm'));c.select('sidearm');assert.equal(c.addAmmo('sidearm',1000),48);
  const saved=c.exportLoadout();c.aim(true);c.restoreLoadout(saved);assert.equal(c.equipped,null);assert(!c.isThreatening());
  assert.equal(c.weaponInfo('sidearm').reserve,64);c.select('sidearm');assert(c.remove('sidearm'));assert.equal(c.equipped,null);
  c.restoreLoadout({pulse:{loaded:99,reserve:-2},carbine:{loaded:NaN,reserve:120}});
  assert.deepEqual(c.exportLoadout(),{pulse:{loaded:1,reserve:0},carbine:{loaded:0,reserve:96}});
});
