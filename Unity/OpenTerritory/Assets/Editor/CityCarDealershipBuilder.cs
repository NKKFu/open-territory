using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    public static class CityCarDealershipBuilder
    {
        public const string RootName = "Sol Motors • car dealership";
        const string LibraryPath = "Assets/Imported/CarDealership.asset";
        static readonly int[] DisplayDonors = { 9476, 8908, 9088, 9283, 9668, 9850, 10241, 10045, 9476, 8908 };
        static readonly Vector3 Site = new Vector3(95, 0, 499);
        static readonly Quaternion Rotation = Quaternion.Euler(0, 349.677f, 0);

        // Replace only the ten car visual batches; keep the authored showroom,
        // parking, landscaping, access routes and every collider untouched.
        public static void RefreshDisplayCars(WorldData world, WorldRegistry registry)
        {
            var info = Object.FindFirstObjectByType<CityCarDealership>();
            var library = AssetDatabase.LoadMainAssetAtPath(LibraryPath);
            if (!info || !library || info.displayCars.Length != DisplayDonors.Length) return;
            var group = info.transform.Find("Shared static geometry by material and area");
            if (!group) return;
            for (int index = 0; index < DisplayDonors.Length; index++)
            {
                var data = world.vehicles.First(v => v.id == DisplayDonors[index]);
                var source = registry.nodes.First(n => n.id == data.id).transform;
                var driver = registry.nodes.FirstOrDefault(n => n.id == data.driver)?.transform;
                var rider = registry.nodes.FirstOrDefault(n => n.id == data.rider)?.transform;
                var batches = new Dictionary<Material, List<CombineInstance>>();
                var placement = info.transform.worldToLocalMatrix * info.displayCars[index].localToWorldMatrix;
                foreach (var f in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!f.sharedMesh || !f.gameObject.activeInHierarchy || driver && f.transform.IsChildOf(driver) || rider && f.transform.IsChildOf(rider)) continue;
                    var renderer = f.GetComponent<MeshRenderer>(); if (!renderer || !renderer.enabled) continue;
                    for (int i = 0; i < Mathf.Min(f.sharedMesh.subMeshCount, renderer.sharedMaterials.Length); i++)
                    {
                        var material = renderer.sharedMaterials[i];
                        if (!batches.TryGetValue(material, out var batch)) batches[material] = batch = new List<CombineInstance>();
                        batch.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = i, transform = placement * source.worldToLocalMatrix * f.transform.localToWorldMatrix });
                    }
                }
                string prefix = "Car " + index + " • ";
                foreach (var batch in batches)
                {
                    var existing = group.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name.StartsWith(prefix) && r.sharedMaterial == batch.Key);
                    if (!existing) throw new InvalidOperationException("Display car material batch missing: " + prefix + batch.Key.name);
                    var mesh = existing.GetComponent<MeshFilter>().sharedMesh;
                    mesh.Clear(); mesh.CombineMeshes(batch.Value.ToArray(), true, true); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                }
            }
        }

        public static bool Reserved(float x, float z, float margin = 0)
        {
            var p = Quaternion.Inverse(Rotation) * (new Vector3(x, 0, z) - Site);
            return Mathf.Abs(p.x) < 31 + margin && Mathf.Abs(p.z) < 30 + margin ||
                p.z > -43 - margin && p.z < -29 && (Mathf.Abs(p.x - 1) < 2.9f + margin || Mathf.Abs(p.x - 27) < 3.4f + margin);
        }

        [MenuItem("Open Territory/Construir loja de carros Sun Motors")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode para construir a loja.");
            Directory.CreateDirectory("../Reports/dealership/backups");
            string backup = "Before-dealership-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (scene.isDirty)
            {
                Directory.CreateDirectory("Assets/Scenes/Backups");
                EditorSceneManager.SaveScene(scene, "Assets/Scenes/Backups/" + backup, true);
            }
            else File.Copy(scene.path, "../Reports/dealership/backups/" + backup);
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var parent = scene.GetRootGameObjects().First(g => g.name.StartsWith("Marin •")).transform;
            Build(parent, world);
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            CityGroundCoverBuilder.Build(parent, visual, world);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform parent, WorldData world)
        {
            var previous = parent.Find(RootName);
            float height = -100;
            for (int x = -31; x <= 31; x += 2) for (int z = -30; z <= 30; z += 2)
            {
                var p = Site + Rotation * new Vector3(x, 0, z);
                height = Mathf.Max(height, CityGroundCoverBuilder.Height(world.ground, p.x, p.z));
            }
            height += .075f;
            ValidateFootprint(world, height, previous);
            if (previous)
            {
                RestoreNature(parent, world, previous.GetComponent<CityCarDealership>());
                Object.DestroyImmediate(previous.gameObject);
            }
            if (AssetDatabase.LoadAssetAtPath<Object>(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, LibraryPath);
            var root = new GameObject(RootName); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(Site.x, height, Site.z), Rotation);
            var info = root.AddComponent<CityCarDealership>();
            new Scenery(info, library, world).Build();
            ClearNature(parent, world, info);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(root.scene); AssetDatabase.SaveAssets();
            Debug.Log("CAR_DEALERSHIP_BUILD_OK cars=" + info.displayCars.Length + " position=" + root.transform.position);
        }

        static void ValidateFootprint(WorldData world, float height, Transform previous)
        {
            Physics.SyncTransforms();
            var natural = new HashSet<string>(world.natureTrees.Select(t => "Colisão-" + t.solidIndex));
            foreach (var rock in world.rocks) { natural.Add("Colisão-" + rock.solidIndex); natural.Add("Rocha • " + rock.solidIndex); }
            var blockers = new HashSet<string>();
            void Check(Vector3 p, Vector3 extents)
            {
                foreach (var c in Physics.OverlapBox(p, extents, Rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                    if (!(previous && c.transform.IsChildOf(previous)) && !natural.Contains(c.name)) blockers.Add(c.name);
            }
            Check(new Vector3(Site.x, height + 3, Site.z), new Vector3(31.5f, 3.2f, 30.5f));
            foreach (float x in new[] { 1f, 27f })
                Check(new Vector3(Site.x, height + 2, Site.z) + Rotation * new Vector3(x, 0, -36.5f), new Vector3(x == 1 ? 3 : 3.4f, 2, 6.5f));
            if (blockers.Count != 0) throw new InvalidOperationException("A loja sobrepõe construções/mobiliário: " + string.Join(", ", blockers));

            var layout = JsonUtility.FromJson<ReferenceLayout>(File.ReadAllText("Assets/Resources/reference-map.json"));
            // The complete paved lot must stay beyond the road and its public sidewalk.
            for (int x = -31; x <= 31; x += 2) for (int z = -30; z <= 30; z += 2)
            {
                var p = Site + Rotation * new Vector3(x, 0, z);
                foreach (var road in layout.roads) for (int i = 1; i < road.points.Length; i++)
                {
                    var a = CityPhysics.Flat(road.points[i - 1]); var ab = CityPhysics.Flat(road.points[i]) - a;
                    var nearest = a + ab * Mathf.Clamp01(Vector3.Dot(CityPhysics.Flat(p) - a, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                    if (Vector3.Distance(nearest, CityPhysics.Flat(p)) < road.width * .5f + 3.6f)
                        throw new InvalidOperationException("O pátio invade a rua/calçada " + road.id + " em " + p);
                }
                foreach (var place in world.places)
                    if (place.Contains(p, -1)) throw new InvalidOperationException("O lote invade " + place.name);
            }
        }

        static void RestoreNature(Transform parent, WorldData world, CityCarDealership previous)
        {
            if (!previous) return;
            var ids = new HashSet<int>(previous.reservedNaturalSolids);
            var forest = parent.Find("Árvores • 24 variações • LOD"); var physics = parent.Find("Colisões e relevo"); var rocks = parent.Find(CityRockBuilder.RootName);
            for (int i = 0; i < world.natureTrees.Length; i++)
            {
                var t = world.natureTrees[i]; if (!ids.Contains(t.solidIndex)) continue;
                var go = forest ? forest.Find(CityVegetation.Forms[t.variant % 8] + " • " + i) : null;
                if (go) go.gameObject.SetActive(!CityCemeteryBuilder.Reserved(t.position.x, t.position.z, 2));
            }
            foreach (var rock in world.rocks)
            {
                if (!ids.Contains(rock.solidIndex)) continue;
                var go = rocks ? rocks.Find("Rocha • " + rock.solidIndex) : null;
                if (go) go.gameObject.SetActive(!CityCemeteryBuilder.Reserved(rock.position.x, rock.position.z, Mathf.Max(rock.scale.x, rock.scale.z)));
            }
            foreach (int id in ids) { var go = physics ? physics.Find("Colisão-" + id) : null; if (go) go.gameObject.SetActive(true); }
        }

        static void ClearNature(Transform parent, WorldData world, CityCarDealership info)
        {
            var ids = new List<int>();
            var forest = parent.Find("Árvores • 24 variações • LOD"); var physics = parent.Find("Colisões e relevo"); var rocks = parent.Find(CityRockBuilder.RootName);
            for (int i = 0; i < world.natureTrees.Length; i++)
            {
                var t = world.natureTrees[i]; if (!Reserved(t.position.x, t.position.z, 3)) continue;
                var go = forest ? forest.Find(CityVegetation.Forms[t.variant % 8] + " • " + i) : null;
                if (go) go.gameObject.SetActive(false); ids.Add(t.solidIndex);
            }
            foreach (var rock in world.rocks)
            {
                if (!Reserved(rock.position.x, rock.position.z, Mathf.Max(rock.scale.x, rock.scale.z))) continue;
                var go = rocks ? rocks.Find("Rocha • " + rock.solidIndex) : null;
                if (go) go.gameObject.SetActive(false); ids.Add(rock.solidIndex);
            }
            foreach (int id in ids) { var go = physics ? physics.Find("Colisão-" + id) : null; if (go) go.gameObject.SetActive(false); }
            info.reservedNaturalSolids = ids.ToArray();
        }

        sealed class Scenery
        {
            readonly CityCarDealership info; readonly Transform root; readonly Object library; readonly WorldData world;
            readonly Dictionary<string, List<CombineInstance>> batches = new Dictionary<string, List<CombineInstance>>();
            readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
            readonly List<Transform> cars = new List<Transform>(); readonly List<Light> lights = new List<Light>();
            Mesh cube; Transform group; Material cream, teal, amber, dark, asphalt, paving, white, glass, timber, foliage, soil, light;
            public Scenery(CityCarDealership info, Object library, WorldData world)
            { this.info = info; root = info.transform; this.library = library; this.world = world; }
            Material Mat(string name, Color c, bool transparent = false)
            {
                var m = new Material(Shader.Find(transparent ? "OpenTerritory/CityGlass" : "OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                m.SetColor("_Color", c); m.SetFloat("_Glossiness", transparent ? .65f : .18f); AssetDatabase.AddObjectToAsset(m, library); return m;
            }
            Transform Group(string name) { group = new GameObject(name).transform; group.SetParent(root, false); return group; }
            void Shape(Mesh mesh, Matrix4x4 matrix, Material material, string sector)
            {
                string key = sector + " • " + material.GetInstanceID();
                if (!batches.TryGetValue(key, out var batch)) { batches[key] = batch = new List<CombineInstance>(); materials[key] = material; }
                batch.Add(new CombineInstance { mesh = mesh, transform = matrix });
            }
            GameObject Box(string name, Vector3 p, Vector3 size, Material material, bool solid = false, bool floor = false, Quaternion? rotation = null)
            {
                var q = rotation ?? Quaternion.identity;
                Shape(cube, Matrix4x4.TRS(p, q, size), material, Mathf.FloorToInt(p.x / 18) + "," + Mathf.FloorToInt(p.z / 18));
                if (!solid) return null;
                var go = new GameObject(name); go.transform.SetParent(group, false); go.transform.localPosition = p; go.transform.localRotation = q;
                go.layer = floor ? CityContext.GroundLayer : CityContext.WorldLayer; go.AddComponent<BoxCollider>().size = size; go.isStatic = true; return go;
            }
            void Label(string name, string text, Vector3 p, Vector2 size, int fontSize, Color color, Quaternion? rotation = null)
            {
                var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(group, false); go.transform.localPosition = p;
                go.transform.localRotation = rotation ?? Quaternion.identity; go.transform.localScale = Vector3.one * .01f;
                go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; ((RectTransform)go.transform).sizeDelta = size * 100;
                var label = new GameObject(name + " lettering", typeof(RectTransform)).AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = fontSize; label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter; label.color = color; label.text = text; label.raycastTarget = false;
            }
            void Ramp(float x, float width, string name)
            {
                var near = root.TransformPoint(new Vector3(x, 0, -42.4f));
                float h = Physics.Raycast(near + Vector3.up * 60, Vector3.down, out var hit, 120, 1 << CityContext.GroundLayer) ? hit.point.y - root.position.y + .015f : 0;
                var mesh = new Mesh { name = name };
                mesh.vertices = new[] { new Vector3(x - width * .5f, h, -42.4f), new Vector3(x - width * .5f, .012f, -29.8f), new Vector3(x + width * .5f, .012f, -29.8f), new Vector3(x + width * .5f, h, -42.4f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                Shape(mesh, Matrix4x4.identity, width < 6 ? paving : asphalt, "Access");
                var go = new GameObject(name); go.transform.SetParent(group, false); go.layer = CityContext.GroundLayer; go.isStatic = true; go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (width < 6) info.streetApproach = root.TransformPoint(new Vector3(x, h + .04f, -42.2f));
                else info.drivewayApproach = root.TransformPoint(new Vector3(x, h + .04f, -42.2f));
            }
            void Planter(float x, float z, float width = 3)
            {
                Box("Raised planter", new Vector3(x, .31f, z), new Vector3(width, .62f, 1.6f), cream, true);
                Box("Planter soil", new Vector3(x, .64f, z), new Vector3(width - .25f, .06f, 1.35f), soil);
                for (int i = 0; i < 4; i++)
                    Box("Low clipped foliage", new Vector3(x - width * .36f + i * width * .24f, .95f, z), new Vector3(width * .27f, .60f, 1.15f), foliage, rotation: Quaternion.Euler(0, i * 18, 0));
            }
            void Lamp(float x, float z, bool indoor)
            {
                if (!indoor) Box("Courtyard lamp column", new Vector3(x, 2.5f, z), new Vector3(.16f, 5, .16f), dark, true);
                Box("Warm light housing", new Vector3(x, indoor ? 5.44f : 5, z), new Vector3(1.2f, .12f, .5f), light);
                var go = new GameObject("Sol Motors warm lighting"); go.transform.SetParent(group, false); go.transform.localPosition = new Vector3(x, indoor ? 4.8f : 4.7f, z);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = indoor ? 18 : 15; l.intensity = 2.1f; l.color = new Color(1, .88f, .69f); l.shadows = LightShadows.None; lights.Add(l);
            }
            void DisplayCar(int donorId, float x, float z, float yaw, string model)
            {
                var registry = Object.FindFirstObjectByType<WorldRegistry>(); var data = world.vehicles.First(v => v.id == donorId);
                var source = registry.nodes.First(n => n.id == donorId).transform;
                CitySteeringWheelBuilder.EnsureColumnMesh();
                CitySteeringWheel.Fit(data, registry.nodes.First(n => n.id == data.steeringWheel).transform);
                var hidden = new List<Transform>(); foreach (int id in new[] { data.driver, data.rider })
                { var item = registry.nodes.FirstOrDefault(n => n.id == id); if (item != null && item.transform) hidden.Add(item.transform); }
                var filters = source.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.gameObject.activeInHierarchy && !hidden.Any(h => f.transform.IsChildOf(h))).ToArray();
                float bottom = 100;
                foreach (var f in filters) foreach (var v in f.sharedMesh.vertices)
                    bottom = Mathf.Min(bottom, (source.worldToLocalMatrix * f.transform.localToWorldMatrix).MultiplyPoint3x4(v).y);
                var q = Quaternion.Euler(0, yaw, 0); var placement = Matrix4x4.TRS(new Vector3(x, .045f - bottom, z), q, Vector3.one);
                foreach (var f in filters)
                {
                    var renderer = f.GetComponent<MeshRenderer>(); if (!renderer || !renderer.enabled) continue;
                    for (int i = 0; i < Mathf.Min(f.sharedMesh.subMeshCount, renderer.sharedMaterials.Length); i++)
                    {
                        var material = renderer.sharedMaterials[i]; string key = "Car " + cars.Count + " • " + material.GetInstanceID();
                        if (!batches.TryGetValue(key, out var batch)) { batches[key] = batch = new List<CombineInstance>(); materials[key] = material; }
                        batch.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = i, transform = placement * source.worldToLocalMatrix * f.transform.localToWorldMatrix });
                    }
                }
                var body = new GameObject("Display car " + (cars.Count + 1).ToString("00") + " • " + model); body.transform.SetParent(group, false);
                body.transform.localPosition = new Vector3(x, .045f - bottom, z); body.transform.localRotation = q; body.layer = CityContext.WorldLayer; body.isStatic = true;
                var collider = body.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .95f, data.centerOffset); collider.size = new Vector3(data.width, 1.55f, data.length);
                cars.Add(body.transform);
                // A small physical placard carries the model name, clear of the walkway.
                var plate = new Vector3(x - 2.1f, .58f, z - 2.6f);
                Box("Vehicle information pedestal", plate, new Vector3(.10f, 1.16f, .10f), dark, true);
                Box("Model placard", plate + Vector3.up * .61f, new Vector3(1.6f, .44f, .08f), teal);
                Label(model + " placard", model, plate + new Vector3(0, .61f, -.046f), new Vector2(1.5f, .35f), 19, Color.white);
            }
            void DoorLeaf(int index)
            {
                var node = new GameObject("Automatic glass door " + (index == 0 ? "left" : "right")).transform; node.SetParent(root, false);
                var pane = GameObject.CreatePrimitive(PrimitiveType.Cube); pane.name = "Safety glass"; pane.transform.SetParent(node, false);
                pane.transform.localPosition = new Vector3(0, 1.65f, 0); pane.transform.localScale = new Vector3(1.94f, 3.24f, .10f);
                pane.layer = CityContext.WorldLayer; pane.GetComponent<MeshRenderer>().sharedMaterial = glass;
                foreach (float y in new[] { .12f, 1.25f, 3.20f })
                {
                    var frame = GameObject.CreatePrimitive(PrimitiveType.Cube); frame.name = "Door safety rail"; frame.transform.SetParent(node, false);
                    frame.transform.localPosition = new Vector3(0, y, -.065f); frame.transform.localScale = new Vector3(1.94f, .065f, .045f);
                    frame.layer = CityContext.WorldLayer; frame.GetComponent<MeshRenderer>().sharedMaterial = teal;
                }
                info.doorLeaves[index] = node;
            }
            public void Build()
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = primitive.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(primitive);
                cream = Mat("Sol Motors • warm limestone", new Color(.86f, .83f, .73f)); teal = Mat("Sol Motors • deep coastal teal", new Color(.045f, .24f, .26f));
                amber = Mat("Sol Motors • amber accent", new Color(.94f, .47f, .12f)); dark = Mat("Sol Motors • charcoal steel", new Color(.09f, .12f, .13f));
                asphalt = Mat("Sol Motors • courtyard asphalt", new Color(.21f, .25f, .25f)); paving = Mat("Sol Motors • pale stone paving", new Color(.63f, .65f, .60f));
                white = Mat("Sol Motors • parking lines", new Color(.91f, .91f, .82f)); glass = Mat("Sol Motors • clear blue glazing", new Color(.47f, .75f, .79f, .16f), true);
                timber = Mat("Sol Motors • oak furniture", new Color(.48f, .29f, .14f)); foliage = Mat("Sol Motors • clipped shrubs", new Color(.20f, .34f, .16f));
                soil = Mat("Sol Motors • planter soil", new Color(.18f, .13f, .09f)); light = Mat("Sol Motors • warm light strips", new Color(.95f, .91f, .73f));
                light.SetColor("_EmissionColor", new Color(.55f, .47f, .28f));
                Group("Lot, continuous paths and parking");
                Box("Paved lot", new Vector3(0, -.16f, 0), new Vector3(62, .32f, 60), asphalt, true, true);
                Box("Showroom floor", new Vector3(-6, -.025f, 15), new Vector3(40, .08f, 28), paving, true, true);
                Box("Pedestrian route", new Vector3(1, .014f, -15), new Vector3(4.5f, .018f, 30), paving);
                Box("Front welcome terrace", new Vector3(-6, .018f, -2), new Vector3(40, .018f, 6), paving);
                Ramp(1, 5.2f, "Pedestrian connection to public sidewalk"); Ramp(27, 6.4f, "Vehicle connection to public sidewalk");
                foreach (int side in new[] { -1, 1 }) Box("Side boundary curb", new Vector3(side * 30.8f, .13f, 0), new Vector3(.24f, .26f, 60), cream, true);
                Box("Rear boundary curb", new Vector3(0, .13f, 29.8f), new Vector3(62, .26f, .24f), cream, true);
                foreach (float x in new[] { -28f, -19f, -10f, 8f, 15f })
                    Box("Visitor parking stripe", new Vector3(x, .013f, -25), new Vector3(.12f, .02f, 6), white);
                Box("Visitor parking back line", new Vector3(-6.5f, .014f, -28), new Vector3(43, .02f, .12f), white);
                foreach (float z in new[] { -20f, -8f, 4f, 16f, 28f }) Box("Stock parking stripe", new Vector3(23, .016f, z), new Vector3(8, .02f, .10f), white);
                foreach (float z in new[] { -19f, -11f })
                {
                    Box("Driveway arrow stem", new Vector3(27, .016f, z), new Vector3(.14f, .02f, 1.5f), white);
                    foreach (int side in new[] { -1, 1 }) Box("Driveway arrow", new Vector3(27 + side * .30f, .016f, z - .7f), new Vector3(.13f, .02f, .85f), white, rotation: Quaternion.Euler(0, side * 45, 0));
                }

                Group("Glass showroom, canopy and facade");
                Box("Rear showroom wall", new Vector3(-6, 2.9f, 29), new Vector3(40, 5.8f, .32f), cream, true);
                foreach (float x in new[] { -26f, 14f })
                {
                    Box("Window sill", new Vector3(x, .25f, 15), new Vector3(.30f, .5f, 28), cream, true);
                    Box("Side glazing", new Vector3(x, 2.75f, 15), new Vector3(.08f, 4.5f, 28), glass, true);
                    foreach (float z in new[] { 1f, 8f, 15f, 22f, 29f }) Box("Side window mullion", new Vector3(x, 2.8f, z), new Vector3(.22f, 5.6f, .22f), teal, true);
                }
                foreach (var panel in new[] { new Vector2(-13.5f, 25), new Vector2(8.5f, 11) })
                {
                    Box("Front glazing", new Vector3(panel.x, 2.7f, 1), new Vector3(panel.y, 5.3f, .08f), glass, true);
                    Box("Front window sill", new Vector3(panel.x, .17f, 1), new Vector3(panel.y, .34f, .30f), cream, true);
                }
                foreach (float x in new[] { -26f, -19.5f, -13f, -6.5f, -1.1f, 3.1f, 8.5f, 14f })
                    Box("Front window mullion", new Vector3(x, 2.8f, 1), new Vector3(.18f, 5.6f, .24f), teal, true);
                Box("Roof and canopy", new Vector3(-6, 5.77f, 12.3f), new Vector3(42.6f, .32f, 35.4f), cream, true);
                Box("Brand fascia", new Vector3(-6, 6.10f, -5.25f), new Vector3(42.6f, 1.15f, .32f), teal, true);
                Box("Amber fascia edge", new Vector3(-6, 5.49f, -5.29f), new Vector3(42.8f, .10f, .35f), amber);
                foreach (float x in new[] { -24f, 12f }) Box("Canopy column", new Vector3(x, 2.7f, -3.8f), new Vector3(.28f, 5.4f, .28f), teal, true);
                Label("Sol Motors main sign", "S U N   M O T O R S", new Vector3(-12, 6.13f, -5.43f), new Vector2(23, .87f), 67, Color.white);
                Label("Dealership descriptor", "NEW & PRE-OWNED", new Vector3(7, 6.13f, -5.43f), new Vector2(11, .65f), 33, new Color(.96f, .78f, .44f));
                Box("Entrance header", new Vector3(1, 3.48f, 1), new Vector3(4.2f, .32f, .26f), teal, true);
                Label("Welcome sign", "WELCOME", new Vector3(1, 3.49f, .85f), new Vector2(3.6f, .25f), 18, Color.white);
                info.doorLeaves = new Transform[2]; DoorLeaf(0); DoorLeaf(1); info.SetDoorPose(0);

                Group("Reception and customer lounge");
                Box("Sales desk", new Vector3(7, .65f, 23), new Vector3(5.4f, 1.3f, 1.6f), teal, true);
                Box("Oak desk top", new Vector3(7, 1.34f, 23), new Vector3(5.7f, .13f, 1.9f), timber, true);
                Box("Sales monitor", new Vector3(7.7f, 1.72f, 23.1f), new Vector3(.9f, .60f, .10f), dark, true);
                Box("Monitor display", new Vector3(7.7f, 1.72f, 23.04f), new Vector3(.8f, .5f, .02f), glass);
                Label("Reception desk sign", "SALES", new Vector3(7, .91f, 22.18f), new Vector2(2.5f, .40f), 27, Color.white);
                foreach (float x in new[] { 5.6f, 8.4f })
                {
                    Box("Customer chair seat", new Vector3(x, .55f, 20.5f), new Vector3(.88f, .16f, .88f), timber, true);
                    Box("Customer chair back", new Vector3(x, 1.01f, 20.05f), new Vector3(.88f, .83f, .11f), teal, true);
                    foreach (float side in new[] { -.30f, .30f }) Box("Chair leg", new Vector3(x + side, .24f, 20.5f), new Vector3(.10f, .48f, .65f), dark);
                }
                Box("Lounge sofa", new Vector3(10.7f, .42f, 9), new Vector3(2.2f, .84f, 3.6f), teal, true);
                Box("Lounge sofa back", new Vector3(11.6f, 1.12f, 9), new Vector3(.32f, .85f, 3.7f), teal, true);
                Box("Lounge table", new Vector3(7.4f, .46f, 9), new Vector3(1.2f, .92f, 2.1f), timber, true);
                Box("Back wall brand panel", new Vector3(-5, 3, 28.81f), new Vector3(15, 2.6f, .06f), teal);
                Label("Interior Sol Motors sign", "SUN MOTORS\nFind your next drive.", new Vector3(-5, 3.05f, 28.76f), new Vector2(14, 2.2f), 47, Color.white);

                Group("Ten vehicles on display");
                DisplayCar(9476, -19, 10, 180, "COMPACT"); DisplayCar(8908, -10, 10, 180, "SEDAN");
                DisplayCar(9088, -19, 23, 180, "ESTATE"); DisplayCar(9283, -10, 23, 180, "PICKUP");
                DisplayCar(9668, -21, -14, 180, "SEDAN"); DisplayCar(9850, -12, -14, 180, "ESTATE"); DisplayCar(10241, -3, -14, 180, "COMPACT");
                DisplayCar(10045, 24, 1, 90, "PICKUP"); DisplayCar(9476, 24, 13, 90, "COMPACT"); DisplayCar(8908, 24, 25, 90, "SEDAN");

                Group("Landscaping, roadside sign and lighting");
                Planter(-24, -4, 3); Planter(11, -4, 3); Planter(-29, 7, 2); Planter(-29, 23, 2); Planter(18, 28, 2);
                foreach (float x in new[] { -20f, -11f, 12f }) Planter(x, -29, x == 12 ? 5 : 7);
                Box("Roadside sign post", new Vector3(-26, 3.5f, -27), new Vector3(.30f, 7, .30f), teal, true);
                Box("Roadside brand sign", new Vector3(-26, 6, -27), new Vector3(4.8f, 2.6f, .3f), teal, true);
                Box("Roadside amber trim", new Vector3(-26, 4.63f, -27), new Vector3(5, .12f, .34f), amber);
                Label("Roadside name", "SUN\nMOTORS", new Vector3(-26, 6, -27.16f), new Vector2(4.4f, 2.3f), 48, Color.white);
                foreach (float x in new[] { -19f, -4f, 8f }) foreach (float z in new[] { 8f, 23f }) Lamp(x, z, true);
                Lamp(-27, -19, false); Lamp(29, -20, false); Lamp(29, 19, false);

                Group("Shared static geometry by material and area");
                foreach (var pair in batches)
                {
                    var mesh = new Mesh { name = pair.Key, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                    mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(pair.Key); go.transform.SetParent(group, false); go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = materials[pair.Key];
                }
                info.displayCars = cars.ToArray(); info.lights = lights.ToArray();
                Vector3 P(float x, float z) => root.TransformPoint(new Vector3(x, .07f, z));
                info.walkRoute = new[] { info.streetApproach, P(1, -30), P(1, -8), P(1, -3), P(1, 4), P(1, 17), P(-23, 17), P(-23, 4), P(1, 4), P(1, 18), P(7, 18), P(1, 18), P(1, -8), P(18, -8), P(18, 26.4f), P(18, -24), P(27, -29), info.drivewayApproach };
            }
        }
    }
}
