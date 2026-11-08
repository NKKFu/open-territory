using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenTerritory.Editor
{
    public static class DriverSeatSnapshotBuild
    {
        [Serializable] sealed class Record
        {
            public bool passed; public string result, executable, snapshot, completedAtUtc;
            public int errors, warnings; public ulong bytes;
        }
        static string Argument(string flag)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, flag);
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException(flag);
            return Path.GetFullPath(args[at + 1]);
        }
        public static void Build()
        {
            string output = Argument("-city-build-path"), recordPath = Argument("-city-build-record");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/pulse.prefab") ||
                !AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/sidearm.prefab") ||
                !AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/carbine.prefab"))
            {
                CityWeaponAssetBuilder.Build();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            EditorSceneManager.OpenScene("Assets/Scenes/BaiaDoSol.unity", OpenSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/BaiaDoSol.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport
            });
            var record = new Record { passed = report.summary.result == BuildResult.Succeeded,
                result = report.summary.result.ToString(), executable = output,
                snapshot = Path.GetFullPath("."), completedAtUtc = DateTime.UtcNow.ToString("o"),
                errors = report.summary.totalErrors, warnings = report.summary.totalWarnings, bytes = report.summary.totalSize };
            File.WriteAllText(recordPath, JsonUtility.ToJson(record, true));
            if (!record.passed) throw new InvalidOperationException("Driver seat build failed: " + record.result);
        }
    }
}
