using System;
using UnityEngine;

namespace OpenTerritory
{
    [Serializable] public class ReferenceRoad { public string id,kind; public float width; public Vector3[] points; }
    [Serializable] public class ReferenceLayout
    {
        public string id;public float minX,maxX,minZ,maxZ,metersPerPixel,originX,originY;
        public Vector2[] coast;public ReferenceRoad[] roads;public Vector3 fountain,harborRadio;
    }
    // Geometry is authored into the scene. This only exposes its shared physical
    // coordinates to swimming, drones and opt-in QA; it creates no second map.
    public static class ReferenceMap
    {
        public static ReferenceLayout Layout {get;private set;}
        static GroundData ground;
        public static bool Active=>Layout!=null;
        public static void Bind(WorldData world)
        {
            Layout=null;ground=world.ground;
            if(string.IsNullOrEmpty(world.layoutId))return;
            var asset=Resources.Load<TextAsset>("reference-map");
            if(!asset)throw new InvalidOperationException("Traced map layout missing");
            Layout=JsonUtility.FromJson<ReferenceLayout>(asset.text);
            if(Layout.id!=world.layoutId)throw new InvalidOperationException("Map manifest and layout do not match");
        }
        public static Vector3 FromPixel(float u,float v,float y=0)=>new Vector3((u-Layout.originX)*Layout.metersPerPixel,y,(Layout.originY-v)*Layout.metersPerPixel);
        public static bool Contains(Vector3 p)=>Active&&p.x>Layout.minX&&p.x<Layout.maxX&&p.z>Layout.minZ&&p.z<Layout.maxZ;
        public static bool Water(Vector3 p)=>Contains(p)&&Height(p.x,p.z)<CityMarine.SeaLevel;
        public static float Height(float x,float z)
        {
            if(ground==null||ground.heights.Length==0)return 0;
            float fx=Mathf.Clamp((x-ground.x)/ground.step,0,ground.width-1.001f),fz=Mathf.Clamp((z-ground.z)/ground.step,0,ground.height-1.001f);
            int i=(int)fx,j=(int)fz;float a=fx-i,b=fz-j;int at=j*ground.width+i;
            float h=ground.heights[at],right=ground.heights[at+1],top=ground.heights[at+ground.width],corner=ground.heights[at+ground.width+1];
            return a+b<=1?h+a*(right-h)+b*(top-h):corner+(1-a)*(top-corner)+(1-b)*(right-corner);
        }
    }
}
