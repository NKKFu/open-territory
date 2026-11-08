import test from 'node:test';
import assert from 'node:assert/strict';
import {footprint,rectangle,cutPavement,overlapFootprints} from '../street-pavement.js';

function area({points}){
  return Math.abs(points.reduce((sum,a,i)=>{
    const b=points[(i+1)%points.length];return sum+a.x*b.z-a.z*b.x;
  },0))/2;
}

test('a diagonal road cuts the tiled corner up to the actual lane edge',()=>{
  const corner=rectangle(-139,0,4,22);
  const sideStreet=rectangle(-143,3,33,11,-.23);
  const removed=overlapFootprints(corner,sideStreet),pieces=cutPavement(corner,[sideStreet]);
  assert.ok(area(removed)>0&&pieces.length>=2);
  assert.ok(Math.abs(pieces.reduce((sum,p)=>sum+area(p),area(removed))-area(corner))<1e-6);
  for(const piece of pieces)assert.equal(overlapFootprints(piece,sideStreet),null);
});

test('at a junction the crosswalk fits on the union of roads and cannot be drawn twice',()=>{
  const main=rectangle(0,0,14,70),branch=rectangle(11,1,25,11,-.27);
  const stripe=rectangle(7,0,3,1,-.27);
  const a=overlapFootprints(stripe,main),b=overlapFootprints(stripe,branch);
  assert.ok(a&&b);
  const fresh=cutPavement(b,[a]);
  assert.ok(Math.abs(area(a)+fresh.reduce((sum,p)=>sum+area(p),0)-area(stripe))<1e-7);
  for(const piece of fresh)assert.equal(overlapFootprints(piece,a),null);
});

test('shared edges leave adjacent tiles intact, independent of polygon winding',()=>{
  const a=rectangle(0,0,4,4),b=rectangle(4,0,4,4);
  assert.deepEqual(cutPavement(a,[footprint([...b.points].reverse())]),[a]);
  assert.equal(overlapFootprints(a,b),null);
  assert.deepEqual(cutPavement(a,[a]),[]);
});
