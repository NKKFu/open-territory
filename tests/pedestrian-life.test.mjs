import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../vendor/three.module.js';
import {createPedestrianLife,trafficBlocksCrossing} from '../pedestrian-life.js';

function resident(name,x,z,scale=1){
  const g=new T.Group();g.position.set(x,0,z);g.scale.setScalar(scale);
  g.userData.head=new T.Group();g.add(g.userData.head);
  g.userData.limbs=Array.from({length:4},()=>new T.Group());
  return {name,g};
}
function street(people,options={}){
  const life=createPedestrianLife({people,groundHeight:()=>0,canStep:()=>true,canPause:()=>false,
    canSee:()=>true,focus:()=>({x:0,z:0}),random:()=>.2,...options});
  life.refresh();return life;
}
const gap=(a,b)=>a.g.position.distanceTo(b.g.position);

test('opposing pedestrians pass to their right with room for differently sized bodies',()=>{
  const a=resident('a',-3,0),b=resident('b',3,0,1.18),life=street([a,b]);
  let minimum=Infinity;
  for(let i=0;i<300;i++){
    life.walk(a,3-a.g.position.x,-a.g.position.z,.02);
    life.walk(b,-3-b.g.position.x,-b.g.position.z,.02);
    life.update(.02,i*.02);minimum=Math.min(minimum,gap(a,b));
  }
  assert.ok(a.g.position.x>1.8&&b.g.position.x<-1.8);
  assert.ok(minimum>=(1+1.18)*.55);
});

test('an initial overlap near a wall resolves on the free side, leaving a seated resident in place',()=>{
  const sitting=resident('sitting',-.48,0),a=resident('a',-.45,0);
  sitting.sitting=sitting.stationary=true;
  const life=street([sitting,a],{canStep:(_p,x,z)=>x>=-.50&&Math.abs(z)<.02});
  for(let i=0;i<40;i++)life.update(.025,i*.025);
  assert.equal(sitting.g.position.x,-.48);assert.ok(gap(sitting,a)>=1.10);
  assert.ok(life.snapshot().corrections>0);
});

test('a crossing approach detours around a signal pole without abandoning the opposite sidewalk',()=>{
  const a=resident('a',3,0);
  const life=street([a],{canStep:(_p,x,z)=>Math.hypot(x-1.35,z)>.61&&Math.abs(z)<1.2});
  for(let frame=0;frame<400;frame++){
    const pos=a.g.position,dx=-3-pos.x,dz=-pos.z;
    life.walk(a,dx,dz,Math.min(.022,Math.hypot(dx,dz)));
    assert.ok(Math.hypot(pos.x-1.35,pos.z)>.61);
  }
  assert.ok(Math.abs(a.g.position.x+3)<.03);
});

test('a car approaching the stripes blocks entry while a parallel lane stays independent',()=>{
  const a={x:-8.8,z:8.8},b={x:8.8,z:8.8};
  const car={g:{position:{x:3.3,z:16},rotation:{y:Math.PI}},width:2.5,length:4.6,velocity:6};
  assert.equal(trafficBlocksCrossing(a,b,[car]),true);
  car.g.position.z=22;
  car.g.rotation.y=0;assert.equal(trafficBlocksCrossing(a,b,[car]),false);
  car.g.position.z=3.3;car.g.rotation.y=Math.PI/2;
  assert.equal(trafficBlocksCrossing(a,b,[car]),false);
  car.g.position.z=8.8;car.velocity=0;assert.equal(trafficBlocksCrossing(a,b,[car]),true);
});

test('ambient conversations alternate speakers and yield immediately to a direct interaction',()=>{
  const a=resident('a',0,0),b=resident('b',1.7,0),life=street([a,b],{canPause:()=>true});
  life.update(.025,1);
  assert.ok(life.isPaused(a)&&life.isPaused(b));const first=life.utterance(1.2);
  life.update(.025,3.95);const reply=life.utterance(3.95);
  assert.notEqual(first.speaker,reply.speaker);assert.notEqual(first.line,reply.line);
  life.interrupt(a,4);
  assert.equal(life.isPaused(a),false);assert.equal(life.isPaused(b),false);assert.equal(life.utterance(4),null);
  life.update(.025,4.1);assert.equal(life.snapshot().started,1);
});

test('a ringing phone reaches only available pedestrians in earshot, and releases them afterwards',()=>{
  const near=resident('near',2,0),wall=resident('wall',-1,0),cross=resident('cross',1,2),far=resident('far',10,0);
  const life=street([near,wall,cross,far],{canPause:p=>p!==cross,canSee:(_a,b)=>b.x>=0});
  assert.equal(life.reactToSound(0,0,0),1);
  assert.equal(life.utterance(1).speaker,near);
  assert.equal(life.isPaused(cross),false);assert.equal(life.isPaused(wall),false);
  life.update(.025,1);assert.notEqual(near.g.rotation.y,0);
  life.update(.025,3);assert.equal(life.isPaused(near),false);
});

