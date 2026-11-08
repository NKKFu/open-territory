using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory.Editor
{
    // Explicit request only. Wait for other authoring/build work in the shared Editor.
    [InitializeOnLoad]
    public static class CityCarDealershipBuildJob
    {
        static double next;
        static CityCarDealershipBuildJob() { EditorApplication.update += Tick; }
        static string Hash(string path)
        {
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            string folder = Path.GetFullPath("../Reports/dealership"); string request = Path.Combine(folder, "build.request");
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity") return;
            string output = Path.GetFullPath(File.ReadAllText(request).Trim());
            string allowed = Path.GetFullPath("Builds/CarDealership-");
            string delivery = Path.GetFullPath("../Deliveries/CarDealership-");
            if ((!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && !output.StartsWith(delivery, StringComparison.OrdinalIgnoreCase)) || Path.GetFileName(output) != "BaiaDoSol.exe")
            { File.Move(request, request + ".rejected-" + DateTime.UtcNow.Ticks); Debug.LogError("Invalid car dealership build output."); return; }
            foreach (string source in new[] { "Assets/Editor/CityCarDealershipBuilder.cs", "Assets/Editor/CityCarDealershipBuildJob.cs" })
                if (File.GetLastWriteTimeUtc(typeof(CityCarDealershipBuildJob).Assembly.Location) < File.GetLastWriteTimeUtc(source)) return;
            foreach (string source in new[] { "Assets/Scripts/World/CityCarDealership.cs", "Assets/Scripts/World/CityCarDealershipProbe.cs" })
                if (File.GetLastWriteTimeUtc(typeof(CityCarDealership).Assembly.Location) < File.GetLastWriteTimeUtc(source)) return;
            File.Move(request, Path.Combine(folder, "build-consumed-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".request"));
            DateTime started = DateTime.UtcNow;
            try
            {
                var shop = UnityEngine.Object.FindFirstObjectByType<CityCarDealership>();
                if (!shop) throw new InvalidOperationException("The dealership must already be authored in the saved city.");
                // Update only this shop's lettering; retain all concurrent scenery edits.
                bool changed = false;
                foreach (var label in shop.GetComponentsInChildren<Text>(true))
                {
                    string text = label.text;
                    if (text == "S O L   M O T O R S") label.text = "S U N   M O T O R S";
                    else if (text == "SOL MOTORS\nFind your next drive.") label.text = "SUN MOTORS\nFind your next drive.";
                    else if (text == "SOL\nMOTORS") label.text = "SUN\nMOTORS";
                    changed |= label.text != text;
                }
                if (changed || scene.isDirty)
                {
                    Directory.CreateDirectory(Path.Combine(folder, "backups"));
                    File.Copy(scene.path, Path.Combine(folder, "backups/Before-final-lettering-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity"));
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
                string sceneHash = Hash(scene.path);
                string runtimeAssemblyHash = Hash(typeof(CityCarDealership).Assembly.Location);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scene.path }, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport
                });
                var summary = report.summary;
                string assembly = Path.Combine(Path.GetDirectoryName(output), "BaiaDoSol_Data/Managed/Assembly-CSharp.dll");
                var result = new {
                    result = summary.result.ToString(), output, buildType = "Full", startedUtc = started.ToString("o"),
                    endedUtc = DateTime.UtcNow.ToString("o"), errors = summary.totalErrors, warnings = summary.totalWarnings,
                    bytes = summary.totalSize, options = "StrictMode, DetailedBuildReport", sceneSha256 = sceneHash,
                    editorRuntimeAssemblySha256 = runtimeAssemblyHash, assemblySha256 = File.Exists(assembly) ? Hash(assembly) : null
                };
                File.WriteAllText(Path.Combine(folder, "build-final.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
                Debug.Log("CAR_DEALERSHIP_FINAL_BUILD " + summary.result + " " + output);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(folder, "build-final.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { result = "Failed", output, error = e.ToString() }));
                Debug.LogException(e);
            }
        }
    }
}
