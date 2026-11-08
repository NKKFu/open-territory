using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    [DefaultExecutionOrder(-1200)]
    public sealed class CityWaterfrontProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed=true;public int routeSamples;public string graphicsApi,unityVersion;
            public List<Check> checks=new List<Check>();public List<string> errors=new List<string>();
        }
        readonly Report report=new Report();CityContext ctx;CityWaterfront waterfront;Vector2 move;string dir;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-waterfront-qa")>=0)new GameObject("Waterfront QA • opt-in").AddComponent<CityWaterfrontProbe>();}
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message+"\n"+stack);}
        void Update(){if(ctx&&!finished)ctx.Input.TouchMove=move;}
        void Test(string name,bool ok,string detail="")
        {report.passed&=ok;report.checks.Add(new Check{name=name,passed=ok,detail=detail});Debug.Log("WATERFRONT_QA "+name+" "+ok+" "+detail);}
        Vector3 Feet(Vector3 p){p.y=ctx.Ground(p.x,p.z)+.06f;return p;}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");
            dir=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Reports/waterfront/editor"));Directory.CreateDirectory(dir);
            report.graphicsApi=SystemInfo.graphicsDeviceType.ToString();report.unityVersion=Application.unityVersion;
            yield return new WaitForSeconds(1.5f);ctx=CityContext.Instance;waterfront=FindFirstObjectByType<CityWaterfront>();
            Test("city-and-waterfront-ready",ctx&&ctx.Ready&&waterfront);if(!ctx||!ctx.Ready||!waterfront){Finish();yield break;}
            var terrain=FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).FirstOrDefault(f=>f.name=="s-surface:0:0-843");
            var terrainRenderer=terrain?terrain.GetComponent<MeshRenderer>():null;
            Test("existing-city-terrain-mesh-present",terrain&&terrain.sharedMesh&&terrain.sharedMesh.vertexCount>100000&&terrainRenderer&&terrainRenderer.enabled,
                "mesh="+(terrain&&terrain.sharedMesh?terrain.sharedMesh.name:"missing"));
            Test("authored-promenade-and-landscaping",waterfront.route.Length>=20&&waterfront.bays>=5&&waterfront.palms>=4&&waterfront.planters>=8&&waterfront.flowerBeds>=2&&waterfront.lanterns.Length>=8&&waterfront.rocks>=50,
                "route="+waterfront.route.Length+" bays="+waterfront.bays+" palms="+waterfront.palms+" shadeTrees="+waterfront.shadeTrees+" planters="+waterfront.planters+" flowerBeds="+waterfront.flowerBeds+" lamps="+waterfront.lanterns.Length+" rocks="+waterfront.rocks);
            Test("benches-bound-to-city-actions",waterfront.seats.Length>=5&&waterfront.seats.All(s=>ctx.Actions.Any(a=>a.id=="seat-"+s.id)),"benches="+waterfront.seats.Length);
            Test("shore-rocks-have-collision",waterfront.GetComponentsInChildren<Collider>().Count(c=>c.name=="Shore rock")==waterfront.rocks);
            int unsupported=0,blocked=0,wet=0,buried=0;var obstacles=new HashSet<string>();
            for(int i=1;i<waterfront.route.Length;i++)
            {
                var a=waterfront.route[i-1];var b=waterfront.route[i];var direction=CityPhysics.Flat(b-a).normalized;var side=new Vector3(direction.z,0,-direction.x);
                int steps=Mathf.CeilToInt(Vector3.Distance(a,b)/.55f);
                for(int j=0;j<=steps;j++)foreach(float lane in new[]{-1.6f,0,1.6f})
                {
                    var p=Vector3.Lerp(a,b,j/(float)Mathf.Max(1,steps))+side*lane;
                    if(!Physics.Raycast(p+Vector3.up*4,Vector3.down,out var hit,8,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore)){unsupported++;continue;}
                    p.y=hit.point.y+.06f;report.routeSamples++;if(p.y<CityMarine.SeaLevel+.35f)wet++;
                    if(!hit.transform.IsChildOf(waterfront.transform))buried++;
                    foreach(var obstacle in Physics.OverlapCapsule(p+Vector3.up*.43f,p+Vector3.up*1.65f,.32f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))
                    {blocked++;obstacles.Add(obstacle.name);}
                }
            }
            Test("continuous-dry-wide-promenade",unsupported==0&&blocked==0&&wet==0&&buried==0,
                "samples="+report.routeSamples+" unsupported="+unsupported+" blocked="+blocked+" wet="+wet+" terrainAbovePaving="+buried+" "+string.Join(",",obstacles.Take(8)));
            int badConnections=0;
            for(int i=1;i<waterfront.route.Length-1;i+=4)
            {
                var p=waterfront.route[i];var tangent=CityPhysics.Flat(waterfront.route[i+1]-waterfront.route[i-1]).normalized;var water=new Vector3(tangent.z,0,-tangent.x);
                for(float d=0;d<=4.4f;d+=.4f)
                {
                    var q=p-water*d;
                    if(!Physics.Raycast(q+Vector3.up*4,Vector3.down,out var hit,8,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore)) {badConnections++;continue;}
                    q.y=hit.point.y+.06f;
                    if(Physics.CheckCapsule(q+Vector3.up*.43f,q+Vector3.up*1.65f,.32f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))badConnections++;
                }
            }
            Test("regular-connections-to-existing-sidewalk",badConnections==0,"blocked or unsupported samples="+badConnections);
            int roadBlocked=0;
            for(int i=1;i<waterfront.route.Length-1;i++)
            {
                var tangent=CityPhysics.Flat(waterfront.route[i+1]-waterfront.route[i-1]).normalized;var waterSide=new Vector3(tangent.z,0,-tangent.x);
                foreach(float lane in new[]{-2.0f,0,2.0f})
                {
                    var p=waterfront.route[i]-waterSide*(11.2f-lane);p=Feet(p);
                    if(Physics.CheckCapsule(p+Vector3.up*.5f,p+Vector3.up*2,.5f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore))roadBlocked++;
                }
            }
            Test("existing-road-remains-clear",roadBlocked==0,"blocked samples="+roadBlocked);
            ctx.Combat.Holster();ctx.Follow.enabled=false;ctx.Systems.Weather.Request("clear",true);ctx.Clock=0;ctx.Motor.SetInputLock(true);
            ctx.Motor.Warp(Feet(waterfront.route[0]));yield return new WaitForSeconds(.4f);
            waterfront.gameObject.SetActive(false);
            Capture("00-existing-shore",new Vector3(630,165,400),new Vector3(530,6,555),55,true);
            waterfront.gameObject.SetActive(true);
            var waterSystem=FindFirstObjectByType<CityWater>();if(waterSystem)waterSystem.gameObject.SetActive(false);
            Capture("00-dry-terrain-diagnostic",new Vector3(630,165,400),new Vector3(530,6,555),55,true);
            if(waterSystem)waterSystem.gameObject.SetActive(true);
            Capture("01-overview",new Vector3(630,165,400),new Vector3(530,6,555),55,true);
            Capture("02-promenade",waterfront.route[5]+new Vector3(-9,2.3f,-9),waterfront.route[8]+Vector3.up*1.4f,70,false);
            Capture("03-looking-over-bay",waterfront.route[14]+new Vector3(-4,3.0f,4),waterfront.route[14]+new Vector3(15,1,-19),70,false);
            ctx.Motor.SetInputLock(false);ctx.Camera.transform.rotation=Quaternion.identity;ctx.Input.TouchRun=true;
            ctx.Motor.Warp(Feet(waterfront.route[0]));yield return new WaitForSeconds(.2f);bool walked=true;
            for(int i=1;i<waterfront.route.Length;i++)
            {
                var target=waterfront.route[i];float deadline=Time.realtimeSinceStartup+CityPhysics.Flat(target-ctx.Player.position).magnitude/3+3;
                while(CityPhysics.Flat(target-ctx.Player.position).magnitude>.48f&&Time.realtimeSinceStartup<deadline)
                {var d=CityPhysics.Flat(target-ctx.Player.position).normalized;move=new Vector2(d.x,d.z);yield return null;}
                if(CityPhysics.Flat(target-ctx.Player.position).magnitude>.9f){walked=false;break;}
            }
            move=Vector2.zero;ctx.Input.TouchRun=false;yield return new WaitForSeconds(.2f);
            Test("real-controller-walks-entire-promenade",walked&&ctx.Motor.Grounded,"position="+ctx.Player.position);
            for(int i=0;i<waterfront.gardenEntrances.Length;i++)
            {
                ctx.Motor.Warp(Feet(waterfront.gardenEntrances[i]));ctx.Camera.transform.rotation=Quaternion.identity;yield return new WaitForSeconds(.2f);
                var target=waterfront.gardenCenters[i];float deadline=Time.realtimeSinceStartup+8;
                while(CityPhysics.Flat(target-ctx.Player.position).magnitude>.5f&&Time.realtimeSinceStartup<deadline)
                {var d=CityPhysics.Flat(target-ctx.Player.position).normalized;move=new Vector2(d.x,d.z);yield return null;}
                move=Vector2.zero;yield return new WaitForSeconds(.2f);
                Test("real-controller-enters-garden-"+i,CityPhysics.Flat(target-ctx.Player.position).magnitude<.95f&&ctx.Motor.Grounded);
            }
            foreach(var seat in waterfront.seats)
            {
                var front=Quaternion.Euler(0,seat.yaw*Mathf.Rad2Deg,0)*Vector3.forward;var approach=Feet(seat.position+front*1.25f);
                ctx.Motor.Warp(approach);yield return new WaitForSeconds(.18f);
                ctx.Camera.transform.position=approach+front*.3f+Vector3.up*2.6f;ctx.Camera.transform.LookAt(seat.position+Vector3.up*.9f);ctx.RefreshAction();
                bool selected=ctx.CurrentAction!=null&&ctx.CurrentAction.id=="seat-"+seat.id;ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);bool sat=ctx.Motor.SeatHeight.HasValue;
                ctx.Input.TouchInteract=true;yield return new WaitForSeconds(.9f);
                Test("E-sit-and-stand-"+seat.id,selected&&sat&&!ctx.Motor.SeatHeight.HasValue&&ctx.Motor.Grounded,"selected="+selected+" sat="+sat);
            }
            ctx.Motor.SetInputLock(true);ctx.Motor.Warp(Feet(waterfront.route[10]));ctx.Clock=168;yield return new WaitForSeconds(.7f);
            Test("lanterns-light-at-night",ctx.Night>.9f&&waterfront.lanterns.All(l=>l.enabled&&l.intensity>.5f),"night="+ctx.Night);
            Capture("04-night",waterfront.route[10]+new Vector3(-13,5,-8),waterfront.route[13]+Vector3.up*1.5f,66,false);
            ctx.UI.SetSettings(true);float clock=ctx.Clock;var position=ctx.Player.position;yield return new WaitForSecondsRealtime(.35f);
            Test("Esc-settings-pause-world",ctx.Paused&&ctx.UI.Open&&Mathf.Abs(ctx.Clock-clock)<.001f&&Vector3.Distance(position,ctx.Player.position)<.001f);
            ctx.UI.SetSettings(false);yield return new WaitForSeconds(.2f);Test("resume-exploration",!ctx.Paused&&ctx.Clock>clock+.08f);
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
        void Finish(){finished=true;if(ctx)ctx.Input.TouchMove=Vector2.zero;File.WriteAllText(Path.Combine(dir,"waterfront-qa.json"),JsonUtility.ToJson(report,true));if(!Application.isEditor)Application.Quit(report.passed?0:2);}
    }
}
