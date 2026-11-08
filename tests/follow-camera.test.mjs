import test from 'node:test';
import assert from 'node:assert/strict';
import {cameraClearance,createCameraBoom} from '../follow-camera.js';

const pivot={x:0,y:1.5,z:0},forward={x:0,y:0,z:1};
const post={x:0,z:5,w:.18,d:.10,bottom:0,top:6};

test('even a thin obstruction between two formerly sampled distances limits the full arm',()=>{
  assert.ok(Math.abs(cameraClearance(pivot,forward,25,[post])-4.695)<1e-8);
  assert.equal(cameraClearance(pivot,{x:0,y:0,z:-1},25,[post]),25);
  assert.equal(cameraClearance({...pivot,y:7},forward,25,[post]),25);
});

test('a nearby wall permits looking out from its clearance margin, and blocks looking through',()=>{
  const wall={x:-1.1,z:0,w:2,d:8,bottom:0,top:5};
  assert.equal(cameraClearance(pivot,{x:1,y:0,z:0},8,[wall]),8);
  assert.equal(cameraClearance(pivot,{x:-1,y:0,z:0},8,[wall]),0);
});

test('a turned vehicle uses its oriented shell rather than a square bounding box',()=>{
  const estate={x:0,z:5,w:2.2,d:4.6,bottom:.1,top:2.35,cos:0,sin:1};
  assert.ok(Math.abs(cameraClearance(pivot,forward,10,[estate])-3.645)<1e-8);
});

test('the camera stays in front of an obstacle and eases back out after clearing it',()=>{
  const place=createCameraBoom(),eye={};
  const panel={...post,w:4,d:.08};
  place(pivot,0,0,12,3.3,[],1/60,eye);
  const blocked=place(pivot,0,0,12,3.3,[panel],1/60,eye);
  assert.ok(blocked<5&&eye.z<panel.z);
  const clear=place(pivot,0,0,12,3.3,[],1/60,eye);
  assert.ok(clear>blocked&&clear<blocked+.6);
  for(let i=0;i<120;i++)place(pivot,0,0,12,3.3,[],1/60,eye);
  assert.ok(eye.z>11.99);
});

test('a post can pass in the foreground while a nearby lens still stops in front of it',()=>{
  const place=createCameraBoom(),eye={};
  for(let i=-36;i<=36;i++){
    const origin={...pivot,x:i/24};
    assert.equal(place(origin,0,0,9,3.3,[post],1/60,eye),9);
    assert.deepEqual(eye,{x:origin.x,y:origin.y,z:9});
  }
  // A zoom starting on the near side may not pass the lens through the pole.
  const zoom=createCameraBoom();
  zoom(pivot,0,0,4,3.3,[post],1/60,eye);
  for(let i=0;i<90;i++){
    zoom(pivot,0,0,Math.min(8,4+i*.08),3.3,[post],1/60,eye);
    assert.ok(eye.z<4.74);
  }
  for(let i=0;i<150;i++)zoom(pivot,Math.PI/4,0,8,3.3,[post],1/60,eye);
  assert.ok(Math.abs(Math.hypot(eye.x,eye.z)-8)<.001);
});

test('a parallel wall never alters the requested orbit or zoom',()=>{
  const sideWall={x:-1.15,z:5,w:.14,d:24,bottom:0,top:7};
  const place=createCameraBoom(),eye={},pitch=.25;
  for(let i=0;i<180;i++){
    const origin={...pivot,z:i*.07},distance=i<60?6:i<120?10.25:4.5;
    assert.equal(place(origin,0,pitch,distance,3.3,[sideWall],1/60,eye),distance);
    assert.equal(eye.x,origin.x);
    assert.ok(Math.abs(eye.y-origin.y-Math.sin(pitch)*distance)<1e-8);
  }
});

function checkArm(place,origin,yaw,pitch,distance,boxes,dt){
  const eye={},length=place(origin,yaw,pitch,distance,3.3,boxes,dt,eye);
  const direction={x:(eye.x-origin.x)/length,y:(eye.y-origin.y)/length,z:(eye.z-origin.z)/length};
  assert.ok(length>0&&length<=distance+1e-8);
  assert.ok(cameraClearance(origin,direction,length,boxes,.18)>=length-1e-8,'the near plane must stay in free space');
  return {length,eye};
}

