// Characters and vehicles keep their articulated scene graphs for simulation.
// Compatible visible parts are drawn together, including their current poses.
export function createActorBatches({THREE:T,scene}){
  const pools=new Map(),geometryKeys=new WeakMap(),bounds=new WeakMap(),local=new T.Matrix4(),world=new T.Matrix4(),tint=new T.Color(),instanceTint=new T.Color();
  const frustum=new T.Frustum(),viewProjection=new T.Matrix4(),viewSphere=new T.Sphere();
  const batchable=object=>object.isMesh&&!Array.isArray(object.material)&&['BoxGeometry','CylinderGeometry','TorusGeometry'].includes(object.geometry.type);
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
    const mat=source.material;
    const mergeColor=mat.isMeshStandardMaterial&&!mat.isMeshPhysicalMaterial&&!mat.transparent&&!mat.map&&!mat.normalMap&&!mat.roughnessMap&&!mat.metalnessMap&&!mat.emissiveMap&&
      mat.emissive.getHex()===0&&mat.opacity===1&&mat.depthWrite&&mat.depthTest&&mat.blending===T.NormalBlending&&!mat.wireframe&&!mat.polygonOffset&&mat.alphaTest===0&&!mat.vertexColors&&!mat.clippingPlanes?.length;
    const style=mergeColor?`palette:${mat.roughness}:${mat.metalness}:${mat.side}:${mat.flatShading}:${mat.fog}:${mat.userData.citySurface||0}`:mat.uuid;
    const key=`${geometryKey(source.geometry)}:${style}:${mergeColor||!!source.instanceColor}`;
    if(!pools.has(key)){
      const material=mergeColor?mat.clone():mat;if(mergeColor){material.color.set('#ffffff');material.onBeforeCompile=mat.onBeforeCompile;material.customProgramCacheKey=mat.customProgramCacheKey;}
      pools.set(key,{mesh:makeMesh(source.geometry,material,256),capacity:256,used:0,useColor:mergeColor||!!source.instanceColor,mergeColor});
    }
    return pools.get(key);
  }
  function append(pool,matrix,color){
    if(pool.used===pool.capacity){
      const next=makeMesh(pool.mesh.geometry,pool.mesh.material,pool.capacity*2);
      next.instanceMatrix.array.set(pool.mesh.instanceMatrix.array);
      if(pool.mesh.instanceColor){next.setColorAt(0,new T.Color());next.instanceColor.array.set(pool.mesh.instanceColor.array);}
      pool.mesh.removeFromParent();pool.mesh.dispose();pool.mesh=next;pool.capacity*=2;
    }
    pool.mesh.setMatrixAt(pool.used,matrix);
    if(pool.useColor)pool.mesh.setColorAt(pool.used,color);
    pool.used++;
  }
  function visit(object){
    if(!object.visible)return;
    if(batchable(object)){
      // Layer 1 preserves visibility flags used by props, culling and ragdolls.
      object.layers.disable(0);object.layers.enable(1);
      const pool=poolFor(object);
      if(object.isInstancedMesh){
        for(let i=0;i<object.count;i++){
          object.getMatrixAt(i,local);world.multiplyMatrices(object.matrixWorld,local);
          if(pool.mergeColor)tint.copy(object.material.color);else tint.setRGB(1,1,1);
          if(object.instanceColor){object.getColorAt(i,instanceTint);tint.multiply(instanceTint);}
          append(pool,world,tint);
        }
      }else append(pool,object.matrixWorld,object.material.color);
    }
    for(const child of object.children)visit(child);
  }
  return {
    update(roots,camera=null,shadowFrustum=null){
      if(camera){camera.updateMatrixWorld();viewProjection.multiplyMatrices(camera.projectionMatrix,camera.matrixWorldInverse);frustum.setFromProjectionMatrix(viewProjection);}
      for(const pool of pools.values())pool.used=0;
      for(const root of roots){
        if(!root)continue;
        let visible=true;for(let p=root;p;p=p.parent)if(!p.visible){visible=false;break;}
        if(!visible)continue;
        if(camera){
          if(!bounds.has(root)){
            root.updateWorldMatrix(true,true);
            const sphere=new T.Box3().setFromObject(root).getBoundingSphere(new T.Sphere());
            sphere.center.applyMatrix4(root.matrixWorld.clone().invert());sphere.radius=sphere.radius/root.matrixWorld.getMaxScaleOnAxis()+1.5;bounds.set(root,sphere);
            root.traverse(object=>{if(batchable(object)){object.layers.disable(0);object.layers.enable(1);}});
          }else root.updateWorldMatrix(true,false);
          viewSphere.copy(bounds.get(root)).applyMatrix4(root.matrixWorld);
          // Off-screen casters can still shade something inside the view.
          if(!frustum.intersectsSphere(viewSphere)&&!shadowFrustum?.intersectsSphere(viewSphere))continue;
          root.updateWorldMatrix(false,true);
        }else root.updateWorldMatrix(true,true);
        visit(root);
      }
      for(const pool of pools.values()){
        pool.mesh.count=pool.used;pool.mesh.visible=pool.used>0;
        if(pool.used){pool.mesh.instanceMatrix.needsUpdate=true;if(pool.mesh.instanceColor)pool.mesh.instanceColor.needsUpdate=true;}
      }
    },
    invalidate(root){bounds.delete(root);}
  };
}
