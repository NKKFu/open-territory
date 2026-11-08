import test from 'node:test';
import assert from 'node:assert/strict';
import {planParkour,parkourPosition} from '../parkour.js';

const ground=()=>.08,forward={x:0,z:1};
const wall={x:0,z:0,w:6.34,d:.54,top:1.195,yaw:0};
const start={x:0,y:.08,z:-1.15};

test('both feet clear the wall before crossing it, including a jump from a standstill against the wall',()=>{
  for(const z of [-1.15,-.69]){
    const plan=planParkour({...start,z},forward,[wall],ground);
    assert.ok(plan&&!plan.climb);
    for(let n=0;n<=100;n++){
      const p=parkourPosition(plan,n/100,{});
      if(Math.abs(p.z)<wall.d/2+.42)assert.ok(p.y>=wall.top-1e-8);
    }
    const end=parkourPosition(plan,1,{});
    assert.ok(end.z>wall.d/2+.42);
    assert.ok(Math.abs(end.y-ground())<1e-8);
  }
});

test('a rotated hood can be vaulted even where only the edge of the shoe overlaps the raised panel',()=>{
  const car=[
    {x:0,z:0,w:2.5,d:4.6,top:1.14,yaw:Math.PI/2},
    {x:-.15,z:0,w:2.32,d:3.8,top:1.43,yaw:Math.PI/2},
    {x:-.30,z:0,w:2.05,d:2.35,top:2.39,yaw:Math.PI/2}
  ];
  const plan=planParkour({x:1.78,y:.08,z:-2.2},forward,car,ground);
  assert.ok(plan&&!plan.climb);
  assert.equal(plan.top,1.43);
  assert.ok(plan.hands.every(h=>h.x<=1.75));
  assert.ok(parkourPosition(plan,1,{}).z>1.25+.42);
  const roof=planParkour({x:0,y:.08,z:-2.2},forward,car,ground);
  assert.ok(roof.climb);
  const end=parkourPosition(roof,1,{});
  assert.equal(end.y,2.39);
  assert.ok(Math.abs(end.z)<2.05/2-.20);
});

test('distant, tall and merely grazed walls do not trigger parkour; a drop behind a wall needs a safe landing',()=>{
  assert.equal(planParkour({...start,z:-4},forward,[wall],ground),null);
  assert.equal(planParkour(start,forward,[{...wall,top:4}],ground),null);
  assert.equal(planParkour({...start,x:wall.w/2+.2},forward,[wall],ground),null);
  assert.equal(planParkour(start,forward,[wall],()=>-2),null);
});
