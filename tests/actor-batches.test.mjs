import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../vendor/three.module.js';
import {createActorBatches} from '../actor-batches.js';
import {createCitySurfaces} from '../city-surfaces.js';

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

test('color palettes preserve clothing colors while emissive lights remain independent',()=>{
  const scene=new THREE.Scene(),actor=new THREE.Group();scene.add(actor);
  const red=new THREE.MeshStandardMaterial({color:'#cc7766'}),blue=new THREE.MeshStandardMaterial({color:'#6699bb'});
  const lamp=new THREE.MeshStandardMaterial({color:'#eeeecc',emissive:'#ffeeaa',emissiveIntensity:2});
  for(const mat of [red,blue,lamp])actor.add(new THREE.Mesh(new THREE.BoxGeometry(),mat));
  const batches=createActorBatches({THREE,scene});batches.update([actor]);
  const meshes=scene.children.filter(p=>p.isInstancedMesh),palette=meshes.find(p=>p.count===2),light=meshes.find(p=>p.material===lamp),color=new THREE.Color();
  assert.equal(meshes.length,2);palette.getColorAt(0,color);for(const channel of ['r','g','b'])assert.ok(Math.abs(color[channel]-red.color[channel])<.00001);
  palette.getColorAt(1,color);assert.ok(Math.abs(color.b-blue.color.b)<.00001);
  red.color.set('#447755');lamp.emissiveIntensity=0;batches.update([actor]);palette.getColorAt(0,color);
  assert.ok(Math.abs(color.g-red.color.g)<.00001);assert.equal(light.material.emissiveIntensity,0);
});

test('actors outside the camera are omitted and return with their current pose',()=>{
  const scene=new THREE.Scene(),actor=new THREE.Group(),arm=new THREE.Group();scene.add(actor);actor.add(arm);
  const hand=new THREE.Mesh(new THREE.BoxGeometry(),new THREE.MeshStandardMaterial());hand.position.set(0,1,0);arm.add(hand);
  const camera=new THREE.PerspectiveCamera(60,1,.1,80);camera.position.set(0,2,10);camera.lookAt(0,1,0);
  const batches=createActorBatches({THREE,scene});batches.update([actor],camera);
  const batch=scene.children.find(p=>p.isInstancedMesh);assert.equal(batch.count,1);
  actor.position.z=25;arm.rotation.x=.8;batches.update([actor],camera);assert.equal(batch.count,0);assert.equal(actor.visible,true);
  const shadowCamera=new THREE.OrthographicCamera(-10,10,10,-10,.1,40);shadowCamera.position.set(0,20,25);shadowCamera.lookAt(0,0,25);shadowCamera.updateMatrixWorld();
  const shadowFrustum=new THREE.Frustum().setFromProjectionMatrix(new THREE.Matrix4().multiplyMatrices(shadowCamera.projectionMatrix,shadowCamera.matrixWorldInverse));
  batches.update([actor],camera,shadowFrustum);assert.equal(batch.count,1,'off-screen shadow casters are retained');
  actor.position.z=-4;batches.update([actor],camera);assert.equal(batch.count,1);
  const matrix=new THREE.Matrix4();batch.getMatrixAt(0,matrix);
  matrix.elements.forEach((v,i)=>assert.ok(Math.abs(v-hand.matrixWorld.elements[i])<.00001));
  actor.position.x=100;batches.update([actor],camera);assert.equal(batch.count,0);
});

test('textured color palettes retain their shared atlas and stay separate from plain actors',()=>{
  const atlas=new THREE.DataTexture(new Uint8Array([255,255,255,255]),1,1),surfaces=createCitySurfaces({THREE,atlas});
  const scene=new THREE.Scene(),root=new THREE.Group();scene.add(root);
  for(const color of ['#d48e79','#7facb8'])root.add(new THREE.Mesh(new THREE.BoxGeometry(),surfaces.decorate(new THREE.MeshStandardMaterial({color}),2)));
  root.add(new THREE.Mesh(new THREE.BoxGeometry(),new THREE.MeshStandardMaterial({color:'#ffffff'})));
  const batches=createActorBatches({THREE,scene});batches.update([root]);
  const meshes=scene.children.filter(o=>o.isInstancedMesh),textured=meshes.find(m=>m.material.userData.citySurface===2);
  assert.equal(meshes.length,2);assert.equal(textured.count,2);
  const shader={uniforms:{},vertexShader:THREE.ShaderLib.standard.vertexShader,fragmentShader:THREE.ShaderLib.standard.fragmentShader};
  textured.material.onBeforeCompile(shader);
  assert.equal(shader.uniforms.cityAtlas.value,atlas);assert.equal(shader.uniforms.cityKind.value,2);
  const color=new THREE.Color();textured.getColorAt(0,color);assert.ok(Math.abs(color.r-new THREE.Color('#d48e79').r)<.00001);
});
