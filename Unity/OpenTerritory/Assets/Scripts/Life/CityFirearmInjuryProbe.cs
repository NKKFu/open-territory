using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Native acceptance only, with the existing isolated --city-*-qa storage.
    [DefaultExecutionOrder(-900)]
    public sealed class CityFirearmInjuryProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool passed=true; public List<Check> checks=new List<Check>(); public List<string> errors=new List<string>(); }
        readonly Report report=new Report();
        readonly Vector3 origin=new Vector3(0,200.04f,0);
        CityContext ctx; CityResidents.Resident person,officer; GameObject floor,wall; Camera lens;
        string dir; bool residentFire;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-firearm-injury-qa")>=0)new GameObject("Firearm injury QA opt-in").AddComponent<CityFirearmInjuryProbe>(); }
        void Awake() { Application.logMessageReceived+=Log; }
        void OnDestroy() { Application.logMessageReceived-=Log; }
        void Log(string message,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message+"\n"+stack); }
        void Update() { if(residentFire&&ctx)ctx.Combat.TryResidentShot(officer,ctx.Player); }
        void CheckResult(string name,bool passed,string detail="")
        { report.passed&=passed;report.checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("FIREARM_INJURY_QA "+name+" "+passed+" "+detail); }
        IEnumerator Frame() { yield return null;yield return new WaitForEndOfFrame(); }
        IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds);yield return Frame(); }
        IEnumerator Press() { ctx.Input.TouchFire=true;yield return Frame(); }
        void Place(CityResidents.Resident p,Vector3 point,float yaw=180)
        {
            p.Controller.enabled=false;p.Root.SetPositionAndRotation(point,Quaternion.Euler(0,yaw,0));p.Controller.enabled=true;
            p.LastPosition=point;p.Velocity=Vector3.zero;p.MoveSpeed=0;p.Controlled=true;p.TalkUntil=p.FearUntil=p.DodgeUntil=0;p.Crossing=false;
        }
        void AimAt(Vector3 point)
        { ctx.Camera.transform.position=origin+new Vector3(0,1.55f,-3);ctx.Camera.transform.LookAt(point); }
        void Capture(string name,Vector3 centre,Vector3 offset)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            if(!lens){lens=new GameObject("QA firearm detail camera").AddComponent<Camera>();lens.enabled=false;lens.fieldOfView=36;lens.nearClipPlane=.025f;lens.farClipPlane=100;lens.clearFlags=CameraClearFlags.SolidColor;lens.backgroundColor=new Color(.055f,.075f,.085f);}
            lens.transform.position=centre+offset;lens.transform.LookAt(centre);
            var texture=RenderTexture.GetTemporary(1280,800,24);var previous=lens.targetTexture;var active=RenderTexture.active;Texture2D picture=null;
            try{lens.targetTexture=texture;lens.Render();RenderTexture.active=texture;picture=new Texture2D(1280,800,TextureFormat.RGB24,false);picture.ReadPixels(new Rect(0,0,1280,800),0,0);picture.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),picture.EncodeToPNG());}
            finally{lens.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(texture);if(picture)Destroy(picture);}
        }
        void CheckGrip(string name,bool support)
        {
            var rig=ctx.Actors.PlayerRig;var visual=ctx.Combat.Visual(ctx.Combat.Equipped);
            float right=Vector3.Distance(rig.Hands[1].position,visual.Root.TransformPoint(visual.RightGrip));
            float left=Vector3.Distance(rig.Hands[0].position,visual.Root.TransformPoint(visual.SupportGrip));
            CheckResult(name+"-right-palm-at-grip",right<.004f,"metres="+right);
            if(support)CheckResult(name+"-support-reachable",left<.045f,"metres="+left);
            CheckResult(name+"-gun-is-upright",Vector3.Dot(visual.Root.up,Vector3.up)>.3f);
        }
        IEnumerator ResetVictim()
        {
            ctx.Input.TouchAim=true;
            ctx.Police.Clear();ctx.Combat.ForgetTemporaryActor(person.Root);person.Health=100;
            Place(person,origin+Vector3.forward*6);Physics.SyncTransforms();AimAt(person.Root.position+Vector3.up*1.35f);yield return Wait(.35f);
        }
        IEnumerator Start()
        {
            while(!CityContext.Instance||!CityContext.Instance.Ready)yield return null;
            ctx=CityContext.Instance;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");
            dir=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Reports/firearms/native"));Directory.CreateDirectory(dir);
            ctx.Population.enabled=ctx.Police.enabled=ctx.Commerce.enabled=ctx.Follow.enabled=false;ctx.UI.SetSettings(false);ctx.Police.Clear();ctx.Combat.Holster();
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA firearm floor";floor.layer=CityContext.GroundLayer;
            floor.transform.position=origin-Vector3.up*1.04f;floor.transform.localScale=new Vector3(80,2,80);floor.GetComponent<Renderer>().material.color=new Color(.20f,.23f,.24f);
            var key=new GameObject("QA firearm key light").AddComponent<Light>();key.type=LightType.Directional;key.intensity=.6f;key.transform.rotation=Quaternion.Euler(32,-55,0);
            ctx.Motor.Warp(origin);ctx.Player.rotation=Quaternion.identity;AimAt(origin+Vector3.up*1.4f+Vector3.forward*6);
            var adult=ctx.Data.residents.FirstOrDefault(p=>!p.child&&!p.sitting&&!p.stationary&&!p.performance&&p.role!="police"&&!ctx.Residents.IsSpawned(p.id));
            var police=ctx.Data.residents.FirstOrDefault(p=>p.role=="police");
            if(adult==null||police==null){CheckResult("real-actors-available",false);Finish();yield break;}
            ctx.Residents.Spawn(adult.id,origin+Vector3.forward*6,Quaternion.identity);
            if(!ctx.Residents.IsSpawned(police.id))ctx.Residents.Spawn(police.id,origin+new Vector3(8,0,0),Quaternion.identity);
            person=ctx.Residents.Find(adult.id);officer=ctx.Residents.Find(police.id);Place(officer,origin+new Vector3(8,0,0));
            CheckResult("real-player-resident-and-officer-rigs",ctx.Actors.PlayerRig.Valid&&person.Rig.Valid&&officer.Rig.Valid);
            CheckResult("QA-storage-is-isolated",CityCommerce.IsReviewRun);
            ctx.Combat.RestoreLoadoutJson(JsonUtility.ToJson(new CityLoadoutSave{weapons=CityCombat.Weapons.Select(w=>new CityWeaponSave{id=w.Id,loaded=w.Capacity,reserve=w.InitialReserve}).ToArray()}));
            yield return Wait(.5f);
            for(int i=0;i<CityCombat.Weapons.Length;i++)
            {
                ctx.Input.TouchAim=false;ctx.Input.TouchWeapon=i+1;yield return Wait(.2f);string id=CityCombat.Weapons[i].Id;
                CheckGrip(id+"-ready",id=="carbine");Capture(id+"-ready",ctx.Player.position+Vector3.up*1.3f,new Vector3(2,1,1.7f));
                ctx.Input.TouchAim=true;yield return Wait(.35f);CheckGrip(id+"-aim",true);
                Capture(id+"-grip",ctx.Player.position+Vector3.up*1.65f+ctx.Player.forward*.32f,new Vector3(1.35f,.42f,1.3f));
                ctx.Input.TouchMove=Vector2.right;yield return Wait(.35f);CheckGrip(id+"-strafe",true);ctx.Input.TouchMove=Vector2.zero;
                ctx.Motor.Warp(origin);yield return Wait(.2f);
                AimAt(origin+new Vector3(0,7,8));yield return Wait(.2f);CheckGrip(id+"-aim-up",true);
                AimAt(origin+new Vector3(0,-3,8));yield return Wait(.2f);CheckGrip(id+"-aim-down",true);
                AimAt(origin+new Vector3(0,1.4f,8));yield return Wait(.2f);
            }
            ctx.Input.TouchWeapon=2;yield return Wait(.25f);yield return ResetVictim();
            int blood=ctx.Combat.GunEffects.BloodBursts,shots=ctx.Combat.Shots;yield return Press();
            CheckResult("pistol-input-hits-real-resident",ctx.Combat.Shots==shots+1&&Mathf.Abs(ctx.Combat.HealthOf(person.Root)-64)<.01f,"health="+ctx.Combat.HealthOf(person.Root));
            CheckResult("resident-bullet-emits-blood",ctx.Combat.GunEffects.BloodBursts==blood+1&&ctx.Combat.GunEffects.LiveBloodParticles>0);
            CheckResult("blood-reaches-solid-floor",ctx.Combat.GunEffects.ActiveBloodMarks>0);
            yield return Frame();
            Capture("resident-hit-blood",person.Root.position+Vector3.up*1.0f,new Vector3(2.8f,1.4f,-3.4f));
            var ps=ctx.Combat.GunEffects.BloodEmitter;var before=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(before);
            ctx.UI.SetSettings(true);float clock=ctx.Clock;float hp=ctx.Combat.HealthOf(person.Root);int residentShots=ctx.Combat.ResidentShots;
            yield return Wait(.3f);var paused=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(paused);
            CheckResult("Esc-freezes-blood-motion-and-expiry",clock==ctx.Clock&&before.Length==paused.Length&&before.Length>0&&Mathf.Abs(before[0].remainingLifetime-paused[0].remainingLifetime)<.0001f&&Vector3.Distance(before[0].position,paused[0].position)<.0001f);
            CheckResult("Esc-rejects-incoming-shot",!ctx.Combat.TryResidentShot(officer,ctx.Player)&&ctx.Combat.ResidentShots==residentShots&&ctx.Combat.HealthOf(person.Root)==hp);
            ctx.UI.SetSettings(false);yield return Wait(.5f);ctx.Input.TouchWeapon=3;yield return Wait(.2f);yield return ResetVictim();
            blood=ctx.Combat.GunEffects.BloodBursts;yield return Press();
            CheckResult("carbine-input-deals-25-damage",Mathf.Abs(ctx.Combat.HealthOf(person.Root)-75)<.01f,"health="+ctx.Combat.HealthOf(person.Root));
            CheckResult("carbine-bullet-emits-blood",ctx.Combat.GunEffects.BloodBursts==blood+1);
            yield return Wait(.4f);yield return ResetVictim();ctx.Input.TouchWeapon=2;yield return Wait(.2f);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA bullet cover";wall.layer=CityContext.WorldLayer;wall.transform.position=origin+new Vector3(0,1.4f,3);wall.transform.localScale=new Vector3(5,3,.22f);Physics.SyncTransforms();
            blood=ctx.Combat.GunEffects.BloodBursts;yield return Press();
            CheckResult("wall-blocks-damage-and-false-blood",ctx.Combat.HealthOf(person.Root)==100&&ctx.Combat.GunEffects.BloodBursts==blood);
            Destroy(wall);yield return Wait(.4f);
            for(int i=0;i<3;i++){yield return Press();yield return Wait(.32f);}
            CheckResult("three-pistol-hits-cause-physical-fall",ctx.Combat.IsDown(person.Root)&&ctx.Combat.HealthOf(person.Root)==0&&!person.Controller.enabled&&person.Root.GetComponent<Rigidbody>());
            Capture("resident-down",person.Root.position+Vector3.up*.2f,new Vector3(2.8f,2.5f,-3.2f));
            ctx.Combat.Holster();ctx.Input.TouchAim=false;ctx.Combat.HealAtClinic();Place(officer,origin+new Vector3(0,0,-7),0);Physics.SyncTransforms();
            residentShots=ctx.Combat.ResidentShots;blood=ctx.Combat.GunEffects.BloodBursts;residentFire=true;
            float deadline=Time.unscaledTime+2.3f;while(Time.unscaledTime<deadline&&ctx.Combat.ResidentShots==residentShots)yield return Frame();residentFire=false;
            CheckResult("officer-fires-from-real-muzzle",ctx.Combat.ResidentShots==residentShots+1);
            CheckResult("incoming-bullet-damages-player",ctx.Combat.Health<100&&ctx.Combat.PlayerBulletHits>0,"health="+ctx.Combat.Health);
            CheckResult("incoming-bullet-emits-player-blood",ctx.Combat.GunEffects.BloodBursts==blood+1);
            yield return Frame();
            Capture("player-hit-blood",ctx.Player.position+Vector3.up,new Vector3(2.8f,1.5f,-3.4f));
            hp=ctx.Combat.Health;blood=ctx.Combat.GunEffects.BloodBursts;
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA incoming cover";wall.layer=CityContext.WorldLayer;wall.transform.position=origin+new Vector3(0,1.4f,-3);wall.transform.localScale=new Vector3(5,3,.22f);Physics.SyncTransforms();
            residentFire=true;yield return Wait(1.2f);residentFire=false;
            CheckResult("cover-protects-player",ctx.Combat.Health==hp&&ctx.Combat.GunEffects.BloodBursts==blood);Destroy(wall);yield return Wait(.3f);
            // Exercise the actual police decision, rather than only its shot API.
            ctx.Combat.HealAtClinic();ctx.Police.Clear();ctx.Input.TouchWeapon=2;ctx.Input.TouchAim=true;yield return Wait(.3f);ctx.Police.enabled=true;
            ctx.Police.Report("disparo",ctx.Player.position,null,true);residentShots=ctx.Combat.ResidentShots;yield return Wait(1.5f);
            CheckResult("police-AI-responds-to-armed-gunfire",ctx.Combat.ResidentShots>residentShots&&ctx.Combat.Health<100,"stage="+ctx.Police.Stage+" shots="+(ctx.Combat.ResidentShots-residentShots));
            ctx.Combat.Holster();ctx.Input.TouchAim=false;yield return Wait(.4f);residentShots=ctx.Combat.ResidentShots;yield return Wait(1.1f);
            CheckResult("holstering-stops-police-gunfire",ctx.Combat.ResidentShots==residentShots);ctx.Police.enabled=false;ctx.Police.Clear();
            int pool=ctx.Combat.GunEffects.PoolSize;
            for(int i=0;i<40;i++)ctx.Combat.GunEffects.Bleed(ctx.Player,origin+new Vector3((i%8)*.12f,1.2f,0),Vector3.back,Vector3.forward,25);
            CheckResult("blood-pool-saturates-without-growth",ctx.Combat.GunEffects.ActiveBloodMarks==32&&ctx.Combat.GunEffects.PoolSize==pool&&ctx.Combat.GunEffects.LiveBloodParticles<=256);
            Capture("blood-pool",origin+new Vector3(.4f,.05f,0),new Vector3(1.4f,2.7f,-2.3f));
            yield return Wait(18.2f);CheckResult("blood-pool-fully-expires",ctx.Combat.GunEffects.ActiveBloodMarks==0&&ctx.Combat.GunEffects.LiveBloodParticles==0);
            ctx.Combat.GunEffects.Bleed(ctx.Player,origin+Vector3.up*1.2f,Vector3.back,Vector3.forward,25);yield return Wait(.25f);
            CheckResult("recycled-blood-has-new-lifetime",ctx.Combat.GunEffects.ActiveBloodMarks==1&&ctx.Combat.GunEffects.LiveBloodParticles>0);
            CheckResult("fallen-resident-recovers",!ctx.Combat.IsDown(person.Root)&&person.Controller.enabled);
            yield return ResetVictim();ctx.Input.TouchWeapon=1;ctx.Input.TouchAim=true;yield return Wait(.3f);blood=ctx.Combat.GunEffects.BloodBursts;yield return Press();
            CheckResult("pulse-deals-damage-and-stuns",Mathf.Abs(ctx.Combat.HealthOf(person.Root)-88)<.01f&&ctx.Combat.IsDown(person.Root),"health="+ctx.Combat.HealthOf(person.Root));
            CheckResult("pulse-preserves-energy-hit-without-bullet-blood",ctx.Combat.GunEffects.BloodBursts==blood);
            CheckResult("no-runtime-errors",report.errors.Count==0,string.Join("\n",report.errors));Finish();
        }
        void Finish()
        {
            residentFire=false;File.WriteAllText(Path.Combine(dir,"firearm-injury-qa.json"),JsonUtility.ToJson(report,true));Debug.Log("FIREARM_INJURY_QA_DONE "+report.passed);
            if(!Application.isEditor)Application.Quit(report.passed?0:1);else Destroy(this);
        }
    }
}
