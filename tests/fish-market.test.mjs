import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createFishMarket} from '../fish-market.js';

function marketFixture(canServe){
  const solids=[],scene=new THREE.Scene(),cameraObstacles=[];
  const solid=(x,z,w,d)=>{const s={x,z,w,d,active:true};solids.push(s);return s;};
  const part=(parent,x,y,z,w,h,d)=>{
    const mesh=new THREE.Mesh(new THREE.BoxGeometry(w,h,d));mesh.position.set(x,y,z);parent.add(mesh);return mesh;
  };
  const human=()=>{const g=new THREE.Group();g.userData.limbs=[0,1,2,3].map(()=>new THREE.Group());return g;};
  const market=createFishMarket({THREE,scene,solids,cameraObstacles,solid,part,human,block(){},sign(){},canServe,groundHeight:()=>.23});
  const at=(x,z)=>solids.filter(s=>Math.abs(x-s.x)<=s.w/2&&Math.abs(z-s.z)<=s.d/2);
  return {market,solids,at};
}

test('fish market crates and hanging net block their actual footprints',()=>{
  const {at}=marketFixture();
  assert.equal(at(163,-117.2).length,1,'the three crates share one stack collider');
  assert.equal(at(163,-117.2)[0].vaultTop,1.98);
  assert.equal(at(157,-116.65).length,1,'the cloth between the posts is physical');
  for(const x of [155.1,157,158.9])assert.ok(at(x,-116.65).length);
  assert.equal(at(157,-117.3).length,0,'clear floor in front of the net stays open');
  for(const x of [157.4,162.6])assert.equal(at(x,-124).length,1,'each activity crate has one collider');
});

test('counter overhangs and low rope have finite support heights, overhead beams leave floor clear',()=>{
  const {at}=marketFixture();
  assert.ok(at(153.3,-120.65).length,'vendor countertop overhang is covered');
  assert.ok(at(161.8,-121.32).length,'balance countertop overhang is covered');
  assert.equal(at(149,-125.7)[0].vaultTop,.535);
  for(const point of [[150,-127],[155,-127],[157,-118],[143,-120]])assert.equal(at(...point).length,0);
});

test('physical crate volumes preserve both balance interactions and vendor conversation',()=>{
  const {market}=marketFixture();
  const reachable=()=>true,speak=()=>{};
  const weights=market.nearest({x:157.4,z:-125.5},reachable,3.4);
  const fish=market.nearest({x:162.6,z:-125.5},reachable,3.4);
  const vendor=market.nearest({x:151,z:-121.8},reachable,3.4);
  assert.equal(weights.data.id,'weights');assert.equal(fish.data.id,'fish');assert.equal(vendor.data.id,'vendor');
  market.interact(weights.data,speak,0);market.interact(fish.data,speak,0);market.interact(vendor.data,speak,0);
  assert.equal(market.snapshot().weightsKg,.5);assert.equal(market.snapshot().fishKg,1.5);assert.equal(market.snapshot().talkCount,1);
});

test('the single fishmonger owns conversation and dispatch; the scales remain operable while he recovers',()=>{
  let open=true,dispatches=0,lines=0;const {market,at}=marketFixture(p=>open&&p===market.worker);
  const nearby=(x,z)=>market.nearest({x,z},()=>true,2);
  const talk=nearby(151,-121.8).data,receipt=nearby(158.58,-121.62).data,weights=nearby(157.4,-125.5).data;
  assert.equal(talk.attendant,market.worker);assert.equal(receipt.attendant,market.worker);
  assert.equal(at(151,-117.9).length,1);assert.equal(at(151,-117.9)[0].bodyOwner,market.worker);
  open=false;assert.equal(nearby(151,-121.8),null);assert.equal(nearby(158.58,-121.62),null);
  const speak=()=>lines++,dispatch=()=>{dispatches++;return {line:'Entregue'};};
  market.interact(talk,speak,1);market.interact(receipt,speak,1,dispatch);assert.equal(lines+dispatches,0);
  market.interact(weights,speak,1);assert.equal(lines,1);
  open=true;market.interact(receipt,speak,5,dispatch);assert.equal(dispatches,1);assert.equal(lines,2);
});
