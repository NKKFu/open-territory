using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Scene-specific integration checks. Installed only by an explicit QA flag.
    [DefaultExecutionOrder(-1200)]
    public sealed class ReferenceMapProbe:MonoBehaviour
    {
        [Serializable]public class Check{public string name,detail;public bool passed;public float value;}
        [Serializable]public class Report{public bool passed=true;public string layout;public List<Check> checks=new List<Check>();public List<string> errors=new List<string>();}
        readonly Report report=new Report();CityContext ctx;Vector2 move;bool interact,brake;string dir;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--city-reference-qa")>=0||Array.IndexOf(args,"--city-nature-qa")>=0)new GameObject("QA do mapa traçado • opt-in").AddComponent<ReferenceMapProbe>();}
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message);}
        void Update(){if(!ctx||finished)return;ctx.Input.TouchMove=move;ctx.Input.TouchBrake=brake;if(interact){ctx.Input.TouchInteract=true;interact=false;}}
        void CheckResult(string name,bool passed,float value=0,string detail=""){report.passed&=passed;report.checks.Add(new Check{name=name,passed=passed,value=value,detail=detail});Debug.Log("REFERENCE_QA "+name+" "+passed+" "+value+" "+detail);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");dir=at>=0&&at+1<args.Length?args[at+1]:Application.persistentDataPath;Directory.CreateDirectory(dir);
            float end=Time.realtimeSinceStartup+120;while((!CityContext.Instance||!CityContext.Instance.Ready)&&Time.realtimeSinceStartup<end)yield return null;
            ctx=CityContext.Instance;if(!ctx||!ctx.Ready){CheckResult("scene-ready",false);Finish();yield break;}
            report.layout=ctx.Data.layoutId;CheckResult("only-traced-map-active",ReferenceMap.Active&&ctx.Data.layoutId=="marin-reference-v2"&&GameObject.Find("Costa norte • enseadas e vilas")==null);
            CheckResult("reference-coast-and-44-traces",ReferenceMap.Layout.coast.Length>400&&ReferenceMap.Layout.roads.Length==44,ReferenceMap.Layout.roads.Length);
            // New native shop compounds retire a few exported doors. Their
            // automatic entrances are covered by the owning compound probes.
            var exportedDoors=ctx.Data.places.Where(p=>p.door!=null).ToArray();
            CheckResult("donor-buildings-and-interiors-reused",ctx.Data.places.Length>=100&&exportedDoors.Length>=200&&exportedDoors.All(p=>ctx.Node(p.door.id)),exportedDoors.Length);
            CheckResult("population-and-fleet-reused",ctx.Data.residents.Length>=111&&ctx.Data.vehicles.Length>=38&&ctx.Population.ResidentDefinitions==ctx.Data.residents.Length&&ctx.Population.VehicleDefinitions==ctx.Data.vehicles.Length&&ctx.Marine.Fleet.Count==ctx.Data.boats.Length,ctx.Population.ResidentDefinitions);
            CheckResult("population-excludes-children",ctx.Data.residents.All(p=>!p.child));
            CheckResult("navigation-rebuilt-for-new-streets",ctx.Data.nav.Length>1000&&ctx.Data.roadRoute.Length>300,ctx.Data.nav.Length);
            var trees = GameObject.Find("Árvores • 24 variações • LOD");
            var groups = trees ? trees.GetComponentsInChildren<LODGroup>() : new LODGroup[0];
            CheckResult("vegetation-forest-with-three-lods", groups.Length > 1000 && groups.All(g => g.GetLODs().Length == 3), groups.Length);
            var uniqueMeshes = new HashSet<Mesh>(); bool reduced = true, collisions = true;
            foreach (var group in groups)
            {
                var levels = group.GetLODs(); int previous = int.MaxValue;
                foreach (var level in levels)
                {
                    var mesh = level.renderers[0].GetComponent<MeshFilter>().sharedMesh;
                    reduced &= mesh && mesh.triangles.Length < previous;
                    previous = mesh.triangles.Length; uniqueMeshes.Add(mesh);
                }
                collisions &= Physics.CheckSphere(group.transform.position + Vector3.up * 1.5f, .1f, 1 << CityContext.WorldLayer);
            }
            CheckResult("vegetation-24-shared-variants", uniqueMeshes.Count == 72, uniqueMeshes.Count);
            CheckResult("vegetation-each-lod-reduces-triangles", reduced && groups.Length > 0);
            CheckResult("vegetation-trunks-retain-collision", collisions && groups.Length > 0);
            yield return CityNatureAudit.Run(ctx, (name, passed, detail) => CheckResult(name, passed, 0, detail));
            if(Array.IndexOf(args,"--city-nature-qa")>=0)
            {
                yield return WalkNatureTrail();
                ctx.UI.SetSettings(true);CheckResult("nature-Esc-pause",ctx.Paused&&Time.timeScale==0);ctx.UI.SetSettings(false);
                ctx.Follow.enabled=false;ctx.Systems.Weather.Request("clear",true);yield return new WaitForSeconds(.3f);
                if(ctx.Data.natureGroves.Length==0)CaptureScattered();else{var dense=ctx.Data.natureGroves.OrderByDescending(g=>g.count).First();
                Vector3 center=new Vector3(dense.x,ctx.Ground(dense.x,dense.z),dense.z);
                Capture("01-bosque-denso",center+new Vector3(58,34,-65),center+Vector3.up*4,false);
                Vector3 clearing=new Vector3(dense.clearingX,ctx.Ground(dense.clearingX,dense.clearingZ),dense.clearingZ);
                Capture("02-clareira-preservada",clearing+new Vector3(4,17,-22),clearing,false);
                Capture("03-sub-bosque",center+new Vector3(-22,6,-14),center+Vector3.up*2,false);}
                CheckResult("nature-no-runtime-errors",report.errors.Count==0,report.errors.Count);Finish();yield break;
            }
            yield return CityLodAudit.Run(ctx, (name, passed, detail) => CheckResult(name, passed, 0, detail), dir);
            yield return new WaitForSeconds(1.5f);
            int missing=0,blocked=0,checkedPoints=0,submerged=0;float maxError=0;
            foreach(var road in ReferenceMap.Layout.roads)
            {
                if(road.kind=="trail")continue;
                for(int i=3;i<road.points.Length-3;i+=5)
                {
                    var p=road.points[i];checkedPoints++;
                    if(!Physics.Raycast(p+Vector3.up*10,Vector3.down,out var hit,35,1<<CityContext.GroundLayer)){missing++;continue;}
                    if(!hit.collider.name.StartsWith("Piso-"))missing++;
                    if(road.kind=="street"&&hit.point.y<.5f)submerged++;
                    maxError=Mathf.Max(maxError,Mathf.Abs(p.y-hit.point.y));
                    if(Physics.CheckCapsule(hit.point+Vector3.up*.55f,hit.point+Vector3.up*1.9f,.33f,1<<CityContext.WorldLayer))
                    {blocked++;if(blocked<20)Debug.Log("REFERENCE_BLOCKED "+road.id+" "+p);}
                }
            }
            CheckResult("all-traced-roads-have-ground",missing==0,missing,"samples="+checkedPoints);
            CheckResult("coastal-streets-stay-above-water",submerged==0,submerged);
            CheckResult("road-lanes-clear-of-reused-assets",blocked==0,blocked);
            CheckResult("spawn-on-new-map",ctx.Motor.Grounded&&Vector3.Distance(ctx.Player.position,ctx.Data.spawn)<3,ctx.Player.position.y);
            var start=ctx.Player.position;ctx.Follow.Face(90);move=Vector2.up;yield return new WaitForSeconds(1.2f);move=Vector2.zero;
            CheckResult("native-walking",CityPhysics.Flat(ctx.Player.position-start).magnitude>1.5f,CityPhysics.Flat(ctx.Player.position-start).magnitude);
            yield return Door();yield return Car();yield return Sea();
            ctx.Marine.Fall();ctx.Motor.SetRide(null);move=Vector2.zero;brake=false;ctx.Motor.Warp(ctx.Data.spawn);ctx.Motor.SetInputLock(true);
            ctx.UI.SetSettings(true);CheckResult("Esc-settings-pause-retained",ctx.Paused&&Time.timeScale==0&&ctx.UI.Open);ctx.UI.SetSettings(false);CheckResult("settings-resume-retained",!ctx.Paused&&Time.timeScale==1);
            var npc=ctx.Residents.People.FirstOrDefault(p=>!p.Down&&p.Data.stationary);if(npc!=null){ctx.Residents.Talk(npc);CheckResult("resident-dialogue-retained",!string.IsNullOrWhiteSpace(ctx.Speech));}
            var bank=ctx.Data.services.FirstOrDefault(s=>s.kind=="bank"||s.mode=="ledger");
            var ledger=bank==null?null:ctx.Actions.FirstOrDefault(a=>a.id=="commerce-"+bank.id);
            if(ledger!=null){ctx.Speech="";ledger.Execute();}
            CheckResult("commerce-and-world-actions-retained",ledger!=null&&!string.IsNullOrWhiteSpace(ctx.Speech)&&ctx.Actions.Any(a=>a.id=="fish-1"),ctx.Actions.Count);
            ctx.Follow.enabled=false;ctx.Systems.Weather.Request("clear",true);yield return new WaitForSeconds(.3f);
            Capture("01-planta",ReferenceMap.FromPixel(733,367,2200),ReferenceMap.FromPixel(733,366,0),true);
            Capture("02-costa",ReferenceMap.FromPixel(700,850,1100),ReferenceMap.FromPixel(730,340,0),false);
            Capture("03-bairro",ReferenceMap.FromPixel(1050,440,100),ReferenceMap.FromPixel(1000,350,12),false);
            Capture("04-peninsula",ReferenceMap.FromPixel(60,670,115),ReferenceMap.FromPixel(210,490,12),false);
            Capture("05-interiores",ctx.Data.spawn+new Vector3(12,5,-12),ctx.Data.spawn+Vector3.up*1.4f,false);
            Capture("06-orla-corrigida",new Vector3(-235,175,365),new Vector3(-235,0,540),false);
            if(ctx.Data.natureGroves.Length==0)CaptureScattered();else{var grove=ctx.Data.natureTrees.Where(t=>t.grove>=0).GroupBy(t=>t.grove).OrderByDescending(g=>g.Count()).First();
            Vector3 groveCenter=grove.Aggregate(Vector3.zero,(p,t)=>p+t.position)/grove.Count();
            Capture("07-bosque-natural",groveCenter+new Vector3(58,34,-65),groveCenter+Vector3.up*4,false);}
            var outcrop=ctx.Data.rocks.OrderByDescending(r=>r.scale.x).First();
            Capture("08-afloramentos",outcrop.position+new Vector3(15,8,-19),outcrop.position+Vector3.up*1.5f,false);
            CheckResult("no-runtime-errors-including-startup",report.errors.Count==0,report.errors.Count);Finish();
        }
        IEnumerator Door()
        {
            var p=ctx.Data.places.First(a=>a.kind=="market");var door=ctx.Node(p.door.id);Quaternion rotation=Quaternion.Euler(0,p.door.yaw*Mathf.Rad2Deg,0);Vector3 outside=p.door.Approach(0,2.1f);outside.y=ctx.Ground(outside.x,outside.z)+.05f;
            ctx.Motor.Warp(outside);ctx.Follow.Face(p.door.yaw*Mathf.Rad2Deg+180);yield return new WaitForSeconds(.5f);move=Vector2.up;yield return new WaitForSeconds(1.6f);move=Vector2.zero;
            CheckResult("reused-door-pushes-open",Quaternion.Angle(rotation,door.rotation)>8,Quaternion.Angle(rotation,door.rotation));
            CheckResult("interior-collider-and-floor",ctx.Motor.Grounded&&p.Contains(ctx.Player.position)&&ctx.Player.position.y>p.elevation-.2f,ctx.Player.position.y-p.elevation);
        }
        IEnumerator Car()
        {
            var road=ReferenceMap.Layout.roads.First(a=>a.id=="101-inland");int k=road.points.Length/2;var p=road.points[k];Vector3 forward=CityPhysics.Flat(road.points[k+2]-road.points[k-2]).normalized;
            var definition=ctx.Data.vehicles.First(a=>!a.auto&&a.kind=="car");
            var car=ctx.Vehicles.Fleet.FirstOrDefault(a=>a.Data.id==definition.id)??ctx.Vehicles.Spawn(definition,p+Vector3.up*.06f,Quaternion.LookRotation(forward));car.LastUsed=ctx.Clock;
            car.Root.SetPositionAndRotation(p+Vector3.up*.06f,Quaternion.LookRotation(forward));car.Body.position=p+Vector3.up*.06f;car.Body.rotation=Quaternion.LookRotation(forward);car.Body.linearVelocity=car.Body.angularVelocity=Vector3.zero;car.Body.WakeUp();Physics.SyncTransforms();
            var entrance=car.Root.TransformPoint(new Vector3(-car.Data.width*.5f-.75f,0,car.Data.seatMount.z));entrance.y=ctx.Ground(entrance.x,entrance.z)+.05f;ctx.Motor.Warp(entrance);ctx.Follow.Face(car.Root.eulerAngles.y);yield return new WaitForSeconds(.7f);
            var action=ctx.Actions.Find(a=>a.id=="vehicle-"+car.Data.id);if(action!=null)action.Execute();
            float entryUntil=Time.time+8;while(ctx.Motor.Busy&&Time.time<entryUntil)yield return null;
            bool riding=ctx.Motor.Riding==car.Root&&!ctx.Motor.Busy;CheckResult("reused-vehicle-enter",riding);if(!riding)yield break;
            var start=car.Position;move=Vector2.up;yield return new WaitForSeconds(1.7f);move=Vector2.zero;
            CheckResult("drive-on-new-road",car.Speed>2&&CityPhysics.Flat(car.Position-start).magnitude>2,car.Speed);
            CheckResult("suspension-supported-on-new-road",car.Position.y>ctx.Ground(car.Position.x,car.Position.z)-.3f,car.Position.y-ctx.Ground(car.Position.x,car.Position.z));
            brake=true;yield return new WaitForSeconds(2);brake=false;
            CheckResult("vehicle-brake",Mathf.Abs(car.Speed)<.4f,car.Speed);
            var exit=ctx.Actions.Find(a=>a.id=="vehicle-exit");if(exit!=null)exit.Execute();yield return new WaitForSeconds(1.3f);CheckResult("reused-vehicle-exit",!ctx.Motor.Riding);
            if(ctx.Motor.Riding)ctx.Motor.SetRide(null);
        }
        IEnumerator Sea()
        {
            var boat=ctx.Marine.Fleet[0];var at=boat.Root.position;
            foreach(int side in new[]{-1,1})
            {
                var candidate=boat.Root.position+boat.Root.right*side*(boat.Data.width*.5f+1.2f);
                if(ctx.Marine.DockAt(candidate,.4f)==null&&ctx.Marine.Depth(candidate.x,candidate.z)>1&&ctx.Ground(candidate.x,candidate.z)<CityMarine.SeaLevel-.5f){at=candidate;break;}
            }
            at.y=ctx.Marine.Surface(at.x,at.z)-.5f;ctx.Motor.Warp(at);yield return new WaitForSeconds(.9f);
            CheckResult("swim-in-traced-bay",ctx.Motor.Swimming&&ctx.Marine.Depth(at.x,at.z)>1,ctx.Marine.Depth(at.x,at.z));
            var board=ctx.Actions.Find(a=>a.id=="boat-"+boat.Data.id);if(board!=null)board.Execute();yield return new WaitForSeconds(.3f);CheckResult("reused-boat-board",ctx.Marine.ActiveBoat==boat);
            if(ctx.Marine.ActiveBoat==boat){move=Vector2.up;yield return new WaitForSeconds(1.5f);move=Vector2.zero;CheckResult("reused-boat-underway",Mathf.Abs(ctx.Marine.Speed)>.2f,ctx.Marine.Speed);}ctx.Marine.Fall();
        }
        void Capture(string name,Vector3 eye,Vector3 target,bool plan)
        {
            var cam=ctx.Camera;cam.transform.position=eye;cam.transform.LookAt(target);cam.farClipPlane=6000;cam.orthographic=plan;cam.orthographicSize=950;cam.fieldOfView=65;
            bool fog=RenderSettings.fog;RenderSettings.fog=false;var rt=new RenderTexture(1800,1000,24);rt.Create();var previous=RenderTexture.active;cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var image=new Texture2D(1800,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1800,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=previous;cam.orthographic=false;RenderSettings.fog=fog;rt.Release();Destroy(rt);Destroy(image);
        }
        void CaptureScattered()
        {
            var reference=ReferenceMap.FromPixel(580,285);
            var center=ctx.Data.natureTrees.OrderBy(t=>CityPhysics.Flat(t.position-reference).sqrMagnitude).First().position;
            Capture("01-vegetacao-espalhada",center+new Vector3(80,45,-100),center+Vector3.up*3,false);
            Capture("02-vegetacao-no-chao",center+new Vector3(-14,5,-20),center+Vector3.up*2,false);
            Capture("03-distribuicao-costeira",ctx.Data.spawn+new Vector3(100,85,-150),ctx.Data.spawn+new Vector3(-80,0,120),false);
        }
        IEnumerator WalkNatureTrail()
        {
            foreach(var trail in ReferenceMap.Layout.roads.Where(r=>r.kind=="trail"))
                for(int i=4;i<trail.points.Length-4;i+=7)
                {
                    var direction=trail.points[i+1]-trail.points[i-1];direction.y=0;direction.Normalize();
                    var p=trail.points[i];bool free=true;Vector3 start=Vector3.zero;
                    for(int step=0;step<7;step++)
                    {
                        var q=p+direction*step*.65f;
                        if(!Physics.Raycast(q+Vector3.up*15,Vector3.down,out var hit,40,1<<CityContext.GroundLayer)||Physics.CheckCapsule(hit.point+Vector3.up*.5f,hit.point+Vector3.up*1.9f,.4f,1<<CityContext.WorldLayer)){free=false;break;}
                        if(step==0)start=hit.point+Vector3.up*.05f;
                    }
                    if(!free)continue;
                    ctx.Motor.Warp(start);ctx.Follow.Face(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg);yield return new WaitForSeconds(.4f);
                    var before=ctx.Player.position;move=Vector2.up;yield return new WaitForSeconds(1.3f);move=Vector2.zero;
                    float distance=CityPhysics.Flat(ctx.Player.position-before).magnitude;
                    CheckResult("nature-native-trail-walking",distance>1.5f,distance,trail.id);yield break;
                }
            CheckResult("nature-native-trail-walking",false,0,"no test segment found");
        }
        void Finish(){finished=true;string name=Array.IndexOf(Environment.GetCommandLineArgs(),"--city-nature-qa")>=0?"nature-qa.json":"reference-qa.json";File.WriteAllText(Path.Combine(dir,name),JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:2);}
    }
}
