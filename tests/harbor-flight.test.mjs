import assert from 'node:assert/strict';
import test from 'node:test';
import * as T from '../vendor/three.module.js';
import {createHarborLift} from '../harbor-district.js';
import {createSurveyDrone,droneFits} from '../survey-drone.js';

test('a suspended crate stops before overlapping someone below it and resumes after the crossing',()=>{
  let occupant=null;
  const lift=createHarborLift((_box,bottom,top)=>occupant!==null&&occupant>=bottom&&occupant<=top);
  lift.toggle();for(let i=0;i<200;i++)lift.update(1/60);
  assert.equal(lift.snapshot().position,1);
  occupant=2.48;lift.toggle();for(let i=0;i<180;i++)lift.update(1/60);
  assert.ok(lift.snapshot().waiting);assert.ok(lift.height>occupant&&lift.height<occupant+.05);
  occupant=null;for(let i=0;i<180;i++)lift.update(1/60);
  assert.deepEqual(lift.snapshot(),{position:0,destination:0,waiting:false});
});

test('the craft can pass under a raised object and along a rotated wall without passing through it',()=>{
  const wall={x:0,z:-1,w:4,d:.20,cos:Math.SQRT1_2,sin:Math.SQRT1_2,bottom:1,top:3};
  assert.ok(droneFits({x:0,y:.72,z:-1},[wall]));
  assert.equal(droneFits({x:1,y:1.5,z:-2},[wall]),false);
  assert.ok(droneFits({x:1,y:1.5,z:.3},[wall]));
});

function setup(boxes=[]){
  const scene=new T.Scene(),body=new T.Vector3();
  const drone=createSurveyDrone({THREE:T,scene,groundHeight:()=>.02,obstacles:()=>boxes,part(parent,x,y,z){
    const p=new T.Object3D();p.position.set(x,y,z);parent.add(p);return p;
  }});
  const nothing={forward:0,sideways:0,lift:0},wind={windSpeed:0,windX:0,windZ:0};
  const drive=(frames,input={})=>{for(let i=0;i<frames;i++)drone.update(1/60,{...nothing,...input},body,wind);};
  assert.equal(drone.launch(body,0),true);return {drone,drive};
}
test('the drone stops at a facade, flies through an opening and can retrace its way to the body',()=>{
  const {drone,drive}=setup([{x:0,z:-4,w:4,d:.28,bottom:0,top:5}]);
  drive(90,{forward:1});assert.ok(drone.position.z> -3.5);
  drive(42,{sideways:1});drive(80,{forward:1});assert.ok(drone.position.z<-6);
  assert.ok(drone.recall());assert.equal(drone.active,false);
  drive(420);assert.equal(drone.mode,'stowed');
});
test('radio range and ceiling are enforced even while accelerating diagonally',()=>{
  const {drone,drive}=setup();
  drive(1000,{forward:1,sideways:1,lift:1});
  assert.ok(drone.position.length()<=48.001);assert.ok(drone.position.y<23.001);
  drive(1000,{lift:-1});assert.ok(drone.position.y>=.32-.0001);
});
