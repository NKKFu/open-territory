using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in player check: compare the same rendered frame with each layer
    // enabled and disabled, then leave the ordinary world settings untouched.
    public sealed class CityTerrainProbe : MonoBehaviour
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public string graphicsApi; public int errors; public List<Check> checks = new List<Check>(); }
        readonly Report report = new Report();
        string directory;
        CityContext city;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-terrain-qa") >= 0)
                new GameObject("Terrain render verification (opt-in)").AddComponent<CityTerrainProbe>();
        }

        void Awake() { Application.logMessageReceived += OnLog; }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) report.errors++;
        }
        void Test(string name, bool passed, string detail) { report.checks.Add(new Check { name = name, passed = passed, detail = detail }); }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--city-report-dir");
            directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Application.persistentDataPath;
            Directory.CreateDirectory(directory); report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            city = CityContext.Instance;
            if (!city || !city.Ready) { Test("city-ready", false, "Timed out waiting for the city"); Finish(); yield break; }
            city.Pause(false); city.Motor.SetInputLock(true); city.Follow.enabled = false;
            yield return new WaitForSeconds(1.5f);
            // The texture globals must survive Unity's normal unused-asset sweep.
            yield return Resources.UnloadUnusedAssets();

            var color = Shader.GetGlobalTexture("_CityGrassColor") as Texture2D;
            var normal = Shader.GetGlobalTexture("_CityGrassNormal") as Texture2D;
            Test("grass-textures-bound", color && normal && Shader.GetGlobalFloat("_CityGrassEnabled") > .5f,
                "color=" + (color ? color.name : "missing") + ", normal=" + (normal ? normal.name : "missing"));
            Test("repeating-mipmapped-textures", color && normal && color.wrapMode == TextureWrapMode.Repeat && normal.wrapMode == TextureWrapMode.Repeat && color.mipmapCount > 1 && normal.mipmapCount > 1,
                "color=" + (color ? color.width + "x" + color.height + ", mips=" + color.mipmapCount : "missing") + ", normal=" + (normal ? normal.width + "x" + normal.height + ", mips=" + normal.mipmapCount : "missing"));
            var terrain = GameObject.Find("s-surface:0:0-843");
            var filter = terrain ? terrain.GetComponent<MeshFilter>() : null; var renderer = terrain ? terrain.GetComponent<MeshRenderer>() : null;
            var material = renderer ? renderer.sharedMaterial : null;
            Test("existing-terrain-and-shader", filter && filter.sharedMesh && material && material.shader.isSupported && material.shader.name == "OpenTerritory/CitySurface" && Mathf.Abs(material.GetFloat("_Surface") - 5) < .01f,
                "mesh=" + (filter && filter.sharedMesh ? filter.sharedMesh.name : "missing") + ", material=" + (material ? material.name : "missing"));

            Material soil = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
                if (candidate && candidate.name == "Terra dos canteiros") { soil = candidate; break; }
            if (soil)
            {
                var swatch = GameObject.CreatePrimitive(PrimitiveType.Quad); swatch.name = "QA flower-bed soil"; swatch.layer = 31;
                swatch.transform.SetPositionAndRotation(new Vector3(0, 1000, 0), Quaternion.Euler(90, 0, 0));
                swatch.transform.localScale = new Vector3(4, 4, 1); swatch.GetComponent<MeshRenderer>().sharedMaterial = soil;
                var diagnostic = new GameObject("QA soil camera").AddComponent<Camera>(); diagnostic.cullingMask = 1 << 31;
                diagnostic.orthographic = true; diagnostic.orthographicSize = 2; diagnostic.nearClipPlane = .1f; diagnostic.farClipPlane = 10;
                diagnostic.transform.SetPositionAndRotation(new Vector3(0, 1004, 0), Quaternion.Euler(90, 0, 0));
                Shader.SetGlobalFloat("_CityGrassEnabled", 0); var beforeSoil = Capture(diagnostic, "soil-original");
                Shader.SetGlobalFloat("_CityGrassEnabled", 1); var afterSoil = Capture(diagnostic, "soil-grass-enabled");
                int soilDifference = Changed(beforeSoil, afterSoil);
                Test("flower-bed-soil-preserved", soilDifference == 0, "pixels changed=" + soilDifference);
                Destroy(diagnostic.gameObject); Destroy(swatch);
            }
            else Test("flower-bed-soil-preserved", false, "Existing flower-bed material not found");

            var camera = city.Camera; camera.fieldOfView = 58;
            var spots = new[] { new Vector2(260, 130), new Vector2(-75, 62), new Vector2(70, 110) };
            int mostColor = 0, mostNormal = 0;
            for (int i = 0; i < spots.Length; i++)
            {
                var p = spots[i]; float ground = city.Ground(p.x, p.y);
                camera.transform.position = new Vector3(p.x, ground + 9, p.y); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                Shader.SetGlobalFloat("_CityGrassEnabled", 0); var before = Capture(camera, "top-" + i + "-original");
                Shader.SetGlobalFloat("_CityGrassEnabled", 1); Shader.SetGlobalFloat("_CityGrassNormalStrength", 0);
                var flat = Capture(camera, "top-" + i + "-color");
                Shader.SetGlobalFloat("_CityGrassNormalStrength", CityTerrainMaterial.NormalStrength);
                var relief = Capture(camera, "top-" + i + "-normal");
                mostColor = Math.Max(mostColor, Changed(before, flat)); mostNormal = Math.Max(mostNormal, Changed(flat, relief));
            }
            Test("grass-image-affects-render", mostColor > 500, "pixels changed=" + mostColor);
            Test("normal-map-affects-lighting", mostNormal > 100, "pixels changed=" + mostNormal);

            var spawn = city.Data.spawn; spawn.y = city.Ground(spawn.x, spawn.z) + .04f;
            city.Motor.Warp(spawn); city.Player.rotation = Quaternion.identity;
            camera.transform.position = spawn + new Vector3(-3.5f, 2.5f, -5.5f);
            camera.transform.LookAt(spawn + new Vector3(1, .7f, 4)); Capture(camera, "street-and-player");
            float parkHeight = city.Ground(182, 420);
            camera.transform.position = new Vector3(182, parkHeight + 2, 420);
            camera.transform.LookAt(new Vector3(180, parkHeight + .2f, 428)); Capture(camera, "park-at-player-height");
            float closeHeight = city.Ground(174, 416);
            camera.transform.position = new Vector3(174, closeHeight + 1.65f, 416);
            camera.transform.LookAt(new Vector3(179, closeHeight + .05f, 423));
            Shader.SetGlobalFloat("_CityGrassNormalStrength", 0); var closeFlat = Capture(camera, "grass-close-flat");
            Shader.SetGlobalFloat("_CityGrassNormalStrength", CityTerrainMaterial.NormalStrength); var closeRelief = Capture(camera, "grass-close-soft");
            int closeDifference = Changed(closeFlat, closeRelief);
            Test("soft-grass-lighting-close-to-player", closeDifference > 100, "pixels changed=" + closeDifference);
            CityTerrainMaterial.Bind(); Finish();
        }

        Color32[] Capture(Camera camera, string name)
        {
            var target = RenderTexture.GetTemporary(960, 600, 24, RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); Destroy(image);
            return pixels;
        }

        static int Changed(Color32[] a, Color32[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) >= 6) count++;
            return count;
        }

        void Finish()
        {
            report.passed = report.errors == 0 && report.checks.Count > 0 && report.checks.TrueForAll(c => c.passed);
            File.WriteAllText(Path.Combine(directory, "terrain-qa.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
