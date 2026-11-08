using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityParkBuilder
    {
        public const string RootName = "Sunmeadow Park • paths, gardens and picnic grove";
        const string LibraryPath = "Assets/Imported/SunmeadowPark.asset";
        static readonly Vector3 Site = new Vector3(210, 0, 410);
        static readonly Quaternion Rotation = Quaternion.Euler(0, 48, 0);
        // The marked site was registered against the five existing plaza pads.
        // Coordinates include their complete hedges; none of those objects is replaced.
        static readonly Vector2[] Boundary = {
            new Vector2(-31.77f,40.99f),new Vector2(-18.94f,35.49f),new Vector2(29.82f,30.14f),
            new Vector2(62.15f,29.49f),new Vector2(47.30f,-12.38f),new Vector2(33.71f,-40.78f),
            new Vector2(21.41f,-34.32f),new Vector2(-8.58f,-29.53f),new Vector2(-47.99f,-15.50f),
            new Vector2(-60.38f,-8.43f),new Vector2(-64.90f,-2.84f)
        };

        public static bool Reserved(float x, float z, float margin = 0)
        {
            // Cheap world-space rejection for the city-wide vegetation pass.
            if (x < 159 - margin || x > 275 + margin || z < 355 - margin || z > 464 + margin) return false;
            var p = Quaternion.Inverse(Rotation) * (new Vector3(x, 0, z) - Site);
            return Inside(new Vector2(p.x, p.z)) || EdgeDistance(new Vector2(p.x,p.z)) < margin;
        }
        static bool Inside(Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = Boundary.Length - 1; i < Boundary.Length; j = i++)
                if ((Boundary[i].y > p.y) != (Boundary[j].y > p.y) &&
                    p.x < (Boundary[j].x - Boundary[i].x) * (p.y - Boundary[i].y) / (Boundary[j].y - Boundary[i].y) + Boundary[i].x) inside = !inside;
            return inside;
        }
        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        { var d=b-a; return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.0001f,d.sqrMagnitude))); }
        static float EdgeDistance(Vector2 p)
        { float d=float.MaxValue;for(int i=0;i<Boundary.Length;i++)d=Mathf.Min(d,SegmentDistance(p,Boundary[i],Boundary[(i+1)%Boundary.Length]));return d; }

        [MenuItem("Open Territory/Construir parque na área marcada")]
        public static void Rebuild()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!="Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode antes de construir o parque.");
            Directory.CreateDirectory("../Reports/park/backups");
            EditorSceneManager.SaveScene(scene,"../Reports/park/backups/Before-park-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var registry=Object.FindFirstObjectByType<WorldRegistry>();
            var world=JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var parent=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Marin •")).transform;
            Build(parent,world);
            var visual=JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            CityGroundCoverBuilder.Build(parent,visual,world);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform parent,WorldData world)
        {
            var previous=parent.Find(RootName);
            if(previous)Object.DestroyImmediate(previous.gameObject);
            if(AssetDatabase.LoadAssetAtPath<Object>(LibraryPath))AssetDatabase.DeleteAsset(LibraryPath);
            var library=ScriptableObject.CreateInstance<CityAssetLibrary>();library.sourceHash=world.sourceHash;
            AssetDatabase.CreateAsset(library,LibraryPath);
            var root=new GameObject(RootName);root.transform.SetParent(parent,false);
            root.transform.SetPositionAndRotation(Site,Rotation);
            var info=root.AddComponent<CityPark>();info.boundary=Boundary;
            Physics.SyncTransforms();
            try { new Scenery(root.transform,info,library,world).Build(); }
            catch { Object.DestroyImmediate(root);throw; }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
            Debug.Log("PARK_BUILD_OK paths="+info.walkways.Length+" benches="+info.seats.Length+" trees="+info.plantedTrees+" beds="+info.flowerBeds);
        }

        sealed class Scenery
        {
            readonly Transform root; readonly CityPark info; readonly Object library; readonly WorldData world;
            readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();
            readonly List<SeatData> seats=new List<SeatData>();readonly List<Transform> seatNodes=new List<Transform>();
            readonly List<CityPark.Walkway> paths=new List<CityPark.Walkway>();
            readonly List<Bounds> furniture=new List<Bounds>();
            readonly RaycastHit[] groundHits=new RaycastHit[32];
            Mesh cube;Material sand,edge,wood,iron,leaf,soil,lawn,lawnLight,flowerPink,flowerGold,flowerWhite,lens;
            Transform collisionRoot;
            public Scenery(Transform r,CityPark i,Object l,WorldData w){root=r;info=i;library=l;world=w;}
            Vector3 World(Vector2 p)=>Site+Rotation*new Vector3(p.x,0,p.y);
            float Height(Vector2 p)
            {
                var w=World(p);float h=CityGroundCoverBuilder.Height(world.ground,w.x,w.z);
                int count=Physics.RaycastNonAlloc(new Vector3(w.x,h+5,w.z),Vector3.down,groundHits,10,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore);
                float top=float.NegativeInfinity;
                for(int i=0;i<count;i++)if(!groundHits[i].transform.IsChildOf(root))top=Mathf.Max(top,groundHits[i].point.y);
                if(!float.IsNegativeInfinity(top))h=top;
                return h;
            }
            Vector3 Point(Vector2 p,float lift=0)=>new Vector3(p.x,Height(p)+lift,p.y);
            Material Mat(string n,Color c,float gloss=.09f)
            {var m=new Material(Shader.Find("OpenTerritory/CitySurface")){name=n,enableInstancing=true};m.SetColor("_Color",c);m.SetFloat("_Glossiness",gloss);AssetDatabase.AddObjectToAsset(m,library);return m;}
            void Shape(Mesh mesh,Vector3 p,Vector3 scale,Quaternion q,Material m)
            {if(!batches.TryGetValue(m,out var list)){list=new List<CombineInstance>();batches[m]=list;}list.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p,q,scale)});}
            GameObject Box(string n,Vector3 p,Vector3 size,Material m,bool solid=false,Quaternion? rotation=null)
            {
                var q=rotation??Quaternion.identity;Shape(cube,p,size,q,m);
                if(!solid)return null;
                var go=new GameObject(n);go.layer=CityContext.WorldLayer;go.transform.SetParent(collisionRoot,false);
                go.transform.localPosition=p;go.transform.localRotation=q;go.AddComponent<BoxCollider>().size=size;go.isStatic=true;return go;
            }
            bool Clear(Vector2 p,Vector2 size,float yaw=0,float height=3)
            {
                var wp=World(p);wp.y=Height(p)+height*.5f+.12f;
                if(Physics.CheckBox(wp,new Vector3(size.x*.5f,height*.5f,size.y*.5f),Rotation*Quaternion.Euler(0,yaw,0),1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))return false;
                return Inside(p)&&EdgeDistance(p)>Mathf.Max(size.x,size.y)*.5f+.4f;
            }
            bool WalkClear(Vector2 p,float radius)
            {
                if(!Inside(p) && EdgeDistance(p)>1.5f)return false;
                var wp=World(p);wp.y=Height(p);
                return !Physics.CheckCapsule(wp+Vector3.up*(radius+.16f),wp+Vector3.up*(2.0f+radius),radius,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore);
            }
            bool LineClear(Vector2 a,Vector2 b,float radius)
            {int n=Mathf.CeilToInt(Vector2.Distance(a,b)/.45f);for(int i=0;i<=n;i++)if(!WalkClear(Vector2.Lerp(a,b,i/(float)Mathf.Max(1,n)),radius))return false;return true;}

            // Offline A*: follow the real hedges, trunks and furniture, rather than
            // clipping a decorative ribbon straight through an existing plaza.
            Vector2[] Route(Vector2 start,Vector2 end,float radius)
            {
                if(LineClear(start,end,radius))return new[]{start,end};
                const int width=151,depth=111;const float minX=-72,minZ=-48,step=.9f;
                int Index(Vector2 p)=>Mathf.Clamp(Mathf.RoundToInt((p.y-minZ)/step),0,depth-1)*width+Mathf.Clamp(Mathf.RoundToInt((p.x-minX)/step),0,width-1);
                Vector2 Pos(int n)=>new Vector2(minX+n%width*step,minZ+n/width*step);
                int from=Index(start),target=Index(end),count=width*depth;
                var cost=new float[count];var previous=new int[count];var state=new byte[count];var open=new List<int>();
                for(int i=0;i<count;i++){cost[i]=float.PositiveInfinity;previous[i]=-1;}
                cost[from]=0;open.Add(from);state[from]=1;
                bool found=false;int budget=count;
                while(open.Count>0 && budget-->0)
                {
                    int at=0;float best=float.PositiveInfinity;
                    for(int j=0;j<open.Count;j++){int n=open[j];float f=cost[n]+Vector2.Distance(Pos(n),end);if(f<best){at=j;best=f;}}
                    int current=open[at];open.RemoveAt(at);if(current==target){found=true;break;}state[current]=2;
                    int cx=current%width,cz=current/width;
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    {
                        if(dx==0&&dz==0)continue;int x=cx+dx,z=cz+dz;if(x<0||x>=width||z<0||z>=depth)continue;
                        int next=z*width+x;if(state[next]==2||state[next]==3)continue;
                        if(!WalkClear(Pos(next),radius)){state[next]=3;continue;}
                        if(dx!=0&&dz!=0 && (!WalkClear(Pos(current)+new Vector2(dx*step,0),radius)||!WalkClear(Pos(current)+new Vector2(0,dz*step),radius)))continue;
                        float g=cost[current]+(dx==0||dz==0?step:step*1.414214f);
                        if(g>=cost[next])continue;cost[next]=g;previous[next]=current;if(state[next]!=1){state[next]=1;open.Add(next);}
                    }
                }
                if(!found)throw new InvalidOperationException("No unobstructed park path from "+start+" to "+end);
                var points=new List<Vector2>{end};int cursor=target;
                while(cursor!=from){cursor=previous[cursor];if(cursor<0)throw new InvalidOperationException("Incomplete park route");points.Add(Pos(cursor));}
                points.Add(start);points.Reverse();
                var simple=new List<Vector2>{start};int anchor=0;
                while(anchor<points.Count-1){int last=anchor+1;for(int j=anchor+2;j<points.Count;j++){if(!LineClear(points[anchor],points[j],radius))break;last=j;}simple.Add(points[last]);anchor=last;}
                return simple.ToArray();
            }
            void Path(string name,float width,params Vector2[] anchors)
            {
                var points=new List<Vector2>();
                for(int i=1;i<anchors.Length;i++){var part=Route(anchors[i-1],anchors[i],width*.5f+.18f);if(points.Count>0)points.RemoveAt(points.Count-1);points.AddRange(part);}
                paths.Add(new CityPark.Walkway{name=name,width=width,points=points.Select(p=>root.TransformPoint(Point(p,.075f))).ToArray()});
                Ribbon(name+" • pale stone border",points,width+.42f,edge,.047f,false);
                Ribbon(name,points,width,sand,.067f,true);
            }
            void Ribbon(string name,List<Vector2> points,float width,Material mat,float lift,bool physical)
            {
                var v=new List<Vector3>();var tri=new List<int>();var uv=new List<Vector2>();float distance=0;
                for(int k=1;k<points.Count;k++)
                {
                    var a=points[k-1];var b=points[k];var dir=(b-a).normalized;var side=new Vector2(-dir.y,dir.x)*width*.5f;
                    int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/1.2f));
                    for(int j=0;j<=n;j++)
                    {
                        var p=Vector2.Lerp(a,b,j/(float)n);v.Add(Point(p-side,lift));v.Add(Point(p+side,lift));
                        uv.Add(new Vector2(0,distance));uv.Add(new Vector2(width,distance));distance+=Vector2.Distance(a,b)/n;
                        if(j>0){int t=v.Count-4;tri.AddRange(new[]{t,t+1,t+2,t+1,t+3,t+2});}
                    }
                    Disc(name+" joint",b,width*.5f,mat,lift,physical,12);
                }
                Surface(name,v,tri,uv,mat,physical);
            }
            void Surface(string name,List<Vector3> v,List<int> t,List<Vector2> uv,Material mat,bool physical)
            {
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);
                Shape(mesh,Vector3.zero,Vector3.one,Quaternion.identity,mat);
                if(physical){var go=new GameObject(name+" • walkable");go.layer=CityContext.GroundLayer;go.transform.SetParent(collisionRoot,false);go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;}
            }
            void Disc(string name,Vector2 p,float radius,Material mat,float lift,bool physical=false,int segments=32)
            {
                var v=new List<Vector3>{Point(p,lift)};var uv=new List<Vector2>{Vector2.zero};var t=new List<int>();
                for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2/segments;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;v.Add(Point(p+d,lift));uv.Add(d);if(i>0)t.AddRange(new[]{0,i+1,i});}
                Surface(name,v,t,uv,mat,physical);
            }
            float PathDistance(Vector2 p)
            {
                float d=float.MaxValue;
                foreach(var path in paths)for(int i=1;i<path.points.Length;i++)
                {var a=root.InverseTransformPoint(path.points[i-1]);var b=root.InverseTransformPoint(path.points[i]);d=Mathf.Min(d,SegmentDistance(p,new Vector2(a.x,a.z),new Vector2(b.x,b.z))-path.width*.5f);}
                return d;
            }
            void Bench(Vector2 requested,float yaw)
            {
                var p=requested;var q=Quaternion.Euler(0,yaw,0);bool found=false;
                for(int ring=0;ring<=10&&!found;ring++)for(int j=0;j<(ring==0?1:16);j++)
                {
                    float angle=j*Mathf.PI*2/16;var offset=q*new Vector3(Mathf.Sin(angle)*ring*.5f,0,-Mathf.Cos(angle)*ring*.5f);
                    var candidate=requested+new Vector2(offset.x,offset.z);
                    if(!Clear(candidate,new Vector2(3.2f,2.6f),yaw,2))continue;
                    bool free=true;
                    foreach(float x in new[]{-1.42f,0,1.42f})foreach(float z in new[]{-.47f,0,.47f})
                    {var corner=q*new Vector3(x,0,z);if(PathDistance(candidate+new Vector2(corner.x,corner.z))<.45f)free=false;}
                    if(!free)continue;p=candidate;found=true;break;
                }
                if(!found)throw new InvalidOperationException("Bench footprint blocked at "+requested);
                float y=Height(p)+.075f;var node=new GameObject("Park bench "+(seats.Count+1)).transform;node.SetParent(root,false);node.localPosition=new Vector3(p.x,y,p.y);node.localRotation=q;
                void Part(string n,Vector3 offset,Vector3 size,Material mat,bool solid=true)=>Box(n,node.localPosition+q*offset,size,mat,solid,q);
                for(int i=0;i<5;i++)Part("Seat slat",new Vector3(0,.64f,-.24f+i*.12f),new Vector3(2.6f,.12f,.10f),wood);
                for(int i=0;i<3;i++)Part("Backrest slat",new Vector3(0,.91f+i*.16f,-.35f),new Vector3(2.6f,.12f,.10f),wood);
                foreach(int side in new[]{-1,1}){Part("Bench leg",new Vector3(side*1.02f,.30f,0),new Vector3(.13f,.60f,.64f),iron);Part("Armrest",new Vector3(side*1.23f,.87f,0),new Vector3(.10f,.10f,.66f),iron);}
                int id=-94000-seats.Count;var wp=node.position;
                seats.Add(new SeatData{id=id,x=wp.x,z=wp.z,position=wp,yaw=(48+yaw)*Mathf.Deg2Rad,seatTop=.70f,width=2.6f,depth=.62f});seatNodes.Add(node);
                Disc("Bench alcove",p,2.4f,sand,.065f,true,16);furniture.Add(new Bounds(new Vector3(p.x,0,p.y),new Vector3(4,1,4)));
                Physics.SyncTransforms();
            }
            void Lamp(Vector2 p)
            {
                if(PathDistance(p)<.75f)
                {
                    bool found=false;
                    for(int i=0;i<16;i++){var candidate=p+new Vector2(Mathf.Cos(i*Mathf.PI/8),Mathf.Sin(i*Mathf.PI/8))*2;if(PathDistance(candidate)>.9f&&Clear(candidate,Vector2.one*.7f,0,4)){p=candidate;found=true;break;}}
                    if(!found)return;
                }
                if(!Clear(p,new Vector2(.65f,.65f),0,4))return;
                float y=Height(p);Box("Lantern footing",new Vector3(p.x,y+.13f,p.y),new Vector3(.42f,.26f,.42f),edge,true);
                Box("Park lantern post",new Vector3(p.x,y+1.65f,p.y),new Vector3(.12f,3.2f,.12f),iron,true);
                Box("Warm lantern",new Vector3(p.x,y+3.25f,p.y),new Vector3(.37f,.48f,.37f),lens);
                Box("Lantern cap",new Vector3(p.x,y+3.52f,p.y),new Vector3(.54f,.08f,.54f),iron);
                var go=new GameObject("Park • warm light");go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(p.x,y+3.23f,p.y);
                var light=go.AddComponent<Light>();light.type=LightType.Point;light.range=12;light.intensity=1.5f;light.color=new Color(1,.79f,.52f);light.shadows=LightShadows.None;
            }
            void FlowerBed(Vector2 p,float radius,int seed)
            {
                if(!Clear(p,Vector2.one*(radius*2+.3f),0,2)||PathDistance(p)<radius+.45f)return;
                Disc("Low flowering island • stone edge",p,radius,edge,.09f,false,20);
                Disc("Mulched flower bed",p,radius-.20f,soil,.11f,false,20);
                var random=new System.Random(seed);
                for(int i=0;i<radius*radius*11;i++)
                {
                    float a=(float)random.NextDouble()*Mathf.PI*2;float r=Mathf.Sqrt((float)random.NextDouble())*(radius-.38f);var f=p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;float y=Height(f)+.15f;
                    float h=.25f+(float)random.NextDouble()*.3f;Box("Flower leaves",new Vector3(f.x,y+h*.45f,f.y),new Vector3(.15f,h,.14f),leaf);
                    var mat=i%5==0?flowerWhite:i%3==0?flowerGold:flowerPink;
                    Box("Flower head",new Vector3(f.x,y+h,f.y),new Vector3(.23f,.12f,.23f),mat,false,Quaternion.Euler(0,i*137,0));
                }
                info.flowerBeds++;furniture.Add(new Bounds(new Vector3(p.x,0,p.y),new Vector3(radius*2+.4f,1,radius*2+.4f)));
            }
            void PicnicGrove()
            {
                var p=new Vector2(-8,-9);if(!Clear(p,new Vector2(12,10),0,4.8f))throw new InvalidOperationException("Picnic grove overlaps existing scenery.");
                float y=Height(p)+.08f;info.picnicCenter=root.TransformPoint(new Vector3(p.x,y,p.y));
                Disc("Picnic terrace",p,7.4f,edge,.045f,true,32);Disc("Picnic terrace gravel",p,7.1f,sand,.065f,true,32);
                foreach(float x in new[]{-4.4f,4.4f})foreach(float z in new[]{-3.4f,3.4f})
                {Box("Pergola stone foot",new Vector3(p.x+x,y+.13f,p.y+z),new Vector3(.72f,.26f,.72f),edge,true);Box("Pergola column",new Vector3(p.x+x,y+1.85f,p.y+z),new Vector3(.27f,3.7f,.27f),wood,true);}
                foreach(float z in new[]{-3.4f,3.4f})Box("Pergola main beam",new Vector3(p.x,y+3.76f,p.y+z),new Vector3(9.9f,.28f,.24f),wood,true);
                for(int i=0;i<12;i++)Box("Open pergola roof",new Vector3(p.x-4.6f+i*.84f,y+3.95f,p.y),new Vector3(.17f,.22f,8.0f),wood,true);
                for(int i=0;i<7;i++)Box("Climbing leaves",new Vector3(p.x-4.6f+i*1.4f,y+4.09f,p.y+2.4f),new Vector3(1.7f,.20f,1.7f),leaf);
                foreach(float dx in new[]{-2.45f,2.45f})
                {
                    for(int j=0;j<6;j++)Box("Picnic table slat",new Vector3(p.x+dx,y+1.05f,p.y-.45f+j*.18f),new Vector3(2.9f,.13f,.15f),wood,true);
                    foreach(float z in new[]{-1.03f,1.03f})for(int j=0;j<3;j++)Box("Picnic bench slat",new Vector3(p.x+dx,y+.61f,p.y+z+(j-1)*.14f),new Vector3(3.2f,.12f,.12f),wood,true);
                    foreach(float x in new[]{-1,1}){Box("Picnic table leg",new Vector3(p.x+dx+x,y+.53f,p.y),new Vector3(.16f,1.03f,1.8f),iron,true);}
                }
                furniture.Add(new Bounds(new Vector3(p.x,0,p.y),new Vector3(13,1,11)));
            }
            void Trees()
            {
                var forest=root.parent.Find("Árvores • 24 variações • LOD");if(!forest)throw new InvalidOperationException("Existing tree library missing.");
                var templates=forest.GetComponentsInChildren<LODGroup>(true).Where(g=>g.name.StartsWith("Acácia")||g.name.StartsWith("Ipê")||g.name.StartsWith("Costeira")).GroupBy(g=>g.name.Split('•')[0]).Select(g=>g.First()).ToArray();
                var positions=new[]{new Vector2(-35,6),new Vector2(-31,-7),new Vector2(-26,-14),new Vector2(-22,8),new Vector2(-19,28),new Vector2(-8,29),new Vector2(4,29),new Vector2(18,29),new Vector2(32,26),new Vector2(45,26),new Vector2(52,24),new Vector2(39,14),new Vector2(27,9),new Vector2(20,2),new Vector2(21,-8),new Vector2(22,-22),new Vector2(14,-30),new Vector2(1,-27),new Vector2(-20,-23),new Vector2(-37,-10),new Vector2(-20,-3),new Vector2(-3,15),new Vector2(9,16),new Vector2(36,-7)};
                int index=0;
                foreach(var p in positions)
                {
                    if(PathDistance(p)<3.0f||!Clear(p,new Vector2(3,3),0,5)||furniture.Any(b=>b.SqrDistance(new Vector3(p.x,0,p.y))<5))continue;
                    var source=templates[index%templates.Length];var tree=Object.Instantiate(source.gameObject,root);tree.name="Park shade tree "+(index+1);tree.SetActive(true);
                    tree.transform.localPosition=Point(p);tree.transform.localRotation=Quaternion.Euler(0,index*137,0);float s=.85f+(index%4)*.11f;tree.transform.localScale=Vector3.one*s;
                    tree.layer=CityContext.WorldLayer;var trunk=tree.AddComponent<BoxCollider>();trunk.center=new Vector3(0,1.2f,0);trunk.size=new Vector3(.58f,2.4f,.58f);
                    furniture.Add(new Bounds(new Vector3(p.x,0,p.y),new Vector3(4.5f,1,4.5f)));index++;
                }
                info.plantedTrees=index;
            }
            void Sign(Vector2 p,float yaw)
            {
                if(!Clear(p,new Vector2(3.6f,1.6f),yaw,2.2f))return;
                float y=Height(p);var q=Quaternion.Euler(0,yaw,0);var origin=new Vector3(p.x,y,p.y);
                foreach(float x in new[]{-1.3f,1.3f})Box("Park sign leg",origin+q*new Vector3(x,.8f,0),new Vector3(.12f,1.6f,.12f),iron,true,q);
                Box("Park entrance sign",origin+new Vector3(0,1.55f,0),new Vector3(3.25f,.90f,.14f),iron,true,q);
                var labelRoot=new GameObject("Sunmeadow Park • entrance",typeof(RectTransform));labelRoot.transform.SetParent(root,false);labelRoot.transform.localPosition=origin+new Vector3(0,1.55f,0)+q*new Vector3(0,0,-.082f);labelRoot.transform.localRotation=q;labelRoot.transform.localScale=Vector3.one*.0045f;
                var canvas=labelRoot.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;((RectTransform)labelRoot.transform).sizeDelta=new Vector2(700,170);
                var text=new GameObject("Park name",typeof(RectTransform)).AddComponent<Text>();text.transform.SetParent(labelRoot.transform,false);text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;text.rectTransform.sizeDelta=Vector2.zero;
                text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=39;text.fontStyle=FontStyle.Bold;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.95f,.88f,.69f);text.raycastTarget=false;text.text="SUNMEADOW\nP A R K";
            }
            void Lawn(Vector2 center,float rx,float rz,Material mat)
            {
                var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();const int rings=8,segments=48;
                for(int ring=0;ring<=rings;ring++)for(int j=0;j<=segments;j++)
                {float a=j*Mathf.PI*2/segments;float r=ring/(float)rings*(1+.06f*Mathf.Sin(a*3+center.x));var p=center+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*rz)*r;v.Add(Point(p,.018f));uv.Add(p*.15f);}
                for(int ring=0;ring<rings;ring++)for(int j=0;j<segments;j++)
                {int n=ring*(segments+1)+j,k=n+segments+1;t.AddRange(new[]{n,k+1,k,n,n+1,k+1});}
                Surface("Mown lawn",v,t,uv,mat,false);
            }
            public void Build()
            {
                var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=temp.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(temp);
                collisionRoot=new GameObject("Park • physical paths and furniture").transform;collisionRoot.SetParent(root,false);
                sand=Mat("Warm limestone gravel",new Color(.67f,.58f,.41f));edge=Mat("Limestone borders",new Color(.72f,.70f,.58f));wood=Mat("Honey cedar",new Color(.40f,.235f,.10f));iron=Mat("Deep green metal",new Color(.055f,.125f,.10f));leaf=Mat("Garden foliage",new Color(.22f,.37f,.12f));soil=Mat("Dark garden mulch",new Color(.20f,.14f,.085f));lawn=Mat("Mown meadow green",new Color(.33f,.44f,.19f));lawnLight=Mat("Mown meadow light",new Color(.39f,.48f,.23f));flowerPink=Mat("Coral and pink flowers",new Color(.78f,.30f,.40f));flowerGold=Mat("Golden flowers",new Color(.95f,.67f,.20f));flowerWhite=Mat("Cream flowers",new Color(.94f,.88f,.71f));lens=Mat("Lantern amber glass",new Color(.93f,.76f,.45f));lens.SetColor("_EmissionColor",new Color(.18f,.105f,.035f));

                Path("Meadow loop",2.8f,new Vector2(-37,-3),new Vector2(-30,12),new Vector2(-18,22),new Vector2(6,24),new Vector2(29,24),new Vector2(33,20),new Vector2(31,8),new Vector2(33,0),new Vector2(28,-18),new Vector2(22,-29),new Vector2(0,-24),new Vector2(-22,-16),new Vector2(-37,-3));
                Path("West sidewalk entrance",3,new Vector2(-30,12),new Vector2(-39,14),new Vector2(-45,26));
                Path("East sidewalk entrance",3,new Vector2(29,24),new Vector2(49,24),new Vector2(59,26),new Vector2(61,28));
                Path("Garden promenade",3.2f,new Vector2(-37,-3),new Vector2(-20,3),new Vector2(-9,4),new Vector2(10,7),new Vector2(33,0));
                Path("Picnic grove access",2.6f,new Vector2(-9,4),new Vector2(-8,-3));
                Path("South picnic connection",2.6f,new Vector2(-8,-15),new Vector2(-10,-20),new Vector2(0,-24));
                info.walkways=paths.ToArray();info.entrances=new[]{root.TransformPoint(Point(new Vector2(-45,26),.08f)),root.TransformPoint(Point(new Vector2(61,28),.08f))};
                Disc("Garden meeting circle border",new Vector2(-9,4),5.6f,edge,.043f,true);Disc("Garden meeting circle",new Vector2(-9,4),5.35f,sand,.064f,true);
                PicnicGrove();Physics.SyncTransforms();
                Bench(new Vector2(-24,16),160);Bench(new Vector2(-2,19),180);Bench(new Vector2(22,20),180);Bench(new Vector2(44,27),180);
                Bench(new Vector2(24,-12),75);Bench(new Vector2(15,-24),0);Bench(new Vector2(-9,-20),0);Bench(new Vector2(-29,-11),35);Bench(new Vector2(-32,2),110);Bench(new Vector2(-18,5),90);
                info.seats=seats.ToArray();info.seatNodes=seatNodes.ToArray();Physics.SyncTransforms();
                foreach(var p in new[]{new Vector2(-35,14),new Vector2(-17,25),new Vector2(5,27),new Vector2(32,27),new Vector2(51,23),new Vector2(37,1),new Vector2(31,-15),new Vector2(23,-26),new Vector2(-2,-27),new Vector2(-25,-19),new Vector2(-40,-4),new Vector2(-14,0)})Lamp(p);
                FlowerBed(new Vector2(-25,8),2.5f,11);FlowerBed(new Vector2(-15,16),2.2f,12);FlowerBed(new Vector2(3,16),2.5f,13);FlowerBed(new Vector2(24,13),2.6f,14);FlowerBed(new Vector2(39,10),2.5f,15);FlowerBed(new Vector2(20,-16),2.4f,16);FlowerBed(new Vector2(8,-20),2.1f,17);FlowerBed(new Vector2(-21,-8),2.3f,18);FlowerBed(new Vector2(-29,-4),2.1f,19);
                Lawn(new Vector2(-17,13),12,7,lawn);Lawn(new Vector2(14,16),18,6,lawnLight);Lawn(new Vector2(15,-9),8,9,lawn);Lawn(new Vector2(-24,-7),9,6,lawnLight);
                Physics.SyncTransforms();Trees();Sign(new Vector2(-41,23),-45);Sign(new Vector2(55,27),180);
                // Batching stays local to this park; shared city geometry and LOD are untouched.
                foreach(var pair in batches)
                {
                    var mesh=new Mesh{name="Park • "+pair.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);
                    var go=new GameObject(mesh.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=pair.Key;r.shadowCastingMode=ShadowCastingMode.On;go.isStatic=true;
                }
                Physics.SyncTransforms();
            }
        }
    }
}
