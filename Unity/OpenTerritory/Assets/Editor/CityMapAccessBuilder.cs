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
    // Move complete fixtures, including their collision and interactions.
    // Candidate search is read-only; every selected footprint is checked first.
    public static class CityMapAccessBuilder
    {
        public const string PlanPath = "Assets/Imported/overall-map-fixtures.json";
        [Serializable] public sealed class Move { public int id; public string name; public Vector3 from, to; }
        [Serializable] public sealed class Plan { public Move[] moves; }
        static void Ready()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/BaiaDoSol.unity" || scene.isDirty || Application.isPlaying || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("The saved city must be idle.");
        }
        public static void Preview()
        {
            Ready(); Physics.SyncTransforms();
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var routes = JsonUtility.FromJson<CityFrontageProbe.Layout>(Resources.Load<TextAsset>("building-frontages").text).entrances.Select(e => e.samples)
                .Concat(JsonUtility.FromJson<CityNeighborhoodProbe.Layout>(Resources.Load<TextAsset>("neighborhood-spaces").text).spaces.Select(s => s.samples)).SelectMany(x => x).ToArray();
            var roads = JsonUtility.FromJson<ReferenceLayout>(Resources.Load<TextAsset>("reference-map").text).roads;
            var targets = new[] { 2184, 2563, 2574, 3538, 3598, 3742 }.Select(id => new KeyValuePair<int, Transform>(id, registry.nodes.First(n => n.id == id).transform)).ToList();
            targets.Add(new KeyValuePair<int, Transform>(-1, GameObject.Find("Poste viário • town-center-cross • 240").transform));
            targets.Add(new KeyValuePair<int, Transform>(-2, GameObject.Find("Poste viário • town-west-cross • 223").transform));
            var moves = new List<Move>(); var reserved = new List<Tuple<Vector3, Quaternion, Vector2>>();
            foreach (var target in targets)
            {
                var tr = target.Value; bool bench = target.Key >= 0;
                var extent = bench ? new Vector2(1.6f, .65f) : new Vector2(.35f, .35f);
                var candidates = new List<Vector3>();
                foreach (float sideways in new[] { 2.8f, -2.8f, 3.6f, -3.6f, 4.6f, -4.6f, 5.8f, -5.8f, 7.2f, -7.2f, 9f, -9f })
                    foreach (float outward in new[] { 0f, .6f, -.6f, 1.2f, -1.2f })
                        candidates.Add(tr.position + tr.right * sideways + tr.forward * outward);
                bool found = false;
                foreach (var original in candidates.OrderBy(p => (p - tr.position).sqrMagnitude))
                {
                    var point = original; float lo = float.MaxValue, hi = float.MinValue; bool valid = true;
                    foreach (float x in new[] { -extent.x, 0, extent.x }) foreach (float z in new[] { -extent.y, 0, extent.y })
                    {
                        var p = point + tr.rotation * new Vector3(x, 0, z);
                        if (world.places.Any(b => b.Contains(p, -.12f))) { valid = false; break; }
                        foreach (var road in roads) for (int i = 1; i < road.points.Length; i++)
                        {
                            var a = CityPhysics.Flat(road.points[i - 1]); var b = CityPhysics.Flat(road.points[i]);
                            var v = b - a; float t = Mathf.Clamp01(Vector3.Dot(CityPhysics.Flat(p) - a, v) / Mathf.Max(.0001f, v.sqrMagnitude));
                            if ((CityPhysics.Flat(p) - (a + v * t)).magnitude < road.width * .5f + .1f) valid = false;
                        }
                        if (!Physics.Raycast(p + Vector3.up * 300, Vector3.down, out var hit, 500, 1 << CityContext.GroundLayer, QueryTriggerInteraction.Ignore)) { valid = false; break; }
                        lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y);
                    }
                    if (!valid || hi - lo > .22f || Mathf.Abs(hi - tr.position.y) > .65f) continue;
                    point.y = hi - .005f;
                    var inverse = Quaternion.Inverse(tr.rotation);
                    if (routes.Any(p => { var q = inverse * (p - point); return Mathf.Abs(q.x) < extent.x + .65f && Mathf.Abs(q.z) < extent.y + .65f; })) continue;
                    if (Physics.OverlapBox(point + Vector3.up * .9f, new Vector3(extent.x, .85f, extent.y), tr.rotation, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore)
                        .Any(c => !c.transform.IsChildOf(tr))) continue;
                    if (reserved.Any(r => (r.Item1 - point).sqrMagnitude < Mathf.Pow(r.Item3.magnitude + extent.magnitude + .4f, 2))) continue;
                    moves.Add(new Move { id = target.Key, name = tr.name, from = tr.position, to = point });
                    reserved.Add(Tuple.Create(point, tr.rotation, extent)); found = true; break;
                }
                if (!found) throw new InvalidOperationException("No clear supported placement for " + tr.name);
            }
            File.WriteAllText("../Reports/overall-map/access-moves.json", JsonUtility.ToJson(new Plan { moves = moves.ToArray() }, true));
        }
        public static Vector3 RoadLightPosition(Vector3 original)
        {
            if (!File.Exists(PlanPath)) return original;
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
            var move = plan.moves.FirstOrDefault(m => m.id < 0 && CityPhysics.Flat(m.from - original).sqrMagnitude < .0025f);
            return move == null ? original : move.to;
        }
        public static void Apply()
        {
            Ready(); var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, "../Reports/overall-map/backups/Before-access-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            ApplyStoredMoves();
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        public static void ApplyStoredMoves()
        {
            if (!File.Exists(PlanPath)) return;
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            foreach (var move in plan.moves)
            {
                var tr = move.id < 0 ? GameObject.Find(move.name).transform : registry.nodes.First(n => n.id == move.id).transform;
                if (Vector3.Distance(tr.position, move.from) > .05f && Vector3.Distance(tr.position, move.to) > .05f)
                    throw new InvalidOperationException("Fixture changed since review: " + move.name);
                tr.position = move.to; EditorUtility.SetDirty(tr);
            }
            Physics.SyncTransforms();
            File.WriteAllText("../Reports/overall-map/access-applied.txt", DateTime.UtcNow.ToString("o"));
        }
        public static void RestoreRemoteTreeColliders()
        {
            var shop = Object.FindFirstObjectByType<CityBikeShop>(); if (!shop) return;
            var registry = Object.FindFirstObjectByType<WorldRegistry>();
            var world = JsonUtility.FromJson<WorldData>(registry.manifest.text);
            var restored = new List<string>();
            shop.retiredColliders = shop.retiredColliders.Where(old => {
                if (!(old.target is BoxCollider box) || !box.name.StartsWith("Colisão-") || !int.TryParse(box.name.Substring(8), out int id)) return true;
                var position = box.transform.TransformPoint(box.center);
                var natural = world.natureTrees.FirstOrDefault(t => t.solidIndex == id && CityPhysics.Flat(t.position - position).sqrMagnitude < .0025f);
                if (natural == null) return true;
                // A retired panel elsewhere must never suppress this trunk.
                box.enabled = old.enabled; EditorUtility.SetDirty(box);
                restored.Add(box.name + " " + position); return false;
            }).ToArray();
            if (restored.Count > 0)
            {
                EditorUtility.SetDirty(shop); Physics.SyncTransforms();
                File.WriteAllText("../Reports/overall-map/restored-tree-collisions.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    verifiedAtUtc = DateTime.UtcNow.ToString("o"), restored = restored.ToArray()
                }, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}
