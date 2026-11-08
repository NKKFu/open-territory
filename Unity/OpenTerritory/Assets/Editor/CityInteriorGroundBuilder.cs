using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    // The retained donor floors are level. Remove only high terrain fragments
    // inside their rotated footprints, preserving the surrounding landscape.
    public static class CityInteriorGroundBuilder
    {
        sealed class Lot { public Matrix4x4 inverse; public float x,z,floor; public Bounds bounds; }
        struct Vertex
        {
            public Vector3 p,n;public Vector2 uv,uv2;public Color color;
            public static Vertex Mix(Vertex a,Vertex b,float t)=>new Vertex{p=Vector3.LerpUnclamped(a.p,b.p,t),n=Vector3.LerpUnclamped(a.n,b.n,t),uv=Vector2.LerpUnclamped(a.uv,b.uv,t),uv2=Vector2.LerpUnclamped(a.uv2,b.uv2,t),color=Color.LerpUnclamped(a.color,b.color,t)};
        }
        public static void Bake()
        {
            var homes=UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();var registry=UnityEngine.Object.FindFirstObjectByType<WorldRegistry>();if(!homes||!registry)return;
            var world=JsonUtility.FromJson<WorldData>(registry.manifest.text);var library=AssetDatabase.LoadMainAssetAtPath("Assets/Imported/CityHomes.asset");
            var lots=new List<Lot>();
            foreach(var home in homes.homes)
            {
                var p=world.places[home.place];var lot=new Lot{inverse=home.root.worldToLocalMatrix,x=p.lotWidth*.5f-.05f,z=p.lotDepth*.5f-.05f,floor=p.elevation+.205f};
                if(home.units==1&&!home.root.Find("Base física nivelada"))
                {
                    var floor=new GameObject("Base física nivelada");floor.transform.SetParent(home.root,false);floor.layer=CityContext.GroundLayer;
                    var shape=floor.AddComponent<BoxCollider>();shape.center=new Vector3(0,.10f,0);shape.size=new Vector3(p.lotWidth-.42f,.16f,p.lotDepth-.42f);
                }
                lot.bounds=new Bounds(home.root.position,Vector3.zero);
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})lot.bounds.Encapsulate(home.root.TransformPoint(new Vector3(x*lot.x,0,z*lot.z)));lots.Add(lot);
            }
            var cache=new Dictionary<Mesh,Mesh>();int changed=0;
            Mesh Cut(Mesh source,Transform owner)
            {
                if(cache.TryGetValue(source,out var cached))return cached;
                var points=source.vertices;var ns=source.normals;var us=source.uv;var vs=source.uv2;var cs=source.colors;var triangles=source.triangles;
                var matrix=owner.localToWorldMatrix;var inverse=matrix.inverse;
                Bounds bounds=new Bounds(matrix.MultiplyPoint3x4(source.bounds.center),Vector3.zero);
                for(int c=0;c<8;c++)bounds.Encapsulate(matrix.MultiplyPoint3x4(source.bounds.center+Vector3.Scale(source.bounds.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1))));
                bool XzOverlap(Bounds a,Bounds b)=>a.min.x<=b.max.x&&a.max.x>=b.min.x&&a.min.z<=b.max.z&&a.max.z>=b.min.z;
                var candidates=lots.Where(l=>XzOverlap(bounds,l.bounds)&&bounds.max.y>l.floor).ToArray();if(candidates.Length==0){cache[source]=source;return source;}
                var output=new List<Vertex>();bool edited=false;
                Vertex Read(int i)=>new Vertex{p=matrix.MultiplyPoint3x4(points[i]),n=ns.Length==points.Length?ns[i]:Vector3.up,uv=us.Length==points.Length?us[i]:Vector2.zero,uv2=vs.Length==points.Length?vs[i]:Vector2.zero,color=cs.Length==points.Length?cs[i]:Color.white};
                void Polygon(List<Vertex> polygon)
                {for(int j=1;j+1<polygon.Count;j++)if(Vector3.Cross(polygon[j].p-polygon[0].p,polygon[j+1].p-polygon[0].p).sqrMagnitude>1e-12f){output.Add(polygon[0]);output.Add(polygon[j]);output.Add(polygon[j+1]);}}
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=Read(triangles[i]);var b=Read(triangles[i+1]);var c=Read(triangles[i+2]);var box=new Bounds(a.p,Vector3.zero);box.Encapsulate(b.p);box.Encapsulate(c.p);
                    var relevant=candidates.Where(l=>box.max.y>l.floor&&XzOverlap(box,l.bounds)).ToArray();
                    if(relevant.Length==0){output.Add(a);output.Add(b);output.Add(c);continue;}
                    var polygons=new List<List<Vertex>>{new List<Vertex>{a,b,c}};
                    foreach(var lot in relevant)
                    {
                        var remainder=new List<List<Vertex>>();
                        foreach(var polygon in polygons)
                        {
                            if(polygon.Count<3)continue;
                            var inside=polygon;
                            for(int side=0;side<4&&inside.Count>=3;side++)
                            {
                                float Distance(Vertex v){var local=lot.inverse.MultiplyPoint3x4(v.p);return side==0?lot.x+local.x:side==1?lot.x-local.x:side==2?lot.z+local.z:lot.z-local.z;}
                                var outside=Clip(inside,Distance,false);if(outside.Count>=3)remainder.Add(outside);inside=Clip(inside,Distance,true);
                            }
                            if(inside.Count>=3)edited=true;
                        }
                        polygons=remainder;
                    }
                    foreach(var polygon in polygons)Polygon(polygon);
                }
                if(!edited){cache[source]=source;return source;}
                var result=new Mesh{name=source.name+" • pisos internos livres",indexFormat=IndexFormat.UInt32};
                result.SetVertices(output.Select(v=>inverse.MultiplyPoint3x4(v.p)).ToList());result.SetNormals(output.Select(v=>v.n.normalized).ToList());result.SetUVs(0,output.Select(v=>v.uv).ToList());result.SetUVs(1,output.Select(v=>v.uv2).ToList());result.SetColors(output.Select(v=>v.color).ToList());result.SetTriangles(Enumerable.Range(0,output.Count).ToArray(),0);result.RecalculateBounds();AssetDatabase.AddObjectToAsset(result,library);cache[source]=result;changed++;return result;
            }
            foreach(var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(collider.name.StartsWith("Piso-")&&collider.sharedMesh)collider.sharedMesh=Cut(collider.sharedMesh,collider.transform);
            foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(filter.sharedMesh&&filter.transform.parent&&filter.transform.parent.name.StartsWith("surface:"))
                {var renderer=filter.GetComponent<Renderer>();if(renderer&&renderer.sharedMaterial&&renderer.sharedMaterial.HasProperty("_Sea")&&renderer.sharedMaterial.GetFloat("_Sea")>.5f)continue;filter.sharedMesh=Cut(filter.sharedMesh,filter.transform);}
            AssetDatabase.SaveAssets();Physics.SyncTransforms();Debug.Log("CITY_INTERIOR_GROUND_FITTED meshes="+changed);
        }
        static List<Vertex> Clip(List<Vertex> polygon,Func<Vertex,float> distance,bool inside)
        {
            var output=new List<Vertex>();if(polygon.Count==0)return output;var previous=polygon[polygon.Count-1];float before=distance(previous)*(inside?1:-1);
            foreach(var current in polygon)
            {
                float after=distance(current)*(inside?1:-1);bool a=before>=0,b=after>=0;
                if(a!=b)output.Add(Vertex.Mix(previous,current,before/(before-after)));if(b)output.Add(current);previous=current;before=after;
            }
            return output;
        }
    }
}
