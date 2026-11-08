using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    // Offline foundations join the retained level slabs to the triangulated
    // landscape. Moving a whole building would also move its doors/interior.
    public static class CityFoundationBuilder
    {
        const string AssetPath = "Assets/Imported/CityFoundations.asset";
        const string RootName = "Building foundations";
        const float Inset = .008f, Top = .03f, Embed = .20f;

        [MenuItem("Open Territory/Assentar bases dos prédios no terreno")]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("Abra BaiaDoSol fora do Play Mode.");
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            if (!registry || !registry.manifest) throw new InvalidOperationException("Registro da cidade ausente.");
            Directory.CreateDirectory("../Reports/foundations/backups");
            EditorSceneManager.SaveScene(scene, "../Reports/foundations/backups/Before-foundations-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            Build(JsonUtility.FromJson<WorldData>(registry.manifest.text));
            Validate();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Build(WorldData world)
        {
            if (world.ground == null || world.ground.heights.Length == 0)
                throw new InvalidOperationException("Relevo da cidade ausente.");
            var city = Object.FindFirstObjectByType<CityLodManager>();
            if (!city) throw new InvalidOperationException("Raiz da cidade ausente.");
            var previous = city.transform.Find(RootName);
            if (previous) Object.DestroyImmediate(previous.gameObject);
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var library = ScriptableObject.CreateInstance<CityAssetLibrary>();
            AssetDatabase.CreateAsset(library, AssetPath);
            var material = new Material(Shader.Find("OpenTerritory/CitySurface")) { name = "Foundations • warm concrete", enableInstancing = true };
            material.SetColor("_Color", new Color(.73f, .71f, .65f));
            material.SetFloat("_Glossiness", .12f);
            material.SetFloat("_Cull", 2);
            AssetDatabase.AddObjectToAsset(material, library);
            var sample = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cube = Object.Instantiate(sample.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(sample);
            cube.name = "Foundation block • shared";
            cube.colors = Enumerable.Repeat(Color.white, cube.vertexCount).ToArray();
            AssetDatabase.AddObjectToAsset(cube, library);
            var root = new GameObject(RootName).transform;
            root.SetParent(city.transform, false);
            for (int i = 0; i < world.places.Length; i++)
            {
                var p = world.places[i];
                if (p.lotWidth <= 0 || p.lotDepth <= 0) continue;
                float bottom = MinimumTerrain(world.ground, p) - Embed;
                bottom = Mathf.Min(bottom, p.elevation - Embed);
                float height = p.elevation + Top - bottom;
                var block = new GameObject("Foundation • " + i);
                block.transform.SetParent(root, false);
                block.transform.SetPositionAndRotation(new Vector3(p.x, bottom + height * .5f, p.z), Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0));
                // A tiny recess avoids coplanar faces where the new base
                // overlaps the old slab. Its top stays below the finished floor.
                block.transform.localScale = new Vector3(p.lotWidth - Inset * 2, height, p.lotDepth - Inset * 2);
                block.layer = CityContext.GroundLayer;
                block.isStatic = true;
                block.AddComponent<MeshFilter>().sharedMesh = cube;
                block.AddComponent<MeshRenderer>().sharedMaterial = material;
                block.AddComponent<BoxCollider>();
            }
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            Debug.Log("CITY_FOUNDATIONS_BAKED count=" + root.childCount);
        }

        static float MinimumTerrain(GroundData g, PlaceData p)
        {
            var rotation = Quaternion.Euler(0, p.yaw * Mathf.Rad2Deg, 0);
            var bounds = new Bounds(new Vector3(p.x, 0, p.z), Vector3.zero);
            foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                bounds.Encapsulate(new Vector3(p.x, 0, p.z) + rotation * new Vector3(x * p.lotWidth * .5f, 0, z * p.lotDepth * .5f));
            int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - g.x) / g.step), 0, g.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x - g.x) / g.step), 0, g.width - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - g.z) / g.step), 0, g.height - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z - g.z) / g.step), 0, g.height - 1);
            // A linear terrain triangle cannot dip below its vertices. Include
            // every grid cell touched by the rotated footprint, not just corners.
            float minimum = float.PositiveInfinity;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                minimum = Mathf.Min(minimum, g.heights[z * g.width + x]);
            return minimum;
        }

        static float TerrainHeight(GroundData g, Vector3 point)
        {
            float x = Mathf.Clamp((point.x - g.x) / g.step, 0, g.width - 1.001f);
            float z = Mathf.Clamp((point.z - g.z) / g.step, 0, g.height - 1.001f);
            int i = (int)x, j = (int)z; float a = x - i, b = z - j;
            float h00 = g.heights[j * g.width + i], h10 = g.heights[j * g.width + i + 1];
            float h01 = g.heights[(j + 1) * g.width + i], h11 = g.heights[(j + 1) * g.width + i + 1];
            return a + b <= 1 ? h00 + a * (h10 - h00) + b * (h01 - h00) : h11 + (1 - a) * (h01 - h11) + (1 - b) * (h10 - h11);
        }

        [Serializable] public sealed class Report
        {
            public bool passed;
            public string verifiedAtUtc;
            public int buildings, perimeterSamples, buildingsWithOriginalGap, missingFoundations, unsealedSamples, collisionFailures, footprintFailures;
            public float largestOriginalGap;
        }

        public static Report Validate()
        {
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var city = Object.FindFirstObjectByType<CityLodManager>();
            var root = city.transform.Find(RootName);
            var report = new Report { verifiedAtUtc = DateTime.UtcNow.ToString("o"), buildings = world.places.Length };
            Physics.SyncTransforms();
            for (int i = 0; i < world.places.Length; i++)
            {
                var p = world.places[i]; var block = root ? root.Find("Foundation • " + i) : null;
                if (!block) { report.missingFoundations++; continue; }
                var collider = block.GetComponent<BoxCollider>(); var renderer = block.GetComponent<MeshRenderer>();
                float bottom = collider.bounds.min.y, top = collider.bounds.max.y;
                if (Mathf.Abs(block.localScale.x - (p.lotWidth - Inset * 2)) > .001f || Mathf.Abs(block.localScale.z - (p.lotDepth - Inset * 2)) > .001f
                    || Mathf.Abs(top - p.elevation - Top) > .001f || !renderer || !renderer.enabled) report.footprintFailures++;
                float originalGap = 0;
                for (int side = 0; side < 4; side++)
                {
                    int count = Mathf.CeilToInt((side < 2 ? p.lotDepth : p.lotWidth) / .25f);
                    for (int k = 0; k <= count; k++)
                    {
                        float t = k / (float)count;
                        var local = side < 2 ? new Vector3((side == 0 ? -.5f : .5f) * p.lotWidth, 0, Mathf.Lerp(-p.lotDepth * .5f, p.lotDepth * .5f, t))
                            : new Vector3(Mathf.Lerp(-p.lotWidth * .5f, p.lotWidth * .5f, t), 0, (side == 2 ? -.5f : .5f) * p.lotDepth);
                        var point = new Vector3(p.x, 0, p.z) + block.rotation * local;
                        float ground = TerrainHeight(world.ground, point);
                        originalGap = Mathf.Max(originalGap, p.elevation - ground);
                        report.perimeterSamples++;
                        if (bottom > ground - Embed + .002f || top < p.elevation) report.unsealedSamples++;
                        var inner = new Vector3(point.x, top + 1, point.z);
                        inner += (new Vector3(p.x, inner.y, p.z) - inner).normalized * .04f;
                        if (!collider.enabled || !collider.Raycast(new Ray(inner, Vector3.down), out var hit, 2) || Mathf.Abs(hit.point.y - top) > .002f)
                            report.collisionFailures++;
                    }
                }
                if (originalGap > .03f) report.buildingsWithOriginalGap++;
                report.largestOriginalGap = Mathf.Max(report.largestOriginalGap, originalGap);
            }
            report.passed = report.missingFoundations + report.unsealedSamples + report.collisionFailures + report.footprintFailures == 0;
            Directory.CreateDirectory("../Reports/foundations");
            File.WriteAllText("../Reports/foundations/editor-validation.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Falha na validação das fundações: " + JsonUtility.ToJson(report));
            Debug.Log("CITY_FOUNDATIONS_VALIDATED " + JsonUtility.ToJson(report));
            return report;
        }
    }
}
