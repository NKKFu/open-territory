import test from 'node:test';
import assert from 'node:assert/strict';
import {createStreetSet} from '../street-performance.js';

test('street set stops through danger or a downpour, then resumes without catching up missed notes',()=>{
  const set=createStreetSet();
  let notes=0;
  for(let i=0;i<80;i++)if(set.update(.025).strum)notes++;
  assert.ok(notes>=6&&notes<=8);
  const before=set.snapshot().elapsed;
  assert.equal(set.update(12,{interrupted:true}).playing,false);
  assert.equal(set.snapshot().elapsed,before);
  assert.equal(set.update(5,{rain:.8}).strum,false);
  const resumed=set.update(.025,{rain:.2});
  assert.equal(resumed.playing,true);assert.equal(resumed.strum,false);
  assert.equal(set.snapshot().elapsed,before+.025);
});

test('applause changes an original phrase with a cooldown and is unavailable while interrupted',()=>{
  const set=createStreetSet();
  assert.equal(set.applaud(1),false);
  set.update(.3);assert.equal(set.applaud(2),true);
  assert.equal(set.applaud(2.1),false);assert.equal(set.snapshot().encores,1);
  assert.equal(set.snapshot().phrase,1);
  set.update(.1,{interrupted:true});assert.equal(set.applaud(6),false);
  set.update(.1);assert.equal(set.applaud(7),true);
  assert.equal(set.snapshot().phrase,2);
});
