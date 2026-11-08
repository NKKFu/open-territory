using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in native acceptance; never installed in an ordinary game.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityBlackthornHouseProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string executable, graphics, verifiedAtUtc;
            public int routeSamples;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>(), obstacles = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx;
        CityBlackthornHouse house;
        Vector2 move;
        string dir;
        bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-blackthorn-qa") >= 0)
                new GameObject("Blackthorn House QA • opt-in").AddComponent<CityBlackthornHouseProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void Update() { if (ctx && !finished) ctx.Input.TouchMove = move; }
        void Test(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("BLACKTHORN_QA " + name + " " + passed + " " + detail);
        }
        Vector3 Point(float x, float y, float z) => house.transform.TransformPoint(new Vector3(x,y,z));
        Vector3 Feet(Vector3 p) { p.y = ctx.Ground(p.x,p.z) + .05f; return p; }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args,"--city-report-dir");
            dir = at >= 0 && at + 1 < args.Length ? args[at+1] : Path.Combine(Application.temporaryCachePath,"blackthorn-qa");
            Directory.CreateDirectory(dir); report.executable = args[0]; report.graphics = SystemInfo.graphicsDeviceType.ToString();
            report.verifiedAtUtc = DateTime.UtcNow.ToString("o");
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance; house = FindFirstObjectByType<CityBlackthornHouse>();
            Test("authored-native-scene-ready",ctx && ctx.Ready && house);
            if (!ctx || !ctx.Ready || !house) { Finish(); yield break; }
            yield return new WaitForSeconds(.8f);
            Test("QA-storage-is-isolated",CityCommerce.IsReviewRun);
            var p=ctx.Data.places[CityBlackthornHouse.PlaceIndex];
            Test("exact-house-from-user-picture",p.name=="#A615AD" && Vector3.Distance(house.transform.position,new Vector3(p.x,p.elevation,p.z))<.01f);
            var home=FindFirstObjectByType<CityHomeCatalog>().homes.First(h=>h.place==CityBlackthornHouse.PlaceIndex);
            Test("authorized-open-shop-refit-keeps-original-floor",home.root && home.root.Find("Base física nivelada").gameObject.activeInHierarchy && house.replacedInterior.Length>0 && house.replacedInterior.All(g=>g && !g.activeSelf));
            Test("preserved-collision-audit-recorded",house.originalColliders>30000 && house.originalCollisionHash.Length==64,"original="+house.originalColliders);
            Test("retail-shelves-counter-candles-and-lighting",house.candleCount==8 && house.shelfCount==6 && house.crystal && house.lights.Length==5);
            Test("native-square-building-style-no-spires-or-raised-roof",house.GetComponentsInChildren<MeshCollider>().Length==0 && house.GetComponentsInChildren<MeshRenderer>().All(r=>house.transform.InverseTransformPoint(r.bounds.max).y<5.3f));
            Test("central-pentagram-keeps-the-open-floor",Vector3.Distance(house.pentagramCenter,Point(0,.24f,0))<.02f && !Physics.CheckCapsule(house.pentagramCenter+Vector3.up*.43f,house.pentagramCenter+Vector3.up*1.87f,.36f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore));
            Test("all-building-materials-supported",house.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterials.All(m=>m && m.shader && m.shader.isSupported && m.shader.name!="Hidden/InternalErrorShader")));
            var labels=house.GetComponentsInChildren<UnityEngine.UI.Text>();
            Test("English-signage",labels.Any(t=>t.text=="BLACKTHORN") && labels.All(t=>!t.raycastTarget));

            ctx.Combat.Holster(); ctx.Follow.enabled=false; ctx.Systems.Weather.Request("clear",true); ctx.Clock=0;
            ctx.Motor.Warp(Feet(house.streetApproach)); ctx.Motor.SetInputLock(true);
            float spawnDeadline=Time.realtimeSinceStartup+7;
            while(!ctx.Residents.IsSpawned(house.wizardData.id) && Time.realtimeSinceStartup<spawnDeadline)yield return null;
            var wizard=ctx.Residents.Find(house.wizardData.id);
            Test("wizard-is-a-native-resident-with-valid-rig",wizard!=null && ctx.Residents.IsSpawned(house.wizardData.id) && wizard.Rig!=null && wizard.Rig.Valid && wizard.Data.stationary);
            Capture("01-blackthorn-overview",new Vector3(25,19,31),new Vector3(0,2.5f,0),53);
            Capture("02-blackthorn-entrance",new Vector3(1.7f,2.9f,18),new Vector3(0,2.7f,6),64);
            Capture("03-blackthorn-shop",new Vector3(5.8f,3.4f,5.9f),new Vector3(0,1.3f,-1.4f),78);
            Capture("04-wizard",new Vector3(1.3f,2.7f,-2.2f),new Vector3(0,1.8f,-5.55f),67);

            var doors=home.root.GetComponentsInChildren<Transform>().Where(t=>t.name=="door").Concat(new[]{house.entranceLeaf}).ToArray();
            bool IsDoor(Collider c)=>doors.Any(d=>d && (c.transform==d || c.transform.IsChildOf(d)));
            int unsupported=0,blocked=0;
            for(int i=1;i<house.walkRoute.Length;i++)
            {
                var a=house.walkRoute[i-1]; var b=house.walkRoute[i]; int count=Mathf.CeilToInt(Vector3.Distance(a,b)/.25f);
                for(int j=0;j<=count;j++)
                {
                    var pt=Vector3.Lerp(a,b,j/(float)Mathf.Max(1,count)); report.routeSamples++;
                    if(!Physics.Raycast(pt+Vector3.up*2,Vector3.down,out var hit,4,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore)){unsupported++;continue;}
                    var contacts=Physics.OverlapCapsule(hit.point+Vector3.up*.43f,hit.point+Vector3.up*1.87f,.36f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore).Where(c=>!IsDoor(c)).ToArray();
                    if(contacts.Length>0){blocked++;if(report.obstacles.Count<12)report.obstacles.Add("segment="+i+" local="+house.transform.InverseTransformPoint(pt)+" "+string.Join(",",contacts.Select(c=>c.name)));}
                }
            }
            Test("sidewalk-door-central-pentagram-and-both-shop-aisles-clearance",unsupported==0 && blocked==0,"samples="+report.routeSamples+" unsupported="+unsupported+" blocked="+blocked);
            var doorStart=house.entranceLeaf.rotation;bool opened=false,walked=true;
            ctx.Motor.SetInputLock(false); ctx.Camera.transform.rotation=Quaternion.identity; ctx.Input.TouchRun=false;
            for(int i=0;i<house.walkRoute.Length;i++)
            {
                var target=house.walkRoute[i];
                float end=Time.realtimeSinceStartup+CityPhysics.Flat(target-ctx.Player.position).magnitude/1.7f+3;
                // Clear the entire open leaf before turning inside the room.
                float arrival=i==7 || i==9 ? .08f : .22f;
                while(CityPhysics.Flat(target-ctx.Player.position).magnitude>arrival && Time.realtimeSinceStartup<end)
                {
                    var direction=CityPhysics.Flat(target-ctx.Player.position).normalized;move=new Vector2(direction.x,direction.z);yield return null;
                    opened|=Quaternion.Angle(doorStart,house.entranceLeaf.rotation)>60;
                }
                if(CityPhysics.Flat(target-ctx.Player.position).magnitude>.65f)
                {
                    walked=false;report.obstacles.Add("walk target="+house.transform.InverseTransformPoint(target)+" player="+house.transform.InverseTransformPoint(ctx.Player.position));
                    report.obstacles.Add(string.Join(",",Physics.OverlapCapsule(ctx.Player.position+Vector3.up*.42f,ctx.Player.position+Vector3.up*1.86f,.44f,1<<CityContext.WorldLayer,QueryTriggerInteraction.Ignore).Select(c=>c.name+" "+house.transform.InverseTransformPoint(c.bounds.center))));break;
                }
            }
            move=Vector2.zero;yield return new WaitForSeconds(.4f);
            Test("real-controller-enter-circle-shelves-pentagram-and-leave",walked && ctx.Motor.Grounded,string.Join(";",report.obstacles));
            Test("preserved-manual-front-door-opens-on-approach",opened);
            ctx.Motor.Warp(Feet(Point(0,.2f,-2.5f)));yield return new WaitForSeconds(.15f);
            var toAltar=-house.transform.forward;move=new Vector2(toAltar.x,toAltar.z);ctx.Input.TouchRun=true;
            yield return new WaitForSeconds(1.1f);move=Vector2.zero;ctx.Input.TouchRun=false;yield return new WaitForSeconds(.2f);
            float altarZ=house.transform.InverseTransformPoint(ctx.Player.position).z;
            Test("shop-counter-stops-the-body",altarZ>-3.9f && altarZ<-3.2f,"localZ="+altarZ);
            ctx.Motor.Warp(Feet(house.counterApproach));ctx.Motor.SetInputLock(true);
            ctx.Camera.transform.position=Point(0,2.6f,-1.8f);ctx.Camera.transform.LookAt(house.wizard.position+Vector3.up*1.6f);
            yield return new WaitForSeconds(.25f);ctx.RefreshAction();
            Test("wizard-Talk-is-visible-across-counter",ctx.CurrentAction!=null && ctx.CurrentAction.id=="resident-"+house.wizardData.id,ctx.CurrentAction?.id);
            ctx.Input.TouchTalk=true;yield return new WaitForSeconds(.35f);
            Test("talk-to-wizard-keeps-the-world-running",ctx.Speech.Contains("Books") || ctx.Speech.Contains("old books"),ctx.Speech);
            ctx.Motor.Warp(Feet(Point(0,.2f,-2.3f)));ctx.Motor.SetInputLock(true);yield return new WaitForSeconds(.1f);
            var crystalStart=house.crystal.localPosition;var crystalRotation=house.crystal.localRotation;
            yield return new WaitForSeconds(.4f);
            Test("prepared-crystal-animates-in-world",Vector3.Distance(crystalStart,house.crystal.localPosition)>.002f || Quaternion.Angle(crystalRotation,house.crystal.localRotation)>1);
            ctx.UI.SetSettings(true);var pausedClock=ctx.Clock;var pausedCrystal=house.crystal.localPosition;var pausedRotation=house.crystal.localRotation;float pausedLight=house.lights[0].intensity;
            yield return new WaitForSecondsRealtime(.4f);
            Test("Esc-freezes-crystal-and-candle-lights",ctx.Paused && Mathf.Abs(ctx.Clock-pausedClock)<.001f && Vector3.Distance(pausedCrystal,house.crystal.localPosition)<.0001f && Quaternion.Angle(pausedRotation,house.crystal.localRotation)<.01f && Mathf.Abs(pausedLight-house.lights[0].intensity)<.0001f);
            ctx.UI.SetSettings(false);yield return new WaitForSeconds(.25f);
            Test("settings-resume-world",!ctx.Paused && ctx.Clock>pausedClock+.1f);
            ctx.UI.Map.SetOpen(true);yield return new WaitForSeconds(.2f);
            Test("Blackthorn-House-is-a-map-place",ctx.UI.Map.Places.Any(q=>q.name==CityBlackthornHouse.DisplayName && q.kind=="occult"));
            float mapClock=ctx.Clock;yield return new WaitForSeconds(.2f);
            Test("map-keeps-city-running",!ctx.Paused && ctx.Clock>mapClock+.1f);ctx.UI.Map.SetOpen(false);
            ctx.Clock=168;yield return new WaitForSeconds(.6f);
            Test("night-lanterns-and-ritual-lighting",ctx.Night>.9f && house.lights.All(l=>l.enabled && l.intensity>.5f));
            ctx.Motor.Warp(Feet(house.streetApproach));yield return new WaitForSeconds(.15f);
            Capture("05-blackthorn-night",new Vector3(25,16,31),new Vector3(0,2.5f,0),53);
            Capture("06-shop-night",new Vector3(5.8f,3.4f,5.9f),new Vector3(0,1.3f,-1.4f),78);
            Test("no-runtime-errors",report.errors.Count==0,"errors="+report.errors.Count);
            Finish();
        }
        void Capture(string name,Vector3 eye,Vector3 target,float fov)
        {
            var camera=ctx.Camera;camera.transform.position=house.transform.TransformPoint(eye);camera.transform.LookAt(house.transform.TransformPoint(target));camera.fieldOfView=fov;
            var lod=FindFirstObjectByType<CityLodManager>();if(lod)foreach(var item in lod.objects)if(item && item.isActiveAndEnabled)item.ApplyLevel(item.SelectLevel(camera));
            var old=RenderTexture.active;var rt=new RenderTexture(1600,1000,24);rt.antiAliasing=2;rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;rt.Release();Destroy(rt);Destroy(image);
        }
        void Finish()
        {
            finished=true;if(ctx){move=Vector2.zero;ctx.Input.TouchMove=Vector2.zero;ctx.Input.TouchRun=false;ctx.UI.SetSettings(false);ctx.Motor.SetInputLock(false);ctx.Follow.enabled=true;}
            File.WriteAllText(Path.Combine(dir,"blackthorn-qa.json"),JsonUtility.ToJson(report,true));
            if(!Application.isEditor)Application.Quit(report.passed?0:2);
        }
    }
}
