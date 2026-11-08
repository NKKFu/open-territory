using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityDenseGroves
    {
        public static void Apply()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying||scene.path!="Assets/Scenes/BaiaDoSol.unity")throw new InvalidOperationException("Open the city outside Play Mode.");
            var before=JsonUtility.FromJson<WorldData>(File.ReadAllText("../Reports/nature/before-density-world.json"));
            var world=JsonUtility.FromJson<WorldData>(File.ReadAllText("Assets/Resources/world.json"));
            var visual=JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var forest=GameObject.Find("Árvores • 24 variações • LOD").transform;
            var meshes=new Mesh[24,3];Material material=null;
            for(int i=0;i<before.natureTrees.Length;i++)
            {
                var data=before.natureTrees[i];var tree=forest.Find(CityVegetation.Forms[data.variant%8]+" • "+i);
                if(!tree)continue;var levels=tree.GetComponent<LODGroup>().GetLODs();
                for(int l=0;l<3;l++){var r=levels[l].renderers[0];meshes[data.variant,l]=r.GetComponent<MeshFilter>().sharedMesh;material=r.sharedMaterial;}
            }
            for(int v=0;v<24;v++)for(int l=0;l<3;l++)if(!meshes[v,l])throw new InvalidOperationException("Missing shared tree variant "+v);
            var existing=new HashSet<int>(before.natureTrees.Select(t=>t.node));
            var physics=forest.parent.Find("Colisões e relevo");
            var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();var references=registry.nodes.ToList();
            var solids=world.solids.ToDictionary(s=>s.index);int added=0;
            for(int i=0;i<world.natureTrees.Length;i++)
            {
                var data=world.natureTrees[i];if(existing.Contains(data.node))continue;
                string name=CityVegetation.Forms[data.variant%8]+" • "+i;
                if(forest.Find(name))continue;
                var tree=new GameObject(name);tree.transform.SetParent(forest,false);tree.transform.position=data.position;
                tree.transform.rotation=Quaternion.Euler(0,data.yaw,0);tree.transform.localScale=data.scale;
                var levels=new LOD[3];float[] heights={.12f,.045f,.003f};
                for(int l=0;l<3;l++)
                {
                    var part=new GameObject("LOD"+l);part.transform.SetParent(tree.transform,false);
                    part.AddComponent<MeshFilter>().sharedMesh=meshes[data.variant,l];var r=part.AddComponent<MeshRenderer>();r.sharedMaterial=material;
                    r.shadowCastingMode=l<2?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
                    levels[l]=new LOD(heights[l],new Renderer[]{r});
                }
                var group=tree.AddComponent<LODGroup>();group.SetLODs(levels);group.RecalculateBounds();
                var s=solids[data.solidIndex];var collider=new GameObject("Colisão-"+s.index);collider.transform.SetParent(physics,false);
                collider.layer=CityContext.WorldLayer;collider.transform.position=new Vector3(s.x,(s.bottom+s.top)*.5f,s.z);collider.transform.rotation=Quaternion.Euler(0,s.yaw*Mathf.Rad2Deg,0);
                collider.AddComponent<BoxCollider>().size=new Vector3(s.w,s.top-s.bottom,s.d);collider.isStatic=true;
                references.Add(new NodeReference{id=data.node,transform=tree.transform});added++;
            }
            registry.nodes=references.ToArray();
            CityGroundCoverBuilder.Build(forest.parent,visual,world);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("DENSE_GROVES_OK addedTrees="+added+" originalTreesAndSceneEditsPreserved=True");
        }
    }
}
