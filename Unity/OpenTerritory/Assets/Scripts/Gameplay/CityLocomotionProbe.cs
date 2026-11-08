using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    // Explicitly attached in Play Mode or installed with --city-locomotion-qa.
    // No scene assets, normal-game input, or saved settings are modified.
    public sealed class CityLocomotionProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed=true; public List<Check> checks=new List<Check>(); }
        readonly Report report=new Report(); CityContext ctx; GameObject floor,wall,target; Vector3 saved; Quaternion rotation; string loadout,equipped;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-locomotion-qa")>=0)new GameObject("Locomotion QA opt-in").AddComponent<CityLocomotionProbe>();}
        void CheckResult(string name,bool passed,float value){report.passed&=passed;report.checks.Add(new Check{name=name,passed=passed,value=value});Debug.Log("LOCOMOTION_QA "+name+" "+passed+" "+value);}
        IEnumerator Hold(Vector2 move,float seconds,bool run=false){float end=Time.unscaledTime+seconds;while(Time.unscaledTime<end){ctx.Input.TouchMove=move;ctx.Input.TouchRun=run;yield return null;}}
        bool HeadFitsView(out float top)
        {
            top=0;bool inside=true;var renderers=ctx.Actors.PlayerRig.Head.GetComponentsInChildren<Renderer>();
            foreach(var renderer in renderers)
            {
                var bounds=renderer.bounds;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var screen=ctx.Camera.WorldToViewportPoint(corner);top=Mathf.Max(top,screen.y);
                    inside&=screen.z>ctx.Camera.nearClipPlane&&screen.x>.01f&&screen.x<.99f&&screen.y>.01f&&screen.y<.95f;
                }
            }
            return renderers.Length>0&&inside;
        }
        IEnumerator Start()
        {
            while(!CityContext.Instance||!CityContext.Instance.Ready)yield return null;
            ctx=CityContext.Instance;saved=ctx.Player.position;rotation=ctx.Player.rotation;loadout=ctx.Combat.ExportLoadoutJson();equipped=ctx.Combat.Equipped;
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA isolated locomotion floor";floor.layer=CityContext.GroundLayer;floor.transform.position=new Vector3(0,199,0);floor.transform.localScale=new Vector3(180,2,180);
            ctx.Motor.Warp(new Vector3(0,200.04f,0));ctx.Follow.Face(0);yield return Hold(Vector2.zero,.6f);
            float yaw=ctx.Camera.transform.eulerAngles.y;
            yield return Hold(Vector2.up,1);float walk=ctx.Motor.Speed;CheckResult("walk-speed",Mathf.Abs(walk-2.65f)<.2f,walk);
            yield return Hold(Vector2.one,.8f);float diagonal=ctx.Motor.Speed;CheckResult("diagonal-normalized",Mathf.Abs(diagonal-walk)<.2f,diagonal);
            yield return Hold(Vector2.up,1,true);float run=ctx.Motor.Speed;CheckResult("sprint-speed",Mathf.Abs(run-CityPlayer.SprintSpeed)<.2f,run);
            yield return Hold(Vector2.zero,.4f);CheckResult("braking-stops",ctx.Motor.Speed<.05f,ctx.Motor.Speed);
            yield return Hold(Vector2.right,1.5f);float drift=Mathf.Abs(Mathf.DeltaAngle(yaw,ctx.Camera.transform.eulerAngles.y));CheckResult("strafe-does-not-steer-camera",drift<1,drift);
            var before=ctx.Player.position;var cameraForward=CityPhysics.Flat(ctx.Camera.transform.forward).normalized;
            yield return Hold(Vector2.down,1);float backwards=Vector3.Dot(ctx.Player.position-before,-cameraForward);CheckResult("reverse-follows-camera",backwards>1.8f,backwards);
            yield return Hold(Vector2.zero,.4f);ctx.Input.TouchLook=new Vector2(25,0);yield return null;yield return Hold(Vector2.zero,.4f);
            float orbit=Mathf.Abs(Mathf.DeltaAngle(yaw,ctx.Camera.transform.eulerAngles.y));CheckResult("manual-orbit",orbit>45,orbit);
            before=ctx.Player.position;cameraForward=CityPhysics.Flat(ctx.Camera.transform.forward).normalized;
            yield return Hold(Vector2.up,.8f);float forward=Vector3.Dot(ctx.Player.position-before,cameraForward);CheckResult("move-uses-new-orbit",forward>1.5f,forward);
            yield return Hold(Vector2.zero,.4f);float ground=ctx.Player.position.y;ctx.Input.TouchJump=true;
            yield return Hold(Vector2.zero,.18f);float rise=ctx.Player.position.y-ground;CheckResult("jump-leaves-ground",rise>.13f,rise);
            yield return Hold(Vector2.zero,.9f);CheckResult("jump-lands",ctx.Motor.Grounded&&Mathf.Abs(ctx.Player.position.y-ground)<.1f,ctx.Player.position.y-ground);
            var rig=ctx.Actors.PlayerRig;CheckResult("articulated-pose-valid",rig!=null&&rig.Valid&&float.IsFinite(rig.Hips.position.y),rig==null?0:rig.Hips.position.y);
            ctx.Motor.Warp(new Vector3(0,200.04f,0));ctx.Follow.Face(0);yield return Hold(Vector2.zero,.5f);
            float clear=ctx.Follow.ObstructionDistance;
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA camera wall";wall.layer=CityContext.WorldLayer;wall.transform.position=new Vector3(0,202,-2.0f);wall.transform.localScale=new Vector3(18,5,.35f);
            yield return Hold(Vector2.zero,.6f);float blocked=ctx.Follow.ObstructionDistance;CheckResult("camera-wall-retracts",blocked<clear*.75f&&blocked>.3f,blocked);
            Destroy(wall);yield return Hold(Vector2.zero,1.5f);CheckResult("camera-restores-distance",ctx.Follow.ObstructionDistance>clear-.2f,ctx.Follow.ObstructionDistance);
            ctx.Player.rotation=Quaternion.identity;ctx.Follow.Face(0);yield return Hold(Vector2.zero,.5f);
            var chest=ctx.Actors.PlayerRig.Chest;
            float normalX=ctx.Camera.WorldToViewportPoint(chest.position).x;
            CheckResult("exploration-offset-shoulder",normalX<.44f,normalX);
            float normalDistance=ctx.Follow.ObstructionDistance;
            float normalPitch=ctx.Camera.transform.eulerAngles.x;
            ctx.Input.TouchWeapon=1;yield return Hold(Vector2.zero,.25f);
            CheckResult("drawing-weapon-preserves-camera",Mathf.Abs(ctx.Follow.ObstructionDistance-normalDistance)<.03f&&ctx.Follow.AimBlend<.01f,ctx.Follow.ObstructionDistance);
            ctx.Input.TouchAim=true;yield return Hold(Vector2.zero,.75f);
            float aimedX=ctx.Camera.WorldToViewportPoint(chest.position).x;
            CheckResult("aim-closes-over-shoulder",ctx.Follow.ObstructionDistance<normalDistance*.9f&&aimedX<normalX-.03f,aimedX);
            float headTop;CheckResult("aim-keeps-entire-head-visible",HeadFitsView(out headTop),headTop);
            float handX=ctx.Camera.WorldToViewportPoint(ctx.Actors.PlayerRig.Hands[1].position).x;
            CheckResult("aim-hand-clears-reticle",handX<.47f,handX);
            float pitchDrift=Mathf.Abs(Mathf.DeltaAngle(normalPitch,ctx.Camera.transform.eulerAngles.x));
            CheckResult("aim-does-not-tilt-view",pitchDrift<.25f,pitchDrift);
            float bodyError=Mathf.Abs(Mathf.DeltaAngle(ctx.Player.eulerAngles.y,ctx.Camera.transform.eulerAngles.y-18));
            CheckResult("stationary-aim-aligns-body",bodyError<2,bodyError);
            var aimScreen=ctx.Camera.WorldToViewportPoint(ctx.Combat.AimPoint);
            CheckResult("reticle-and-target-aligned",Mathf.Abs(aimScreen.x-.5f)<.002f&&Mathf.Abs(aimScreen.y-.5f)<.002f,Vector2.Distance(new Vector2(aimScreen.x,aimScreen.y),new Vector2(.5f,.5f)));
            yield return Hold(Vector2.right,.8f,true);
            CheckResult("aim-strafe-speed-limited",ctx.Motor.Speed<1.85f&&ctx.Motor.Speed>1.4f,ctx.Motor.Speed);
            bodyError=Mathf.Abs(Mathf.DeltaAngle(ctx.Player.eulerAngles.y,ctx.Camera.transform.eulerAngles.y-18));CheckResult("aim-strafe-retains-facing",bodyError<2,bodyError);
            yield return Hold(Vector2.down,.8f);CheckResult("aim-backstep",ctx.Motor.Speed<1.45f&&ctx.Motor.Speed>1.1f,ctx.Motor.Speed);
            yield return Hold(Vector2.zero,.4f);target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="QA reticle target";target.layer=CityContext.WorldLayer;
            var ray=ctx.Follow.AimRay;target.transform.position=ray.GetPoint(6);target.transform.rotation=Quaternion.LookRotation(ray.direction);target.transform.localScale=new Vector3(2,2,.2f);
            yield return Hold(Vector2.zero,.15f);Vector3 intended=ctx.Combat.AimPoint;int shots=ctx.Combat.Shots;ctx.Input.TouchFire=true;yield return null;yield return Hold(Vector2.zero,.15f);CheckResult("aim-fire",ctx.Combat.Shots==shots+1,ctx.Combat.Shots-shots);
            float impactError=Vector3.Distance(intended,ctx.Combat.LastImpact);CheckResult("shot-hits-reticle-target",impactError<.08f,impactError);Destroy(target);
            ctx.Combat.Reload();yield return Hold(Vector2.zero,.2f);
            CheckResult("reload-preserves-aim-camera",ctx.Combat.Reloading&&Mathf.Abs(ctx.Follow.ObstructionDistance-CityFollowCamera.AimDistance)<.05f,ctx.Follow.ObstructionDistance);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA aiming camera wall";wall.layer=CityContext.WorldLayer;
            var cameraBack=-ctx.Camera.transform.forward;wall.transform.position=ctx.Player.position+Vector3.up*2+cameraBack*1.5f;wall.transform.rotation=Quaternion.Euler(0,ctx.Camera.transform.eulerAngles.y,0);wall.transform.localScale=new Vector3(18,5,.35f);
            yield return Hold(Vector2.zero,.4f);
            CheckResult("aim-camera-stays-in-front-of-wall",!Physics.CheckSphere(ctx.Camera.transform.position,.12f,ctx.SolidMask,QueryTriggerInteraction.Ignore)&&ctx.Follow.ObstructionDistance<1.5f,ctx.Follow.ObstructionDistance);
            Destroy(wall);yield return Hold(Vector2.zero,1.3f);
            CheckResult("aim-camera-recovers-after-wall",Mathf.Abs(ctx.Follow.ObstructionDistance-CityFollowCamera.AimDistance)<.08f,ctx.Follow.ObstructionDistance);
            for(int i=0;i<3;i++){ctx.Input.TouchAim=false;yield return Hold(Vector2.zero,.08f);ctx.Input.TouchAim=true;yield return Hold(Vector2.zero,.08f);}
            yield return Hold(Vector2.zero,.6f);
            CheckResult("rapid-aim-toggle-settles",ctx.Follow.AimBlend>.99f&&HeadFitsView(out headTop),ctx.Follow.AimBlend);
            ctx.Input.TouchAim=false;ctx.Combat.Holster();yield return Hold(Vector2.zero,.8f);CheckResult("aim-exit-restores-framing",ctx.Follow.AimBlend<.01f&&ctx.Follow.ObstructionDistance>normalDistance-.2f,ctx.Follow.ObstructionDistance);
            ctx.UI.SetSettings(true);var paused=ctx.Player.position;yield return Hold(Vector2.up,.25f);CheckResult("settings-pause-preserved",Vector3.Distance(paused,ctx.Player.position)<.001f,Vector3.Distance(paused,ctx.Player.position));ctx.Input.TouchMove=Vector2.zero;ctx.UI.SetSettings(false);
            var args=Environment.GetCommandLineArgs();int reportAt=Array.IndexOf(args,"--city-report-dir");
            string dir=reportAt>=0&&reportAt+1<args.Length?args[reportAt+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Reports/locomotion"));
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"qa.json"),JsonUtility.ToJson(report,true));
            Debug.Log("LOCOMOTION_QA_DONE "+report.passed);Destroy(this);
            if(!Application.isEditor&&Array.IndexOf(args,"--city-locomotion-qa")>=0)Application.Quit(report.passed?0:1);
        }
        bool quitting;
        void OnApplicationQuit(){quitting=true;}
        void OnDestroy(){if(quitting)return;if(ctx&&ctx.Player&&ctx.Motor&&ctx.UI&&ctx.Combat&&ctx.Follow){ctx.Input.TouchMove=ctx.Input.TouchLook=Vector2.zero;ctx.Input.TouchRun=ctx.Input.TouchAim=false;ctx.UI.SetSettings(false);ctx.Combat.RestoreLoadoutJson(loadout);if(!string.IsNullOrEmpty(equipped))ctx.Combat.Select(equipped);ctx.Motor.Warp(saved);ctx.Player.rotation=rotation;ctx.Follow.ResetTracking();}if(floor)Destroy(floor);if(wall)Destroy(wall);if(target)Destroy(target);}
    }
}
