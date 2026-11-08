import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../vendor/three.module.js';
import {createMarine,seaBounds,seaLevel,seabed,waveHeight} from '../marine.js';

function bay(storm=0){
  const player=new T.Group(),wind={windX:0,windZ:0,seaState:storm},sounds=[];
  const sea=createMarine({player,buildBoat:()=>new T.Group(),weather:()=>wind,sound:s=>sounds.push(s)});
  sea.addDock({x:0,z:-163,w:6,d:32,top:.35});sea.addLadder({x:-3.5,z:-173,landX:-2.12,landZ:-173,top:.35});
  const boat=sea.addBoat({name:'Brisa',model:'launch',x:4.72,z:-173,heading:Math.PI});let now=0;
  function step(seconds,input={}){for(let i=0;i<Math.round(seconds*60);i++){now+=1/60;sea.update(1/60,now,input);}}
  function board(){player.position.set(2.18,.35,-173);assert.equal(sea.nearestAction()?.type,'marine-board');assert(sea.interact(sea.nearestAction()));}
  return {sea,player,boat,step,board,wind,sounds};
}

test('bathymetry, floating and the shallow exit agree across calm and rough weather',()=>{
  assert(seabed(0,-169)>seabed(0,-178));assert.equal(waveHeight(0,-165,1,1),seaLevel);
  for(const storm of [0,1]){const {sea,player,step,sounds}=bay(storm);player.position.set(-24,-.9,-169.9);step(2);
    assert.equal(sea.mode,'swim');assert(Math.abs(player.position.y-(sea.surface(-24,-169.9)-1.63))<.18);
    step(3,{moveZ:1});assert.equal(sea.mode,'land');assert.equal(sea.snapshot().lastExit,'beach');assert(sounds.includes('splash'));}
});

test('a swimmer cannot pass under the dock and can climb continuously onto its dry deck',()=>{
  const {sea,player,step}=bay();player.position.set(-4.28,-1.65,-173);step(.2);step(1.4,{moveX:1});
  assert(player.position.x< -3.38);const action=sea.nearestAction();assert.equal(action.type,'marine-ladder');sea.interact(action);
  step(.3);assert(sea.climb&&player.position.y<.35&&player.position.x< -2.12);step(.55);
  assert.equal(sea.mode,'land');assert.equal(player.position.y,.35);assert.equal(sea.snapshot().lastExit,'ladder');
});

test('mooring, acceleration, steering, water exit, and climbing back aboard preserve the same hull',()=>{
  const {sea,board,step,boat}=bay();board();assert.equal(sea.activeBoat,boat);step(3,{forward:1});
  assert(boat.speed>8&&boat.g.position.z< -189&&sea.wake.width>2);
  step(.55,{forward:1,sideways:1});assert(boat.heading<Math.PI-.12);
  assert(sea.interact(sea.nearestAction()));assert(boat.exitRequested);step(3);
  assert.equal(sea.mode,'swim');const current=[boat.g.position.x,boat.g.position.z];step(.5);assert.deepEqual([boat.g.position.x,boat.g.position.z],current);
  assert(sea.interact(sea.nearestAction()));assert.equal(sea.activeBoat,boat);
  const tied=bay();tied.board();tied.sea.interact(tied.sea.nearestAction());assert.equal(tied.sea.mode,'land');assert(tied.sea.dockAt(tied.player.position.x,tied.player.position.z));
});

test('full rotated hulls cannot enter the beach, pier, other craft, buoys, or the chart boundary',()=>{
  const {sea,boat,board,step,player}=bay();const other=sea.addBoat({name:'Estrela',model:'fishing',x:4.72,z:-191,heading:1});
  assert(!sea.hullClear(boat,other.g.position.x,other.g.position.z,.9));assert(!sea.hullClear(boat,2.4,-173,.3));
  assert(!sea.hullClear(boat,-28,-166,0));assert(!sea.hullClear(boat,-24,seaBounds.outer+.6,Math.PI));
  sea.addBuoy(-30,-185,.5);assert(!sea.hullClear(boat,-30,-184,.45));board();step(6,{forward:1});
  assert(boat.g.position.z>other.g.position.z+3&&sea.snapshot().collisions>0);assert(Number.isFinite(player.position.y));
  sea.interact(sea.nearestAction());step(3);assert.equal(sea.mode,'swim');player.position.set(-55,-1.55,seaBounds.outer+1);step(3,{moveZ:-1});assert(player.position.z>seaBounds.outer+.4);
});
