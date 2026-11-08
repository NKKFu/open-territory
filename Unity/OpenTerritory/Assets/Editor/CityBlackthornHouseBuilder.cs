using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    // CLI fallback for long Editor builds that temporarily disconnect MCP.
    // Nothing runs without the explicit task-scoped build.request file.
    [InitializeOnLoad]
    static class CityBlackthornHouseBuildRequest
    {
        static double nextCheck;
        static CityBlackthornHouseBuildRequest() { EditorApplication.update += Check; }
        static void Check()
        {
            if(EditorApplication.timeSinceStartup<nextCheck)return;nextCheck=EditorApplication.timeSinceStartup+1;
            const string request="../Reports/blackthorn-lowpoly-shop/build.request";
            if(!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)return;
            var scene=EditorSceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/BaiaDoSol.unity" || scene.isDirty)return;
            if(File.GetLastWriteTimeUtc(typeof(CityBlackthornHouseBuilder).Assembly.Location)<File.GetLastWriteTimeUtc("Assets/Editor/CityBlackthornHouseBuilder.cs"))return;
            File.Move(request,"../Reports/blackthorn-lowpoly-shop/build-started-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".txt");
            try {CityBlackthornHouseBuilder.Rebuild();CityBlackthornHouseBuilder.BuildDelivery();}
            catch(Exception e){File.WriteAllText("../Reports/blackthorn-lowpoly-shop/build-last-error.txt",e.ToString());Debug.LogException(e);}
        }
    }

    public static class CityBlackthornHouseBuilder
    {
        public const string RootName = "Blackthorn House • occult residence";
        const string AssetPath = "Assets/Imported/BlackthornHouse.asset";
        const string ReportPath = "../Reports/blackthorn-lowpoly-shop";

        public static void BuildDelivery()
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!="Assets/Scenes/BaiaDoSol.unity" || scene.isDirty || !Object.FindFirstObjectByType<CityBlackthornHouse>())
                throw new InvalidOperationException("Build the saved, authored BaiaDoSol scene outside Play Mode.");
            var output=Path.GetFullPath("../Deliveries/BlackthornLowPolyShop-20261108/BaiaDoSol.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));Directory.CreateDirectory(ReportPath);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path},locationPathName=output,
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport});
            File.WriteAllText(ReportPath+"/build-complete.json",JsonUtility.ToJson(new BuildRecord{passed=report.summary.result==BuildResult.Succeeded,
                result=report.summary.result.ToString(),executable=output,completedAtUtc=DateTime.UtcNow.ToString("o"),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize},true));
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Blackthorn House full build failed: "+report.summary.result);
            Debug.Log("BLACKTHORN_FULL_BUILD_OK "+output);
        }
        [Serializable] sealed class BuildRecord {public bool passed;public string result,executable,completedAtUtc;public int errors,warnings;public ulong bytes;}

        [MenuItem("Open Territory/Build Blackthorn House at the pictured corner")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Open BaiaDoSol outside Play Mode to author Blackthorn House.");
            Directory.CreateDirectory(ReportPath);
            // Copy the exact open scene, including unrelated edits, before authoring.
            Directory.CreateDirectory("Assets/Scenes/Backups");
            if (!EditorSceneManager.SaveScene(scene, "Assets/Scenes/Backups/Before-blackthorn-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Could not preserve the current scene before authoring.");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var city = scene.GetRootGameObjects().First(g => g.name.StartsWith("Marin •")).transform;
            Build(city, world);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform city, WorldData world)
        {
            var place = world.places[CityBlackthornHouse.PlaceIndex];
            if (place.name != "#A615AD" || Vector2.Distance(new Vector2(place.x, place.z), new Vector2(947.0106f, 922.1479f)) > .05f)
                throw new InvalidOperationException("The pictured corner no longer matches the source lot.");
            var home = Object.FindFirstObjectByType<CityHomeCatalog>().homes.First(h => h.place == CityBlackthornHouse.PlaceIndex);
            if (!home.root) throw new InvalidOperationException("Preserve the existing house and its complete interior before adding the theme.");
            var previous = city.Find(RootName);
            if (previous)
                foreach (var item in previous.GetComponent<CityBlackthornHouse>().replacedInterior) if (item) item.SetActive(true);
            // The user authorized turning this house's interior into a shop.
            // Keep the original floor, exterior, garden and entrance untouched.
            var interior = home.root.Cast<Transform>().Where(t => t.name != "Base física nivelada").Select(t => t.gameObject).ToArray();
            var replacedColliders = new HashSet<int>(interior.SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Select(c => c.GetInstanceID()));
            Physics.SyncTransforms();
            var before = Snapshot(previous, replacedColliders);
            var origin = new Vector3(place.x, place.elevation, place.z);
            var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
            // Check the complete retail floor, including its side aisles.
            var blockers = Physics.OverlapBox(origin + rotation * new Vector3(0, 2.4f, 0), new Vector3(8.25f, 2.1f, 6.2f), rotation,
                1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)
                .Where(c => !replacedColliders.Contains(c.GetInstanceID()) && !(previous && c.transform.IsChildOf(previous))).ToArray();
            if (blockers.Length > 0) throw new InvalidOperationException("Shop floor occupied: " + string.Join(", ", blockers.Select(c => c.name)));
            if (previous) Object.DestroyImmediate(previous.gameObject);
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, AssetPath);
            var root = new GameObject(RootName); root.transform.SetParent(city, false);
            root.transform.SetPositionAndRotation(origin, rotation);
            var info = root.AddComponent<CityBlackthornHouse>();
            info.originalColliders = before.Count; info.originalCollisionHash = Hash(before.Values);
            info.replacedInterior = interior; info.replacedInteriorColliders = replacedColliders.Count;
            foreach (var item in interior) item.SetActive(false);
            info.entranceLeaf = Object.FindFirstObjectByType<WorldRegistry>().nodes.First(n => n.id == place.door.id).transform;
            new Author(root.transform, library, info).Build();
            Physics.SyncTransforms();
            var after = Snapshot(root.transform, replacedColliders);
            var changed = before.Where(p => !after.TryGetValue(p.Key, out var value) || value != p.Value).Select(p => p.Key).ToArray();
            if (changed.Length > 0 || after.Count != before.Count) throw new InvalidOperationException("An existing collider changed during authoring.");
            Directory.CreateDirectory(ReportPath);
            File.WriteAllText(ReportPath + "/placement.json", JsonUtility.ToJson(new Placement
            {
                passed = true, authoredAtUtc = DateTime.UtcNow.ToString("o"), sourcePlace = place.name,
                position = origin, yaw = rotation.eulerAngles.y, existingColliders = before.Count,
                preservedColliders = after.Count, collisionHashBefore = Hash(before.Values), collisionHashAfter = Hash(after.Values),
                additions = root.GetComponentsInChildren<Collider>().Length, candles = info.candleCount,
                authorizedInteriorColliders = replacedColliders.Count,
                note = "User-authorized open-plan shop interior. Original building shell, flat roof, doors, windows, garden, sidewalk and all unrelated collisions preserved."
            }, true));
            AssetDatabase.SaveAssets();
            Debug.Log("BLACKTHORN_BUILD_OK preserved=" + before.Count + " candles=" + info.candleCount);
        }

        [Serializable] sealed class Placement
        {
            public bool passed;
            public string authoredAtUtc, sourcePlace, collisionHashBefore, collisionHashAfter, note;
            public Vector3 position;
            public float yaw;
            public int existingColliders, preservedColliders, additions, candles, authorizedInteriorColliders;
        }
        static Dictionary<int, string> Snapshot(Transform except, HashSet<int> authorizedInterior)
        {
            return Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene.IsValid() && !authorizedInterior.Contains(c.GetInstanceID()) && !(except && c.transform.IsChildOf(except)))
                .ToDictionary(c => c.GetInstanceID(), c => EditorJsonUtility.ToJson(c) + "|" + c.transform.localToWorldMatrix.ToString("F6") + "|" + c.gameObject.activeInHierarchy + "|" + c.gameObject.layer);
        }
        static string Hash(IEnumerable<string> values)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", values.OrderBy(v => v, StringComparer.Ordinal))))).Replace("-", "");
        }

        sealed class Author
        {
            readonly Transform root;
            readonly Object library;
            readonly CityBlackthornHouse info;
            readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
            readonly List<Light> lights = new List<Light>();
            Mesh cube;
            Material stone, edge, roof, gold, violet, wax, fire, wood, pages, ink;
            public Author(Transform root, Object library, CityBlackthornHouse info) { this.root = root; this.library = library; this.info = info; }
            Material Mat(string name, Color c, Color? emission = null, float gloss = .12f)
            {
                var mat = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                mat.SetColor("_Color", c); mat.SetFloat("_Glossiness", gloss); mat.SetFloat("_Cull", 0);
                if (emission.HasValue) mat.SetColor("_EmissionColor", emission.Value);
                AssetDatabase.AddObjectToAsset(mat, library); return mat;
            }
            void Shape(Mesh mesh, Vector3 at, Vector3 size, Quaternion q, Material material)
            {
                if (!batches.TryGetValue(material, out var parts)) { parts = new List<CombineInstance>(); batches.Add(material, parts); }
                parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(at, q, size) });
            }
            void Box(string name, Vector3 at, Vector3 size, Material mat, bool solid = false, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity; Shape(cube, at, size, q, mat);
                if (!solid) return;
                var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = at; go.transform.localRotation = q;
                go.layer = CityContext.WorldLayer; go.isStatic = true; go.AddComponent<BoxCollider>().size = size;
            }
            Mesh Mesh(string name, Vector3[] vertices, int[] triangles)
            {
                var mesh = new Mesh { name = name }; mesh.vertices = triangles.Select(i => vertices[i]).ToArray(); mesh.triangles = Enumerable.Range(0,triangles.Length).ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh, library); return mesh;
            }
            void SolidMesh(string name, Mesh mesh, Material mat)
            {
                Shape(mesh, Vector3.zero, Vector3.one, Quaternion.identity, mat);
                var go = new GameObject(name); go.layer = CityContext.WorldLayer; go.isStatic = true; go.transform.SetParent(root, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            void Beam(Vector3 a, Vector3 b, float thickness, Material material)
            { Box("Inlay", (a + b) * .5f, new Vector3(thickness, (b - a).magnitude, thickness), material, rotation: Quaternion.FromToRotation(Vector3.up, b - a)); }
            void Ring(Vector3 center, float radius, float width, Material mat, bool upright = false)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
                    Vector3 Point(float angle) => center + (upright ? new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0) : new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle))) * radius;
                    Beam(Point(a), Point(b), width, mat);
                }
            }
            void Sigil(Vector3 center, float radius, bool upright)
            {
                Ring(center, radius, .035f, pages, upright);
                var points = Enumerable.Range(0, 5).Select(i =>
                {
                    float a = Mathf.PI * 2 * i / 5;
                    return center + (upright ? new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0) : new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a))) * radius * .78f;
                }).ToArray();
                for (int i = 0; i < 5; i++) Beam(points[i], points[(i + 2) % 5], .045f, pages);
            }
            void Candle(Vector3 p, float height = .3f)
            {
                Box("Candle saucer", p, new Vector3(.22f, .035f, .22f), gold);
                Box("Black candle", p + Vector3.up * (height * .5f), new Vector3(.11f, height, .11f), wax);
                Shape(Diamond(), p + Vector3.up * (height + .08f), new Vector3(.065f, .15f, .065f), Quaternion.identity, fire);
                info.candleCount++;
            }
            Mesh diamond;
            Mesh Diamond()
            {
                if (diamond) return diamond;
                diamond = Mesh("Faceted crystal", new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back },
                    new[] { 0,3,2,0,4,3,0,5,4,0,2,5,1,2,3,1,3,4,1,4,5,1,5,2 }); return diamond;
            }
            void Light(string name, Vector3 p, Color color, float range)
            {
                var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.localPosition = p;
                var lamp = go.AddComponent<Light>(); lamp.type = LightType.Point; lamp.color = color; lamp.range = range;
                lamp.intensity = 1.15f; lamp.shadows = LightShadows.None; lights.Add(lamp);
            }
            void Book(Vector3 p, float yaw, float size = 1)
            {
                var q = Quaternion.Euler(0, yaw, 0);
                Box("Grimoire cover", p, new Vector3(.70f, .06f, .53f) * size, wood, rotation: q);
                Box("Grimoire pages", p + Vector3.up * .045f * size, new Vector3(.63f, .045f, .47f) * size, pages, rotation: q);
                for (int i = 0; i < 5; i++)
                    Box("Handwritten marks", p + q * new Vector3(.06f, .071f * size, -.15f + i * .065f) * size, new Vector3(.33f, .008f, .013f) * size, ink, rotation: q);
            }
            void Sign(string text, Vector3 at, float width, float height, int size, Color color)
            {
                var go = new GameObject(text); go.transform.SetParent(root, false); go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.Euler(0, 180, 0); go.transform.localScale = Vector3.one * .01f;
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(width * 100, height * 100);
                var label = new GameObject("Lettering").AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                var rt = label.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text; label.fontSize = size;
                label.alignment = TextAnchor.MiddleCenter; label.color = color; label.raycastTarget = false;
            }
            public void Build()
            {
                var sample = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = sample.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(sample);
                stone = Mat("Blackthorn • muted sign", new Color(.28f,.26f,.31f));
                edge = Mat("Blackthorn • cream paint", new Color(.74f,.70f,.58f));
                roof = Mat("Blackthorn • faded plum cloth", new Color(.33f,.25f,.35f));
                gold = Mat("Blackthorn • dull ochre", new Color(.57f,.46f,.29f));
                violet = Mat("Blackthorn • dull amethyst", new Color(.43f,.34f,.49f));
                wax = Mat("Blackthorn • wax", new Color(.72f,.66f,.48f));
                fire = Mat("Blackthorn • candle flames", new Color(.97f,.63f,.28f), new Color(.35f,.16f,.035f));
                wood = Mat("Blackthorn • shop wood", new Color(.43f,.32f,.22f));
                pages = Mat("Blackthorn • chalk and paper", new Color(.82f,.77f,.63f));
                ink = Mat("Blackthorn • charcoal", new Color(.20f,.22f,.22f));
                Facades(); Interior(); Wizard();
                foreach (var batch in batches)
                {
                    var mesh = new Mesh { name = batch.Key.name, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(batch.Value.ToArray(), true, true); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(batch.Key.name); go.transform.SetParent(root, false); go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = batch.Key;
                }
                info.lights = lights.ToArray();
                info.streetApproach = root.TransformPoint(new Vector3(0,.1f,11.5f));
                var route = new[] {new Vector3(0,.24f,11.5f),new Vector3(0,.24f,8.5f),new Vector3(0,.24f,5.5f),new Vector3(0,.24f,3.1f),
                    new Vector3(3.1f,.24f,3.1f),new Vector3(3.1f,.24f,-3.1f),new Vector3(0,.24f,-3.1f),new Vector3(-3.1f,.24f,-3.1f),
                    new Vector3(-3.1f,.24f,3.1f),new Vector3(0,.24f,3.1f),new Vector3(0,.24f,0),new Vector3(0,.24f,3.1f),new Vector3(0,.24f,11.5f)};
                info.walkRoute = route.Select(root.TransformPoint).ToArray();
                var lod = root.gameObject.AddComponent<CityObjectLod>(); lod.category = "building"; lod.fullDistance = 85; lod.cullDistance = 650;
                lod.localBounds = new Bounds(new Vector3(0,2.7f,0), new Vector3(18.5f,5.7f,16));
                lod.parts = root.GetComponentsInChildren<MeshRenderer>().Where(r=>!r.transform.IsChildOf(info.wizard)).Select(r => new CityLodPart { renderer = r, filter = r.GetComponent<MeshFilter>(),
                    full = r.GetComponent<MeshFilter>().sharedMesh, medium = r.GetComponent<MeshFilter>().sharedMesh, far = r.GetComponent<MeshFilter>().sharedMesh, shadows = r.shadowCastingMode }).ToArray();
                var manager = Object.FindFirstObjectByType<CityLodManager>(); if (manager) manager.objects = manager.objects.Where(x => x).Concat(new[] {lod}).ToArray();
            }

            void Facades()
            {
                // Keep the original square stucco shell, flat roof, vents and
                // cream window frames. The shop only adds small painted details.
                foreach(int side in new[]{-1,1})
                {
                    float x=side*5.335f;
                    Box("Window shade",new Vector3(x,3.36f,7.25f),new Vector3(3.15f,.14f,.34f),roof);
                    foreach(int edgeSide in new[]{-1,1})
                        Box("Cloth curtain",new Vector3(x+edgeSide*1.10f,2.46f,6.77f),new Vector3(.35f,1.42f,.035f),roof);
                    var at=new Vector3(side*2.18f,2.85f,7.48f);
                    Box("Square porch lamp",at,new Vector3(.25f,.38f,.24f),wood);
                    Box("Warm lamp lens",at+Vector3.forward*.13f,new Vector3(.16f,.24f,.025f),fire);
                    Light("Porch lamp",at+Vector3.forward*.25f,new Color(1,.74f,.48f),5);
                }
                Box("Painted sign",new Vector3(0,4.55f,7.29f),new Vector3(4.45f,.87f,.09f),stone);
                foreach(int side in new[]{-1,1})
                    Box("Sign border",new Vector3(0,4.55f+side*.425f,7.35f),new Vector3(4.5f,.055f,.045f),edge);
                Sigil(new Vector3(-1.68f,4.55f,7.355f),.27f,true);
                Sign("BLACKTHORN",new Vector3(.27f,4.55f,7.36f),3.43f,.57f,30,new Color(.87f,.84f,.72f));
            }

            void Bottle(Vector3 p,int color)
            {
                var material=color%3==0?violet:color%3==1?roof:gold;
                Box("Potion bottle",p+Vector3.up*.14f,new Vector3(.16f,.27f,.16f),material);
                Box("Bottle neck",p+Vector3.up*.31f,new Vector3(.075f,.10f,.075f),material);
                Box("Cork",p+Vector3.up*.37f,new Vector3(.09f,.035f,.09f),wood);
                Box("Paper bottle label",p+new Vector3(0,.15f,.084f),new Vector3(.11f,.11f,.012f),pages);
            }
            void Shelf(Vector3 p,float width,float height,float yaw)
            {
                var q=Quaternion.Euler(0,yaw,0);
                void Part(string label,Vector3 at,Vector3 size,Material mat)=>Box(label,p+q*at,size,mat,rotation:q);
                Box("Shop shelf",p+Vector3.up*(height*.5f),new Vector3(width,height,.68f),wood,true,q);
                // Recessed dark back and three broad horizontal ledges.
                Part("Shelf back",new Vector3(0,height*.5f,.35f),new Vector3(width-.18f,height-.16f,.025f),ink);
                for(int row=0;row<3;row++)
                {
                    float y=.22f+row*(height-.28f)/3;
                    Part("Wooden ledge",new Vector3(0,y,.43f),new Vector3(width,.10f,.85f),wood);
                    for(int i=0;i<9;i++)
                    {
                        float x=-width*.43f+i*width*.105f;
                        if((i+row)%3==0)Bottle(p+q*new Vector3(x,y+.07f,.45f),i+row);
                        else Part("Bound books",new Vector3(x,y+.25f,.48f),new Vector3(.11f,.36f+(i%2)*.07f,.25f),(i+row)%2==0?roof:pages);
                    }
                }
                info.shelfCount++;
            }
            void Interior()
            {
                var floor=Mat("Blackthorn • plain wood floor",new Color(.59f,.48f,.35f));
                Box("Shop floor finish",new Vector3(0,.196f,0),new Vector3(17.35f,.012f,13.35f),floor);
                Box("Square ritual rug",new Vector3(0,.208f,0),new Vector3(4.75f,.01f,4.75f),roof);
                Sigil(new Vector3(0,.235f,0),2.05f,false);
                info.pentagramCenter=root.TransformPoint(new Vector3(0,.24f,0));
                info.counterApproach=root.TransformPoint(new Vector3(0,.24f,-3.1f));
                foreach(int side in new[]{-1,1})
                {
                    Shelf(new Vector3(side*5.25f,.20f,-6.25f),4.15f,2.35f,0);
                    Shelf(new Vector3(side*7.7f,.20f,2.8f),3.15f,1.65f,side<0?90:-90);
                    Shelf(new Vector3(side*7.7f,.20f,-1.1f),3.15f,1.65f,side<0?90:-90);
                    Box("Window display table",new Vector3(side*4.95f,.72f,4.35f),new Vector3(2.1f,1.02f,.84f),wood,true);
                    Box("Display table cloth",new Vector3(side*4.95f,1.25f,4.35f),new Vector3(2.2f,.04f,.92f),roof);
                    for(int i=0;i<3;i++)Bottle(new Vector3(side*4.95f+(i-1)*.38f,1.28f,4.35f),i);
                    Candle(new Vector3(side*4.95f-.77f,1.28f,4.35f),.21f);
                    Candle(new Vector3(side*4.95f+.77f,1.28f,4.35f),.28f);
                    Box("Square ceiling lamp",new Vector3(side*4.2f,4.84f,1),new Vector3(1.05f,.10f,.55f),edge);
                }
                Box("Shop counter",new Vector3(0,.73f,-4.45f),new Vector3(4.8f,1.06f,.93f),wood,true);
                Box("Countertop",new Vector3(0,1.29f,-4.45f),new Vector3(5.0f,.10f,1.05f),edge);
                Book(new Vector3(-.35f,1.38f,-4.34f),-8);
                foreach(int side in new[]{-1,1})
                {
                    Candle(new Vector3(side*.90f,1.36f,-4.55f),.27f);
                    Candle(new Vector3(side*1.15f,1.36f,-4.55f),.20f);
                }
                Box("Cash register",new Vector3(1.85f,1.48f,-4.4f),new Vector3(.58f,.28f,.52f),ink);
                Box("Register display",new Vector3(1.85f,1.72f,-4.55f),new Vector3(.45f,.23f,.13f),stone);
                Box("Register screen",new Vector3(1.85f,1.73f,-4.47f),new Vector3(.33f,.12f,.015f),edge);
                var go=new GameObject("Suspended amethyst");go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(-1.70f,1.73f,-4.4f);
                go.AddComponent<MeshFilter>().sharedMesh=Diamond();go.AddComponent<MeshRenderer>().sharedMaterial=violet;go.transform.localScale=new Vector3(.12f,.24f,.12f);info.crystal=go.transform;
                Light("Counter lamp",new Vector3(0,3.35f,-4.25f),new Color(1,.81f,.61f),9);
                Light("Shop ceiling lamp left",new Vector3(-4.2f,4.65f,1),new Color(1,.88f,.70f),10);
                Light("Shop ceiling lamp right",new Vector3(4.2f,4.65f,1),new Color(1,.88f,.70f),10);
            }
            void Wizard()
            {
                var registry=Object.FindFirstObjectByType<WorldRegistry>();
                var world=JsonUtility.FromJson<WorldData>(registry.manifest.text);
                var donor=world.residents.First(d=>d.name=="Davi" && !d.child && !d.sitting);
                var original=registry.nodes.First(n=>n.id==donor.id).transform;
                var wizard=Object.Instantiate(original.gameObject,root,false).transform;
                wizard.name="Elias • occult shopkeeper";wizard.localPosition=new Vector3(0,.22f,-5.55f);wizard.localRotation=Quaternion.identity;wizard.gameObject.SetActive(true);
                info.wizard=wizard;
                foreach(var collider in wizard.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                var sources=original.GetComponentsInChildren<Transform>(true);var copies=wizard.GetComponentsInChildren<Transform>(true);
                var mapping=sources.Select((t,i)=>new {t,copy=copies[i]}).ToDictionary(x=>x.t,x=>x.copy);
                var ids=new Dictionary<int,int>();var nodes=new List<NodeReference>();int next=-956000;
                foreach(var reference in registry.nodes)
                    if(reference.transform && mapping.TryGetValue(reference.transform,out var copy))
                    {ids.Add(reference.id,next);nodes.Add(new NodeReference{id=next--,transform=copy});}
                int Remap(int id)=>ids.TryGetValue(id,out var value)?value:0;
                var rig=JsonUtility.FromJson<RigData>(JsonUtility.ToJson(donor.rig));
                rig.root=Remap(rig.root);rig.hips=Remap(rig.hips);rig.chest=Remap(rig.chest);rig.head=Remap(rig.head);
                rig.limbs=rig.limbs.Select(Remap).ToArray();rig.knees=rig.knees.Select(Remap).ToArray();rig.elbows=rig.elbows.Select(Remap).ToArray();rig.hands=rig.hands.Select(Remap).ToArray();rig.ankles=rig.ankles.Select(Remap).ToArray();
                info.wizardNodes=nodes.ToArray();info.wizardData=new ResidentData{id=Remap(donor.id),name="Elias",role="shopkeeper",stationary=true,indoor=true,
                    position=wizard.position,place="#A615AD",rig=rig,phase=.4f,speed=.8f,line="Books, charms, and remedies. Mind the chalk.",
                    lines=new[]{"Books, charms, and remedies. Mind the chalk.","The old books are on the back shelves."}};
                var head=nodes.First(n=>n.id==rig.head).transform;var hips=nodes.First(n=>n.id==rig.hips).transform;
                foreach(var renderer in wizard.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string name=renderer.sharedMaterial.name;
                    if(name=="material-332" || name=="material-369")renderer.sharedMaterial=roof;
                    if(name=="material-345" || name=="material-343" || name=="material-269" || name=="material-344")renderer.sharedMaterial=ink;
                }
                var details=wizard.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="costume-details");if(details)details.gameObject.SetActive(false);
                void Part(Transform parent,string label,Vector3 at,Vector3 scale,Material mat,Mesh mesh=null)
                {
                    var g=new GameObject(label);g.transform.SetParent(parent,false);g.transform.localPosition=at;g.transform.localScale=scale;
                    g.AddComponent<MeshFilter>().sharedMesh=mesh?mesh:cube;g.AddComponent<MeshRenderer>().sharedMaterial=mat;
                }
                Part(hips,"Blocky robe",new Vector3(0,-.04f,0),new Vector3(1.04f,.60f,.59f),roof);
                Part(head,"Square hat brim",new Vector3(0,.67f,0),new Vector3(.81f,.075f,.70f),roof);
                var hat=Mesh("Four sided wizard hat",new[]{new Vector3(-.32f,.71f,-.28f),new Vector3(.32f,.71f,-.28f),new Vector3(.32f,.71f,.28f),new Vector3(-.32f,.71f,.28f),new Vector3(-.09f,1.18f,0)},new[]{0,4,1,1,4,2,2,4,3,3,4,0});
                Part(head,"Simple pointed hat",Vector3.zero,Vector3.one,roof,hat);
                Part(head,"Square gray beard",new Vector3(0,.015f,.285f),new Vector3(.27f,.27f,.13f),edge);
                Part(head,"Beard tip",new Vector3(0,-.15f,.28f),new Vector3(.16f,.12f,.12f),edge);
                var lod=wizard.GetComponent<CityObjectLod>();if(lod)Object.DestroyImmediate(lod);
            }
        }
    }
}
