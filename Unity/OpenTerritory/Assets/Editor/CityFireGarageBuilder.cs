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
    public static class CityFireGarageBuilder
    {
        const string AssetPath = "Assets/Imported/FireGarage.asset";
        static readonly Bounds FacadeCut = new Bounds(new Vector3(-6.51f, 2.02f, 10.63f), new Vector3(9.98f, 4.04f, .90f));
        static readonly Bounds FloorCut = new Bounds(new Vector3(-7.01f, .18f, 2.25f), new Vector3(8.56f, .25f, 16.05f));
        static readonly Bounds Plant = new Bounds(new Vector3(-3.5f, 1.35f, 11.55f), new Vector3(1.05f, 2.25f, 1.05f));
        static readonly Vector3 PlantMove = new Vector3(-6.95f, 0, 0);

        [MenuItem("Open Territory/Atualizar garagem dos bombeiros")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode para atualizar a garagem.");
            // A snapshot also preserves unrelated edits in this already heavily authored scene.
            Directory.CreateDirectory("../Reports/garage/backups");
            EditorSceneManager.SaveScene(scene, "../Reports/garage/backups/Before-garage-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var relocated = Object.FindFirstObjectByType<CityFireStationSite>();
            if (relocated) world.places[CityFireStationSite.PlaceIndex] = JsonUtility.FromJson<PlaceData>(JsonUtility.ToJson(relocated.place));
            Build(world, registry);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        public static void Build(WorldData world, WorldRegistry registry)
        {
            int index = Array.FindIndex(world.places, p => p.kind == "fire");
            if (index < 0) return;
            var place = world.places[index];
            var matrix = Matrix4x4.TRS(new Vector3(place.x, place.elevation, place.z), Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0), Vector3.one);
            var previous = Object.FindFirstObjectByType<CityFireGarage>();
            if (previous) Restore(previous);
            Physics.SyncTransforms();
            // This refits the existing left room; no building, garden or neighbour is removed.
            var blockers = Physics.OverlapBox(matrix.MultiplyPoint3x4(new Vector3(-6.2f, 1.8f, 2.3f)),
                new Vector3(3.05f, 1.35f, 7.5f), matrix.rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            if (blockers.Length > 0) throw new InvalidOperationException("A baia contém obstáculos: " + string.Join(", ", blockers.Select(c => c.name)));
            var plantBlockers = Physics.OverlapBox(matrix.MultiplyPoint3x4(new Vector3(-10.45f, 1.2f, 11.55f)),
                new Vector3(.47f, .8f, .47f), matrix.rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            if (plantBlockers.Length > 0) throw new InvalidOperationException("O novo lugar do vaso está ocupado.");
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>(); AssetDatabase.CreateAsset(library, AssetPath);
            var root = new GameObject("Bombeiros • garagem operacional").AddComponent<CityFireGarage>();
            root.transform.SetPositionAndRotation(matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation);
            var sourceChanges = new List<CityFireGarage.MeshChange>();
            foreach (var lod in Object.FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None)
                .Where(l => l.name == "building:Reused building-" + index || l.name == "Janelas • " + index))
            {
                for (int i = 0; i < lod.parts.Length; i++)
                {
                    var part = lod.parts[i]; if (!part.filter || !part.full) continue;
                    var original = new CityFireGarage.MeshChange { filter = part.filter, lod = lod, part = i, full = part.full, medium = part.medium, far = part.far };
                    var cache = new Dictionary<Mesh, Mesh>();
                    Mesh Patch(Mesh source)
                    {
                        if (!source) return source;
                        if (!cache.TryGetValue(source, out var mesh))
                            cache[source] = mesh = Cut(source, matrix.inverse * part.filter.transform.localToWorldMatrix, library);
                        return mesh;
                    }
                    part.full = Patch(part.full); part.medium = Patch(part.medium); part.far = Patch(part.far);
                    part.filter.sharedMesh = part.full; part.externalMesh = false;
                    sourceChanges.Add(original);
                }
            }
            root.originals = sourceChanges.ToArray();
            var collisionChanges = new List<CityFireGarage.ColliderChange>();
            foreach (var collider in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                var q = matrix.inverse.MultiplyPoint3x4(collider.bounds.center);
                bool wall = Mathf.Abs(q.x + 6.51f) < .12f && Mathf.Abs(q.z - 10.5f) < .12f && collider.size.y > 5;
                bool plant = Mathf.Abs(q.x + 3.5f) < .12f && Mathf.Abs(q.z - 11.55f) < .12f;
                if (!wall && !plant) continue;
                collisionChanges.Add(new CityFireGarage.ColliderChange { collider = collider, enabled = collider.enabled,
                    position = collider.transform.position, center = collider.center, size = collider.size });
                if (wall) collider.enabled = false;
                else collider.transform.position += matrix.MultiplyVector(PlantMove);
            }
            if (!collisionChanges.Any(c => c.size.y > 5)) throw new InvalidOperationException("A parede original da garagem não foi localizada.");
            root.colliders = collisionChanges.ToArray();
            var existingLighting = Object.FindFirstObjectByType<CityLighting>();
            if (existingLighting)
            {
                root.replacedLamps = existingLighting.fixtures.Where(f => f.place == index && f.anchor && f.source && f.anchor.gameObject.activeSelf)
                    .Where(f => { var q = matrix.inverse.MultiplyPoint3x4(f.source.transform.position); return q.x < -2.8f && q.x > -11.3f && q.z > -5.7f && q.z < 10.3f; })
                    .Select(f => f.anchor.gameObject).Distinct().ToArray();
                foreach (var oldLamp in root.replacedLamps) oldLamp.SetActive(false);
            }
            var truck = world.vehicles.FirstOrDefault(v => v.role == "fire");
            if (truck != null)
            {
                root.vehicle = truck.id; root.parkedTruck = registry.nodes.First(n => n.id == truck.id).transform;
                root.originalTruckPosition = root.parkedTruck.position; root.originalTruckRotation = root.parkedTruck.rotation;
                root.parkedTruck.SetPositionAndRotation(root.TruckPosition, root.transform.rotation);
            }

            Material Pigment(string name, Color color, float smooth = .15f, float metal = 0)
            {
                var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = name, enableInstancing = true };
                material.SetColor("_Color", color); material.SetFloat("_Glossiness", smooth); material.SetFloat("_Metallic", metal); material.SetFloat("_Cull", 2);
                AssetDatabase.AddObjectToAsset(material, library); return material;
            }
            var plaster = Pigment("Garagem • reboco quente", new Color(.75f, .70f, .60f));
            var red = Pigment("Garagem • vermelho óxido", new Color(.53f, .18f, .14f), .28f, .3f);
            var dark = Pigment("Garagem • aço grafite", new Color(.12f, .17f, .18f), .38f, .55f);
            var steel = Pigment("Garagem • alumínio", new Color(.55f, .61f, .59f), .45f, .65f);
            var cream = Pigment("Garagem • marfim", new Color(.89f, .84f, .69f));
            var yellow = Pigment("Garagem • marcação ocre", new Color(.94f, .66f, .21f));
            var floor = Pigment("Garagem • concreto selado", new Color(.40f, .46f, .44f), .22f);
            var rubber = Pigment("Garagem • borracha", new Color(.08f, .11f, .12f));
            var lens = Pigment("Garagem • luminária", new Color(.88f, .94f, .86f)); lens.SetColor("_EmissionColor", new Color(.65f, .70f, .60f));
            var cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube); var cube = cubeObject.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cubeObject);
            GameObject Box(string name, Vector3 center, Vector3 size, Material material, bool solid = false, Transform parent = null)
            {
                var go = new GameObject(name); go.transform.SetParent(parent ? parent : root.transform, false);
                go.transform.localPosition = center; go.transform.localScale = size;
                go.AddComponent<MeshFilter>().sharedMesh = cube; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                if (solid) { go.layer = CityContext.WorldLayer; go.AddComponent<BoxCollider>(); }
                return go;
            }
            void Wall(string name, float x, float y, float z, float w, float h, float d, Material material) =>
                Box(name, new Vector3(x, y, z), new Vector3(w, h, d), material, true);
            void Label(string text, Vector3 center, Vector2 size, float fontSize, bool inward = false, Transform parent = null)
            {
                var go = new GameObject(text, typeof(RectTransform)); go.transform.SetParent(parent ? parent : root.transform, false);
                go.transform.localPosition = center; go.transform.localRotation = Quaternion.Euler(0, inward ? 0 : 180, 0); go.transform.localScale = Vector3.one * .01f;
                go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; ((RectTransform)go.transform).sizeDelta = size * 100;
                var label = new GameObject("Letreiro", typeof(RectTransform)).AddComponent<Text>(); label.transform.SetParent(go.transform, false);
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = Mathf.RoundToInt(fontSize); label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter; label.color = new Color(.96f, .90f, .73f); label.raycastTarget = false; label.text = text;
            }

            Wall("Pilar esquerdo da garagem", -10.4f, 2.15f, 10.5f, 2.2f, 3.78f, .42f, plaster);
            Wall("Pilar junto à recepção", -2.31f, 2.15f, 10.5f, 1.58f, 3.78f, .42f, plaster);
            Wall("Verga estrutural", -6.2f, 3.92f, 10.5f, 6.2f, .32f, .48f, dark);
            Label("G A R A G E   /   0 1", new Vector3(-6.2f, 3.93f, 10.76f), new Vector2(5.9f, .26f), 21);
            foreach (float x in new[] { -9.42f, -2.98f })
            {
                Wall("Trilho vertical protegido", x, 2.07f, 10.50f, .18f, 3.56f, .26f, steel);
                Wall("Protetor de impacto", x, .78f, 10.84f, .24f, .94f, .26f, yellow);
                for (int i = 0; i < 4; i++) Box("Faixa do protetor", new Vector3(x, .46f + i * .20f, 10.98f), new Vector3(.245f, .085f, .018f), dark);
                Box("Trilho no teto", new Vector3(x, 4.28f, 8.5f), new Vector3(.12f, .12f, 3.5f), steel);
            }
            root.statusLamp = Box("Sensor e sinal do portão", new Vector3(-2.69f, 3.5f, 10.82f), new Vector3(.16f, .15f, .12f), yellow).GetComponent<Renderer>();
            root.panels = new Transform[10];
            for (int i = 0; i < root.panels.Length; i++)
            {
                var panel = new GameObject("Portão • painel " + (i + 1)).transform; panel.SetParent(root.transform, false); root.panels[i] = panel;
                Box("Chapa vermelha", Vector3.zero, new Vector3(6.16f, .337f, .11f), red, true, panel);
                Box("Nervura externa", new Vector3(0, -.13f, .061f), new Vector3(6.05f, .025f, .025f), steel, false, panel);
                Box("Reforço interno", new Vector3(0, 0, -.08f), new Vector3(6.02f, .08f, .065f), dark, false, panel);
                foreach (float x in new[] { -3f, 3f }) Box("Dobradiça", new Vector3(x, .12f, -.13f), new Vector3(.1f, .10f, .07f), steel, false, panel);
            }
            root.SetPose(0);
            Box("Concreto da baia", new Vector3(-7.01f, .23f, 2.25f), new Vector3(8.56f, .16f, 16.05f), floor, true).layer = CityContext.GroundLayer;
            // Flush joints remain visible without introducing a step for tires.
            for (float z = -4; z < 10; z += 3) Box("Junta do concreto", new Vector3(-7.01f, .312f, z), new Vector3(8.5f, .004f, .016f), dark);
            foreach (float x in new[] { -8.1f, -4.3f }) Box("Faixa da vaga", new Vector3(x, .317f, 1.6f), new Vector3(.09f, .009f, 11.4f), yellow);
            Box("Fundo da vaga", new Vector3(-6.2f, .317f, -4.1f), new Vector3(3.8f, .009f, .10f), yellow);
            for (int i = 0; i < 5; i++) Box("Corredor de circulação", new Vector3(-10.5f, .317f, -2 + i * 2.4f), new Vector3(.12f, .009f, .8f), cream);
            foreach (float x in new[] { -7.36f, -5.04f }) Wall("Batente de roda", x, .39f, -4.15f, .85f, .16f, .30f, rubber);

            // A personnel passage joins the bay and reception. The existing rear equipment stays accessible.
            Wall("Divisória da baia • frente", -2.70f, 2.55f, 6.15f, .18f, 4.48f, 8.27f, plaster);
            Wall("Divisória da baia • fundo", -2.70f, 2.55f, -2.65f, .18f, 4.48f, 6.20f, plaster);
            Wall("Verga da passagem interna", -2.70f, 3.87f, 1.22f, .18f, 1.84f, 1.77f, plaster);
            Wall("Fundo da baia", -7.01f, 2.55f, -5.73f, 8.56f, 4.48f, .18f, plaster);
            Box("Rodapé da divisória", new Vector3(-2.81f, .61f, 6.15f), new Vector3(.035f, .58f, 8.27f), red);
            Box("Rodapé do fundo", new Vector3(-7.01f, .61f, -5.62f), new Vector3(8.56f, .58f, .035f), red);
            Box("Painel de identificação da baia", new Vector3(-6.2f, 3.25f, -5.60f), new Vector3(4.8f, 1.1f, .07f), red);
            Label("01   /   BOMBEIROS", new Vector3(-6.2f, 3.26f, -5.55f), new Vector2(4.6f, .8f), 39);
            var garageFixtures = new List<CityLighting.Fixture>();
            for (int i = 0; i < 2; i++)
            {
                float z = -.8f + i * 7;
                Box("Corpo da luminária linear", new Vector3(-6.2f, 4.71f, z), new Vector3(3.8f, .12f, .38f), dark);
                var lightGo = Box("Difusor da luminária linear", new Vector3(-6.2f, 4.63f, z), new Vector3(3.5f, .05f, .27f), lens);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Point; light.range = 10; light.intensity = .85f;
                light.color = new Color(1, .96f, .85f); light.shadows = LightShadows.None;
                garageFixtures.Add(new CityLighting.Fixture { kind = "teto", place = index, anchor = lightGo.transform,
                    lens = lightGo.GetComponent<Renderer>(), source = light, intensity = 1.05f });
            }
            var receptionLight = Object.FindFirstObjectByType<CityLighting>();
            if (receptionLight)
            {
                receptionLight.fixtures = receptionLight.fixtures.Where(f => f.anchor).Concat(garageFixtures).ToArray();
                var room = receptionLight.rooms.FirstOrDefault(r => r.place == index);
                if (room != null) room.surfaces = room.surfaces.Where(r => r).Concat(root.GetComponentsInChildren<Renderer>()).ToArray();
            }
            Ramp(root.transform, matrix, floor, library);
            CityFireStationPaint.Apply(world);
            AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            Debug.Log("CITY_FIRE_GARAGE_READY parts=" + sourceChanges.Count + " panels=" + root.panels.Length + " vehicle=" + root.vehicle);
        }

        static void Restore(CityFireGarage garage)
        {
            foreach (var item in garage.originals)
            {
                if (item.lod && item.part < item.lod.parts.Length)
                {
                    var part = item.lod.parts[item.part]; part.full = item.full; part.medium = item.medium; part.far = item.far; part.externalMesh = false;
                }
                if (item.filter) item.filter.sharedMesh = item.full;
            }
            foreach (var item in garage.colliders) if (item.collider)
            { item.collider.enabled = item.enabled; item.collider.transform.position = item.position; item.collider.center = item.center; item.collider.size = item.size; }
            foreach (var oldLamp in garage.replacedLamps) if (oldLamp) oldLamp.SetActive(true);
            if (garage.parkedTruck) garage.parkedTruck.SetPositionAndRotation(garage.originalTruckPosition, garage.originalTruckRotation);
            Object.DestroyImmediate(garage.gameObject);
        }

        static void Ramp(Transform root, Matrix4x4 matrix, Material material, Object library)
        {
            const float end = 12.7f;
            float Height(float x)
            {
                RaycastHit hit; var point = matrix.MultiplyPoint3x4(new Vector3(x, 4, end));
                return Physics.Raycast(point, Vector3.down, out hit, 8, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore) ? hit.point.y - root.position.y + .018f : .14f;
            }
            var mesh = new Mesh { name = "Acesso nivelado da garagem" };
            mesh.vertices = new[] { new Vector3(-9.3f, .31f, 10.24f), new Vector3(-9.3f, Height(-9.3f), end),
                new Vector3(-3.1f, Height(-3.1f), end), new Vector3(-3.1f, .31f, 10.24f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray(); mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); mesh.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(mesh, library);
            var go = new GameObject("Rampa entre calçada e baia"); go.transform.SetParent(root, false); go.layer = CityContext.GroundLayer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material; go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        struct Vertex
        {
            public Vector3 p, q, n;
            public Vector2 uv, uv2;
            public Color color;
            public static Vertex Mix(Vertex a, Vertex b, float t) => new Vertex { p = Vector3.LerpUnclamped(a.p, b.p, t), q = Vector3.LerpUnclamped(a.q, b.q, t),
                n = Vector3.LerpUnclamped(a.n, b.n, t), uv = Vector2.LerpUnclamped(a.uv, b.uv, t), uv2 = Vector2.LerpUnclamped(a.uv2, b.uv2, t), color = Color.LerpUnclamped(a.color, b.color, t) };
        }
        // Subtract the opening and floor in room coordinates, preserving every channel and LOD.
        static Mesh Cut(Mesh source, Matrix4x4 toRoom, Object library)
        {
            var points = source.vertices; var normals = source.normals; var uv = source.uv; var uv2 = source.uv2; var colors = source.colors;
            var input = new Vertex[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                input[i] = new Vertex { p = points[i], q = toRoom.MultiplyPoint3x4(points[i]), n = normals.Length == points.Length ? normals[i] : Vector3.up,
                    uv = uv.Length == points.Length ? uv[i] : Vector2.zero, uv2 = uv2.Length == points.Length ? uv2[i] : Vector2.zero, color = colors.Length == points.Length ? colors[i] : Color.white };
            }
            var output = new List<Vertex>(); var submeshes = new List<int[]>(); bool changed = false;
            // The old floating sign extends above the old window. Remove its
            // complete quad without cutting the canopy immediately behind it.
            var oldSign = new Bounds(new Vector3(-6, 3.64f, 10.86f), new Vector3(4.1f, 1.05f, .04f));
            bool oldLabel = points.Length == 4 && input.All(v => oldSign.Contains(v.q));
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var triangles = source.GetTriangles(sub); int start = output.Count;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (oldLabel) { changed = true; continue; }
                    var polygon = new List<Vertex> { input[triangles[i]], input[triangles[i + 1]], input[triangles[i + 2]] };
                    var center = (polygon[0].q + polygon[1].q + polygon[2].q) / 3;
                    if (Plant.Contains(center))
                    {
                        for (int j = 0; j < polygon.Count; j++) { var v = polygon[j]; v.q += PlantMove; v.p += toRoom.inverse.MultiplyVector(PlantMove); polygon[j] = v; }
                        changed = true;
                    }
                    var polygons = new List<List<Vertex>> { polygon };
                    foreach (var cut in new[] { FacadeCut, FloorCut })
                    {
                        var next = new List<List<Vertex>>();
                        foreach (var poly in polygons)
                        {
                            var bounds = new Bounds(poly[0].q, Vector3.zero); foreach (var v in poly) bounds.Encapsulate(v.q);
                            if (!bounds.Intersects(cut)) { next.Add(poly); continue; }
                            var inside = poly;
                            for (int axis = 0; axis < 3 && inside.Count >= 3; axis++) for (int side = 0; side < 2 && inside.Count >= 3; side++)
                            {
                                float edge = side == 0 ? cut.min[axis] : cut.max[axis], sign = side == 0 ? 1 : -1;
                                var outside = Clip(inside, axis, edge, -sign); if (outside.Count >= 3) next.Add(outside);
                                inside = Clip(inside, axis, edge, sign);
                            }
                            if (inside.Count >= 3) changed = true;
                        }
                        polygons = next;
                    }
                    foreach (var poly in polygons) for (int j = 1; j + 1 < poly.Count; j++)
                    {
                        if (Vector3.Cross(poly[j].p - poly[0].p, poly[j + 1].p - poly[0].p).sqrMagnitude < 1e-12f) continue;
                        output.Add(poly[0]); output.Add(poly[j]); output.Add(poly[j + 1]);
                    }
                }
                submeshes.Add(Enumerable.Range(start, output.Count - start).ToArray());
            }
            if (!changed) return source;
            var result = new Mesh { name = source.name + " • garagem", indexFormat = IndexFormat.UInt32 };
            result.SetVertices(output.Select(v => v.p).ToList()); result.SetNormals(output.Select(v => v.n.normalized).ToList());
            result.SetUVs(0, output.Select(v => v.uv).ToList()); result.SetUVs(1, output.Select(v => v.uv2).ToList()); result.SetColors(output.Select(v => v.color).ToList());
            result.subMeshCount = submeshes.Count; for (int i = 0; i < submeshes.Count; i++) result.SetTriangles(submeshes[i], i);
            result.RecalculateBounds(); AssetDatabase.AddObjectToAsset(result, library); return result;
        }
        static List<Vertex> Clip(List<Vertex> polygon, int axis, float edge, float sign)
        {
            var result = new List<Vertex>(); var previous = polygon[polygon.Count - 1]; float a = (previous.q[axis] - edge) * sign;
            foreach (var current in polygon)
            {
                float b = (current.q[axis] - edge) * sign;
                if ((a >= 0) != (b >= 0)) result.Add(Vertex.Mix(previous, current, a / (a - b)));
                if (b >= 0) result.Add(current); previous = current; a = b;
            }
            return result;
        }
    }
}
