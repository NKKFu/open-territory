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
    public static class CityCemeteryBuilder
    {
        public const string RootName = "Cemitério Jardim da Saudade";
        const string LibraryPath = "Assets/Imported/Cemetery.asset";
        static readonly Vector3 Site = new Vector3(126.112f, 0, 412.534f);
        const float Yaw = 169.677f;
        static readonly Quaternion Rotation = Quaternion.Euler(0, Yaw, 0);

        public static bool Reserved(float x, float z, float margin = 0)
        {
            var p = Quaternion.Inverse(Rotation) * (new Vector3(x, 0, z) - Site);
            return Mathf.Abs(p.x) < 22.5f + margin && Mathf.Abs(p.z) < 29.5f + margin ||
                Mathf.Abs(p.x) < 2.8f + margin && p.z > -39 - margin && p.z < -28;
        }

        [MenuItem("Open Territory/Construir cemitério Jardim da Saudade")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode para construir o cemitério.");
            // Preserve a reviewable copy of any unrelated unsaved edits.
            if (scene.isDirty)
            {
                Directory.CreateDirectory("Assets/Scenes/Backups");
                EditorSceneManager.SaveScene(scene, "Assets/Scenes/Backups/Before-cemetery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            }
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var parent = scene.GetRootGameObjects().First(g => g.name.StartsWith("Marin •")).transform;
            Build(parent, world);
            // Rebuild the existing soft vegetation with the new footprint reserved.
            // Its shared meshes remain generated offline, with the usual wind/LOD.
            var visual = JsonUtility.FromJson<VisualData>(File.ReadAllText("Assets/Imported/visual.json"));
            CityGroundCoverBuilder.Build(parent, visual, world);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(Transform parent, WorldData world)
        {
            var old = parent.Find(RootName);
            float height = -100;
            for (int x = -22; x <= 22; x += 4) for (int z = -29; z <= 29; z += 4)
            {
                var point = Site + Rotation * new Vector3(x, 0, z);
                height = Mathf.Max(height, CityGroundCoverBuilder.Height(world.ground, point.x, point.z));
            }
            ValidateFootprint(height, world, old);
            if (old)
            {
                RestoreNature(parent, world, old.GetComponent<CityCemetery>());
                Object.DestroyImmediate(old.gameObject);
            }
            if (AssetDatabase.LoadAssetAtPath<Object>(LibraryPath)) AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); library.sourceHash = world.sourceHash;
            AssetDatabase.CreateAsset(library, LibraryPath);
            var root = new GameObject(RootName); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(Site.x, height + .12f, Site.z), Rotation);
            var info = root.AddComponent<CityCemetery>();
            var builder = new Scenery(root.transform, library, info, world);
            builder.Build();
            ClearNature(parent, world, info);
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("CEMETERY_BUILD_OK graves=" + info.graveCount + " benches=" + info.seats.Length + " center=" + root.transform.position);
        }

        public static void RefreshNature(Transform parent, WorldData world)
        {
            var root = parent.Find(RootName);
            if (root && root.TryGetComponent<CityCemetery>(out var info)) ClearNature(parent, world, info);
        }

        static void ValidateFootprint(float height, WorldData world, Transform previous)
        {
            // Check actual native colliders too: donor gardens and street
            // furniture are not necessarily present in the buildings manifest.
            Physics.SyncTransforms();
            var natural = new HashSet<string>(world.natureTrees.Select(t => "Colisão-" + t.solidIndex));
            foreach (var rock in world.rocks) { natural.Add("Colisão-" + rock.solidIndex); natural.Add("Rocha • " + rock.solidIndex); }
            var blockers = new HashSet<string>();
            void Check(Vector3 center, Vector3 extents)
            {
                foreach (var hit in Physics.OverlapBox(center, extents, Rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore))
                {
                    if (previous && hit.transform.IsChildOf(previous) || natural.Contains(hit.name)) continue;
                    blockers.Add(hit.name);
                }
            }
            Check(new Vector3(Site.x, height + 2.7f, Site.z), new Vector3(23, 2.5f, 30));
            Check(new Vector3(Site.x, height + 2.7f, Site.z) + Rotation * new Vector3(0, 0, -33.8f), new Vector3(2.8f, 2.5f, 5.2f));
            if (blockers.Count > 0) throw new InvalidOperationException("O cemitério sobrepõe objetos existentes: " + string.Join(", ", blockers.Take(12)));
        }

        static void RestoreNature(Transform parent, WorldData world, CityCemetery previous)
        {
            if (!previous) return;
            var reserved = new HashSet<int>(previous.reservedNaturalSolids);
            var forest = parent.Find("Árvores • 24 variações • LOD");
            var physics = parent.Find("Colisões e relevo");
            var rocks = parent.Find(CityRockBuilder.RootName);
            for (int i = 0; i < world.natureTrees.Length; i++)
            {
                var tree = world.natureTrees[i]; if (!reserved.Contains(tree.solidIndex)) continue;
                var item = forest ? forest.Find(CityVegetation.Forms[tree.variant % 8] + " • " + i) : null;
                if (item) item.gameObject.SetActive(true);
            }
            foreach (var rock in world.rocks)
            {
                if (!reserved.Contains(rock.solidIndex)) continue;
                var item = rocks ? rocks.Find("Rocha • " + rock.solidIndex) : null;
                if (item) item.gameObject.SetActive(true);
            }
            foreach (int index in reserved)
            {
                var item = physics ? physics.Find("Colisão-" + index) : null;
                if (item) item.gameObject.SetActive(true);
            }
        }

        static void ClearNature(Transform parent, WorldData world, CityCemetery info)
        {
            var solids = new List<int>();
            var forest = parent.Find("Árvores • 24 variações • LOD");
            var physics = parent.Find("Colisões e relevo");
            var rocks = parent.Find(CityRockBuilder.RootName);
            for (int i = 0; i < world.natureTrees.Length; i++)
            {
                var tree = world.natureTrees[i];
                if (!Reserved(tree.position.x, tree.position.z, 2)) continue;
                if (forest)
                {
                    var item = forest.Find(CityVegetation.Forms[tree.variant % 8] + " • " + i);
                    if (item) item.gameObject.SetActive(false);
                }
                solids.Add(tree.solidIndex);
            }
            foreach (var rock in world.rocks)
            {
                if (!Reserved(rock.position.x, rock.position.z, Mathf.Max(rock.scale.x, rock.scale.z))) continue;
                var item = rocks ? rocks.Find("Rocha • " + rock.solidIndex) : null;
                if (item) item.gameObject.SetActive(false);
                solids.Add(rock.solidIndex);
            }
            foreach (int index in solids)
            {
                var item = physics ? physics.Find("Colisão-" + index) : null;
                if (item) item.gameObject.SetActive(false);
            }
            info.reservedNaturalSolids = solids.ToArray();
        }

        sealed class Scenery
        {
            readonly Transform root;
            readonly Object library;
            readonly CityCemetery info;
            readonly WorldData world;
            readonly Dictionary<string, List<CombineInstance>> batches = new Dictionary<string, List<CombineInstance>>();
            readonly Dictionary<string, Material> batchMaterials = new Dictionary<string, Material>();
            readonly List<SeatData> seats = new List<SeatData>();
            readonly List<Transform> seatNodes = new List<Transform>();
            Mesh cube;
            Transform group;
            Material plaster, coping, gravel, grass, soil, iron, wood, roof, bronze, flower, stem, lens;
            Material[] stone;

            public Scenery(Transform root, Object library, CityCemetery info, WorldData world)
            { this.root = root; this.library = library; this.info = info; this.world = world; }

            Material Mat(string name, Color color, float gloss = .08f)
            {
                var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                material.SetColor("_Color", color); material.SetFloat("_Glossiness", gloss);
                AssetDatabase.AddObjectToAsset(material, library); return material;
            }
            Transform Group(string name)
            { var go = new GameObject(name); go.transform.SetParent(root, false); group = go.transform; return group; }
            void Shape(Mesh mesh, Vector3 p, Vector3 scale, Quaternion rotation, Material material)
            {
                string key = material.name + ":" + Mathf.FloorToInt(p.x / 20) + ":" + Mathf.FloorToInt(p.z / 20);
                if (!batches.TryGetValue(key, out var batch)) { batch = new List<CombineInstance>(); batches.Add(key, batch); batchMaterials[key] = material; }
                batch.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(p, rotation, scale) });
            }
            GameObject Box(string name, Vector3 p, Vector3 scale, Material material, bool solid = false, Quaternion? rotation = null, bool floor = false)
            {
                var q = rotation ?? Quaternion.identity; Shape(cube, p, scale, q, material);
                if (!solid) return null;
                var go = new GameObject(name); go.transform.SetParent(group, false);
                go.transform.localPosition = p; go.transform.localRotation = q; go.layer = floor ? CityContext.GroundLayer : CityContext.WorldLayer;
                go.AddComponent<BoxCollider>().size = scale; go.isStatic = true; return go;
            }
            void Flowers(Vector3 p, int seed)
            {
                Box("Vaso", p + Vector3.up * .16f, new Vector3(.26f, .32f, .26f), roof);
                for (int j = 0; j < 3; j++)
                {
                    float x = (j - 1) * .11f, z = Mathf.Sin(seed + j * 2) * .10f, y = .45f + j * .05f;
                    Box("Haste", p + new Vector3(x, y - .13f, z), new Vector3(.025f, .32f, .025f), stem);
                    Box("Flores", p + new Vector3(x, y + .04f, z), new Vector3(.16f, .09f, .16f), flower, rotation: Quaternion.Euler(0, seed * 31 + j * 40, 0));
                }
            }
            void Bench(float x, float z, float yaw, bool pew = false)
            {
                int id = -91000 - seats.Count;
                var position = new Vector3(x, .025f, z); var rotation = Quaternion.Euler(0, yaw, 0);
                float width = pew ? 2.2f : 2.6f;
                var node = new GameObject(pew ? "Banco da capela" : "Banco do jardim").transform; node.SetParent(group, false);
                node.localPosition = position; node.localRotation = rotation;
                void Part(string name, Vector3 offset, Vector3 size, Material material, bool solid = true)
                { Box(name, position + rotation * offset, size, material, solid, rotation); }
                Part("Assento", new Vector3(0, .68f, 0), new Vector3(width, .14f, .62f), wood);
                Part("Encosto", new Vector3(0, 1.04f, -.30f), new Vector3(width, .48f, .10f), wood);
                for (int side = -1; side <= 1; side += 2)
                {
                    Part("Pé do banco", new Vector3(side * (width * .5f - .22f), .31f, 0), new Vector3(.16f, .62f, .55f), iron);
                    if (!pew) Part("Braço do banco", new Vector3(side * (width * .5f - .05f), .87f, 0), new Vector3(.10f, .12f, .65f), iron);
                }
                var point = root.TransformPoint(position);
                seats.Add(new SeatData { id = id, x = point.x, z = point.z, position = point, yaw = (Yaw + yaw) * Mathf.Deg2Rad, seatTop = .75f, width = width, depth = .62f });
                seatNodes.Add(node);
            }
            void Lamp(float x, float z)
            {
                Box("Base da luminária", new Vector3(x, .10f, z), new Vector3(.46f, .20f, .46f), coping, true);
                Box("Poste", new Vector3(x, 1.55f, z), new Vector3(.12f, 2.9f, .12f), iron, true);
                Box("Lanterna", new Vector3(x, 3.04f, z), new Vector3(.40f, .56f, .40f), lens);
                Box("Chapéu da lanterna", new Vector3(x, 3.35f, z), new Vector3(.57f, .10f, .57f), iron);
                var go = new GameObject("Luz quente do cemitério"); go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3(x, 3.04f, z);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 11; light.intensity = 1.6f;
                light.color = new Color(1, .77f, .47f); light.shadows = LightShadows.None;
            }
            void Wall(Vector3 p, Vector3 size)
            {
                Box("Muro de alvenaria", p + Vector3.up * .66f, new Vector3(size.x, 1.32f, size.z), plaster, true);
                Box("Coroamento do muro", p + Vector3.up * 1.37f, new Vector3(size.x + .12f, .14f, size.z + .12f), coping, true);
            }
            void Sign()
            {
                Box("Placa do portão", new Vector3(0, 3.92f, -29.07f), new Vector3(8.2f, 1.08f, .20f), iron, true);
                Box("Friso da placa", new Vector3(0, 4.49f, -29.07f), new Vector3(8.45f, .08f, .25f), bronze);
                var go = new GameObject("Nome do cemitério", typeof(RectTransform)); go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3(0, 3.93f, -29.19f); go.transform.localScale = Vector3.one * .008f;
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(1000, 118);
                var label = new GameObject("Jardim da Saudade", typeof(RectTransform)).AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 42;
                label.alignment = TextAnchor.MiddleCenter; label.color = new Color(.94f, .87f, .67f); label.raycastTarget = false;
                label.text = "C E M I T É R I O\nJARDIM DA SAUDADE";
            }
            void Access()
            {
                var start = root.TransformPoint(new Vector3(0, 0, -38.6f)); RaycastHit hit;
                float y = Physics.Raycast(start + Vector3.up * 100, Vector3.down, out hit, 200, 1 << CityContext.GroundLayer) ? hit.point.y : root.position.y;
                float localY = y - root.position.y + .01f;
                var vertices = new[] { new Vector3(-2.15f, localY, -38.6f), new Vector3(-2.15f, .025f, -28.8f), new Vector3(2.15f, .025f, -28.8f), new Vector3(2.15f, localY, -38.6f) };
                var mesh = new Mesh { name = "Rampa contínua entre calçada e portão" }; mesh.vertices = vertices;
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.uv = new[] { Vector2.zero, Vector2.up * 6.8f, new Vector2(4.3f, 6.8f), Vector2.right * 4.3f };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                Shape(mesh, Vector3.zero, Vector3.one, Quaternion.identity, gravel);
                var go = new GameObject("Acesso contínuo da calçada"); go.transform.SetParent(group, false); go.layer = CityContext.GroundLayer;
                go.AddComponent<MeshCollider>().sharedMesh = mesh; go.isStatic = true;
                info.streetApproach = root.TransformPoint(new Vector3(0, localY + .04f, -38.4f));
            }
            public void Build()
            {
                var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = temporary.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(temporary);
                plaster = Mat("Cal branca envelhecida", new Color(.77f, .76f, .65f)); coping = Mat("Pedra dos muros", new Color(.43f, .44f, .38f));
                gravel = Mat("Alamedas de cascalho claro", new Color(.66f, .59f, .43f)); grass = Mat("Jardim aparado", new Color(.25f, .34f, .17f));
                soil = Mat("Terra dos canteiros", new Color(.22f, .16f, .10f)); iron = Mat("Ferro verde escuro", new Color(.055f, .105f, .09f), .22f);
                wood = Mat("Bancos de madeira", new Color(.28f, .16f, .075f)); roof = Mat("Terracota da capela", new Color(.43f, .19f, .105f));
                bronze = Mat("Bronze fosco", new Color(.51f, .37f, .14f), .28f); flower = Mat("Flores claras", new Color(.87f, .73f, .64f));
                stem = Mat("Folhas do jardim", new Color(.12f, .23f, .095f)); lens = Mat("Vidro âmbar das lanternas", new Color(.85f, .66f, .38f));
                lens.SetColor("_EmissionColor", new Color(.17f, .09f, .025f));
                stone = new[] { Mat("Calcário claro", new Color(.69f, .68f, .59f)), Mat("Granito cinza", new Color(.38f, .42f, .39f)), Mat("Pedra antiga", new Color(.51f, .49f, .39f)), Mat("Lápide marfim", new Color(.82f, .81f, .72f)) };

                Group("Terreno e alamedas");
                Box("Piso do jardim", new Vector3(0, -.13f, 0), new Vector3(44, .22f, 58), grass, true, floor: true);
                Box("Alameda central", new Vector3(0, -.025f, -4.6f), new Vector3(4.2f, .10f, 49), gravel, true, floor: true);
                Box("Alameda transversal", new Vector3(0, -.025f, -1.5f), new Vector3(40, .10f, 2.4f), gravel, true, floor: true);
                foreach (float x in new[] { -18.2f, 18.2f }) Box("Passeio lateral", new Vector3(x, -.025f, 0), new Vector3(1.8f, .10f, 52), gravel, true, floor: true);
                foreach (float z in new[] { -25.2f, 27f }) Box("Passeio de contorno", new Vector3(0, -.025f, z), new Vector3(38.2f, .10f, 2), gravel, true, floor: true);
                Access();

                Group("Muros, portão aberto e placa");
                foreach (float x in new[] { -21.8f, 21.8f }) Wall(new Vector3(x, 0, 0), new Vector3(.44f, 0, 58));
                Wall(new Vector3(0, 0, 28.8f), new Vector3(44, 0, .44f));
                foreach (int side in new[] { -1, 1 })
                {
                    Wall(new Vector3(side * 13.2f, 0, -28.8f), new Vector3(17.6f, 0, .44f));
                    Box("Pilar do portão", new Vector3(side * 4.4f, 1.75f, -28.9f), new Vector3(.88f, 3.5f, .88f), plaster, true);
                    Box("Capitel", new Vector3(side * 4.4f, 3.55f, -28.9f), new Vector3(1.05f, .22f, 1.05f), coping, true);
                    // Both iron leaves stand fully open, outside the walking channel.
                    foreach (float y in new[] { .32f, 2.55f }) Box("Travessa do portão aberto", new Vector3(side * 4.01f, y, -27.1f), new Vector3(.10f, .11f, 3.45f), iron, true);
                    for (int j = 0; j < 9; j++) Box("Grade do portão aberto", new Vector3(side * 4.01f, 1.45f, -28.6f + j * .38f), new Vector3(.07f, 2.4f, .07f), iron, true);
                }
                Sign();

                Group("48 sepulturas e flores");
                int serial = 0;
                foreach (float z in new[] { -20.2f, -15.6f, -11f, -6.4f, 3.2f, 7.8f, 12.4f, 17f })
                    foreach (float x in new[] { -14.8f, -10.4f, -6f, 6f, 10.4f, 14.8f })
                    {
                        int n = serial++; Material material = stone[n % stone.Length]; string name = "Sepultura " + (n + 1).ToString("00");
                        Box(name + " • base", new Vector3(x, .10f, z), new Vector3(1.7f, .24f, 2.8f), coping, true);
                        Box(name + " • laje", new Vector3(x, .25f, z), new Vector3(1.52f, .14f, 2.58f), material, true);
                        if (n % 4 == 0)
                        {
                            Box(name + " • cruz", new Vector3(x, 1.14f, z + 1.02f), new Vector3(.19f, 1.7f, .22f), material, true);
                            Box(name + " • braços", new Vector3(x, 1.49f, z + 1.02f), new Vector3(.92f, .18f, .22f), material, true);
                        }
                        else
                        {
                            float h = n % 4 == 1 ? 1.12f : n % 4 == 2 ? .86f : 1.37f;
                            Box(name + " • lápide", new Vector3(x, .30f + h * .5f, z + 1.04f), new Vector3(1.03f, h, .22f), material, true);
                            if (n % 4 == 3) Box("Coroa da lápide", new Vector3(x, h + .34f, z + 1.04f), new Vector3(.76f, .10f, .26f), material);
                            Box("Inscrição discreta", new Vector3(x, .82f, z + .918f), new Vector3(.43f, .022f, .012f), bronze);
                            Box("Inscrição discreta", new Vector3(x, .72f, z + .918f), new Vector3(.30f, .018f, .012f), bronze);
                        }
                        if (n % 3 != 1) Flowers(new Vector3(x + .43f, .33f, z - .65f), n);
                    }
                info.graveCount = serial;

                Group("Jardim, bancos e luminárias");
                var treeMaterial = Mat("Ciprestes • cores por vértice", Color.white); treeMaterial.SetFloat("_Foliage", 1);
                var treeMeshes = new Mesh[3];
                for (int l = 0; l < 3; l++) { treeMeshes[l] = CityTreeModels.Create(20, l); AssetDatabase.AddObjectToAsset(treeMeshes[l], library); }
                foreach (float x in new[] { -20f, 20f }) foreach (float z in new[] { -21f, -8f, 8f, 22f })
                {
                    var tree = new GameObject("Cipreste do cemitério").transform; tree.SetParent(group, false);
                    tree.localPosition = new Vector3(x, -.02f, z); tree.localRotation = Quaternion.Euler(0, z * 13, 0);
                    tree.localScale = new Vector3(.78f, .82f + .035f * Mathf.Sin(z), .78f);
                    var levels = new LOD[3];
                    for (int l = 0; l < 3; l++)
                    {
                        var part = new GameObject("LOD" + l); part.transform.SetParent(tree, false);
                        part.AddComponent<MeshFilter>().sharedMesh = treeMeshes[l]; var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = treeMaterial;
                        renderer.shadowCastingMode = l == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        levels[l] = new LOD(l == 0 ? .12f : l == 1 ? .04f : .002f, new Renderer[] { renderer });
                    }
                    var lod = tree.gameObject.AddComponent<LODGroup>(); lod.SetLODs(levels); lod.RecalculateBounds();
                    Box("Tronco do cipreste", new Vector3(x, 1.4f, z), new Vector3(.5f, 2.8f, .5f), iron, true);
                }
                foreach (float x in new[] { -10.5f, 10.5f })
                {
                    Box("Canteiro de entrada", new Vector3(x, .10f, -25.2f), new Vector3(4.8f, .24f, 1.2f), coping, true);
                    Box("Terra do canteiro", new Vector3(x, .24f, -25.2f), new Vector3(4.5f, .06f, 1), soil);
                    for (int j = -3; j <= 3; j++) Flowers(new Vector3(x + j * .58f, .26f, -25.2f), j + 13);
                }
                Bench(-10.4f, -3.8f, 0); Bench(10.4f, -3.8f, 0);
                foreach (float x in new[] { -3.15f, 3.15f }) foreach (float z in new[] { -23.4f, -2f, 16f }) Lamp(x, z);

                Group("Capela aberta");
                Box("Piso contínuo da capela", new Vector3(0, -.015f, 22), new Vector3(8.4f, .10f, 7.4f), stone[3], true, floor: true);
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Fachada da capela", new Vector3(side * 2.85f, 1.7f, 18.4f), new Vector3(2.7f, 3.4f, .34f), plaster, true);
                    Box("Parede sob a janela", new Vector3(side * 4.05f, .65f, 22), new Vector3(.30f, 1.3f, 7.2f), plaster, true);
                    Box("Parede sobre a janela", new Vector3(side * 4.05f, 2.95f, 22), new Vector3(.30f, .9f, 7.2f), plaster, true);
                    foreach (float z in new[] { 19.7f, 24.3f }) Box("Parede lateral", new Vector3(side * 4.05f, 1.9f, z), new Vector3(.30f, 1.2f, 2.6f), plaster, true);
                    Box("Vidro da capela", new Vector3(side * 4.05f, 1.9f, 22), new Vector3(.035f, 1.16f, 1.95f), lens, true);
                    Box("Caixilho", new Vector3(side * 4.05f, 1.9f, 22), new Vector3(.34f, 1.2f, .06f), iron);
                    Box("Caixilho", new Vector3(side * 4.05f, 1.9f, 22), new Vector3(.34f, .06f, 2), iron);
                    Box("Água do telhado", new Vector3(side * 2.25f, 4.23f, 22), new Vector3(4.91f, .19f, 8.1f), roof, true, Quaternion.Euler(0, 0, -side * 20));
                    Bench(side * 2.5f, 20.3f, 0, true); Bench(side * 2.5f, 22.7f, 0, true);
                }
                Box("Verga da entrada", new Vector3(0, 3.07f, 18.4f), new Vector3(3.0f, .66f, .34f), plaster, true);
                Box("Parede do altar", new Vector3(0, 1.7f, 25.5f), new Vector3(8.4f, 3.4f, .34f), plaster, true);
                foreach (float z in new[] { 18.4f, 25.5f })
                {
                    var mesh = new Mesh { name = "Frontão da capela" };
                    mesh.vertices = new[] { new Vector3(-4.2f, 3.4f, z), new Vector3(4.2f, 3.4f, z), new Vector3(0, 5.0f, z), new Vector3(-4.2f, 3.4f, z), new Vector3(0, 5.0f, z), new Vector3(4.2f, 3.4f, z) };
                    mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    Shape(mesh, Vector3.zero, Vector3.one, Quaternion.identity, plaster);
                }
                Box("Mesa do altar", new Vector3(0, .47f, 24.55f), new Vector3(1.9f, .92f, .85f), stone[3], true);
                Box("Cruz da capela", new Vector3(0, 2.23f, 25.28f), new Vector3(.13f, 1.36f, .10f), wood);
                Box("Cruz da capela", new Vector3(0, 2.47f, 25.28f), new Vector3(.80f, .13f, .10f), wood);
                Flowers(new Vector3(-1.35f, 0, 24.6f), 9); Flowers(new Vector3(1.35f, 0, 24.6f), 10);
                Box("Cruz do frontão", new Vector3(0, 5.4f, 18.45f), new Vector3(.14f, .9f, .14f), coping);
                Box("Cruz do frontão", new Vector3(0, 5.56f, 18.45f), new Vector3(.58f, .13f, .14f), coping);

                Group("Memorial do jardim");
                Box("Base do memorial", new Vector3(12, .18f, 23), new Vector3(1.8f, .36f, 1.8f), coping, true);
                Box("Memorial", new Vector3(12, 1.2f, 23), new Vector3(.9f, 1.7f, .9f), stone[1], true);
                Box("Coroamento do memorial", new Vector3(12, 2.13f, 23), new Vector3(1.15f, .16f, 1.15f), stone[1]);
                Flowers(new Vector3(10.9f, 0, 22.7f), 7); Flowers(new Vector3(13.1f, 0, 22.7f), 8);

                Group("Malhas estáticas agrupadas por região e material");
                foreach (var batch in batches)
                {
                    var mesh = new Mesh { name = batch.Key, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(batch.Value.ToArray(), true, true);
                    mesh.RecalculateBounds(); AssetDatabase.AddObjectToAsset(mesh, library);
                    var go = new GameObject(batch.Key); go.transform.SetParent(group, false); go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = batchMaterials[batch.Key];
                }
                info.seats = seats.ToArray(); info.seatNodes = seatNodes.ToArray();
                info.walkRoute = new[] { info.streetApproach, Point(0, -27), Point(0, -1.5f), Point(-18.2f, -1.5f), Point(-18.2f, 27), Point(0, 27), Point(18.2f, 27), Point(18.2f, -1.5f), Point(0, -1.5f), Point(0, 19.2f), Point(0, 23.2f), Point(0, -27), info.streetApproach };
            }
            Vector3 Point(float x, float z) => root.TransformPoint(new Vector3(x, .075f, z));
        }
    }
}