test('walking past a thin door panel retracts progressively at different frame rates',()=>{
  const door={x:0,z:4,w:3.04,d:.16,bottom:0,top:3.3};
  for(const fps of [30,60,120]){
    const place=createCameraBoom();
    let last=9,minimum=9,maximumChange=0;
    for(let i=0;i<4*fps;i++){
      const origin={...pivot,x:-4+Math.min(8,Math.max(0,i/fps-.3)*4.9)};
      const {length}=checkArm(place,origin,0,.21,9,[door],1/fps);
      minimum=Math.min(minimum,length);maximumChange=Math.max(maximumChange,Math.abs(length-last));last=length;
    }
    assert.ok(minimum>3.6&&minimum<3.9);
    assert.ok(maximumChange*fps<26,'a passing door should not force a sudden cut');
    assert.ok(last>8.99);
  }
});

test('an approaching sliding door is anticipated even when the player stands still',()=>{
  const place=createCameraBoom(),door={x:4,z:4,w:3.04,d:.16,bottom:0,top:3.3,motionKey:{}};
  let last=9,minimum=9,maximumChange=0;
  for(let i=0;i<100;i++){
    door.x=4-Math.min(4,Math.max(0,i-10)*.07);
    const {length}=checkArm(place,pivot,0,.21,9,[{...door}],1/60);
    minimum=Math.min(minimum,length);maximumChange=Math.max(maximumChange,Math.abs(length-last));last=length;
  }
  assert.ok(minimum>3.6&&minimum<3.9);
  assert.ok(maximumChange<.48);
});

test('walking toward a closed door leaves a clear camera outside the room alone',()=>{
  const leaf={x:0,z:0,w:3.04,d:.16,bottom:0,top:3.3,motionKey:{}};
  const place=createCameraBoom(),eye={};
  for(let i=0;i<100;i++){
    const origin={x:0,y:1.44,z:4-Math.min(3.39,i*5/60)};
    const length=place(origin,0,.23,7,3.3,[leaf],1/60,eye);
    assert.equal(length,7);assert.equal(eye.x,0);
  }
});

test('small movements beside an L-shaped corner keep the same clear viewing side',()=>{
  const boxes=[{x:.46,z:1.69,w:1.74,d:.18,bottom:0,top:5},
    {x:-.41,z:1.02,w:.18,d:1.58,bottom:0,top:5}];
  const place=createCameraBoom();let last=null;
  for(let i=0;i<280;i++){
    const origin={x:.13*Math.sin(i/9),y:1.5,z:.11*Math.sin(i/11+.8)};
    const {eye}=checkArm(place,origin,0,.19,7,boxes,1/60);
    const angle=Math.atan2(eye.x-origin.x,eye.z-origin.z);
    if(i>65){
      assert.ok(angle>.55,'keep the original side as the body brushes the corner');
      assert.ok(Math.abs(angle-last)<.13,'avoid a sudden reversal near the corner');
    }
    last=angle;
  }
});

test('the lens can follow the aisle beside the bookshop shelves with roof and walls in place',()=>{
  const shelves=[29,36,43].map(x=>({x,z:-44,w:4,d:1.08,bottom:.18,top:3.28}));
  const room=[...shelves,{x:36,z:-36,w:24,d:20,bottom:5,top:5.4},
    {x:36,z:-46.5,w:23,d:.4,bottom:0,top:5.1}];
  const place=createCameraBoom();
  let last=7.4,minimum=7.4,maximumChange=0;
  for(let i=0;i<220;i++){
    const origin={x:32+Math.min(8.8,Math.max(0,i-40)*5.05/60),y:1.62,z:-45.52};
    const {length,eye}=checkArm(place,origin,0,.43,7.4,room,1/60);
    assert.ok(eye.y<4.83);
    minimum=Math.min(minimum,length);maximumChange=Math.max(maximumChange,Math.abs(length-last));last=length;
  }
  assert.ok(minimum>2.7,'maintain space for the character along the corridor');
  assert.ok(maximumChange<.5,'do not cut from a long shot to the near side of a shelf');
});
