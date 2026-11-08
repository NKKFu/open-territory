using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityParkProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed=true; public int routeSamples; public string graphicsApi,unityVersion;
            public List<Check> checks=new List<Check>(); public List<string> errors=new List<string>();
        }
        readonly Report report=new Report();CityContext ctx;CityPark park;Vector2 move;string dir;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        { if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-park-qa")>=0)new GameObject("Park QA • opt-in").AddComponent<CityParkProbe>(); }
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message+"\n"+stack);}
        void Update(){if(ctx&&!finished)ctx.Input.TouchMove=move;}
        void Test(string name,bool passed,string detail="")
        {report.passed&=passed;report.checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("PARK_QA "+name+" "+passed+" "+detail);}
        Vector3 Feet(Vector3 p){p.y=ctx.Ground(p.x,p.z)+.06f;return p;}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");
            dir=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Reports/park/editor"));Directory.CreateDirectory(dir);
            report.graphicsApi=SystemInfo.graphicsDeviceType.ToString();report.unityVersion=Application.unityVersion;
            yield return new WaitForSeconds(1.5f);ctx=CityContext.Instance;park=FindFirstObjectByType<CityPark>();
            Test("native-park-and-city-ready",ctx&&ctx.Ready&&park);if(!ctx||!ctx.Ready||!park){Finish();yield break;}
            Test("park-features-authored-in-scene",park.walkways.Length==6&&park.seats.Length==10&&park.plantedTrees>=8&&park.flowerBeds>=5,
                "paths="+park.walkways.Length+" benches="+park.seats.Length+" trees="+park.plantedTrees+" beds="+park.flowerBeds);
            Test("benches-bound-to-usual-E-actions",park.seats.All(s=>ctx.Actions.Any(a=>a.id=="seat-"+s.id)));
            // The park retains every original natural object; no manifest solid is hidden.
            int retained=0,missing=0;
            foreach(var tree in ctx.Data.natureTrees)
            {
                var p=tree.position;if(p.x<165||p.x>270||p.z<360||p.z>460)continue;
                var solid=ctx.Data.solids.FirstOrDefault(s=>s.index==tree.solidIndex);
                if(solid!=null&&solid.active)retained++;else missing++;
            }
            Test("original-trees-retained",retained>8&&missing==0,"retained="+retained+" missing="+missing);
            int unsupported=0,blocked=0;var obstacles=new HashSet<string>();
            foreach(var path in park.walkways)
                for(int i=1;i<path.points.Length;i++)
                {
                    var a=path.points[i-1];var b=path.points[i];var direction=CityPhysics.Flat(b-a).normalized;var side=new Vector3(-direction.z,0,direction.x);
                    int steps=Mathf.CeilToInt(Vector3.Distance(a,b)/.45f);
                    for(int j=0;j<=steps;j++)foreach(float lane in new[]{-.75f,0,.75f})
                    {
                        var p=Vector3.Lerp(a,b,j/(float)Mathf.Max(1,steps))+side*lane;
                        if(!Physics.Raycast(p+Vector3.up*3,Vector3.down,out var hit,6,1<<CityContext.GroundLayer)){unsupported++;continue;}
                        p.y=hit.point.y+.07f;report.routeSamples++;
                        var hits=Physics.OverlapCapsule(p+Vector3.up*.43f,p+Vector3.up*1.65f,.32f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore);
                        if(hits.Length>0){blocked++;foreach(var h in hits)obstacles.Add(path.name+":"+h.name);}
                    }
                }
            Test("continuous-wide-path-support-and-clearance",unsupported==0&&blocked==0,"samples="+report.routeSamples+" unsupported="+unsupported+" blocked="+blocked+" "+string.Join(",",obstacles.Take(12)));
            ctx.Combat.Holster();ctx.Follow.enabled=false;ctx.Systems.Weather.Request("clear",true);ctx.Clock=0;ctx.Motor.SetInputLock(true);
            ctx.Motor.Warp(Feet(park.entrances[0]));yield return new WaitForSeconds(.35f);
            Capture("01-park-overview",new Vector3(-75,105,-105),new Vector3(0,9,0),56,true);
            Capture("02-west-entrance",new Vector3(-47,12.2f,28),new Vector3(-25,10.4f,10),70);
            Capture("03-picnic-grove",new Vector3(-18,11.7f,0),new Vector3(-8,11,-9),70);
            Capture("04-garden-promenade",new Vector3(-30,11.8f,0),new Vector3(15,10.2f,8),70);
            ctx.Motor.SetInputLock(false);ctx.Camera.transform.rotation=Quaternion.identity;ctx.Input.TouchRun=true;
            foreach(var path in park.walkways)
            {
                ctx.Motor.Warp(Feet(path.points[0]));yield return new WaitForSeconds(.15f);bool walked=true;
                for(int i=1;i<path.points.Length;i++)
                {
                    var target=path.points[i];float end=Time.realtimeSinceStartup+CityPhysics.Flat(target-ctx.Player.position).magnitude/3+3;
                    while(CityPhysics.Flat(target-ctx.Player.position).magnitude>.40f&&Time.realtimeSinceStartup<end)
                    {var d=CityPhysics.Flat(target-ctx.Player.position).normalized;move=new Vector2(d.x,d.z);yield return null;}
                    if(CityPhysics.Flat(target-ctx.Player.position).magnitude>.85f){walked=false;break;}
                }
                move=Vector2.zero;yield return new WaitForSeconds(.18f);
                Test("real-controller-walks-"+path.name,walked&&ctx.Motor.Grounded,"position="+park.transform.InverseTransformPoint(ctx.Player.position));
            }
            ctx.Input.TouchRun=false;
            ctx.Motor.Warp(Feet(park.walkways[0].points[0]));yield return new WaitForSeconds(.2f);float y=ctx.Player.position.y;ctx.Input.TouchJump=true;
            yield return new WaitForSeconds(.35f);bool airborne=ctx.Player.position.y>y+.2f;yield return new WaitForSeconds(1.5f);
            Test("jump-and-land-on-path",airborne&&ctx.Motor.Grounded);
            foreach(var seat in park.seats)
            {
                var front=Quaternion.Euler(0,seat.yaw*Mathf.Rad2Deg,0)*Vector3.forward;var approach=Feet(seat.position+front*1.25f);ctx.Motor.Warp(approach);yield return new WaitForSeconds(.18f);
                ctx.Camera.transform.position=approach+front*.3f+Vector3.up*2.6f;ctx.Camera.transform.LookAt(seat.position+Vector3.up*.9f);ctx.RefreshAction();
                bool selected=ctx.CurrentAction!=null&&ctx.CurrentAction.id=="seat-"+seat.id;ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);bool sat=ctx.Motor.SeatHeight.HasValue;
                ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);
                Test("E-sit-and-stand-"+seat.id,selected&&sat&&!ctx.Motor.SeatHeight.HasValue&&ctx.Motor.Grounded,"selected="+selected+" sat="+sat);
            }
            ctx.UI.Map.SetOpen(true);yield return new WaitForSeconds(.2f);float clock=ctx.Clock;
            Test("park-named-on-map",ctx.UI.Map.Places.Any(p=>p.name==CityPark.DisplayName&&p.kind=="park"));yield return new WaitForSeconds(.25f);
            Test("map-keeps-city-running",!ctx.Paused&&ctx.Clock>clock+.1f);ctx.UI.Map.SetOpen(false);
            ctx.Motor.SetInputLock(true);ctx.Motor.Warp(Feet(park.picnicCenter+park.transform.forward*8));ctx.Clock=168;yield return new WaitForSeconds(.6f);
            var lights=park.GetComponentsInChildren<Light>();Test("park-lanterns-light-at-night",lights.Length>=8&&ctx.Night>.9f&&lights.All(l=>l.enabled&&l.intensity>.5f));
            Capture("05-park-night",new Vector3(-38,23,-39),new Vector3(-4,10,0),64);
            ctx.UI.SetSettings(true);clock=ctx.Clock;var position=ctx.Player.position;yield return new WaitForSecondsRealtime(.35f);
            Test("Esc-settings-pause-world",ctx.Paused&&ctx.UI.Open&&Mathf.Abs(ctx.Clock-clock)<.001f&&Vector3.Distance(position,ctx.Player.position)<.001f);
            ctx.UI.SetSettings(false);yield return new WaitForSeconds(.2f);Test("resume-exploration",!ctx.Paused&&ctx.Clock>clock+.08f);
            Test("no-game-runtime-errors",report.errors.Count==0,string.Join("\n",report.errors.Take(3)));Finish();
        }
        void Capture(string name,Vector3 eye,Vector3 target,float fov,bool overview=false)
        {
            var camera=ctx.Camera;camera.transform.position=park.transform.TransformPoint(eye);camera.transform.LookAt(park.transform.TransformPoint(target));camera.fieldOfView=fov;
            bool fog=RenderSettings.fog;if(overview)RenderSettings.fog=false;
            var lod=FindFirstObjectByType<CityLodManager>();if(lod)foreach(var item in lod.objects)if(item&&item.isActiveAndEnabled)item.ApplyLevel(item.SelectLevel(camera));
            var old=RenderTexture.active;var rt=new RenderTexture(1600,1000,24);rt.antiAliasing=2;rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;RenderSettings.fog=fog;rt.Release();Destroy(rt);Destroy(image);
        }
        void Finish(){finished=true;if(ctx){move=Vector2.zero;ctx.Input.TouchMove=move;}File.WriteAllText(Path.Combine(dir,"park-qa.json"),JsonUtility.ToJson(report,true));if(!Application.isEditor)Application.Quit(report.passed?0:2);}
    }
}
