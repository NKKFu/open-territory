using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Authored crown layouts, deterministic variation and shared silhouettes across LODs.
    public static class CityTreeModels
    {
        public static Mesh Create(int variant, int lod)
        {
            int form = variant % 8, palette = variant / 8;
            var mesh = new Parts();
            Color bark = new Color(.14f, .085f, .043f);
            Color leaf = palette == 0 ? new Color(.085f, .225f, .068f) : palette == 1 ? new Color(.145f, .27f, .075f) : new Color(.065f, .195f, .115f);
            if (form == 3 || form == 4) leaf *= .79f;
            var random = new System.Random(7109 + variant * 197);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            var fork = new Vector3(form == 6 ? .18f : -.08f, form == 7 ? 2.75f : 3.45f, .04f);
            mesh.Limb(Vector3.zero, new Vector3(.06f, 1.8f, .015f), .31f, .235f, bark, lod);
            mesh.Limb(new Vector3(.06f, 1.8f, .015f), fork, .235f, .17f, bark, lod);
            if (lod == 0)
                for (int r = 0; r < 4; r++)
                {
                    float a = r * Mathf.PI * .5f + .3f;
                    mesh.Limb(new Vector3(Mathf.Cos(a) * .46f, .035f, Mathf.Sin(a) * .46f), Vector3.up * .65f, .09f, .12f, bark * .85f, lod);
                }
            int crownIndex = 0;
            void Crown(Vector3 center, Vector3 radius, Color color, bool cone = false, bool branch = true)
            {
                // Consume exactly the same random values at all LODs.
                center += new Vector3(R(-.17f,.17f), R(-.13f,.13f), R(-.17f,.17f));
                radius = Vector3.Scale(radius, new Vector3(R(.92f,1.08f), R(.94f,1.08f), R(.92f,1.08f)));
                int seed = variant * 173 + crownIndex++ * 37;
                mesh.Crown(center, radius, color, seed, lod, cone);
                if (branch)
                {
                    var elbow = Vector3.Lerp(fork, center, .55f) + Vector3.down * .33f;
                    mesh.Limb(fork, elbow, .135f, .09f, bark, lod);
                    if (lod < 2) mesh.Limb(elbow, center + Vector3.down * .25f, .09f, .035f, bark * 1.1f, lod);
                }
            }
            if (form == 0) // Broad oak: distinct lobes and an exposed branching framework.
            {
                Crown(new Vector3(-.25f,6.25f,.1f),new Vector3(1.65f,1.5f,1.6f),leaf*1.09f);
                for (int j=0;j<6;j++)
                {
                    float a=j*Mathf.PI/3+.35f;
                    Crown(new Vector3(Mathf.Cos(a)*1.65f,4.95f+R(-.3f,.45f),Mathf.Sin(a)*1.55f),new Vector3(1.65f,1.35f,1.55f),leaf*R(.83f,1.07f));
                }
            }
            else if (form == 1) // Acacia: spreading limbs beneath a shallow, airy crown.
            {
                Crown(new Vector3(.15f,5.75f,.05f),new Vector3(1.8f,.95f,1.7f),leaf*1.05f);
                for(int j=0;j<5;j++)
                {
                    float a=j*Mathf.PI*2/5+.2f;
                    Crown(new Vector3(Mathf.Cos(a)*2.05f,5.15f+R(-.2f,.25f),Mathf.Sin(a)*1.75f),new Vector3(1.8f,.92f,1.6f),leaf*R(.88f,1.1f));
                }
            }
            else if (form == 2) // Poplar: narrow, irregular clusters along a tall trunk.
            {
                mesh.Limb(fork,new Vector3(.14f,7.2f,-.12f),.16f,.045f,bark,lod);
                for(int j=0;j<5;j++)
                {
                    float t=j/4f,w=1.2f*(1-.43f*t);
                    Crown(new Vector3(Mathf.Sin(j*2.4f)*.38f,4.05f+j*1.03f,Mathf.Cos(j*2.4f)*.25f),new Vector3(w,1.48f,w*.88f),leaf*R(.88f,1.12f),false,j<3);
                }
            }
            else if (form == 4) // Cypress: one continuous, pointed silhouette.
            {
                mesh.Limb(fork,new Vector3(.12f,7.4f,-.08f),.16f,.025f,bark,lod);
                Crown(new Vector3(-.12f,4.9f,.08f),new Vector3(.94f,2.05f,.87f),leaf*.88f,false,false);
                Crown(new Vector3(.16f,6.5f,-.05f),new Vector3(.79f,1.9f,.72f),leaf,false,false);
                Crown(new Vector3(.05f,7.85f,.04f),new Vector3(.48f,1.43f,.43f),leaf*1.08f,false,false);
            }
            else if (form == 3) // Needle skirts have triangular tips and broken, sloping edges.
            {
                mesh.Limb(fork,new Vector3(.12f,8,-.08f),.17f,.025f,bark,lod);
                for(int j=0;j<4;j++)
                {
                    float w=2.55f-j*.53f;
                    Crown(new Vector3(j*.07f,3.65f+j*1.15f,-j*.06f),new Vector3(w,1.62f,w*.91f),leaf*(.84f+j*.075f),true,false);
                }
            }
            else if(form == 5)
            {
                Color flower=palette==0?new Color(.61f,.235f,.36f):palette==1?new Color(.73f,.50f,.105f):new Color(.40f,.235f,.51f);
                Crown(new Vector3(-.2f,6.15f,.05f),new Vector3(1.25f,1.25f,1.3f),flower*1.06f);
                for(int j=0;j<5;j++)
                {
                    float a=j*Mathf.PI*2/5;
                    Crown(new Vector3(Mathf.Cos(a)*1.6f,5.05f+R(-.3f,.35f),Mathf.Sin(a)*1.5f),new Vector3(1.5f,1.3f,1.4f),flower*R(.85f,1.08f));
                }
            }
            else if(form == 6) // Coastal crowns follow the wind, with an asymmetrical canopy.
            {
                mesh.Limb(fork,new Vector3(1.05f,4.6f,.12f),.17f,.11f,bark,lod);
                for(int j=0;j<5;j++)
                {
                    float a=j*Mathf.PI*2/5;
                    Crown(new Vector3(1.15f+Mathf.Cos(a)*1.65f,5.5f+R(-.4f,.55f),Mathf.Sin(a)*1.35f),new Vector3(1.7f,1.18f,1.45f),leaf*R(.85f,1.1f));
                }
                Crown(new Vector3(1.25f,6.45f,0),new Vector3(1.2f,.95f,1.2f),leaf*1.1f);
            }
            else
            {
                Crown(new Vector3(0,4.35f,.05f),new Vector3(1.05f,1.25f,1.05f),leaf*1.1f);
                for(int j=0;j<3;j++)
                {
                    float a=j*Mathf.PI*2/3;
                    Crown(new Vector3(Mathf.Cos(a)*.85f,3.7f+R(-.2f,.2f),Mathf.Sin(a)*.8f),new Vector3(1.05f,1.05f,1.05f),leaf*R(.85f,1.05f));
                }
            }
            return mesh.Finish(CityVegetation.Forms[form]+"-natural-"+palette+"-LOD"+lod);
        }

        sealed class Parts
        {
            readonly List<Vector3> points=new List<Vector3>();
            readonly List<Color> colors=new List<Color>();
            readonly List<int> triangles=new List<int>();
            void Face(Vector3 a,Vector3 b,Vector3 c,Color color)
            {
                int start=points.Count;points.Add(a);points.Add(b);points.Add(c);
                colors.Add(color);colors.Add(color);colors.Add(color);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }
            public void Limb(Vector3 start,Vector3 end,float bottom,float top,Color color,int lod)
            {
                int sides=lod==0?6:4;
                Quaternion q=Quaternion.FromToRotation(Vector3.up,end-start);
                var a=new Vector3[sides];var b=new Vector3[sides];
                for(int i=0;i<sides;i++)
                {
                    float angle=i*Mathf.PI*2/sides;
                    Vector3 radial=q*new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    a[i]=start+radial*bottom;b[i]=end+radial*top;
                }
                for(int i=0;i<sides;i++)
                {
                    int n=(i+1)%sides;Color tint=color*(.89f+.18f*(i%3)/2f);
                    Face(a[i],b[i],b[n],tint);Face(a[i],b[n],a[n],tint);
                    Face(start,a[i],a[n],color*.8f);Face(end,b[n],b[i],color);
                }
            }
            public void Crown(Vector3 center,Vector3 radius,Color color,int seed,int lod,bool cone)
            {
                int sides=lod==0?8:lod==1?6:4,rings=lod<2?3:2;
                var ring=new Vector3[rings,sides];
                float phase=(seed%41)*.153f;
                float Noise(float angle,float height)=>1+.085f*Mathf.Sin(angle*3+phase)+.045f*Mathf.Sin(angle*5-phase+height);
                for(int j=0;j<rings;j++)
                {
                    float y=rings==3?(j==0?-.62f:j==1?0:.62f):(j==0?-.48f:.48f);
                    float r=cone?(j==0?.91f:j==rings-1?.42f:1):(rings==3?(j==1?1:.7f):.84f);
                    for(int i=0;i<sides;i++)
                    {
                        float a=i*Mathf.PI*2/sides+phase*.1f;
                        float edge=Noise(a,j);
                        Vector3 p=new Vector3(Mathf.Cos(a)*r*edge,y+.055f*Mathf.Sin(a*3+phase),Mathf.Sin(a)*r*edge);
                        if(cone)p.y=j==0?-.63f:j==rings-1?.35f:-.1f;
                        ring[j,i]=center+Vector3.Scale(p,radius);
                    }
                }
                Vector3 bottom=center+Vector3.Scale(new Vector3(-.07f,-1,.035f),radius);
                Vector3 top=center+Vector3.Scale(new Vector3(.10f,1,-.06f),radius);
                for(int i=0;i<sides;i++)
                {
                    int n=(i+1)%sides;
                    Face(bottom,ring[0,i],ring[0,n],color*.70f);
                    Face(top,ring[rings-1,n],ring[rings-1,i],color*(1.03f+.065f*Mathf.Sin(i+phase)));
                    for(int j=0;j<rings-1;j++)
                    {
                        Color tint=color*(.81f+.14f*j+.07f*Mathf.Sin(i*2.1f+phase));
                        Face(ring[j,i],ring[j+1,i],ring[j+1,n],tint);
                        Face(ring[j,i],ring[j+1,n],ring[j,n],tint);
                    }
                }
            }
            public Mesh Finish(string name)
            {
                var mesh=new Mesh{name=name};mesh.SetVertices(points);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
                mesh.SetUVs(0,new List<Vector2>(new Vector2[points.Count]));mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            }
        }
    }
}
