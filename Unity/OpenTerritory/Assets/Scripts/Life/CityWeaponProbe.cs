using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in only. Uses the real sampled controls, rig, camera, physics and
    // audio engine. Native QA receives the existing isolated save directory.
    [DefaultExecutionOrder(-900)]
    public sealed class CityWeaponProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name,detail; public bool passed; }
        [Serializable] public class Report { public bool passed=true; public List<Check> checks=new List<Check>(); public List<string> errors=new List<string>(),particles=new List<string>(); public float audioPeak; public int audioFrames; }
        readonly Report report=new Report();
        CityContext ctx;CityAudio audio;CityAudioCapture capture;GameObject floor,target,wall;
        string dir;bool holdFire,finished;Vector3 saved;Quaternion savedRotation;string loadout,equipped;bool commerceEnabled;
        readonly Vector3 origin=new Vector3(0,200.04f,0);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-weapons-qa")>=0)new GameObject("Armas QA opt-in").AddComponent<CityWeaponProbe>();}
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)report.errors.Add(message+"\n"+stack);}
        void Update(){if(!finished&&holdFire&&ctx)ctx.Input.FireHeld=true;}
        void CheckResult(string name,bool passed,string detail="")
        {report.passed&=passed;report.checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("WEAPONS_QA "+name+" "+passed+" "+detail);}
        IEnumerator Frame(){yield return null;yield return new WaitForEndOfFrame();}
        IEnumerator Wait(float duration){yield return new WaitForSecondsRealtime(duration);yield return Frame();}
        IEnumerator Press(){ctx.Input.TouchFire=true;yield return Frame();}
        void Capture(string name,Camera camera=null)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            if(!camera)camera=ctx.Camera;
            var texture=RenderTexture.GetTemporary(1280,800,24);var previous=camera.targetTexture;var active=RenderTexture.active;Texture2D picture=null;
            try{camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;picture=new Texture2D(1280,800,TextureFormat.RGB24,false);picture.ReadPixels(new Rect(0,0,1280,800),0,0);picture.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),picture.EncodeToPNG());}
            finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(texture);if(picture)Destroy(picture);}
        }
        void CaptureModel(CityWeaponDefinition definition)
        {
            var stand=new GameObject("QA model stand");stand.transform.position=origin+Vector3.up*25;
            var model=new CityWeaponVisual(definition,stand.transform);model.Root.gameObject.SetActive(true);
            var lens=new GameObject("QA model lens");var camera=lens.AddComponent<Camera>();camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.075f,.085f);camera.cullingMask=1<<CityContext.PlayerLayer;camera.nearClipPlane=.02f;camera.farClipPlane=5;camera.fieldOfView=36;
            var center=stand.transform.position+new Vector3(0,.015f,definition.Id=="carbine"?.12f:.17f);
            lens.transform.position=center+new Vector3(definition.Id=="carbine"?1.15f:.73f,.43f,.55f);lens.transform.LookAt(center);
            var key=new GameObject("QA soft key");var light=key.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.color=new Color(1,.91f,.77f);key.transform.rotation=Quaternion.Euler(35,-110,0);
            Capture("model-"+definition.Id,camera);model.Dispose();Destroy(stand);Destroy(lens);Destroy(key);
        }
        IEnumerator CheckParticles(string name)
        {
            ParticleSystem observed=null;ParticleSystem.Particle sample=default;float longest=0;
            foreach(var ps in ctx.Combat.GunEffects.Emitters)
            {
                var live=new ParticleSystem.Particle[ps.particleCount];int count=ps.GetParticles(live);
                for(int i=0;i<count;i++)if(live[i].remainingLifetime>longest)
                {observed=ps;sample=live[i];longest=sample.remainingLifetime;}
            }
            float before=ctx.Clock;
            // Let the game's real Update simulate the emitted burst. A slow
            // frame may legitimately expire it; that is also clock progress.
            yield return Frame();
            bool advanced=false,found=false;float elapsed=ctx.Clock-before;
            if(observed)
            {
                var live=new ParticleSystem.Particle[observed.particleCount];int count=observed.GetParticles(live);
                for(int i=0;i<count;i++)if(live[i].randomSeed==sample.randomSeed)
                {found=true;advanced=live[i].remainingLifetime<sample.remainingLifetime;break;}
                if(!found)advanced=elapsed>=sample.remainingLifetime-.002f;
            }
            bool particleBounds=false;
            foreach(var ps in ctx.Combat.GunEffects.Emitters)
            {
                var renderer=ps.GetComponent<ParticleSystemRenderer>();var particles=new ParticleSystem.Particle[ps.particleCount];int count=ps.GetParticles(particles);
                particleBounds|=count>0&&renderer.enabled&&renderer.sharedMaterial.shader.isSupported&&renderer.bounds.size.sqrMagnitude>.00001f;
                report.particles.Add(name+" "+ps.name+" count="+count+" renderer="+renderer.enabled+" mode="+renderer.renderMode+" scale="+ps.transform.lossyScale+" opacity="+renderer.sharedMaterial.GetFloat("_Opacity")+" supported="+renderer.sharedMaterial.shader.isSupported+(count>0?" position="+particles[0].position+" size="+particles[0].GetCurrentSize(ps)+" color="+particles[0].GetCurrentColor(ps)+" lifetime="+particles[0].remainingLifetime:"")+" bounds="+renderer.bounds);
            }
            var observedRenderer=observed?observed.GetComponent<ParticleSystemRenderer>():null;
            bool expired=observed&&!found&&advanced;
            CheckResult(name+"-particles-simulate-and-render",advanced&&observedRenderer&&observedRenderer.enabled&&observedRenderer.sharedMaterial.shader.isSupported&&(particleBounds||expired),"clock-step="+elapsed+" initial-life="+longest+" expired="+expired);
        }
        void CaptureEffects(CityWeaponVisual visual,string name,bool impact)
        {
            var lens=new GameObject("QA effect lens");var camera=lens.AddComponent<Camera>();camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.055f,.07f);camera.nearClipPlane=.02f;camera.farClipPlane=2;camera.fieldOfView=58;
            camera.cullingMask=1|(1<<CityContext.PlayerLayer)|(impact?(1<<CityContext.WorldLayer):0);
            Vector3 direction=visual.Root.forward,at=impact?ctx.Combat.GunEffects.LastContact:ctx.Combat.LastMuzzle;
            lens.transform.position=at+visual.Root.right*.42f+Vector3.up*.12f-direction*(impact?.6f:.18f);lens.transform.LookAt(at+direction*(impact?0:.04f));
            Capture(name,camera);Destroy(lens);
        }
        void PlaceTarget(string surface,float distance=7)
        {
            if(target)Destroy(target);target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="QA "+surface;target.layer=CityContext.WorldLayer;
            var ray=ctx.Follow.AimRay;target.transform.SetPositionAndRotation(ray.GetPoint(distance),Quaternion.LookRotation(ray.direction));target.transform.localScale=new Vector3(2,2,.18f);
            target.GetComponent<Renderer>().material.color=surface=="metal"?new Color(.31f,.44f,.46f):surface=="wood"?new Color(.43f,.29f,.16f):new Color(.58f,.55f,.49f);Physics.SyncTransforms();
        }
        IEnumerator Start()
        {
            while(!CityContext.Instance||!CityContext.Instance.Ready)yield return null;
            ctx=CityContext.Instance;audio=ctx.Systems.Audio;saved=ctx.Player.position;savedRotation=ctx.Player.rotation;loadout=ctx.Combat.ExportLoadoutJson();equipped=ctx.Combat.Equipped;
            commerceEnabled=ctx.Commerce.enabled;ctx.Commerce.enabled=false;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--city-report-dir");dir=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Reports/weapons/editor"));Directory.CreateDirectory(dir);
            ctx.UI.SetSettings(false);ctx.Combat.Holster();ctx.Police.Clear();
            ctx.Combat.RestoreLoadoutJson(JsonUtility.ToJson(new CityLoadoutSave{weapons=CityCombat.Weapons.Select(w=>new CityWeaponSave{id=w.Id,loaded=w.Capacity,reserve=w.InitialReserve}).ToArray()}));
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA isolated weapon floor";floor.layer=CityContext.GroundLayer;floor.transform.position=new Vector3(0,199,0);floor.transform.localScale=new Vector3(120,2,120);
            floor.GetComponent<Renderer>().material.color=new Color(.17f,.21f,.23f);
            ctx.Motor.Warp(origin);ctx.Player.rotation=Quaternion.identity;ctx.Follow.Face(0);yield return Wait(.6f);
            capture=ctx.Camera.gameObject.AddComponent<CityAudioCapture>();capture.Begin(AudioSettings.outputSampleRate,35);
            int pool=ctx.Combat.GunEffects.PoolSize;
            foreach(var definition in CityCombat.Weapons)
            {
                ctx.Combat.Grant(definition.Id);ctx.Input.TouchWeapon=Array.IndexOf(CityCombat.Weapons,definition)+1;yield return Wait(.18f);
                var visual=ctx.Combat.Visual(definition.Id);
                CheckResult(definition.Id+"-selected-through-input",ctx.Combat.Equipped==definition.Id&&visual.Root.gameObject.activeSelf);
                var asset=visual.Root.GetComponent<CityWeaponAsset>();
                CheckResult(definition.Id+"-licensed-local-model",asset&&asset.License=="CC0-1.0"&&asset.SourceSha256.Length==64&&!string.IsNullOrEmpty(asset.SourceUrl),visual.SourceAsset);
                if(definition.Id=="pulse")CheckResult("pulse-emitter-enabled-in-player",asset.EmissionRenderer&&asset.EmissionRenderer.sharedMaterial.IsKeywordEnabled("_EMISSION")&&(asset.EmissionRenderer.sharedMaterial.globalIlluminationFlags&MaterialGlobalIlluminationFlags.EmissiveIsBlack)==0);
                CheckResult(definition.Id+"-source-geometry-preserved",visual.SourceTriangles>=500&&visual.Triangles>=visual.SourceTriangles&&visual.Triangles<=visual.SourceTriangles+24,"source="+visual.SourceTriangles+" imported="+visual.Triangles);
                CheckResult(definition.Id+"-faceted-batched-model",visual.Triangles>=500&&visual.Triangles<16000&&visual.RendererCount<=15,"triangles="+visual.Triangles+" renderers="+visual.RendererCount);
                CheckResult(definition.Id+"-no-weapon-colliders",visual.Root.GetComponentsInChildren<Collider>(true).Length==0);
                CaptureModel(definition);ctx.Input.TouchAim=true;yield return Wait(.6f);PlaceTarget("metal");yield return Wait(.1f);
                int shots=ctx.Combat.Shots,ammo=ctx.Combat.Ammo(definition.Id),bursts=ctx.Combat.GunEffects.MuzzleBursts,impacts=ctx.Combat.GunEffects.ImpactBursts,cases=ctx.Combat.GunEffects.EjectedCases;
                Vector3 intended=ctx.Combat.AimPoint,magazine=visual.Magazine.localPosition,action=visual.Action.localPosition;
                yield return Press();
                CheckResult(definition.Id+"-one-press-one-shot",ctx.Combat.Shots==shots+1&&ctx.Combat.Ammo(definition.Id)==ammo-1);
                CheckResult(definition.Id+"-hits-reticle",Vector3.Distance(ctx.Combat.LastImpact,intended)<.08f,"error="+Vector3.Distance(ctx.Combat.LastImpact,intended));
                CheckResult(definition.Id+"-real-muzzle-origin",Vector3.Distance(ctx.Combat.LastMuzzle,visual.Muzzle.position)<.19f,"error="+Vector3.Distance(ctx.Combat.LastMuzzle,visual.Muzzle.position));
                CheckResult(definition.Id+"-flash-and-contact",ctx.Combat.GunEffects.MuzzleBursts==bursts+1&&ctx.Combat.GunEffects.ImpactBursts==impacts+1&&ctx.Combat.GunEffects.LiveParticles>0);
                CheckResult(definition.Id+"-casing-type",ctx.Combat.GunEffects.EjectedCases==cases+(definition.Id=="pulse"?0:1));
                CheckResult(definition.Id+"-moving-action",Vector3.Distance(action,visual.Action.localPosition)>.003f);
                if(definition.Id=="pulse")
                {
                    // Inspect the live burst before capture/render work advances a frame.
                    // Short-lived particles may have expired after the preview captures.
                    var ps=ctx.Combat.GunEffects.Emitters.First(p=>p.particleCount>0);var frozen=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(frozen);
                    ctx.UI.SetSettings(true);yield return Wait(.14f);var after=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(after);
                    CheckResult("Esc-freezes-live-particles",after.Length==frozen.Length&&after.Length>0&&Mathf.Abs(after[0].remainingLifetime-frozen[0].remainingLifetime)<.0001f&&Vector3.Distance(after[0].position,frozen[0].position)<.0001f);ctx.UI.SetSettings(false);
                }
                yield return CheckParticles("muzzle-"+definition.Id);
                Capture("shot-"+definition.Id);CaptureEffects(visual,"muzzle-"+definition.Id,false);CaptureEffects(visual,"impact-"+definition.Id,true);
                yield return Wait(.035f);
                CheckResult(definition.Id+"-recorded-or-designed-shot",audio.EventCount(definition.Id=="pulse"?"pulse":definition.Id=="carbine"?"carbine":"shot")>0);
                yield return Wait(.20f);ctx.Input.TouchReload=true;yield return Wait(definition.Reload*.38f);
                CheckResult(definition.Id+"-reload-moves-cell-or-magazine",ctx.Combat.Reloading&&Vector3.Distance(magazine,visual.Magazine.localPosition)>.12f);
                Capture("reload-"+definition.Id);
                int frozenShots=ctx.Combat.Shots;Vector3 frozenMagazine=visual.Magazine.localPosition;ctx.UI.SetSettings(true);float clock=ctx.Clock;int particles=ctx.Combat.GunEffects.LiveParticles;yield return Wait(.22f);
                CheckResult(definition.Id+"-Esc-pauses-reload-effects",ctx.Clock==clock&&Vector3.Distance(frozenMagazine,visual.Magazine.localPosition)<.001f&&particles==ctx.Combat.GunEffects.LiveParticles);
                ctx.Input.TouchFire=true;yield return Frame();CheckResult(definition.Id+"-no-shot-in-settings",ctx.Combat.Shots==frozenShots);
                ctx.UI.SetSettings(false);yield return Wait(definition.Reload*.75f);
                CheckResult(definition.Id+"-reload-completes",!ctx.Combat.Reloading&&ctx.Combat.Ammo(definition.Id)==definition.Capacity&&Vector3.Distance(magazine,visual.Magazine.localPosition)<.003f);
                CheckResult(definition.Id+"-reload-audio",audio.EventCount("reload-"+definition.Id)>0);
                ctx.Input.TouchAim=false;yield return Wait(.1f);ctx.Police.Clear();
            }
            ctx.Input.TouchWeapon=2;ctx.Input.TouchAim=true;yield return Wait(.4f);
            foreach(string surface in new[]{"stone","wood","glass"})
            {
                PlaceTarget(surface);yield return Wait(.12f);int impacts=ctx.Combat.GunEffects.ImpactBursts;yield return Press();yield return Wait(.12f);
                CheckResult("impact-"+surface,ctx.Combat.GunEffects.ImpactBursts==impacts+1&&ctx.Combat.GunEffects.LastSurface==surface&&audio.EventCount("bullet-"+surface)>0);yield return Wait(.25f);
            }
            int marks=ctx.Combat.GunEffects.ImpactMarks;CheckResult("bounded-surface-marks",marks>=5,"marks="+marks);
            Destroy(target);yield return Wait(.15f);ctx.Follow.enabled=false;ctx.Camera.transform.rotation=Quaternion.Euler(-25,0,0);yield return Wait(.15f);
            int noContact=ctx.Combat.GunEffects.ImpactBursts;yield return Press();yield return Wait(.12f);
            CheckResult("miss-has-no-phantom-impact",ctx.Combat.GunEffects.ImpactBursts==noContact);ctx.Follow.enabled=true;ctx.Follow.Face(0);yield return Wait(.45f);
            PlaceTarget("metal");yield return Wait(.15f);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA close cover";wall.layer=CityContext.WorldLayer;wall.transform.position=ctx.Player.position+ctx.Player.forward*.28f+Vector3.up*1.25f;wall.transform.rotation=ctx.Player.rotation;wall.transform.localScale=new Vector3(3,2.5f,.14f);Physics.SyncTransforms();
            yield return Wait(.15f);int coverShots=ctx.Combat.Shots;yield return Press();yield return Wait(.12f);
            var cover=wall.GetComponent<Collider>();float coverError=Vector3.Distance(cover.ClosestPoint(ctx.Combat.LastImpact),ctx.Combat.LastImpact);
            CheckResult("cover-stops-shot-before-distant-target",ctx.Combat.Shots==coverShots+1&&coverError<.025f,"surface-error="+coverError+" impact="+ctx.Combat.LastImpact+" wall="+wall.transform.position);Capture("close-cover");Destroy(wall);yield return Wait(.3f);
            ctx.Input.TouchWeapon=3;yield return Wait(.2f);PlaceTarget("metal");yield return Wait(.12f);int before=ctx.Combat.Shots;holdFire=true;yield return Wait(.8f);holdFire=false;yield return Frame();
            CheckResult("carbine-held-trigger-keeps-cadence",ctx.Combat.Shots-before>=4&&ctx.Combat.Shots-before<=7,"shots="+(ctx.Combat.Shots-before));
            int stopped=ctx.Combat.Shots;yield return Wait(.3f);CheckResult("release-stops-automatic-fire",ctx.Combat.Shots==stopped);
            ctx.Input.TouchHolster=true;yield return Wait(.15f);CheckResult("X-hides-every-model",ctx.Combat.Equipped==null&&CityCombat.Weapons.All(w=>!ctx.Combat.Visual(w.Id).Root.gameObject.activeSelf));
            CheckResult("effects-pool-does-not-grow",ctx.Combat.GunEffects.PoolSize==pool,"pool="+pool);
            yield return Wait(1);CheckResult("short-traces-expire",!ctx.Combat.GunEffects.TracesVisible);
            capture.End(Path.Combine(dir,"weapon-mix.wav"));report.audioPeak=capture.Peak;report.audioFrames=capture.Frames;
            CheckResult("native-DSP-output",capture.Frames>1000&&capture.Peak>.005f&&capture.Peak<.99f,"frames="+capture.Frames+" peak="+capture.Peak);
            CheckResult("all-audio-clips-imported",audio.MissingClips==0);
            CheckResult("no-runtime-errors",report.errors.Count==0,string.Join("\n",report.errors));
            finished=true;File.WriteAllText(Path.Combine(dir,"weapons-qa.json"),JsonUtility.ToJson(report,true));Debug.Log("WEAPONS_QA_DONE "+report.passed);
            if(!Application.isEditor)Application.Quit(report.passed?0:1);else {Restore();Destroy(this);}
        }
        void Restore()
        {
            if(!ctx)return;holdFire=false;ctx.Input.TouchAim=ctx.Input.TouchRun=false;ctx.Input.TouchMove=Vector2.zero;ctx.UI.SetSettings(false);ctx.Follow.enabled=true;
            ctx.Combat.RestoreLoadoutJson(loadout);if(!string.IsNullOrEmpty(equipped))ctx.Combat.Select(equipped);ctx.Motor.Warp(saved);ctx.Player.rotation=savedRotation;ctx.Follow.ResetTracking();ctx.Police.Clear();ctx.Commerce.enabled=commerceEnabled;
            if(floor)Destroy(floor);if(target)Destroy(target);if(wall)Destroy(wall);if(capture)Destroy(capture);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(!finished&&ctx)Restore();}
    }
}
