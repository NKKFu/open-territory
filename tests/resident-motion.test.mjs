import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createCharacter} from '../character-assets.js';
import {createResidentMotion} from '../resident-motion.js';
import {createNpcLife} from '../npc-life.js';

function makePerson(extra={}){
  const part=(parent,x,y,z)=>{const mesh=new THREE.Object3D();mesh.position.set(x,y,z);parent.add(mesh);return mesh;};
  return {g:createCharacter({part,shirt:'#628793',skin:'#c98e71',trousers:'#41565b',hair:'#493b34',...extra}),phase:.4};
}
test('walking and turning plants each stance foot, with the root controlled only by navigation',()=>{
  const p=makePerson(),motion=createResidentMotion({THREE}),feet=p.g.userData.ankles,last=feet.map(()=>new THREE.Vector3());
  let maxSlip=0,maxFloor=0,planted=0,previous=[false,false];
  for(let i=0;i<420;i++){
    const dt=1/60;p.g.rotation.y=i<160?0:Math.min((i-160)*.007,Math.PI/2);
    p.g.position.x+=Math.sin(p.g.rotation.y)*1.3*dt;p.g.position.z+=Math.cos(p.g.rotation.y)*1.3*dt;
    const before=p.g.position.clone();motion.update(p,dt,i*dt);assert.deepEqual(p.g.position.toArray(),before.toArray());
    const pose=motion.snapshot(p);
    for(let n=0;n<2;n++){
      const point=feet[n].getWorldPosition(new THREE.Vector3());
      if(i>80&&pose.planted[n]&&previous[n]){maxSlip=Math.max(maxSlip,point.distanceTo(last[n]));maxFloor=Math.max(maxFloor,Math.abs(point.y-.08));planted++;}
      previous[n]=pose.planted[n];last[n].copy(point);
    }
  }
  assert(planted>150);assert(maxSlip<.025,`stance slip ${maxSlip}m`);assert(maxFloor<.025,`sole error ${maxFloor}m`);
});
test('reading and cleaning use both actual palms, then dialogue hides the props',()=>{
  for(const kind of ['read','work']){
    const p=makePerson();p.routine=[kind];p.fixedScale=1;let talking=false;
    const life=createNpcLife({THREE,people:[p],scene:new THREE.Scene(),player:{position:new THREE.Vector3()},groundHeight:()=>0,
      moveTo:()=>true,locations:[{id:kind,activity:kind,x:0,z:0,duration:100}],random:()=>.4,isTalking:()=>talking});
    const motion=createResidentMotion({THREE});life.update(.1,0);life.update(.1,.1);motion.update(p,1/60,.1);
    // Exercise the supported caller signature, including a translated and turned actor.
    p.g.position.set(7,0,-9);p.g.rotation.y=.8;
    for(let i=0;i<20;i++){life.update(.016,i*.016);motion.update(p,.016,i*.016);}
    const prop=p.routinePose.props[kind==='read'?'book':'broom'];
    for(let n=0;n<2;n++){
      const point=kind==='read'?new THREE.Vector3(n?.25:-.25,-.035,.06):new THREE.Vector3(0,n?1.56:1.35,0);
      prop.localToWorld(point);const palm=p.g.userData.hands[n].getWorldPosition(new THREE.Vector3());
      assert(palm.distanceTo(point)<.035,`${kind} hand ${n}: ${palm.distanceTo(point)}`);
    }
    talking=true;life.update(.1,1);assert.equal(prop.visible,false);assert.equal(p.routinePose,null);life.dispose();
  }
});
test('guitar anchors follow chest motion; applause meets palms without moving the collision root',()=>{
  const p=makePerson(),motion=createResidentMotion({THREE}),d=p.g.userData,anchors=[new THREE.Object3D(),new THREE.Object3D()];
  anchors[0].position.set(-.18,.42,.43);anchors[1].position.set(.38,.71,.38);anchors.forEach(a=>d.chest.add(a));
  p.performancePose={active:true,kind:'guitar',handNodes:anchors,beat:0};
  for(let i=0;i<80;i++){p.performancePose.beat=i/30;motion.update(p,1/60,i/60);
    for(let n=0;n<2;n++)assert(d.hands[n].getWorldPosition(new THREE.Vector3()).distanceTo(anchors[n].getWorldPosition(new THREE.Vector3()))<.015);}
  p.performancePose=null;p.ambientPose={kind:'clap',beat:Math.PI/12};motion.update(p,1/60,2);
  assert(d.hands[0].getWorldPosition(new THREE.Vector3()).distanceTo(d.hands[1].getWorldPosition(new THREE.Vector3()))<.205);
  assert.deepEqual(p.g.position.toArray(),[0,0,0]);
});
test('sitting and externally owned poses keep seat, knees and root intact',()=>{
  const p=makePerson({seatHeight:.82});p.sitting=true;
  const d=p.g.userData,motion=createResidentMotion({THREE}),knees=d.knees.map(k=>k.quaternion.clone()),hip=d.hips.position.clone();
  for(let i=0;i<100;i++)motion.update(p,1/60,i/60,{talking:true});
  assert.deepEqual(d.hips.position.toArray(),hip.toArray());knees.forEach((q,i)=>assert(q.equals(d.knees[i].quaternion)));
  const pose=d.limbs[2].quaternion.clone();motion.update(p,.1,3,{external:true});assert(pose.equals(d.limbs[2].quaternion));
});

test('photographers look up at an elevated drone and lift both supporting hands',()=>{
  const p=makePerson(),motion=createResidentMotion({THREE}),hands=p.g.userData.hands;
  p.ambientPose={kind:'photo',weight:1,lookX:0,lookY:2.05,lookZ:4};motion.update(p,1/60,0);
  const low=hands.map(h=>h.getWorldPosition(new THREE.Vector3()).y);
  p.ambientPose.lookY=8;motion.update(p,1/60,.1);
  assert(p.g.userData.head.rotation.x<-.5);
  hands.forEach((h,i)=>assert(h.getWorldPosition(new THREE.Vector3()).y>low[i]+.1));
  assert(hands[0].getWorldPosition(new THREE.Vector3()).distanceTo(hands[1].getWorldPosition(new THREE.Vector3()))<.23);
});