function vehicle(x,z,speed,heading=0){
  const g=new T.Group();g.position.set(x,0,z);g.rotation.y=heading;
  return {g,velocity:speed,width:2.4,length:4.6};
}

test('checks both sides once at a curb, without taking a step or restarting a completed check',()=>{
  const p=resident('curb',9,9),life=street([p]);p.nav={index:2};let lo=0,hi=0,ready=0;
  for(let i=0;i<80;i++){
    const t=i*.025,ok=life.curbReady(p,0,5,.025,t);if(ok&&!ready)ready=t;
    life.animateAttention(p,.025,t);lo=Math.min(lo,p.g.userData.head.rotation.y);hi=Math.max(hi,p.g.userData.head.rotation.y);
  }
  assert.ok(ready>=.72&&ready<1.04&&lo<-.88&&hi>.90);
  assert.deepEqual(p.g.position.toArray(),[9,0,9]);assert.equal(life.snapshot().traffic.checks,1);
  p.nav.index=5;assert.equal(life.curbReady(p,0,-5,.025,2),false);
});

test('horn listeners keep walking, respect walls and direct interactions, and ignore repeated honks',()=>{
  const p=resident('near',3,4),indoors=resident('indoor',1,0),wall=resident('wall',-3,2),talking=resident('talking',5,5),far=resident('far',19,0);
  indoors.indoor=true;const car=vehicle(0,0,0),life=street([p,indoors,wall,talking,far],{canSee:(_s,q)=>q.x>=0,canReact:q=>q!==talking});
  assert.equal(life.reactToHorn(car,1),1);assert.equal(life.reactToHorn(car,1.1),0);
  assert.equal(life.isPaused(p),false);assert.equal(life.utterance(1.2),null);
  for(let i=0;i<15;i++){life.walk(p,.0,1,.022);life.animateAttention(p,.025,1+i*.025);}
  assert.ok(p.g.position.z>4.3&&Math.abs(p.g.userData.head.rotation.y)>.9);
  for(let i=0;i<80;i++)life.animateAttention(p,.025,3+i*.025);
  assert.ok(Math.abs(p.g.userData.head.rotation.y)<.003);
  assert.equal(life.reactToHorn(Object.assign(car,{kind:'bike'}),14),1);
});

test('a sidewalk collision course prompts a continuous sidestep while an adjacent lane, retreating car and wall do not',()=>{
  const car=vehicle(0,-11,9),direct=resident('in path',.35,0),side=resident('by lane',3.5,2),behind=resident('behind',.35,-17),wall=resident('behind wall',0,2);
  const life=street([direct,side,behind,wall],{cars:[car],canSee:(_c,p)=>p!==wall.g.position});let largest=0;
  for(let i=0;i<100;i++){
    const x=direct.g.position.x;car.g.position.z+=car.velocity*.025;life.update(.025,i*.025);
    largest=Math.max(largest,Math.abs(direct.g.position.x-x));
  }
  assert.ok(direct.g.position.x>1.8&&largest<.055);
  assert.equal(side.g.position.x,3.5);assert.equal(behind.g.position.x,.35);assert.equal(wall.g.position.x,0);
  assert.equal(life.isPaused(direct),false);assert.equal(life.snapshot().traffic.sidesteps,1);
  const retreat=street([direct],{cars:[vehicle(2,-11,-8)]});retreat.update(.025,4);
  assert.equal(retreat.snapshot().traffic.notices,0);
});

test('a committed crosser quickens toward the same destination, respects clear paths and never steps into traffic to dodge',()=>{
  const p=resident('crossing',.4,0),car=vehicle(0,-10,9),life=street([p],{cars:[car],onRoad:()=>true});
  const destination={x:8.8,z:0};p.nav={crossing:true,landing:destination,index:2};
  life.update(.025,.1);assert(life.pace(p,.1)>1.4);assert.equal(life.isPaused(p),false);
  assert.equal(p.nav.landing,destination);assert.equal(life.snapshot().traffic.hurried,1);
  car.g.position.z=18;life.update(.025,1);assert.equal(life.pace(p,1),1);
  assert.equal(life.snapshot().traffic.sidesteps,0);
});

test('predicts where the walker will be when the car arrives, and rechecks a threatened curb before stepping off',()=>{
  const leaving=resident('leaving',.96,0),joining=resident('joining',2.7,0);
  const car=vehicle(0,-12,0),life=street([leaving,joining],{cars:[car],onRoad:()=>true});
  leaving.nav={crossing:true};joining.nav={crossing:false,index:0};
  life.update(.025,1);life.place(leaving,1.3,0);life.place(joining,2.44,0);
  car.velocity=8;life.update(.025,1.2);
  assert.equal(life.pace(leaving,1.2),1);
  assert.equal(life.snapshot().traffic.notices,1);
  for(let i=0;i<50;i++)life.curbReady(joining,-1,0,.025,1.2);
  assert.equal(life.curbReady(joining,-1,0,.025,1.25),false);
  car.velocity=0;life.update(.025,2);assert.equal(life.curbReady(joining,-1,0,.025,2),true);
});
