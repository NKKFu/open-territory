import test from 'node:test';
import assert from 'node:assert/strict';
import {stepCarDynamics,steeringLock,wheelSteeringAngle,integrateCarPose} from '../vehicle-dynamics.js';

function drive(state,input,seconds,dt=1/60){
  for(let time=0;time<seconds-1e-8;time+=dt)state=stepCarDynamics(state,input,Math.min(dt,seconds-time));
  return state;
}

test('accelerates forward, brakes and engages reverse while S is held',()=>{
  const forward=drive({}, {throttle:1}, 3);
  assert.ok(forward.speed*3.6>39&&forward.speed*3.6<47,'a city sedan takes over three seconds to reach 50 km/h');
  const reverse=drive(forward,{throttle:-1},4);
  assert.ok(reverse.speed< -5);
  assert.equal(reverse.gear,-1);
});

test("steers right and left relative to the driver's view, including in reverse",()=>{
  const right=drive({speed:8},{throttle:1,steer:1},.4);
  const left=drive({speed:8},{throttle:1,steer:-1},.4);
  const backwards=drive({speed:-8},{throttle:-1,steer:1},.4);
  assert.ok(right.steering<0&&right.yawRate<0);
  assert.ok(left.steering>0&&left.yawRate>0);
  assert.ok(backwards.steering<0&&backwards.yawRate>0);
});

test('tire grip changes acceleration and braking without changing controls',()=>{
  const road=drive({}, {throttle:1,grip:1},2),grass=drive({}, {throttle:1,grip:.68},2);
  assert.ok(road.speed>grass.speed*1.20);
  const stopped=drive({speed:10},{brake:true,grip:1},1);
  assert.ok(Math.abs(stopped.speed)<.03);
  assert.ok(drive({speed:10},{brake:true,grip:.68},1).speed>1,'loose ground extends the stopping distance');
});

test('acceleration is stable across common frame rates',()=>{
  const fine=drive({}, {throttle:1,steer:.3},3,1/120);
  const coarse=drive({}, {throttle:1,steer:.3},3,1/30);
  assert.ok(Math.abs(fine.speed-coarse.speed)<.2);
  assert.ok(Math.abs(fine.yawRate-coarse.yawRate)<.03);
});

test('at speed, braking and loose ground reduce the cornering grip',()=>{
  function turn(grip,brake){
    return stepCarDynamics({speed:27,steering:-.28,yawRate:0},{steer:1,grip,brake},.025);
  }
  const road=turn(1,false),grass=turn(.65,false),braked=turn(1,true);
  assert.ok(Math.abs(road.yawRate)>Math.abs(grass.yawRate));
  assert.ok(Math.abs(road.yawRate)>Math.abs(braked.yawRate));
  const settled=drive({speed:22,yawRate:-.7,lateral:2},{steer:0},1.3);
  assert.ok(Math.abs(settled.yawRate)<.005&&Math.abs(settled.lateral)<.01);
});

test('the rear axle rolls around the parking turn and the inner front wheel takes a tighter angle',()=>{
  const wheelbase=3,rearOffset=1.5;
  const car=drive({speed:1.8},{steer:-1,wheelbase,rearOffset},.7);
  assert.ok(Math.abs(car.lateral-car.yawRate*rearOffset)<.03,'rear axle should not skate sideways');
  assert.ok(Math.abs(car.yawRate-car.speed*Math.tan(car.steering)/wheelbase)<.025);
  const inside=wheelSteeringAngle(car.steering,wheelbase,1.22),outside=wheelSteeringAngle(car.steering,wheelbase,-1.22);
  assert.ok(inside>car.steering&&outside<car.steering);
  assert.ok(Math.abs(wheelbase/Math.tan(outside)-wheelbase/Math.tan(inside)-2.44)<1e-8);
  assert.ok(steeringLock(24)<steeringLock(3)*.25);
});

test('holding reverse first stops the wheels, then engages after a short continuous pause',()=>{
  let car={speed:7},stopped=null,reversed=null;
  for(let frame=0;frame<180;frame++){
    car=stepCarDynamics(car,{throttle:-1},1/120);
    if(car.speed===0&&stopped===null)stopped=frame/120;
    if(car.speed<-.01&&reversed===null)reversed=frame/120;
  }
  assert.ok(stopped!==null&&reversed-stopped>=.22&&reversed-stopped<.40);
  const tap=drive({speed:0,driveDirection:1},{throttle:-1},.12);
  assert.equal(tap.speed,0);
  assert.equal(drive(tap,{},.25).driveDirection,1);
});

test('front and rear tire grip change the turn differently; the handbrake releases rear grip',()=>{
  const turn=options=>drive({speed:14},{steer:1,...options},.55);
  const road=turn({}),frontLoose=turn({frontGrip:.5}),rearLoose=turn({rearGrip:.5}),slide=turn({handbrake:true});
  assert.ok(Math.abs(frontLoose.yawRate)<Math.abs(road.yawRate)*.75);
  assert.ok(Math.abs(rearLoose.lateral)>Math.abs(road.lateral)+.7);
  assert.ok(slide.speed<road.speed&&slide.tireSlip>road.tireSlip*2);
  const recovered=drive(slide,{},1.8);
  assert.ok(recovered.handbrakeLevel<.001&&Math.abs(recovered.lateral)<.15);
});

test('integrating a constant steering arc gives the same pose in one or many steps',()=>{
  const yaw=.73,speed=8,lateral=.7,rate=-.54;
  const once=integrateCarPose(yaw,speed,lateral,rate,1);
  let x=0,z=0,h=yaw;
  for(let i=0;i<60;i++){
    const part=integrateCarPose(h,speed,lateral,rate,1/60);
    x+=part.dx;z+=part.dz;h=part.heading;
  }
  assert.ok(Math.hypot(x-once.dx,z-once.dz)<1e-10);
  assert.ok(Math.abs(h-once.heading)<1e-10);
});

test('the service brake holds a stopped car on a slope and loaded vehicles accelerate more slowly',()=>{
  assert.equal(drive({speed:0},{brake:true,pitch:.12},2).speed,0);
  assert.ok(drive({speed:0},{pitch:.12},2).speed>.4);
  const sedan=drive({},{throttle:1},2);
  const pickup=drive({},{throttle:1,massScale:1.31,driveScale:.86},2);
  assert.ok(pickup.speed<sedan.speed-1);
});
