using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class CityLinearParkInspectGround
    {
        public static void Inspect()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity",OpenSceneMode.Single);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-linear-rebuild")>=0)CityLinearParkBuilder.Rebuild();
            Physics.SyncTransforms();
            var park=UnityEngine.Object.FindFirstObjectByType<CityLinearPark>();var text=new StringBuilder();
            foreach(var path in park.paths)for(int i=1;i<path.points.Length;i++)
            {
                var a=path.points[i-1];var b=path.points[i];var dir=CityPhysics.Flat(b-a).normalized;var side=new Vector3(-dir.z,0,dir.x);
                int steps=Mathf.CeilToInt(CityPhysics.Flat(b-a).magnitude/.55f);
                for(int j=0;j<=steps;j++)foreach(float lane in new[]{-.65f,0f,.65f})
                {
                    var p=Vector3.Lerp(a,b,j/(float)Mathf.Max(1,steps))+side*lane;
                    var hits=Physics.RaycastAll(p+Vector3.up*5,Vector3.down,10,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y).ToArray();
                    if(hits.Length==0||hits[0].transform.IsChildOf(park.transform))continue;
                    text.Append(path.name).Append(" point=").Append(p.ToString("F4")).Append(" hits=");
                    foreach(var h in hits.Take(8))text.Append(h.collider.name).Append('@').Append(h.point.y.ToString("F4",CultureInfo.InvariantCulture)).Append(h.transform.IsChildOf(park.transform)?"(park)":"(old)").Append(' ');
                    text.AppendLine();
                }
            }
            foreach(var pair in new[]{new[]{new Vector3(504.4f,0,590.6f),new Vector3(508.2f,0,591.9f)},new[]{new Vector3(570.6f,0,612.3f),new Vector3(572.3f,0,607f)}})
            {
                text.AppendLine("\nPROFILE " + pair[0]+" -> "+pair[1]);
                for(int i=0;i<=30;i++)
                {
                    var p=Vector3.Lerp(pair[0],pair[1],i/30f);var all=Physics.RaycastAll(p+Vector3.up*20,Vector3.down,40,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y).Take(4).ToArray();
                    text.Append(p.x.ToString("F2",CultureInfo.InvariantCulture)).Append(',').Append(p.z.ToString("F2",CultureInfo.InvariantCulture)).Append(' ');
                    foreach(var h in all)text.Append(h.collider.name).Append('@').Append(h.point.y.ToString("F3",CultureInfo.InvariantCulture)).Append(" nY=").Append(h.normal.y.ToString("F2",CultureInfo.InvariantCulture)).Append(' ');
                    text.AppendLine();
                }
            }
            foreach(var p in new[]{new Vector3(505.55f,0,591f),new Vector3(571f,0,611.06f)})
            foreach(var floor in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            {
                if(!floor.name.StartsWith("Piso-")||!floor.sharedMesh||p.x<floor.bounds.min.x||p.x>floor.bounds.max.x||p.z<floor.bounds.min.z||p.z>floor.bounds.max.z)continue;
                var v=floor.sharedMesh.vertices;var ts=floor.sharedMesh.triangles;
                for(int i=0;i<ts.Length;i+=3)
                {
                    var a=floor.transform.TransformPoint(v[ts[i]]);var b=floor.transform.TransformPoint(v[ts[i+1]]);var c=floor.transform.TransformPoint(v[ts[i+2]]);
                    float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(den)<.00001f)continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/den,q=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/den;
                    if(u<0||q<0||u+q>1)continue;float y=u*a.y+q*b.y+(1-u-q)*c.y;if(y<8.1f||y>8.6f)continue;
                    text.AppendLine("TRI "+floor.name+" p="+p+" y="+y.ToString("F4")+" a="+a.ToString("F4")+" b="+b.ToString("F4")+" c="+c.ToString("F4"));
                }
            }
            var args=Environment.GetCommandLineArgs();File.WriteAllText(args[Array.IndexOf(args,"-linear-output")+1],text.ToString());
            Debug.Log("LINEAR_PARK_GROUND_INSPECTION " + text.ToString().Split('\n').Length);
        }
    }
}
