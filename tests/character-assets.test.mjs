import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../vendor/three.module.js';
import {createCharacter,characterStyles,buildDriverCharacter,addCharacterUniform} from '../character-assets.js';
import {createPlayerMotion} from '../player-motion.js';
import {createActorBatches} from '../actor-batches.js';
import {vehicleDesigns} from '../vehicle-assets.js';

const cube=new T.BoxGeometry(),point=new T.Vector3();
function part(g,x,y,z,w,h,d,mat){
  const mesh=new T.Mesh(cube,typeof mat==='string'?new T.MeshStandardMaterial({color:mat}):mat);
  mesh.position.set(x,y,z);mesh.scale.set(w,h,d);g.add(mesh);return mesh;
}
const palette={shirt:'#cd9c60',skin:'#c99575',trousers:'#4b6164',hair:'#483c38'};
function triangles(g){let n=0;g.traverseVisible(m=>{if(m.isMesh)n+=(m.geometry.index?.count??m.geometry.attributes.position.count)*(m.count??1)/3;});return n;}

test('eight outfits keep the foot origin, head height, legacy limb ids and reachable palm pivots',()=>{
  assert.equal(characterStyles.length,8);
  for(let style=0;style<8;style++){
    const g=createCharacter({part,...palette,style}),d=g.userData,bounds=new T.Box3().setFromObject(g);
    assert.ok(Math.abs(bounds.min.y)<1e-6);assert.ok(bounds.max.y>2.32&&bounds.max.y<2.47);
    assert.ok(bounds.max.x<.65&&bounds.min.x>-.65,'clothes retain the original shoulder envelope');
    assert.equal(d.limbs[0].parent,d.hips);assert.equal(d.limbs[2].parent,d.chest);
    assert.equal(d.hands[0].parent,d.elbows[0]);assert.equal(d.elbows[0].parent,d.limbs[2]);
    assert.equal(d.shoes[0],d.ankles[0]);assert.equal(d.knees[0].parent,d.limbs[0]);
    assert.ok(Math.abs(d.head.getWorldPosition(point).y-1.76)<1e-8);
    assert.ok(Math.abs(d.hands[1].getWorldPosition(point).y-.87)<1e-8);
    assert.ok(Math.abs(d.ankles[0].getWorldPosition(point).y-.08)<1e-8);
    assert.ok(triangles(g)<=1100,`outfit ${style} has ${triangles(g)} triangles`);
    g.traverse(o=>{if(o.isMesh){assert.ok(o.material.roughness>=.85);assert.equal(o.material.metalness,0);}});
  }
});

test('facets face outward and every citizen shares the same four reusable shape buffers',()=>{
  const a=createCharacter({part,...palette,style:2}),b=createCharacter({part,...palette,style:2}),shapes=new Map();
  a.traverse(m=>{if(m.geometry?.userData.characterShape)shapes.set(m.geometry.userData.characterShape,m.geometry);});
  assert.equal(shapes.size,4);
  b.traverse(m=>{if(m.geometry?.userData.characterShape)assert.equal(m.geometry,shapes.get(m.geometry.userData.characterShape));});
  for(const geo of shapes.values()){
    const p=geo.attributes.position,n=geo.attributes.normal;
    for(let i=0;i<p.count;i++)assert.ok(p.getX(i)*n.getX(i)+p.getY(i)*n.getY(i)+p.getZ(i)*n.getZ(i)>.30,'the outer planes must render from outside');
  }
});

test('seated citizens have the same faces and plant both soles at each actual seat height',()=>{
  for(const seatHeight of [.61,.63,.82]){
    const g=createCharacter({part,...palette,style:4,seatHeight}),d=g.userData;
    g.position.set(4,.18,-3);g.rotation.y=.7;g.updateMatrixWorld(true);
    assert.equal(d.rig.seated,true);assert.equal(d.rig.seatHeight,seatHeight);
    for(const shoe of d.shoes){assert.ok(Math.abs(shoe.getWorldPosition(point).y-.26)<1e-8);assert.ok(Math.abs(new T.Box3().setFromObject(shoe).min.y-.18)<1e-7);}
    assert.ok(Math.abs(d.hips.position.y-seatHeight-.10)<1e-8);
  }
});

test('driver heads fit every existing cabin and live steering meshes stay individually movable',()=>{
  for(let style=0;style<8;style++){
    const g=buildDriverCharacter({part,...palette,style}),d=g.userData,head=new T.Box3().setFromObject(d.head);
    assert.ok(head.max.y<=2.20);assert.equal(d.grips.length,2);
    for(const grip of d.grips){assert.equal(grip.arm.parent,g);assert.equal(grip.hand.parent,g);}
    // The vehicle asset's seat lowering is roof-relative and shared by all cars.
    for(const car of vehicleDesigns)assert.ok(head.max.y+car.roof-2.278<car.roof-.075);
  }
});

test('player preserves its rear sling bag and rigid art follows elbow and knee batching',()=>{
  const motion=createPlayerMotion({THREE:T,part,...palette}),g=motion.root,d=g.userData;
  assert.ok(d.outerwear.children.some(p=>p.position.z<-.32&&p.scale.x>.35),'the bag remains visible from the usual rear camera');
  const scene=new T.Scene();scene.add(g);const batches=createActorBatches({THREE:T,scene});batches.update([g]);
  const meshes=[];g.traverse(m=>{if(m.isMesh)meshes.push(m);});
  assert.ok(scene.children.filter(o=>o.isInstancedMesh&&o.count).length<=6,'shared palette batches must retain low draw count');
  const handPiece=d.hands[0].children.find(p=>p.isMesh),old=handPiece.getWorldPosition(new T.Vector3());
  d.elbows[0].rotation.x=-1;d.knees[0].rotation.x=.6;batches.update([g]);
  assert.ok(handPiece.getWorldPosition(point).distanceTo(old)>.3);
  assert.equal(handPiece.layers.isEnabled(0),false);assert.equal(handPiece.visible,true);
});

test('service badges and helmet follow the articulated chest and head',()=>{
  for(const kind of ['police','fire']){
    const g=createCharacter({part,...palette,style:4}),d=g.userData,base=d.chest.children.length;
    addCharacterUniform(g,kind,{part});assert.equal(d.outerwear.visible,false);
    assert.ok(d.chest.children.length>base);assert.ok(d.head.userData.hairParts.every(p=>!p.visible));
    assert.ok(new T.Box3().setFromObject(g).max.y<2.48);
  }
});
