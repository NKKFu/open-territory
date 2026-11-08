import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createPlayerMotion} from '../player-motion.js';
import {planParkour,parkourPosition} from '../parkour.js';

function makeRig(){
  // Only the joint transforms are needed to check contact points.
  function part(parent,x,y,z){
    const mesh=new THREE.Object3D();mesh.position.set(x,y,z);parent.add(mesh);return mesh;
  }
  return createPlayerMotion({THREE,part});
}

test('both pilot palms meet a tilted steering wheel while the boat turns',()=>{
  const rig=makeRig(),boat=new THREE.Group(),wheel=new THREE.Group();boat.add(wheel);
  wheel.position.set(.43,.98,.07);wheel.rotation.x=-.63;const hands=[new THREE.Vector3(),new THREE.Vector3()];
  let largest=0;
  for(let i=0;i<30;i++){
    const angle=i*.03;boat.rotation.set(.025,angle,-.015,'YXZ');boat.position.set(4.72,-.28,-180);
    wheel.rotation.z=Math.sin(i*.1)*.3;wheel.updateWorldMatrix(true,false);
    wheel.localToWorld(hands[0].set(-.16,0,0));wheel.localToWorld(hands[1].set(.16,0,0));
    rig.root.position.copy(boat.localToWorld(new THREE.Vector3(.43,.24,-.4)));rig.root.rotation.y=angle;
    rig.update(1/60,{time:i/60,phase:0,speed:0,airborne:false,verticalSpeed:0,helm:{hands}});
    largest=Math.max(largest,rig.weaponHand.getWorldPosition(new THREE.Vector3()).distanceTo(hands[0]),rig.hand.getWorldPosition(new THREE.Vector3()).distanceTo(hands[1]));
  }
  assert.ok(largest<.05,`both palms must stay on the rim, measured ${largest}`);
});
const pose={time:0,phase:0,speed:0,verticalSpeed:0,airborne:false};

test('applause closes the palms and releases immediately to movement or equipment',()=>{
  const rig=makeRig();
  for(let i=0;i<60;i++)rig.update(1/60,{...pose,time:i/60,clap:true});
  rig.update(1/60,{...pose,time:Math.PI/12,clap:true});
  assert.equal(rig.mode,'clap');
  assert(rig.hand.getWorldPosition(new THREE.Vector3()).distanceTo(rig.weaponHand.getWorldPosition(new THREE.Vector3()))<.215);
  rig.update(1/60,{...pose,time:2,speed:3,clap:false});assert.equal(rig.mode,'walk');
  rig.update(1/60,{...pose,time:2,weapon:'pulse',aimed:true,clap:true});assert.equal(rig.mode,'aim');
});

test('aiming a carbine lifts the grip with the target and puts the second hand on the fore-end',()=>{
  const rig=makeRig(),near=new THREE.Vector3(),support=new THREE.Vector3();
  function settle(height){
    const target=new THREE.Vector3(0,height,10);
    for(let i=0;i<45;i++)rig.update(1/60,{...pose,time:i/60,weapon:'carbine',aimed:true,aimPoint:target});
    rig.weaponHand.getWorldPosition(near);rig.hand.getWorldPosition(support);
    assert.ok(near.x<0&&support.z>near.z+.12);
    assert.ok(support.distanceTo(near)<.32,'support palm should be on the barrel');
    return near.y;
  }
  const low=settle(-5),high=settle(8);assert.ok(high-low>.35,'the model aims up and down with the shot');
});

test('the seated player rests a hand on the lap and plants both shoes at each seat height',()=>{
  const rig=makeRig(),point=new THREE.Vector3();
  rig.root.position.set(4,.18,-3);rig.root.rotation.y=.85;
  for(const seatHeight of [.61,.63,.82]){
    for(let i=0;i<45;i++)rig.update(1/60,{...pose,time:i/60,seatHeight});
    assert.equal(rig.mode,'sit');
    const lap=rig.root.localToWorld(new THREE.Vector3(.26,seatHeight+.23,.28));
    assert.ok(rig.hand.getWorldPosition(point).distanceTo(lap)<.04);
    for(const hip of rig.root.userData.limbs.slice(0,2)){
      const knee=hip.children.find(node=>node.isGroup),shoe=knee.children.find(node=>node.isGroup);
      assert.ok(Math.abs(shoe.getWorldPosition(point).y-.125-rig.root.position.y)<.015);
    }
  }
});

test('the petting hand follows a moving head with the player translated and turned',()=>{
  const rig=makeRig(),pet={animal:{type:'dog'},elapsed:1},target=new THREE.Vector3(),hand=new THREE.Vector3();
  rig.root.position.set(12,.2,-7);rig.root.rotation.y=1.19;
  for(let frame=0;frame<80;frame++){
    const time=frame/60;
    rig.root.localToWorld(target.set(.07,1.10,.81+.09*Math.sin(time*9)));
    rig.update(1/60,{...pose,time,pet,petPoint:target});
    if(frame>12)assert.ok(rig.hand.getWorldPosition(hand).distanceTo(target)<.08);
  }
  assert.equal(rig.mode,'pet');
  assert.deepEqual(rig.root.position.toArray(),[12,.2,-7]);
  rig.update(1/60,pose);assert.equal(rig.mode,'idle');
});

test('a vault plants the palm on the physical ledge while the feet cross it',()=>{
  const rig=makeRig(),wall={x:0,z:0,w:5,d:.54,top:1.19,yaw:0};
  const vault=planParkour({x:0,y:.08,z:-1.15},{x:0,z:1},[wall],()=>.08);
  const anchor=new THREE.Vector3().copy(vault.hands[1]),hand=new THREE.Vector3();
  let held=0,longest=0;
  for(let frame=0;frame<=60;frame++){
    const t=frame/60;vault.elapsed=t*vault.duration;
    parkourPosition(vault,t,rig.root.position);
    rig.update(vault.duration/60,{...pose,vault});
    held=rig.hand.getWorldPosition(hand).distanceTo(anchor)<.10?held+1:0;
    longest=Math.max(longest,held);
  }
  assert.ok(longest>=8,`palm stayed within 10 cm of the ledge for ${longest} samples`);
});
