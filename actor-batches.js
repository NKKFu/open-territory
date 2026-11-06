// Characters and vehicles keep their articulated scene graphs for simulation.
// Compatible visible parts are drawn together, including their current poses.
export function createActorBatches({THREE:T,scene}){
  const pools=new Map(),geometryKeys=new WeakMap(),local=new T.Matrix4(),world=new T.Matrix4();
  function geometryKey(geometry){
    if(!geometryKeys.has(geometry))geometryKeys.set(geometry,`${geometry.type}:${JSON.stringify(geometry.parameters)}`);
    return geometryKeys.get(geometry);
  }
  function makeMesh(geometry,material,capacity){
    const mesh=new T.InstancedMesh(geometry,material,capacity);
    mesh.instanceMatrix.setUsage(T.DynamicDrawUsage);mesh.count=0;mesh.frustumCulled=false;
    mesh.castShadow=true;mesh.receiveShadow=true;scene.add(mesh);return mesh;
  }
  function poolFor(source){
    const key=`${geometryKey(source.geometry)}:${source.material.uuid}`;
    if(!pools.has(key))pools.set(key,{mesh:makeMesh(source.geometry,source.material,256),capacity:256,used:0});
    return pools.get(key);
  }
  function append(pool,matrix){
    if(pool.used===pool.capacity){
      const next=makeMesh(pool.mesh.geometry,pool.mesh.material,pool.capacity*2);
      next.instanceMatrix.array.set(pool.mesh.instanceMatrix.array);
      pool.mesh.removeFromParent();pool.mesh.dispose();pool.mesh=next;pool.capacity*=2;
    }
    pool.mesh.setMatrixAt(pool.used++,matrix);
  }
  function visit(object){
    if(!object.visible)return;
    if(object.isMesh&&!Array.isArray(object.material)&&['BoxGeometry','CylinderGeometry','TorusGeometry'].includes(object.geometry.type)){
      // Layer 1 preserves visibility flags used by props, culling and ragdolls.
      object.layers.disable(0);object.layers.enable(1);
      const pool=poolFor(object);
      if(object.isInstancedMesh){
        for(let i=0;i<object.count;i++){object.getMatrixAt(i,local);world.multiplyMatrices(object.matrixWorld,local);append(pool,world);}
      }else append(pool,object.matrixWorld);
    }
    for(const child of object.children)visit(child);
  }
  return {
    update(roots){
      for(const pool of pools.values())pool.used=0;
      for(const root of roots){
        if(!root)continue;
        let visible=true;for(let p=root;p;p=p.parent)if(!p.visible){visible=false;break;}
        if(!visible)continue;
        root.updateWorldMatrix(true,true);visit(root);
      }
      for(const pool of pools.values()){
        pool.mesh.count=pool.used;pool.mesh.visible=pool.used>0;
        if(pool.used)pool.mesh.instanceMatrix.needsUpdate=true;
      }
    }
  };
}
