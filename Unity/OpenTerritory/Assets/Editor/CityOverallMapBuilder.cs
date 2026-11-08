using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenTerritory.Editor
{
    // The patch touches the existing scene. It never imports/replans the city.
    [InitializeOnLoad]
    public static class CityOverallMapBuilder
    {
        const string Reports = "../Reports/overall-map";
        static double nextRequestCheck;
        static CityOverallMapBuilder() { EditorApplication.update += ApplyRequested; }
        static void ApplyRequested()
        {
            if (EditorApplication.timeSinceStartup < nextRequestCheck) return;
            nextRequestCheck = EditorApplication.timeSinceStartup + 1;
            string request = Reports + "/apply.request";
            bool sitesOnly = !File.Exists(request) && File.Exists(Reports + "/preserve-sites.request");
            if (sitesOnly) request = Reports + "/preserve-sites.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            File.Move(request, Reports + "/apply-started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            try
            {
                if (sitesOnly) { PreserveAuthoredSitesAndSave(); return; }
                var world = JsonUtility.FromJson<WorldData>(Object.FindFirstObjectByType<WorldRegistry>().manifest.text);
                string complete = Reports + "/apply-complete.json";
                if (File.Exists(complete) && JsonUtility.FromJson<Preservation>(File.ReadAllText(complete)).sourceHash == world.sourceHash) return;
                Apply();
            }
            catch (Exception error) { File.WriteAllText(Reports + "/apply-last-error.txt", error.ToString()); Debug.LogException(error); }
        }
        [Serializable] sealed class Move { public string category; public Vector3 before, after; }
        [Serializable] sealed class Patch { public int[] solids, materials; public string[] batchKeys; public Move[] natureMoves; }
        [Serializable] sealed class Preservation
        {
            public bool passed;
            public string verifiedAtUtc, sourceHash;
            public int protectedColliders, approvedColliderChanges, movedTrees, movedRocks;
            public List<string> unexpectedChanges = new List<string>();
        }
        [Serializable] sealed class BuildRecord
        {
            public bool passed;
            public string completedAtUtc, executable, result;
            public int errors, warnings;
            public ulong bytes;
        }

        static string Signature(Collider c)
        {
            string s = c.name + "|" + c.enabled + "|" + c.gameObject.activeSelf + "|" + c.gameObject.layer + "|" + c.transform.localToWorldMatrix.ToString("R");
            if (c is BoxCollider box) s += "|" + box.center.ToString("R") + "|" + box.size.ToString("R");
            if (c is MeshCollider mesh) s += "|" + (mesh.sharedMesh ? mesh.sharedMesh.GetInstanceID() : 0);
            return s;
        }

        [MenuItem("Open Territory/Aplicar revisão geral do mapa")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity")
                throw new InvalidOperationException("BaiaDoSol deve estar salva e fora de Play Mode.");
            var patch = JsonUtility.FromJson<Patch>(File.ReadAllText("Assets/Imported/overall-map-patch.json"));
            // Donor materials also belong to converted interiors. A parking
            // patch must address those original draw batches by exact name.
            if (patch.materials.Any(m => m < 843)) throw new InvalidOperationException("Material de doador fora de um agrupamento explícito.");
            var staticRoot = Object.FindFirstObjectByType<CityLodManager>().transform.Find("Novo mapa • geometria agrupada");
            var batchNames = new HashSet<string>(staticRoot.GetComponentsInChildren<MeshFilter>(true).Select(f => f.name));
            foreach (string key in patch.batchKeys)
                if (!batchNames.Contains(key)) throw new InvalidOperationException("Agrupamento do estacionamento ausente: " + key);
            var approved = new HashSet<int>(patch.solids);
            var preserved = new Dictionary<Collider, string>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.gameObject.layer == CityContext.GroundLayer) continue;
                if (c.name.StartsWith("Colisão-") && int.TryParse(c.name.Substring(8), out int id) && approved.Contains(id)) continue;
                preserved[c] = Signature(c);
            }
            Directory.CreateDirectory(Reports + "/backups");
            if (!EditorSceneManager.SaveScene(scene, Reports + "/backups/Before-scene-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Não foi possível preservar a cena anterior.");
            CityStreetReviewBuilder.ApplyPatch("Assets/Imported/overall-map-patch.json", "Assets/Imported/CityOverallMap.asset");
            var report = new Preservation { verifiedAtUtc = DateTime.UtcNow.ToString("o"), approvedColliderChanges = approved.Count, protectedColliders = preserved.Count };
            foreach (var pair in preserved)
                if (!pair.Key || Signature(pair.Key) != pair.Value) report.unexpectedChanges.Add(pair.Key ? pair.Key.name : "Removed collider");
            report.passed = report.unexpectedChanges.Count == 0;
            if (!report.passed)
            {
                File.WriteAllText(Reports + "/preservation.json", JsonUtility.ToJson(report, true));
                throw new InvalidOperationException("A revisão alterou colisões fora do escopo: " + string.Join(", ", report.unexpectedChanges.Take(12)));
            }

            var city = Object.FindFirstObjectByType<CityLodManager>().transform;
            foreach (var move in patch.natureMoves)
            {
                var parent = city.Find(move.category == "tree" ? "Árvores • 24 variações • LOD" : "Pedras e rochas • afloramentos naturais");
                var target = parent.Cast<Transform>().FirstOrDefault(t => (t.position - move.before).sqrMagnitude < .0025f);
                if (!target) throw new InvalidOperationException("Elemento natural não encontrado: " + move.category + " " + move.before);
                target.position = move.after;
                if (move.category == "tree") report.movedTrees++; else report.movedRocks++;
            }
            Physics.SyncTransforms();
            // Keep the current home interiors, fixtures and their original cuts.
            CityInteriorGroundBuilder.Bake();
            PreserveAuthoredSites();
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            report.sourceHash = world.sourceHash;
            CityPropGroundingBuilder.Build(world, registry);
            Physics.SyncTransforms();
            CityGroundCoverBuilder.PlacementHeight = (x, z) =>
            {
                if (Physics.Raycast(new Vector3(x, 350, z), Vector3.down, out var hit, 500, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) return hit.point.y;
                throw new InvalidOperationException("Vegetação sem piso físico: " + x + ", " + z);
            };
            try { CityGroundCoverBuilder.Rebuild(); }
            finally { CityGroundCoverBuilder.PlacementHeight = null; }
            File.WriteAllText(Reports + "/preservation.json", JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Não foi possível salvar a revisão.");
            File.WriteAllText(Reports + "/apply-complete.json", JsonUtility.ToJson(report, true));
            Debug.Log("CITY_OVERALL_MAP_APPLIED protectedColliders=" + report.protectedColliders + " movedTrees=" + report.movedTrees + " movedRocks=" + report.movedRocks);
        }

        public static void PreserveAuthoredSites()
        {
            CityMapAccessBuilder.ApplyStoredMoves();
            CityMapAccessBuilder.RestoreRemoteTreeColliders();
            var library = AssetDatabase.LoadMainAssetAtPath("Assets/Imported/CityOverallMap.asset");
            var clothing = Object.FindFirstObjectByType<CityClothingShop>();
            if (clothing)
            {
                bool stale = Enumerable.Range(0, clothing.groundColliders.Length).Any(i => clothing.groundColliders[i] && clothing.groundColliders[i].sharedMesh != clothing.cutCollisionMeshes[i])
                    || Enumerable.Range(0, clothing.groundRenderers.Length).Any(i => clothing.groundRenderers[i] && clothing.groundRenderers[i].sharedMesh != clothing.cutGroundMeshes[i]);
                if (stale) { CityClothingGroundCut.Restore(clothing); CityClothingGroundCut.Bake(clothing, library); EditorUtility.SetDirty(clothing); }
            }
            var bikes = Object.FindFirstObjectByType<CityBikeShop>();
            if (bikes && bikes.floorChanges.Any(f => f.target && f.target.sharedMesh && !f.target.sharedMesh.name.Contains("bike shop retired floors")))
            {
                // Reuse the owning builder's narrow collision cut. Rebuilding
                // its scenery would replace the other task's authored interiors.
                var world = JsonUtility.FromJson<WorldData>(Object.FindFirstObjectByType<WorldRegistry>().manifest.text);
                typeof(CityBikeShopBuilder).GetMethod("CutOldFloors", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, new object[] { bikes, world, library });
                EditorUtility.SetDirty(bikes);
            }
            Physics.SyncTransforms();
        }

        public static void PreserveAuthoredSitesAndSave()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty || Application.isPlaying || BuildPipeline.isBuildingPlayer || scene.path != "Assets/Scenes/BaiaDoSol.unity") throw new InvalidOperationException("A cena deve estar salva e livre.");
            PreserveAuthoredSites();
            CityGroundCoverBuilder.PlacementHeight = (x, z) => { if (Physics.Raycast(new Vector3(x,350,z),Vector3.down,out var hit,500,1 << CityContext.GroundLayer)) return hit.point.y; throw new InvalidOperationException("Ground cover has no physical floor."); };
            try { CityGroundCoverBuilder.Rebuild(); } finally { CityGroundCoverBuilder.PlacementHeight = null; }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Reports + "/authored-sites-preserved.txt", DateTime.UtcNow.ToString("o"));
        }

        // Invoked via delayCall so the MCP response completes before a full build.
        public static void QueueBuild(string output)
        {
            output = Path.GetFullPath(output);
            string deliveries = Path.GetFullPath("../Deliveries") + Path.DirectorySeparatorChar;
            if (!output.StartsWith(deliveries, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use uma pasta exclusiva dentro de Unity/Deliveries.");
            if (File.Exists(output)) throw new InvalidOperationException("A entrega já existe: " + output);
            if (EditorSceneManager.GetActiveScene().isDirty || Application.isPlaying || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("O Editor deve estar livre, com a cena salva.");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" },
                        locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport });
                    var record = new BuildRecord { passed = build.summary.result == BuildResult.Succeeded, completedAtUtc = DateTime.UtcNow.ToString("o"),
                        executable = output, result = build.summary.result.ToString(), errors = build.summary.totalErrors, warnings = build.summary.totalWarnings, bytes = build.summary.totalSize };
                    File.WriteAllText(Reports + "/build-complete.json", JsonUtility.ToJson(record, true));
                }
                catch (Exception error) { File.WriteAllText(Reports + "/build-error.txt", error.ToString()); Debug.LogException(error); }
            };
        }
    }
}
