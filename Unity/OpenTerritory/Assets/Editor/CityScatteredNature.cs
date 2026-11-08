using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityScatteredNature
    {
        public static void Apply()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying||scene.path!="Assets/Scenes/BaiaDoSol.unity")throw new InvalidOperationException("Open the saved city outside Play Mode.");
            if(scene.isDirty)
            {
                Directory.CreateDirectory("Assets/Scenes/Backups");
                EditorSceneManager.SaveScene(scene,"Assets/Scenes/Backups/Before-scattered-nature-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true);
            }
            var before=JsonUtility.FromJson<WorldData>(File.ReadAllText("../Reports/nature/before-scatter-world.json"));
            var world=JsonUtility.FromJson<WorldData>(File.ReadAllText("Assets/Resources/world.json"));
            var visual=JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            var old=GameObject.Find("Árvores • 24 variações • LOD");var parent=old.transform.parent;
            var oldIds=new HashSet<int>(before.natureTrees.Select(t=>t.node).Concat(world.natureTrees.Select(t=>t.node)));
            var solidIds=new HashSet<int>(before.natureTrees.Select(t=>t.solidIndex).Concat(world.natureTrees.Select(t=>t.solidIndex)));
            var physics=parent.Find("Colisões e relevo");
            foreach(int id in solidIds){var item=physics.Find("Colisão-"+id);if(item)UnityEngine.Object.DestroyImmediate(item.gameObject);}
            var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();
            foreach(var item in registry.nodes.Where(n=>oldIds.Contains(n.id)))
                if(item.transform&&!item.transform.IsChildOf(old.transform))UnityEngine.Object.DestroyImmediate(item.transform.gameObject);
            UnityEngine.Object.DestroyImmediate(old);
            var library=ScriptableObject.CreateInstance<CityAssetLibrary>();library.sourceHash=world.sourceHash;
            AssetDatabase.CreateAsset(library,AssetDatabase.GenerateUniqueAssetPath("Assets/Imported/ScatteredTrees.asset"));
            CityVegetation.Build(parent,visual,world,library);
            var forest=parent.GetChild(parent.childCount-1);
            var references=registry.nodes.Where(n=>n.transform&&!oldIds.Contains(n.id)).ToList();
            var solids=world.solids.ToDictionary(s=>s.index);
            for(int i=0;i<world.natureTrees.Length;i++)
            {
                var t=world.natureTrees[i];var tree=forest.Find(CityVegetation.Forms[t.variant%8]+" • "+i);
                references.Add(new NodeReference{id=t.node,transform=tree});
                var s=solids[t.solidIndex];var go=new GameObject("Colisão-"+s.index);go.transform.SetParent(physics,false);go.layer=CityContext.WorldLayer;
                go.transform.position=new Vector3(s.x,(s.bottom+s.top)*.5f,s.z);go.transform.rotation=Quaternion.Euler(0,s.yaw*Mathf.Rad2Deg,0);
                go.AddComponent<BoxCollider>().size=new Vector3(s.w,s.top-s.bottom,s.d);go.isStatic=true;
            }
            registry.nodes=references.ToArray();CityGroundCoverBuilder.Build(parent,visual,world);
            CityCemeteryBuilder.RefreshNature(parent,world);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("SCATTERED_NATURE_OK trees="+world.natureTrees.Length+" groves="+world.natureGroves.Length+" existing rocks retained");
        }
    }
}
