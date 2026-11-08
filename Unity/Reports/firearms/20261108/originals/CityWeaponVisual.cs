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
            Root.SetParent(hand,false); Root.localPosition = new Vector3(0,0,.05f);
            graphite=Finish("Aço azulado",new Color(.085f,.13f,.15f),.72f,.36f);
            steel=Finish("Metal escovado",new Color(.39f,.46f,.46f),.82f,.43f);
            rubber=Finish("Empunhadura",new Color(.045f,.065f,.067f),.02f,.18f);
            trim=Finish("Latão Farol",new Color(.64f,.39f,.15f),.65f,.34f);
            ivory=Finish("Cerâmica Maré",new Color(.68f,.78f,.73f),.12f,.3f);
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
            body.Box(graphite,new Vector3(0,.024f,.115f),new Vector3(.102f,.078f,.30f),.011f);
            Grip(body,new Vector3(0,-.082f,.002f),new Vector3(.093f,.198f,.097f)); Guard(body,.038f);
            body.Box(steel,new Vector3(-.055f,.048f,.015f),new Vector3(.011f,.012f,.062f),.003f);
            body.Box(trim,new Vector3(-.057f,.015f,.055f),new Vector3(.006f,.012f,.025f),.002f);
            body.Tube(steel,new Vector3(0,.081f,.24f),.027f,.155f,.017f);
            body.Tube(rubber,new Vector3(0,.081f,.304f),.017f,.006f);
            body.Box(steel,new Vector3(0,.016f,.23f),new Vector3(.043f,.018f,.09f),.004f);
            body.Bake(Root,this,"Armação P-9");
            Action=Marker("Ferrolho móvel",Vector3.zero); var slide=new Geometry();
            slide.Box(steel,new Vector3(0,.103f,.128f),new Vector3(.107f,.089f,.347f),.012f);
            slide.Box(graphite,new Vector3(.055f,.102f,.151f),new Vector3(.003f,.039f,.078f),.002f);
            slide.Box(trim,new Vector3(.058f,.093f,.146f),new Vector3(.004f,.014f,.045f),.002f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<6;i++) slide.Box(graphite,new Vector3(s*.054f,.102f,-.016f+i*.013f),new Vector3(.003f,.051f,.005f),.001f,new Vector3(15,0,0));
            Sights(slide,-.025f,.266f,.159f); slide.Bake(Action,this,"Ferrolho P-9");
            Magazine=Marker("Carregador P-9",new Vector3(0,-.183f,-.017f)); var mag=new Geometry();
            mag.Box(graphite,Vector3.zero,new Vector3(.101f,.022f,.115f),.005f); mag.Bake(Magazine,this,"Base do carregador");
            Muzzle=Marker("Boca P-9",new Vector3(0,.081f,.322f)); Ejection=Marker("Ejeção P-9",new Vector3(.065f,.115f,.149f));
        }
        void Carbine()
        {
            var g=new Geometry();
            g.Box(graphite,new Vector3(0,.056f,.125f),new Vector3(.119f,.15f,.34f),.012f);
            g.Box(steel,new Vector3(0,.103f,.325f),new Vector3(.11f,.104f,.26f),.014f);
            g.Box(graphite,new Vector3(0,.039f,.34f),new Vector3(.107f,.056f,.25f),.009f);
            Grip(g,new Vector3(0,-.10f,-.014f),new Vector3(.093f,.20f,.108f)); Guard(g,.036f);
            g.Box(graphite,new Vector3(0,.071f,-.155f),new Vector3(.072f,.077f,.23f),.009f);
            g.Box(ivory,new Vector3(0,.031f,-.277f),new Vector3(.106f,.164f,.17f),.015f,new Vector3(0,0,0));
            g.Box(rubber,new Vector3(0,.012f,-.371f),new Vector3(.113f,.185f,.029f),.009f);
            g.Box(rubber,new Vector3(0,.114f,-.263f),new Vector3(.096f,.028f,.19f),.007f);
            for(int s=-1;s<=1;s+=2)
            {
                for(int i=0;i<5;i++) g.Box(rubber,new Vector3(s*.057f,.107f,.23f+i*.042f),new Vector3(.004f,.037f,.022f),.003f);
                g.Box(ivory,new Vector3(s*.062f,.052f,.355f),new Vector3(.01f,.027f,.18f),.005f);
                g.Tube(steel,new Vector3(s*.062f,.037f,.06f),.009f,.004f,0,new Vector3(0,90,0),6);
            }
            g.Box(rubber,new Vector3(.063f,.078f,.119f),new Vector3(.006f,.038f,.093f),.004f);
            g.Tube(steel,new Vector3(0,.091f,.483f),.027f,.15f,.016f);
            g.Tube(graphite,new Vector3(0,.091f,.568f),.036f,.067f,.021f);
            g.Tube(rubber,new Vector3(0,.091f,.594f),.020f,.006f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<2;i++) g.Box(steel,new Vector3(s*.035f,.091f,.552f+i*.025f),new Vector3(.003f,.022f,.008f),.001f);
            g.Box(graphite,new Vector3(0,.164f,.17f),new Vector3(.067f,.023f,.48f),.005f);
            for(int i=0;i<19;i++) g.Box(steel,new Vector3(0,.180f,-.045f+i*.024f),new Vector3(.075f,.009f,.010f),.002f);
            Sights(g,-.025f,.395f,.208f);
            // Open reflex sight, with the view through its centre unobstructed.
            g.Box(graphite,new Vector3(0,.197f,.085f),new Vector3(.073f,.022f,.073f),.004f);
            for(int s=-1;s<=1;s+=2) g.Box(steel,new Vector3(s*.039f,.235f,.085f),new Vector3(.012f,.066f,.038f),.004f);
            g.Box(steel,new Vector3(0,.271f,.085f),new Vector3(.082f,.012f,.038f),.003f);
            g.Box(glow,new Vector3(0,.215f,.071f),new Vector3(.009f,.009f,.006f),.002f);
            g.Bake(Root,this,"Corpo C-12");
            Action=Marker("Ferrolho C-12",new Vector3(.067f,.08f,.116f));var bolt=new Geometry();
            bolt.Box(steel,Vector3.zero,new Vector3(.01f,.025f,.066f),.003f);bolt.Box(graphite,new Vector3(.015f,0,-.02f),new Vector3(.022f,.014f,.02f),.003f);bolt.Bake(Action,this,"Culatra C-12");
            Magazine=Marker("Carregador C-12",new Vector3(0,-.125f,.16f));var mag=new Geometry();
            mag.Box(steel,Vector3.zero,new Vector3(.078f,.236f,.124f),.009f,new Vector3(-9,0,0));
            mag.Box(rubber,new Vector3(0,-.119f,.019f),new Vector3(.088f,.024f,.135f),.005f);
            for(int s=-1;s<=1;s+=2) for(int i=0;i<3;i++) mag.Box(graphite,new Vector3(s*.040f,-.015f,-.036f+i*.036f),new Vector3(.004f,.17f,.009f),.002f,new Vector3(-9,0,0));
            mag.Bake(Magazine,this,"Carregador estriado");
            Muzzle=Marker("Boca C-12",new Vector3(0,.091f,.607f)); Ejection=Marker("Ejeção C-12",new Vector3(.08f,.086f,.13f));
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
