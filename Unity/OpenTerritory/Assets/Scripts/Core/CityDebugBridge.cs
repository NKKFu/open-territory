using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace OpenTerritory {
// Opt-in local development control. Absent unless the player is launched with --city-debug.
[DefaultExecutionOrder(-1100)]
public class CityDebugBridge:MonoBehaviour {
 [Serializable] public class Command {public int seq;public string op,path,action,weather,weapon;public Vector3 point,lookAt;public float yaw,seconds=1,moveX,moveY;public bool run,jump,aim,fire,interact,talk,hack,drone,pause,follow=true;}
 [Serializable] public class Sample {public int seq,frame,errors;public bool ready,grounded,paused;public float fps,speed,health,rain,clock;public Vector3 position,camera;public string mode,action,actionKey,speech,police;public string[] nearby;public CarSample[] cars;}
 [Serializable] public class CarSample {public int id;public Vector3 position;public float speed;public string kind;}
 CityContext ctx;string dir;int lastSeq=-1,errorCount;float nextRead,inputUntil;Command input;bool captured;float elapsed;int frames;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void StartDebug(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--city-debug");if(i<0)return;var bridge=new GameObject("Diagnóstico local opt-in").AddComponent<CityDebugBridge>();bridge.dir=i+1<args.Length?args[i+1]:Application.persistentDataPath;Directory.CreateDirectory(bridge.dir);}
 void OnEnable(){Application.logMessageReceived+=OnLog;}void OnDisable(){Application.logMessageReceived-=OnLog;}
 void OnLog(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error)errorCount++;}
 void Update(){ctx=CityContext.Instance;if(!ctx||!ctx.Ready)return;elapsed+=Time.unscaledDeltaTime;frames++;if(input!=null){if(Time.unscaledTime<inputUntil){ctx.Input.TouchMove=new Vector2(input.moveX,input.moveY);ctx.Input.TouchRun=input.run;ctx.Input.TouchAim=input.aim;ctx.Input.TouchFire=input.fire;ctx.Input.TouchHack=input.hack;}else{ctx.Input.TouchMove=Vector2.zero;ctx.Input.TouchRun=ctx.Input.TouchAim=ctx.Input.TouchFire=ctx.Input.TouchHack=false;input=null;}}
  if(Time.unscaledTime<nextRead)return;nextRead=Time.unscaledTime+.1f;string path=Path.Combine(dir,"command.json");if(!File.Exists(path))return;Command c;try{c=JsonUtility.FromJson<Command>(File.ReadAllText(path));}catch{return;}if(c==null||c.seq==lastSeq)return;lastSeq=c.seq;
  try{
   switch(c.op){
    case "warp":ctx.Motor.Warp(c.point);ctx.Player.rotation=Quaternion.Euler(0,c.yaw,0);ctx.Follow.Face(c.yaw);break;
    case "camera":ctx.Follow.enabled=c.follow;if(!c.follow){ctx.Camera.transform.position=c.point;ctx.Camera.transform.LookAt(c.lookAt);}break;
    case "input":input=c;inputUntil=Time.unscaledTime+Mathf.Clamp(c.seconds,.02f,15);ctx.Input.TouchJump=c.jump;ctx.Input.TouchInteract=c.interact;ctx.Input.TouchTalk=c.talk;ctx.Input.TouchDrone=c.drone;break;
    case "pause":ctx.Pause(c.pause);break;
    case "weather":ctx.Systems.Weather.Request(c.weather,true);break;
    case "action":var a=ctx.Actions.Find(a=>a.id==c.action);if(a!=null&&(a.Enabled==null||a.Enabled()))a.Execute();break;
    case "weapon":ctx.Combat.Grant(c.weapon);ctx.Combat.Select(c.weapon);break;
    case "screenshot":StartCoroutine(Capture(c.path));break;
    case "quit":Application.Quit();break;
   }
  }catch(Exception e){Debug.LogException(e);}StartCoroutine(Snapshot());
 }
 IEnumerator Capture(string path){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(string.IsNullOrEmpty(path)?Path.Combine(dir,"native.png"):path);}
 IEnumerator Snapshot(){yield return new WaitForEndOfFrame();var p=ctx.Player.position;var s=new Sample{seq=lastSeq,frame=Time.frameCount,errors=errorCount,ready=ctx.Ready,grounded=ctx.Motor.Grounded,paused=ctx.Paused,fps=frames/Mathf.Max(.001f,elapsed),speed=ctx.Motor.Speed,health=ctx.Combat.Health,rain=ctx.Rain,clock=ctx.Clock,position=p,camera=ctx.Camera.transform.position,mode=ctx.Motor.Mode,action=ctx.CurrentAction?.Text,actionKey=ctx.CurrentAction?.KeyLabel,speech=ctx.Speech,police=ctx.Police.Stage,nearby=ctx.Actions.Where(a=>Vector3.Distance(a.Position,p)<12).Select(a=>a.id+" "+a.Text).ToArray(),cars=ctx.Vehicles.Fleet.Select(c=>new CarSample{id=c.Data.id,position=c.Position,speed=c.Speed,kind=c.Data.kind}).ToArray()};File.WriteAllText(Path.Combine(dir,"state.json"),JsonUtility.ToJson(s,true));}
 }
}
