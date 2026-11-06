import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createActorBatches} from '../actor-batches.js';

test('draw batches preserve articulated world transforms and visibility',()=>{
  const scene=new THREE.Scene(),actor=new THREE.Group(),arm=new THREE.Group();
  actor.position.set(12,0,5);actor.rotation.y=.5;actor.add(arm);scene.add(actor);
  const hand=new THREE.Mesh(new THREE.BoxGeometry(),new THREE.MeshStandardMaterial());
  hand.position.set(0,-1,.2);arm.position.set(.5,1.7,0);arm.add(hand);
  const batches=createActorBatches({THREE,scene});batches.update([actor]);
  const batch=scene.children.find(p=>p.isInstancedMesh),actual=new THREE.Matrix4();
  batch.getMatrixAt(0,actual);
  actual.elements.forEach((v,i)=>assert.ok(Math.abs(v-hand.matrixWorld.elements[i])<.00001));
  assert.equal(hand.visible,true);assert.equal(hand.layers.isEnabled(0),false);
  arm.rotation.x=1;batches.update([actor]);batch.getMatrixAt(0,actual);
  actual.elements.forEach((v,i)=>assert.ok(Math.abs(v-hand.matrixWorld.elements[i])<.00001));
  hand.visible=false;batches.update([actor]);assert.equal(batch.count,0);
  hand.visible=true;actor.visible=false;batches.update([actor]);assert.equal(batch.count,0);
});

test('existing instances retain individual transforms when a batch grows',()=>{
  const scene=new THREE.Scene(),actor=new THREE.Group();scene.add(actor);actor.position.x=10;
  const source=new THREE.InstancedMesh(new THREE.BoxGeometry(),new THREE.MeshStandardMaterial(),300);
  const matrix=new THREE.Matrix4();for(let i=0;i<300;i++){matrix.makeTranslation(i,2,0);source.setMatrixAt(i,matrix);}actor.add(source);
  const batches=createActorBatches({THREE,scene});batches.update([actor]);
  const batch=scene.children.find(p=>p.isInstancedMesh);assert.equal(batch.count,300);
  for(const i of [0,255,299]){batch.getMatrixAt(i,matrix);assert.equal(matrix.elements[12],10+i);assert.equal(matrix.elements[13],2);}
  batches.update([]);assert.equal(batch.count,0);assert.equal(batch.visible,false);
});
