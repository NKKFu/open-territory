using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityLinearParkProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name,detail;public bool passed; }
        [Serializable] public sealed class Report { public bool passed=true;public string unityVersion,graphicsApi;public int samples;public List<Check> checks=new List<Check>();public List<string> errors=new List<string>(); }
        readonly Report report=new Report();CityContext ctx;CityLinearPark park;Vector2 move;string dir;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-linear-park-qa")>=0)new GameObject("Linear park QA • opt-in").AddComponent<CityLinearParkProbe>();}
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message+"\n"+stack);}
        void Update(){if(ctx&&!finished)ctx.Input.TouchMove=move;}
        void Test(string name,bool ok,string detail=""){report.passed&=ok;report.checks.Add(new Check{name=name,passed=ok,detail=detail});Debug.Log("LINEAR_PARK_QA "+name+" "+ok+" "+detail);File.WriteAllText(Path.Combine(dir,"linear-park-qa.json"),JsonUtility.ToJson(report,true));}
        Vector3 Feet(Vector3 p){p.y=ctx.Ground(p.x,p.z)+.06f;return p;}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");dir=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.temporaryCachePath,"linear-park-qa");Directory.CreateDirectory(dir);
            report.unityVersion=Application.unityVersion;report.graphicsApi=SystemInfo.graphicsDeviceType.ToString();
            float timeout=Time.realtimeSinceStartup+90;while((!CityContext.Instance||!CityContext.Instance.Ready)&&Time.realtimeSinceStartup<timeout)yield return null;
            ctx=CityContext.Instance;park=FindFirstObjectByType<CityLinearPark>();Test("native-city-and-park-ready",ctx&&ctx.Ready&&park);
            if(!ctx||!ctx.Ready||!park){Finish();yield break;}
            Test("isolated-review-storage",CityCommerce.IsReviewRun);
            var homes=FindFirstObjectByType<CityHomeCatalog>();
            Test("five-selected-residences-retired",CityLinearPark.RetiredPlaces.All(i=>ctx.Data.places[i].kind=="retired"&&!homes.homes.Any(h=>h.place==i))&&park.retiredObjects.All(r=>r.target&&!r.target.activeSelf));
            Test("all-lot-solids-retired",park.retiredSolidSlots.All(i=>!ctx.Data.solids[i].active),"solids="+park.retiredSolidSlots.Length);
            var city=FindFirstObjectByType<CityLodManager>().transform;var grouped=city.Find("Novo mapa • geometria agrupada");
            Test("neighboring-homes-and-waterfront-preserved",new[]{61,19}.All(i=>homes.homes.Any(h=>h.place==i&&h.root&&h.root.gameObject.activeInHierarchy)&&grouped.Find("building:Reused residential interior "+(i-17)).gameObject.activeInHierarchy)&&FindFirstObjectByType<CityWaterfront>());
            Test("playground-pergola-gym-and-gardens-authored",park.playground!=Vector3.zero&&park.pergola!=Vector3.zero&&park.exercise!=Vector3.zero&&park.flowerBeds>=5&&park.trees>=4&&park.shrubs>=5&&park.smallRocks>=5&&park.seats.Length>=4,
                "trees="+park.trees+" shrubs="+park.shrubs+" stones="+park.smallRocks+" beds="+park.flowerBeds+" seats="+park.seats.Length);
            Test("benches-use-normal-city-actions",park.seats.All(s=>ctx.Actions.Any(a=>a.id=="seat-"+s.id)));
            Test("pedestrian-graph-connects-to-five-existing-sidewalk-nodes",park.navLinks.Count(l=>l.b<0)==5&&park.navLinks.Where(l=>l.b<0).All(l=>ctx.Data.nav.Any(n=>n.id==l.externalId&&n.edges.Length>1)),"park nodes="+park.navPoints.Length);
            int unsupported=0,blocked=0,buried=0;var obstacles=new HashSet<string>();var buriedDetails=new HashSet<string>();
            foreach(var path in park.paths)
                for(int i=1;i<path.points.Length;i++)
                {
                    var a=path.points[i-1];var b=path.points[i];var direction=CityPhysics.Flat(b-a).normalized;var side=new Vector3(-direction.z,0,direction.x);
                    int steps=Mathf.CeilToInt(CityPhysics.Flat(b-a).magnitude/.55f);
                    for(int j=0;j<=steps;j++)foreach(float lane in new[]{-.65f,0f,.65f})
                    {
                        var p=Vector3.Lerp(a,b,j/(float)Mathf.Max(1,steps))+side*lane;
                        if(!Physics.Raycast(p+Vector3.up*5,Vector3.down,out var hit,10,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore)){unsupported++;continue;}
                        p.y=hit.point.y+.06f;report.samples++;if(!hit.transform.IsChildOf(park.transform)){buried++;buriedDetails.Add(path.name+":"+hit.collider.name+"@"+p.ToString("F1"));}
                        foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.43f,p+Vector3.up*1.65f,.32f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))
                        {blocked++;obstacles.Add(path.name+":"+c.name);}
                    }
                }
            Test("all-paths-supported-and-clear",unsupported==0&&blocked==0&&buried==0,"samples="+report.samples+" unsupported="+unsupported+" blocked="+blocked+" buried="+buried+" "+string.Join(",",obstacles.Take(12))+" terrain: "+string.Join(",",buriedDetails.Take(6)));
            ctx.Combat.Holster();ctx.Follow.enabled=false;ctx.Systems.Weather.Request("clear",true);ctx.Clock=0;ctx.Motor.SetInputLock(true);
            ctx.Motor.Warp(Feet(park.paths[0].points[0]));yield return new WaitForSeconds(.35f);
            Capture("01-park-overview",new Vector3(520,100,515),new Vector3(532,7,602),58,true);
            Capture("02-park-and-homes",new Vector3(497,40,554),new Vector3(531,8,604),59,true);
            Capture("03-playground",park.playground+new Vector3(-14,6,-15),park.playground+Vector3.up,68,false);
            Capture("04-pergola-and-gym",park.pergola+new Vector3(-8,5,-13),park.pergola+Vector3.up*1.2f,68,false);
            Capture("05-pedestrian-view",park.paths[0].points[9]+Vector3.up*1.85f,park.paths[0].points[16]+Vector3.up*1.15f,68,false);
            if(RenderSettings.sun){var shadows=RenderSettings.sun.shadows;RenderSettings.sun.shadows=LightShadows.None;
                Capture("diagnostic-no-sun-shadows",new Vector3(497,40,554),new Vector3(531,8,604),59,true);RenderSettings.sun.shadows=shadows;}
            ctx.Population.enabled=false;foreach(var resident in ctx.Residents.People.ToArray())ctx.Residents.Despawn(resident);
            ctx.Motor.SetInputLock(false);ctx.Camera.transform.rotation=Quaternion.identity;ctx.Input.TouchRun=true;
            foreach(var path in park.paths)
            {
                ctx.Motor.Warp(Feet(path.points[0]));yield return new WaitForSeconds(.12f);bool walked=true;string failure="";
                for(int i=1;i<path.points.Length;i++)
                {
                    var target=path.points[i];float deadline=Time.realtimeSinceStartup+CityPhysics.Flat(target-ctx.Player.position).magnitude/3+3;
                    while(CityPhysics.Flat(target-ctx.Player.position).magnitude>.42f&&Time.realtimeSinceStartup<deadline)
                    {var d=CityPhysics.Flat(target-ctx.Player.position).normalized;move=new Vector2(d.x,d.z);yield return null;}
                    if(CityPhysics.Flat(target-ctx.Player.position).magnitude>.95f){walked=false;failure=" target="+i+"/"+target+" gap="+CityPhysics.Flat(target-ctx.Player.position).magnitude.ToString("F2");break;}
                }
                move=Vector2.zero;yield return new WaitForSeconds(.8f);Test("controller-walks-"+path.name,walked&&ctx.Motor.Grounded,"position="+ctx.Player.position+" grounded="+ctx.Motor.Grounded+failure);
            }
            ctx.Input.TouchRun=false;
            var seat=park.seats[0];var front=Quaternion.Euler(0,seat.yaw*Mathf.Rad2Deg,0)*Vector3.forward;var approach=Feet(seat.position+front*1.25f);
            ctx.Motor.Warp(approach);yield return new WaitForSeconds(.25f);ctx.Camera.transform.position=approach+front*.3f+Vector3.up*2.4f;ctx.Camera.transform.LookAt(seat.position+Vector3.up*.9f);ctx.RefreshAction();
            bool selected=ctx.CurrentAction!=null&&ctx.CurrentAction.id=="seat-"+seat.id;ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);bool sat=ctx.Motor.SeatHeight.HasValue;
            ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);Test("bench-sit-and-stand",selected&&sat&&!ctx.Motor.SeatHeight.HasValue&&ctx.Motor.Grounded,"selected="+selected+" sat="+sat);
            ctx.Population.enabled=false;ctx.Motor.Warp(Feet(park.pergola+Vector3.forward*7));
            var person=ctx.Residents.People.FirstOrDefault(p=>!p.Data.stationary&&!p.Data.sitting&&!p.Down);
            if(person==null)
            {
                var data=ctx.Data.residents.FirstOrDefault(p=>!p.stationary&&!p.sitting&&!p.indoor&&ctx.Node(p.id));
                if(data!=null&&ctx.Residents.Spawn(data.id,Feet(park.paths[0].points[5]),Quaternion.identity))person=ctx.Residents.Find(data.id);
            }
            bool npc=false;
            if(person!=null)
            {
                ctx.Residents.Control(person,true);var start=Feet(park.paths[0].points[5]);person.Controller.enabled=false;person.Root.position=start;person.Controller.enabled=true;
                person.LastPosition=start;person.Velocity=Vector3.zero;person.Steering=Vector3.zero;person.MoveSpeed=0;person.Path.Clear();person.PathIndex=0;
                var goal=Feet(park.paths[0].points[15]);float end=ctx.Clock+30;
                while(ctx.Clock<end&&!(npc=ctx.Residents.MoveToward(person,goal,Time.deltaTime,1.9f)))yield return null;
                Test("resident-walks-park-route",npc,"remaining="+CityPhysics.Flat(person.Root.position-goal).magnitude.ToString("F2"));
            }
            else Test("resident-walks-park-route",false,"No resident available");
            ctx.Motor.SetInputLock(true);ctx.Motor.Warp(Feet(park.paths[0].points[15]));ctx.Clock=168;yield return new WaitForSeconds(.75f);
            Test("park-lanterns-light-at-night",park.lights.Length>=8&&ctx.Night>.9f&&park.lights.All(l=>l&&l.enabled&&l.intensity>.5f));
            Capture("06-park-at-night",park.pergola+new Vector3(-8,5,-13),park.pergola+Vector3.up*1.2f,68,false);
            Test("no-runtime-errors",report.errors.Count==0,string.Join("\n",report.errors.Take(3)));Finish();
        }
        void Capture(string name,Vector3 eye,Vector3 target,float fov,bool overview)
        {
            var camera=ctx.Camera;camera.transform.position=eye;camera.transform.LookAt(target);camera.fieldOfView=fov;
            bool fog=RenderSettings.fog;if(overview)RenderSettings.fog=false;
            var lod=FindFirstObjectByType<CityLodManager>();if(lod)foreach(var item in lod.objects)if(item&&item.isActiveAndEnabled)item.ApplyLevel(item.SelectLevel(camera));
            var old=RenderTexture.active;var rt=new RenderTexture(1600,900,24);rt.antiAliasing=2;rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;RenderSettings.fog=fog;rt.Release();Destroy(rt);Destroy(image);
        }
        void Finish(){finished=true;if(ctx)ctx.Input.TouchMove=Vector2.zero;File.WriteAllText(Path.Combine(dir,"linear-park-qa.json"),JsonUtility.ToJson(report,true));if(!Application.isEditor)Application.Quit(report.passed?0:2);}
    }
}
