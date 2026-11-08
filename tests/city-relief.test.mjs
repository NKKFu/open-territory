import test from 'node:test';
import assert from 'node:assert/strict';
import {cityElevation,gradedBetween,addGradedFace} from '../city-relief.js';

test('the waterfront, building lots and hilltop pond are level between street climbs',()=>{
  for(const [start,end] of [[-180,-96],[-53,48],[92,117],[157,214]]){
    assert.equal(gradedBetween(start,end),false);
    assert.equal(cityElevation(start),cityElevation(end));
  }
  assert.equal(cityElevation(-150),0);
  assert.equal(cityElevation(104),4.30);
  assert.equal(cityElevation(164),8.80);
  assert.ok(gradedBetween(-95,-52),'the district road runs up and down across its ridge');
  for(let z=-120;z<=210;z+=.25)assert.ok(Math.abs(cityElevation(z+.25)-cityElevation(z))<=.25*.17);
});

test('a long road, an adjacent curb and their paint interpolate the same grade inside every triangle',()=>{
  const road=[],mark=[],curb=[];
  function panel(x0,x1,height){
    return [[x0,42],[x1,42],[x1,161],[x0,161]].map(([x,z])=>({x,y:height,z}));
  }
  addGradedFace(road,panel(-5,5,.06));addGradedFace(mark,panel(1,1.16,.065));
  addGradedFace(curb,panel(5,6,.23));
  function interpolated(buffer,x,z){
    for(let i=0;i<buffer.length;i+=9){
      const [a,ay,b,c,cy,d,e,ey,f]=buffer.slice(i,i+9);
      const den=(d-f)*(a-e)+(e-c)*(b-f);if(Math.abs(den)<1e-9)continue;
      const u=((d-f)*(x-e)+(e-c)*(z-f))/den,v=((f-b)*(x-e)+(a-e)*(z-f))/den;
      if(Math.min(u,v,1-u-v)>-1e-8)return u*ay+v*cy+(1-u-v)*ey;
    }
    assert.fail('missing face at '+[x,z]);
  }
  for(let z=42.1;z<161;z+=.61){
    const pavement=interpolated(road,1.08,z);
    assert.ok(Math.abs(pavement-cityElevation(z)-.06)<1e-7);
    assert.ok(Math.abs(interpolated(mark,1.08,z)-pavement-.005)<1e-7);
    assert.ok(Math.abs(interpolated(curb,5,z)-interpolated(road,5,z)-.17)<1e-7);
  }
});
