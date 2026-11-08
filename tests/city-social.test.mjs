import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../vendor/three.module.js';
import {createCitySocial,createSocialProps,umbrellaSpace,umbrellaClear} from '../city-social.js';

function person(name,x,z){
  const g=new T.Group();g.position.set(x,0,z);g.userData.hands=[new T.Group(),new T.Group()];
  g.userData.hands.forEach(h=>g.add(h));g.userData.hands[1].position.set(.4,1.66,.24);
  return {name,g};
}
function world(people,options={}){
  let time=0;
  const system=createCitySocial({people,focus:()=>({x:0,z:0}),...options});
  return {system,advance(seconds){for(let i=0;i<Math.ceil(seconds/.025);i++){time+=.025;system.update(.025,time);}return time;}};
}

test('at most two observers notice a visible low drone, raise a phone after looking and keep their exact positions',()=>{
  const people=Array.from({length:8},(_,i)=>person('p'+i,i+2,1)),positions=people.map(p=>p.g.position.clone());
  const wall=people[0],road=people[1],w=world(people,{drone:()=>({active:true,x:0,y:4,z:0}),canSee:p=>p!==wall,canPause:p=>p!==road});
  w.advance(.4);assert.equal(w.system.snapshot().active.length,2);
  assert.ok(!w.system.isPaused(wall)&&!w.system.isPaused(road));
  assert.ok(w.system.snapshot().active.every(p=>p.kind==='look'));
  w.advance(1.2);assert.ok(w.system.snapshot().active.some(p=>p.kind==='photo'));
  people.forEach((p,i)=>assert.deepEqual(p.g.position.toArray(),positions[i].toArray()));
  const started=w.system.snapshot().drone;w.advance(8);
  assert.ok(w.system.snapshot().drone<=6,'Each actor has a cooldown instead of staring forever');
  assert.ok(w.system.snapshot().completed>=started);
});

test('cover, takeoff height, distance and disappearance interrupt observation without a stuck pause',()=>{
  const p=person('a',2,1),source={active:true,x:0,y:4,z:0};let seen=true;
  const w=world([p],{drone:()=>source,canSee:()=>seen});w.advance(1.6);assert.equal(p.ambientPose.kind,'photo');
  seen=false;w.advance(.25);assert.equal(w.system.isPaused(p),false);assert.equal(p.ambientPose,undefined);
  seen=true;w.advance(2);assert.equal(w.system.snapshot().drone,1);
  w.advance(38);assert.ok(w.system.snapshot().drone>=2);
  source.active=false;w.advance(.025);assert.equal(w.system.isPaused(p),false);
  source.active=true;source.y=20;w.advance(50);assert.equal(w.system.snapshot().active.length,0);
});

test('rain protects exposed walkers with finite canopy allocation and leaves indoor props and paths alone',()=>{
  const people=Array.from({length:30},(_,i)=>person('p'+i,i*.9,1));
  people[1].indoor=true;people[2].busState='waiting';people[3].lifeState='activity';people[3].lifeActivity='read';
  const rain={rain:.8},w=world(people,{weather:()=>rain,exposed:p=>p!==people[4]});
  w.advance(1);const state=w.system.snapshot();
  assert.equal(state.rain.length,24);assert.ok(state.rain.filter(p=>p.kind==='umbrella').length<=10);
  assert.ok(state.rain.some(p=>p.kind==='rainCover'&&p.pace===1.24));
  for(const p of people)assert.equal(w.system.isPaused(p),false);
  for(const index of [1,2,3,4])assert.equal(people[index].ambientPose,undefined);
  people[0].indoor=true;w.advance(.025);assert.equal(people[0].ambientPose,undefined);assert.equal(people[0].ambientSpeed,undefined);
  rain.rain=0;w.advance(.025);assert.ok(people.every(p=>!p.ambientPose&&!p.ambientSpeed));
});

