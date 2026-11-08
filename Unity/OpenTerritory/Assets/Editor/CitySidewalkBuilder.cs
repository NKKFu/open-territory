using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Explicit authoring request only. Waiting for a saved edit-mode scene keeps
    // an unrelated Play session, build or unfinished scene edit intact.
    [InitializeOnLoad]
    public static class CitySidewalkBuilder
    {
        const string Reports = "../Reports/sidewalks";
        static double next;
        [Serializable] sealed class BuildRecord
        {
            public string verifiedAtUtc, executable, result;
            public bool passed;
            public int errors, warnings;
        }
        static CitySidewalkBuilder() { EditorApplication.update += Tick; }

        [MenuItem("Open Territory/Aplicar alargamento das calçadas")]
        public static void Queue()
        {
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Reports + "/apply.request", DateTime.UtcNow.ToString("o"));
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            string request = Reports + "/apply.request";
            if ((!File.Exists(request) && !File.Exists(Reports + "/build.request")) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty || scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            if (!File.Exists(request)) { BuildQueuedPlayer(); return; }
            File.Move(request, Reports + "/apply-started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
            bool applied = false;
            try
            {
                Directory.CreateDirectory(Reports + "/backups");
                if (!EditorSceneManager.SaveScene(scene, Reports + "/backups/Before-scene-patch-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                    throw new InvalidOperationException("Não foi possível preservar a cena anterior.");
                CityStreetReviewBuilder.ApplyPatch("Assets/Imported/sidewalk-patch.json", "Assets/Imported/CitySidewalks.asset");
                CityGroundCoverBuilder.Rebuild();
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Não foi possível salvar as calçadas.");
                File.WriteAllText(Reports + "/apply-complete.txt", DateTime.UtcNow.ToString("o"));
                applied = true;
                Debug.Log("CITY_SIDEWALKS_APPLIED: pavement, curbs and ground-cover mask updated.");
            }
            catch (Exception error)
            {
                File.WriteAllText(Reports + "/apply-error.txt", error.ToString());
                Debug.LogException(error);
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
                EditorApplication.UnlockReloadAssemblies();
            }
            if (applied) BuildQueuedPlayer();
        }

        static void BuildQueuedPlayer()
        {
            string request = Reports + "/build.request";
            if (!File.Exists(request)) return;
            var library = AssetDatabase.LoadAssetAtPath<CityAssetLibrary>("Assets/Imported/CitySidewalks.asset");
            var world = JsonUtility.FromJson<WorldData>(File.ReadAllText("Assets/Resources/world.json"));
            if (!library || library.sourceHash != world.sourceHash) return;
            string output = Path.GetFullPath(File.ReadAllText(request).Trim());
            string builds = Path.GetFullPath("Builds") + Path.DirectorySeparatorChar;
            if (!output.StartsWith(builds, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O player de calçadas deve ficar em uma pasta exclusiva dentro de Builds.");
            File.Move(request, Reports + "/build-started-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                });
                var record = new BuildRecord { verifiedAtUtc = DateTime.UtcNow.ToString("o"), executable = output,
                    passed = result.summary.result == BuildResult.Succeeded, result = result.summary.result.ToString(),
                    errors = result.summary.totalErrors, warnings = result.summary.totalWarnings };
                File.WriteAllText(Reports + "/build-complete.json", JsonUtility.ToJson(record, true));
                if (!record.passed) throw new InvalidOperationException("A construção das calçadas falhou: " + record.result);
            }
            catch (Exception error)
            {
                File.WriteAllText(Reports + "/build-error.txt", error.ToString());
                Debug.LogException(error);
            }
        }
    }
}
