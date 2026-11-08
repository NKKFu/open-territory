using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityWaterfrontBuilder
    {
        public const string RootName = "Bayfront • promenade and gardens";
        const string LibraryPath = "Assets/Imported/BayfrontPromenade.asset";
        // Centerline of the existing town-low-cross road, measured from the
        // actual scene. The positive side faces the bay.
        static readonly Vector2[] Road = {
            new Vector2(437.1f,488.8f), new Vector2(472.2f,509.9f),
            new Vector2(507.2f,530.5f), new Vector2(541.8f,550.6f),
            new Vector2(578.9f,570.0f), new Vector2(614.0f,591.3f),
            new Vector2(626.8f,600.3f)
        };
        static readonly Vector2[] Beds = {
            new Vector2(455.04f,518.19f),new Vector2(494.76f,541.8f),new Vector2(526.11f,562.42f),
            new Vector2(567.23f,588.72f),new Vector2(601.04f,609.13f)
        };

        // Also used by future ground-cover rebuilds. Buildings, established
        // trees, rocks and street objects are never removed by this builder.
        public static bool Reserved(float x, float z, float margin = 0)
        {
            if (x < 427-margin || x > 648+margin || z < 467-margin || z > 616+margin) return false;
            var p = new Vector2(x,z); float nearest = float.MaxValue, side = 0;
            foreach(var bed in Beds)if((p-bed).sqrMagnitude<(2.6f+margin)*(2.6f+margin))return true;
            for (int i=1; i<Road.Length; i++)
            {
                var a=Road[i-1]; var d=Road[i]-a; float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);
                var q=a+d*t; float sq=(p-q).sqrMagnitude;
                if (sq < nearest) { nearest=sq; side=Vector2.Dot(p-q,new Vector2(d.y,-d.x).normalized); }
            }
            return side > 6.8f-margin && side < 21.3f+margin && nearest < (22+margin)*(22+margin);
        }

        [Serializable] sealed class Record
        {
            public string createdAtUtc, scene, sourceHash;
            public bool passed; public int protectedColliders, changedColliders, palms, shadeTrees, benches, planters, flowerBeds, bays, rocks, trimmedMeshes;
        }

        [MenuItem("Open Territory/Construir passeio da orla")]
        public static void Rebuild()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path!="Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol salva e fora do Play Mode antes de construir a orla.");
            Directory.CreateDirectory("../Reports/waterfront/backups");
            if (!EditorSceneManager.SaveScene(scene,"../Reports/waterfront/backups/Before-waterfront-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true))
                throw new InvalidOperationException("Não foi possível guardar uma cópia anterior da cena.");
            var registry=Object.FindFirstObjectByType<WorldRegistry>();
            var world=JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var parent=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Marin •")).transform;
            Build(parent,world);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Não foi possível salvar a orla.");
        }

        public static void Build(Transform parent, WorldData world)
        {
            var previous=parent.Find(RootName);
            if (previous)
            {
                var old=previous.GetComponent<CityWaterfront>();
                if (old) for(int i=0;i<old.trimmedVegetation.Length && i<old.originalVegetation.Length;i++)
                    if(old.trimmedVegetation[i] && old.originalVegetation[i]) old.trimmedVegetation[i].sharedMesh=old.originalVegetation[i];
                Object.DestroyImmediate(previous.gameObject);
            }
            if(AssetDatabase.LoadAssetAtPath<Object>(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var protectedColliders=Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .ToDictionary(c=>c,c=>Signature(c));
            var library=ScriptableObject.CreateInstance<CityAssetLibrary>();library.sourceHash=world.sourceHash;
            AssetDatabase.CreateAsset(library,LibraryPath);
            var go=new GameObject(RootName);go.transform.SetParent(parent,false);
            var info=go.AddComponent<CityWaterfront>();
            try
            {
                Physics.SyncTransforms();new Scenery(go.transform,info,library).Build();Physics.SyncTransforms();
                int changed=protectedColliders.Count(pair=>!pair.Key || Signature(pair.Key)!=pair.Value);
                var report=new Record { createdAtUtc=DateTime.UtcNow.ToString("o"),scene=parent.gameObject.scene.path,
                    sourceHash=world.sourceHash,passed=changed==0,protectedColliders=protectedColliders.Count,changedColliders=changed,
                    palms=info.palms,shadeTrees=info.shadeTrees,benches=info.seats.Length,planters=info.planters,flowerBeds=info.flowerBeds,bays=info.bays,rocks=info.rocks,trimmedMeshes=info.trimmedVegetation.Length };
                Directory.CreateDirectory("../Reports/waterfront");File.WriteAllText("../Reports/waterfront/placement.json",JsonUtility.ToJson(report,true));
                if(changed!=0)throw new InvalidOperationException("Uma colisão existente foi alterada durante a implantação.");
                EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
                Debug.Log("BAYFRONT_BUILD_OK benches="+info.seats.Length+" palms="+info.palms+" bays="+info.bays+" protected="+protectedColliders.Count);
            }
            catch
            {
                for(int i=0;i<info.trimmedVegetation.Length&&i<info.originalVegetation.Length;i++)
                    if(info.trimmedVegetation[i]&&info.originalVegetation[i])info.trimmedVegetation[i].sharedMesh=info.originalVegetation[i];
                Object.DestroyImmediate(go);throw;
            }
        }

        static string Signature(Collider c)
        {
            string result=c.name+"|"+c.enabled+"|"+c.gameObject.activeSelf+"|"+c.gameObject.layer+"|"+c.transform.localToWorldMatrix.ToString("R");
            if(c is BoxCollider) {var b=(BoxCollider)c;result+="|"+b.center.ToString("R")+"|"+b.size.ToString("R");}
            if(c is MeshCollider) {var m=(MeshCollider)c;result+="|"+(m.sharedMesh?m.sharedMesh.GetInstanceID():0);}
            return result;
        }

        sealed class Scenery
        {
            readonly Transform root, collisions; readonly CityWaterfront info; readonly Object library;
            readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();
            readonly List<SeatData> seats=new List<SeatData>();readonly List<Transform> seatNodes=new List<Transform>();
            readonly List<Light> lights=new List<Light>(); readonly RaycastHit[] groundHits=new RaycastHit[48];
            readonly List<Vector3> road=new List<Vector3>();readonly List<float> cumulative=new List<float>();
            Mesh cube,trunk,frond,rock;Material paving,edge,joint,wood,iron,glass,green,greenLight,soil,pink,gold,white,rockMat;
            float length;

            public Scenery(Transform root,CityWaterfront info,Object library)
            {
                this.root=root;this.info=info;this.library=library;
                collisions=new GameObject("Bayfront • physical surfaces and furniture").transform;collisions.SetParent(root,false);
                var layout=JsonUtility.FromJson<ReferenceLayout>(File.ReadAllText("Assets/Resources/reference-map.json"));
                var street=layout.roads.First(r=>r.id=="town-low-cross");
                foreach(var p in street.points)if(p.x>=437.0f && p.x<=627.0f)
                {if(road.Count>0)length+=Vector3.Distance(road[road.Count-1],p);road.Add(p);cumulative.Add(length);}
                if(road.Count<10 || length<200)throw new InvalidOperationException("Trecho esperado da avenida não encontrado.");
            }
            Vector3 Along(float s)
            {
                s=Mathf.Clamp(s,0,length);int i=1;while(i<cumulative.Count-1 && cumulative[i]<s)i++;
                float t=Mathf.InverseLerp(cumulative[i-1],cumulative[i],s);return Vector3.Lerp(road[i-1],road[i],t);
            }
            Vector3 Normal(float s)
            {var d=Along(Mathf.Min(length,s+2))-Along(Mathf.Max(0,s-2));d.y=0;d.Normalize();return new Vector3(d.z,0,-d.x);}
            Vector3 At(float s,float offset)
            {var p=Along(s)+Normal(s)*offset;p.y=Height(p.x,p.z);return p;}
            float Height(float x,float z)
            {
                int n=Physics.RaycastNonAlloc(new Vector3(x,120,z),Vector3.down,groundHits,160,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore);
                float top=float.NegativeInfinity;for(int i=0;i<n;i++)if(!groundHits[i].transform.IsChildOf(root))top=Mathf.Max(top,groundHits[i].point.y);
                if(float.IsNegativeInfinity(top))throw new InvalidOperationException("Sem piso na orla: "+x+", "+z);return top;
            }
            bool Clear(Vector3 p,Vector3 size,Quaternion rotation)
            {return !Physics.CheckBox(p+Vector3.up*(size.y*.5f+.08f),size*.5f,rotation,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore);}
            Material Mat(string name,Color color,Color? emission=null)
            {
                var m=new Material(Shader.Find("OpenTerritory/CitySurface")){name=name,enableInstancing=true};
                m.SetColor("_Color",color);m.SetFloat("_Glossiness",.08f);if(emission.HasValue)m.SetColor("_EmissionColor",emission.Value);
                AssetDatabase.AddObjectToAsset(m,library);return m;
            }
            void Shape(Mesh mesh,Vector3 position,Vector3 scale,Quaternion rotation,Material mat)
            {if(!batches.TryGetValue(mat,out var list)){list=new List<CombineInstance>();batches[mat]=list;}list.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(position,rotation,scale)});}
            void Box(string name,Vector3 pos,Vector3 size,Material mat,Quaternion rotation,bool solid=false,Transform colliderParent=null)
            {
                Shape(cube,pos,size,rotation,mat);if(!solid)return;
                Collision(name,pos,size,rotation,colliderParent);
            }
            void Collision(string name,Vector3 pos,Vector3 size,Quaternion rotation,Transform colliderParent=null)
            {
                var go=new GameObject(name);go.layer=CityContext.WorldLayer;go.transform.SetParent(colliderParent?colliderParent:collisions,false);
                if(colliderParent){go.transform.position=pos;go.transform.rotation=rotation;}else{go.transform.localPosition=pos;go.transform.localRotation=rotation;}
                go.AddComponent<BoxCollider>().size=size;go.isStatic=true;
            }
            void Surface(string name,List<Vector3> vertices,List<int> triangles,Material mat,bool physical)
            {
                var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(vertices);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);
                Shape(m,Vector3.zero,Vector3.one,Quaternion.identity,mat);
                if(physical){var o=new GameObject(name+" • walkable");o.layer=CityContext.GroundLayer;o.transform.SetParent(collisions,false);o.AddComponent<MeshCollider>().sharedMesh=m;o.isStatic=true;}
            }
            void Ribbon(string name,float start,float end,float inner,float outer,float lift,Material mat,bool physical,float step=.8f)
            {
                var v=new List<Vector3>();var t=new List<int>();int count=Mathf.Max(1,Mathf.CeilToInt((end-start)/step));
                int across=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(outer-inner)/.7f)),stride=across+1;
                for(int i=0;i<=count;i++)
                {
                    float s=Mathf.Lerp(start,end,i/(float)count);
                    for(int j=0;j<=across;j++)
                    {
                        float offset=Mathf.Lerp(inner,outer,j/(float)across);v.Add(At(s,offset)+Vector3.up*lift);
                        if(i>0&&j>0){int k=(i-1)*stride+j-1;t.AddRange(new[]{k,k+stride,k+1,k+1,k+stride,k+stride+1});}
                    }
                }
                Surface(name,v,t,mat,physical);
            }
            void Disc(string name,float s,float offset,float radius,Material mat,float lift,bool physical)
            {
                var center=At(s,offset);var v=new List<Vector3>{center+Vector3.up*lift};var t=new List<int>();const int count=32,rings=6,stride=count+1;
                for(int ring=1;ring<=rings;ring++)for(int i=0;i<=count;i++)
                {
                    float a=i*Mathf.PI*2/count,reach=radius*ring/rings;float x=center.x+Mathf.Cos(a)*reach,z=center.z+Mathf.Sin(a)*reach;
                    v.Add(new Vector3(x,Height(x,z)+lift,z));
                    if(i>0)
                    {
                        int current=1+(ring-1)*stride+i;
                        if(ring==1)t.AddRange(new[]{0,current,current-1});
                        else {int inner=current-stride;t.AddRange(new[]{inner-1,current, current-1,inner-1,inner,current});}
                    }
                }
                Surface(name,v,t,mat,physical);
            }
            void Bench(float s,float offset)
            {
                var p=At(s,offset);var forward=Normal(s);var q=Quaternion.LookRotation(forward,Vector3.up);
                if(!Clear(p,new Vector3(3,2.1f,1.5f),q))return;
                var node=new GameObject("Bayfront bench "+(seats.Count+1)).transform;node.SetParent(root,false);node.position=p;node.rotation=q;
                void Part(string name,Vector3 local,Vector3 size,Material m,bool physical=false)
                {Box(name,p+q*local,size,m,q,physical,node);}
                for(int i=0;i<5;i++)Part("Bench seat",new Vector3(0,.64f,-.23f+i*.115f),new Vector3(2.45f,.10f,.095f),wood);
                for(int i=0;i<3;i++)Part("Bench back",new Vector3(0,.89f+i*.15f,-.31f),new Vector3(2.45f,.10f,.09f),wood);
                foreach(float side in new[]{-1f,1f})
                {Part("Bench leg",new Vector3(side*.95f,.30f,0),new Vector3(.12f,.60f,.56f),iron);Part("Bench arm",new Vector3(side*1.14f,.84f,0),new Vector3(.09f,.09f,.60f),iron);}
                Collision("Bench collision",p+q*new Vector3(0,.61f,0),new Vector3(2.45f,.24f,.60f),q,node);
                Collision("Backrest collision",p+q*new Vector3(0,1.02f,-.31f),new Vector3(2.45f,.55f,.10f),q,node);
                int id=-97000-seats.Count;seats.Add(new SeatData{id=id,x=p.x,z=p.z,position=p,yaw=q.eulerAngles.y*Mathf.Deg2Rad,seatTop=.69f,width=2.45f,depth=.62f});seatNodes.Add(node);
                Physics.SyncTransforms();
            }
            void Lamp(float s)
            {
                var p=At(s,16.2f);var q=Quaternion.identity;if(!Clear(p,new Vector3(.65f,3.8f,.65f),q))return;
                Box("Lantern footing",p+Vector3.up*.13f,new Vector3(.40f,.26f,.40f),edge,q,true);
                Box("Lantern post",p+Vector3.up*1.65f,new Vector3(.13f,3.0f,.13f),iron,q,true);
                Box("Lantern warm glass",p+Vector3.up*3.25f,new Vector3(.40f,.44f,.40f),glass,q);
                Box("Lantern cap",p+Vector3.up*3.50f,new Vector3(.52f,.10f,.52f),iron,q);
                var lamp=new GameObject("Bayfront lantern light");lamp.transform.SetParent(root,false);lamp.transform.position=p+Vector3.up*3.2f;
                var l=lamp.AddComponent<Light>();l.type=LightType.Point;l.range=12;l.intensity=0;l.enabled=false;l.shadows=LightShadows.None;l.color=new Color(1,.75f,.43f);lights.Add(l);
                Physics.SyncTransforms();
            }
            void Palm(float s)
            {
                var p=At(s,17.3f);if(!Clear(p,new Vector3(1.6f,7.2f,1.6f),Quaternion.identity))return;
                float h=5.3f+(info.palms%3)*.42f;Shape(trunk,p,new Vector3(.64f,h,.64f),Quaternion.Euler(0,info.palms*41,0),wood);
                for(int i=0;i<8;i++)
                {float yaw=i*45+info.palms*21;Shape(frond,p+Vector3.up*(h-.08f),Vector3.one,Quaternion.Euler(0,yaw,0),i%3==0?greenLight:green);}
                Collision("Palm trunk collision",p+Vector3.up*1.9f,new Vector3(.58f,3.8f,.58f),Quaternion.identity);
                info.palms++;Physics.SyncTransforms();
            }
            void Planter(float s,float offset)
            {
                var p=At(s,offset);var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                if(p.y<.75f||!Clear(p,new Vector3(2.6f,1.3f,1.15f),q))return;
                Box("Planter box",p+Vector3.up*.28f,new Vector3(2.5f,.56f,1.05f),edge,q,true);
                Box("Planter soil",p+Vector3.up*.57f,new Vector3(2.28f,.07f,.86f),soil,q);
                for(int i=0;i<12;i++)
                {
                    float x=-1.0f+(i%6)*.4f,z=(i/6==0?-.22f:.22f);float h=.26f+(i%3)*.08f;
                    Box("Flower foliage",p+q*new Vector3(x,.60f+h*.5f,z),new Vector3(.17f,h,.17f),green,q);
                    Box("Flower",p+q*new Vector3(x,.60f+h,z),new Vector3(.25f,.13f,.25f),i%4==0?white:i%3==0?gold:pink,q);
                }
                info.planters++;Physics.SyncTransforms();
            }
            void FlowerBed(float s,float offset,int seed)
            {
                var center=At(s,offset);if(!Clear(center,new Vector3(4.3f,1.6f,4.3f),Quaternion.identity))return;
                for(int i=0;i<8;i++)
                {float a=i*Mathf.PI/4;float x=center.x+Mathf.Cos(a)*2,z=center.z+Mathf.Sin(a)*2;if(Height(x,z)<CityMarine.SeaLevel+.75f)return;}
                Disc("Garden flower bed border",s,offset,2.05f,edge,.055f,false);
                Disc("Garden flower bed soil",s,offset,1.87f,soil,.075f,false);
                var random=new System.Random(seed);
                for(int i=0;i<28;i++)
                {
                    float a=(float)random.NextDouble()*Mathf.PI*2, r=Mathf.Sqrt((float)random.NextDouble())*1.6f;
                    float x=center.x+Mathf.Cos(a)*r,z=center.z+Mathf.Sin(a)*r,y=Height(x,z)+.10f,h=.22f+(i%4)*.055f;
                    Box("Garden flower leaves",new Vector3(x,y+h*.5f,z),new Vector3(.14f,h,.14f),green,Quaternion.identity);
                    Box("Garden flower petals",new Vector3(x,y+h,z),new Vector3(.23f,.13f,.23f),i%5==0?white:i%3==0?gold:pink,Quaternion.Euler(0,i*37,0));
                }
                info.flowerBeds++;
            }
            void ShadeTree(float s,float offset)
            {
                var p=At(s,offset);if(p.y<CityMarine.SeaLevel+1 || !Clear(p,new Vector3(5.5f,6,5.5f),Quaternion.identity))return;
                Shape(trunk,p,new Vector3(.60f,3.5f,.60f),Quaternion.Euler(0,24,0),wood);
                Box("Shade tree crown",p+Vector3.up*4.3f,new Vector3(2.7f,1.7f,2.7f),green,Quaternion.Euler(0,45,0));
                foreach(var d in new[]{new Vector3(-1.1f,4.0f,0),new Vector3(1.1f,4.2f,0),new Vector3(0,4.15f,-1.0f),new Vector3(0,4.5f,1.0f)})
                    Box("Shade tree foliage",p+d,new Vector3(1.8f,1.45f,1.8f),info.shadeTrees%2==0?greenLight:green,Quaternion.Euler(0,45,0));
                Collision("Shade tree trunk",p+Vector3.up*1.45f,new Vector3(.50f,2.9f,.50f),Quaternion.identity);info.shadeTrees++;Physics.SyncTransforms();
            }
            void Railing()
            {
                const float offset=20.4f;float last=0;var previous=At(0,offset);
                for(float s=0;s<=length+3;s+=4)
                {
                    float station=Mathf.Min(s,length);var p=At(station,offset);var post=p+Vector3.up*.77f;
                    Box("Waterfront rail post",post,new Vector3(.15f,1.54f,.15f),iron,Quaternion.identity,true);
                    Box("Rail post cap",p+Vector3.up*1.57f,new Vector3(.25f,.08f,.25f),gold,Quaternion.identity);
                    if(station>last+.01f)
                    {
                        foreach(float height in new[]{.65f,1.26f})
                        {
                            var a=previous+Vector3.up*height;var b=p+Vector3.up*height;var d=b-a;
                            Box("Waterfront railing",(a+b)*.5f,new Vector3(.10f,.10f,d.magnitude),iron,Quaternion.LookRotation(d.normalized,Vector3.up),true);
                        }
                        Box("Warm railing light",(previous+p)*.5f+Vector3.up*1.25f,new Vector3(.15f,.15f,.15f),glass,Quaternion.identity);
                    }
                    if(station>=length)break;last=station;previous=p;
                }
                Physics.SyncTransforms();
            }
            void Rocks()
            {
                var rng=new System.Random(4832);
                for(float s=1;s<length;s+=2.7f)
                {
                    var p=At(s,22.1f+(float)rng.NextDouble()*1.1f);float sx=1.9f+(float)rng.NextDouble()*1.5f,sy=.85f+(float)rng.NextDouble()*.55f,sz=1.6f+(float)rng.NextDouble()*1.1f;
                    var q=Quaternion.Euler(0,(float)rng.NextDouble()*360,0);var size=new Vector3(sx*.65f,sy*.60f,sz*.65f);
                    if(!Clear(p,size,q))continue;Shape(rock,p,new Vector3(sx,sy,sz),q,rockMat);
                    Collision("Shore rock",p+Vector3.up*sy*.33f,size,q);info.rocks++;
                }
                Physics.SyncTransforms();
            }
            bool GardenClear(float s)
            {
                for(float a=0;a<Mathf.PI*2;a+=Mathf.PI/4)
                {
                    var c=At(s,-16.0f);float x=c.x+Mathf.Cos(a)*3.7f,z=c.z+Mathf.Sin(a)*3.7f;
                    if(Height(x,z)<.65f)return false;
                    if(!Clear(new Vector3(x,Height(x,z),z),new Vector3(1,2,1),Quaternion.identity))return false;
                }
                for(float d=-7.5f;d>=-15;d-=1.5f)if(!Clear(At(s,d),new Vector3(3,2,1),Quaternion.LookRotation(Normal(s))))return false;
                return true;
            }
            void Gardens()
            {
                var entries=new List<Vector3>();var centers=new List<Vector3>();
                foreach(float fraction in new[]{.66f,.87f})
                {
                    float s=length*fraction;if(!GardenClear(s))continue;
                    Ribbon("Garden access",s-1.6f,s+1.6f,-16.0f,-7.2f,.14f,paving,true,.8f);
                    Disc("Garden stone border",s,-16.0f,3.9f,edge,.09f,false);
                    Disc("Garden terrace",s,-16.0f,3.7f,paving,.16f,true);
                    entries.Add(At(s,-7.5f)+Vector3.up*.16f);centers.Add(At(s,-15.8f)+Vector3.up*.16f);
                    Bench(s,-17.1f);
                }
                info.gardenEntrances=entries.ToArray();info.gardenCenters=centers.ToArray();
            }
            void TrimVegetation()
            {
                var cover=root.parent.Find(CityGroundCoverBuilder.RootName);if(!cover)return;
                var changed=new List<MeshFilter>();var originals=new List<Mesh>();
                foreach(var f in cover.GetComponentsInChildren<MeshFilter>(true))
                {
                    var source=f.sharedMesh;if(!source || !source.isReadable)continue;var b=f.GetComponent<Renderer>().bounds;
                    if(b.max.x<427||b.min.x>648||b.max.z<467||b.min.z>616)continue;
                    var vertices=source.vertices;var triangles=source.triangles;var kept=new List<int>(triangles.Length);
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var a=f.transform.TransformPoint(vertices[triangles[i]]);var c=f.transform.TransformPoint(vertices[triangles[i+1]]);var d=f.transform.TransformPoint(vertices[triangles[i+2]]);
                        var mid=(a+c+d)/3;if(Reserved(mid.x,mid.z,.65f))continue;
                        kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);
                    }
                    if(kept.Count==triangles.Length)continue;
                    var copy=Object.Instantiate(source);copy.name=source.name+" • waterfront clearance";copy.SetTriangles(kept,0);copy.bounds=source.bounds;
                    AssetDatabase.AddObjectToAsset(copy,library);changed.Add(f);originals.Add(source);f.sharedMesh=copy;
                }
                info.trimmedVegetation=changed.ToArray();info.originalVegetation=originals.ToArray();
            }
            Mesh MakeTrunk()
            {
                var v=new List<Vector3>();var t=new List<int>();const int n=8;
                for(int i=0;i<=n;i++) {float a=i*Mathf.PI*2/n;v.Add(new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f));v.Add(new Vector3(Mathf.Cos(a)*.32f,1,Mathf.Sin(a)*.32f));if(i>0){int k=v.Count-4;t.AddRange(new[]{k,k+1,k+2,k+1,k+3,k+2});}}
                var m=new Mesh{name="Bayfront • tapered palm trunk"};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);return m;
            }
            Mesh MakeFrond()
            {
                var m=new Mesh{name="Bayfront • palm frond"};m.vertices=new[]{Vector3.zero,new Vector3(-.32f,.15f,.65f),new Vector3(.32f,.15f,.65f),new Vector3(-.42f,.08f,1.45f),new Vector3(.42f,.08f,1.45f),new Vector3(-.25f,-.30f,2.25f),new Vector3(.25f,-.30f,2.25f),new Vector3(0,-.85f,3.0f)};
                m.triangles=new[]{0,1,2,1,3,2,2,3,4,3,5,4,4,5,6,5,7,6};m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);return m;
            }
            Mesh MakeRock()
            {
                var v=new List<Vector3>();var t=new List<int>();const int n=7;
                void Face(Vector3 a,Vector3 b,Vector3 c){int i=v.Count;v.Add(a);v.Add(b);v.Add(c);t.AddRange(new[]{i,i+1,i+2});}
                for(int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;
                    var p=new Vector3(Mathf.Cos(a)*.48f,0,Mathf.Sin(a)*.48f);var q=new Vector3(Mathf.Cos(b)*.48f,0,Mathf.Sin(b)*.48f);
                    var u=new Vector3(Mathf.Cos(a)*.43f,.48f,Mathf.Sin(a)*.43f);var w=new Vector3(Mathf.Cos(b)*.43f,.48f,Mathf.Sin(b)*.43f);
                    Face(p,u,q);Face(q,u,w);Face(u,new Vector3(.08f,.9f,0),w);
                }
                var m=new Mesh{name="Bayfront • faceted shore rock"};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);return m;
            }
            public void Build()
            {
                var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=temp.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(temp);
                trunk=MakeTrunk();frond=MakeFrond();rock=MakeRock();
                paving=Mat("Bayfront • warm limestone",new Color(.73f,.63f,.51f));edge=Mat("Bayfront • pale stone",new Color(.83f,.78f,.67f));
                joint=Mat("Bayfront • paving joints",new Color(.56f,.49f,.41f));wood=Mat("Bayfront • warm timber",new Color(.39f,.23f,.12f));
                iron=Mat("Bayfront • dark iron",new Color(.085f,.12f,.13f));glass=Mat("Bayfront • lantern glass",new Color(.95f,.76f,.43f),new Color(.65f,.32f,.08f));
                green=Mat("Bayfront • palm green",new Color(.20f,.37f,.16f));greenLight=Mat("Bayfront • sunlit leaves",new Color(.33f,.49f,.19f));green.SetFloat("_Cull",0);greenLight.SetFloat("_Cull",0);
                soil=Mat("Bayfront • garden soil",new Color(.22f,.16f,.10f));pink=Mat("Bayfront • coral flowers",new Color(.84f,.31f,.35f));gold=Mat("Bayfront • gold",new Color(.89f,.65f,.25f));white=Mat("Bayfront • cream flowers",new Color(.94f,.88f,.71f));rockMat=Mat("Bayfront • shore stone",new Color(.36f,.37f,.35f));
                TrimVegetation();
                Ribbon("Promenade border",0,length,7.0f,15.3f,.09f,edge,false);
                Ribbon("Promenade",0,length,7.3f,15.0f,.14f,paving,true);
                Ribbon("Promenade waterside band",0,length,14.55f,14.95f,.16f,edge,false);
                Ribbon("Promenade street band",0,length,7.35f,7.68f,.16f,edge,false);
                for(float s=2;s<length;s+=3.1f)Ribbon("Stone paving joint",s,s+.045f,7.7f,14.52f,.17f,joint,false,.1f);
                var route=new List<Vector3>();for(float s=1;s<length;s+=8)route.Add(At(s,11.2f)+Vector3.up*.15f);route.Add(At(length-1,11.2f)+Vector3.up*.15f);info.route=route.ToArray();
                for(float s=18;s<length-8;s+=37)
                {
                    Disc("Lookout border",s,15.4f,4.6f,edge,.09f,false);Disc("Lookout paving",s,15.4f,4.35f,paving,.16f,true);info.bays++;
                    Bench(s,16.8f);Planter(s+3.1f,17.1f);
                }
                for(float s=8;s<length;s+=18.5f)Lamp(s);
                for(float s=29;s<length-6;s+=36)Palm(s);
                for(float s=12;s<length-8;s+=28)Planter(s,-11.2f);
                Gardens();
                FlowerBed(length*.14f,-16,13);FlowerBed(length*.35f,-16,27);FlowerBed(length*.52f,-18,38);
                FlowerBed(length*.74f,-22,49);FlowerBed(length*.93f,-22,61);
                ShadeTree(length*.25f,-23);ShadeTree(length*.60f,-23);ShadeTree(length*.82f,-24);
                Railing();Rocks();
                info.seats=seats.ToArray();info.seatNodes=seatNodes.ToArray();info.lanterns=lights.ToArray();
                foreach(var pair in batches)
                {
                    var mesh=new Mesh{name=pair.Key.name+" • combined",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);
                    var go=new GameObject(mesh.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;renderer.shadowCastingMode=ShadowCastingMode.On;go.isStatic=true;
                }
                Physics.SyncTransforms();
            }
        }
    }
}
