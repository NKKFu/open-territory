import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {installPublicCameras} from '../surveillance.js';

test('each public view uses its mounted lens, respects the pan stops and returns control',()=>{
  const scene=new THREE.Scene(),fixtures=[],obstacles=[],footings=[];
  const network=installPublicCameras({THREE,scene,fixtures,obstacles,groundHeight:()=>.23,
    solid:(...box)=>footings.push(box),
    part(parent,x,y,z){const node=new THREE.Group();node.position.set(x,y,z);parent.add(node);return node;}});
  const camera=new THREE.PerspectiveCamera(),a=new THREE.Vector3(),b=new THREE.Vector3();
  assert.equal(network.devices.length,2);
  assert.equal(fixtures.length,footings.length);
  assert.equal(fixtures.length,obstacles.length);
  for(const unit of network.devices){
    network.connect(unit);network.view(camera,1/60,0,0);
    assert.ok(camera.position.distanceTo(unit.lens.getWorldPosition(a))<1e-8);
    assert.ok(camera.getWorldDirection(a).dot(unit.lens.getWorldDirection(b))>.9999);
    network.turn(10,10);
    for(let i=0;i<90;i++)network.view(camera,1/60,1,1);
    assert.ok(Math.abs(unit.head.rotation.y-unit.homeYaw-1.48)<.001);
    assert.ok(Math.abs(unit.head.rotation.x-1.1)<.001);
    assert.ok(camera.position.distanceTo(unit.lens.getWorldPosition(a))<1e-8);
    network.disconnect();assert.equal(network.active,null);
  }
});
