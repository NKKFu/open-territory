import test from 'node:test';
import assert from 'node:assert/strict';
import {createPoliceRoute} from '../police-road.js';
import {stepTrafficDrive} from '../traffic-driving.js';

test('a patrol changes streets toward a dispatch using ordinary steering and lane-sized junction turns',()=>{
  const route=createPoliceRoute((_x,z)=>z),dt=1/60,goal={x:-71,z:71};
  let car={x:50,z:3.3,heading:-Math.PI/2,speed:4},nearest=Infinity,indicated=false;
  for(let i=0;i<3600;i++){
    const advised=route.update(car,car.speed,goal,i*dt);
    const motion=stepTrafficDrive(car,{target:advised.aim,speed:advised.limit},dt);
    car={...motion,x:car.x+motion.dx,z:car.z+motion.dz};
    const xGap=Math.min(...[-130,-72,0,72,130].map(x=>Math.abs(car.x-x)));
    const zGap=Math.min(...[-131,-72,0,72].map(z=>Math.abs(car.z-z)));
    assert.ok(Math.min(xGap,zGap)<7.2,'remain in a street or its junction');
    assert.ok(Math.hypot(motion.dx,motion.dz)<.22);
    nearest=Math.min(nearest,Math.hypot(car.x-goal.x,car.z-goal.z));indicated||=!!advised.signal;
  }
  assert.ok(indicated&&nearest<11);
});
