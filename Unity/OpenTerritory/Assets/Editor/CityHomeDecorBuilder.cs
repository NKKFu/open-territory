using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory.Editor
{
    public static class CityHomeDecorBuilder
    {
        const string AssetPath = "Assets/Imported/CityHomeDecor.asset", RootName = "Home decoration";
        static readonly Color Cream = new Color(.91f, .86f, .73f), Ink = new Color(.08f, .16f, .19f);
        static readonly Color[] Palette = { new Color(.19f,.43f,.42f), new Color(.68f,.30f,.20f), new Color(.24f,.36f,.55f),
            new Color(.69f,.51f,.20f), new Color(.43f,.31f,.49f), new Color(.33f,.47f,.29f), new Color(.69f,.40f,.40f), new Color(.26f,.49f,.59f) };
        static CityAssetLibrary library; static Material material; static Dictionary<string, Mesh> meshes;
        static readonly List<CityHomeStyleCatalog.Unit> units = new List<CityHomeStyleCatalog.Unit>();
        static int plants, rugs, paintings;

        [MenuItem("Open Territory/Decorar casas com combinações individuais")]
        public static void Bake()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de decorar.");
            var homes = UnityEngine.Object.FindFirstObjectByType<CityHomeCatalog>();
            if (!homes) throw new InvalidOperationException("Interiores ausentes.");
            library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>(AssetPath);
            if (!library) { library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, AssetPath); }
            meshes = AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Mesh>().ToDictionary(m => m.name);
            material = homes.homes[0].root.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            units.Clear(); plants = rugs = paintings = 0; Physics.SyncTransforms();
            foreach (var home in homes.homes.OrderBy(h => h.place))
            {
                var old = home.root.Find(RootName);
                if (old) Undo.DestroyObjectImmediate(old.gameObject);
                var root = new GameObject(RootName).transform; Undo.RegisterCreatedObjectUndo(root.gameObject, "Decorar casa"); root.SetParent(home.root, false);
                if (home.units == 1) Decorate(home, root, 0, false);
                else foreach (var flat in home.root.Cast<Transform>().Where(t => t.name.StartsWith("Apartamento ") && t.childCount > 0).OrderBy(t => t.name))
                {
                    int number = int.Parse(flat.name.Substring("Apartamento ".Length));
                    var unit = new GameObject("Decoration " + number).transform; unit.SetParent(root, false); unit.localPosition = flat.localPosition;
                    Decorate(home, unit, number, true);
                }
                var lod = home.root.GetComponent<CityObjectLod>();
                if (lod)
                {
                    Undo.RecordObject(lod, "Registrar decoração");
                    lod.parts = lod.parts.Where(p => p.filter && p.renderer).Concat(root.GetComponentsInChildren<MeshFilter>().Select(f => new CityLodPart {
                        filter = f, renderer = f.GetComponent<Renderer>(), full = f.sharedMesh, medium = f.sharedMesh, far = f.sharedMesh,
                        lastVisibleLevel = f.name.StartsWith("Rug") ? 1 : 0, shadows = ShadowCastingMode.On })).ToArray();
                }
            }
            var catalog = UnityEngine.Object.FindFirstObjectByType<CityHomeStyleCatalog>();
            if (!catalog) catalog = homes.gameObject.AddComponent<CityHomeStyleCatalog>();
            Undo.RecordObject(catalog, "Registrar estilos das casas"); catalog.units = units.ToArray();
            Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Directory.CreateDirectory("../Reports/home-style");
            File.WriteAllText("../Reports/home-style/decor.json", JsonUtility.ToJson(new Summary { units = units.Count, combinations = units.Select(u => u.combination).Distinct().Count(),
                plants = plants, rugs = rugs, paintings = paintings, sharedMeshes = meshes.Count }, true));
            Debug.Log("HOME_DECOR_BAKED units=" + units.Count + " combinations=" + units.Select(u => u.combination).Distinct().Count() + " plants=" + plants + " rugs=" + rugs + " paintings=" + paintings);
        }
        [Serializable] sealed class Summary { public int units, combinations, plants, rugs, paintings, sharedMeshes; }

        static void Decorate(CityHomeCatalog.Home home, Transform root, int number, bool compact)
        {
            // A permutation of all 240 dwellings gives each a distinct palette,
            // textile pattern and painting theme, without runtime randomness.
            int key = units.Count * 137 % 240, palette = key % 8, pattern = key / 8 % 6, art = key / 48;
            units.Add(new CityHomeStyleCatalog.Unit { place = home.place, unit = number, root = root, palette = palette, rugPattern = pattern, art = art,
                combination = palette + ":" + pattern + ":" + art });
            int side = compact && root.localPosition.x < 0 ? -1 : 1;
            if (compact)
            {
                Rug(root, new Vector3(-side * 1.2f, .025f, 3.8f), new Vector2(3.1f, 2.5f), palette, pattern);
                Rug(root, new Vector3(-side * .9f, .025f, -4.1f), new Vector2(2.5f, 2.2f), (palette + 3) % 8, (pattern + 2) % 6);
                Picture(root, new Vector3(0, 2.65f, -.59f), 0, palette, art);
                Picture(root, new Vector3(0, 2.65f, -.81f), 180, (palette + 2) % 8, (art + 1) % 5);
            }
            else
            {
                Rug(root, new Vector3(-5.05f, .205f, 3.2f), new Vector2(3.4f, 2.6f), palette, pattern);
                Rug(root, new Vector3(-5, .205f, -1.35f), new Vector2(3.1f, 1.05f), (palette + 3) % 8, (pattern + 2) % 6);
                Rug(root, new Vector3(5.15f, .205f, 4.7f), new Vector2(2.7f, .8f), (palette + 1) % 8, (pattern + 4) % 6);
                float x = Mathf.Abs(home.root.GetComponentsInChildren<Transform>().First(t => t.name == "Divisória entre os cômodos").localPosition.x);
                Picture(root, new Vector3(-x, 2.83f, .11f), 0, palette, art);
                Picture(root, new Vector3(-x + (key % 2 == 0 ? .4f : -.4f), 2.83f, -.11f), 180, (palette + 2) % 8, (art + 1) % 5);
                Picture(root, new Vector3(x, 2.83f, .11f), 0, (palette + 4) % 8, (art + 2) % 5);
            }
            var candidates = compact ? new[] { new Vector3(3.15f,0,6.35f), new Vector3(-3.15f,0,6.35f), new Vector3(3.15f,0,1.3f),
                new Vector3(-3.15f,0,1.3f), new Vector3(3.15f,0,-6.3f), new Vector3(-3.15f,0,-6.3f), new Vector3(1.2f,0,6.35f), new Vector3(-1.2f,0,6.35f) }
                : new[] { new Vector3(-7.8f,0,.85f),new Vector3(7.8f,0,.85f),new Vector3(-2.6f,0,5.6f),new Vector3(2.6f,0,-5.8f),
                    new Vector3(-7.8f,0,-5.8f),new Vector3(7.8f,0,-5.8f),new Vector3(-7.8f,0,5.8f),new Vector3(7.8f,0,4.1f) };
            int wanted = 2 + key % 2, placed = 0;
            for (int i = 0; i < candidates.Length && placed < wanted; i++)
            {
                Vector3 point = candidates[(i + key) % candidates.Length]; point.y = compact ? .02f : .20f;
                Vector3 world = root.TransformPoint(point);
                if (Physics.CheckBox(world + Vector3.up * .85f, new Vector3(.43f, .72f, .43f), root.rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)) continue;
                var shape = GetMesh("Plant " + palette + " " + ((key + placed) % 3), () => PlantMesh(palette, (key + placed) % 3));
                var t = Object(root, "Plant " + placed, shape, point, Vector3.one, (key * 97 + placed * 67) % 360);
                var box = t.gameObject.AddComponent<BoxCollider>(); box.center = new Vector3(0, .72f, 0); box.size = new Vector3(.64f, 1.44f, .64f);
                Destructible(t, "plant", home.place, box); plants++; placed++; Physics.SyncTransforms();
            }
            if (placed < 2) throw new InvalidOperationException("Sem espaço para plantas na unidade " + home.place + "/" + number);
            foreach (var prop in root.GetComponentsInChildren<CityDestructible>()) prop.place = home.place;
        }
        static Transform Object(Transform parent, string name, Mesh mesh, Vector3 at, Vector3 scale, float yaw = 0)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = at; t.localRotation = Quaternion.Euler(0, yaw, 0); t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material; t.gameObject.layer = CityContext.WorldLayer; return t;
        }
        static void Destructible(Transform t, string category, int place, Collider box)
        {
            var d = t.gameObject.AddComponent<CityDestructible>(); d.category = category; d.place = place; d.durability = 32;
            d.visuals = new[] { t.GetComponent<Renderer>() }; d.shapes = new[] { box };
        }
        static Mesh GetMesh(string name, Func<Geometry> create)
        { if (!meshes.TryGetValue(name, out var mesh)) { mesh = create().Save(name); meshes.Add(name, mesh); } return mesh; }
        static void Rug(Transform root, Vector3 point, Vector2 size, int palette, int pattern)
        {
            var mesh = GetMesh("Rug " + palette + " " + pattern, () => RugMesh(palette, pattern));
            Object(root, "Rug " + rugs, mesh, point, new Vector3(size.x, 1, size.y)); rugs++;
        }
        static void Picture(Transform root, Vector3 point, float yaw, int palette, int art)
        {
            var mesh = GetMesh("Painting " + palette + " " + art, () => PaintingMesh(palette, art));
            var t = Object(root, "Painting " + paintings, mesh, point, Vector3.one, yaw);
            var box = t.gameObject.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = mesh.bounds.size;
            Destructible(t, "painting", -1, box); paintings++;
        }
        static Geometry RugMesh(int palette, int pattern)
        {
            var g = new Geometry(); var c = Palette[palette];
            g.Box(Vector3.zero, new Vector3(1,.007f,1), Color.Lerp(c,Ink,.2f)); g.Box(new Vector3(0,.004f,0),new Vector3(.91f,.002f,.88f),c);
            if (pattern == 0 || pattern == 3) for(int i=-3;i<=3;i++) g.Box(new Vector3(i*.12f,.006f,0),new Vector3(.035f,.002f,.80f),i%2==0?Cream:Color.Lerp(c,Ink,.4f));
            if (pattern == 1 || pattern == 4) for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)if((x+z)%2==0)g.Box(new Vector3(x*.16f,.006f,z*.16f),new Vector3(.12f,.002f,.12f),Cream);
            if (pattern == 2 || pattern == 5) for(int i=0;i<4;i++) { float s=.64f-i*.14f;g.Box(new Vector3(0,.006f+i*.002f,0),new Vector3(s,.0015f,s),i%2==0?Cream:c); }
            if(pattern>=3)for(int z=-3;z<=3;z++)g.Box(new Vector3(0,.016f,z*.12f),new Vector3(.78f,.001f,.018f),Color.Lerp(c,Ink,.6f));
            return g;
        }
        static Geometry PaintingMesh(int palette, int art)
        {
            var g = new Geometry(); var c = Palette[palette]; var wood = Color.Lerp(c, new Color(.43f,.26f,.14f), .65f);
            g.Box(Vector3.zero,new Vector3(1.65f,1.10f,.06f),wood);g.Box(new Vector3(0,0,.035f),new Vector3(1.49f,.94f,.015f),Cream);
            int layer = 0;
            void Paint(float x,float y,float w,float h,Color color)=>g.Box(new Vector3(x,y,.052f + layer++ * .001f),new Vector3(w,h,.01f),color);
            if(art==0) { Paint(0,-.21f,1.46f,.40f,c);Paint(0,-.08f,1.46f,.12f,Color.Lerp(c,Cream,.45f));Paint(.35f,.22f,.24f,.24f,new Color(.90f,.64f,.24f));Paint(-.25f,-.21f,.45f,.05f,Cream); }
            else if(art==1) { for(int i=0;i<5;i++)Paint((i-2)*.26f,-.13f,.19f,.33f+(i%3)*.13f,Color.Lerp(c,Ink,i*.13f));Paint(0,-.36f,1.45f,.09f,c);Paint(-.42f,.26f,.19f,.19f,new Color(.87f,.61f,.28f)); }
            else if(art==2) { Paint(0,-.05f,.035f,.72f,Ink);for(int i=0;i<5;i++){float sign=i%2==0?-1:1;Paint(sign*.19f,-.29f+i*.13f,.38f,.09f,Color.Lerp(c,Ink,i*.08f));} }
            else if(art==3) { Paint(-.35f,.10f,.46f,.54f,c);Paint(.24f,-.14f,.60f,.37f,Color.Lerp(c,Ink,.5f));Paint(.30f,.25f,.25f,.20f,new Color(.85f,.62f,.27f)); }
            else { for(int i=0;i<6;i++)Paint((i-2.5f)*.22f,Mathf.Sin(i*1.6f)*.16f,.16f,.34f,Color.Lerp(c,Ink,(i%3)*.25f)); }
            return g;
        }
        static Geometry PlantMesh(int palette, int variant)
        {
            var g = new Geometry(); var pot = Color.Lerp(Palette[palette],Cream,.16f); var leaf = new Color(.19f,.39f,.26f);
            g.Box(new Vector3(0,.22f,0),new Vector3(.49f,.44f,.49f),pot);g.Box(new Vector3(0,.45f,0),new Vector3(.55f,.07f,.55f),Color.Lerp(pot,Ink,.22f));
            g.Box(new Vector3(0,.49f,0),new Vector3(.43f,.015f,.43f),new Color(.19f,.13f,.08f));
            int count=variant==0?7:variant==1?11:5;
            for(int i=0;i<count;i++)
            { float a=i*2.4f, y=.72f+i*(variant==1?.065f:.11f);var p=new Vector3(Mathf.Sin(a)*.18f,y,Mathf.Cos(a)*.18f);
                g.Bar(new Vector3(0,.5f,0),p,.028f,new Color(.32f,.28f,.13f));g.Box(p,new Vector3(variant==1?.17f:.38f,.07f,variant==2?.30f:.20f),Color.Lerp(leaf,Cream,(i%3)*.08f),Quaternion.Euler(variant*12,i*137,variant==0?25:-12)); }
            return g;
        }
        sealed class Geometry
        {
            readonly List<Vector3> points=new List<Vector3>(),normals=new List<Vector3>(); readonly List<Color> colors=new List<Color>(); readonly List<int> indices=new List<int>();
            public void Bar(Vector3 a,Vector3 b,float width,Color c)=>Box((a+b)*.5f,new Vector3(width,(b-a).magnitude,width),c,Quaternion.FromToRotation(Vector3.up,b-a));
            public void Box(Vector3 center,Vector3 size,Color c)=>Box(center,size,c,Quaternion.identity);
            public void Box(Vector3 center,Vector3 size,Color c,Quaternion rotation)
            {
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {var n=Vector3.zero;n[axis]=sign;var u=Vector3.zero;u[(axis+1)%3]=1;var v=Vector3.Cross(n,u);int first=points.Count;
                    foreach(var q in new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)})
                    {points.Add(center+rotation*Vector3.Scale(n*.5f+u*q.x+v*q.y,size));normals.Add(rotation*n);colors.Add(c);}indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});}
            }
            public Mesh Save(string name)
            {var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(points);m.SetNormals(normals);m.SetColors(colors);m.SetUVs(0,Enumerable.Repeat(Vector2.zero,points.Count).ToList());m.SetTriangles(indices,0);m.RecalculateBounds();AssetDatabase.AddObjectToAsset(m,library);return m;}
        }
    }
}