test('canopies need clearance from walls and each other, and fold immediately as a walker reaches a doorway',()=>{
  const people=[person('first',2,0),person('cover',2.8,0),person('crowd',3.1,0),person('wall',8,0)];
  let wall=false;
  const w=world(people,{weather:()=>({rain:.7}),canOpenUmbrella:(p,c)=>p!==people[3]&&!wall&&c.y>2.4});
  w.advance(.7);assert.equal(people[0].ambientPose.kind,'umbrella');assert.equal(people[2].ambientPose.kind,'rainCover');
  assert.equal(people[3].ambientPose.kind,'rainCover');
  const space=umbrellaSpace(people[0]);assert.ok(space.radius>.8&&space.y>2.4);
  wall=true;w.advance(.025);assert.equal(people[0].ambientPose.kind,'rainCover');
  assert.ok(people.every(p=>!w.system.isPaused(p)));
});

test('a low overhang or angled wall blocks a canopy while a higher roof and bench below it do not',()=>{
  const space={x:0,z:0,y:2.49,height:.34,radius:.86};
  assert.equal(umbrellaClear(space,[{x:0,z:0,w:3,d:3,bottom:2.7,top:2.9}]),false);
  assert.equal(umbrellaClear(space,[{x:.95,z:0,w:.3,d:4,bottom:0,top:3.2,cos:Math.SQRT1_2,sin:Math.SQRT1_2}]),false);
  assert.equal(umbrellaClear(space,[{x:0,z:0,w:3,d:3,bottom:3.1,top:3.3},{x:0,z:0,w:3,d:1,bottom:0,top:1.4}]),true);
});

test('combat, direct conversation, bus boarding, work and musician ownership override every ambient reaction',()=>{
  const people=Array.from({length:6},(_,i)=>person('p'+i,i+2,1)),band={id:'square',x:0,z:0,active:true,beat:1};
  let priority=false;
  people[0].performance=true;people[1].role='police';
  const w=world(people,{music:()=>band,canReact:()=>!priority});w.advance(2);
  assert.equal(w.system.snapshot().active.length,3);assert.equal(people[0].ambientPose,undefined);assert.equal(people[1].ambientPose,undefined);
  priority=true;w.advance(.025);assert.equal(w.system.snapshot().active.length,0);assert.ok(people.every(p=>!p.ambientPose));
  priority=false;w.advance(40);band.active=false;w.advance(.025);assert.equal(w.system.snapshot().active.length,0);
});

test('music spectators alternate listening, dance and applause then return to their existing routine',()=>{
  const people=[person('a',2,0),person('b',3,0),person('c',4,0),person('d',5,0)];
  const band={id:'square',x:0,z:0,active:true,beat:1},w=world(people,{music:()=>band});
  const kinds=new Set();
  for(let i=0;i<28;i++){w.advance(.4);for(const a of w.system.snapshot().active)kinds.add(a.kind);}
  assert.ok(kinds.has('listen')&&kinds.has('clap')&&kinds.has('dance'));
  assert.ok(w.system.snapshot().completed>=3);assert.ok(w.system.snapshot().active.length<=3);
});

test('phone and umbrella pool stays bounded across a long walk and keeps the canopy upright at the wrist',()=>{
  const props=createSocialProps(T),people=Array.from({length:35},(_,i)=>person('p'+i,i,0));
  for(let turn=0;turn<40;turn++){
    for(const p of people)delete p.ambientPose;
    for(let i=0;i<12;i++)people[(turn+i)%people.length].ambientPose={owner:'city-social',kind:i<10?'umbrella':'photo',weight:1,beat:0,phase:0};
    people.forEach(p=>{p.g.rotation.y=.9;p.g.userData.hands[1].rotation.set(.4,.6,-1.1);});props.update(people);
    const stats=props.snapshot();assert.equal(stats.attached.length,12);assert.deepEqual(stats.created,{umbrella:10,phone:2});
    for(const p of people){const canopy=p.g.getObjectByName('Guarda-chuva do morador');if(!canopy)continue;
      const q=canopy.getWorldQuaternion(new T.Quaternion()),up=new T.Vector3(0,1,0).applyQuaternion(q);
      assert.ok(up.distanceTo(new T.Vector3(0,1,0))<1e-8);
    }
  }
  for(const p of people)delete p.ambientPose;props.update(people);assert.equal(props.snapshot().attached.length,0);props.dispose();
});
