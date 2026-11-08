using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // Original, faceted geometry. Static details are baked by material, rather
    // than leaving a renderer and collider on every screw and rail tooth.
    public sealed class CityWeaponVisual
    {
        public Transform Root { get; private set; }
        public Transform Muzzle { get; private set; }
        public Transform Ejection { get; private set; }
        public Transform Action { get; private set; }
        public Transform Magazine { get; private set; }
        // Palm centres in weapon space. The model origin is the receiver, not
        // the wrist; every pose uses these same sockets, including recoil.
        public Vector3 RightGrip { get; private set; }
        public Vector3 SupportGrip { get; private set; }
        public Quaternion SupportRotation => id == "carbine" ? Quaternion.Euler(0, 0, 78) : Quaternion.identity;
        public int Triangles { get; private set; }
        public int RendererCount { get; private set; }
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        readonly string id;
        Material graphite, steel, rubber, trim, ivory, glow;
        Vector3 actionHome, magazineHome;
        float shotAt = -100;

        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<int> indices = new List<int>();
        }
        sealed class Geometry
        {
            readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
            void Face(Material material, Vector3 center, Quaternion rotation, params Vector3[] points)
            { FaceImpl(material,center,rotation,false,points); }
            void FaceImpl(Material material, Vector3 center, Quaternion rotation, bool inward, params Vector3[] points)
            {
                if (!batches.TryGetValue(material, out var batch)) batches[material] = batch = new Batch();
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                Vector3 midpoint = Vector3.zero; foreach (var p in points) midpoint += p;
                if ((Vector3.Dot(normal, midpoint / points.Length) < 0) != inward) { System.Array.Reverse(points); normal = -normal; }
                int start = batch.vertices.Count;
                foreach (var point in points) { batch.vertices.Add(center + rotation * point); batch.normals.Add(rotation * normal); }
                for (int i = 1; i < points.Length - 1; i++) { batch.indices.Add(start); batch.indices.Add(start + i); batch.indices.Add(start + i + 1); }
            }
            public void Box(Material material, Vector3 center, Vector3 size, float bevel = .006f, Vector3 euler = default)
            {
                var rotation = Quaternion.Euler(euler); Vector3 h = size * .5f;
                float b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * .45f);
                Vector3 inset = h - Vector3.one * b;
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 a = Vector3.zero, c = Vector3.zero, d = Vector3.zero, e = Vector3.zero;
                        a[axis] = c[axis] = d[axis] = e[axis] = h[axis] * sign;
                        a[u] = e[u] = -inset[u]; c[u] = d[u] = inset[u];
                        a[v] = c[v] = -inset[v]; d[v] = e[v] = inset[v];
                        Face(material, center, rotation, a, c, d, e);
                    }
                    // Four long chamfers around this axis.
                    for (int su = -1; su <= 1; su += 2) for (int sv = -1; sv <= 1; sv += 2)
                    {
                        Vector3 a = Vector3.zero, c = Vector3.zero, d = Vector3.zero, e = Vector3.zero;
                        a[axis] = c[axis] = -inset[axis]; d[axis] = e[axis] = inset[axis];
                        a[u] = e[u] = h[u] * su; c[u] = d[u] = inset[u] * su;
                        a[v] = e[v] = inset[v] * sv; c[v] = d[v] = h[v] * sv;
                        Face(material, center, rotation, a, c, d, e);
                    }
                }
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    Face(material, center, rotation, new Vector3(h.x*x,inset.y*y,inset.z*z), new Vector3(inset.x*x,h.y*y,inset.z*z), new Vector3(inset.x*x,inset.y*y,h.z*z));
            }
            public void Tube(Material material, Vector3 center, float radius, float length, float bore = 0, Vector3 euler = default, int sides = 8)
            {
                Quaternion rotation = Quaternion.Euler(euler);
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                    Vector3 p = new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,-length*.5f), q = new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,-length*.5f);
                    Vector3 r = new Vector3(q.x,q.y,length*.5f), s = new Vector3(p.x,p.y,length*.5f);
                    Face(material, center, rotation, p,q,r,s);
                    if (bore <= 0)
                    { Face(material,center,rotation,new Vector3(0,0,-length*.5f),q,p); Face(material,center,rotation,new Vector3(0,0,length*.5f),s,r); }
                    else
                    {
                        Vector3 ip = new Vector3(p.x*bore/radius,p.y*bore/radius,p.z), iq = new Vector3(q.x*bore/radius,q.y*bore/radius,q.z);
                        Vector3 ir = new Vector3(iq.x,iq.y,r.z), ist = new Vector3(ip.x,ip.y,s.z);
                        Face(material,center,rotation,p,ip,iq,q); Face(material,center,rotation,s,r,ir,ist);
                        // Inner wall normals point into the bore.
                        FaceImpl(material,center,rotation,true,ip,ist,ir,iq);
                    }
                }
            }
            public void Bake(Transform parent, CityWeaponVisual owner, string label)
            {
                foreach (var pair in batches)
                {
                    var mesh = new Mesh { name = label + " · " + pair.Key.name };
                    mesh.SetVertices(pair.Value.vertices); mesh.SetNormals(pair.Value.normals); mesh.SetTriangles(pair.Value.indices,0); mesh.RecalculateBounds();
                    var part = new GameObject(mesh.name); part.layer = CityContext.PlayerLayer; part.transform.SetParent(parent,false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pair.Key;
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    owner.meshes.Add(mesh); owner.Triangles += pair.Value.indices.Count / 3; owner.RendererCount++;
                }
            }
        }

        Material Finish(string name, Color color, float metallic, float smoothness)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color, enableInstancing = true };
            material.SetFloat("_Metallic",metallic); material.SetFloat("_Glossiness",smoothness); materials.Add(material); return material;
        }
        Transform Marker(string name, Vector3 position)
        { var marker = new GameObject(name).transform; marker.gameObject.layer = CityContext.PlayerLayer; marker.SetParent(Root,false); marker.localPosition=position; return marker; }
        public CityWeaponVisual(CityWeaponDefinition definition, Transform hand)
        {
            id=definition.Id; Root = new GameObject(definition.Name).transform; Root.gameObject.layer = CityContext.PlayerLayer;
            Root.SetParent(hand,false);
            RightGrip = new Vector3(0, -.071f, -.012f);
            SupportGrip = id == "carbine" ? new Vector3(-.035f, -.008f, .190f) : new Vector3(-.087f, -.071f, .016f);
            graphite=Finish("Parkerized steel",new Color(.068f,.078f,.088f),.55f,.27f);
            steel=Finish("Brushed gunmetal",new Color(.24f,.27f,.30f),.73f,.34f);
            rubber=Finish("Empunhadura",new Color(.045f,.065f,.067f),.02f,.18f);
            trim=Finish("Latão Farol",new Color(.64f,.39f,.15f),.65f,.34f);
            ivory=Finish(id=="carbine"?"Coyote composite":"Maré ceramic",id=="carbine"?new Color(.48f,.37f,.23f):new Color(.68f,.78f,.73f),.08f,.24f);
            glow=Finish("Carga turquesa",new Color(.08f,.57f,.55f),.18f,.42f);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor",new Color(.06f,.75f,.65f)*1.3f);
            if(id=="sidearm") Pistol(); else if(id=="carbine") Carbine(); else Pulsor();
            actionHome=Action.localPosition; magazineHome=Magazine.localPosition; Root.gameObject.SetActive(false);
        }
        void Grip(Geometry g, Vector3 position, Vector3 size)
        {
            g.Box(rubber,position,size,.01f,new Vector3(-13,0,0));
            for(int i=0;i<4;i++) g.Box(graphite,position+new Vector3(0,-.058f+i*.034f,-size.z*.48f),new Vector3(size.x*.86f,.009f,.008f),.002f);
            for(int s=-1;s<=1;s+=2) g.Tube(trim,position+new Vector3(s*(size.x*.5f+.002f),.025f,0),.008f,.004f,0,new Vector3(0,90,0),6);
        }
        void Guard(Geometry g, float z)
        {
            g.Box(graphite,new Vector3(0,-.044f,z+.06f),new Vector3(.057f,.018f,.13f),.004f);
            g.Box(graphite,new Vector3(0,-.01f,z+.12f),new Vector3(.057f,.08f,.017f),.004f);
            g.Box(steel,new Vector3(0,-.014f,z+.039f),new Vector3(.015f,.044f,.014f),.003f,new Vector3(-19,0,0));
        }
        void Sights(Geometry g, float rear, float front, float height)
        {
            for(int s=-1;s<=1;s+=2) { g.Box(graphite,new Vector3(s*.025f,height,rear),new Vector3(.018f,.024f,.025f),.003f); g.Box(ivory,new Vector3(s*.025f,height+.004f,rear-.014f),new Vector3(.007f,.007f,.003f),.001f); }
            g.Box(graphite,new Vector3(0,height,front),new Vector3(.014f,.026f,.028f),.003f);
            g.Box(glow,new Vector3(0,height+.006f,front-.016f),new Vector3(.006f,.009f,.004f),.001f);
        }
        void Pistol()
        {
            var body=new Geometry();
            body.Box(graphite,new Vector3(0,.016f,.105f),new Vector3(.085f,.062f,.269f),.009f);
            Grip(body,new Vector3(0,-.082f,-.002f),new Vector3(.080f,.177f,.094f)); Guard(body,.028f);
            body.Box(steel,new Vector3(-.046f,.033f,.010f),new Vector3(.009f,.013f,.055f),.003f);
            body.Box(trim,new Vector3(-.048f,.006f,.044f),new Vector3(.005f,.011f,.021f),.002f);
            body.Tube(steel,new Vector3(0,.078f,.228f),.021f,.116f,.012f);
            // A deep, open muzzle, visible through the cut-back slide nose.
            body.Tube(rubber,new Vector3(0,.078f,.268f),.012f,.007f);
            body.Box(graphite,new Vector3(0,-.019f,.204f),new Vector3(.048f,.019f,.068f),.004f);
            for(int i=0;i<3;i++)body.Box(steel,new Vector3(0,-.031f,.184f+i*.019f),new Vector3(.051f,.006f,.006f),.001f);
            body.Bake(Root,this,"Armação P-9");
            Action=Marker("Ferrolho móvel",Vector3.zero); var slide=new Geometry();
            slide.Box(steel,new Vector3(0,.092f,.109f),new Vector3(.091f,.077f,.291f),.012f);
            slide.Box(graphite,new Vector3(.047f,.096f,.124f),new Vector3(.004f,.037f,.067f),.002f);
            slide.Box(steel,new Vector3(.050f,.082f,.128f),new Vector3(.005f,.011f,.045f),.002f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<5;i++)
            {
                slide.Box(graphite,new Vector3(s*.046f,.094f,-.017f+i*.012f),new Vector3(.003f,.044f,.004f),.001f,new Vector3(17,0,0));
                slide.Box(graphite,new Vector3(s*.046f,.094f,.196f+i*.009f),new Vector3(.003f,.039f,.003f),.001f,new Vector3(17,0,0));
            }
            Sights(slide,-.020f,.228f,.142f); slide.Bake(Action,this,"Ferrolho P-9");
            Magazine=Marker("Carregador P-9",new Vector3(0,-.173f,-.020f)); var mag=new Geometry();
            mag.Box(graphite,Vector3.zero,new Vector3(.089f,.023f,.110f),.005f); mag.Bake(Magazine,this,"Base do carregador");
            Muzzle=Marker("Boca P-9",new Vector3(0,.078f,.289f)); Ejection=Marker("Ejeção P-9",new Vector3(.055f,.105f,.127f));
        }
        void Carbine()
        {
            var g=new Geometry();
            g.Box(ivory,new Vector3(0,.048f,.095f),new Vector3(.106f,.123f,.278f),.014f);
            g.Box(graphite,new Vector3(0,.108f,.103f),new Vector3(.098f,.055f,.29f),.009f);
            g.Box(ivory,new Vector3(0,.067f,.302f),new Vector3(.105f,.111f,.226f),.013f);
            g.Box(graphite,new Vector3(0,.008f,.30f),new Vector3(.098f,.024f,.211f),.006f);
            Grip(g,new Vector3(0,-.085f,-.012f),new Vector3(.083f,.181f,.098f)); Guard(g,.026f);
            g.Box(steel,new Vector3(0,.069f,-.083f),new Vector3(.055f,.056f,.110f),.007f);
            g.Box(ivory,new Vector3(0,.049f,-.130f),new Vector3(.097f,.134f,.100f),.016f);
            g.Box(rubber,new Vector3(0,.026f,-.184f),new Vector3(.104f,.153f,.022f),.007f);
            g.Box(rubber,new Vector3(0,.117f,-.124f),new Vector3(.087f,.025f,.100f),.006f);
            for(int s=-1;s<=1;s+=2)
            {
                for(int i=0;i<5;i++) g.Box(rubber,new Vector3(s*.054f,.073f,.225f+i*.035f),new Vector3(.004f,.024f,.019f),.003f);
                g.Box(graphite,new Vector3(s*.055f,.036f,.322f),new Vector3(.008f,.018f,.13f),.003f);
                g.Tube(steel,new Vector3(s*.055f,.035f,.046f),.008f,.004f,0,new Vector3(0,90,0),6);
            }
            g.Box(rubber,new Vector3(.056f,.078f,.115f),new Vector3(.004f,.034f,.080f),.003f);
            g.Tube(steel,new Vector3(0,.082f,.442f),.022f,.108f,.012f);
            g.Tube(graphite,new Vector3(0,.082f,.512f),.029f,.061f,.017f);
            g.Tube(rubber,new Vector3(0,.082f,.533f),.016f,.006f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<2;i++) g.Box(steel,new Vector3(s*.029f,.082f,.495f+i*.022f),new Vector3(.003f,.019f,.007f),.001f);
            g.Box(graphite,new Vector3(0,.143f,.175f),new Vector3(.065f,.019f,.445f),.004f);
            for(int i=0;i<17;i++) g.Box(steel,new Vector3(0,.155f,-.026f+i*.024f),new Vector3(.070f,.007f,.009f),.002f);
            Sights(g,-.023f,.373f,.181f);
            // Open reflex sight, with the view through its centre unobstructed.
            g.Box(graphite,new Vector3(0,.171f,.080f),new Vector3(.068f,.019f,.069f),.004f);
            for(int s=-1;s<=1;s+=2) g.Box(graphite,new Vector3(s*.035f,.204f,.080f),new Vector3(.010f,.052f,.032f),.004f);
            g.Box(graphite,new Vector3(0,.233f,.080f),new Vector3(.075f,.010f,.032f),.003f);
            g.Box(trim,new Vector3(0,.187f,.065f),new Vector3(.007f,.007f,.005f),.001f);
            g.Bake(Root,this,"Corpo C-12");
            Action=Marker("Ferrolho C-12",new Vector3(.067f,.08f,.116f));var bolt=new Geometry();
            bolt.Box(steel,Vector3.zero,new Vector3(.01f,.025f,.066f),.003f);bolt.Box(graphite,new Vector3(.015f,0,-.02f),new Vector3(.022f,.014f,.02f),.003f);bolt.Bake(Action,this,"Culatra C-12");
            Magazine=Marker("Carregador C-12",new Vector3(0,-.112f,.124f));var mag=new Geometry();
            mag.Box(graphite,Vector3.zero,new Vector3(.073f,.213f,.111f),.009f,new Vector3(-9,0,0));
            mag.Box(rubber,new Vector3(0,-.107f,.017f),new Vector3(.083f,.021f,.122f),.005f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<3;i++) mag.Box(steel,new Vector3(s*.038f,-.015f,-.032f+i*.032f),new Vector3(.003f,.15f,.007f),.002f,new Vector3(-9,0,0));
            mag.Bake(Magazine,this,"Carregador estriado");
            Muzzle=Marker("Boca C-12",new Vector3(0,.082f,.545f)); Ejection=Marker("Ejeção C-12",new Vector3(.069f,.086f,.13f));
        }
        void Pulsor()
        {
            var g=new Geometry();
            g.Box(graphite,new Vector3(0,.041f,.13f),new Vector3(.143f,.133f,.28f),.018f);
            g.Box(ivory,new Vector3(0,.09f,.093f),new Vector3(.157f,.102f,.23f),.018f);
            Grip(g,new Vector3(0,-.082f,.006f),new Vector3(.103f,.194f,.111f)); Guard(g,.043f);
            for(int s=-1;s<=1;s+=2)
            {
                g.Box(ivory,new Vector3(s*.064f,.055f,.253f),new Vector3(.04f,.093f,.151f),.008f);
                g.Tube(steel,new Vector3(s*.061f,.056f,.332f),.021f,.045f,.011f);
                g.Box(glow,new Vector3(s*.085f,.084f,.101f),new Vector3(.008f,.035f,.112f),.003f);
                for(int i=0;i<4;i++) g.Box(graphite,new Vector3(s*.079f,.043f,.035f+i*.027f),new Vector3(.006f,.015f,.012f),.002f);
            }
            g.Tube(graphite,new Vector3(0,.058f,.238f),.046f,.134f,.029f);
            for(int i=0;i<4;i++) g.Tube(glow,new Vector3(0,.058f,.202f+i*.027f),.048f,.008f,.035f);
            g.Box(graphite,new Vector3(0,.152f,.044f),new Vector3(.077f,.017f,.106f),.005f);
            for(int i=0;i<3;i++) g.Box(glow,new Vector3(-.023f+i*.023f,.162f,.043f),new Vector3(.014f,.006f,.057f),.002f);
            g.Box(trim,new Vector3(-.084f,.087f,-.001f),new Vector3(.006f,.021f,.027f),.002f);
            g.Bake(Root,this,"Carcaça e bobinas Maré");
            Action=Marker("Capacitor Maré",new Vector3(0,.056f,.29f));var cap=new Geometry();cap.Tube(glow,Vector3.zero,.025f,.025f);cap.Bake(Action,this,"Núcleo energizado");
            Magazine=Marker("Célula Maré",new Vector3(0,-.179f,-.004f));var cell=new Geometry();
            cell.Box(ivory,Vector3.zero,new Vector3(.111f,.039f,.129f),.007f);cell.Box(glow,new Vector3(0,-.021f,0),new Vector3(.074f,.005f,.083f),.002f);cell.Bake(Magazine,this,"Célula de carga");
            Muzzle=Marker("Emissor Maré",new Vector3(0,.056f,.356f)); Ejection=Marker("Respiro Maré",new Vector3(.08f,.06f,.14f));
        }
        public void Kick(float clock) { shotAt=clock; }
        public void Animate(float clock, bool reloading, float reloadProgress, int ammo)
        {
            float age=clock-shotAt, travel=age>=0?Mathf.Clamp01(1-age/.12f):0;
            float locked=ammo==0&&!reloading&&id=="sidearm"?.032f:0;
            Action.localPosition=actionHome+Vector3.back*(id=="pulse"?travel*.015f:Mathf.Max(locked,travel*(id=="carbine"?.05f:.046f)));
            float drop=reloading?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.3f,reloadProgress))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.58f,.82f,reloadProgress))):0;
            Magazine.localPosition=magazineHome+new Vector3(-.022f*drop,-.17f*drop,-.025f*drop);
            Magazine.localRotation=Quaternion.Euler(0,0,-14*drop);
            if(id=="pulse")glow.SetColor("_EmissionColor",new Color(.06f,.75f,.65f)*(reloading?Mathf.Lerp(.15f,1.3f,reloadProgress):Mathf.Lerp(1.3f,3.8f,Mathf.Exp(-Mathf.Max(0,age)*15))));
        }
        public void Dispose()
        { foreach(var mesh in meshes)Object.Destroy(mesh);foreach(var material in materials)Object.Destroy(material);if(Root)Object.Destroy(Root.gameObject); }
    }
}
