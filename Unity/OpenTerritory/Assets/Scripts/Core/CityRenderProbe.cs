using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace OpenTerritory {
public class CityRenderProbe:MonoBehaviour {
 CityContext ctx;string dir;int errors;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--city-render-qa")>=0)new GameObject("Verificação de render nativo").AddComponent<CityRenderProbe>();}
 void Awake(){Application.logMessageReceived+=Log;}void OnDestroy(){Application.logMessageReceived-=Log;}
 void Log(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error){errors++;Debug.LogWarning("RENDER_QA_ERROR "+text);}}
 IEnumerator Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--city-report-dir");dir=i>=0&&i+1<args.Length?args[i+1]:Application.persistentDataPath;Directory.CreateDirectory(dir);float end=Time.realtimeSinceStartup+60;while((!CityContext.Instance||!CityContext.Instance.Ready)&&Time.realtimeSinceStartup<end)yield return null;ctx=CityContext.Instance;if(!ctx||!ctx.Ready){Application.Quit(2);yield break;}
  ctx.Pause(false);ctx.Motor.SetInputLock(true);ctx.Follow.enabled=false;yield return new WaitForSeconds(1.5f);
  Place(new Vector3(-24,0,42),0);yield return new WaitForSeconds(.3f);Capture("01-praca",ctx.Player.position+new Vector3(6,4,-8),ctx.Player.position+new Vector3(-5,1.4f,-1));
  Capture("02-cidade",new Vector3(-110,90,160),new Vector3(0,4,0));
  var p=ctx.Data.places[0];Place(new Vector3(p.x,p.elevation+.2f,p.z+3),180);yield return new WaitForSeconds(.3f);Capture("03-interior",new Vector3(p.x+5,p.elevation+3.9f,p.z+6),new Vector3(p.x-2,p.elevation+1.5f,p.z-4));
  Place(new Vector3(151,0,-143),180);yield return new WaitForSeconds(.3f);Capture("04-cais",new Vector3(137,12,-136),new Vector3(164,2,-159));
  Capture("05-barcos",new Vector3(169,6,-164),new Vector3(164,-.1f,-181));
  Place(new Vector3(-161,0,-96),180);yield return new WaitForSeconds(.3f);Capture("06-carros",new Vector3(-145,8,-94),new Vector3(-161,2,-103));
  Place(new Vector3(-26,0,24),180);yield return new WaitForSeconds(.3f);Capture("07-personagens",ctx.Player.position+new Vector3(-5,3.5f,-7),new Vector3(-26,ctx.Ground(-26,19.2f)+1.4f,19.2f));
  Place(new Vector3(-20,0,62),180);ctx.Systems.Weather.Request("rain",true);yield return new WaitForSeconds(1.5f);Capture("08-chuva",ctx.Player.position+new Vector3(5,3.8f,-8),ctx.Player.position+new Vector3(0,1.3f,5));
  File.WriteAllText(Path.Combine(dir,"render-qa.json"),"{\"passed\":"+(errors==0?"true":"false")+",\"screenshots\":8,\"errors\":"+errors+"}");Application.Quit(errors==0?0:2);
 }
 void Place(Vector3 p,float yaw){p.y=ctx.Ground(p.x,p.z)+.04f;ctx.Motor.Warp(p);ctx.Player.rotation=Quaternion.Euler(0,yaw,0);}
 void Capture(string name,Vector3 eye,Vector3 target){var cam=ctx.Camera;cam.transform.position=eye;cam.transform.LookAt(target);cam.fieldOfView=58;var rt=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);rt.antiAliasing=2;rt.Create();cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),image.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=old;rt.Release();Destroy(rt);Destroy(image);Debug.Log("RENDER_QA_CAPTURE "+name);}
}
}
