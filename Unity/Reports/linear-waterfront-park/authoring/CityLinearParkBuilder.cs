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
    public static class CityLinearParkBuilder
    {
        public const string RootName = "Bay Gardens • linear neighborhood park";
        const string LibraryPath = "Assets/Imported/LinearWaterfrontPark.asset";
        const string GeometryPath = "Assets/Editor/LinearParkYards.json";
        const string Reports = "../Reports/linear-waterfront-park";
        static readonly int[] Places = { 56, 48, 36, 27, 23 };
        static readonly int[] YardRoots = { 26680,26691,26699,26707,26956,26964,26972,27239,27247,27255 };
        static readonly int[] Trees = { 5843,5844,6045,6046,6275,6276 };
        static readonly Vector2[] Centers = { new Vector2(485.55f,587.28f),new Vector2(493.15f,590.02f),
            new Vector2(512.80f,597.61f),new Vector2(531.28f,605.62f),new Vector2(550.49f,613.24f),
            new Vector2(570.44f,618.36f),new Vector2(578.2f,620.16f) };

        public static bool Reserved(float x,float z,float margin=0)
        {
            var p=new Vector2(x,z);float best=float.MaxValue,side=0;
            if(Vector2.Dot(p-Centers[0],(Centers[1]-Centers[0]).normalized)<-margin ||
               Vector2.Dot(p-Centers[Centers.Length-1],(Centers[Centers.Length-1]-Centers[Centers.Length-2]).normalized)>margin)return false;
            for(int i=1;i<Centers.Length;i++) { var a=Centers[i-1];var d=Centers[i]-a;
                var q=a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);float sq=(p-q).sqrMagnitude;
                if(sq<best){best=sq;side=Vector2.Dot(p-q,new Vector2(-d.y,d.x).normalized);} }
            return side>-7.2f-margin && side<7.8f+margin && best<(8+margin)*(8+margin);
        }

        [Serializable] sealed class Record
        {
            public bool passed; public string createdAtUtc, sourceHash; public int houses, retiredObjects,
                retiredColliderCount, preservedColliders, unexpectedColliders, preservedBuildings, unexpectedBuildings, removedYardTriangles, changedMeshCount;
        }
        static string Signature(Collider c)
        {
            string s=c.name+"|"+c.enabled+"|"+c.gameObject.activeSelf+"|"+c.gameObject.layer+"|"+c.transform.localToWorldMatrix.ToString("R");
            if(c is BoxCollider b)s+="|"+b.center.ToString("R")+"|"+b.size.ToString("R");
            if(c is MeshCollider m)s+="|"+(m.sharedMesh?m.sharedMesh.GetInstanceID():0);
            return s;
        }

        [MenuItem("Open Territory/Construir parque linear na orla")]
        public static void Rebuild()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(Application.isPlaying||BuildPipeline.isBuildingPlayer||scene.isDirty||scene.path!="Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra a cena BaiaDoSol salva e fora do Play Mode.");
            Directory.CreateDirectory(Reports+"/backups");
            if(!EditorSceneManager.SaveScene(scene,Reports+"/backups/Before-linear-park-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true))
                throw new InvalidOperationException("Não foi possível salvar a cópia anterior da cena.");
            var registry=Object.FindFirstObjectByType<WorldRegistry>();
            Build(Object.FindFirstObjectByType<CityLodManager>().transform,JsonUtility.FromJson<WorldData>(registry.manifest.text));
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Não foi possível salvar o parque.");
        }

        [MenuItem("Open Territory/Restaurar casas do parque linear")]
        public static void RestoreMenu()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(Application.isPlaying||scene.isDirty||scene.path!="Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra a cena salva e fora do Play Mode.");
            var park=Object.FindFirstObjectByType<CityLinearPark>();if(!park)return;
            Restore(park);Object.DestroyImmediate(park.gameObject);EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        }
        static void Restore(CityLinearPark park)
        {
            foreach(var item in park.changedMeshes)if(item.target)item.target.sharedMesh=item.original;
            foreach(var item in park.changedFloors)if(item.target)item.target.sharedMesh=item.original;
            foreach(var item in park.retiredColliders)if(item.target)item.target.enabled=item.enabled;
            foreach(var item in park.retiredObjects)if(item.target)item.target.SetActive(item.active);
            Physics.SyncTransforms();
        }

        public static void Build(Transform city,WorldData world)
        {
            var prior=city.Find(RootName);
            if(prior) { var old=prior.GetComponent<CityLinearPark>();if(old)Restore(old);Object.DestroyImmediate(prior.gameObject); }
            if(AssetDatabase.LoadAssetAtPath<Object>(LibraryPath))AssetDatabase.DeleteAsset(LibraryPath);
            var before=Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).ToDictionary(c=>c,Signature);
            var grouped=city.Find("Novo mapa • geometria agrupada");
            var targetShells=new HashSet<string>(Places.Select(i=>"building:Reused residential interior "+(i-17)));
            var protectedBuildings=grouped.Cast<Transform>().Where(t=>t.name.StartsWith("building:")&&!targetShells.Contains(t.name))
                .ToDictionary(t=>t,t=>t.gameObject.activeSelf+"|"+t.localToWorldMatrix.ToString("R")+"|"+t.childCount);
            var library=ScriptableObject.CreateInstance<CityAssetLibrary>();library.sourceHash=world.sourceHash;AssetDatabase.CreateAsset(library,LibraryPath);
            var root=new GameObject(RootName);root.transform.SetParent(city,false);var park=root.AddComponent<CityLinearPark>();
            var retired=new List<CityLinearPark.RetiredObject>();var colliders=new List<CityLinearPark.RetiredCollider>();
            void Retire(GameObject go) { if(!go||retired.Any(x=>x.target==go))return;retired.Add(new CityLinearPark.RetiredObject{target=go,active=go.activeSelf});park.retiredObjects=retired.ToArray();go.SetActive(false); }
            try
            {
                var registry=Object.FindFirstObjectByType<WorldRegistry>();var homes=Object.FindFirstObjectByType<CityHomeCatalog>();var retiredNodes=new List<int>(YardRoots);
                var lighting=Object.FindFirstObjectByType<CityLighting>();
                foreach(int index in Places)
                {
                    var p=world.places[index];var home=homes.homes.FirstOrDefault(h=>h.place==index);
                    if(home==null || home.units!=1 || !home.root || p.lotWidth!=18 || p.lotDepth!=14)
                        throw new InvalidOperationException("O lote residencial esperado mudou: "+index);
                    var shell=city.Find("Novo mapa • geometria agrupada/building:Reused residential interior "+(index-17));
                    var foundation=city.Find("Building foundations/Foundation • "+index);
                    if(!shell||!foundation)throw new InvalidOperationException("Casca ou fundação ausente: "+index);
                    Retire(shell.gameObject);Retire(foundation.gameObject);Retire(home.root.gameObject);
                    foreach(var window in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name=="Janelas • "+index))Retire(window.gameObject);
                    var door=registry.nodes.FirstOrDefault(n=>p.door!=null&&n.id==p.door.id);if(door!=null&&door.transform)Retire(door.transform.gameObject);
                    if(p.door!=null)
                    {
                        retiredNodes.Add(p.door.id);
                        var panels=registry.nodes.Where(n=>n.transform&&n.transform.name.StartsWith("Doorway sidelight-"+p.door.id+"-",StringComparison.Ordinal)).ToArray();
                        if(panels.Length!=2)throw new InvalidOperationException("Painéis laterais da entrada ausentes: "+p.name);
                        foreach(var panel in panels){retiredNodes.Add(panel.id);Retire(panel.transform.gameObject);}
                    }
                    if(lighting)foreach(var fixture in lighting.fixtures.Where(f=>f.place==index))
                    {if(fixture.lens)Retire(fixture.lens.gameObject);if(fixture.source)Retire(fixture.source.gameObject);}
                }
                foreach(int id in YardRoots){var node=registry.nodes.FirstOrDefault(n=>n.id==id);if(node==null||!node.transform)throw new InvalidOperationException("Mobiliário de quintal ausente: "+id);Retire(node.transform.gameObject);}
                park.retiredNodes=retiredNodes.ToArray();
                var slots=new List<int>();
                for(int i=0;i<world.solids.Length;i++)
                {
                    var solid=world.solids[i];if(!solid.active)continue;
                    bool inLot=Places.Any(k=>world.places[k].Contains(new Vector3(solid.x,world.places[k].elevation,solid.z),-.08f));
                    bool yard=YardRoots.Contains(solid.owner)||Trees.Contains(solid.index);
                    if(inLot||yard)slots.Add(i);
                }
                park.retiredSolidSlots=slots.ToArray();
                foreach(var c in before.Keys)
                {
                    if(!c.name.StartsWith("Colisão-")||!int.TryParse(c.name.Substring(8),out int id))continue;
                    var position=c.transform.position;
                    if(!slots.Any(i=>world.solids[i].index==id&&Vector2.Distance(new Vector2(position.x,position.z),new Vector2(world.solids[i].x,world.solids[i].z))<.08f))continue;
                    colliders.Add(new CityLinearPark.RetiredCollider{target=c,enabled=c.enabled});c.enabled=false;
                }
                park.retiredColliders=colliders.ToArray();
                park.retiredSeats=world.seats.Where(s=>YardRoots.Contains(s.id)||Places.Any(i=>world.places[i].Contains(s.position,-.08f))).Select(s=>s.id).ToArray();
                CutYards(park,library);CutHouseFloors(park,library,world);Physics.SyncTransforms();
                new Scenery(root.transform,park,library,world).Build();Physics.SyncTransforms();
                var changedFloors=new HashSet<Collider>(park.changedFloors.Select(c=>(Collider)c.target));
                var changedSet=new HashSet<Collider>(colliders.Select(c=>c.target));
                int protectedCount=0,unexpected=0;
                foreach(var pair in before)
                {
                    if(changedSet.Contains(pair.Key)||changedFloors.Contains(pair.Key)||retired.Any(r=>pair.Key.transform.IsChildOf(r.target.transform)))continue;
                    protectedCount++;if(!pair.Key||Signature(pair.Key)!=pair.Value)unexpected++;
                }
                park.preservedColliders=protectedCount;
                int changedBuildings=protectedBuildings.Count(pair=>!pair.Key || pair.Value!=pair.Key.gameObject.activeSelf+"|"+pair.Key.localToWorldMatrix.ToString("R")+"|"+pair.Key.childCount);
                var record=new Record{passed=unexpected==0&&changedBuildings==0,createdAtUtc=DateTime.UtcNow.ToString("o"),sourceHash=world.sourceHash,
                    houses=Places.Length,retiredObjects=retired.Count,retiredColliderCount=colliders.Count,preservedColliders=protectedCount,
                    unexpectedColliders=unexpected,preservedBuildings=protectedBuildings.Count,unexpectedBuildings=changedBuildings,
                    removedYardTriangles=park.removedYardTriangles,changedMeshCount=park.changedMeshes.Length};
                Directory.CreateDirectory(Reports);File.WriteAllText(Reports+"/placement.json",JsonUtility.ToJson(record,true));
                if(!record.passed)throw new InvalidOperationException("Objetos fora dos lotes mudaram: colisões="+unexpected+", prédios="+changedBuildings);
                EditorUtility.SetDirty(park);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(city.gameObject.scene);
                Debug.Log("LINEAR_PARK_BUILD_OK houses=5 retained="+protectedCount+" paths="+park.paths.Length+" yardTriangles="+park.removedYardTriangles);
            }
            catch {Restore(park);Object.DestroyImmediate(root);throw;}
        }

        static string VertexKey(Vector3 p)=>Mathf.RoundToInt(p.x*1000)+","+Mathf.RoundToInt(p.y*1000)+","+Mathf.RoundToInt(p.z*1000);
        static string TriangleKey(Vector3 a,Vector3 b,Vector3 c)
        {var p=new[]{VertexKey(a),VertexKey(b),VertexKey(c)};Array.Sort(p,StringComparer.Ordinal);return string.Join("|",p);}
        static void CutYards(CityLinearPark park,Object library)
        {
            var source=JsonUtility.FromJson<VisualData>(File.ReadAllText(GeometryPath));
            var targets=new Dictionary<string,HashSet<string>>();
            foreach(var node in source.nodes)
            {
                var g=source.geometries[node.geometry];var instances=node.instances.Length>0?node.instances:new[]{new InstanceData{matrix=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1}}};
                foreach(var instance in instances)
                {
                    var m=new Matrix4x4();for(int c=0;c<4;c++)m.SetColumn(c,new Vector4(instance.matrix[c*4],instance.matrix[c*4+1],instance.matrix[c*4+2],instance.matrix[c*4+3]));m=Matrix4x4.TRS(node.position,node.rotation,node.scale)*m;
                    var point=m.MultiplyPoint3x4(Vector3.zero);var groups=node.materials.Length>1&&g.groups.Length>0?g.groups:new[]{new GeometryGroup{start=0,count=g.indices.Length,materialIndex=0}};
                    foreach(var group in groups)
                    {
                        int mat=node.materials[Mathf.Min(node.materials.Length-1,group.materialIndex)];
                        string batch=node.name=="Árvore reaproveitada da praça" || node.name=="Jardins e muros • Flores dos jardins" || node.name.StartsWith("Doorway fixed part-source-",StringComparison.Ordinal) ? "s-scenery:"+Mathf.FloorToInt(point.x/24)+":"+Mathf.FloorToInt(point.z/24)+"-"+mat : "s-surface:0:0-"+mat;
                        if(!targets.TryGetValue(batch,out var set))targets[batch]=set=new HashSet<string>();
                        for(int i=group.start;i+2<Mathf.Min(g.indices.Length,group.start+group.count);i+=3)
                        {var p=new Vector3[3];for(int j=0;j<3;j++){int k=g.indices[i+j]*3;p[j]=m.MultiplyPoint3x4(new Vector3(g.positions[k],g.positions[k+1],g.positions[k+2]));}set.Add(TriangleKey(p[0],p[1],p[2]));}
                    }
                }
            }
            var changes=new List<CityLinearPark.MeshChange>();
            foreach(var target in targets)
            {
                var go=GameObject.Find(target.Key);if(!go)throw new InvalidOperationException("Malha do quintal ausente: "+target.Key);
                var filter=go.GetComponent<MeshFilter>();var sourceMesh=filter.sharedMesh;var vertices=sourceMesh.vertices;var triangles=sourceMesh.triangles;var keep=new List<int>();int removed=0;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=go.transform.TransformPoint(vertices[triangles[i]]);var b=go.transform.TransformPoint(vertices[triangles[i+1]]);var c=go.transform.TransformPoint(vertices[triangles[i+2]]);
                    if(target.Value.Contains(TriangleKey(a,b,c))){removed++;continue;}keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);
                }
                if(removed!=target.Value.Count)throw new InvalidOperationException("A geometria compartilhada mudou: "+target.Key+" esperado="+target.Value.Count+" encontrado="+removed);
                var copy=Object.Instantiate(sourceMesh);copy.name=sourceMesh.name+" • removed selected yards";copy.SetTriangles(keep,0);copy.bounds=sourceMesh.bounds;AssetDatabase.AddObjectToAsset(copy,library);
                changes.Add(new CityLinearPark.MeshChange{target=filter,original=sourceMesh});park.changedMeshes=changes.ToArray();filter.sharedMesh=copy;park.removedYardTriangles+=removed;
            }
        }

        static void CutHouseFloors(CityLinearPark park,Object library,WorldData world)
        {
            var changes=new List<CityLinearPark.FloorChange>();int removed=0;
            foreach(var floor in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            {
                if(!floor.name.StartsWith("Piso-")||!floor.sharedMesh||floor.bounds.max.x<480||floor.bounds.min.x>585||floor.bounds.max.z<575||floor.bounds.min.z>635)continue;
                var mesh=floor.sharedMesh;var v=mesh.vertices;var triangles=mesh.triangles;var keep=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=floor.transform.TransformPoint(v[triangles[i]]);var b=floor.transform.TransformPoint(v[triangles[i+1]]);var c=floor.transform.TransformPoint(v[triangles[i+2]]);
                    bool remove=Places.Any(k=>{var p=world.places[k];bool upper=Mathf.Abs(a.y-p.elevation-.18f)<.025f&&Mathf.Abs(b.y-p.elevation-.18f)<.025f&&Mathf.Abs(c.y-p.elevation-.18f)<.025f;
                        bool lower=Mathf.Abs(a.y-p.elevation+.05f)<.025f&&Mathf.Abs(b.y-p.elevation+.05f)<.025f&&Mathf.Abs(c.y-p.elevation+.05f)<.025f;
                        return (upper||lower)&&p.Contains(a,-.45f)&&p.Contains(b,-.45f)&&p.Contains(c,-.45f);});
                    if(remove){removed++;continue;}keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);
                }
                if(keep.Count==triangles.Length)continue;
                var copy=Object.Instantiate(mesh);copy.name=mesh.name+" • removed house floors";copy.SetTriangles(keep,0);copy.bounds=mesh.bounds;AssetDatabase.AddObjectToAsset(copy,library);
                changes.Add(new CityLinearPark.FloorChange{target=floor,original=mesh});park.changedFloors=changes.ToArray();floor.sharedMesh=copy;
            }
            if(removed<10)throw new InvalidOperationException("Os dez triângulos das fundações antigas não foram localizados: "+removed);
        }

        sealed class Scenery
        {
            readonly Transform root,physical;readonly CityLinearPark park;readonly Object library;readonly WorldData world;
            readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();
            readonly List<CityLinearPark.Path> paths=new List<CityLinearPark.Path>();
            readonly List<SeatData> seats=new List<SeatData>();readonly List<Transform> seatNodes=new List<Transform>();
            readonly List<Light> lamps=new List<Light>();readonly List<Vector3> nav=new List<Vector3>();readonly List<CityLinearPark.NavLink> links=new List<CityLinearPark.NavLink>();
            readonly List<float> distance=new List<float>();readonly RaycastHit[] hits=new RaycastHit[48];
            Mesh cube,round,faceted;Material grass,walk,edge,bike,bikeMark,wood,metal,flowerPink,flowerWhite,flowerGold,leaf,leafLight,stone,soil,sand,play,bright,glow;
            float length;

            public Scenery(Transform root,CityLinearPark park,Object library,WorldData world)
            {
                this.root=root;this.park=park;this.library=library;this.world=world;
                physical=new GameObject("Paths and furniture • physical colliders").transform;physical.SetParent(root,false);
                distance.Add(0);for(int i=1;i<Centers.Length;i++){length+=Vector2.Distance(Centers[i-1],Centers[i]);distance.Add(length);}
            }
            Vector3 Along(float s)
            {
                s=Mathf.Clamp(s,0,length);int i=1;while(i<distance.Count-1&&distance[i]<s)i++;
                var q=Vector2.Lerp(Centers[i-1],Centers[i],Mathf.InverseLerp(distance[i-1],distance[i],s));return new Vector3(q.x,0,q.y);
            }
            Vector3 Tangent(float s){var d=Along(Mathf.Min(length,s+1))-Along(Mathf.Max(0,s-1));return d.sqrMagnitude>.0001f?d.normalized:Vector3.right;}
            Vector3 Normal(float s){var t=Tangent(s);return new Vector3(-t.z,0,t.x);}
            Vector3 At(float s,float d){return Along(s)+Normal(s)*d;}
            float Height(float x,float z)
            {
                int n=Physics.RaycastNonAlloc(new Vector3(x,90,z),Vector3.down,hits,130,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore);
                float best=float.NegativeInfinity;
                for(int i=0;i<n;i++)if(!hits[i].transform.IsChildOf(root))best=Mathf.Max(best,hits[i].point.y);
                float natural=CityGroundCoverBuilder.Height(world.ground,x,z);
                if(Places.Any(i=>world.places[i].Contains(new Vector3(x,world.places[i].elevation,z),-.01f)))best=Mathf.Max(best,natural+.025f);
                return float.IsNegativeInfinity(best)?natural:best;
            }
            Vector3 Point(float s,float d,float lift=0){var p=At(s,d);p.y=Height(p.x,p.z)+lift;return p;}
            Material Mat(string name,Color color,bool emissive=false)
            {var m=new Material(Shader.Find("OpenTerritory/CitySurface")){name=name,enableInstancing=true};m.SetColor("_Color",color);m.SetFloat("_Glossiness",.08f);if(emissive)m.SetColor("_EmissionColor",color*.6f);AssetDatabase.AddObjectToAsset(m,library);return m;}
            void Shape(Mesh mesh,Vector3 p,Vector3 scale,Quaternion rotation,Material mat)
            {if(!batches.TryGetValue(mat,out var list))batches[mat]=list=new List<CombineInstance>();list.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p,rotation,scale)});}
            void Box(string name,Vector3 p,Vector3 size,Quaternion q,Material mat,bool solid=false)
            {
                Shape(cube,p,size,q,mat);if(!solid)return;
                var o=new GameObject(name);o.transform.SetParent(physical,false);o.transform.SetPositionAndRotation(p,q);o.layer=CityContext.WorldLayer;o.AddComponent<BoxCollider>().size=size;o.isStatic=true;
            }
            void Bar(Vector3 a,Vector3 b,float thickness,Material mat)
            {Shape(round,(a+b)*.5f,new Vector3(thickness,Vector3.Distance(a,b)*.5f,thickness),Quaternion.FromToRotation(Vector3.up,b-a),mat);}
            void Surface(string name,List<Vector3> vertices,List<int> triangles,Material material,bool collider)
            {
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetColors(Enumerable.Repeat(Color.white,vertices.Count).ToList());mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);
                if(!collider){Shape(mesh,Vector3.zero,Vector3.one,Quaternion.identity,material);return;}
                var o=new GameObject(name);o.transform.SetParent(root,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=material;o.isStatic=true;
                o.layer=CityContext.GroundLayer;o.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            void PlotGround(PlaceData p)
            {
                var v=new List<Vector3>();var triangles=new List<int>();int nx=18,nz=14;var q=Quaternion.Euler(0,p.yaw*Mathf.Rad2Deg,0);
                for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
                {var a=new Vector3(p.x,0,p.z)+q*new Vector3(-9+x,0,-7+z);a.y=CityGroundCoverBuilder.Height(world.ground,a.x,a.z)+.025f;v.Add(a);}
                for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int i=z*(nx+1)+x;triangles.AddRange(new[]{i,i+nx+1,i+1,i+1,i+nx+1,i+nx+2});}
                Surface("Restored green ground • "+p.name,v,triangles,grass,true);
            }
            float WalkOffset(float s)=>-3.85f+.55f*Mathf.Sin(s*.125f);
            Vector3 PavedPoint(float s,float d,float lift){var p=At(s,d);p.y=Height(p.x,p.z)+lift;return p;}
            void Ribbon(string name,Vector3[] centers,float width,float lift,Material mat,bool collider,bool route)
            {
                var line=(Vector3[])centers.Clone();
                if(collider&&line.Length>=2)
                {
                    var first=(line[1]-line[0]).normalized;var last=(line[line.Length-1]-line[line.Length-2]).normalized;
                    line[0]-=first*.20f;line[line.Length-1]+=last*.20f;
                }
                var sampled=new List<Vector3>();
                for(int i=1;i<line.Length;i++)
                {
                    var a=line[i-1];var b=line[i];int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.3f));
                    for(int j=0;j<count;j++)sampled.Add(Vector3.Lerp(a,b,j/(float)count));
                }
                sampled.Add(line[line.Length-1]);var v=new List<Vector3>();var t=new List<int>();int across=Mathf.Max(1,Mathf.CeilToInt(width/.3f)),stride=across+1;
                for(int i=0;i<sampled.Count;i++)
                {
                    var dir=(sampled[Mathf.Min(i+1,sampled.Count-1)]-sampled[Mathf.Max(0,i-1)]).normalized;var side=new Vector3(-dir.z,0,dir.x);
                    for(int j=0;j<=across;j++){var p=sampled[i]+side*(width*(j/(float)across-.5f));p.y=Height(p.x,p.z)+lift;v.Add(p);}
                    if(i>0)for(int j=0;j<across;j++){int a=(i-1)*stride+j,b=i*stride+j;t.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
                }
                Surface(name,v,t,mat,collider);
                if(route)paths.Add(new CityLinearPark.Path{name=name,width=width,points=centers.Select(p=>new Vector3(p.x,Height(p.x,p.z)+lift,p.z)).ToArray()});
            }
            void Oval(string name,float s,float d,float rx,float rz,Material mat,float lift,bool collider)
            {
                var center=At(s,d);var right=Tangent(s);var forward=Normal(s);var v=new List<Vector3>();var tri=new List<int>();const int rings=5,segments=36;
                for(int r=0;r<=rings;r++)for(int i=0;i<=segments;i++)
                {float a=i*Mathf.PI*2/segments,scale=r/(float)rings;var p=center+right*(Mathf.Cos(a)*rx*scale)+forward*(Mathf.Sin(a)*rz*scale);p.y=Height(p.x,p.z)+lift;v.Add(p);}
                for(int r=0;r<rings;r++)for(int i=0;i<segments;i++){int a=r*(segments+1)+i,b=a+segments+1;tri.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
                Surface(name,v,tri,mat,collider);
            }
            void TrimCover()
            {
                var cover=root.parent.Find(CityGroundCoverBuilder.RootName);if(!cover)return;
                var changes=park.changedMeshes.ToList();
                foreach(var f in cover.GetComponentsInChildren<MeshFilter>(true))
                {
                    var source=f.sharedMesh;if(!source||!source.isReadable)continue;var bounds=f.GetComponent<Renderer>().bounds;
                    if(bounds.max.x<476||bounds.min.x>590||bounds.max.z<574||bounds.min.z>635)continue;
                    var p=source.vertices;var ts=source.triangles;var keep=new List<int>();
                    for(int i=0;i<ts.Length;i+=3){var c=f.transform.TransformPoint((p[ts[i]]+p[ts[i+1]]+p[ts[i+2]])/3);if(Reserved(c.x,c.z,.2f))continue;keep.Add(ts[i]);keep.Add(ts[i+1]);keep.Add(ts[i+2]);}
                    if(keep.Count==ts.Length)continue;var copy=Object.Instantiate(source);copy.name=source.name+" • park clearance";copy.SetTriangles(keep,0);copy.bounds=source.bounds;AssetDatabase.AddObjectToAsset(copy,library);
                    changes.Add(new CityLinearPark.MeshChange{target=f,original=source});park.changedMeshes=changes.ToArray();f.sharedMesh=copy;
                }
            }
            void Bench(float s,float d)
            {
                var p=Point(s,d);var q=Quaternion.LookRotation(Normal(s),Vector3.up);var node=new GameObject("Park bench • interaction").transform;node.SetParent(root,false);node.SetPositionAndRotation(p,q);
                for(int i=0;i<4;i++)Box("Seat slat",p+q*new Vector3(0,.62f,-.23f+i*.15f),new Vector3(2.2f,.11f,.13f),q,wood);
                for(int i=0;i<3;i++)Box("Back slat",p+q*new Vector3(0,.88f+i*.15f,-.30f),new Vector3(2.2f,.10f,.10f),q,wood);
                foreach(float x in new[]{-.83f,.83f})Box("Bench leg",p+q*new Vector3(x,.32f,0),new Vector3(.12f,.60f,.48f),q,metal);
                var o=new GameObject("Bench collision");o.transform.SetParent(node,false);o.transform.localPosition=new Vector3(0,.69f,-.05f);o.layer=CityContext.WorldLayer;o.AddComponent<BoxCollider>().size=new Vector3(2.25f,.24f,.70f);
                int id=-98000-seats.Count;seats.Add(new SeatData{id=id,x=p.x,z=p.z,position=p,yaw=q.eulerAngles.y*Mathf.Deg2Rad,seatTop=.69f,width=2.2f,depth=.62f});seatNodes.Add(node);
            }
            void Lamp(float s,float d)
            {
                var p=Point(s,d);var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                Box("Lamp post",p+Vector3.up*1.35f,new Vector3(.13f,2.7f,.13f),q,metal,true);
                Box("Lamp lantern",p+Vector3.up*2.82f,new Vector3(.37f,.34f,.37f),q,glow);
                Box("Lamp cap",p+Vector3.up*3.02f,new Vector3(.48f,.10f,.48f),q,metal);
                var o=new GameObject("Warm park light");o.transform.SetParent(root,false);o.transform.position=p+Vector3.up*2.85f;var l=o.AddComponent<Light>();l.type=LightType.Point;l.range=11;l.intensity=0;l.enabled=false;l.color=new Color(1,.77f,.46f);l.shadows=LightShadows.None;lamps.Add(l);
            }
            void Bin(float s,float d)
            {
                var p=Point(s,d);var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                Box("Park litter bin",p+Vector3.up*.43f,new Vector3(.54f,.82f,.54f),q,wood,true);
                Box("Bin rim",p+Vector3.up*.87f,new Vector3(.64f,.10f,.64f),q,metal);
            }
            void FlowerBed(float s,float d,int seed)
            {
                var rng=new System.Random(seed);var p=Point(s,d);var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                Box("Flower bed soil",p+Vector3.up*.08f,new Vector3(3.5f,.12f,1.15f),q,soil);
                for(int i=0;i<27;i++)
                {
                    float x=(float)rng.NextDouble()*3.2f-1.6f,z=(float)rng.NextDouble()*.9f-.45f;var b=p+q*new Vector3(x,0,z);b.y=Height(b.x,b.z);
                    Shape(cube,b+Vector3.up*.15f,new Vector3(.13f,.30f,.13f),q,leaf);
                    Shape(cube,b+Vector3.up*(.30f+(float)rng.NextDouble()*.12f),new Vector3(.19f,.16f,.19f),Quaternion.Euler(0,(float)rng.NextDouble()*90,0),i%4==0?flowerWhite:i%4==1?flowerGold:flowerPink);
                }
                park.flowerBeds++;
            }
            Mesh Faceted()
            {
                float t=(1+Mathf.Sqrt(5))*.5f;var v=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
                    new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                    new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
                int[] triangles={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
                    3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
                var m=new Mesh{name="Park • shared low-poly shrub and stone"};m.vertices=v.Select(p=>p.normalized).ToArray();m.triangles=triangles;m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);return m;
            }
            void Shrub(float s,float d,int seed)
            {
                var p=Point(s,d);var q=Quaternion.Euler(0,seed*67,0);
                Shape(faceted,p+q*new Vector3(-.30f,.30f,0),new Vector3(.58f,.39f,.48f),q,leaf);
                Shape(faceted,p+q*new Vector3(.29f,.31f,.06f),new Vector3(.55f,.42f,.48f),q,leafLight);
                Shape(faceted,p+Vector3.up*.46f,new Vector3(.48f,.45f,.42f),q,leaf);park.shrubs++;
            }
            void SmallRock(float s,float d,int seed)
            {
                var p=Point(s,d);var q=Quaternion.Euler(0,seed*53,0);var size=new Vector3(.36f+(seed%3)*.07f,.28f,.29f+(seed%2)*.1f);
                Shape(faceted,p+Vector3.up*.12f,size,q,stone);park.smallRocks++;
            }
            void Tree(float s,float d,int variant,float scale)
            {
                var templateRoot=root.parent.Find("Árvores • 24 variações • LOD");if(!templateRoot)return;
                var choices=templateRoot.GetComponentsInChildren<LODGroup>(true);if(choices.Length==0)return;
                var donor=choices[Mathf.Abs(variant*31)%choices.Length];var p=Point(s,d);
                if(Physics.CheckCapsule(p+Vector3.up*.3f,p+Vector3.up*2,.6f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))return;
                var o=Object.Instantiate(donor.gameObject,root);o.name="Park shade tree • "+park.trees;o.transform.position=p;o.transform.rotation=Quaternion.Euler(0,variant*73,0);o.transform.localScale=Vector3.one*scale;o.SetActive(true);
                var collision=new GameObject("Park tree trunk");collision.transform.SetParent(physical,false);collision.transform.position=p+Vector3.up*1.2f;collision.layer=CityContext.WorldLayer;collision.AddComponent<BoxCollider>().size=new Vector3(.65f,2.4f,.65f);collision.isStatic=true;park.trees++;
            }
            void Playground()
            {
                const float s=18,d=.1f;park.playground=Point(s,d);Oval("Playground • soft sand",s,d,7.0f,3.15f,sand,.07f,true);
                var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                foreach(float side in new[]{-1f,1f})foreach(float end in new[]{-1f,1f})
                {var foot=Point(s-2.8f+end*2.0f,d+side*1.0f);var top=Point(s-2.8f+end*2.0f,d,2.8f);Bar(foot,top,.075f,wood);
                    var post=new GameObject("Swing support");post.transform.SetParent(physical,false);post.transform.position=(foot+top)*.5f;post.transform.rotation=Quaternion.FromToRotation(Vector3.up,top-foot);post.layer=CityContext.WorldLayer;post.AddComponent<BoxCollider>().size=new Vector3(.16f,Vector3.Distance(foot,top),.16f);post.isStatic=true;}
                Bar(Point(s-4.8f,d,2.8f),Point(s-.8f,d,2.8f),.085f,wood);
                foreach(float dx in new[]{-3.5f,-2.1f})
                {var seat=Point(s+dx,d,.66f);Box("Swing seat",seat,new Vector3(.82f,.12f,.32f),q,metal,true);Bar(seat+q*new Vector3(-.32f,0,0),Point(s+dx-.32f,d,2.75f),.018f,metal);Bar(seat+q*new Vector3(.32f,0,0),Point(s+dx+.32f,d,2.75f),.018f,metal);}
                float slide=s+3.7f;var basePoint=Point(slide,d);foreach(float x in new[]{-1f,1f})foreach(float z in new[]{-.8f,.8f})Box("Play tower post",basePoint+q*new Vector3(x,1.05f,z),new Vector3(.14f,2.1f,.14f),q,wood,true);
                Box("Play tower deck",basePoint+Vector3.up*1.25f,new Vector3(2.2f,.18f,1.9f),q,wood,true);
                var a=basePoint+q*new Vector3(1.0f,1.3f,0);var b=basePoint+q*new Vector3(3.5f,.16f,0);var delta=b-a;
                Box("Playground slide",(a+b)*.5f,new Vector3(delta.magnitude,.12f,1.0f),Quaternion.FromToRotation(q*Vector3.right,delta)*q,bright,true);
                Box("Play tower roof",basePoint+Vector3.up*2.85f,new Vector3(2.45f,.24f,2.1f),q,play);
            }
            void Pergola()
            {
                const float s=40,d=.1f;park.pergola=Point(s,d);Oval("Pergola plaza • stone",s,d,4.7f,3.05f,walk,.085f,true);var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                foreach(float x in new[]{-2.6f,2.6f})foreach(float z in new[]{-1.5f,1.5f})
                {var p=Point(s+x,d+z);Box("Pergola column",p+Vector3.up*1.45f,new Vector3(.19f,2.9f,.19f),q,wood,true);}
                for(float z=-1.65f;z<=1.7f;z+=.55f)Box("Pergola cross beam",Point(s,d+z,3.0f),new Vector3(5.8f,.16f,.16f),q,wood);
                foreach(float x in new[]{-2.8f,2.8f})Box("Pergola long beam",Point(s+x,d,2.87f),new Vector3(.18f,.2f,3.6f),q,wood);
            }
            void Exercise()
            {
                const float s=77,d=.2f;park.exercise=Point(s,d);Oval("Outdoor gym • rubber floor",s,d,5.0f,3.05f,play,.07f,true);
                var q=Quaternion.LookRotation(Normal(s),Vector3.up);
                foreach(float x in new[]{-3.1f,-1.3f,1.3f,3.1f})
                {var p=Point(s+x,d);Box("Exercise post",p+Vector3.up*1.1f,new Vector3(.13f,2.2f,.13f),q,metal,true);}
                Bar(Point(s-3.1f,d,2.15f),Point(s-1.3f,d,2.15f),.055f,bright);
                Bar(Point(s+1.3f,d,1.35f),Point(s+3.1f,d,1.35f),.055f,bright);
                Bar(Point(s+1.3f,d,1.85f),Point(s+3.1f,d,1.85f),.055f,bright);
            }
            void Flush()
            {
                foreach(var item in batches)
                {var mesh=new Mesh{name="Park scenery • "+item.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(item.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.AddObjectToAsset(mesh,library);
                    var o=new GameObject(mesh.name);o.transform.SetParent(root,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=item.Key;o.isStatic=true;}
            }
            public void Build()
            {
                var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=temp.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(temp);
                temp=GameObject.CreatePrimitive(PrimitiveType.Cylinder);round=temp.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(temp);
                faceted=Faceted();
                var terrain=GameObject.Find("s-surface:0:0-843");if(!terrain||!terrain.GetComponent<Renderer>())throw new InvalidOperationException("Material do terreno existente ausente.");
                grass=terrain.GetComponent<Renderer>().sharedMaterial;walk=Mat("Park • pale limestone",new Color(.78f,.71f,.59f));edge=Mat("Park • light paving edge",new Color(.87f,.82f,.70f));
                bike=Mat("Park • terracotta cycle lane",new Color(.63f,.30f,.25f));bikeMark=Mat("Park • lane markings",new Color(.94f,.87f,.74f));
                wood=Mat("Park • warm wood",new Color(.43f,.25f,.13f));metal=Mat("Park • dark iron",new Color(.10f,.14f,.14f));
                leaf=Mat("Park • foliage",new Color(.30f,.48f,.19f));leafLight=Mat("Park • light foliage",new Color(.39f,.53f,.22f));stone=Mat("Park • weathered stone",new Color(.50f,.53f,.42f));soil=Mat("Park • soil",new Color(.25f,.17f,.10f));
                flowerPink=Mat("Park • pink flowers",new Color(.91f,.43f,.55f));flowerWhite=Mat("Park • white flowers",new Color(.96f,.91f,.77f));flowerGold=Mat("Park • yellow flowers",new Color(.97f,.72f,.28f));
                sand=Mat("Park • playground sand",new Color(.75f,.60f,.38f));play=Mat("Park • warm play surface",new Color(.62f,.34f,.27f));bright=Mat("Park • ochre playground equipment",new Color(.94f,.66f,.18f));glow=Mat("Park • lantern glass",new Color(1,.79f,.45f),true);
                TrimCover();foreach(int i in Places)PlotGround(world.places[i]);Physics.SyncTransforms();
                var pedestrian=new List<Vector3>();var cycle=new List<Vector3>();
                for(float s=2;s<length-2;s+=2){pedestrian.Add(At(s,WalkOffset(s)));cycle.Add(At(s,5.05f));}
                Ribbon("Pedestrian path • border",pedestrian.ToArray(),3.15f,.10f,edge,false,false);
                Ribbon("Pedestrian path",pedestrian.ToArray(),2.72f,.14f,walk,true,true);
                Ribbon("Cycle lane • border",cycle.ToArray(),2.65f,.10f,edge,false,false);
                Ribbon("Cycle lane",cycle.ToArray(),2.28f,.14f,bike,true,true);
                for(float s=3;s<length-3;s+=4)Ribbon("Cycle lane • dashed center",new[]{At(s,5.05f),At(s+1.6f,5.05f)},.08f,.17f,bikeMark,false,false);
                for(int i=0;i<pedestrian.Count;i++){var p=pedestrian[i];p.y=Height(p.x,p.z)+.14f;nav.Add(p);if(i>0)links.Add(new CityLinearPark.NavLink{a=i-1,b=i});}
                int[] external={5510,5478,5430,5394,5378};float[] branch={8,29,49,69,89};
                for(int i=0;i<external.Length;i++)
                {
                    int local=Mathf.Clamp(Mathf.RoundToInt((branch[i]-2)/2),0,pedestrian.Count-1);var existing=world.nav.FirstOrDefault(n=>n.id==external[i]);
                    if(existing==null)throw new InvalidOperationException("Ligação de calçada ausente: "+external[i]);
                    var end=new Vector3(existing.x,0,existing.z);Ribbon("Sidewalk connection "+(i+1),new[]{pedestrian[local],end},2.25f,.16f,walk,true,true);
                    links.Add(new CityLinearPark.NavLink{a=local,b=-1,externalId=external[i]});
                }
                foreach(float s in new[]{8f,89f})
                {int local=Mathf.Clamp(Mathf.RoundToInt((s-2)/2),0,pedestrian.Count-1);var end=At(s,-18f);Ribbon("Lawn connection "+s,new[]{pedestrian[local],end},2.45f,.14f,walk,true,true);
                    int index=nav.Count;var p=end;p.y=Height(p.x,p.z)+.14f;nav.Add(p);links.Add(new CityLinearPark.NavLink{a=local,b=index});}
                Playground();Pergola();Exercise();
                foreach(float s in new[]{5f,32f,58f,94f})Bench(s,-7.0f);
                foreach(float s in new[]{2.5f,18f,27f,39f,55f,72f,80f,97f})Lamp(s,-7.0f);
                foreach(float s in new[]{6f,59f,82f})Bin(s,-8.0f);
                foreach(float s in new[]{13f,35f,51f,67f,85f})FlowerBed(s,-7.0f,121+(int)s);
                foreach(float s in new[]{17f,38f,58f,78f})FlowerBed(s,2.9f,241+(int)s);
                Tree(3,-10.5f,2,.88f);Tree(20,-10.3f,5,1.04f);Tree(35,-10.6f,7,.94f);Tree(54,-10.4f,9,1.06f);Tree(73,-10.4f,13,.92f);Tree(96,-10.2f,17,1.03f);
                Tree(35,1.8f,6,.85f);Tree(62,1.9f,11,.91f);
                foreach(float s in new[]{13f,29f,47f,64f,81f})Shrub(s,-9.0f,(int)s);
                foreach(float s in new[]{11f,26f,45f,66f,83f})SmallRock(s,-11.8f,(int)s);
                Flush();park.paths=paths.ToArray();park.seats=seats.ToArray();park.seatNodes=seatNodes.ToArray();park.lights=lamps.ToArray();park.navPoints=nav.ToArray();park.navLinks=links.ToArray();
            }
        }
    }
}
