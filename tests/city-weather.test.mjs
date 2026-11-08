import test from 'node:test';
import assert from 'node:assert/strict';
import {createCityWeather,createRainCover,wetRoadGrip} from '../city-weather.js';
import {stepCarDynamics} from '../vehicle-dynamics.js';

test('fronts change gradually on simulation time and the quay forecasts their actual order',()=>{
  const w=createCityWeather(),s=w.state;
  assert.equal(s.id,'clear');assert.equal(w.forecast().next,'Bruma marítima');
  w.update(78);assert.equal(s.id,'mist');assert.equal(s.fog,.035);
  w.update(10);assert.ok(s.fog>.36&&s.fog<.57);
  w.update(10);assert.equal(s.fog,.91);
  const before={...s};assert.equal(w.request('squall'),true);assert.equal(s.rain,before.rain);
  w.update(7);assert.ok(s.rain>.45&&s.rain<.55);assert.ok(s.windSpeed>3);
  w.update(13);assert.equal(s.rain,1);assert.ok(s.wetness>.3);
  const wet=s.wetness;w.request('clear');w.update(20);
  assert.equal(s.rain,0);assert.ok(s.wetness>wet*.75,'Roads retain water after rain stops');
  w.update(45);assert.ok(s.wetness<wet,'Exposed pavement dries');
  const frozen={...s};w.update(0);assert.deepEqual(s,frozen);assert.equal(w.request('unknown'),false);
});

test('the same physical canopy shelters the listener, tires and floor texture from rain',()=>{
  const cover=createRainCover([{x:30,z:12,w:8,d:3,bottom:4,top:4.3,cos:0,sin:1},
    {x:0,z:0,w:22,d:18,bottom:5,top:10},{x:45,z:20,w:.3,d:.3,bottom:0,top:4}],()=>0);
  assert.equal(cover.exposed(30,15,1),false);assert.equal(cover.exposed(32,12,1),true);
  assert.equal(cover.exposed(30,12,4.32),true);assert.equal(cover.exposed(0,0,2),false);
  assert.equal(cover.exposed(45,20,1),true);
  const mask=cover.pavementMask(),pixel=(x,z)=>mask[(z+256)*512+x+256];
  assert.equal(pixel(0,0),0);assert.equal(pixel(30,14),0);assert.equal(pixel(33,12),255);
});

test('windblown streaks stop on the cinema screen without a fictitious roof, and a raised container still shelters the quay',()=>{
  const moving={x:12,z:9,w:5,d:3,bottom:.2,top:3};
  const cover=createRainCover([{x:0,z:0,w:.23,d:8.9,bottom:.8,top:4.65},moving],()=>0);
  assert.equal(cover.exposed(-2,0,1.6),true);
  assert.equal(cover.blocksStreak(-.36,2.1,0,.27,1.6,0),true);
  assert.equal(cover.blocksStreak(-.36,5.7,0,.27,5.1,0),false);
  assert.equal(cover.blocksStreak(-.36,2.1,5,.27,1.6,5),false);
  assert.equal(cover.exposed(12,9,4),true);
  moving.bottom=3.4;moving.top=6.2;
  assert.equal(cover.exposed(12,9,4),false);assert.equal(cover.exposed(12,9,7),true);
});

test('rain extends a 50 km/h stop while covered and dry tires keep their normal grip',()=>{
  function brakingDistance(grip){
    let state={speed:50/3.6},distance=0;
    for(let i=0;i<450&&state.speed>.015;i++){
      const v=state.speed;state=stepCarDynamics(state,{throttle:-1,grip},1/120);
      distance+=(v+Math.max(0,state.speed))/240;
    }
    assert.ok(state.speed<.015);return distance;
  }
  const dry=brakingDistance(wetRoadGrip(1,0,1)),wet=brakingDistance(wetRoadGrip(1,.92,1));
  assert.ok(wet>dry*1.13&&wet<dry*1.65);assert.equal(wetRoadGrip(.91,1,0),.91);
});
