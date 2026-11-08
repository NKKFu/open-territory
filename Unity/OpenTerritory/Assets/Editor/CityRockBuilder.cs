using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    public static class CityRockBuilder
    {
        public const string RootName = "Pedras e rochas • afloramentos naturais";
        public static void Build(Transform parent, WorldData world, Object library)
        {
            var root = new GameObject(RootName).transform; root.SetParent(parent, false);
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Pedra natural • cinza, granito e arenito", enableInstancing = true };
            material.SetColor("_Color", Color.white);material.SetFloat("_Glossiness",.10f);AssetDatabase.AddObjectToAsset(material,library);
            var meshes = new Mesh[12,3];
            for(int v=0;v<12;v++)for(int l=0;l<3;l++){meshes[v,l]=Rock(v,l);AssetDatabase.AddObjectToAsset(meshes[v,l],library);}
            foreach(var data in world.rocks)
            {
                var go = new GameObject("Rocha • "+data.solidIndex);go.transform.SetParent(root,false);
                go.layer=CityContext.WorldLayer;go.transform.position=data.position;go.transform.rotation=Quaternion.Euler(0,data.yaw,0);go.transform.localScale=data.scale;go.isStatic=true;
                if(CityCemeteryBuilder.Reserved(data.position.x,data.position.z,Mathf.Max(data.scale.x,data.scale.z)))go.SetActive(false);
                if(CityCarDealershipBuilder.Reserved(data.position.x,data.position.z,Mathf.Max(data.scale.x,data.scale.z)))go.SetActive(false);
                var levels=new LOD[3];float[] heights={.12f,.04f,.002f};
                for(int l=0;l<3;l++)
                {
                    var part=new GameObject("LOD"+l);part.transform.SetParent(go.transform,false);part.layer=CityContext.WorldLayer;
                    part.AddComponent<MeshFilter>().sharedMesh=meshes[data.variant,l];var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                    renderer.shadowCastingMode=l<2?ShadowCastingMode.On:ShadowCastingMode.Off;
                    levels[l]=new LOD(heights[l],new Renderer[]{renderer});
                }
                var group=go.AddComponent<LODGroup>();group.SetLODs(levels);group.RecalculateBounds();
                // Pebbles below the controller's step height do not become foot traps.
                if(data.scale.y*.75f>.35f||Mathf.Max(data.scale.x,data.scale.z)>1.1f)
                {var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=meshes[data.variant,0];collider.convex=true;}
            }
            Debug.Log("NATURE_ROCKS_OK count="+world.rocks.Length+" variants=12 sharedMeshes=36");
        }

        static Mesh Rock(int variant,int lod)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
            Color color=variant/4==0?new Color(.25f,.27f,.25f):variant/4==1?new Color(.32f,.285f,.23f):new Color(.22f,.26f,.29f);
            int sides=lod==0?9:lod==1?6:4;float phase=variant*.71f;
            var rings=new Vector3[3,sides];
            for(int j=0;j<3;j++)for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides;
                float radius=(j==0?.38f:j==1?.51f:.29f)*(1+.13f*Mathf.Sin(a*3+phase)+.07f*Mathf.Cos(a*2-phase));
                float y=j==0?-.24f:j==1?.17f:.62f;
                rings[j,i]=new Vector3(Mathf.Cos(a)*radius+(j==2?.08f:0),y+(j==0?0:.05f*Mathf.Sin(a*2+phase)),Mathf.Sin(a)*radius+(j==2?-.04f:0));
            }
            void Face(Vector3 a,Vector3 b,Vector3 c,Color tint)
            {int at=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);colors.Add(tint);colors.Add(tint);colors.Add(tint);indices.Add(at);indices.Add(at+1);indices.Add(at+2);}
            for(int i=0;i<sides;i++)
            {
                int next=(i+1)%sides;
                Face(new Vector3(0,-.24f,0),rings[0,i],rings[0,next],color*.8f);
                Face(new Vector3(.08f,.73f,-.04f),rings[2,next],rings[2,i],color*1.12f);
                for(int j=0;j<2;j++)
                {
                    Color shade=color*(.85f+j*.12f+.06f*Mathf.Sin(i+phase));
                    Face(rings[j,i],rings[j+1,i],rings[j+1,next],shade);Face(rings[j,i],rings[j+1,next],rings[j,next],shade);
                }
            }
            var mesh=new Mesh{name="Rocha-"+variant+"-LOD"+lod};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);
            mesh.SetUVs(0,new List<Vector2>(new Vector2[vertices.Count]));mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
