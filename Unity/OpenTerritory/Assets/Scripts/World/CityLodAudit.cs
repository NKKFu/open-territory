using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Called exclusively from the opt-in native reference probe.
    public static class CityLodAudit
    {
        public static IEnumerator Run(CityContext ctx, Action<string, bool, string> check, string directory)
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<CityLodManager>();
            check("world-lod-manager", manager && manager.objects.Length > 0, manager ? manager.objects.Length.ToString() : "missing");
            if (!manager) yield break;
            bool enabled = manager.enabled; manager.enabled = false;
            var objects = manager.objects;
            // This audit covers the imported art's LOD conversion. Authored
            // native NPCs use their own compound and population acceptance.
            var exportedResidents = JsonUtility.FromJson<WorldData>(UnityEngine.Object.FindFirstObjectByType<WorldRegistry>().manifest.text).residents;
            check("lod-exported-residents", exportedResidents.All(r => ctx.Node(r.id) && ctx.Node(r.id).GetComponent<CityObjectLod>()), exportedResidents.Length.ToString());
            check("lod-all-vehicles", ctx.Data.vehicles.All(r => ctx.Node(r.id) && ctx.Node(r.id).GetComponent<CityObjectLod>()), ctx.Data.vehicles.Length.ToString());
            check("lod-all-animals-and-boats", ctx.Data.animals.All(r => ctx.Node(r.id) && ctx.Node(r.id).GetComponent<CityObjectLod>()) && ctx.Data.boats.All(r => ctx.Node(r.id) && ctx.Node(r.id).GetComponent<CityObjectLod>()), "animals=" + ctx.Data.animals.Length + " boats=" + ctx.Data.boats.Length);
            check("lod-building-and-scenery-coverage", objects.Count(o => o.category == "building") >= ctx.Data.places.Length && objects.Any(o => o.category == "prop") && objects.Any(o => o.category == "scenery"), "buildings=" + objects.Count(o => o.category == "building"));
            var renderers = new HashSet<Renderer>(); bool unique = true, valid = true, monotonic = true;
            foreach (var o in objects) foreach (var p in o.parts)
            {
                unique &= renderers.Add(p.renderer);
                valid &= p.renderer && p.filter && p.full && p.medium && p.far;
                monotonic &= p.medium.triangles.Length <= p.full.triangles.Length && p.far.triangles.Length <= p.medium.triangles.Length;
            }
            check("lod-renderers-have-single-owner", unique, renderers.Count.ToString());
            check("lod-assets-valid-and-nonincreasing", valid && monotonic, "shared immutable meshes");
            bool restored = true, state = true, hierarchy = true;
            var cameraObject = new GameObject("Temporary LOD QA camera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = ctx.Camera.fieldOfView;
            try
            {
                foreach (var category in objects.GroupBy(o => o.category))
                {
                    long full = category.Sum(o => o.parts.Sum(p => (long)p.full.triangles.Length / 3));
                    long far = category.Sum(o => o.parts.Where(p => p.lastVisibleLevel >= 2).Sum(p => (long)p.far.triangles.Length / 3));
                    bool preserve=category.Key=="windows"||category.Key=="street-light";
                    check((preserve?"lod-preserves-minimal-geometry-":"lod-reduction-") + category.Key, far<=full&&(preserve||far<full), full + " -> " + far);
                    var o = category.OrderBy(item => item.localBounds.size.sqrMagnitude).ElementAt(category.Count() / 2); var parents = o.parts.Select(p => p.filter.transform.parent).ToArray();
                    var visibility = o.parts.Select(p => p.renderer.enabled).ToArray();
                    var materials = o.parts.Select(p => p.renderer.sharedMaterial).ToArray();
                    int colliders = o.GetComponentsInChildren<Collider>(true).Length;
                    for (int level = 0; level < 4; level++)
                    {
                        o.ApplyLevel(level);
                        for (int i = 0; i < o.parts.Length; i++)
                        {
                            var p = o.parts[i];
                            state &= p.renderer.enabled == visibility[i] && p.renderer.sharedMaterial == materials[i] && p.renderer.forceRenderingOff == (level > p.lastVisibleLevel);
                            hierarchy &= p.filter.transform.parent == parents[i];
                        }
                        hierarchy &= o.GetComponentsInChildren<Collider>(true).Length == colliders;
                    }
                    o.ApplyLevel(0);
                    restored &= o.parts.All(p => p.externalMesh || p.filter.sharedMesh == p.full);
                    Vector3 center = o.transform.TransformPoint(o.localBounds.center);
                    camera.transform.position = center; bool near = o.SelectLevel(camera) == 0;
                    camera.transform.position = center + Vector3.forward * (o.cullDistance + o.localBounds.size.magnitude + 100);
                    check("lod-camera-near-far-" + category.Key, near && o.SelectLevel(camera) == 3, "full detail nearby, culled beyond range");
                    if (category.Key == "npc" || category.Key == "vehicle")
                    {
                        var selected = new HashSet<int>();
                        for (float distance = 0; distance <= o.cullDistance + 50; distance += 2)
                        { camera.transform.position = center + Vector3.forward * distance; selected.Add(o.SelectLevel(camera)); }
                        check("lod-automatic-four-stages-" + category.Key, selected.Count == 4, string.Join(",", selected.OrderBy(l => l)));
                    }
                    if (category.Key == "building" || category.Key == "vehicle" || category.Key == "npc" || category.Key == "prop")
                        Capture(ctx.Camera, o, Path.Combine(directory, "lod-" + category.Key + ".png"));
                }
            }
            finally { UnityEngine.Object.Destroy(cameraObject); foreach (var o in objects) o.ApplyLevel(0); manager.enabled = enabled; }
            check("lod-retains-visibility-materials-hierarchy-colliders", state && hierarchy && restored, "all levels and return to LOD0");
            check("lod-player-excluded", !ctx.Player.GetComponent<CityObjectLod>() && !ctx.Player.GetComponentInParent<CityObjectLod>(), "player keeps full detail");
            yield return null;
        }

        static void Capture(Camera camera, CityObjectLod item, string path)
        {
            bool itemActive = item.gameObject.activeSelf;
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            var target = camera.targetTexture; var active = RenderTexture.active;
            RenderTexture rt = null; Texture2D tile = null, sheet = null;
            try
            {
                item.gameObject.SetActive(true);
                Vector3 center = item.transform.TransformPoint(item.localBounds.center);
                Vector3 size = Vector3.Scale(item.localBounds.size, item.transform.lossyScale);
                float distance = Mathf.Max(4, size.magnitude * 1.25f);
                camera.transform.position = center + new Vector3(.65f, .35f, -.76f).normalized * distance;
                camera.transform.LookAt(center);
                rt = new RenderTexture(640, 480, 24); camera.targetTexture = rt;
                sheet = new Texture2D(1920, 480, TextureFormat.RGB24, false);
                tile = new Texture2D(640, 480, TextureFormat.RGB24, false);
                for (int level = 0; level < 3; level++)
                {
                    item.ApplyLevel(level); camera.Render(); RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); tile.Apply();
                    sheet.SetPixels(level * 640, 0, 640, 480, tile.GetPixels());
                }
                sheet.Apply(); File.WriteAllBytes(path, sheet.EncodeToPNG());
            }
            finally
            {
                item.ApplyLevel(0); camera.transform.SetPositionAndRotation(position, rotation);
                item.gameObject.SetActive(itemActive);
                camera.targetTexture = target; RenderTexture.active = active;
                if (rt) UnityEngine.Object.Destroy(rt); if (tile) UnityEngine.Object.Destroy(tile); if (sheet) UnityEngine.Object.Destroy(sheet);
            }
        }
    }
}
